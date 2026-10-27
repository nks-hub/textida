using NksHub.Textida.Core.Markdown;

namespace NksHub.Textida.Core.Tests;

/// <summary>P9.3.3: headings, the table around a line and its alignment, and the table of contents.</summary>
public class MarkdownToolsTests
{
    private static string[] Lines(string text) => text.Split('\n');

    [Fact]
    public void Headings_skip_fenced_code_and_keep_level_and_line()
    {
        var h = MarkdownTools.Headings(Lines("# One\n```\n# not a heading\n```\n## Two ##\ntext\n#nospace"));
        Assert.Equal([(1, "One", 0), (2, "Two", 4)], h.Select(x => (x.Level, x.Title, x.Line)));
    }

    [Fact]
    public void A_table_is_aligned_with_marks_kept_and_short_rows_filled()
    {
        var rows = Lines("name|qty|price\n:--|:-:|--:\napple|3|1.5\nfig|12");
        var formatted = MarkdownTools.FormatTable(rows);
        Assert.Equal(
        [
            "| name  | qty | price |",
            "| :---- | :-: | ----: |",
            "| apple |  3  |   1.5 |",
            "| fig   | 12  |       |",
        ], formatted);
    }

    [Fact]
    public void The_table_around_a_line_needs_a_separator_row_and_stops_at_a_blank_line()
    {
        var lines = Lines("intro\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\nplain | text\nmore");
        Assert.Equal((2, 4), MarkdownTools.TableAround(lines, 4));
        Assert.Equal((2, 4), MarkdownTools.TableAround(lines, 2));
        Assert.Null(MarkdownTools.TableAround(lines, 6));
        Assert.Null(MarkdownTools.TableAround(lines, 0));
    }

    [Fact]
    public void An_escaped_pipe_stays_in_its_cell()
    {
        Assert.Equal(["a\\|b", "c"], MarkdownTools.Cells("| a\\|b | c |"));
    }

    [Fact]
    public void The_table_of_contents_nests_by_level_and_numbers_equal_anchors()
    {
        var toc = MarkdownTools.TocLines(MarkdownTools.Headings(Lines("## Intro\n### Part A\n### Part A\n## End!")));
        Assert.Equal(["- [Intro](#intro)", "  - [Part A](#part-a)", "  - [Part A](#part-a-1)", "- [End!](#end)"], toc);
        Assert.Equal((1, 3), MarkdownTools.TocBlock(Lines("x\n<!-- TOC -->\nold\n<!-- /TOC -->\ny")));
        Assert.Null(MarkdownTools.TocBlock(Lines("<!-- TOC -->\nno close")));
    }
}
