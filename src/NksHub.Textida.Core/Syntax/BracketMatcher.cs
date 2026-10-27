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
    public static TextPosition? FindPair(IReadOnlyList<string> lines, Tokenizer? tokenizer, int line, int column) =>
        Match(lines, new LineTokens(lines, tokenizer), line, column, int.MaxValue)?.Partner;

    /// <summary>
    /// The bracket at <paramref name="column"/> (or just before it) and its partner, looked for at most <paramref name="maxLines"/> lines away.
    /// <paramref name="tokensOf"/> gives the tokens of a 0-based line from a source that keeps its own line states; null counts every bracket.
    /// Each line of <paramref name="lines"/> is read once, so it may build its text on demand.
    /// </summary>
    public static (TextPosition Bracket, TextPosition Partner)? Match(IReadOnlyList<string> lines, Func<int, IReadOnlyList<Token>>? tokensOf, int line, int column, int maxLines) =>
        Match(lines, new LineTokens(tokensOf), line, column, maxLines);

    private static (TextPosition Bracket, TextPosition Partner)? Match(IReadOnlyList<string> lines, LineTokens tokens, int line, int column, int maxLines)
    {
        if ((uint)line >= (uint)lines.Count) return null;
        string text = lines[line];
        int at = IsBracket(text, column) ? column : IsBracket(text, column - 1) ? column - 1 : -1;
        if (at < 0 || tokens.IsText(line, at)) return null;
        int kind = Brackets.IndexOf(text[at]);
        int step = kind < 3 ? 1 : -1;
        char self = text[at], partner = Brackets[(kind + 3) % 6];
        int stop = step > 0 ? (int)Math.Min(lines.Count, (long)line + maxLines + 1) : (int)Math.Max(-1, (long)line - maxLines - 1);
        int depth = 0;
        for (int l = line; l != stop; l += step)
        {
            string t = l == line ? text : lines[l];
            for (int c = l == line ? at : step > 0 ? 0 : t.Length - 1; c >= 0 && c < t.Length; c += step)
            {
                char ch = t[c];
                if ((ch != self && ch != partner) || tokens.IsText(l, c)) continue;
                if (ch == self) depth++;
                else if (--depth == 0) return (new TextPosition(line, at), new TextPosition(l, c));
            }
        }
        return null;
    }

    private static bool IsBracket(string text, int column) => (uint)column < (uint)text.Length && Brackets.Contains(text[column]);

    internal sealed class LineTokens
    {
        private readonly Func<int, IReadOnlyList<Token>>? _tokensOf;
        private int _line = -1;
        private IReadOnlyList<Token> _tokens = [];

        /// <summary>Tokenizes <paramref name="lines"/> from the first one on, as far as they are asked for.</summary>
        public LineTokens(IReadOnlyList<string> lines, Tokenizer? tokenizer)
        {
            if (tokenizer is null) return;
            var done = new List<List<Token>>();
            var state = tokenizer.InitialState;
            _tokensOf = line =>
            {
                while (done.Count <= line)
                {
                    var tokens = new List<Token>();
                    state = tokenizer.TokenizeLine(lines[done.Count], state, tokens);
                    done.Add(tokens);
                }
                return done[line];
            };
        }

        /// <summary>Takes the tokens from a source that may tokenize a line again on every call; the last line asked for is kept.</summary>
        public LineTokens(Func<int, IReadOnlyList<Token>>? tokensOf) => _tokensOf = tokensOf;

        /// <summary>True when the character belongs to a string, comment or regex literal token.</summary>
        public bool IsText(int line, int column) => Covers(line, column, t => t.Kind.StartsWith("string", StringComparison.Ordinal) || t.Kind.StartsWith("comment", StringComparison.Ordinal) || t.Kind == TokenKinds.Regex);

        /// <summary>True when the character belongs to a comment token.</summary>
        public bool IsComment(int line, int column) => Covers(line, column, t => t.Kind.StartsWith("comment", StringComparison.Ordinal));

        private bool Covers(int line, int column, Func<Token, bool> kind)
        {
            if (_tokensOf is null) return false;
            if (_line != line)
            {
                _tokens = _tokensOf(line);
                _line = line;
            }
            foreach (var t in _tokens)
                if (column >= t.Start && column < t.End && kind(t))
                    return true;
            return false;
        }
    }
}
