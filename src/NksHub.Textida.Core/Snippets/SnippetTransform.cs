using System.Text;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Snippets;

/// <summary>Limits of the snippet parser. The editor and the plugin host pass their own values; <see cref="Fallback"/> is for callers that have none.</summary>
/// <param name="MaxDepth">How many placeholders may stand inside one another; a deeper one stays in the text as it is.</param>
/// <param name="MaxChars">Most UTF-16 units the expanded text (and the text of any placeholder) may have; more is a <see cref="SnippetLimitException"/>.</param>
/// <param name="TransformTimeout">How long one regular expression of a transform may run on one text.</param>
public sealed record SnippetLimits(int MaxDepth, int MaxChars, TimeSpan TransformTimeout)
{
    /// <summary>shortcut: fixed bounds for callers that pass none (tests, previews); the editor takes its numbers from the placeholder class of the app, so change them there.</summary>
    public static SnippetLimits Fallback { get; } = new(16, 64 * 1024, TimeSpan.FromMilliseconds(100));
}

/// <summary>The snippet is too long, or a transform of it ran too long, to be expanded.</summary>
public sealed class SnippetLimitException(string message) : Exception(message);

/// <summary>One piece of the format of a transform: a text, or a group of the match with an optional case change and conditional text.</summary>
internal sealed record FormatPart(string Text = "", int Group = -1, string? Case = null, string? If = null, string? Else = null);

/// <summary>
/// The transform of a snippet, <c>/regex/format/options</c>: what a mirror of a stop (<c>${1/(.*)/${1:/upcase}/}</c>) or a variable shows instead of the plain text.
/// The format has <c>$1</c>, <c>${1}</c>, <c>${1:/upcase}</c> (also <c>downcase</c>, <c>capitalize</c>, <c>camelcase</c>, <c>pascalcase</c>), <c>${1:+if}</c>,
/// <c>${1:?if:else}</c> and <c>${1:-else}</c> (<c>${1:else}</c>); the options are <c>g</c>, <c>i</c>, <c>m</c>, <c>s</c> and <c>u</c>. The regular expression is read by .NET.
/// </summary>
public sealed class SnippetTransform
{
    private readonly Regex _regex;
    private readonly bool _global;
    private readonly IReadOnlyList<FormatPart> _format;

    private SnippetTransform(Regex regex, bool global, IReadOnlyList<FormatPart> format)
    {
        _regex = regex;
        _global = global;
        _format = format;
    }

    internal static SnippetTransform? TryCreate(string pattern, IReadOnlyList<FormatPart> format, string options, TimeSpan timeout)
    {
        var flags = RegexOptions.CultureInvariant;
        bool global = false;
        foreach (char c in options)
        {
            switch (c)
            {
                case 'g': global = true; break;
                case 'i': flags |= RegexOptions.IgnoreCase; break;
                case 'm': flags |= RegexOptions.Multiline; break;
                case 's': flags |= RegexOptions.Singleline; break;
                case 'u': break;
                default: return null;
            }
        }
        try { return new SnippetTransform(new Regex(pattern, flags, timeout), global, format); }
        catch (ArgumentException) { return null; }
    }

    /// <summary>The text with the first match (every match with the <c>g</c> option) replaced by the format. Throws <see cref="RegexMatchTimeoutException"/> when the expression runs too long.</summary>
    public string Apply(string input) => _regex.Replace(input, Format, _global ? -1 : 1);

    private string Format(Match match)
    {
        var sb = new StringBuilder();
        foreach (var part in _format)
        {
            if (part.Group < 0) { sb.Append(part.Text); continue; }
            string value = part.Group < match.Groups.Count && match.Groups[part.Group] is { Success: true } g ? g.Value : "";
            if (value.Length > 0) sb.Append(part.If ?? ChangeCase(value, part.Case));
            else sb.Append(part.Else ?? "");
        }
        return sb.ToString();
    }

    private static string ChangeCase(string value, string? how) => how switch
    {
        "upcase" => value.ToUpperInvariant(),
        "downcase" => value.ToLowerInvariant(),
        "capitalize" => char.ToUpperInvariant(value[0]) + value[1..],
        "camelcase" => Words(value, firstLower: true),
        "pascalcase" => Words(value, firstLower: false),
        _ => value,
    };

    /// <summary>Runs of letters and digits joined with the first letter of each in upper case (the first word in lower case for camel case); a text without any stays as it is.</summary>
    private static string Words(string value, bool firstLower)
    {
        var sb = new StringBuilder(value.Length);
        bool start = true, first = true;
        foreach (char c in value)
        {
            if (!char.IsLetterOrDigit(c)) { start = true; continue; }
            if (start) { sb.Append(first && firstLower ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)); first = false; }
            else sb.Append(c);
            start = false;
        }
        return sb.Length == 0 ? value : sb.ToString();
    }
}
