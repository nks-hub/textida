using System.Text;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Markdown;

/// <summary>A heading of a Markdown file: its level (1 to 6), text and zero-based line.</summary>
public sealed record MarkdownHeading(int Level, string Title, int Line);

/// <summary>
/// The text work of the Markdown commands (P9.3.3) on a list of lines: the headings, the table around a line and its alignment, and the table of contents between
/// <c>&lt;!-- TOC --&gt;</c> and <c>&lt;!-- /TOC --&gt;</c>. Only ATX headings (<c>#</c>) count; the width of a cell is its number of UTF-16 units, so wide characters are not measured.
/// </summary>
public static partial class MarkdownTools
{
    public const string TocOpen = "<!-- TOC -->";
    public const string TocClose = "<!-- /TOC -->";

    [GeneratedRegex(@"^ {0,3}(#{1,6})\s+(.*?)\s*#*\s*$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^\s*\|?\s*:?-{1,}:?\s*(\|\s*:?-{1,}:?\s*)*\|?\s*$")]
    private static partial Regex SeparatorLine();

    private static bool Fence(string line) => line.TrimStart().StartsWith("```", StringComparison.Ordinal) || line.TrimStart().StartsWith("~~~", StringComparison.Ordinal);

    /// <summary>The headings outside fenced code, in document order.</summary>
    public static IReadOnlyList<MarkdownHeading> Headings(IReadOnlyList<string> lines)
    {
        var found = new List<MarkdownHeading>();
        bool fenced = false;
        for (int i = 0; i < lines.Count; i++)
        {
            if (Fence(lines[i])) { fenced = !fenced; continue; }
            if (!fenced && HeadingLine().Match(lines[i]) is { Success: true } m && m.Groups[2].Value.Length > 0)
                found.Add(new MarkdownHeading(m.Groups[1].Length, m.Groups[2].Value, i));
        }
        return found;
    }

    // ---- tables ----

    /// <summary>True for the row of dashes under the header of a table, such as <c>|:--|--:|</c>.</summary>
    public static bool IsSeparator(string line) => line.Contains('-') && SeparatorLine().IsMatch(line);

    /// <summary>The cells of a table row, without the outer pipes; <c>\|</c> stays inside its cell.</summary>
    public static List<string> Cells(string row)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        string s = row.Trim();
        if (s.StartsWith('|')) s = s[1..];
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length) { cell.Append(s[i]).Append(s[++i]); }
            else if (s[i] == '|') { cells.Add(cell.ToString().Trim()); cell.Clear(); }
            else cell.Append(s[i]);
        }
        if (cell.ToString().Trim().Length > 0 || cells.Count == 0 || !row.TrimEnd().EndsWith('|')) cells.Add(cell.ToString().Trim());
        return cells;
    }

    /// <summary>The first and last line of the table that holds <paramref name="line"/> (a header row, a separator row of dashes and the rows after it); null when the line is in none.</summary>
    public static (int First, int Last)? TableAround(IReadOnlyList<string> lines, int line)
    {
        bool Row(int i) => i >= 0 && i < lines.Count && lines[i].Contains('|') && lines[i].Trim().Length > 0;
        if (!Row(line)) return null;
        int first = line, last = line;
        while (Row(first - 1)) first--;
        while (Row(last + 1)) last++;
        for (int head = first; head < last; head++)
            if (SeparatorLine().IsMatch(lines[head + 1]) && lines[head + 1].Contains('-') && !SeparatorLine().IsMatch(lines[head]) && line >= head) return (head, last);
        return null;
    }

    /// <summary>The lines of the table rewritten with every column as wide as its widest cell, the alignment marks kept and short rows filled with empty cells.</summary>
    public static IReadOnlyList<string> FormatTable(IReadOnlyList<string> rows)
    {
        var table = rows.Select(Cells).ToList();
        int columns = table.Max(r => r.Count);
        var marks = new string[columns];
        for (int c = 0; c < columns; c++)
        {
            string sep = c < table[1].Count ? table[1][c] : "";
            marks[c] = (sep.StartsWith(':'), sep.EndsWith(':')) switch { (true, true) => "C", (true, false) => "L", (false, true) => "R", _ => "N" };
        }
        var widths = new int[columns];
        for (int c = 0; c < columns; c++)
            widths[c] = Math.Max(3, table.Where((r, i) => i != 1 && c < r.Count).Select(r => r[c].Length).DefaultIfEmpty(0).Max());
        string Pad(string text, int c) => marks[c] switch { "R" => text.PadLeft(widths[c]), "C" => text.PadLeft((widths[c] + text.Length) / 2).PadRight(widths[c]), _ => text.PadRight(widths[c]) };
        var result = new List<string>();
        for (int r = 0; r < table.Count; r++)
        {
            var cells = Enumerable.Range(0, columns).Select(c =>
            {
                if (r != 1) return Pad(c < table[r].Count ? table[r][c] : "", c);
                int w = widths[c];
                return marks[c] switch { "L" => ":" + new string('-', w - 1), "R" => new string('-', w - 1) + ":", "C" => ":" + new string('-', w - 2) + ":", _ => new string('-', w) };
            });
            result.Add("| " + string.Join(" | ", cells) + " |");
        }
        return result;
    }

    // ---- table of contents ----

    /// <summary>The lines of the open and close marker of the first table of contents block; null when there is none.</summary>
    public static (int Open, int Close)? TocBlock(IReadOnlyList<string> lines)
    {
        int open = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            string t = lines[i].Trim();
            if (open < 0 && t.Equals(TocOpen, StringComparison.OrdinalIgnoreCase)) open = i;
            else if (open >= 0 && t.Equals(TocClose, StringComparison.OrdinalIgnoreCase)) return (open, i);
        }
        return null;
    }

    /// <summary>The anchor a heading gets (lower case, punctuation dropped, spaces as hyphens); a title seen before gets <c>-1</c>, <c>-2</c>… as on the web.</summary>
    public static string Slug(string title, Dictionary<string, int> seen)
    {
        var sb = new StringBuilder();
        foreach (char c in title.ToLowerInvariant())
            if (char.IsLetterOrDigit(c) || c is '-' or '_') sb.Append(c);
            else if (c == ' ') sb.Append('-');
        string slug = sb.ToString();
        seen.TryGetValue(slug, out int n);
        seen[slug] = n + 1;
        return n == 0 ? slug : $"{slug}-{n}";
    }

    /// <summary>The list of links to the headings, nested two spaces per level below the highest one; the table of contents block itself is not listed.</summary>
    public static IReadOnlyList<string> TocLines(IReadOnlyList<MarkdownHeading> headings)
    {
        if (headings.Count == 0) return [];
        int top = headings.Min(h => h.Level);
        var seen = new Dictionary<string, int>();
        return [.. headings.Select(h => $"{new string(' ', (h.Level - top) * 2)}- [{h.Title}](#{Slug(h.Title, seen)})")];
    }
}
