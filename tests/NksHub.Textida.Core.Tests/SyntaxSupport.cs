using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.Core.Tests;

/// <summary>Editable list of lines for the incremental tokenizer tests.</summary>
internal sealed class StringLines(IEnumerable<string> lines) : ILineSource
{
    public List<string> Lines { get; } = [.. lines];
    public long LineCount => Lines.Count;
    public string GetLine(long line) => Lines[(int)line];
    public static StringLines Of(string text) => new(text.Split('\n'));
    public override string ToString() => string.Join('\n', Lines);

    /// <summary>Replaces <paramref name="length"/> characters at (line, column), counted over line breaks, by <paramref name="insert"/>; returns the line edit.</summary>
    public (int First, int Removed, int Inserted) Edit(int line, int column, int length, string insert)
    {
        string text = ToString();
        int offset = 0;
        for (int i = 0; i < line; i++) offset += Lines[i].Length + 1;
        offset += Math.Min(column, Lines[line].Length);
        length = Math.Min(length, text.Length - offset);
        int endLine = line + text.AsSpan(offset, length).Count('\n');
        string prefix = Lines[line][..Math.Min(column, Lines[line].Length)];
        int endColumn = text.AsSpan(offset, length).LastIndexOf('\n') is var nl and >= 0 ? length - nl - 1 : Math.Min(column, Lines[line].Length) + length;
        string suffix = Lines[endLine][Math.Min(endColumn, Lines[endLine].Length)..];
        var replacement = (prefix + insert + suffix).Split('\n');
        Lines.RemoveRange(line, endLine - line + 1);
        Lines.InsertRange(line, replacement);
        return (line, endLine - line + 1, replacement.Length);
    }
}

internal static class SyntaxHelpers
{
    public static List<LineTokens> TokenizeAll(Tokenizer tokenizer, IReadOnlyList<string> lines)
    {
        var result = new List<LineTokens>();
        var state = tokenizer.InitialState;
        for (int i = 0; i < lines.Count; i++)
        {
            var tokens = new List<Token>();
            var end = tokenizer.TokenizeLine(lines[i], state, tokens);
            result.Add(new LineTokens(i, tokens, state, end));
            state = end;
        }
        return result;
    }

    public static string Render(string line, IEnumerable<Token> tokens) =>
        string.Join(" | ", tokens.Select(t => t.Kind + "=" + line.Substring(t.Start, t.Length)));

    /// <summary>Merges neighbouring tokens of one kind, as tokens of consecutive windows of one line are not merged by the window scan.</summary>
    public static List<Token> Merge(IEnumerable<Token> tokens)
    {
        var merged = new List<Token>();
        foreach (var t in tokens)
        {
            if (merged.Count > 0 && merged[^1].End == t.Start && merged[^1].Kind == t.Kind) merged[^1] = merged[^1] with { Length = merged[^1].Length + t.Length };
            else merged.Add(t);
        }
        return merged;
    }
}
