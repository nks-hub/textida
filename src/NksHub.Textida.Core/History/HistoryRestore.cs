namespace NksHub.Textida.Core.History;

/// <summary>Restoring a part of a file from a revision.</summary>
public static class HistoryRestore
{
    /// <summary>Largest product of the line counts that is aligned; beyond it a section cannot be restored (a limit of this method, not a product setting).</summary>
    public const long MaxAlignCells = 64_000_000;

    /// <summary>
    /// Replaces the lines <paramref name="fromLine"/> to <paramref name="toLine"/> (zero based, end exclusive) of <paramref name="current"/> by the lines that
    /// correspond to them in <paramref name="revision"/>. Lines are matched by a longest common subsequence; a boundary that lies inside a changed block
    /// is widened to that block. Everything outside the section stays byte for byte. Returns null when the texts are too large to align.
    /// </summary>
    public static string? RestoreSection(string current, string revision, int fromLine, int toLine)
    {
        var c = SplitLines(current);
        var r = SplitLines(revision);
        fromLine = Math.Clamp(fromLine, 0, c.Count);
        toLine = Math.Clamp(toLine, fromLine, c.Count);
        var pairs = Align(c, r);
        if (pairs is null) return null;

        int rs, re;
        if (fromLine == toLine)
        {
            rs = re = RAfterLastMatchBefore(pairs, fromLine);
        }
        else
        {
            rs = pairs[fromLine] >= 0 ? pairs[fromLine] : RAfterLastMatchBefore(pairs, fromLine);
            re = pairs[toLine - 1] >= 0 ? pairs[toLine - 1] + 1 : NextMatch(pairs, toLine, r.Count);
        }
        if (re < rs) re = rs;
        return string.Concat(c.Take(fromLine)) + string.Concat(r.Skip(rs).Take(re - rs)) + string.Concat(c.Skip(toLine));
    }

    private static int RAfterLastMatchBefore(int[] pairs, int line)
    {
        for (int i = line - 1; i >= 0; i--) if (pairs[i] >= 0) return pairs[i] + 1;
        return 0;
    }

    private static int NextMatch(int[] pairs, int line, int rLength)
    {
        for (int i = line; i < pairs.Length; i++) if (pairs[i] >= 0) return pairs[i];
        return rLength;
    }

    /// <summary>For each line of a: the index of the matching line of b, or -1.</summary>
    private static int[]? Align(List<string> a, List<string> b)
    {
        var map = new int[a.Count];
        Array.Fill(map, -1);
        int head = 0;
        while (head < a.Count && head < b.Count && a[head] == b[head]) { map[head] = head; head++; }
        int tail = 0;
        while (tail < a.Count - head && tail < b.Count - head && a[^(tail + 1)] == b[^(tail + 1)]) { map[a.Count - 1 - tail] = b.Count - 1 - tail; tail++; }
        int n = a.Count - head - tail, m = b.Count - head - tail;
        if (n == 0 || m == 0) return map;
        if ((long)n * m > MaxAlignCells) return null;

        var len = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                len[i, j] = a[head + i] == b[head + j] ? len[i + 1, j + 1] + 1 : Math.Max(len[i + 1, j], len[i, j + 1]);
        for (int i = 0, j = 0; i < n && j < m;)
        {
            if (a[head + i] == b[head + j]) { map[head + i] = head + j; i++; j++; }
            else if (len[i + 1, j] >= len[i, j + 1]) i++;
            else j++;
        }
        return map;
    }

    /// <summary>Lines with their terminators, so that joining them gives the text back.</summary>
    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n' || (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')))
            {
                lines.Add(text[start..(i + 1)]);
                start = i + 1;
            }
            else if (text[i] == '\r') { lines.Add(text[start..(i + 2)]); i++; start = i + 1; }
        }
        if (start < text.Length) lines.Add(text[start..]);
        return lines;
    }
}
