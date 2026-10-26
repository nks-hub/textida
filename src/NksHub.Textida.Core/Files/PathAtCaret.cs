namespace NksHub.Textida.Core.Files;

/// <summary>A path found in text, with the line (and column) written after it as <c>path:12</c> or <c>path:12:3</c>.</summary>
public readonly record struct PathReference(string Text, int? Line, int? Column);

/// <summary>Reads a file path from a line of text around a column, for "open the path under the caret".</summary>
public static class PathAtCaret
{
    private static bool IsPathChar(char c) => !char.IsWhiteSpace(c) && c is not ('"' or '\'' or '<' or '>' or '|' or '*' or '?' or '(' or ')' or '[' or ']' or '{' or '}' or ',' or ';' or '`');

    /// <summary>The path token at <paramref name="column"/> (0-based, may be at the end of the token); quoted paths with spaces are read up to the quote. Null when the caret is not on one.</summary>
    public static PathReference? Find(string line, int column)
    {
        column = Math.Clamp(column, 0, line.Length);
        if (QuotedAt(line, column) is { } quoted) return Split(quoted);
        int start = column, end = column;
        while (start > 0 && IsPathChar(line[start - 1])) start--;
        while (end < line.Length && IsPathChar(line[end])) end++;
        if (end == start) return null;
        string token = line[start..end].TrimEnd('.', ':');
        return token.Length == 0 ? null : Split(token);
    }

    private static string? QuotedAt(string line, int column)
    {
        foreach (char quote in new[] { '"', '\'' })
        {
            int open = column == 0 ? -1 : line.LastIndexOf(quote, column - 1);
            int close = open < 0 ? -1 : line.IndexOf(quote, Math.Max(column, open + 1));
            if (open >= 0 && close > open + 1 && line.AsSpan(open + 1, close - open - 1).Contains(' ')) return line[(open + 1)..close];
        }
        return null;
    }

    /// <summary>Splits a trailing <c>:line</c> or <c>:line:column</c> off; a drive letter is not a number and stays.</summary>
    private static PathReference Split(string token)
    {
        var numbers = new List<int>();
        string text = token;
        while (numbers.Count < 2)
        {
            int colon = text.LastIndexOf(':');
            if (colon <= 0 || !int.TryParse(text.AsSpan(colon + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n)) break;
            numbers.Add(n);
            text = text[..colon];
        }
        return new PathReference(text, numbers.Count == 0 ? null : numbers[^1], numbers.Count == 2 ? numbers[0] : null);
    }
}
