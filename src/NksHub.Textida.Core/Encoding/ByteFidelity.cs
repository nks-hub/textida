using System.Text;

namespace NksHub.Textida.Core.Encodings;

/// <summary>A replacement of original characters [Start, Start+Length) with <see cref="Text"/>, in coordinates of <see cref="DecodedText.Text"/>.</summary>
public readonly record struct TextEdit(int Start, int Length, string Text)
{
    public int End => Start + Length;

    public static string Apply(string source, IReadOnlyList<TextEdit> edits)
    {
        var sb = new StringBuilder(source.Length);
        int pos = 0;
        foreach (var e in edits) { sb.Append(source, pos, e.Start - pos).Append(e.Text); pos = e.End; }
        return sb.Append(source, pos, source.Length - pos).ToString();
    }

    /// <summary>The single smallest edit that turns <paramref name="oldText"/> into <paramref name="newText"/> (common prefix and suffix are kept).</summary>
    public static TextEdit FromDiff(string oldText, string newText)
    {
        int p = 0, max = Math.Min(oldText.Length, newText.Length);
        while (p < max && oldText[p] == newText[p]) p++;
        if (p > 0 && p < max && char.IsHighSurrogate(oldText[p - 1])) p--;
        int s = 0;
        while (s < max - p && oldText[^(s + 1)] == newText[^(s + 1)]) s++;
        if (s > 0 && char.IsLowSurrogate(oldText[^s])) s--;
        return new TextEdit(p, oldText.Length - p - s, newText.Substring(p, newText.Length - p - s));
    }
}

/// <summary>One piece of the output: a verbatim range of the source bytes, or freshly encoded bytes.</summary>
public readonly record struct OutputSegment(long SourceStart, long SourceLength, byte[]? Data)
{
    public bool IsVerbatim => Data is null;
    public long Length => Data?.Length ?? SourceLength;
}

/// <param name="FirstDifference">Offset of the first changed byte (the common length when only the length changed).</param>
/// <param name="OldChanged">Bytes of the old content inside the changed range.</param>
/// <param name="NewChanged">Bytes of the new content inside the changed range.</param>
public readonly record struct ByteDiff(long FirstDifference, long OldChanged, long NewChanged)
{
    public bool IsEmpty => OldChanged == 0 && NewChanged == 0;

    public static ByteDiff Compute(ReadOnlySpan<byte> oldBytes, ReadOnlySpan<byte> newBytes)
    {
        int max = Math.Min(oldBytes.Length, newBytes.Length);
        int p = oldBytes[..max].CommonPrefixLength(newBytes[..max]);
        int s = 0;
        while (s < max - p && oldBytes[^(s + 1)] == newBytes[^(s + 1)]) s++;
        return new ByteDiff(p, oldBytes.Length - p - s, newBytes.Length - p - s);
    }
}

/// <summary>
/// The result of planning a save or conversion. Nothing is written until <see cref="ToBytes"/> or <see cref="WriteTo"/>,
/// and neither writes anything while characters would be lost and no replacement was chosen.
/// </summary>
public sealed class SavePlan
{
    private readonly ReadOnlyMemory<byte> _source;

    internal SavePlan(ReadOnlyMemory<byte> source, IReadOnlyList<OutputSegment> segments, LossReport loss, bool replacementChosen)
    {
        _source = source; Segments = segments; Loss = loss; ReplacementChosen = replacementChosen;
    }

    public IReadOnlyList<OutputSegment> Segments { get; }
    public LossReport Loss { get; }
    public bool ReplacementChosen { get; }
    /// <summary>True when characters would be lost and the caller has not chosen a replacement.</summary>
    public bool IsBlocked => Loss.HasLoss && !ReplacementChosen;
    public long Length => Segments.Sum(s => s.Length);

    /// <summary>Output assembled from the segments; with an unchosen replacement the lost characters show as '?' and the plan is blocked.</summary>
    internal byte[] Assemble()
    {
        var ms = new MemoryStream(checked((int)Length));
        WriteSegments(ms);
        return ms.ToArray();
    }

    public byte[] ToBytes()
    {
        if (IsBlocked) throw new UnrepresentableCharactersException(Loss);
        return Assemble();
    }

    public void WriteTo(Stream output)
    {
        if (IsBlocked) throw new UnrepresentableCharactersException(Loss);
        WriteSegments(output);
    }

    private void WriteSegments(Stream output)
    {
        foreach (var s in Segments)
        {
            if (s.Data is { } d) output.Write(d);
            else output.Write(_source.Span.Slice((int)s.SourceStart, (int)s.SourceLength));
        }
    }

    /// <summary>Which bytes differ from the source; available even while blocked (preview).</summary>
    public ByteDiff DiffFromSource() => ByteDiff.Compute(_source.Span, Assemble());
}

