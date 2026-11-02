using Avalonia.Controls;

namespace NksHub.Textida.App.Shell;

/// <summary>The layout the commit panel and the history share: two halves with a handle between them, side by side or one above the other.</summary>
internal static class SplitPaneLayout
{
    private const double HandleThickness = 4;

    /// <summary>Puts the halves of <paramref name="grid"/> in a row (<paramref name="wide"/>, the first half <paramref name="firstSize"/> wide) or in a column (the halves share the height).</summary>
    public static void Apply(Grid grid, Control first, GridSplitter handle, Control second, bool wide, double firstSize)
    {
        grid.ColumnDefinitions = new ColumnDefinitions(wide ? $"{firstSize},Auto,*" : "*");
        grid.RowDefinitions = new RowDefinitions(wide ? "*" : "*,Auto,*");
        Grid.SetRow(first, 0);
        Grid.SetColumn(first, 0);
        Grid.SetRow(handle, wide ? 0 : 1);
        Grid.SetColumn(handle, wide ? 1 : 0);
        Grid.SetRow(second, wide ? 0 : 2);
        Grid.SetColumn(second, wide ? 2 : 0);
        handle.ResizeDirection = wide ? GridResizeDirection.Columns : GridResizeDirection.Rows;
        handle.Width = wide ? HandleThickness : double.NaN;
        handle.Height = wide ? double.NaN : HandleThickness;
    }
}
