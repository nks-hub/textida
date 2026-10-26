using NksHub.Textida.Accessibility;

namespace NksHub.Textida.Accessibility.Tests;

public class DocumentTextSurfaceTests
{
    [Fact]
    public void Typing_raises_text_change_and_moves_the_caret()
    {
        using var s = new TestSurface("ab\ncd");
        var changes = new List<TextChange>();
        int selections = 0;
        s.TextChanged += changes.Add;
        s.SelectionChanged += () => selections++;
        s.Select(2, 2);
        s.ReplaceText(2, 0, "XY");
        Assert.Equal("abXY\ncd", s.GetText(0, s.Length));
        Assert.Equal(new TextChange(2, 0, 2, s.Revision), Assert.Single(changes));
        Assert.Equal(4, s.CaretOffset);
        Assert.True(selections >= 2);
    }

    [Fact]
    public void Undo_maps_selection_through_the_change_and_reports_it()
    {
        using var s = new TestSurface("hello");
        s.Select(5, 5);
        s.ReplaceText(5, 0, " world");
        var changes = new List<TextChange>();
        s.TextChanged += changes.Add;
        s.Document.Undo();
        Assert.Equal("hello", s.GetText(0, s.Length));
        var c = Assert.Single(changes);
        Assert.Equal((5, 6, 0), (c.Start, c.Removed, c.Inserted));
        Assert.Equal(5, s.CaretOffset);
    }

    [Fact]
    public void Edit_after_surrogate_pair_uses_utf16_offsets()
    {
        using var s = new TestSurface("\U0001F600\nx");
        s.ReplaceText(3, 1, "yz");
        Assert.Equal("\U0001F600\nyz", s.GetText(0, s.Length));
        Assert.Equal(2, s.LineCount);
    }

    [Fact]
    public void Selection_keeps_direction()
    {
        using var s = new TestSurface("abcdef");
        s.Select(4, 1);
        Assert.Equal(new TextSpan(1, 3), s.Selection);
        Assert.Equal(1, s.CaretOffset);
    }

    [Fact]
    public void Empty_document_has_one_empty_line()
    {
        using var s = new TestSurface("");
        Assert.Equal(1, s.LineCount);
        Assert.Equal(0, s.Length);
        var r = TextRange.Document(s);
        Assert.Equal("", r.GetText());
        Assert.Equal(0, r.Move(TextUnit.Line, 1));
    }
}
