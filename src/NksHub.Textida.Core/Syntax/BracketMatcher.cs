namespace NksHub.Textida.Core.Syntax;

/// <summary>A position in lines of text: 0-based line, 0-based UTF-16 column.</summary>
public readonly record struct TextPosition(int Line, int Column);

/// <summary>
/// Finds the partner of a bracket (<c>()</c>, <c>[]</c>, <c>{}</c>). With a tokenizer, a bracket inside a string, a comment or a regex literal
/// neither counts nor is matched; without one every bracket counts. Only brackets of the same kind are counted for nesting.
/// shortcut: tokenizes the lines from the start of the document up to the lines searched, so a jump costs one pass over that part; a paged document needs checkpoints (P3).
/// </summary>
public static class BracketMatcher
{
    private const string Brackets = "([{)]}";

    /// <summary>
    /// The partner of the bracket at <paramref name="column"/> or, when there is none, of the one just before it (the caret sits behind a bracket).
    /// Null when there is no bracket there, it is not in code, or it has no partner.
    /// </summary>
    public static TextPosition? FindPair(IReadOnlyList<string> lines, Tokenizer? tokenizer, int line, int column)
    {
        if ((uint)line >= (uint)lines.Count) return null;
        string text = lines[line];
        int at = IsBracket(text, column) ? column : IsBracket(text, column - 1) ? column - 1 : -1;
        if (at < 0) return null;

        var tokens = new LineTokens(lines, tokenizer);
        if (tokens.IsText(line, at)) return null;
        int kind = Brackets.IndexOf(text[at]);
        bool forward = kind < 3;
        char self = text[at], partner = Brackets[(kind + 3) % 6];
        int depth = 0;
        if (forward)
        {
            for (int l = line; l < lines.Count; l++)
                for (int c = l == line ? at : 0; c < lines[l].Length; c++)
                    if (Step(lines[l][c], l, c)) return new TextPosition(l, c);
        }
        else
        {
            for (int l = line; l >= 0; l--)
                for (int c = l == line ? at : lines[l].Length - 1; c >= 0; c--)
                    if (Step(lines[l][c], l, c)) return new TextPosition(l, c);
        }
        return null;

        bool Step(char ch, int l, int c)
        {
            if (ch != self && ch != partner) return false;
            if (tokens.IsText(l, c)) return false;
            if (ch == self) depth++;
            else if (--depth == 0) return true;
            return false;
        }
    }

    private static bool IsBracket(string text, int column) => (uint)column < (uint)text.Length && Brackets.Contains(text[column]);

    internal sealed class LineTokens(IReadOnlyList<string> lines, Tokenizer? tokenizer)
    {
        private readonly List<List<Token>> _done = [];
        private LineState? _state = tokenizer?.InitialState;

        /// <summary>True when the character belongs to a string, comment or regex literal token.</summary>
        public bool IsText(int line, int column) => Covers(line, column, t => t.Kind.StartsWith("string", StringComparison.Ordinal) || t.Kind.StartsWith("comment", StringComparison.Ordinal) || t.Kind == TokenKinds.Regex);

        /// <summary>True when the character belongs to a comment token.</summary>
        public bool IsComment(int line, int column) => Covers(line, column, t => t.Kind.StartsWith("comment", StringComparison.Ordinal));

        private bool Covers(int line, int column, Func<Token, bool> kind)
        {
            if (tokenizer is null) return false;
            while (_done.Count <= line)
            {
                var tokens = new List<Token>();
                _state = tokenizer.TokenizeLine(lines[_done.Count], _state!, tokens);
                _done.Add(tokens);
            }
            foreach (var t in _done[line])
                if (column >= t.Start && column < t.End && kind(t))
                    return true;
            return false;
        }
    }
}
