using System.Text;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.Core.Fragments;

public enum LiteralKind { PhpSingle, PhpDouble, PhpHeredoc, PhpNowdoc, JsSingle, JsDouble, JsTemplate }

public enum FragmentProblem
{
    None,
    /// <summary>No string literal at the caret.</summary>
    NoLiteral,
    /// <summary>A PHP double-quoted string or heredoc holds a variable or expression; unescaping it would change what the code does.</summary>
    Interpolation,
    /// <summary>A JS template holds <c>${}</c>.</summary>
    TemplateExpression,
    /// <summary>An escape sequence the editor does not turn into text (octal or hex byte above 127, legacy octal, a lone surrogate, a broken code point).</summary>
    BadEscape,
    /// <summary>The lines of a heredoc are indented less than its closing marker.</summary>
    Indentation,
    /// <summary>The text has a line that would end the heredoc or nowdoc.</summary>
    ClosingMarker,
    /// <summary>The encoding of the parent cannot hold a character of the text.</summary>
    NotEncodable,
    /// <summary>The parent changed inside the string (or the change log no longer proves it did not).</summary>
    Conflict,
    /// <summary>The ends of the string were deleted in the parent.</summary>
    RangeLost,
    /// <summary>The link was cut, for example because the parent was closed.</summary>
    Detached,
    /// <summary>The parent refused the edit.</summary>
    ParentRejected,
}

/// <summary>A string literal in UTF-16 positions of the source: the whole literal and the part between its delimiters.</summary>
public sealed record StringLiteral(LiteralKind Kind, int Start, int End, int ContentStart, int ContentEnd, string Indent = "", string Marker = "")
{
    public bool IsDocument => Kind is LiteralKind.PhpHeredoc or LiteralKind.PhpNowdoc;
}

/// <summary>Finds string literals of PHP and JavaScript with the syntax tokenizer, and converts their content to the text they stand for and back.</summary>
public static class StringLiterals
{
    /// <summary>
    /// The literal that contains <paramref name="caret"/> (a UTF-16 index into <paramref name="text"/>) or touches it. The tokenizer says where a string
    /// starts, so quotes in comments, regular expressions and other strings are not taken for literals; the end is found by the rules of the literal kind.
    /// </summary>
    public static StringLiteral? Find(string text, Tokenizer tokenizer, bool php, int caret)
    {
        var tokens = new List<Token>();
        var state = tokenizer.InitialState;
        int lineStart = 0, skipUntil = 0;
        while (lineStart <= text.Length && lineStart <= caret)
        {
            int newline = text.IndexOf('\n', lineStart);
            int lineEnd = newline < 0 ? text.Length : newline;
            if (lineEnd > lineStart && text[lineEnd - 1] == '\r') lineEnd--;
            tokens.Clear();
            state = tokenizer.TokenizeLine(text[lineStart..lineEnd], state, tokens);
            foreach (var token in tokens)
            {
                int at = lineStart + token.Start;
                if (token.Kind != TokenKinds.String || at < skipUntil) continue;
                if (Scan(text, at, php) is not { } literal) continue;
                skipUntil = literal.End;
                if (caret >= literal.Start && caret <= literal.End) return literal;
            }
            if (newline < 0) break;
            lineStart = newline + 1;
        }
        return null;
    }

