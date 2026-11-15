using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Syntax;

internal static class StreamingNumberProbe
{
    private const string Pattern = @"(?<![\w.])-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?";
    private const int MaximumCandidate = 64;

    internal static Regex? Create(string pattern, Alt alt)
    {
        if (alt.Kind != AltKind.Match || alt.Owner.Id != "json" || pattern != Pattern
            || !ReferenceEquals(alt.Owner.Registry, BuiltinGrammars.Registry)) return null;
        var options = RegexOptions.CultureInvariant | (alt.Owner.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        return new Regex(pattern, options, TimeSpan.FromSeconds(1));
    }

    internal static bool TryMatch(Regex number, StreamingLineSource source, long at, out bool matched, out long end)
    {
        matched = false;
        end = at;
        if (number.RightToLeft) return false;
        Span<char> probe = stackalloc char[MaximumCandidate + 2];
        int prefix = at > 0 ? 1 : 0;
        if (prefix != 0)
        {
            if (!source.TryReadCached(at - 1, out int previous)) return false;
            probe[0] = (char)previous;
        }
        int length = prefix;
        for (int i = 0; i <= MaximumCandidate; i++)
        {
            if (!source.TryReadCached(at + i, out int value) || value > 127) return false;
            char c = (char)value;
            if (c is >= '0' and <= '9' or '-' or '+' or '.' or 'e' or 'E')
            {
                if (i == MaximumCandidate) return false;
                probe[length++] = c;
                continue;
            }
            // A real delimiter prevents a cache edge from shortening a match that continues in the next window.
            probe[length++] = c;
            var matches = number.EnumerateMatches(probe[..length], prefix);
            matched = matches.MoveNext() && matches.Current.Index == prefix;
            if (matched) end = at + matches.Current.Length;
            return true;
        }
        return false;
    }
}
