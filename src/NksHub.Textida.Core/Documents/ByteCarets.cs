using System.Buffers;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Documents;

/// <summary>Reads bytes of one revision, from a <see cref="Document"/> or from one of its snapshots.</summary>
public delegate int ByteReader(long offset, Span<byte> destination);

/// <summary>One revision as text bytes: how to read it, its length, how its bytes decode and where its text starts (behind the BOM).</summary>
public readonly struct ByteText(ByteReader read, long length, TextCodec codec, int bom)
{
    public ByteReader Read { get; } = read;
    public long Length { get; } = length;
    public TextCodec Codec { get; } = codec;
    public int Bom { get; } = bom;

    public static ByteText Of(Document doc) => new(doc.Read, doc.ByteLength, doc.Codec, doc.BomLength);

    /// <param name="bom">Length of the BOM of the document the snapshot was taken from.</param>
    public static ByteText Of(DocumentSnapshot snapshot, int bom) => new(snapshot.Read, snapshot.ByteLength, snapshot.Codec, bom);
}

/// <summary>
/// The edit one caret makes in a multi-caret transaction, in bytes of the revision it is applied to. <see cref="CaretAt"/> is where the caret stands
/// inside the inserted bytes afterwards (their length for typing, 0 for a deletion); <see cref="Index"/> is the caret it belongs to in the selections before.
/// </summary>
public readonly record struct CaretEdit(long Start, long Deleted, byte[] Insert, int CaretAt, int Index)
{
    public bool HasContent => Deleted > 0 || Insert.Length > 0;
}

/// <summary>The edits of a multi-caret transaction that do not overlap, and the carets after them.</summary>
public sealed class CaretPlan
{
    internal CaretPlan(List<CaretEdit> kept, SelectionSet after)
    {
        Kept = kept;
        After = after;
    }

    public IReadOnlyList<CaretEdit> Kept { get; }

    /// <summary>The selections once the edits are applied; carets that end at one place are one.</summary>
    public SelectionSet After { get; }
}

/// <summary>
/// Several carets and selections over the bytes of one document (the paged text view): plans for typing, deleting and pasting at every caret,
/// one transaction that applies them, and the searches that add carets. Everything works on bytes of one revision, so no text of the whole document is made.
/// </summary>
public static class ByteCarets
{
    /// <summary>Bytes read around an offset to find the character next to it: one character is at most 4, a line break two units of at most 4.</summary>
    private const int Reach = 12;
    private const int SearchChunk = 1 << 20;
    private const int WordReach = 512;

    // ---- characters next to an offset ----

    /// <summary>The offset after the character at <paramref name="offset"/>; a CR LF pair counts as one character. The end of the text stays where it is.</summary>
    public static long Next(ByteText t, long offset)
    {
        if (offset >= t.Length) return t.Length;
        int w = t.Codec.Alignment;
        Span<byte> buffer = stackalloc byte[Reach];
        int n = t.Read(offset, buffer[..(int)Math.Min(Reach, t.Length - offset)]);
        if (n <= 0) return offset;
        int unit = Math.Max(1, t.Codec.ReadUnit(buffer[..n], out _));
        if (unit == w && n >= 2 * w && LineScanner.Unit(buffer, 0, t.Codec) == 13 && LineScanner.Unit(buffer, w, t.Codec) == 10) unit = 2 * w;
        return offset + unit;
    }