/// <summary>Byte-faithful save and convert planning over <see cref="DecodedText"/>.</summary>
public static class ByteFidelity
{
    /// <summary>
    /// Plans a save: bytes outside <paramref name="edits"/> are copied verbatim (BOM and invalid sequences included),
    /// only edit texts are encoded in the document's own codec. Edits must be sorted and must not overlap.
    /// </summary>
    /// <param name="replacement">Explicit text written for each unrepresentable character; null means such characters block the save.</param>
    public static SavePlan PlanSave(DecodedText doc, IReadOnlyList<TextEdit> edits, string? replacement = null)
    {
        Validate(doc, edits);
        var rep = ReplacementBytes(doc.Codec, replacement);
        var loss = new LossReport.Collector();
        var segments = new List<OutputSegment>();
        long srcPos = 0, outLen = 0;
        int delta = 0;
        foreach (var e in edits)
        {
            long eStart = doc.ByteOffsetOf(e.Start);
            AddVerbatim(segments, srcPos, eStart - srcPos, ref outLen);
            var data = Encode(doc.Codec, e.Text, rep, loss, outLen, e.Start + delta, null);
            segments.Add(new OutputSegment(0, 0, data));
            outLen += data.Length;
            srcPos = doc.ByteOffsetOf(e.End);
            delta += e.Text.Length - e.Length;
        }
        AddVerbatim(segments, srcPos, doc.Bytes.Length - srcPos, ref outLen);
        var finalText = edits.Count == 0 ? doc.Text : TextEdit.Apply(doc.Text, edits);
        return new SavePlan(doc.Bytes, segments, loss.Build(finalText), replacement is not null);
    }

    public static byte[] Save(DecodedText doc, IReadOnlyList<TextEdit> edits, string? replacement = null) =>
        PlanSave(doc, edits, replacement).ToBytes();

    /// <summary>
    /// Plans "Convert to...": the whole document is rewritten in <paramref name="target"/>. Undecodable source bytes
    /// cannot be carried across and count as loss, except when only the BOM changes.
    /// </summary>
    public static SavePlan PlanConvert(DecodedText doc, TextCodec target, string? replacement = null)
    {
        var bom = new OutputSegment(0, 0, target.Bom);
        if (target.SameBytesAs(doc.Codec))
            return new SavePlan(doc.Bytes, [bom, new OutputSegment(doc.BomLength, doc.Bytes.Length - doc.BomLength, null)], LossReport.None, true);

        var rep = ReplacementBytes(target, replacement);
        var loss = new LossReport.Collector();
        var data = Encode(target, doc.Text, rep, loss, target.Bom.Length, 0, doc.IsInvalid);
        return new SavePlan(doc.Bytes, [bom, new OutputSegment(0, 0, data)], loss.Build(doc.Text), replacement is not null);
    }

    /// <summary>Every codec with the number of characters it would lose, so the picker can mark lossy targets before selection.</summary>
    public static IReadOnlyList<(TextCodec Codec, int Lost)> ListTargets(DecodedText doc) =>
        TextCodec.All.Select(c => (c, PlanConvert(doc, c, "?").Loss.Total)).ToList();

    public static byte[] Convert(DecodedText doc, TextCodec target, string? replacement = null) =>
        PlanConvert(doc, target, replacement).ToBytes();

    private static void Validate(DecodedText doc, IReadOnlyList<TextEdit> edits)
    {
        int prevEnd = 0;
        foreach (var e in edits)
        {
            if (e.Start < prevEnd || e.Length < 0 || e.End > doc.Text.Length)
                throw new ArgumentException("Edits must be sorted, non-overlapping and inside the document.", nameof(edits));
            if (!doc.IsCharBoundary(e.Start) || !doc.IsCharBoundary(e.End))
                throw new ArgumentException("An edit boundary lies inside a surrogate pair.", nameof(edits));
            prevEnd = e.End;
        }
    }

    private static void AddVerbatim(List<OutputSegment> segments, long start, long length, ref long outLen)
    {
        if (length <= 0) return;
        if (segments.Count > 0 && segments[^1] is { IsVerbatim: true } last && last.SourceStart + last.SourceLength == start)
            segments[^1] = last with { SourceLength = last.SourceLength + length };
        else
            segments.Add(new OutputSegment(start, length, null));
        outLen += length;
    }

    /// <summary>Bytes of the chosen replacement; '?' is used only to measure a blocked preview.</summary>
    private static byte[] ReplacementBytes(TextCodec codec, string? replacement)
    {
        try { return codec.Encode(replacement ?? "?"); }
        catch (UnrepresentableCharactersException) { throw new ArgumentException("The replacement cannot be written in the target encoding.", nameof(replacement)); }
    }

    private static byte[] Encode(TextCodec codec, string text, byte[] rep, LossReport.Collector loss, long outBase, int charBase, Func<int, bool>? isInvalid)
    {
        var buf = new List<byte>(text.Length);
        for (int i = 0; i < text.Length;)
        {
            bool ok = Rune.DecodeFromUtf16(text.AsSpan(i), out Rune r, out int used) == System.Buffers.OperationStatus.Done;
            int cp = ok ? r.Value : text[i];
            bool invalid = isInvalid?.Invoke(i) ?? false;
            if (invalid || !codec.CanEncode(cp))
            {
                loss.Add(cp, charBase + i, outBase + buf.Count, invalid);
                buf.AddRange(rep);
            }
            else codec.EncodeRune(cp, buf);
            i += used;
        }
        return buf.ToArray();
    }
}
