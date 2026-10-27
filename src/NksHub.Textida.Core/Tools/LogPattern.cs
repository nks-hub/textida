using System.Globalization;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Tools;

/// <summary>A LOG pattern that is not valid: <see cref="Exception.Message"/> says what and where.</summary>
public sealed class LogPatternException(string message) : Exception(message);

/// <summary>The two ways a LOG pattern is written: the mask of PSPad (<c>* %F %L %C %S %E %D</c>) or a regular expression with the groups <c>file</c>, <c>line</c> and <c>column</c>.</summary>
public enum LogPatternSyntax { Mask, Regex }

/// <summary>Where a line of compiler output points. <paramref name="File"/> is null when the pattern has no file part: the file being compiled is meant.</summary>
public readonly record struct LogLocation(string? File, int Line, int? Column);

/// <summary>One line of a sample as the preview shows it: its text and the place the pattern found in it, or null.</summary>
public readonly record struct LogPreviewLine(int Number, string Text, LogLocation? Location);

/// <summary>
/// Reads the place of an error from one line of tool output. The mask is matched from the left, without backtracking and without regard to case:
/// <list type="bullet">
/// <item><c>*</c> skips any text up to the next literal of the mask (up to the next digit when a number follows);</item>
/// <item><c>%F</c> is the file name, up to the next literal, trimmed; <c>%D</c> is a drive (<c>C:</c> followed by a slash) in front of it;</item>
/// <item><c>%L</c> and <c>%C</c> are the line and the column, digits only; <c>%S</c> skips spaces and tabs;</item>
/// <item><c>%E</c> ends the mask, the rest of the line is ignored (as it is after the last part of the mask anyway).</item>
/// </list>
/// A line is a hit only when a <c>%L</c> was found. A mask without <c>%F</c> names no file.
/// </summary>
public sealed class LogPattern
{
    private enum Kind { Literal, Any, File, Line, Column, Skip, End, Drive }

    private readonly record struct Part(Kind Kind, string Text = "");

    private readonly Part[]? _mask;
    private readonly Regex? _regex;

    private LogPattern(Part[]? mask, Regex? regex) => (_mask, _regex) = (mask, regex);

    public static LogPattern Compile(string pattern, LogPatternSyntax syntax = LogPatternSyntax.Mask) =>
        syntax == LogPatternSyntax.Regex ? CompileRegex(pattern) : CompileMask(pattern);

