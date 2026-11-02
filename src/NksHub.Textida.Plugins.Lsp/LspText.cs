using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Plugins.Lsp;

/// <summary>
/// Lines of a text as the language server protocol counts them: <c>\n</c>, <c>\r\n</c> and a lone <c>\r</c> end a line, and a column is a number of UTF-16 code units.
/// </summary>
public sealed class LineMap
{
    private readonly string _text;
    private readonly List<int> _starts = [0];

    public LineMap(string text)
    {
        _text = text;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            else if (text[i] != '\r' && text[i] != '\n') continue;
            _starts.Add(i + 1);
        }
    }

    public int LineCount => _starts.Count;

    /// <summary>The 0-based line and UTF-16 column of a character offset.</summary>
    public (int Line, int Character) PositionOf(int offset)
    {
        offset = Math.Clamp(offset, 0, _text.Length);
        int at = _starts.BinarySearch(offset);
        int line = at >= 0 ? at : ~at - 1;
        return (line, offset - _starts[line]);
    }

    /// <summary>The character offset of a position; a column past the end of the line stops at the end of its text, a line past the end of the text at the end of the text.</summary>
    public int OffsetOf(int line, int character)
    {
        if (line < 0) return 0;
        if (line >= _starts.Count) return _text.Length;
        int end = line + 1 < _starts.Count ? _starts[line + 1] : _text.Length;
        while (end > _starts[line] && _text[end - 1] is '\n' or '\r') end--;
        return (int)Math.Min((long)_starts[line] + Math.Max(character, 0), end);
    }

    /// <summary>The text of a line without its line break.</summary>
    public string LineText(int line)
    {
        if (line < 0 || line >= _starts.Count) return "";
        int start = _starts[line];
        return _text[start..OffsetOf(line, int.MaxValue)];
    }
}

