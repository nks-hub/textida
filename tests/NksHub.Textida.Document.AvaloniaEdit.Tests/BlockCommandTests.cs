using AvaloniaEdit.Document;
using NksHub.Textida.AvaloniaEditAdapter;
using static NksHub.Textida.Document.AvaloniaEdit.Tests.EditingCommandTests;

namespace NksHub.Textida.Document.AvaloniaEdit.Tests;

/// <summary>
/// P4.5.3 and P4.5.4 on the document: insert into lines, numbering, sort, alignment, paragraph wrapping, line ranges and replacing selections.
/// Same notation as the line commands; every case that changes the text also proves one undo step restores the bytes.
/// </summary>
public class BlockCommandTests
{
    private static void Insert(string input, LineInsertOptions o, string expected) =>
        Run(input, (b, s) => LineOperations.InsertIntoLines(b.TextDocument, s, o), expected);

    [Fact]
    public void Text_goes_to_the_start_of_the_selected_lines()
    {
        Insert("a\n«b\nc»\nd", new(Text: "- "), "a\n- «b\n- c»\nd");
        Insert("a|\r\nb", new(Text: "# "), "# a|\r\n# b"); // one caret: the whole document, line endings stay CRLF
    }

    [Fact]
    public void Text_goes_to_the_end_of_the_lines() =>
        Insert("a|\nb", new(Text: ";", Place: LineInsertPlace.LineEnd), "a;|\nb;");

    [Fact]
    public void Column_is_one_based_and_a_shorter_line_takes_the_text_at_its_end() =>
        Insert("|abcd\na", new(Text: "X", Place: LineInsertPlace.Column, Column: 3), "|abXcd\naX");

    [Fact]
    public void Skipping_the_indent_puts_the_text_before_the_first_visible_character() =>
        Insert("|  a\n\tb", new(Text: ">", SkipIndent: true), "|  >a\n\t>b");

    [Fact]
    public void Numbering_pads_the_numbers_and_empty_lines_can_be_skipped()
    {
        Insert("|a\n\nb", new(LineInsertMode.Numbering, SkipEmptyLines: true, Start: 1, Width: 2, Separator: ". "), "01. |a\n\n02. b");
        Insert("|a\n\nb", new(LineInsertMode.Numbering, Start: 9, Width: 1, Separator: ". "), "9. |a\n10. \n11. b");
        Insert("|a\nb", new(LineInsertMode.Numbering, Place: LineInsertPlace.LineEnd, Start: 5, Separator: " #"), "|a #5\nb #6");
    }

    [Fact]
    public void Numbering_puts_a_number_into_every_caret()
    {
        Insert("|a\n|b\n|c", new(LineInsertMode.Numbering, Place: LineInsertPlace.Selections, Start: 1), "1|a\n2|b\n3|c");
        Insert("x |y z |", new(LineInsertMode.Numbering, Place: LineInsertPlace.Selections, Start: 7, Width: 3), "x 007|y z 008|");
    }

    [Fact]
    public void Bullets_use_the_text_as_the_bullet() =>
        Insert("«a\nb»", new(LineInsertMode.Bullets, Text: "• "), "• «a\n• b»");

    [Theory]
    [InlineData(5, 3, "005")]
    [InlineData(-5, 3, "-005")]
    [InlineData(12345, 2, "12345")]
    [InlineData(0, 0, "0")]
    public void Numbers_are_padded_with_zeros(long value, int width, string expected) => Assert.Equal(expected, LineOperations.FormatNumber(value, width));

    private static void Sort(string input, SortOptions o, string expected) =>
        Run(input, (b, s) => LineOperations.SortLines(b.TextDocument, s, o), expected);

    [Fact]
    public void Sort_orders_the_selected_lines_and_selects_them()
    {
        Sort("x\n«c\nb\na»\ny", new(), "x\n«a\nb\nc»\ny");
        Sort("x\n«b\nc\na»", new(Descending: true), "x\n«c\nb\na»");
    }

    [Fact]
    public void Sort_of_one_caret_takes_the_whole_document_and_keeps_the_caret_and_the_line_ending() =>
        Sort("b|\r\nA\r\nc", new(), "A|\r\nb\r\nc");

    [Fact]
    public void Sort_can_drop_duplicates_and_respects_case()
    {
        Sort("|b\na\nb\nA", new(RemoveDuplicates: true), "|a\nb");
        Sort("|b\na\nb\nA", new(CaseSensitive: true, RemoveDuplicates: true), "|A\na\nb");
    }

    [Fact]
    public void Numeric_sort_reads_the_leading_number_and_puts_lines_without_one_first() =>
        Sort("|10 x\n9 y\nabc\n-1 z\n2.5 w", new(Method: SortMethod.Numeric), "|abc\n-1 z\n2.5 w\n9 y\n10 x");

    [Fact]
    public void Sort_compares_only_the_column_range() =>
        Sort("|x3b\ny1a\nz2c", new(ColumnFrom: 3, ColumnTo: 3), "|y1a\nx3b\nz2c");

    [Fact]
    public void Locale_sort_follows_the_collation_of_the_culture()
    {
        Assert.Equal("cs-CZ", new SortOptions(Locale: "0405").Culture!.Name);
        Assert.Equal("cs-CZ", new SortOptions(Locale: "cs-CZ").Culture!.Name);
        Assert.Null(new SortOptions(Locale: "not-a-culture-xx").Culture);
        Assert.Equal(["ci", "h", "ch", "i"], LineText.Sort(["ch", "ci", "h", "i"], new SortOptions(Method: SortMethod.Locale, Locale: "0405")));
        Assert.Equal(["ch", "ci", "h", "i"], LineText.Sort(["i", "h", "ci", "ch"], new SortOptions()));
    }

