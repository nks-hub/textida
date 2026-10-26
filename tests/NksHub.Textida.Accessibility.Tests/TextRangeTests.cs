using NksHub.Textida.Accessibility;

namespace NksHub.Textida.Accessibility.Tests;

public class TextRangeTests
{
    private const string Sample = "one two,three\r\nfour\nfive";

    [Fact]
    public void Lines_split_on_crlf_lf_and_cr()
    {
        using var s = new TestSurface("a\r\nb\nc\rd");
        Assert.Equal(4, s.LineCount);
        Assert.Equal(new[] { 0, 3, 5, 7 }, Enumerable.Range(0, 4).Select(s.LineStart));
        Assert.Equal(3, s.LineOfOffset(7));
        Assert.Equal(3, s.LineOfOffset(s.Length));
    }

    [Fact]
    public void Expand_to_word_includes_trailing_blank()
    {
        using var s = new TestSurface(Sample);
        var r = new TextRange(s, 5, 5);
        r.ExpandToEnclosingUnit(TextUnit.Word);
        Assert.Equal("two", r.GetText());
        r = new TextRange(s, 7, 7);
        r.ExpandToEnclosingUnit(TextUnit.Word);
        Assert.Equal(",", r.GetText());
    }

    [Fact]
    public void Expand_to_line_and_document()
    {
        using var s = new TestSurface(Sample);
        var r = new TextRange(s, 16, 16);
        r.ExpandToEnclosingUnit(TextUnit.Line);
        Assert.Equal("four\n", r.GetText());
        r.ExpandToEnclosingUnit(TextUnit.Document);
        Assert.Equal(Sample, r.GetText());
    }

    [Fact]
    public void Character_unit_keeps_surrogate_pairs_and_crlf_whole()
    {
        using var s = new TestSurface("a\U0001F600\r\nb");
        var r = new TextRange(s, 1, 1);
        r.ExpandToEnclosingUnit(TextUnit.Character);
        Assert.Equal("\U0001F600", r.GetText());
        r.Move(TextUnit.Character, 1);
        Assert.Equal("\r\n", r.GetText());
    }

    [Fact]
    public void Move_by_line_reports_units_actually_moved()
    {
        using var s = new TestSurface(Sample);
        var r = new TextRange(s, 0, 3);
        Assert.Equal(1, r.Move(TextUnit.Line, 1));
        Assert.Equal("four\n", r.GetText());
        Assert.Equal(1, r.Move(TextUnit.Line, 5));
        Assert.Equal("five", r.GetText());
        Assert.Equal(0, r.Move(TextUnit.Line, 1));
        Assert.Equal(-2, r.Move(TextUnit.Line, -5));
        Assert.Equal("one two,three\r\n", r.GetText());
    }

    [Fact]
    public void Empty_range_stays_empty_when_moved()
    {
        using var s = new TestSurface(Sample);
        var r = new TextRange(s, 2, 2);
        Assert.Equal(3, r.Move(TextUnit.Character, 3));
        Assert.True(r.IsEmpty);
        Assert.Equal(5, r.Start);
    }

    [Fact]
    public void MoveEndpoint_past_the_other_end_collapses_the_range()
    {
        using var s = new TestSurface(Sample);
        var r = new TextRange(s, 4, 7);
        Assert.Equal(2, r.MoveEndpointByUnit(RangeEndpoint.Start, TextUnit.Word, 2));
        Assert.True(r.IsEmpty);
        Assert.Equal(r.Start, r.End);
        r = new TextRange(s, 4, 7);
        Assert.Equal(-1, r.MoveEndpointByUnit(RangeEndpoint.Start, TextUnit.Word, -1));
        Assert.Equal(0, r.Start);
        Assert.Equal(7, r.End);
    }

    [Fact]
    public void Compare_and_endpoints()
    {
        using var s = new TestSurface(Sample);
        var a = new TextRange(s, 1, 5);
        var b = new TextRange(s, 1, 5);
        Assert.True(a.Equals(b));
        Assert.True(a.CompareEndpoints(RangeEndpoint.End, new TextRange(s, 0, 3), RangeEndpoint.End) > 0);
        Assert.Equal(0, a.CompareEndpoints(RangeEndpoint.Start, b, RangeEndpoint.Start));
    }

    [Fact]
    public void FindText_forward_backward_and_ignore_case()
    {
        using var s = new TestSurface("Ab ab AB");
        var all = TextRange.Document(s);
        Assert.Equal(3, all.FindText("ab", false, false)!.Start);
        Assert.Equal(6, all.FindText("ab", true, true)!.Start);
        Assert.Null(all.FindText("zz", false, false));
    }

    [Fact]
    public void Range_is_clamped_after_the_text_shrinks()
    {
        using var s = new TestSurface("hello world");
        var r = new TextRange(s, 6, 11);
        s.ReplaceText(5, 6, "");
        Assert.Equal(5, r.Start);
        Assert.Equal(5, r.End);
    }

    [Fact]
    public void Select_sets_surface_selection_and_bounds_follow_lines()
    {
        using var s = new TestSurface("ab\ncd");
        var r = new TextRange(s, 1, 4);
        r.Select();
        Assert.Equal(new TextSpan(1, 3), s.Selection);
        var rects = r.GetBounds();
        Assert.Equal(2, rects.Count);
        Assert.Equal(new ScreenRect(110, 200, 20, 20), rects[0]);
        Assert.Equal(new ScreenRect(100, 220, 10, 20), rects[1]);
    }
}
