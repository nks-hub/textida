using NksHub.Textida.App.Editing;

namespace NksHub.Textida.App.Tests;

/// <summary>PR.4.3: a block of the compare view is marked in the gutter on every row, also when neither side accepts changes, so its first row is not told by tint alone.</summary>
public class DiffGutterMarkTests
{
    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, false, false)]
    public void Every_row_of_a_block_has_a_mark_in_the_gutter(bool firstRow, bool left, bool right, bool arrows) =>
        Assert.Equal(arrows ? DiffView.GutterMark.Arrows : DiffView.GutterMark.Bar, DiffView.GutterMarkOf(firstRow, left, right));
}
