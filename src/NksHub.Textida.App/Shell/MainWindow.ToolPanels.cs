using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The tool windows that hold a view of their own (the commit panel of a repository and the history of a file) and the strip of buttons on each edge of the window. Every panel docked to an edge has
/// a button in the strip of that edge: it shows the panel while it is hidden and hides it while it is shown, so a hidden panel is a button and not a menu item to look for. Where a panel stands and
/// whether it is shown is the business of the host in <c>MainWindow.PanelLayout.cs</c>; here is what is specific to these two panels and to the strips.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly ToolWindowPanel _commitTool = new(), _historyTool = new();
    private CommitView? _commitView;
    private HistoryView? _historyView;
    private string? _historyKey;
    private readonly Dictionary<PanelDock, (Border Strip, StackPanel Items)> _strips = [];
    private readonly Dictionary<SidePanel, (ToggleButton Button, TextBlock Text)> _stripButtons = [];

    /// <summary>The commit panel of this window, or null while none was opened.</summary>
    internal CommitView? CommitPanelView => _commitView;

    /// <summary>The history panel of this window, or null while none was opened.</summary>
    internal HistoryView? HistoryPanelView => _historyView;

    internal ToolWindowPanel CommitTool => _commitTool;

    internal ToolWindowPanel HistoryTool => _historyTool;

    /// <summary>The strip of buttons on an edge of the window.</summary>
    internal Border PanelStrip(PanelDock dock) => _strips[dock].Strip;

    /// <summary>The button of a panel in the strip of the edge it is docked to.</summary>
    internal ToggleButton StripButton(SidePanel panel) => _stripButtons[panel].Button;

    /// <summary>True when the panel has something to show: the plug-in panel once a plug-in filled it, the commit panel and the history once they were opened.</summary>
    internal bool PanelAvailable(SidePanel panel) => panel switch
    {
        SidePanel.Plugin => _pluginPanelKey is not null,
        SidePanel.Commit => _commitView is not null,
        SidePanel.History => _historyView is not null,
        SidePanel.Headings => IsOpen(SidePanel.Headings) || _activeGroup?.Active?.Doc.Language.Id == "markdown",
        _ => true,
    };

    // ---- building ----

    private void BuildToolPanels()
    {
        _ = HeadingsPanelView;
        foreach (var (panel, tool) in new[] { (SidePanel.Commit, _commitTool), (SidePanel.History, _historyTool), (SidePanel.Headings, _headingsTool) })
        {
            var which = panel;
            MakeDraggable(tool.Header, which);
            tool.Close.Click += (_, _) => TogglePanel(which, false);
        }
        foreach (var dock in PanelLayout.Docks)
        {
            var items = new StackPanel { Orientation = dock == PanelDock.Bottom ? Avalonia.Layout.Orientation.Horizontal : Avalonia.Layout.Orientation.Vertical, Spacing = 2 };
            var strip = new Border
            {
                Child = items, IsVisible = false,
                BorderThickness = dock switch { PanelDock.Right => new Thickness(1, 0, 0, 0), PanelDock.Bottom => new Thickness(0, 1, 0, 0), _ => new Thickness(0, 0, 1, 0) },
            };
            strip.Classes.Add("panelstrip");
            _strips[dock] = (strip, items);
        }
        foreach (var panel in PanelLayout.Panels)
        {
            var text = new TextBlock { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var button = new ToggleButton { Content = text, IsVisible = false, Focusable = true };
            button.Classes.Add("stripbutton");
            var which = panel;
            button.Click += (_, _) => OnStripClick(which);
            _stripButtons[panel] = (button, text);
        }
    }

    /// <summary>Puts the strips around the text: the left and the right one beside it, the bottom one below.</summary>
    private Control WithStrips(Control content)
    {
        var panel = new ReadingDockPanel();
        DockPanel.SetDock(_strips[PanelDock.Left].Strip, Avalonia.Controls.Dock.Left);
        DockPanel.SetDock(_strips[PanelDock.Right].Strip, Avalonia.Controls.Dock.Right);
        DockPanel.SetDock(_strips[PanelDock.Bottom].Strip, Avalonia.Controls.Dock.Bottom);
        panel.Children.Add(_strips[PanelDock.Left].Strip);
        panel.Children.Add(_strips[PanelDock.Right].Strip);
        panel.Children.Add(_strips[PanelDock.Bottom].Strip);
        panel.Children.Add(content);
        return panel;
    }

    // ---- the strips ----

    /// <summary>Puts the button of every panel into the strip of its edge, with the state of the panel, and shows the strips that have a button.</summary>
    internal void RefreshStrips()
    {
        foreach (var panel in PanelLayout.Panels)
        {
            var dock = _layout.DockOf(panel);
            var (button, text) = _stripButtons[panel];
            var items = _strips[dock].Items;
            if (!ReferenceEquals(button.Parent, items))
            {
                (button.Parent as Panel)?.Children.Remove(button);
                button.Content = null;
                if (text.Parent is LayoutTransformControl old) old.Child = null;
                button.Content = dock == PanelDock.Bottom ? text : new LayoutTransformControl { Child = text, LayoutTransform = new RotateTransform(dock == PanelDock.Left ? -90 : 90) };
                items.Children.Add(button);
            }
            button.IsVisible = PanelAvailable(panel);
            button.IsChecked = IsOpen(panel);
        }
        foreach (var (strip, items) in _strips.Values) strip.IsVisible = items.Children.Any(c => c.IsVisible);
    }

    private void OnStripClick(SidePanel panel)
    {
        if (IsOpen(panel)) TogglePanel(panel, false);
        else ShowPanel(panel);
    }

    /// <summary>Shows a panel. The commit panel and the history have to be opened for a repository and a file first, which is what their commands do.</summary>
    private void ShowPanel(SidePanel panel)
    {
        if (panel == SidePanel.Commit && _commitView is null) ShowCommit();
        else if (panel == SidePanel.History && _historyView is null) ShowHistoryPanel();
        else TogglePanel(panel, true);
    }

    /// <summary>
    /// The command of a panel's key: shows the panel and puts the keyboard in it; when it is shown but the keyboard is elsewhere, the keyboard goes there; when the keyboard is in it already, it is
    /// hidden and the keyboard goes back to the text.
    /// </summary>
    internal void ToggleOrFocusPanel(SidePanel panel)
    {
        if (!IsOpen(panel)) ShowPanel(panel);
        else if (!ControlOf(panel).IsKeyboardFocusWithin) FocusPanel(panel);
        else TogglePanel(panel, false);
    }

    // ---- the commit panel ----

    /// <summary>Puts a commit panel into its tool window and shows it; the panel of another repository that was there is disposed.</summary>
    internal void HostCommit(CommitView view)
    {
        var old = _commitView;
        _commitView = view;
        _commitTool.View = view;
        if (old is not null && !ReferenceEquals(old, view)) old.Dispose();
        ApplyToolTitles();
        TogglePanel(SidePanel.Commit, true);
    }

    // ---- texts ----

    private void ApplyToolTitles()
    {
        _commitTool.Title.Text = _commitView is { } c ? Loc.Get(K.CommitTitle, ("repo", Path.GetFileName(c.Root.TrimEnd('/', '\\')))) : Loc.Get(K.PanelCommitName);
        _historyTool.Title.Text = _historyKey is { } key ? Loc.Get(K.HistoryPanelTitle, ("file", Path.GetFileName(key))) : Loc.Get(K.PanelHistoryName);
        AutomationProperties.SetName(_commitTool, Loc.Get(K.PanelCommitName));
        AutomationProperties.SetName(_historyTool, Loc.Get(K.PanelHistoryName));
        AutomationProperties.SetName(_commitTool.Close, Loc.Get(K.PanelToolHide, ("panel", Loc.Get(K.PanelCommitName))));
        AutomationProperties.SetName(_historyTool.Close, Loc.Get(K.PanelToolHide, ("panel", Loc.Get(K.PanelHistoryName))));
        _headingsTool.Title.Text = Loc.Get(K.PanelHeadingsName);
        AutomationProperties.SetName(_headingsTool, Loc.Get(K.PanelHeadingsName));
        AutomationProperties.SetName(_headingsTool.Close, Loc.Get(K.PanelToolHide, ("panel", Loc.Get(K.PanelHeadingsName))));
        _headingsView?.ApplyTexts();
    }

    private void ApplyStripTexts()
    {
        ApplyToolTitles();
        foreach (var (panel, (button, text)) in _stripButtons)
        {
            text.Text = PanelName(panel);
            AutomationProperties.SetName(button, PanelName(panel));
        }
        AutomationProperties.SetName(_strips[PanelDock.Left].Strip, Loc.Get(K.A11yPanelStripLeft));
        AutomationProperties.SetName(_strips[PanelDock.Right].Strip, Loc.Get(K.A11yPanelStripRight));
        AutomationProperties.SetName(_strips[PanelDock.Bottom].Strip, Loc.Get(K.A11yPanelStripBottom));
    }

    /// <summary>The commit panel and the history lay their two halves side by side at the bottom and one above the other at a side.</summary>
    private void ApplyToolOrientation()
    {
        _commitView?.SetWide(_layout.DockOf(SidePanel.Commit) == PanelDock.Bottom);
        _historyView?.SetWide(_layout.DockOf(SidePanel.History) == PanelDock.Bottom);
    }
}
