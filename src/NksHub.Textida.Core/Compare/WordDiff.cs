namespace NksHub.Textida.Core.Compare;

/// <summary>A stretch of a line that differs: <paramref name="Start"/> and <paramref name="Length"/> count UTF-16 units.</summary>
public readonly record struct WordSpan(int Start, int Length);

/// <summary>The stretches of an old line and of the new line that differ, for marking the words that changed inside a changed line.</summary>
public sealed record WordDiffResult(IReadOnlyList<WordSpan> Old, IReadOnlyList<WordSpan> New);

/// <summary>
/// The words that changed between two versions of a line. A line is cut into words (letters, digits and underscores), runs of white space and single other characters;
/// the longest common run of those is kept and everything else is changed. The common start and end are skipped first, so a small change in a long line costs
/// next to nothing, and a line of more than <c>maxTokens</c> pieces after that is reported as changed from its first to its last differing piece.
/// </summary>
public static class WordDiff
{
    public static WordDiffResult Compute(string oldText, string newText, int maxTokens = 600)
    {
        var a = Tokens(oldText);
        var b = Tokens(newText);
        int prefix = 0;
        while (prefix < a.Count && prefix < b.Count && Same(oldText, a[prefix], newText, b[prefix])) prefix++;
        int suffix = 0;
        while (suffix < a.Count - prefix && suffix < b.Count - prefix && Same(oldText, a[^(suffix + 1)], newText, b[^(suffix + 1)])) suffix++;
        int n = a.Count - prefix - suffix, m = b.Count - prefix - suffix;
        var changedA = new bool[a.Count];
        var changedB = new bool[b.Count];
        if (n > maxTokens || m > maxTokens || n == 0 || m == 0)
        {
            for (int i = 0; i < n; i++) changedA[prefix + i] = true;
            for (int j = 0; j < m; j++) changedB[prefix + j] = true;
        }
        else
        {
            var table = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    table[i, j] = Same(oldText, a[prefix + i], newText, b[prefix + j]) ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);
            for (int i = 0, j = 0; i < n || j < m;)
            {
                if (i < n && j < m && Same(oldText, a[prefix + i], newText, b[prefix + j])) { i++; j++; }
                else if (j >= m || (i < n && table[i + 1, j] >= table[i, j + 1])) changedA[prefix + i++] = true;
                else changedB[prefix + j++] = true;
            }
        }
        return new WordDiffResult(Spans(a, changedA), Spans(b, changedB));
    }

    private static bool Same(string x, WordSpan a, string y, WordSpan b) => a.Length == b.Length && string.CompareOrdinal(x, a.Start, y, b.Start, a.Length) == 0;

    private static List<WordSpan> Spans(List<WordSpan> tokens, bool[] changed)
    {
        var spans = new List<WordSpan>();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (!changed[i]) continue;
            int start = tokens[i].Start, end = start + tokens[i].Length;
            while (i + 1 < tokens.Count && changed[i + 1]) end = tokens[++i].Start + tokens[i].Length;
            spans.Add(new WordSpan(start, end - start));
        }
        return spans;
    }

    private static int Kind(char c) => char.IsLetterOrDigit(c) || c == '_' ? 1 : char.IsWhiteSpace(c) ? 2 : 0;

    private static List<WordSpan> Tokens(string text)
    {
        var tokens = new List<WordSpan>();
        for (int i = 0; i < text.Length;)
        {
            int kind = Kind(text[i]), end = i + 1;
            if (kind != 0) while (end < text.Length && Kind(text[end]) == kind) end++;
            else if (char.IsHighSurrogate(text[i]) && end < text.Length && char.IsLowSurrogate(text[end])) end++;
            tokens.Add(new WordSpan(i, end - i));
            i = end;
        }
        return tokens;
    }
}
