using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Agents.Hooks;

public enum HookMatchKind { All, Exact, Regex }

/// <summary>
/// The matcher of a hook group as the public documentation describes it. Claude Code: <c>*</c>, an empty or a missing matcher matches everything; a matcher of only letters, digits, <c>_</c>, <c>-</c>,
/// spaces, <c>,</c> and <c>|</c> is an exact value or a list of them; any other matcher is a JavaScript regular expression, tested unanchored. Codex: the matcher is a regular expression, <c>*</c>, an empty
/// or a missing one matches everything. The pattern is run as a .NET regular expression, which reads the usual patterns the same way as JavaScript but not every one.
/// </summary>
public static class HookMatcher
{
    private static bool IsExact(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or ' ' or ',' or '|';

    private static bool IsNarrow(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '|';

    public static HookMatchKind Classify(HookEventInfo ev, string? matcher)
    {
        if (string.IsNullOrEmpty(matcher) || matcher == "*") return HookMatchKind.All;
        if (ev.Product == HookProduct.Codex) return HookMatchKind.Regex;
        Func<char, bool> allowed = ev.NarrowMatcher ? IsNarrow : IsExact;
        return matcher.All(allowed) ? HookMatchKind.Exact : HookMatchKind.Regex;
    }

    /// <summary>
    /// Whether the group fires for <paramref name="value"/>, the value the matcher of this event is compared with; null when the matcher is a regular expression that does not compile or
    /// takes too long. An event without a matcher always fires: a matcher on it is ignored.
    /// </summary>
    public static bool? Matches(HookEventInfo ev, string? matcher, string value, TimeSpan regexTimeout)
    {
        if (ev.MatcherField is null) return true;
        switch (Classify(ev, matcher))
        {
            case HookMatchKind.All:
                return true;
            case HookMatchKind.Exact:
                return matcher!.Split(['|', ','], StringSplitOptions.None).Any(part => part.Trim() == value);
            default:
                try { return Regex.IsMatch(value, matcher!, RegexOptions.CultureInvariant, regexTimeout); }
                catch (Exception e) when (e is ArgumentException or RegexMatchTimeoutException) { return null; }
        }
    }

    /// <summary>True when <paramref name="matcher"/> is a regular expression (see <see cref="Classify"/>) that .NET cannot compile.</summary>
    public static bool IsBrokenPattern(HookEventInfo ev, string? matcher, TimeSpan regexTimeout)
    {
        if (ev.MatcherField is null || Classify(ev, matcher) != HookMatchKind.Regex) return false;
        try
        {
            _ = new Regex(matcher!, RegexOptions.CultureInvariant, regexTimeout);
            return false;
        }
        catch (ArgumentException) { return true; }
    }
}
