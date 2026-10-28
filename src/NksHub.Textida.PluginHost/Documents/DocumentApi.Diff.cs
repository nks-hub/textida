using System.Text.Json;
using NksHub.Textida.Core.Compare;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.PluginHost.Documents;

public sealed partial class DocumentApi
{
    /// <summary>
    /// One side of a diff: <c>{"document":id,"revision":n}</c> (the revision must be the current one) or <c>{"snapshot":id}</c>. The plugin must be allowed to read
    /// the document, and a secrets file whose values are hidden from the plugin is refused: a line diff would tell which values differ, and a staged hunk would copy them.
    /// </summary>
    private async Task<Target> ResolveSideAsync(string pluginId, JsonElement p, string name)
    {
        var side = Need(p, name);
        if (side.ValueKind != JsonValueKind.Object) throw Invalid($"Parameter '{name}' must be an object with a document and revision, or a snapshot.");
        var (scope, document) = ScopeOf(pluginId, side);
        if (document is not null && !_options.Permissions.IsGranted(pluginId, DocumentPermissions.Read, scope))
            throw PluginPermissions.Denied(DocumentPermissions.Read, "NotGranted", $"The plugin does not hold the '{DocumentPermissions.Read}' permission for the document in '{name}'.");
        var target = await ResolveAsync(pluginId, side);
        if (target.Secrets is { Count: > 0 })
            throw PluginPermissions.Denied(DocumentPermissions.SecretsReveal, "Secret", $"The document in '{name}' has values the plugin has not been allowed to see; a diff would show them.");
        return target;
    }

    private static DiffSource SourceOf(Target t) => new(t.Snap, t.Snap.Codec, t.Bom);

    private static TextDiff Compare(Target left, Target right, CancellationToken ct) =>
        Guarded(left.Snap, () => Guarded(right.Snap, () => LineDiff.Compute(SourceOf(left), SourceOf(right), null, ct)));

    private static void WriteSide(Utf8JsonWriter w, string name, Target t)
    {
        w.WriteStartObject(name);
        w.WriteString("document", t.Snap.DocumentId);
        w.WriteNumber("revision", t.Snap.Revision);
        w.WriteEndObject();
    }

    private static void WriteHunkSide(Utf8JsonWriter w, string name, LineIndex index, long firstLine, long lines)
    {
        w.WriteStartObject(name);
        w.WriteNumber("startLine", firstLine);
        w.WriteNumber("lines", lines);
        w.WriteNumber("start", index.OffsetOf(firstLine));
        w.WriteNumber("end", index.OffsetOf(firstLine + lines));
        w.WriteEndObject();
    }

