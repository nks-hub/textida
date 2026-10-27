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

    [Fact]
    public void A_pipe_in_inline_code_or_after_a_backslash_does_not_split_a_cell()
    {
        Assert.Equal(["`a|b`", "c"], MarkdownTools.Cells("| `a|b` | c |"));
        Assert.Equal(["``x|`y``", "z"], MarkdownTools.Cells("``x|`y`` | z"));
        Assert.Equal(["`open", "x"], MarkdownTools.Cells("`open | x"));
        Assert.Equal(["a\\|b", "c"], MarkdownTools.Cells("a\\|b|c"));
    }

    [Fact]
    public void The_cell_spans_point_at_the_trimmed_text_and_an_empty_cell_starts_after_one_space()
    {
        var spans = MarkdownTools.CellSpans("|  ab | |  c|");
        Assert.Equal([("ab", 3, 2), ("", 8, 0), ("c", 11, 1)], spans.Select(c => (c.Text, c.Start, c.Length)));
    }

    [Fact]
    public void Wide_and_combining_characters_are_measured_by_their_display_width()
    {
        Assert.Equal(4, MarkdownTools.DisplayWidth("日本"));
        Assert.Equal(1, MarkdownTools.DisplayWidth("é"));
        Assert.Equal(2, MarkdownTools.DisplayWidth("\U0001F600"));
        Assert.Equal(3, MarkdownTools.DisplayWidth("a​bc"));
        var formatted = MarkdownTools.FormatTable(Lines("k|v\n-|-\n日本|x\nabcd|y"));
        Assert.Equal(["| k    | v   |", "| ---- | --- |", "| 日本 | x   |", "| abcd | y   |"], formatted);
    }

    [Fact]
    public void Tab_walks_the_cells_skips_the_dashes_and_adds_a_row_after_the_last_cell()
    {
        var lines = Lines("a|b\n:-|-:\nc|d");
        var edit = MarkdownTools.MoveCell(lines, 0, 0, forward: true)!;
        Assert.Equal(["| a   |   b |", "| :-- | --: |", "| c   |   d |"], edit.Lines);
        Assert.Equal((0, "b"), (edit.CaretRow, edit.Lines[edit.CaretRow].Substring(edit.CaretStart, edit.CaretLength)));

        var next = MarkdownTools.MoveCell(edit.Lines, 0, edit.CaretStart, forward: true)!;
        Assert.Equal((2, "c"), (next.CaretRow, next.Lines[2].Substring(next.CaretStart, next.CaretLength)));

        var last = MarkdownTools.MoveCell(next.Lines, 2, next.Lines[2].LastIndexOf('d'), forward: true)!;
        Assert.Equal(4, last.Lines.Count);
        Assert.Equal(3, last.CaretRow);
        Assert.Equal("|     |     |", last.Lines[3]);
        Assert.Equal(3, last.Count);
    }

    [Fact]
    public void Shift_tab_goes_back_over_the_dashes_and_stays_in_the_first_cell()
    {
        var lines = Lines("| a | b |\n|---|---|\n| c | d |");
        var back = MarkdownTools.MoveCell(lines, 2, 2, forward: false)!;
        Assert.Equal((0, "b"), (back.CaretRow, back.Lines[0].Substring(back.CaretStart, back.CaretLength)));
        var first = MarkdownTools.MoveCell(lines, 0, 2, forward: false)!;
        Assert.Equal((0, "a"), (first.CaretRow, first.Lines[0].Substring(first.CaretStart, first.CaretLength)));
        Assert.Null(MarkdownTools.MoveCell(Lines("plain | text"), 0, 0, forward: true));
    }

    [Fact]
    public void A_new_row_goes_below_the_caret_row_and_below_the_dashes_for_the_header()
    {
        var lines = Lines("| a | b | c |\n|---|---|---|\n| 1 | 2 | 3 |\n| 4 | 5 | 6 |");
        var edit = MarkdownTools.NewRow(lines, 2)!;
        Assert.Equal(5, edit.Lines.Count);
        Assert.Equal(3, edit.CaretRow);
        Assert.Equal("|     |     |     |", edit.Lines[3]);
        Assert.Equal("| 4   | 5   | 6   |", edit.Lines[4]);
        Assert.Equal(2, MarkdownTools.NewRow(lines, 0)!.CaretRow);
    }

    [Fact]
    public void A_broken_table_gets_its_dashes_back_short_rows_filled_and_colons_kept()
    {
        var noDashes = MarkdownTools.Repair(Lines("a|b|c\n1|2\n4"), 1)!;
        Assert.Equal(["| a   | b   | c   |", "| --- | --- | --- |", "| 1   | 2   |     |", "| 4   |     |     |"], noDashes.Lines);
        Assert.Equal(2, noDashes.CaretRow);
        Assert.Equal(3, noDashes.Count);

        var colons = MarkdownTools.Repair(Lines("h1|h2\n:-:|--:\nx"), 2)!;
        Assert.Equal(["| h1  |  h2 |", "| :-: | --: |", "|  x  |     |"], colons.Lines);

        var stray = MarkdownTools.Repair(Lines("| a | b |\n|---|---|\n| 1 | 2 |\n|---|---|"), 0)!;
        Assert.Equal(3, stray.Lines.Count);

        var onlyDashes = MarkdownTools.Repair(Lines("|--|--|\n| 1 | 2 |"), 1)!;
        Assert.Equal(3, onlyDashes.Lines.Count);
        Assert.Equal("|     |     |", onlyDashes.Lines[0]);
        Assert.Null(MarkdownTools.Repair(Lines("no pipes"), 0));
    }

    [Fact]
    public void Repair_keeps_an_escaped_pipe_and_a_pipe_in_code_in_their_cells()
    {
        var edit = MarkdownTools.Repair(Lines("a|b\n`x|y`|p\\|q"), 0)!;
        Assert.Equal(["| a     | b    |", "| ----- | ---- |", "| `x|y` | p\\|q |"], edit.Lines);
    }
}
