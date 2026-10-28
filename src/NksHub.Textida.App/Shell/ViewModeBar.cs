using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Help;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The one switcher bar of the view modes (P3.6.11, P9.3.4): a picture file offers picture, text and hex, a Markdown file reading, editing and source. It sits above the other bars of the tab, one toggle per
/// mode with the current one pressed, and names the current mode for a screen reader. Choosing a mode hands it to <c>choose</c>; choosing the current one changes nothing.
/// </summary>
internal sealed class ViewModeBar : Border
{
    private readonly List<(ViewMode Mode, ToggleButton Button)> _buttons = [];

    /// <param name="name">The accessible name of the bar, with the current mode in it.</param>
    /// <param name="items">The modes the bar offers and the label of each button.</param>
    /// <param name="current">The mode the document is in; a mode the bar does not offer (hex in a Markdown file) leaves every button released.</param>
    /// <param name="choose">Runs when the user presses the button of another mode.</param>
    public ViewModeBar(string name, IReadOnlyList<(ViewMode Mode, string Label)> items, ViewMode current, Action<ViewMode> choose, string helpTopic)
    {
        Current = current;
        Classes.Add("modebar");
        HelpContext.Set(this, helpTopic);
        AutomationProperties.SetName(this, name);
        AutomationProperties.SetControlTypeOverride(this, Avalonia.Automation.Peers.AutomationControlType.Group);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(14, 3), VerticalAlignment = VerticalAlignment.Center };
        foreach (var (mode, label) in items)
        {
            var button = new ToggleButton { Content = label, IsChecked = mode == current };
            button.Classes.Add("findtoggle");
            AutomationProperties.SetName(button, label);
            var target = mode;
            button.Click += (_, _) =>
            {
                if (target == Current) button.IsChecked = true; // pressing the pressed one must not release it
                else choose(target);
            };
            row.Children.Add(button);
            _buttons.Add((mode, button));
        }
        Child = row;
    }

    /// <summary>The mode the bar shows as the current one.</summary>
    public ViewMode Current { get; }

    public IReadOnlyList<ToggleButton> Buttons => [.. _buttons.Select(b => b.Button)];

    /// <summary>Gives the keyboard to the button of the current mode (or the first one), so a keyboard user who switched with the bar stays on it.</summary>
    public bool FocusCurrent() => (_buttons.FirstOrDefault(b => b.Mode == Current).Button ?? _buttons.FirstOrDefault().Button)?.Focus() ?? false;
}
