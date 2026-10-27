namespace NksHub.Textida.Plugins.Emmet;

/// <summary>Finds the abbreviation that ends at the caret in the text of a line.</summary>
public static class AbbreviationText
{
    private static bool IsHtmlChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.' or '#' or '$' or '@' or '+' or '>' or '^' or '*' or '!';

    private static bool IsCssChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '#' or '.' or '%' or '!' or ':' or '+';

    /// <summary>
    /// The index in <paramref name="line"/> (the text of the line in front of the caret) where the abbreviation starts, or -1. The abbreviation reaches back over
    /// the characters the syntax allows and over balanced brackets, braces and parentheses (which may hold spaces); a <c>&gt;</c> that closes a tag ends it. HTML
    /// is not expanded inside an open tag. CSS needs only the line to be empty or end in <c>{</c>, <c>;</c> or <c>}</c> before the abbreviation.
    /// </summary>
    public static int Find(string syntax, string line)
    {
        bool css = syntax == "css";
        int i = line.Length;
        while (i > 0)
        {
            char c = line[i - 1];
            if (!css && c is ']' or ')' or '}')
            {
                int opener = MatchBack(line, i - 1);
                if (opener < 0) break;
                i = opener;
                continue;
            }
            if (css ? !IsCssChar(c) : !IsHtmlChar(c)) break;
            if (!css && c == '>')
            {
                int lt = i >= 2 ? line.LastIndexOf('<', i - 2) : -1;
                if (lt >= 0 && line.IndexOf('>', lt, i - 1 - lt) < 0) break; // the end of a tag, not an operator
            }
            i--;
        }
        while (i < line.Length && line[i] is '+' or '>' or '^' or '*' or '-' or '@' or ':' && !(css && line[i] == '-' && i + 1 < line.Length && char.IsAsciiDigit(line[i + 1]))) i++;
        if (i >= line.Length) return -1;
        string before = line[..i];
        if (css)
        {
            string t = before.TrimEnd();
            return t.Length == 0 || t[^1] is '{' or ';' or '}' ? i : -1;
        }
        return before.LastIndexOf('<') > before.LastIndexOf('>') ? -1 : i;
    }

    private static int MatchBack(string text, int closer)
    {
        char close = text[closer], open = close switch { ']' => '[', ')' => '(', _ => '{' };
        int depth = 0;
        for (int i = closer; i >= 0; i--)
        {
            if (text[i] == close) depth++;
            else if (text[i] == open && --depth == 0) return i;
        }
        return -1;
    }
}
