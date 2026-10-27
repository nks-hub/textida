using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NksHub.Textida.AvaloniaEditAdapter;

/// <summary>What the lines are compared by when they are sorted.</summary>
public enum SortMethod
{
    /// <summary>Code unit order of the characters.</summary>
    Ordinal,
    /// <summary>The collation rules of a culture, given as a language tag or a hexadecimal locale id.</summary>
    Locale,
    /// <summary>The number the line (or its column range) starts with; lines without one come first.</summary>
    Numeric,
}

/// <summary>The rules of one sort. <see cref="ColumnFrom"/> and <see cref="ColumnTo"/> are 1-based columns; 0 means the whole line (and for <see cref="ColumnTo"/> the end of the line).</summary>
public sealed record SortOptions(
    bool Descending = false,
    bool CaseSensitive = false,
    bool RemoveDuplicates = false,
    SortMethod Method = SortMethod.Ordinal,
    string Locale = "",
    int ColumnFrom = 0,
    int ColumnTo = 0)
{
    /// <summary>The culture for <see cref="SortMethod.Locale"/>; null when <see cref="Locale"/> names none. An empty name means the invariant culture.</summary>
    public CultureInfo? Culture
    {
        get
        {
            string name = Locale.Trim();
            if (name.Length == 0) return CultureInfo.InvariantCulture;
            try
            {
                if (name.Length <= 4 && int.TryParse(name, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int lcid)) return CultureInfo.GetCultureInfo(lcid);
                return CultureInfo.GetCultureInfo(name, predefinedOnly: true);
            }
            catch (CultureNotFoundException) { return null; }
        }
    }
}

/// <summary>How lines are aligned inside a block.</summary>
public enum Justification { Left, Right, Center, Block }

/// <summary>Plain text work on lists of lines, with no document and no selection: sorting, line ranges, alignment and paragraph wrapping.</summary>
public static partial class LineText
{
    // ---- sort ----

    /// <summary>The lines in the order <paramref name="options"/> asks for. The sort is stable; duplicates (whole lines) are dropped after sorting, the first one stays.</summary>
    public static List<string> Sort(IReadOnlyList<string> lines, SortOptions options)
    {
        var compare = Comparison(options);
        string Key(string line) => ColumnKey(line, options);
        var ordered = (options.Descending ? lines.OrderByDescending(Key, compare) : lines.OrderBy(Key, compare)).ToList();
        if (!options.RemoveDuplicates) return ordered;
        var seen = new HashSet<string>(options.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        return [.. ordered.Where(seen.Add)];
    }

    private static string ColumnKey(string line, SortOptions o)
    {
        if (o.ColumnFrom <= 0 && o.ColumnTo <= 0) return line;
        int from = Math.Clamp(o.ColumnFrom - 1, 0, line.Length);
        int to = o.ColumnTo > 0 ? Math.Clamp(o.ColumnTo, from, line.Length) : line.Length;
        return line[from..to];
    }

    private static IComparer<string> Comparison(SortOptions o) => o.Method switch
    {
        SortMethod.Numeric => Comparer<string>.Create((a, b) =>
        {
            var (x, y) = (LeadingNumber(a), LeadingNumber(b));
            if (x is null || y is null) return x is null && y is null ? string.Compare(a, b, o.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) : x is null ? -1 : 1;
            return x.Value.CompareTo(y.Value);
        }),
        SortMethod.Locale => Comparer<string>.Create((a, b) =>
            (o.Culture ?? CultureInfo.InvariantCulture).CompareInfo.Compare(a, b, o.CaseSensitive ? CompareOptions.None : CompareOptions.IgnoreCase)),
        _ => o.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase,
    };

    [GeneratedRegex(@"^\s*[+-]?(\d+(\.\d+)?|\.\d+)")]
    private static partial Regex NumberAtStart();

    private static double? LeadingNumber(string text)
    {
        var m = NumberAtStart().Match(text);
        return m.Success && double.TryParse(m.Value,NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
    }

    // ---- line ranges ----

    /// <summary>
    /// Reads "10-20, 40-50" or "7": numbers or ranges (hyphen or en dash) separated by commas, semicolons or blanks. Ranges are 1-based, overlapping
    /// or touching ones are merged and the result is in ascending order. False when anything else is in the text, a number is 0 or a range runs backwards.
    /// </summary>
    public static bool TryParseRanges(string text, out List<(int From, int To)> ranges)
    {
        ranges = [];
        int at = 0;
        var found = new List<(int From, int To)>();
        foreach (Match m in RangeItem().Matches(text))
        {
            if (!SeparatorsOnly(text.AsSpan(at, m.Index - at))) return false;
            at = m.Index + m.Length;
            if (!int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int from) || from < 1) return false;
            int to = from;
            if (m.Groups[2].Success && (!int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out to) || to < from)) return false;
            found.Add((from, to));
        }
        if (found.Count == 0 || !SeparatorsOnly(text.AsSpan(at))) return false;
        foreach (var r in found.OrderBy(r => r.From))
        {
            if (ranges.Count > 0 && r.From <= ranges[^1].To + 1) ranges[^1] = (ranges[^1].From, Math.Max(ranges[^1].To, r.To));
            else ranges.Add(r);
        }
        return true;
    }

