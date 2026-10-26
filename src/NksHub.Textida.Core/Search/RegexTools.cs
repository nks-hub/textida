using System.Text;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Search;

/// <summary>One match of <see cref="RegexTools.Test"/>; <see cref="Groups"/> holds the numbered and named groups that took part (group 0 is the whole match).</summary>
public sealed record RegexMatchResult(int Index, int Length, string Value, IReadOnlyList<RegexGroupResult> Groups);

public sealed record RegexGroupResult(string Name, int Index, int Length, string Value);

/// <param name="Error">The reason the pattern cannot be used; no matches then.</param>
/// <param name="TimedOut">The pattern took longer than the match timeout on this text.</param>
/// <param name="Truncated">More than <see cref="RegexTools.MaxTestMatches"/> matches; the rest is not listed.</param>
/// <param name="Replaced">The sample with every match replaced, when a replacement was given.</param>
public sealed record RegexTestResult(IReadOnlyList<RegexMatchResult> Matches, string? Error, bool TimedOut, bool Truncated, string? Replaced);

/// <summary>
/// One piece of a pattern in reading order. <see cref="Kind"/> names the construct (<c>group</c>, <c>digit</c>, <c>quantStar</c> ...), <see cref="Arg"/> carries what the
/// description needs (the literal text, the class content, the group name, the repeat counts). <see cref="Lazy"/> marks a quantifier that takes as little as possible.
/// A part of kind <c>unsupported</c> is syntax of other dialects (PCRE, Python) that the .NET engine does not read; the search rejects or misreads it.
/// </summary>
public sealed record RegexPart(string Token, string Kind, string Arg = "", bool Lazy = false, int Depth = 0);

/// <summary>
/// The one regular expression dialect of text search: .NET (<see cref="Regex"/>, culture invariant, <c>^</c> and <c>$</c> at line boundaries). Everything that matches
/// a pattern, the search worker, Replace in Files and the tester, builds it through <see cref="CompiledPattern"/>, so "Try" shows what the search finds.
/// Replacement text supports <c>$1</c>, <c>${name}</c>, <c>$&amp;</c> and the case conversions <c>\U \L \E \u \l</c>.
/// </summary>
public static class RegexTools
{
    public const int MaxTestMatches = 1000;

    /// <summary>The reason <paramref name="pattern"/> cannot be searched for, or null when it is usable (an empty pattern is usable: it finds nothing).</summary>
    public static string? Validate(SearchPattern pattern)
    {
        if (string.IsNullOrEmpty(pattern.Text)) return null;
        try
        {
            CompiledPattern.Create(pattern);
            return null;
        }
        catch (ArgumentException e)
        {
            return e.Message;
        }
    }

    /// <summary>A regex for <paramref name="pattern"/> exactly as the search builds it. Throws <see cref="ArgumentException"/> for an empty or invalid pattern.</summary>
    public static Regex Create(SearchPattern pattern, RegexEngine engine = RegexEngine.Auto) => CompiledPattern.Create(pattern, engine).Regex;

