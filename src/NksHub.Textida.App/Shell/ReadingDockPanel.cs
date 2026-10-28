using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// A <see cref="DockPanel"/> whose Tab order is its reading order: the parts docked to the top and the left, the part that fills the rest,
/// then the parts docked to the bottom and the right. Those are listed first in the children because they dock outermost, and a plain
/// panel would put the keyboard on them before the content.
/// </summary>
internal class ReadingDockPanel : DockPanel
{
    protected override Type StyleKeyOverride => typeof(DockPanel);

    protected override Size MeasureOverride(Size availableSize)
    {
        Number();
        return base.MeasureOverride(availableSize);
    }

    private void Number()
    {
        var children = Children;
        int fill = LastChildFill && children.Count > 0 ? children.Count - 1 : -1;
        int rank = 0;
        for (int i = 0; i < children.Count; i++)
            if (i != fill && GetDock(children[i]) is Dock.Top or Dock.Left) Set(children[i], rank++);
        if (fill >= 0) Set(children[fill], rank++);
        for (int i = children.Count - 1; i >= 0; i--)
            if (i != fill && GetDock(children[i]) is Dock.Bottom or Dock.Right) Set(children[i], rank++);
    }

    private static void Set(Control child, int rank)
    {
        if (KeyboardNavigation.GetTabIndex(child) != rank) KeyboardNavigation.SetTabIndex(child, rank);
    }
}