    /// <summary>
    /// The line diff of two revisions. Hunks are numbered from 0 in file order and are only meaningful for the two revisions named in the answer: the same pair always gives the
    /// same hunks. At most <see cref="DocumentApiOptions.MaxFindMatches"/> hunks are listed; <c>from</c> continues after a truncated answer.
    /// </summary>
    private async Task<byte[]> DiffComputeAsync(string pluginId, JsonElement p, CancellationToken ct)
    {
        var left = await ResolveSideAsync(pluginId, p, "left");
        var right = await ResolveSideAsync(pluginId, p, "right");
        long from = p.TryGetProperty("from", out _) ? NeedLong(p, "from") : 0;
        if (from < 0) throw Invalid("Parameter 'from' must not be negative.");
        var diff = await Task.Run(() => Compare(left, right, ct), ct);
        return Obj(w =>
        {
            WriteSide(w, "left", left);
            WriteSide(w, "right", right);
            w.WriteBoolean("identical", diff.IsIdentical);
            w.WriteBoolean("exact", diff.Exact);
            w.WriteNumber("leftLines", diff.A.LineCount);
            w.WriteNumber("rightLines", diff.B.LineCount);
            w.WriteNumber("hunkCount", diff.Hunks.Count);
            w.WriteStartArray("hunks");
            int listed = 0;
            for (long i = from; i < diff.Hunks.Count && listed < _options.MaxFindMatches; i++, listed++)
            {
                var h = diff.Hunks[(int)i];
                w.WriteStartObject();
                w.WriteNumber("index", i);
                WriteHunkSide(w, "left", diff.A, h.AStart, h.ACount);
                WriteHunkSide(w, "right", diff.B, h.BStart, h.BCount);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteBoolean("truncated", from + listed < diff.Hunks.Count);
        });
    }

    /// <summary>
    /// The optional <c>key</c> and <c>actions</c> (<c>[{"command","title","refresh"}]</c>) of <see cref="DiffOpenAsync"/>. An editor that takes none (<see cref="DocumentApiOptions.DiffActions"/> is null) leaves them out.
    /// </summary>
    private (string? Key, IReadOnlyList<DiffAction>? Actions) DiffActionsOf(JsonElement p)
    {
        if (_options.DiffActions is not { } limits) return (null, null);
        string Text(JsonElement e, string name, bool required)
        {
            if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return required ? throw Invalid($"'{name}' is required.") : "";
            var text = v.ValueKind == JsonValueKind.String ? v.GetString()! : throw Invalid($"'{name}' must be a string.");
            return text.Length is > 0 && text.Length <= limits.MaxTextLength ? text : throw Invalid($"'{name}' must have 1 to {limits.MaxTextLength} characters.");
        }
        var key = Text(p, "key", required: false);
        var list = new List<DiffAction>();
        if (p.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
        {
            if (actions.GetArrayLength() > limits.MaxActions) throw Invalid($"A comparison takes at most {limits.MaxActions} actions.");
            foreach (var a in actions.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object) throw Invalid("Every action must be an object.");
                var refresh = Text(a, "refresh", required: false);
                list.Add(new DiffAction(Text(a, "command", required: true), Text(a, "title", required: true), refresh.Length == 0 ? null : refresh));
            }
        }
        return (key.Length == 0 ? null : key, list.Count == 0 ? null : list);
    }

    /// <summary>Shows the diff of two revisions in a comparison tab. Both revisions stay as they were asked for, whatever the documents do afterwards.</summary>
    private async Task<byte[]> DiffOpenAsync(string pluginId, JsonElement p, CancellationToken ct)
    {
        var left = await ResolveSideAsync(pluginId, p, "left");
        var right = await ResolveSideAsync(pluginId, p, "right");
        string? Title(string name) => p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var (key, actions) = DiffActionsOf(p);
        var request = new DiffRequest(Title("title"), new DiffInput(left.Snap, left.Bom, Title("leftTitle")), new DiffInput(right.Snap, right.Bom, Title("rightTitle")), pluginId, key, actions);
        bool opened = await _owner.InvokeAsync(() => _options.Tools.OpenDiff(request));
        return Obj(w => w.WriteBoolean("opened", opened));
    }

    /// <summary>
    /// Stages one hunk: makes the lines of hunk <c>hunk</c> of the diff of <c>base</c> against <c>source</c> equal to the source, in <c>base</c>, which must be a virtual document the
    /// plugin provides (the index of a repository). <c>base</c> names the revision the plugin saw; if the document moved on since, nothing is staged and the plugin gets a
    /// <see cref="DocumentErrorCodes.RevisionConflict"/> with the current revision. Hunk numbers of the other hunks change after a stage: compute the diff again.
    /// </summary>
    private async Task<byte[]> DiffStageAsync(string pluginId, JsonElement p, CancellationToken ct)
    {
        var baseSide = Need(p, "base");
        if (baseSide.ValueKind != JsonValueKind.Object || !baseSide.TryGetProperty("document", out _)) throw Invalid("Parameter 'base' must name a document and its revision.");
        var hunk = NeedLong(p, "hunk");
        FindProvided(pluginId, NeedDocumentId(baseSide));
        var target = await ResolveSideAsync(pluginId, p, "base");
        var source = await ResolveSideAsync(pluginId, p, "source");
        if (target.Snap.DocumentId == source.Snap.DocumentId) throw Invalid("A document cannot be staged into itself.");
        var diff = await Task.Run(() => Compare(target, source, ct), ct);
        if (hunk < 0 || hunk >= diff.Hunks.Count)
            throw Fail(DocumentErrorCodes.InvalidRange, $"There is no hunk {hunk}; the diff has {diff.Hunks.Count}.", w => w.WriteNumber("hunkCount", diff.Hunks.Count));
        var edit = Guarded(target.Snap, () => Guarded(source.Snap, () => DiffTransfer.Plan(diff, (int)hunk, DiffSide.A)));
        if (edit.Lossy) throw Invalid("The encoding of the base document cannot hold the characters of the hunk.");
        var e = Find(target.Snap.DocumentId);
        return await _owner.InvokeAsync(() =>
        {
            var d = e.Doc;
            CheckCurrent(d, target.Snap.Revision);
            var result = d.ApplyVirtual([new ByteEdit(edit.Range, edit.Bytes)], target.Snap.Revision, "stage:" + pluginId);
            if (!result.Applied) throw EditFailure(result.Conflict, result.Revision);
            return Obj(w =>
            {
                w.WriteString("document", d.Id);
                w.WriteNumber("revision", d.Revision);
                w.WriteNumber("hunk", hunk);
                w.WriteNumber("start", edit.Range.Start);
                w.WriteNumber("end", edit.Range.Start + edit.Bytes.Length);
            });
        });
    }
}
