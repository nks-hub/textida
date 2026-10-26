using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Documents;

public sealed partial class DocumentApi
{
    private sealed record AppliedBatch(string Id, string Fingerprint, long Revision);

    private readonly Dictionary<string, List<AppliedBatch>> _batches = [];

    /// <summary>
    /// Parses <c>edits</c>: ranges of the revision the request names, each with <c>text</c> (written in the encoding of the document)
    /// or <c>bytes</c> (base64, written as they are); none of the two deletes.
    /// </summary>
    private static List<ByteEdit> ParseEdits(JsonElement array, TextCodec codec)
    {
        if (array.ValueKind != JsonValueKind.Array) throw Invalid("Parameter 'edits' must be an array.");
        List<ByteEdit> edits = [];
        foreach (var item in array.EnumerateArray())
        {
            long start = NeedLong(item, "start"), end = NeedLong(item, "end");
            if (start < 0 || end < start) throw Invalid($"The range [{start}, {end}) is not valid.");
            bool hasText = item.TryGetProperty("text", out var text), hasBytes = item.TryGetProperty("bytes", out var bytes);
            if (hasText && hasBytes) throw Invalid("An edit has either 'text' or 'bytes', not both.");
            byte[] insert = [];
            try
            {
                if (hasText) insert = text.ValueKind == JsonValueKind.String ? codec.Encode(text.GetString()!) : throw Invalid("'text' must be a string.");
                else if (hasBytes) insert = bytes.ValueKind == JsonValueKind.String ? Convert.FromBase64String(bytes.GetString()!) : throw Invalid("'bytes' must be a base64 string.");
            }
            catch (UnrepresentableCharactersException) { throw Invalid("The text cannot be written in the encoding of the document."); }
            catch (FormatException) { throw Invalid("'bytes' is not valid base64."); }
            edits.Add(new ByteEdit(new ByteRange(start, end), insert));
        }
        return edits;
    }

    /// <summary>
    /// Applies a batch as one transaction: one revision and one undo step, or nothing. It is applied only against the stated revision;
    /// any other revision is a conflict and the plugin reads again. <c>batchId</c> makes a repeat harmless: a batch whose answer
    /// was lost (the plugin or the connection died after the editor applied it) is answered with the stored revision and not applied twice.
    /// </summary>
    private async Task<byte[]> ApplyAsync(string pluginId, JsonElement p)
    {
        if (!_options.HasPermission(pluginId, DocumentPermissions.EditDirect))
            throw Fail(DocumentErrorCodes.PermissionDenied, $"The plugin does not hold the '{DocumentPermissions.EditDirect}' permission.");
        var e = Find(NeedDocumentId(p));
        long revision = NeedLong(p, "revision");
        var batchId = NeedString(p, "batchId");
        if (batchId.Length == 0) throw Invalid("Parameter 'batchId' must not be empty.");
        var editsJson = Need(p, "edits");
        var fingerprint = $"{e.Doc.Id:N}|{revision}|{editsJson.GetRawText()}";

        return await _owner.InvokeAsync(() =>
        {
            lock (_gate)
            {
                if (_batches.TryGetValue(pluginId, out var log) && log.Find(b => b.Id == batchId) is { } previous)
                {
                    if (previous.Fingerprint != fingerprint) throw Invalid($"The batch id '{batchId}' was already used for different edits.");
                    return ApplyResult(previous.Revision, duplicate: true);
                }
            }
            var d = e.Doc;
            CheckCurrent(d, revision);
            var result = d.Apply(ParseEdits(editsJson, d.Codec), new EditOptions { BaseRevision = revision, Source = "plugin:" + pluginId });
            if (!result.Applied) throw EditFailure(result.Conflict, result.Revision);
            lock (_gate)
            {
                if (!_batches.TryGetValue(pluginId, out var log)) _batches[pluginId] = log = [];
                log.Add(new AppliedBatch(batchId, fingerprint, result.Revision));
                if (log.Count > _options.MaxRememberedBatches) log.RemoveAt(0);
            }
            return ApplyResult(result.Revision, duplicate: false);
        });
    }

    private static byte[] ApplyResult(long revision, bool duplicate) => Obj(w =>
    {
        w.WriteBoolean("applied", true);
        w.WriteNumber("revision", revision);
        w.WriteBoolean("duplicate", duplicate);
    });

    private static RpcException EditFailure(ConflictReason reason, long revision) => reason switch
    {
        ConflictReason.InvalidRange => Fail(DocumentErrorCodes.InvalidRange, "An edit range is outside the document, inside the BOM or not on character boundaries."),
        ConflictReason.EditsOverlap => Fail(DocumentErrorCodes.EditsOverlap, "Two edits of the batch overlap; nothing was applied."),
        _ => Conflict(reason.ToString(), revision),
    };

    /// <summary>Hands a batch against the current revision to the change preview service; the document does not change.</summary>
    private async Task<byte[]> ProposeAsync(string pluginId, JsonElement p, CancellationToken ct)
    {
        var e = Find(NeedDocumentId(p));
        long revision = NeedLong(p, "revision");
        var editsJson = Need(p, "edits");
        var title = p.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        var proposal = await _owner.InvokeAsync(() =>
        {
            var d = e.Doc;
            CheckCurrent(d, revision);
            var edits = ParseEdits(editsJson, d.Codec);
            Validate(d, edits);
            return new EditProposal(pluginId, d.Id, revision, edits, title);
        });
        var id = await _options.Proposals.SubmitAsync(proposal, ct);
        return Obj(w =>
        {
            w.WriteString("proposal", id);
            w.WriteNumber("revision", revision);
        });
    }

    private static void Validate(Document d, List<ByteEdit> edits)
    {
        foreach (var edit in edits)
            if (edit.Range.End > d.ByteLength || !d.IsCharBoundary(edit.Range.Start) || !d.IsCharBoundary(edit.Range.End))
                throw EditFailure(ConflictReason.InvalidRange, d.Revision);
        var sorted = edits.OrderBy(x => x.Range.Start).ThenBy(x => x.Range.IsEmpty ? 0 : 1).ToList();
        for (int i = 1; i < sorted.Count; i++)
        {
            var prev = sorted[i - 1].Range;
            var cur = sorted[i].Range;
            if (prev.End > cur.Start || (prev.IsEmpty && cur.IsEmpty && prev.Start == cur.Start)) throw EditFailure(ConflictReason.EditsOverlap, d.Revision);
        }
    }
}
