using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.App.Services;

/// <summary>A word of the text that the dictionary does not know: where it is and what it says.</summary>
internal readonly record struct Misspelling(int Start, int Length, string Word);

/// <summary>
/// What of a text is checked, which words in it are words, and the diacritics of a word (PZ.3.1). In source code only comments and strings are text (a name is not a word); in Markdown the text is, but code,
/// links and tags are not; plain text is text all through.
/// </summary>
internal static partial class SpellText
{
    // the kinds of a token that are language, in a source file
    private static readonly HashSet<string> SourceKinds = [TokenKinds.Comment, TokenKinds.DocComment, TokenKinds.String];

    // the kinds of a token that are not language, in Markdown
    private static readonly HashSet<string> MarkdownSkipped = [TokenKinds.Code, TokenKinds.Link, TokenKinds.Tag, TokenKinds.Attribute, TokenKinds.Preprocessor, TokenKinds.Meta, TokenKinds.Regex, TokenKinds.Escape, TokenKinds.Constant];

    [GeneratedRegex(@"\p{L}[\p{L}\p{M}'’]*")]
    private static partial Regex WordPattern();

    /// <summary>The stretches of <paramref name="text"/> that are language. Without a tokenizer (plain text) it is all of it.</summary>
    public static List<(int Start, int End)> Regions(string text, Tokenizer? tokenizer, bool markdown)
    {
        if (tokenizer is null) return [(0, text.Length)];
        var regions = new List<(int, int)>();
        var state = tokenizer.InitialState;
        var tokens = new List<Token>();
        for (int pos = 0; pos < text.Length;)
        {
            int eol = text.AsSpan(pos).IndexOfAny('\r', '\n');
            int end = eol < 0 ? text.Length : pos + eol;
            int next = end == text.Length ? end : end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
            tokens.Clear();
            state = tokenizer.TokenizeLine(text.Substring(pos, end - pos), state, tokens);
            if (markdown)
            {
                int at = 0;
                foreach (var token in tokens.Where(t => MarkdownSkipped.Contains(t.Kind)).OrderBy(t => t.Start))
                {
                    if (token.Start > at) Add(regions, pos + at, pos + token.Start);
                    at = Math.Max(at, token.End);
                }
                if (at < end - pos) Add(regions, pos + at, end);
            }
            else
                foreach (var token in tokens.Where(t => SourceKinds.Contains(t.Kind))) Add(regions, pos + token.Start, pos + Math.Min(token.End, end - pos));
            pos = next;
        }
        return regions;
    }

    private static void Add(List<(int, int)> regions, int start, int end)
    {
        if (end <= start) return;
        if (regions.Count > 0 && regions[^1].Item2 >= start) regions[^1] = (regions[^1].Item1, Math.Max(regions[^1].Item2, end));
        else regions.Add((start, end));
    }

    /// <summary>
    /// The words of the regions, leaving out what a dictionary does not judge: a word of fewer than three letters, one in capitals, one with a capital inside (<c>camelCase</c>), one next to a digit, an underscore or a slash
    /// (an identifier, a path, an address) and one before or after <c>@</c> or <c>:</c> without a space.
    /// </summary>
    public static IEnumerable<Misspelling> Words(string text, IReadOnlyList<(int Start, int End)> regions)
    {
        foreach (var (from, to) in regions)
            foreach (Match m in WordPattern().Matches(text.Substring(from, to - from)))
            {
                int start = from + m.Index, end = start + m.Length;
                string word = m.Value.Trim('\'', '’');
                if (word.Length < 3 || IsCode(text, start, end, word)) continue;
                yield return new Misspelling(start, m.Length, m.Value);
            }
    }

    private static bool IsCode(string text, int start, int end, string word)
    {
        if (word.All(char.IsUpper)) return true;
        if (word.Skip(1).Any(char.IsUpper)) return true; // camelCase, HTTPServer, "iPhone": a capital that does not start the word
        char before = start > 0 ? text[start - 1] : ' ', after = end < text.Length ? text[end] : ' ';
        // a word joined to another by a dot is a file name or an address: example.test, file.txt
        if ((before == '.' && start > 1 && char.IsLetterOrDigit(text[start - 2])) || (after == '.' && end + 1 < text.Length && char.IsLetterOrDigit(text[end + 1]))) return true;
        return char.IsDigit(before) || char.IsDigit(after) || before is '_' or '/' or '\\' or '@' or '#' or '$' or '%' || after is '_' or '/' or '\\' or '@' || (before == ':' && start > 1 && text[start - 2] != ' ') || (after == ':' && end + 1 < text.Length && text[end + 1] == '/');
    }

    /// <summary>The words <paramref name="known"/> does not know, in text order.</summary>
    public static List<Misspelling> Misspelled(string text, Tokenizer? tokenizer, bool markdown, Func<string, bool> known) =>
        [.. Words(text, Regions(text, tokenizer, markdown)).Where(w => !known(w.Word.Trim('\'', '’')))];

    private static readonly Dictionary<char, string> Unaccented = new()
    {
        ['ł'] = "l", ['Ł'] = "L", ['đ'] = "d", ['Đ'] = "D", ['ø'] = "o", ['Ø'] = "O", ['ß'] = "ss", ['æ'] = "ae", ['Æ'] = "AE", ['œ'] = "oe", ['Œ'] = "OE", ['ı'] = "i",
    };

    /// <summary>The word without its diacritics: <c>příliš</c> becomes <c>prilis</c>, <c>Łódź</c> becomes <c>Lodz</c>.</summary>
    public static string Strip(string word)
    {
        var sb = new StringBuilder(word.Length);
        foreach (char c in word.Normalize(NormalizationForm.FormD))
        {
            if (Unaccented.TryGetValue(c, out string? plain)) sb.Append(plain);
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>The one suggestion that differs from <paramref name="word"/> only in its diacritics, or null when there is none or more than one.</summary>
    public static string? DiacriticsFor(string word, IEnumerable<string> suggestions)
    {
        string plain = Strip(word);
        var same = suggestions.Select(s => s.Trim()).Where(s => s.Length == word.Length && !string.Equals(s, word, StringComparison.Ordinal) && string.Equals(Strip(s), plain, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.Ordinal).ToList();
        if (same.Count != 1) return null;
        // the capitals of the word stay: a word written in capitals, or with a capital first letter, is answered the same way
        string answer = same[0];
        if (word.All(c => !char.IsLetter(c) || char.IsUpper(c))) return answer.ToUpper(CultureInfo.CurrentCulture);
        return char.IsUpper(word[0]) && char.IsLower(answer[0]) ? char.ToUpper(answer[0], CultureInfo.CurrentCulture) + answer[1..] : answer;
    }
}
