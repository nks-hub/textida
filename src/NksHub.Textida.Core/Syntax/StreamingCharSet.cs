using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Syntax;

internal sealed class StreamCharSet
{
    private readonly bool[] ascii = new bool[128];
    private readonly Regex unicode;
    private readonly string expression;

    internal StreamCharSet(string pattern, bool caseSensitive)
    {
        Pattern = pattern;
        CaseSensitive = caseSensitive;
        expression = caseSensitive ? pattern : "(?i:" + pattern + ")";
        unicode = new Regex("\\A(?:" + expression + ")\\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        Span<char> input = stackalloc char[1];
        for (int i = 0; i < ascii.Length; i++)
        {
            input[0] = (char)i;
            ascii[i] = unicode.IsMatch(input);
        }
    }

    internal string Pattern { get; }
    internal bool CaseSensitive { get; }

    internal bool Matches(char value)
    {
        if (value < ascii.Length) return ascii[value];
        Span<char> input = stackalloc char[1];
        input[0] = value;
        return unicode.IsMatch(input);
    }

    public override string ToString() => expression;
}
