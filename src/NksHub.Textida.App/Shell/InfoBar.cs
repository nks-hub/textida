using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Shell;

/// <summary>A non-modal strip with a message and a few actions, shown above the editor (external change, restore, warnings). F1 inside it opens <c>helpTopic</c>.</summary>
internal sealed class InfoBar : Border
{
    public InfoBar(string message, IReadOnlyList<(string Label, Action Click)> actions, bool warning = false, Action? onClose = null, string? helpTopic = null)
    {
        Message = message;
        if (helpTopic is not null) HelpContext.Set(this, helpTopic);
        Classes.Add("infobar");
        if (warning) Classes.Add("warning");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (label, click) in actions)
        {
            var button = new Button { Content = label, Padding = new Thickness(10, 2), MinHeight = 24 };
            button.Classes.Add("barbutton");
            button.Click += (_, _) => click();
            row.Children.Add(button);
        }
        var close = new Button { Content = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M0,0 L8,8 M8,0 L0,8"), StrokeThickness = 1.2, Width = 8, Height = 8, Stretch = Stretch.None } };
        close.Classes.Add("tabclose");
        AutomationProperties.SetName(close, Loc.Get(K.InfoBarClose));
        close.Click += (_, _) => onClose?.Invoke();
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 12, Margin = new Thickness(14, 6) };
        Grid.SetColumn(row, 1);
        Grid.SetColumn(close, 2);
        grid.Children.Add(text);
        grid.Children.Add(row);
        grid.Children.Add(close);
        Child = grid;
        AutomationProperties.SetName(this, message);
    }

    public string Message { get; }

    /// <summary>A notice appears without taking the keyboard, so a screen reader is told what it says; a warning interrupts.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (TopLevel.GetTopLevel(this) as MainWindow)?.Announce(Message, urgent: Classes.Contains("warning"));
    }

    public IEnumerable<Button> Buttons => ((Panel)((Grid)Child!).Children[1]).Children.OfType<Button>();
}
