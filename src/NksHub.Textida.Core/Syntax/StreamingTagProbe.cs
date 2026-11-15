using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Syntax;

internal static class StreamingTagProbe
{
    private const int MaximumCandidate = 256;

    internal static Regex? Create(string pattern, Alt alt)
    {
        if (alt.Owner.Id != "xml" || !ReferenceEquals(alt.Owner.Registry, BuiltinGrammars.Registry)) return null;
        bool bounded = pattern is "<!--" or "-->" or @"<!\[CDATA\[" or @"\]\]>" or @"<\?" or @"\?>"
            or @"<!DOCTYPE\b" or ">" or @"</[A-Za-z_:][\w:.-]*\s*>" or @"<[A-Za-z_:][\w:.-]*" or "/?>";
        if (!bounded) return null;
        var options = RegexOptions.CultureInvariant | (alt.Owner.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        return new Regex(@"\A(?:" + pattern + ")", options, TimeSpan.FromSeconds(1));
    }

    internal static bool TryMatch(Regex tag, StreamingLineSource source, long at, out bool matched, out long end)
    {
        matched = false;
        end = at;
        var cached = source.CachedSpan(at);
        int stop = cached[..Math.Min(cached.Length, MaximumCandidate)].IndexOf('>');
        // These rules cannot consume past '>'. A real delimiter keeps a cache edge from ending a name or lookahead.
        if (stop < 0) return false;
        var matches = tag.EnumerateMatches(cached[..(stop + 1)]);
        matched = matches.MoveNext();
        if (matched) end = at + matches.Current.Length;
        return true;
    }
}