    /// <summary>The offset before the character that ends at <paramref name="offset"/>; a CR LF pair counts as one character. The start of the text stays where it is.</summary>
    public static long Previous(ByteText t, long offset)
    {
        if (offset <= t.Bom) return offset;
        int w = t.Codec.Alignment;
        int back = (int)Math.Min(Reach, offset - t.Bom);
        back -= back % w;
        if (back == 0) return offset;
        Span<byte> window = stackalloc byte[Reach];
        window = window[..back];
        long from = offset - back;
        t.Read(from, window);
        int start = 0;
        // the window may begin inside a character: its continuation bytes belong to a character that ends before the one asked for
        if (t.Codec.Kind == CodecKind.Utf8)
            while (start < back && from + start > t.Bom && (window[start] & 0xC0) == 0x80) start++;
        int last = start;
        for (int i = start; i < back;)
        {
            last = i;
            i += Math.Max(1, t.Codec.ReadUnit(window[i..], out _));
        }
        if (last - w >= start && LineScanner.Unit(window, last, t.Codec) == 10 && LineScanner.Unit(window, last - w, t.Codec) == 13) last -= w;
        return from + last;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static string ReadString(ByteText t, long from, long to)
    {
        var bytes = new byte[to - from];
        t.Read(from, bytes);
        return t.Codec.GetString(bytes);
    }

    private static bool WordBefore(ByteText t, long offset)
    {
        if (offset <= t.Bom) return false;
        string s = ReadString(t, Previous(t, offset), offset);
        return s.Length > 0 && IsWordChar(s[^1]);
    }

    private static bool WordAfter(ByteText t, long offset)
    {
        if (offset >= t.Length) return false;
        string s = ReadString(t, offset, Next(t, offset));
        return s.Length > 0 && IsWordChar(s[0]);
    }

    /// <summary>
    /// The word (letters, digits, underscore) the offset is in or touches, as bytes. Only 512 bytes each way are read, so a word longer than that is cut
    /// at its ends (shortcut: a word of a minified line is cut, upgrade by reading on until the word ends).
    /// </summary>
    public static bool TryWordAt(ByteText t, long offset, out long start, out long end)
    {
        start = end = offset;
        int w = t.Codec.Alignment;
        long from = Math.Max(t.Bom, offset - WordReach);
        from -= (from - t.Bom) % w;
        long to = Math.Min(t.Length, offset + WordReach);
        if (to <= from || offset < from) return false;
        var bytes = new byte[to - from];
        t.Read(from, bytes);
        int skip = 0;
        if (t.Codec.Kind == CodecKind.Utf8)
            while (skip < bytes.Length && from + skip > t.Bom && (bytes[skip] & 0xC0) == 0x80) skip++;
        int complete = t.Codec.CompleteLength(bytes.AsSpan(skip));
        var decoded = t.Codec.WithBom(false).Decode(bytes.AsMemory(skip, complete));
        long origin = from + skip;
        if (offset < origin || offset > origin + complete) return false;
        var text = decoded.Text;
        int s = decoded.CharIndexOf(offset - origin), e = s;
        while (s > 0 && IsWordChar(text[s - 1])) s--;
        while (e < text.Length && IsWordChar(text[e])) e++;
        if (e == s) return false;
        start = origin + decoded.ByteOffsetOf(s);
        end = origin + decoded.ByteOffsetOf(e);
        return true;
    }

    // ---- plans ----

    /// <summary>Every selection is replaced by <paramref name="insertOf"/> of its index; the caret goes behind the new bytes.</summary>
    public static List<CaretEdit> PlanReplace(SelectionSet before, Func<int, byte[]> insertOf)
    {
        var plan = new List<CaretEdit>(before.Count);
        for (int i = 0; i < before.Count; i++)
        {
            var s = before.Items[i];
            byte[] bytes = insertOf(i);
            plan.Add(new CaretEdit(s.Start, s.Length, bytes, bytes.Length, i));
        }
        return plan;
    }

    /// <summary>Backspace or Delete at every caret: a selection is removed, an empty caret removes the character before or after it (a line break whole).</summary>
    public static List<CaretEdit> PlanDelete(ByteText t, SelectionSet before, bool backwards)
    {
        var plan = new List<CaretEdit>(before.Count);
        for (int i = 0; i < before.Count; i++)
        {
            var s = before.Items[i];
            long start = s.Start, end = s.End;
            if (s.IsEmpty)
            {
                long other = backwards ? Previous(t, s.Caret) : Next(t, s.Caret);
                (start, end) = (Math.Min(s.Caret, other), Math.Max(s.Caret, other));
            }
            plan.Add(new CaretEdit(start, end - start, [], 0, i));
        }
        return plan;
    }

    /// <summary>Removes the selected text only (cut); a caret without a selection stays where it is.</summary>
    public static List<CaretEdit> PlanDeleteSelected(SelectionSet before)
    {
        var plan = new List<CaretEdit>(before.Count);
        for (int i = 0; i < before.Count; i++)
        {
            var s = before.Items[i];
            plan.Add(new CaretEdit(s.Start, s.Length, [], 0, i));
        }
        return plan;
    }

    // ---- applying ----

    /// <summary>
    /// Orders the plan and drops the edits that overlap an earlier one (their carets join it). The carets after the kept edits are worked out from the
    /// edits, not looked up again: edit k moves everything behind it by what it inserts minus what it deletes. The primary caret stays the one of the last
    /// kept edit that belongs to it or to a caret before it.
    /// </summary>
    public static CaretPlan Assemble(SelectionSet before, IReadOnlyList<CaretEdit> plan)
    {
        IReadOnlyList<CaretEdit> ordered = IsOrdered(plan) ? plan : plan.OrderBy(e => e.Start).ThenBy(e => e.Index).ToList();
        var kept = new List<CaretEdit>(ordered.Count);
        var carets = new List<TextSelection>(ordered.Count);
        int primary = 0;
        long end = -1, insertAt = -1, shift = 0;
        foreach (var e in ordered)
        {
            if (e.Start < end) continue;
            if (e.HasContent && e.Deleted == 0 && e.Start == insertAt) continue;
            if (e.HasContent)
            {
                end = e.Start + e.Deleted;
                insertAt = e.Deleted == 0 ? e.Start : -1;
            }
            if (e.Index <= before.Primary) primary = carets.Count;
            carets.Add(TextSelection.At(e.Start + shift + e.CaretAt));
            kept.Add(e);
            shift += e.Insert.Length - e.Deleted;
        }
        return new CaretPlan(kept, SelectionSet.Create(carets, primary));
    }

    private static bool IsOrdered(IReadOnlyList<CaretEdit> plan)
    {
        for (int i = 1; i < plan.Count; i++)
            if (plan[i - 1].Start > plan[i].Start || (plan[i - 1].Start == plan[i].Start && plan[i - 1].Index > plan[i].Index)) return false;
        return true;
    }

    /// <summary>The selections as the document stores them for undo: the primary one last.</summary>
    public static IReadOnlyList<ByteRange> PrimaryLast(SelectionSet set) =>
        [.. set.Items.Where((_, i) => i != set.Primary).Select(s => s.ToRange()), set.PrimarySelection.ToRange()];

    /// <summary>
    /// Applies a plan as one transaction: one revision and one undo step that gives back every caret before it (and redo every caret after it).
    /// Edits with the same <paramref name="coalesceKey"/> in a row share one undo step.
    /// </summary>
    public static EditResult Apply(Document doc, SelectionSet before, CaretPlan plan, string? coalesceKey)
    {
        var edits = new List<ByteEdit>(plan.Kept.Count);
        foreach (var e in plan.Kept)
            if (e.HasContent) edits.Add(new ByteEdit(new ByteRange(e.Start, e.Start + e.Deleted), e.Insert));
        return doc.Apply(edits, new EditOptions
        {
            SelectionsBefore = PrimaryLast(before),
            SelectionsAfter = PrimaryLast(plan.After),
            CoalesceKey = coalesceKey,
            Source = "user",
        });
    }

    // ---- occurrences ----

    /// <summary>
    /// Calls <paramref name="onHit"/> with the offset of each occurrence of <paramref name="needle"/> (bytes of the document encoding) that lies inside
    /// [<paramref name="from"/>, <paramref name="to"/>), in order and without overlapping; it stops when the callback returns false. A match must start on a
    /// unit of the encoding; with <paramref name="wholeWord"/> the characters next to it must not be word characters. The text is read in 1 MiB blocks.
    /// </summary>
    public static void Find(ByteText t, byte[] needle, long from, long to, bool wholeWord, Func<long, bool> onHit, CancellationToken cancel = default)
    {
        if (needle.Length == 0) return;
        int w = t.Codec.Alignment;
        to = Math.Min(to, t.Length);
        long pos = Math.Max(from, t.Bom), floor = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(SearchChunk + needle.Length - 1);
        try
        {
            while (pos + needle.Length <= to)
            {
                cancel.ThrowIfCancellationRequested();
                int want = (int)Math.Min(SearchChunk + needle.Length - 1L, to - pos);
                int got = t.Read(pos, buffer.AsSpan(0, want));
                if (got < needle.Length) break;
                for (int at = 0; at <= got - needle.Length;)
                {
                    int i = buffer.AsSpan(at, got - at).IndexOf(needle);
                    if (i < 0) break;
                    long hit = pos + at + i;
                    at += i + 1;
                    if (hit < floor || (w > 1 && (hit - t.Bom) % w != 0) || (wholeWord && (WordBefore(t, hit) || WordAfter(t, hit + needle.Length)))) continue;
                    floor = hit + needle.Length;
                    if (!onHit(hit)) return;
                }
                if (pos + got >= to) break;
                pos += got - (needle.Length - 1);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
