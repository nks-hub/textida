using System.Text;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Search;

/// <summary>
/// The rules of one ignore file in gitignore syntax: blank lines and <c>#</c> comments, <c>!</c> negation, a trailing <c>/</c>
/// for directories only, a slash inside the pattern anchors it to the directory of the file, otherwise it matches at any depth,
/// <c>*</c>, <c>?</c>, <c>[...]</c> and <c>**</c> (leading <c>**/</c>, trailing <c>/**</c>, inner <c>/**/</c>). The last matching rule wins.
/// </summary>
public sealed class IgnoreRules
{
    private sealed record Rule(Regex Pattern, bool Negate, bool DirectoryOnly);

    private readonly Rule[] _rules;

    private IgnoreRules(Rule[] rules) => _rules = rules;

    public int Count => _rules.Length;

    public static IgnoreRules Parse(IEnumerable<string> lines, bool ignoreCase = false)
    {
        var rules = new List<Rule>();
        foreach (var raw in lines)
            if (ParseLine(raw, ignoreCase) is { } rule) rules.Add(rule);
        return new IgnoreRules([.. rules]);
    }

    /// <summary>Reads an ignore file as UTF-8; null when it cannot be read.</summary>
    public static IgnoreRules? TryLoad(string path, bool ignoreCase = false)
    {
        try
        {
            return Parse(File.ReadAllLines(path, new UTF8Encoding(false)), ignoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Verdict for a path relative to the directory of the ignore file, using '/' separators:
    /// true ignored, false re-included by a negation, null when no rule matches.
    /// </summary>
    public bool? Evaluate(string relativePath, bool isDirectory, bool? current = null)
    {
        var result = current;
        foreach (var rule in _rules)
        {
            if (rule.DirectoryOnly && !isDirectory) continue;
            if (rule.Pattern.IsMatch(relativePath)) result = !rule.Negate;
        }
        return result;
    }

    private static Rule? ParseLine(string raw, bool ignoreCase)
    {
        string line = raw.TrimEnd('\r', '\n');
        int end = line.Length;
        while (end > 0 && line[end - 1] == ' ' && !(end > 1 && line[end - 2] == '\\')) end--;
        line = line[..end];
        if (line.Length == 0 || line[0] == '#') return null;

        bool negate = false;
        if (line[0] == '!')
        {
            negate = true;
            line = line[1..];
        }
        bool dirOnly = false;
        if (line.Length > 1 && line[^1] == '/' && line[^2] != '\\')
        {
            dirOnly = true;
            line = line[..^1];
        }
        if (line.Length == 0) return null;

        bool anchored = line.Contains('/');
        if (line[0] == '/') line = line[1..];
        if (line.Length == 0) return null;

        string body = Translate(line);
        string regex = anchored ? $"^{body}$" : $"^(?:.*/)?{body}$";
        var options = RegexOptions.CultureInvariant | RegexOptions.Singleline | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        return new Rule(new Regex(regex, options), negate, dirOnly);
    }

    private static string Translate(string glob)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < glob.Length; i++)
        {
            char c = glob[i];
            switch (c)
            {
                case '\\' when i + 1 < glob.Length:
                    sb.Append(Regex.Escape(glob[++i].ToString()));
                    break;
                case '*':
                    if (i + 1 < glob.Length && glob[i + 1] == '*')
                    {
                        bool atStart = i == 0 || glob[i - 1] == '/';
                        int after = i + 2;
                        bool slashAfter = after < glob.Length && glob[after] == '/';
                        bool atEnd = after == glob.Length;
                        if (atStart && slashAfter)
                        {
                            sb.Append("(?:.*/)?");
                            i = after;
                            break;
                        }
                        if (atStart && atEnd)
                        {
                            sb.Append(".*");
                            i = after - 1;
                            break;
                        }
                        // "/**" at the end matches everything inside, but not the directory itself
                        if (i > 0 && glob[i - 1] == '/' && atEnd)
                        {
                            sb.Append(".+");
                            i = after - 1;
                            break;
                        }
                        while (i + 1 < glob.Length && glob[i + 1] == '*') i++;
                    }
                    sb.Append("[^/]*");
                    break;
                case '?':
                    sb.Append("[^/]");
                    break;
                case '[':
                    if (TryClass(glob, i, out string cls, out int next))
                    {
                        sb.Append(cls);
                        i = next;
                    }
                    else sb.Append(@"\[");
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
        return sb.ToString();
    }

    private static bool TryClass(string glob, int open, out string regex, out int close)
    {
        regex = "";
        close = open;
        int i = open + 1;
        bool negate = i < glob.Length && glob[i] is '!' or '^';
        if (negate) i++;
        var sb = new StringBuilder(negate ? "[^/" : "[");
        int first = i;
        for (; i < glob.Length; i++)
        {
            char c = glob[i];
            if (c == ']' && i > first)
            {
                regex = sb.Append(']').ToString();
                close = i;
                return true;
            }
            if (c == '[' && i + 1 < glob.Length && glob[i + 1] == ':')
            {
                int endName = glob.IndexOf(":]", i + 2, StringComparison.Ordinal);
                if (endName > 0 && PosixClass(glob[(i + 2)..endName]) is { } posix)
                {
                    sb.Append(posix);
                    i = endName + 1;
                    continue;
                }
            }
            if (c == '\\' && i + 1 < glob.Length) c = glob[++i];
            if (c is '\\' or '[' or ']' or '^') sb.Append('\\');
            sb.Append(c);
        }
        return false;
    }

    private static string? PosixClass(string name) => name switch
    {
        "alpha" => "a-zA-Z",
        "digit" => "0-9",
        "alnum" => "a-zA-Z0-9",
        "upper" => "A-Z",
        "lower" => "a-z",
        "space" => @"\s",
        "punct" => @"!-/:-@\[-`{-~",
        "xdigit" => "0-9A-Fa-f",
        "blank" => @" \t",
        _ => null,
    };
}