    [Fact]
    public void Sort_preview_shows_the_start_of_the_result_without_changing_the_document()
    {
        var (text, sels) = Parse("|c\nb\na");
        var doc = new TextDocument(text);
        Assert.Equal("a\nb", LineOperations.SortPreview(doc, sels, new(), 2));
        Assert.Equal(text, doc.Text);
    }

    private static void Align(string input, Justification mode, string expected) =>
        Run(input, (b, s) => LineOperations.JustifyLines(b.TextDocument, s, mode), expected);

    [Fact]
    public void Lines_are_aligned_to_the_longest_one()
    {
        Align("«  a  \n\tb\nccc»", Justification.Left, "«a\nb\nccc»");
        Align("«a\n  ccc\nbb»", Justification.Right, "«  a\nccc\n bb»");
        Align("«a\nccc\nb»", Justification.Center, "« a\nccc\n b»");
        Align("«aa bb\nc d e\nlonger line»", Justification.Block, "«aa       bb\nc    d    e\nlonger line»");
    }

    private static void Wrap(string input, int width, string expected) =>
        Run(input, (b, s) => LineOperations.ReformatParagraphs(b.TextDocument, s, width), expected);

    [Fact]
    public void Reformat_wraps_the_paragraph_of_the_caret_only()
    {
        Wrap("|aaa bbb ccc ddd\neee\n\nff  \n  gg", 10, "aaa bbb\nccc ddd\neee|\n\nff  \n  gg");
        Wrap("aaa\nbbb|\n\nzz\nyy", 10, "aaa bbb|\n\nzz\nyy");
    }

    [Fact]
    public void Reformat_of_a_selection_wraps_every_paragraph_and_keeps_the_indentation_and_the_blank_lines() =>
        Wrap("«  aaa\n  bbb ccc\n\n\nlongword x\ny»", 9, "«  aaa bbb\n  ccc\n\n\nlongword\nx y»");

    private static void WrapShort(string input, string expected) =>
        Run(input, (b, s) => LineOperations.ReformatParagraphs(b.TextDocument, s, 30, shortLine: 15), expected);

    [Fact]
    public void A_short_line_ends_the_paragraph_of_the_caret_without_a_blank_line()
    {
        WrapShort("|alpha beta gamma\ndelta.\nepsilon zeta eta\ntheta iota", "alpha beta gamma delta.|\nepsilon zeta eta\ntheta iota");
        WrapShort("alpha beta gamma\ndelta.\n|epsilon zeta eta\ntheta iota", "alpha beta gamma\ndelta.\nepsilon zeta eta theta iota|");
        Wrap("|alpha beta gamma\ndelta.\nepsilon zeta eta\ntheta iota", 30, "alpha beta gamma delta.\nepsilon zeta eta theta iota|");
    }

    [Fact]
    public void A_short_line_ends_every_paragraph_of_a_selection() =>
        WrapShort("«alpha beta gamma\ndelta.\nepsilon zeta eta\ntheta iota»", "«alpha beta gamma delta.\nepsilon zeta eta theta iota»");

    [Fact]
    public void A_short_line_is_counted_without_its_trailing_blanks()
    {
        Assert.True(LineText.IsShort("ab   \t", 3));
        Assert.False(LineText.IsShort("abc", 3));
        Assert.False(LineText.IsShort("", 0));
    }

    private static void Lines(string text, string expected, params (int From, int To)[] ranges)
    {
        var doc = new TextDocument(text);
        Assert.Equal(expected, Mark(text, LineOperations.SelectLineRanges(doc, ranges)));
    }

    [Fact]
    public void Line_ranges_become_one_selection_each()
    {
        Lines("a\nb\nc\nd\ne\nf", "a\n«b\nc\n»d\n«e\nf»", (2, 3), (5, 9));
        Lines("a\nb", "a\nb", (7, 8));
    }

    [Theory]
    [InlineData("10-20, 40–50", "10-20;40-50")]
    [InlineData("7", "7-7")]
    [InlineData("5,3-4", "3-5")]
    [InlineData("1;;2  4", "1-2;4-4")]
    [InlineData("1 - 3 5-6", "1-3;5-6")]
    public void Ranges_are_read_merged_and_ordered(string text, string expected)
    {
        Assert.True(LineText.TryParseRanges(text, out var ranges));
        Assert.Equal(expected, string.Join(';', ranges.Select(r => $"{r.From}-{r.To}")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("20-10")]
    [InlineData("10-20 and 30")]
    [InlineData("a")]
    [InlineData("1-")]
    public void Wrong_ranges_are_refused(string text) => Assert.False(LineText.TryParseRanges(text, out _));

    [Fact]
    public void Replacing_selections_writes_the_text_of_each_and_places_the_caret_after_or_before_it()
    {
        Run("|a\n«bc»", (b, s) => LineOperations.ReplaceSelections(b.TextDocument, s, i => "X" + i), "X0|a\nX1|");
        Run("|a\n«bc»", (b, s) => LineOperations.ReplaceSelections(b.TextDocument, s, i => "X" + i, caretBefore: true), "|X0a\n|X1");
    }
}