    private static StringLiteral? Scan(string text, int at, bool php)
    {
        char c = text[at];
        if (php && c == '<') return ScanDocument(text, at);
        LiteralKind? kind = (php, c) switch
        {
            (true, '\'') => LiteralKind.PhpSingle,
            (true, '"') => LiteralKind.PhpDouble,
            (false, '\'') => LiteralKind.JsSingle,
            (false, '"') => LiteralKind.JsDouble,
            (false, '`') => LiteralKind.JsTemplate,
            _ => null,
        };
        if (kind is not { } k) return null;
        bool oneLine = k is LiteralKind.JsSingle or LiteralKind.JsDouble;
        for (int i = at + 1; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '\\')
            {
                i += i + 2 < text.Length && text[i + 1] == '\r' && text[i + 2] == '\n' ? 2 : 1;
                continue;
            }
            if (ch == c) return new(k, at, i + 1, at + 1, i);
            if (oneLine && ch is '\n' or '\r') return null;
        }
        return null;
    }

    private static StringLiteral? ScanDocument(string text, int at)
    {
        if (!text.AsSpan(at).StartsWith("<<<")) return null;
        int i = at + 3;
        while (i < text.Length && text[i] is ' ' or '\t') i++;
        char quote = i < text.Length && text[i] is '\'' or '"' ? text[i] : '\0';
        if (quote != '\0') i++;
        int idStart = i;
        if (i >= text.Length || !(char.IsAsciiLetter(text[i]) || text[i] == '_')) return null;
        while (i < text.Length && IsWord(text[i])) i++;
        string marker = text[idStart..i];
        if (quote != '\0')
        {
            if (i >= text.Length || text[i] != quote) return null;
            i++;
        }
        if (i < text.Length && text[i] == '\r') i++;
        if (i >= text.Length || text[i] != '\n') return null;
        int bodyStart = i + 1;
        for (int line = bodyStart; line <= text.Length;)
        {
            int j = line;
            while (j < text.Length && text[j] is ' ' or '\t') j++;
            if (text.AsSpan(j).StartsWith(marker, StringComparison.Ordinal) && (j + marker.Length == text.Length || !IsWord(text[j + marker.Length])))
            {
                int bodyEnd = line == bodyStart ? bodyStart : line - (text[line - 2] == '\r' ? 2 : 1);
                return new(quote == '\'' ? LiteralKind.PhpNowdoc : LiteralKind.PhpHeredoc, at, j + marker.Length, bodyStart, bodyEnd, text[line..j], marker);
            }
            int next = text.IndexOf('\n', line);
            if (next < 0) return null;
            line = next + 1;
        }
        return null;
    }

    private static bool IsWord(char c) => char.IsAsciiLetterOrDigit(c) || c == '_' || c >= 0x80;

    // ---- text of a literal ----

    /// <summary>The text the content of the literal stands for. False with the reason when it cannot be shown as plain text without changing its meaning.</summary>
    public static bool TryUnescape(StringLiteral literal, string content, out string text, out FragmentProblem problem)
    {
        text = "";
        problem = FragmentProblem.None;
        if (literal.IsDocument)
        {
            if (Unindent(content, literal.Indent) is not { } plain) { problem = FragmentProblem.Indentation; return false; }
            content = plain;
        }
        var result = literal.Kind switch
        {
            LiteralKind.PhpSingle => PhpSingleDecode(content),
            LiteralKind.PhpNowdoc => (content, FragmentProblem.None),
            LiteralKind.PhpDouble or LiteralKind.PhpHeredoc => PhpDoubleDecode(content, literal.Kind == LiteralKind.PhpDouble),
            _ => JsDecode(content, literal.Kind == LiteralKind.JsTemplate),
        };
        if (result.Item2 == FragmentProblem.None && !IsWellFormed(result.Item1)) return Fail(out problem, FragmentProblem.BadEscape);
        (text, problem) = result;
        return problem == FragmentProblem.None;
    }

    /// <summary>The content to write between the delimiters of the literal for <paramref name="text"/>. False when the text cannot stand in this kind of literal.</summary>
    public static bool TryEscape(StringLiteral literal, string text, out string content, out FragmentProblem problem)
    {
        problem = FragmentProblem.None;
        content = literal.Kind switch
        {
            LiteralKind.PhpSingle => PhpSingleEncode(text),
            LiteralKind.PhpNowdoc => text,
            LiteralKind.PhpDouble => PhpDoubleEncode(text, quote: true),
            LiteralKind.PhpHeredoc => PhpDoubleEncode(text, quote: false),
            LiteralKind.JsTemplate => JsEncode(text, '`'),
            LiteralKind.JsSingle => JsEncode(text, '\''),
            _ => JsEncode(text, '"'),
        };
        if (!literal.IsDocument) return true;
        content = Indent(content, literal.Indent);
        if (EndsDocument(content, literal.Marker)) { problem = FragmentProblem.ClosingMarker; return false; }
        return true;
    }

    private static bool Fail(out FragmentProblem problem, FragmentProblem value)
    {
        problem = value;
        return false;
    }

    private static bool IsWellFormed(string s)
    {
        for (int i = 0; i < s.Length;)
        {
            if (!Rune.TryGetRuneAt(s, i, out var rune)) return false;
            i += rune.Utf16SequenceLength;
        }
        return true;
    }

    private static bool EndsDocument(string content, string marker)
    {
        foreach (var line in content.Split('\n'))
        {
            var t = line.TrimStart(' ', '\t');
            if (t.StartsWith(marker, StringComparison.Ordinal) && (t.Length == marker.Length || !IsWord(t[marker.Length]))) return true;
        }
        return false;
    }

    private static string? Unindent(string content, string indent)
    {
        if (indent.Length == 0 || content.Length == 0) return content;
        var sb = new StringBuilder(content.Length);
        for (int line = 0; line <= content.Length;)
        {
            int end = content.IndexOf('\n', line);
            int stop = end < 0 ? content.Length : end + 1;
            var part = content.AsSpan(line, stop - line);
            if (part.StartsWith(indent, StringComparison.Ordinal)) sb.Append(part[indent.Length..]);
            else if (part.TrimEnd("\r\n").IsEmpty) sb.Append(part);
            else return null;
            if (end < 0) break;
            line = stop;
        }
        return sb.ToString();
    }

    private static string Indent(string content, string indent)
    {
        if (indent.Length == 0 || content.Length == 0) return content;
        var sb = new StringBuilder(content.Length + indent.Length);
        bool atStart = true;
        foreach (char c in content)
        {
            if (atStart && c is not ('\r' or '\n')) sb.Append(indent);
            sb.Append(c);
            atStart = c == '\n';
        }
        return sb.ToString();
    }

    // ---- PHP ----

    private static (string, FragmentProblem) PhpSingleDecode(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length && s[i + 1] is '\\' or '\'') i++;
            sb.Append(s[i]);
        }
        return (sb.ToString(), FragmentProblem.None);
    }

    private static string PhpSingleEncode(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");

    private static (string, FragmentProblem) PhpDoubleDecode(string s, bool quoted)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool next = i + 1 < s.Length;
            if (c == '$' && next && (s[i + 1] == '{' || s[i + 1] == '_' || char.IsAsciiLetter(s[i + 1]) || s[i + 1] >= 0x80)) return ("", FragmentProblem.Interpolation);
            if (c == '{' && next && s[i + 1] == '$') return ("", FragmentProblem.Interpolation);
            if (c != '\\' || !next) { sb.Append(c); continue; }
            char d = s[++i];
            switch (d)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'v': sb.Append('\v'); break;
                case 'e': sb.Append('\x1b'); break;
                case 'f': sb.Append('\f'); break;
                case '\\': sb.Append('\\'); break;
                case '$': sb.Append('$'); break;
                case '"' when quoted: sb.Append('"'); break;
                case 'x' when i + 1 < s.Length && char.IsAsciiHexDigit(s[i + 1]):
                    int hex = ReadDigits(s, ref i, 16, 2);
                    if (hex > 0x7F) return ("", FragmentProblem.BadEscape);
                    sb.Append((char)hex);
                    break;
                case 'u' when i + 1 < s.Length && s[i + 1] == '{':
                    int close = s.IndexOf('}', i);
                    if (close < 0 || close == i + 2 || !int.TryParse(s.AsSpan(i + 2, close - i - 2), System.Globalization.NumberStyles.AllowHexSpecifier, null, out int cp) || !Rune.IsValid(cp))
                        return ("", FragmentProblem.BadEscape);
                    sb.Append(char.ConvertFromUtf32(cp));
                    i = close;
                    break;
                case >= '0' and <= '7':
                    i--;
                    int oct = ReadDigits(s, ref i, 8, 3);
                    if (oct > 0x7F) return ("", FragmentProblem.BadEscape);
                    sb.Append((char)oct);
                    break;
                default: sb.Append('\\').Append(d); break;
            }
        }
        return (sb.ToString(), FragmentProblem.None);
    }

    /// <summary>Reads up to <paramref name="max"/> digits after position <paramref name="i"/> and leaves <paramref name="i"/> on the last one.</summary>
    private static int ReadDigits(string s, ref int i, int radix, int max)
    {
        int value = 0, count = 0;
        while (count < max && i + 1 < s.Length && char.IsAsciiHexDigit(s[i + 1]) && Convert.ToInt32(s[i + 1].ToString(), 16) is var digit && digit < radix)
        {
            value = value * radix + digit;
            i++;
            count++;
        }
        return value;
    }

    /// <summary>
    /// <c>$</c> always gets a backslash, which also keeps <c>{$</c> from opening an expression; a lone <c>{</c> must not get one, because <c>\{</c> is kept
    /// with its backslash by PHP.
    /// </summary>
    private static string PhpDoubleEncode(string s, bool quote)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            if (c is '\\' or '$' || (quote && c == '"')) sb.Append('\\').Append(c);
            else if (c < ' ' && c is not ('\n' or '\r' or '\t')) sb.Append("\\x").Append(((int)c).ToString("x2"));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // ---- JavaScript ----

    private static (string, FragmentProblem) JsDecode(string s, bool template)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (template && c == '$' && i + 1 < s.Length && s[i + 1] == '{') return ("", FragmentProblem.TemplateExpression);
            if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
            char d = s[++i];
            switch (d)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'v': sb.Append('\v'); break;
                case '0' when i + 1 >= s.Length || !char.IsAsciiDigit(s[i + 1]): sb.Append('\0'); break;
                case >= '0' and <= '9': return ("", FragmentProblem.BadEscape);
                case '\r': if (i + 1 < s.Length && s[i + 1] == '\n') i++; break;
                case '\n' or '\u2028' or '\u2029': break;
                case 'x':
                    if (i + 2 >= s.Length || !char.IsAsciiHexDigit(s[i + 1]) || !char.IsAsciiHexDigit(s[i + 2])) return ("", FragmentProblem.BadEscape);
                    sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 2), 16));
                    i += 2;
                    break;
                case 'u' when i + 1 < s.Length && s[i + 1] == '{':
                    int close = s.IndexOf('}', i);
                    if (close < 0 || close == i + 2 || !int.TryParse(s.AsSpan(i + 2, close - i - 2), System.Globalization.NumberStyles.AllowHexSpecifier, null, out int cp) || !Rune.IsValid(cp))
                        return ("", FragmentProblem.BadEscape);
                    sb.Append(char.ConvertFromUtf32(cp));
                    i = close;
                    break;
                case 'u':
                    if (i + 4 >= s.Length || !int.TryParse(s.AsSpan(i + 1, 4), System.Globalization.NumberStyles.AllowHexSpecifier, null, out int unit))
                        return ("", FragmentProblem.BadEscape);
                    sb.Append((char)unit);
                    i += 4;
                    break;
                default: sb.Append(d); break;
            }
        }
        return (sb.ToString(), FragmentProblem.None);
    }

    private static string JsEncode(string s, char quote)
    {
        var sb = new StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' || c == quote) sb.Append('\\').Append(c);
            else if (quote == '`' && c == '$' && i + 1 < s.Length && s[i + 1] == '{') sb.Append("\\$");
            else if (quote != '`' && c == '\n') sb.Append("\\n");
            else if (quote != '`' && c == '\r') sb.Append("\\r");
            else if (quote != '`' && c == '\t') sb.Append("\\t");
            else if (c is '\u2028' or '\u2029') sb.Append("\\u").Append(((int)c).ToString("x4"));
            else if (c < ' ' && c is not ('\n' or '\r' or '\t')) sb.Append("\\x").Append(((int)c).ToString("x2"));
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