    /// <summary>Runs the pattern over a sample text the way the search runs it over a document; empty matches are left out like in the search.</summary>
    public static RegexTestResult Test(SearchPattern pattern, string sample, string? replacement = null)
    {
        Regex regex;
        try
        {
            regex = Create(pattern);
        }
        catch (ArgumentException e)
        {
            return new RegexTestResult([], e.Message, false, false, null);
        }

        var matches = new List<RegexMatchResult>();
        bool truncated = false;
        try
        {
            foreach (Match m in regex.Matches(sample))
            {
                if (m.Length == 0) continue;
                if (matches.Count == MaxTestMatches)
                {
                    truncated = true;
                    break;
                }
                var groups = new List<RegexGroupResult>();
                foreach (Group g in m.Groups)
                    if (g.Success) groups.Add(new RegexGroupResult(g.Name, g.Index, g.Length, g.Value));
                matches.Add(new RegexMatchResult(m.Index, m.Length, m.Value, groups));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return new RegexTestResult(matches, null, true, truncated, null);
        }

        string? replaced = null;
        if (replacement is not null)
        {
            var sb = new StringBuilder();
            int at = 0;
            foreach (Match m in regex.Matches(sample))
            {
                if (m.Length == 0) continue;
                sb.Append(sample, at, m.Index - at).Append(pattern.IsRegex ? ExpandReplacement(m, replacement) : replacement);
                at = m.Index + m.Length;
            }
            replaced = sb.Append(sample, at, sample.Length - at).ToString();
        }
        return new RegexTestResult(matches, null, false, truncated, replaced);
    }

    /// <summary>
    /// Expands <paramref name="replacement"/> for one match: <c>$1</c>, <c>${name}</c>, <c>$&amp;</c> as in .NET, and <c>\U</c> (upper case until <c>\E</c>),
    /// <c>\L</c> (lower case until <c>\E</c>), <c>\u</c> and <c>\l</c> (next character only) over the text that follows, including what a group inserts.
    /// A backslash before anything else is a plain backslash.
    /// </summary>
    public static string ExpandReplacement(Match match, string replacement)
    {
        if (!replacement.Contains('\\')) return match.Result(replacement);
        var sb = new StringBuilder();
        char mode = 'E', once = '\0';
        void Emit(string text)
        {
            foreach (char c in text)
            {
                char d = c;
                if (once != '\0')
                {
                    d = once == 'u' ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c);
                    once = '\0';
                }
                else if (mode == 'U') d = char.ToUpperInvariant(c);
                else if (mode == 'L') d = char.ToLowerInvariant(c);
                sb.Append(d);
            }
        }

        int start = 0;
        for (int i = 0; i + 1 < replacement.Length; i++)
        {
            if (replacement[i] != '\\' || replacement[i + 1] is not ('U' or 'L' or 'E' or 'u' or 'l')) continue;
            Emit(match.Result(replacement[start..i]));
            char code = replacement[i + 1];
            if (code is 'u' or 'l') once = code;
            else mode = code;
            start = ++i + 1;
        }
        Emit(match.Result(replacement[start..]));
        return sb.ToString();
    }

    // ---- explain ----

    private static readonly string[] Control = ["t", "n", "r", "f", "v", "a", "e"];

    /// <summary>
    /// Splits a .NET pattern into described parts. Literal characters in a row are one part; a group opens at one part and closes at another (<see cref="RegexPart.Depth"/>
    /// counts the open groups). Syntax of other dialects is reported as <c>unsupported</c> instead of being guessed at.
    /// </summary>
    public static IReadOnlyList<RegexPart> Explain(string pattern)
    {
        var parts = new List<RegexPart>();
        var literal = new StringBuilder();
        int depth = 0, i = 0;

        void FlushLiteral()
        {
            if (literal.Length == 0) return;
            parts.Add(new RegexPart(literal.ToString(), "literal", literal.ToString(), Depth: depth));
            literal.Clear();
        }
        void Add(string token, string kind, string arg = "", bool lazy = false)
        {
            FlushLiteral();
            parts.Add(new RegexPart(token, kind, arg, lazy, depth));
        }

        while (i < pattern.Length)
        {
            char c = pattern[i];
            switch (c)
            {
                case '\\':
                    i = Escape(pattern, i, Add);
                    break;
                case '[':
                    i = Class(pattern, i, Add);
                    break;
                case '(':
                    i = Open(pattern, i, ref depth, FlushLiteral, parts);
                    break;
                case ')':
                    FlushLiteral();
                    depth = Math.Max(0, depth - 1);
                    parts.Add(new RegexPart(")", "groupEnd", Depth: depth));
                    i++;
                    break;
                case '|':
                    Add("|", "alternation");
                    i++;
                    break;
                case '.':
                    Add(".", "any");
                    i++;
                    break;
                case '^':
                    Add("^", "startLine");
                    i++;
                    break;
                case '$':
                    Add("$", "endLine");
                    i++;
                    break;
                case '?' or '*' or '+':
                    i = Quantifier(pattern, i + 1, c == '?' ? "quantOpt" : c == '*' ? "quantStar" : "quantPlus", "", Add);
                    break;
                case '{' when TryRepeat(pattern, i, out int close, out string counts):
                    i = Quantifier(pattern, close + 1, "quantRange", counts, Add, i);
                    break;
                default:
                    literal.Append(c);
                    i++;
                    break;
            }
        }
        FlushLiteral();
        return parts;
    }