    [GeneratedRegex(@"(\d+)(?:\s*[-–]\s*(\d+))?")]
    private static partial Regex RangeItem();

    private static bool SeparatorsOnly(ReadOnlySpan<char> s)
    {
        foreach (char c in s)
            if (c is not (',' or ';' or ' ' or '\t')) return false;
        return true;
    }

    // ---- alignment ----

    /// <summary>
    /// Aligns the lines to the width of the longest one (leading and trailing blanks removed first). Blank lines become empty. Block spreads the
    /// blanks between the words until the line is as wide as the longest; a line of one word stays as it is.
    /// </summary>
    public static List<string> Justify(IReadOnlyList<string> lines, Justification mode)
    {
        var trimmed = lines.Select(l => l.Trim(' ', '\t')).ToList();
        int width = trimmed.Count == 0 ? 0 : trimmed.Max(l => l.Length);
        return [.. trimmed.Select(l => l.Length == 0 ? l : mode switch
        {
            Justification.Right => l.PadLeft(width),
            Justification.Center => new string(' ', (width - l.Length) / 2) + l,
            Justification.Block => Spread(l, width),
            _ => l,
        })];
    }

    private static string Spread(string line, int width)
    {
        var words = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        int gaps = words.Length - 1;
        int extra = width - words.Sum(w => w.Length);
        if (gaps < 1) return line;
        var sb = new StringBuilder(words[0]);
        for (int i = 1; i < words.Length; i++) sb.Append(' ', extra / gaps + (i <= extra % gaps ? 1 : 0)).Append(words[i]);
        return sb.ToString();
    }

    // ---- paragraphs ----

    /// <summary>
    /// Wraps each paragraph (lines between blank lines) at <paramref name="width"/> columns: the words are joined with single blanks and every line
    /// of the paragraph gets the indentation of its first line. A word longer than the width stays on a line of its own. Blank lines are kept.
    /// A tab in the indentation counts as one column. With <paramref name="shortLine"/> above 0 a line shorter than that (see <see cref="IsShort"/>)
    /// also ends its paragraph.
    /// </summary>
    public static List<string> Reformat(IReadOnlyList<string> lines, int width, int shortLine = 0)
    {
        var result = new List<string>();
        var paragraph = new List<string>();
        void Flush()
        {
            if (paragraph.Count == 0) return;
            string first = paragraph[0];
            string indent = first[..(first.Length - first.TrimStart(' ', '\t').Length)];
            var current = new StringBuilder(indent);
            bool empty = true;
            foreach (var word in paragraph.SelectMany(p => p.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)))
            {
                if (!empty && current.Length + 1 + word.Length > width)
                {
                    result.Add(current.ToString());
                    current.Clear().Append(indent);
                    empty = true;
                }
                if (!empty) current.Append(' ');
                current.Append(word);
                empty = false;
            }
            result.Add(current.ToString());
            paragraph.Clear();
        }
        foreach (var line in lines)
        {
            if (line.AsSpan().Trim(" \t").Length == 0) { Flush(); result.Add(""); }
            else
            {
                paragraph.Add(line);
                if (IsShort(line, shortLine)) Flush();
            }
        }
        Flush();
        return result;
    }

    /// <summary>True when <paramref name="shortLine"/> is above 0 and the line without its trailing blanks has fewer characters than that.</summary>
    public static bool IsShort(string line, int shortLine) => shortLine > 0 && line.AsSpan().TrimEnd(" \t").Length < shortLine;
}
