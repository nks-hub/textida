using System.Globalization;

namespace NksHub.Textida.Core.Syntax;

/// <summary>What case the keywords are changed to.</summary>
public enum KeywordCaseMode { Upper, Lower, AsDefined }

/// <summary>
/// Changes the case of the keywords of a language, and only of them. A keyword is a word of a keyword group of the grammar (kind <c>keyword</c>) that the tokenizer found outside comments and strings; a word that is only
/// colored like a keyword by a pattern, an identifier and the text of a string or comment are never touched. A language whose grammar is case sensitive is refused: there the case is part of the word, and a change
/// would make another program. A change that would change the length of the text (a letter with no one-to-one case) is skipped.
/// </summary>
public static class KeywordCaseChange
{
    /// <summary>Whether the grammar allows the change: its keywords mean the same in any case.</summary>
    public static bool IsSafe(Grammar grammar) => !grammar.CaseSensitive;

    /// <summary>
    /// The edits that bring the keywords between <paramref name="from"/> and <paramref name="to"/> (offsets in <paramref name="text"/>; a keyword that is not whole inside is left alone) to <paramref name="mode"/>:
    /// start, length and new text of each. Empty when the grammar is not safe. The text is tokenized from its start, so a range that begins inside a comment is known to.
    /// </summary>
    public static IReadOnlyList<(int Start, int Length, string Text)> Edits(string text, Tokenizer tokenizer, KeywordCaseMode mode, int from, int to)
    {
        var grammar = tokenizer.Grammar;
        var edits = new List<(int, int, string)>();
        if (!IsSafe(grammar)) return edits;
        var state = tokenizer.InitialState;
        var tokens = new List<Token>();
        int offset = 0;
        while (offset <= text.Length)
        {
            int end = offset;
            while (end < text.Length && text[end] is not ('\r' or '\n')) end++;
            string line = text.Substring(offset, end - offset);
            tokens.Clear();
            state = tokenizer.TokenizeLine(line, state, tokens);
            if (offset + line.Length >= from && offset <= to)
                foreach (var token in tokens)
                {
                    if (token.Kind != TokenKinds.Keyword && !token.Kind.StartsWith(TokenKinds.Keyword + ".", StringComparison.Ordinal)) continue;
                    int start = offset + token.Start, stop = start + token.Length;
                    if (start < from || stop > to) continue;
                    string word = line.Substring(token.Start, token.Length);
                    if (grammar.WordKind(word) is not { } kind || kind != token.Kind) continue; // colored by a pattern, not a word of the list
                    string changed = mode switch
                    {
                        KeywordCaseMode.Upper => word.ToUpper(CultureInfo.InvariantCulture),
                        KeywordCaseMode.Lower => word.ToLower(CultureInfo.InvariantCulture),
                        _ => grammar.SpellingOf(word) ?? word,
                    };
                    if (changed.Length == word.Length && changed != word) edits.Add((start, word.Length, changed));
                }
            if (end >= text.Length) break;
            offset = end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
        }
        return edits;
    }

    /// <summary>The text with <paramref name="edits"/> applied.</summary>
    public static string Apply(string text, IReadOnlyList<(int Start, int Length, string Text)> edits)
    {
        var chars = text.ToCharArray();
        foreach (var (start, length, replacement) in edits) replacement.CopyTo(0, chars, start, length);
        return new string(chars);
    }
}
