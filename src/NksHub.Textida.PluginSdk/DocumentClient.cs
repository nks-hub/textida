using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginSdk;

/// <summary>A change of the bytes <c>[Start, End)</c> of a document into <see cref="Text"/>, written in the encoding of the document by the editor.</summary>
public readonly record struct RangeEdit(long Start, long End, string Text);

/// <summary>What <c>document/info</c> says about the revision a client is bound to. <see cref="Path"/> is null for a document that has no file.</summary>
public sealed record DocumentFacts(long ByteLength, string Codec, int BomLength, string? Path);

/// <summary>A selection as byte offsets; <see cref="Anchor"/> may be after <see cref="Caret"/>.</summary>
public readonly record struct ByteSelection(long Anchor, long Caret)
{
    public long Start => Math.Min(Anchor, Caret);
    public long End => Math.Max(Anchor, Caret);
    public bool IsEmpty => Anchor == Caret;
}

/// <summary>Text of a range. <see cref="HasInvalid"/> is true when the range holds bytes that are not valid in the encoding of the document; their text is then not what the file holds.</summary>
public sealed record TextRead(string Text, bool HasInvalid);

/// <summary>
/// The document API for one document at the revision a command started on. Every call that depends on the revision checks it and ends with a
/// <see cref="DocumentErrorCodes.RevisionConflict"/> <see cref="RpcException"/> when the document has moved on, so a plugin never reads or changes text it has not seen.
/// </summary>
public sealed class DocumentClient(JsonRpcDispatcher rpc, string document, long revision)
{
    public string Document { get; } = document;
    public long Revision { get; } = revision;

    /// <summary>The client of the document and revision a <c>command/execute</c> request named.</summary>
    public static DocumentClient For(JsonRpcDispatcher rpc, CommandContext command) =>
        command is { DocumentId: { } id, Revision: { } revision } ? new DocumentClient(rpc, id, revision) : throw new RpcException(RpcErrorCodes.InvalidParams, "The command names no document and revision.");

    private RpcException Stale() => new(DocumentErrorCodes.RevisionConflict, "Stale.");

    public async Task<DocumentFacts> InfoAsync(CancellationToken cancellation = default)
    {
        var info = await rpc.RequestAsync(DocumentMethods.Info, new DocumentRef(Document), SdkJson.Default.DocumentRef, SdkJson.Default.DocumentInfoResult, cancellation).ConfigureAwait(false);
        if (info.Revision != Revision) throw Stale();
        return new DocumentFacts(info.ByteLength, info.Codec, info.BomLength, info.Path);
    }

    /// <summary>The codec of the document by its name, UTF-8 when the editor names one this build does not know.</summary>
    public static TextCodec CodecOf(DocumentFacts facts) => TextCodec.ByName(facts.Codec) ?? TextCodec.Utf8;

    /// <summary>The selections of the document, empty ones (a bare caret) included, in the order the editor keeps them.</summary>
    public async Task<IReadOnlyList<ByteSelection>> SelectionsAsync(CancellationToken cancellation = default)
    {
        var result = await rpc.RequestAsync(DocumentMethods.Selections, new DocumentRef(Document), SdkJson.Default.DocumentRef, SdkJson.Default.SelectionsResult, cancellation).ConfigureAwait(false);
        if (result.Revision != Revision) throw Stale();
        return [.. result.Selections.Select(s => new ByteSelection(s.Anchor, s.Caret))];
    }

    public async Task<TextRead> ReadTextAsync(long start, long end, CancellationToken cancellation = default)
    {
        var read = await rpc.RequestAsync(DocumentMethods.ReadText, new ReadTextParams(Document, Revision, start, end), SdkJson.Default.ReadTextParams, SdkJson.Default.ReadTextResult, cancellation).ConfigureAwait(false);
        return new TextRead(read.Text, read.Invalid.Count > 0);
    }

    /// <summary>The line ending most lines of the document use (<c>"\n"</c>, <c>"\r\n"</c> or <c>"\r"</c>), or null when it has none.</summary>
    public async Task<string?> DominantLineEndingAsync(CancellationToken cancellation = default)
    {
        var facts = await rpc.RequestAsync(DocumentMethods.Facts, new RevisionParams(Document, Revision), SdkJson.Default.RevisionParams, SdkJson.Default.FactsResult, cancellation).ConfigureAwait(false);
        return facts.LineEndings.Dominant switch { "lf" => "\n", "crlf" => "\r\n", "cr" => "\r", _ => null };
    }

    /// <summary>The 0-based line and column (in UTF-16 units) of a byte offset, or null where the line index of the editor does not reach yet.</summary>
    public async Task<(long Line, long Column)?> PositionAsync(long offset, CancellationToken cancellation = default)
    {
        try
        {
            var position = await rpc.RequestAsync(DocumentMethods.OffsetToPosition, new PositionParams(Document, Revision, offset), SdkJson.Default.PositionParams, SdkJson.Default.PositionResult, cancellation).ConfigureAwait(false);
            return (position.Line, position.Column);
        }
        catch (RpcException e) when (e.Code == DocumentErrorCodes.PositionUnknown) { return null; }
    }

    /// <summary>Applies all edits as one batch, so they are one undo step; none is applied unless all are. A batch the editor already took (after a restart) is not applied twice.</summary>
    public Task ApplyAsync(IReadOnlyList<RangeEdit> edits, string? batchId = null, CancellationToken cancellation = default) =>
        rpc.RequestAsync(DocumentMethods.EditApply, new ApplyParams(Document, Revision, batchId ?? Guid.NewGuid().ToString("N"), [.. edits]), SdkJson.Default.ApplyParams, cancellation);

    /// <summary>Offers the edits as a proposal the user reviews in the change preview; the document stays as it is until then.</summary>
    public Task ProposeAsync(string title, IReadOnlyList<RangeEdit> edits, CancellationToken cancellation = default) =>
        rpc.RequestAsync(DocumentMethods.EditPropose, new ProposeParams(Document, Revision, title, [.. edits]), SdkJson.Default.ProposeParams, cancellation);

    /// <summary>Shows a notice in the window of the editor.</summary>
    public static Task NotifyAsync(JsonRpcDispatcher rpc, NoticeLevel level, string message, CancellationToken cancellation = default) =>
        rpc.RequestAsync(UiMethods.Notify, new NoticeParams(level.ToString().ToLowerInvariant(), message), SdkJson.Default.NoticeParams, cancellation);
}
