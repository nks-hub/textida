using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The frame of a tool window that holds a view the window creates and replaces (the commit panel of a repository, the history of a file): a title that is dragged to move the panel, a button that
/// hides it, and the view below. The header is what <c>MainWindow.MakeDraggable</c> makes the handle of the panel.
/// </summary>
internal sealed class ToolWindowPanel : DockPanel
{
    private readonly ContentControl _body = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };

    public ToolWindowPanel()
    {
        IsVisible = false;
        Title = new TextBlock { FontWeight = FontWeight.SemiBold, Margin = new Thickness(12, 8, 12, 6), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Close = new Button { Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Arrow) };
        Close.Classes.Add("tabclose");
        Close.Content = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M0,0 L8,8 M8,0 L0,8"), StrokeThickness = 1.2, Width = 8, Height = 8, Stretch = Stretch.None };
        DockPanel.SetDock(Close, Dock.Right);
        Header = new DockPanel { Background = Brushes.Transparent, Children = { Close, Title } };
        DockPanel.SetDock(Header, Dock.Top);
        Children.Add(Header);
        Children.Add(_body);
    }

    protected override Type StyleKeyOverride => typeof(DockPanel);

    public TextBlock Title { get; }

    public Button Close { get; }

    /// <summary>The part of the frame a panel is dragged by.</summary>
    public DockPanel Header { get; }

    /// <summary>The view in the frame, or null.</summary>
    public Control? View
    {
        get => _body.Content as Control;
        set => _body.Content = value;
    }
}