    private static LogPattern CompileRegex(string pattern)
    {
        Regex regex;
        try { regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); }
        catch (ArgumentException e) { throw new LogPatternException("Invalid regular expression: " + e.Message); }
        if (regex.GroupNumberFromName("line") < 0) throw new LogPatternException("The expression needs a group named 'line'.");
        return new LogPattern(null, regex);
    }

    private static LogPattern CompileMask(string pattern)
    {
        var parts = new List<Part>();
        var literal = new System.Text.StringBuilder();
        void Flush()
        {
            if (literal.Length == 0) return;
            parts.Add(new Part(Kind.Literal, literal.ToString()));
            literal.Clear();
        }
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '*') { Flush(); parts.Add(new Part(Kind.Any)); }
            else if (c == '%')
            {
                if (i + 1 >= pattern.Length || char.ToUpperInvariant(pattern[i + 1]) is not ('F' or 'L' or 'C' or 'S' or 'E' or 'D'))
                    throw new LogPatternException($"Unknown '%' code at position {i + 1}: use %F %L %C %S %E or %D.");
                Flush();
                parts.Add(new Part(char.ToUpperInvariant(pattern[++i]) switch
                {
                    'F' => Kind.File, 'L' => Kind.Line, 'C' => Kind.Column, 'S' => Kind.Skip, 'D' => Kind.Drive, _ => Kind.End,
                }));
            }
            else literal.Append(c);
        }
        Flush();
        if (!parts.Any(p => p.Kind == Kind.Line)) throw new LogPatternException("The mask needs %L for the line number.");
        return new LogPattern([.. parts], null);
    }

    /// <summary>The place named in <paramref name="line"/>, or null when the pattern does not fit it.</summary>
    public LogLocation? Match(string line) => _regex is not null ? MatchRegex(line) : MatchMask(line);

    /// <summary>Every line of <paramref name="sample"/> with the place found in it; this is what the preview of a pattern shows.</summary>
    public IReadOnlyList<LogPreviewLine> Preview(string sample)
    {
        var result = new List<LogPreviewLine>();
        int number = 0;
        foreach (var line in sample.ReplaceLineEndings("\n").Split('\n'))
            result.Add(new LogPreviewLine(++number, line, Match(line)));
        return result;
    }

    private LogLocation? MatchRegex(string line)
    {
        Match m;
        try { m = _regex!.Match(line); }
        catch (RegexMatchTimeoutException) { return null; }
        if (!m.Success || !TryNumber(m.Groups["line"].Value, out int ln)) return null;
        int? column = m.Groups["column"] is { Success: true } g && TryNumber(g.Value, out int col) ? col : null;
        string? file = m.Groups["file"] is { Success: true } f && f.Value.Trim() is { Length: > 0 } name ? name : null;
        return new LogLocation(file, ln, column);
    }

    private static bool TryNumber(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;

    private LogLocation? MatchMask(string line)
    {
        var mask = _mask!;
        int p = 0;
        string disk = "";
        string? file = null;
        int ln = 0;
        int? column = null;
        for (int t = 0; t < mask.Length; t++)
        {
            var part = mask[t];
            var next = t + 1 < mask.Length ? mask[t + 1] : (Part?)null;
            switch (part.Kind)
            {
                case Kind.Literal:
                    if (!line.AsSpan(p).StartsWith(part.Text, StringComparison.OrdinalIgnoreCase)) return null;
                    p += part.Text.Length;
                    break;
                case Kind.Any:
                    int to = Until(line, p, next, toEndWhenFree: next is null);
                    if (to < 0) return null;
                    p = to;
                    break;
                case Kind.Drive:
                    if (p + 2 < line.Length && char.IsAsciiLetter(line[p]) && line[p + 1] == ':' && line[p + 2] is '\\' or '/')
                    {
                        disk = line.Substring(p, 2);
                        p += 2;
                    }
                    break;
                case Kind.File:
                    int end = Until(line, p, next, toEndWhenFree: true);
                    if (end < 0) return null;
                    string name = line[p..end].Trim();
                    if (name.Length == 0) return null;
                    file = disk + name;
                    p = end;
                    break;
                case Kind.Line:
                case Kind.Column:
                    int start = p;
                    while (p < line.Length && char.IsAsciiDigit(line[p])) p++;
                    if (p == start || !TryNumber(line.AsSpan(start, p - start).ToString(), out int number)) return null;
                    if (part.Kind == Kind.Line) ln = number;
                    else column = number;
                    break;
                case Kind.Skip:
                    while (p < line.Length && line[p] is ' ' or '\t') p++;
                    break;
                case Kind.End:
                    t = mask.Length;
                    break;
            }
        }
        return ln > 0 ? new LogLocation(file, ln, column) : null;
    }

    /// <summary>Where the text covered by a * or %F stops: before the next literal, before the next number, or (nothing to stop at) the end of the line or here.</summary>
    private static int Until(string line, int from, Part? next, bool toEndWhenFree)
    {
        switch (next?.Kind)
        {
            case Kind.Literal:
                return line.IndexOf(next.Value.Text, from, StringComparison.OrdinalIgnoreCase);
            case Kind.Line or Kind.Column:
                for (int i = from; i < line.Length; i++) if (char.IsAsciiDigit(line[i])) return i;
                return -1;
            default:
                return toEndWhenFree ? line.Length : from;
        }
    }
}
