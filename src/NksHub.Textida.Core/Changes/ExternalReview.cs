using NksHub.Textida.Core.Compare;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Changes;

/// <summary>
/// The review of a change another program made to the file of an open document (P6.1.1). The document holds the new content; the base is the content the
/// user saw before. The hunks are the line differences, in bytes of the document, with the base text as their replacement: applying a hunk reverts it,
/// leaving it out keeps the change. The same <see cref="ChangeApplier"/> that applies a Replace in Files applies the reverts, as one undo step.
/// </summary>
public static class ExternalReview
{
    /// <summary>The owner that pins the base revision in the local history, so retention never deletes what a pending review needs.</summary>
    public const string PinOwner = "review";

    /// <summary>
    /// The hunks that turn the document back into <paramref name="baseBytes"/>, or null when the content is the same or no set of hunks reproduces the base exactly.
    /// Where the line diff cannot express the difference (a changed line ending of the last line, for instance) the whole differing middle becomes one hunk.
    /// </summary>
    public static FileChange? BuildRevert(Document doc, byte[] baseBytes, CancellationToken ct = default)
    {
        var codec = doc.Codec;
        var current = ChangeBuilder.ReadAll(doc.GetSnapshot());
        if (current.AsSpan().SequenceEqual(baseBytes)) return null;

        var a = DiffSource.Of(doc);
        var b = new DiffSource(new ArraySnapshot(baseBytes), codec, codec.BomLengthIn(baseBytes));
        var diff = LineDiff.Compute(a, b, ct: ct);
        var edits = new List<(long Start, long End, byte[] Bytes)>();
        foreach (var h in diff.Hunks)
            edits.Add((diff.A.OffsetOf(h.AStart), diff.A.OffsetOf(h.AStart + h.ACount), baseBytes[(int)diff.B.OffsetOf(h.BStart)..(int)diff.B.OffsetOf(h.BStart + h.BCount)]));

        if (!Reproduces(current, baseBytes, edits)) edits = Whole(doc, current, baseBytes);
        // the apply step writes the hunk text in the encoding of the document, so the bytes must survive being read as text
        if (!Reproduces(current, baseBytes, edits) || edits.Any(e => !RoundTrips(codec, e.Bytes))) return null;
        try { return ChangeBuilder.ForDocument(doc, edits.Select(e => new TextReplacement(new ByteRange(e.Start, e.End), codec.GetString(e.Bytes)))); }
        catch (ArgumentException) { return null; }
    }

    /// <summary>The same hunks seen the other way round: what the other program changed (base text removed, new text added), for showing.</summary>
    public static FileChange AsChange(FileChange revert) =>
        revert with { Hunks = [.. revert.Hunks.Select(h => h with { OldText = h.NewText, NewText = h.OldText })] };

    private static bool RoundTrips(Encodings.TextCodec codec, byte[] bytes)
    {
        try { return codec.Encode(codec.GetString(bytes)).AsSpan().SequenceEqual(bytes); }
        catch (Exception e) when (e is ArgumentException or Encodings.UnrepresentableCharactersException) { return false; }
    }

    private static bool Reproduces(byte[] current, byte[] baseBytes, List<(long Start, long End, byte[] Bytes)> edits)
    {
        using var ms = new MemoryStream(baseBytes.Length);
        long at = 0;
        foreach (var (start, end, bytes) in edits)
        {
            if (start < at || end < start || end > current.Length) return false;
            ms.Write(current, (int)at, (int)(start - at));
            ms.Write(bytes);
            at = end;
        }
        ms.Write(current, (int)at, current.Length - (int)at);
        return ms.GetBuffer().AsSpan(0, (int)ms.Length).SequenceEqual(baseBytes);
    }

    private static List<(long Start, long End, byte[] Bytes)> Whole(Document doc, byte[] current, byte[] baseBytes)
    {
        int bom = doc.BomLength;
        int max = Math.Min(current.Length, baseBytes.Length);
        int prefix = bom;
        while (prefix < max && current[prefix] == baseBytes[prefix]) prefix++;
        while (prefix > bom && !doc.IsCharBoundary(prefix)) prefix--;
        int suffix = 0;
        while (suffix < max - prefix && current[^(suffix + 1)] == baseBytes[^(suffix + 1)]) suffix++;
        while (suffix > 0 && !doc.IsCharBoundary(current.Length - suffix)) suffix--;
        return [(prefix, current.Length - suffix, baseBytes[prefix..(baseBytes.Length - suffix)])];
    }
}
