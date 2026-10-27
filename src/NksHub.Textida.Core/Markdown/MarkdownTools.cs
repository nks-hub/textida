using System.Text;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Markdown;

/// <summary>A heading of a Markdown file: its level (1 to 6), text and zero-based line.</summary>
public sealed record MarkdownHeading(int Level, string Title, int Line);

/// <summary>
/// The text work of the Markdown commands (P9.3.3) on a list of lines: the headings, the table around a line and its alignment, and the table of contents between
/// <c>&lt;!-- TOC --&gt;</c> and <c>&lt;!-- /TOC --&gt;</c>. Only ATX headings (<c>#</c>) count; the width of a cell is its display width (wide East Asian characters count 2).
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

    /// <summary>One cell of a table row: its text, where the trimmed text starts in the row (an empty cell starts after one space) and the position after the pipe in front of it.</summary>
    public sealed record CellSpan(string Text, int Start, int Length, int RawStart);

    /// <summary>The cells of a row with their places. A pipe splits cells unless it is escaped (<c>\|</c>) or inside an inline code span.</summary>
    public static List<CellSpan> CellSpans(string row)
    {
        var spans = new List<CellSpan>();
        int end = row.TrimEnd().Length, i = row.Length - row.TrimStart().Length, cellStart;
        if (i < end && row[i] == '|') i++;
        cellStart = i;

        void Add(int from, int to)
        {
            int s = from, e = to;
            while (s < e && char.IsWhiteSpace(row[s])) s++;
            while (e > s && char.IsWhiteSpace(row[e - 1])) e--;
            spans.Add(s == e ? new CellSpan("", Math.Min(from + 1, to), 0, spans.Count == 0 ? 0 : from) : new CellSpan(row[s..e], s, e - s, spans.Count == 0 ? 0 : from));
        }

        while (i < end)
        {
            char ch = row[i];
            if (ch == '\\' &&i + 1 < end) i += 2;
            else if (ch == '`')
            {
                int run = 1;
                while (i + run < end && row[i + run] == '`') run++;
                int close = ClosingRun(row, i + run, end, run);
                i = close < 0 ? i + run : close + run;
            }
            else if (ch == '|') { Add(cellStart, i); cellStart = ++i; }
            else i++;
        }
        if (cellStart < end || spans.Count == 0) Add(cellStart, end);
        return spans;
    }

    private static int ClosingRun(string text, int from, int end, int length)
    {
        for (int j = from; j < end;)
        {
            if (text[j] != '`') { j++; continue; }
            int run = 1;
            while (j + run < end && text[j + run] == '`') run++;
            if (run == length) return j;
            j += run;
        }
        return -1;
    }

    /// <summary>The cells of a table row, without the outer pipes; <c>\|</c> stays inside its cell.</summary>
    public static List<string> Cells(string row) => [.. CellSpans(row).Select(c => c.Text)];

    /// <summary>The number of columns a text takes in a monospaced font: 2 for East Asian wide characters and emoji, 0 for combining marks and zero-width characters.</summary>
    public static int DisplayWidth(string text)
    {
        int width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            int v = rune.Value;
            if (v < 0x300) { width += v < 32 || (v >= 0x7F && v < 0xA0) ? 0 : 1; continue; }
            var category = Rune.GetUnicodeCategory(rune);
            if (category is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.EnclosingMark or System.Globalization.UnicodeCategory.Format) continue;
            width += IsWide(v) ? 2 : 1;
        }
        return width;
    }

    private static bool IsWide(int v) =>
        v is >= 0x1100 and <= 0x115F or >= 0x2E80 and <= 0x303E or >= 0x3041 and <= 0x33FF or >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF
            or >= 0xA000 and <= 0xA4CF or >= 0xAC00 and <= 0xD7A3 or >= 0xF900 and <= 0xFAFF or >= 0xFE30 and <= 0xFE6F or >= 0xFF00 and <= 0xFF60
            or >= 0xFFE0 and <= 0xFFE6 or >= 0x1F300 and <= 0x1F64F or >= 0x1F900 and <= 0x1F9FF or >= 0x20000 and <= 0x3FFFD;

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
            widths[c] = Math.Max(3, table.Where((r, i) => i != 1 && c < r.Count).Select(r => DisplayWidth(r[c])).DefaultIfEmpty(0).Max());
        string Pad(string text, int c)
        {
            int gap = Math.Max(0, widths[c] - DisplayWidth(text));
            return marks[c] switch { "R" => new string(' ', gap) + text, "C" => new string(' ', gap / 2) + text + new string(' ', gap - gap / 2), _ => text + new string(' ', gap) };
        }
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

    /// <summary>A rewrite of the table lines <see cref="First"/> to <see cref="First"/> + <see cref="Count"/> - 1 by <see cref="Lines"/>, and the text to select afterwards (<see cref="CaretRow"/> is an index into <see cref="Lines"/>).</summary>
    public sealed record TableEdit(int First, int Count, IReadOnlyList<string> Lines, int CaretRow, int CaretStart, int CaretLength);

    private static List<string> Slice(IReadOnlyList<string> lines, int first, int last)
    {
        var rows = new List<string>(last - first + 1);
        for (int i = first; i <= last; i++) rows.Add(lines[i]);
        return rows;
    }

    private static TableEdit Edit(int first, int count, List<string> rows, int caretRow, int caretCell)
    {
        var formatted = FormatTable(rows);
        var cell = CellSpans(formatted[caretRow])[caretCell];
        return new TableEdit(first, count, formatted, caretRow, cell.Start, cell.Length);
    }

    /// <summary>Tab (forward) or Shift+Tab: realigns the table and selects the next or previous cell, skipping the row of dashes; Tab in the last cell adds a row. Null when the line is in no table.</summary>
    public static TableEdit? MoveCell(IReadOnlyList<string> lines, int line, int column, bool forward)
    {
        if (TableAround(lines, line) is not { } table) return null;
        var rows = Slice(lines, table.First, table.Last);
        int row = line - table.First, columns = rows.Max(r => CellSpans(r).Count);
        int cell = row == 1 ? (forward ? columns - 1 : 0) : CellAt(CellSpans(rows[row]), column);
        if (forward)
        {
            if (++cell >= columns) { cell = 0; row += row == 0 ? 2 : 1; }
            if (row >= rows.Count) rows.Add("|");
        }
        else if (--cell < 0)
        {
            cell = columns - 1;
            row -= row == 2 ? 2 : 1;
            if (row < 0) (row, cell) = (0, 0);
        }
        return Edit(table.First, table.Last - table.First + 1, rows, row, cell);
    }

    /// <summary>Shift+Enter: an empty row below the line (below the row of dashes when the line is the header), the first cell selected. Null when the line is in no table.</summary>
    public static TableEdit? NewRow(IReadOnlyList<string> lines, int line)
    {
        if (TableAround(lines, line) is not { } table) return null;
        var rows = Slice(lines, table.First, table.Last);
        int at = Math.Max(line - table.First + 1, 2);
        rows.Insert(at, "|");
        return Edit(table.First, table.Last - table.First + 1, rows, at, 0);
    }

    /// <summary>The block of lines with pipes around the line, whether or not it is a complete table; null when the line has no pipe.</summary>
    public static (int First, int Last)? TableBlock(IReadOnlyList<string> lines, int line)
    {
        bool Row(int i) => i >= 0 && i < lines.Count && lines[i].Contains('|') && lines[i].Trim().Length > 0;
        if (!Row(line)) return null;
        int first = line, last = line;
        while (Row(first - 1)) first--;
        while (Row(last + 1)) last++;
        return (first, last);
    }

    /// <summary>Repairs the table block around the line: adds a missing row of dashes (and an empty header when the block starts with one), drops stray rows of dashes, fills short rows and aligns the columns. Null when the line is in no block.</summary>
    public static TableEdit? Repair(IReadOnlyList<string> lines, int line)
    {
        if (TableBlock(lines, line) is not { } block) return null;
        var rows = Slice(lines, block.First, block.Last);
        int row = line - block.First;
        if (IsSeparator(rows[0])) { rows.Insert(0, "|"); row++; }
        else if (rows.Count < 2 || !IsSeparator(rows[1]))
        {
            rows.Insert(1, "|" + string.Concat(Enumerable.Repeat(" --- |", Math.Max(1, CellSpans(rows[0]).Count))));
            if (row >= 1) row++;
        }
        for (int i = rows.Count - 1; i > 1; i--)
            if (IsSeparator(rows[i])) { rows.RemoveAt(i); if (row >= i) row--; }
        return Edit(block.First, block.Last - block.First + 1, rows, Math.Clamp(row, 0, rows.Count - 1), 0);
    }

    private static int CellAt(List<CellSpan> spans, int column)
    {
        int cell = 0;
        for (int i = 0; i < spans.Count; i++)
            if (spans[i].RawStart <= column) cell = i;
        return cell;
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
