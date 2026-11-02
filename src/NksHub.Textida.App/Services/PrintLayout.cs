using System.Text;

namespace NksHub.Textida.App.Services;

/// <summary>A stretch of one printed line in one colour (<see cref="Rgb"/> is 0xRRGGBB).</summary>
internal readonly record struct PrintRun(string Text, int Rgb);

/// <summary>One printed row: the runs of text and, on the first row of a source line, its number (a wrapped continuation has none).</summary>
internal sealed record PrintLine(int? Number, IReadOnlyList<PrintRun> Runs);

internal sealed record PrintPage(int Number, IReadOnlyList<PrintLine> Lines);

/// <summary>How many characters and rows fit on a page, whether the line numbers are printed and the width of a tab.</summary>
internal sealed record PrintOptions(int Columns, int Rows, bool LineNumbers = true, int TabWidth = 4);

/// <summary>
/// Cuts coloured text into pages for a fixed-width font (PZ.2.2): the printer and the PDF file get the same pages, each device only says how many columns and rows it holds.
/// Tabs become spaces to the next tab stop, a row longer than the page wraps, control characters are dropped, and a line number is on the first row of its line only.
/// </summary>
internal static class PrintLayout
{
    private readonly record struct Row(int? Number, List<PrintRun> Runs);

    /// <summary>Splits the segments (start, length, colour or null for the plain colour) into source lines of runs.</summary>
    internal static List<List<PrintRun>> SourceLines(string text, IEnumerable<(int Start, int Length, int? Rgb)> segments, int plain)
    {
        var lines = new List<List<PrintRun>> { new() };
        foreach (var (start, length, rgb) in segments)
        {
            int color = rgb ?? plain;
            var piece = new StringBuilder();
            void Flush()
            {
                if (piece.Length == 0) return;
                lines[^1].Add(new PrintRun(piece.ToString(), color));
                piece.Clear();
            }
            for (int i = start; i < start + length; i++)
            {
                char c = text[i];
                if (c == '\r' && i + 1 < start + length && text[i + 1] == '\n') continue;
                if (c is '\r' or '\n')
                {
                    Flush();
                    lines.Add([]);
                }
                else piece.Append(c);
            }
            Flush();
        }
        if (lines.Count > 1 && lines[^1].Count == 0) lines.RemoveAt(lines.Count - 1); // the text ended with a line break
        return lines;
    }

    /// <summary>Lays the source lines out on pages. A document always has at least one page.</summary>
    internal static List<PrintPage> Paginate(IReadOnlyList<List<PrintRun>> lines, PrintOptions options)
    {
        int numberWidth = options.LineNumbers ? lines.Count.ToString(System.Globalization.CultureInfo.InvariantCulture).Length + 1 : 0;
        int width = Math.Max(1, options.Columns - numberWidth);
        int rows = Math.Max(1, options.Rows);
        var pages = new List<PrintPage>();
        var current = new List<PrintLine>();
        void Add(PrintLine line)
        {
            if (current.Count == rows)
            {
                pages.Add(new PrintPage(pages.Count + 1, current));
                current = [];
            }
            current.Add(line);
        }

        for (int n = 0; n < lines.Count; n++)
        {
            var wrapped = Wrap(lines[n], width, options.TabWidth);
            for (int r = 0; r < wrapped.Count; r++)
            {
                var runs = wrapped[r];
                if (options.LineNumbers)
                    runs.Insert(0, new PrintRun(r == 0 ? (n + 1).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(numberWidth - 1) + " " : new string(' ', numberWidth), NumberColor));
                Add(new PrintLine(r == 0 ? n + 1 : null, runs));
            }
        }
        pages.Add(new PrintPage(pages.Count + 1, current));
        return pages;
    }

    /// <summary>The grey of the line numbers.</summary>
    internal const int NumberColor = 0x808080;

    private static List<List<PrintRun>> Wrap(List<PrintRun> runs, int width, int tab)
    {
        var rows = new List<List<PrintRun>> { new() };
        int column = 0;
        foreach (var run in runs)
        {
            var piece = new StringBuilder();
            void Flush()
            {
                if (piece.Length == 0) return;
                rows[^1].Add(new PrintRun(piece.ToString(), run.Rgb));
                piece.Clear();
            }
            foreach (char c in run.Text)
            {
                if (char.IsControl(c) && c != '\t') continue;
                int span = c == '\t' ? tab - column % tab : 1;
                if (column + span > width && column > 0 && !char.IsLowSurrogate(c)) // a surrogate pair stays on one row
                {
                    Flush();
                    rows.Add([]);
                    column = 0;
                    if (c == '\t') span = tab;
                }
                if (c == '\t') piece.Append(' ', span);
                else piece.Append(c);
                column += span;
            }
            Flush();
        }
        return rows;
    }
}