    private static int Quantifier(string p, int next, string kind, string arg, Action<string, string, string, bool> add, int tokenStart = -1)
    {
        if (tokenStart < 0) tokenStart = next - 1;
        bool lazy = next < p.Length && p[next] == '?';
        bool possessive = next < p.Length && p[next] == '+';
        if (possessive)
        {
            add(p[tokenStart..(next + 1)], "unsupported", "possessive", false);
            return next + 1;
        }
        add(p[tokenStart..(lazy ? next + 1 : next)], kind, arg, lazy);
        return lazy ? next + 1 : next;
    }

    private static bool TryRepeat(string p, int at, out int close, out string counts)
    {
        close = p.IndexOf('}', at);
        counts = "";
        if (close < 0) return false;
        counts = p[(at + 1)..close];
        return Regex.IsMatch(counts, @"^\d+(,\d*)?$", RegexOptions.CultureInvariant);
    }

    private static int Escape(string p, int i, Action<string, string, string, bool> add)
    {
        if (i + 1 >= p.Length)
        {
            add("\\", "literal", "\\", false);
            return i + 1;
        }
        char n = p[i + 1];
        string two = p.Substring(i, 2);
        switch (n)
        {
            case 'd': add(two, "digit", "", false); return i + 2;
            case 'D': add(two, "notDigit", "", false); return i + 2;
            case 'w': add(two, "word", "", false); return i + 2;
            case 'W': add(two, "notWord", "", false); return i + 2;
            case 's': add(two, "space", "", false); return i + 2;
            case 'S': add(two, "notSpace", "", false); return i + 2;
            case 'b': add(two, "wordBoundary", "", false); return i + 2;
            case 'B': add(two, "notWordBoundary", "", false); return i + 2;
            case 'A': add(two, "startText", "", false); return i + 2;
            case 'z': add(two, "endText", "", false); return i + 2;
            case 'Z': add(two, "endTextNewline", "", false); return i + 2;
            case 'G': add(two, "unsupported", "contiguous", false); return i + 2;
            case 'K' or 'h' or 'H' or 'R' or 'X' or 'Q' or 'E' or 'N' or 'V' or 'C': add(two, "unsupported", "escape", false); return i + 2;
            case 'p' or 'P':
                {
                    int close = i + 2 < p.Length && p[i + 2] == '{' ? p.IndexOf('}', i + 2) : -1;
                    if (close < 0) { add(two, "unsupported", "escape", false); return i + 2; }
                    add(p[i..(close + 1)], n == 'p' ? "property" : "notProperty", p[(i + 3)..close], false);
                    return close + 1;
                }
            case 'k':
                {
                    int close = i + 2 < p.Length && p[i + 2] is '<' or '\'' ? p.IndexOfAny(['>', '\''], i + 3) : -1;
                    if (close < 0) { add(two, "unsupported", "escape", false); return i + 2; }
                    add(p[i..(close + 1)], "backreference", p[(i + 3)..close], false);
                    return close + 1;
                }
            case 'g': add(two, "unsupported", "escape", false); return i + 2;
            case 'x':
                {
                    int len = Math.Min(2, p.Length - i - 2);
                    add(p.Substring(i, 2 + len), "charCode", p.Substring(i + 2, len), false);
                    return i + 2 + len;
                }
            case 'u':
                {
                    int len = Math.Min(4, p.Length - i - 2);
                    add(p.Substring(i, 2 + len), "charCode", p.Substring(i + 2, len), false);
                    return i + 2 + len;
                }
            case >= '1' and <= '9':
                {
                    int j = i + 1;
                    while (j < p.Length && char.IsAsciiDigit(p[j])) j++;
                    add(p[i..j], "backreference", p[(i + 1)..j], false);
                    return j;
                }
            case '0': add(two, "controlChar", "0", false); return i + 2;
        }
        if (Control.Contains(n.ToString())) add(two, "controlChar", n.ToString(), false);
        else add(two, "literal", n.ToString(), false);
        return i + 2;
    }