/// <summary>The one change that turns an old text into a new one: the old characters <c>[Start, OldEnd)</c> become <see cref="Inserted"/>.</summary>
public readonly record struct TextChange(int Start, int OldEnd, string Inserted)
{
    /// <summary>The smallest single change between two texts (common prefix and suffix), or null when they are equal. A surrogate pair and a <c>\r\n</c> are never cut in two (a position between them does not exist in the protocol).</summary>
    public static TextChange? Between(string old, string next) => old == next ? null : Narrow(old, 0, old.Length, next, 0, next.Length);

    /// <summary>
    /// The separate changes that turn <paramref name="old"/> into <paramref name="next"/>, in the order of the old text, each against the old text; empty when the texts are equal.
    /// The texts are compared by lines (<c>\n</c>, <c>\r\n</c> and a lone <c>\r</c> end one, so a line break is never cut in two), and every run of different lines is narrowed to its own common prefix and suffix,
    /// so the carets of a multi-caret edit on different lines are separate changes. Send them from the last to the first, or apply them with <see cref="ApplyAll"/>.
    /// shortcut: edits on the same or on neighbouring lines are one change, and a text that differs by more than <paramref name="maxEdits"/> lines is one change spanning them; fine while the server only needs the text to be right.
    /// </summary>
    public static IReadOnlyList<TextChange> Hunks(string old, string next, int maxEdits = 256)
    {
        if (Between(old, next) is not { } whole) return [];
        var (a, b) = (Lines(old), Lines(next));
        int head = 0;
        while (head < a.Count && head < b.Count && SameLine(old, a[head], next, b[head])) head++;
        int tail = 0;
        while (tail < a.Count - head && tail < b.Count - head && SameLine(old, a[^(tail + 1)], next, b[^(tail + 1)])) tail++;
        int n = a.Count - head - tail, m = b.Count - head - tail;
        var runs = Myers(n, m, (i, j) => SameLine(old, a[head + i], next, b[head + j]), maxEdits);
        if (runs is null) return [whole];
        var result = new List<TextChange>();
        foreach (var (a0, a1, b0, b1) in runs)
        {
            int oldStart = a0 < a.Count - head ? a[head + a0].Start : old.Length, oldEnd = a1 > a0 ? a[head + a1 - 1].Start + a[head + a1 - 1].Length : oldStart;
            int newStart = b0 < b.Count - head ? b[head + b0].Start : next.Length, newEnd = b1 > b0 ? b[head + b1 - 1].Start + b[head + b1 - 1].Length : newStart;
            var change = Narrow(old, oldStart, oldEnd, next, newStart, newEnd);
            if (change.Start != change.OldEnd || change.Inserted.Length > 0) result.Add(change);
        }
        return result;
    }

    private static TextChange Narrow(string old, int oldStart, int oldEnd, string next, int newStart, int newEnd)
    {
        int max = Math.Min(oldEnd - oldStart, newEnd - newStart), prefix = 0;
        while (prefix < max && old[oldStart + prefix] == next[newStart + prefix]) prefix++;
        if (prefix > 0 && (char.IsHighSurrogate(old[oldStart + prefix - 1]) || (old[oldStart + prefix - 1] == '\r' && oldStart + prefix < old.Length && old[oldStart + prefix] == '\n'))) prefix--;
        int suffix = 0;
        while (suffix < max - prefix && old[oldEnd - 1 - suffix] == next[newEnd - 1 - suffix]) suffix++;
        if (suffix > 0 && (char.IsLowSurrogate(old[oldEnd - suffix]) || (old[oldEnd - suffix] == '\n' && oldEnd - suffix > 0 && old[oldEnd - suffix - 1] == '\r'))) suffix--;
        return new TextChange(oldStart + prefix, oldEnd - suffix, next[(newStart + prefix)..(newEnd - suffix)]);
    }

    private static List<(int Start, int Length)> Lines(string text)
    {
        var lines = new List<(int, int)>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            else if (text[i] != '\r' && text[i] != '\n') continue;
            lines.Add((start, i + 1 - start));
            start = i + 1;
        }
        if (start < text.Length) lines.Add((start, text.Length - start));
        return lines;
    }

    private static bool SameLine(string x, (int Start, int Length) a, string y, (int Start, int Length) b) =>
        a.Length == b.Length && string.CompareOrdinal(x, a.Start, y, b.Start, a.Length) == 0;

    /// <summary>The runs of the shortest edit script between two sequences (Myers), as [from, to) of the first and [from, to) of the second; null when it needs more than <paramref name="maxEdits"/> edits.</summary>
    private static List<(int A0, int A1, int B0, int B1)>? Myers(int n, int m, Func<int, int, bool> same, int maxEdits)
    {
        if (n == 0 && m == 0) return [];
        int limit = Math.Min(n + m, maxEdits), offset = limit + 1;
        var v = new int[2 * limit + 3];
        var trace = new List<int[]>();
        for (int d = 0; d <= limit; d++)
        {
            trace.Add((int[])v.Clone());
            for (int k = -d; k <= d; k += 2)
            {
                int x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]) ? v[offset + k + 1] : v[offset + k - 1] + 1, y = x - k;
                while (x < n && y < m && same(x, y)) { x++; y++; }
                v[offset + k] = x;
                if (x < n || y < m) continue;
                var removed = new bool[n];
                var added = new bool[m];
                for (int step = d; step > 0; step--)
                {
                    var before = trace[step];
                    int at = x - y, from = at == -step || (at != step && before[offset + at - 1] < before[offset + at + 1]) ? at + 1 : at - 1, fromX = before[offset + from], fromY = fromX - from;
                    if (from == at + 1) added[fromY] = true;
                    else removed[fromX] = true;
                    (x, y) = (fromX, fromY);
                }
                var runs = new List<(int, int, int, int)>();
                for (int i = 0, j = 0; i < n || j < m;)
                {
                    if (i < n && j < m && !removed[i] && !added[j]) { i++; j++; continue; }
                    int i0 = i, j0 = j;
                    while (i < n && removed[i]) i++;
                    while (j < m && added[j]) j++;
                    runs.Add((i0, i, j0, j));
                }
                return runs;
            }
        }
        return null;
    }

    /// <summary>Changes of <see cref="Hunks"/> (all against the old text) applied to it, the last one first so that the earlier ranges stay valid.</summary>
    public static string ApplyAll(string old, IReadOnlyList<TextChange> changes)
    {
        for (int i = changes.Count - 1; i >= 0; i--) old = changes[i].ApplyTo(old);
        return old;
    }

    /// <summary>The change applied to <paramref name="old"/>.</summary>
    public string ApplyTo(string old) => string.Concat(old.AsSpan(0, Start), Inserted, old.AsSpan(OldEnd));
}

/// <summary>Character offsets of a text turned into byte offsets of the file it was read from.</summary>
public static class ByteMap
{
    /// <summary>Byte offsets (from the start of the file, byte order mark included) by character offset; an offset inside a surrogate pair moves behind it.</summary>
    public static Dictionary<int, long> ToBytes(TextCodec codec, int bom, string text, IEnumerable<int> offsets)
    {
        var plain = codec.WithBom(false);
        var result = new Dictionary<int, long>();
        int at = 0;
        long bytes = bom;
        foreach (var c in offsets.Distinct().Order())
        {
            int target = Math.Clamp(c, at, text.Length);
            if (target > 0 && target < text.Length && char.IsHighSurrogate(text[target - 1]) && char.IsLowSurrogate(text[target])) target++;
            bytes += plain.Encode(text[at..target]).Length;
            at = target;
            result[c] = bytes;
        }
        return result;
    }
}