    private static int Class(string p, int i, Action<string, string, string, bool> add)
    {
        int j = i + 1;
        bool negated = j < p.Length && p[j] == '^';
        if (negated) j++;
        int bodyStart = j;
        if (j < p.Length && p[j] == ']') j++;
        while (j < p.Length && p[j] != ']')
        {
            if (p[j] == '\\') j++;
            else if (p[j] == '[' && j + 1 < p.Length && p[j + 1] == ':')
            {
                add(p[i..Math.Min(p.Length, j + 2)], "unsupported", "posix", false);
                int end = p.IndexOf(":]", j, StringComparison.Ordinal);
                return end < 0 ? p.Length : p.IndexOf(']', end + 2) is var q and >= 0 ? q + 1 : p.Length;
            }
            j++;
        }
        if (j >= p.Length)
        {
            add(p[i..], "unsupported", "unclosed", false);
            return p.Length;
        }
        add(p[i..(j + 1)], negated ? "negatedClass" : "charClass", p[bodyStart..j], false);
        return j + 1;
    }

    private static int Open(string p, int i, ref int depth, Action flush, List<RegexPart> parts)
    {
        flush();
        string rest = p[i..];
        (string Token, string Kind, string Arg)? found = null;
        if (rest.StartsWith("(?#", StringComparison.Ordinal))
        {
            int end = p.IndexOf(')', i);
            string token = end < 0 ? rest : p[i..(end + 1)];
            parts.Add(new RegexPart(token, "comment", token.Length > 4 ? token[3..^1] : "", Depth: depth));
            return end < 0 ? p.Length : end + 1;
        }
        if (rest.StartsWith("(*", StringComparison.Ordinal)) found = ("(*", "unsupported", "verb");
        else if (rest.StartsWith("(?P<", StringComparison.Ordinal) || rest.StartsWith("(?P=", StringComparison.Ordinal) || rest.StartsWith("(?P>", StringComparison.Ordinal)) found = (rest[..4], "unsupported", "python");
        else if (rest.StartsWith("(?|", StringComparison.Ordinal)) found = ("(?|", "unsupported", "branchReset");
        else if (rest.StartsWith("(?R)", StringComparison.Ordinal) || Regex.IsMatch(rest, @"^\(\?[+-]?\d+\)", RegexOptions.CultureInvariant)) found = (rest[..rest.IndexOf(')')], "unsupported", "recursion");
        else if (rest.StartsWith("(?:", StringComparison.Ordinal)) found = ("(?:", "nonCapturing", "");
        else if (rest.StartsWith("(?=", StringComparison.Ordinal)) found = ("(?=", "lookahead", "");
        else if (rest.StartsWith("(?!", StringComparison.Ordinal)) found = ("(?!", "negativeLookahead", "");
        else if (rest.StartsWith("(?<=", StringComparison.Ordinal)) found = ("(?<=", "lookbehind", "");
        else if (rest.StartsWith("(?<!", StringComparison.Ordinal)) found = ("(?<!", "negativeLookbehind", "");
        else if (rest.StartsWith("(?>", StringComparison.Ordinal)) found = ("(?>", "atomic", "");
        else if (rest.StartsWith("(?<", StringComparison.Ordinal) || rest.StartsWith("(?'", StringComparison.Ordinal))
        {
            int end = p.IndexOfAny(['>', '\''], i + 3);
            if (end > 0) found = (p[i..(end + 1)], "namedGroup", p[(i + 3)..end]);
        }
        else if (Regex.Match(rest, @"^\(\?([imnsx-]+)(\)|:)", RegexOptions.CultureInvariant) is { Success: true } opt)
        {
            if (opt.Groups[2].Value == ")")
            {
                parts.Add(new RegexPart(opt.Value, "inlineOptions", opt.Groups[1].Value, Depth: depth));
                return i + opt.Length;
            }
            found = (opt.Value, "optionGroup", opt.Groups[1].Value);
        }
        if (found is { } f)
        {
            if (f.Kind == "unsupported")
            {
                parts.Add(new RegexPart(f.Token, f.Kind, f.Arg, Depth: depth));
                return i + f.Token.Length;
            }
            parts.Add(new RegexPart(f.Token, f.Kind, f.Arg, Depth: depth));
            depth++;
            return i + f.Token.Length;
        }
        parts.Add(new RegexPart("(", "group", (parts.Count(x => x.Kind == "group") + 1).ToString(), Depth: depth));
        depth++;
        return i + 1;
    }
}
