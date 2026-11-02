using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NksHub.Textida.App.Profile;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>P3.6.9: the tool window host. The commit panel and the history stand on every side like the older panels, collapse to a button in the strip of their edge, keep their place in the profile and answer the keyboard.</summary>
[Collection("LocState")]
public class ToolWindowHostTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public ToolWindowHostTests()
    {
        _app = TestApp.Prepare(_temp, new ScriptedDialogs());
        _w = _app.CreateWindow(null);
        _w.Width = 1000;
        _w.Height = 700;
        _w.Show();
        _w.UpdateLayout();
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        _w.UpdateLayout();
    }

    private Workbench.Commands.CommandOutcome Run(string id) => _app.Workbench.Commands.ExecuteAsync(id, new ShellContext(_w, forMenu: true)).AsTask().GetAwaiter().GetResult();

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>A commit panel of a repository that has no changes, shown in the window.</summary>
    private CommitView OpenCommit()
    {
        var view = new CommitView(_temp.Path, new CommitViewActions(
            Read: () => Task.FromResult<CommitChanges?>(new CommitChanges(_temp.Path, "main", null, 0, 0, true, 0, [], [])),
            Stage: (_, _) => Task.CompletedTask, StageHunk: (_, _, _) => Task.FromResult(false), Blob: (_, _) => Task.FromResult<CommitBlob?>(null),
            Commit: (_, _) => Task.FromResult(false), Push: () => Task.CompletedTask), () => _w.Options);
        _w.HostCommit(view);
        Settle();
        return view;
    }

    private HistoryView OpenHistory()
    {
        string path = _temp.File("h.txt", "a\n");
        var tab = _w.OpenFile(path, null);
        _w.SelectTab(tab);
        var view = _w.OpenHistoryPanel(tab, path);
        Settle();
        return view;
    }

    private static Control HostOf(MainWindow w, ToolWindowPanel tool) => (Control)tool.Parent!.Parent!;

    [AvaloniaFact]
    public void The_commit_panel_and_the_history_start_at_the_bottom_and_stand_in_the_host_of_every_side_they_are_docked_to()
    {
        var commit = OpenCommit();
        var history = OpenHistory();
        Assert.Equal(PanelDock.Bottom, _w.SideLayout.DockOf(SidePanel.Commit));
        Assert.Equal(PanelDock.Bottom, _w.SideLayout.DockOf(SidePanel.History));
        Assert.Equal(SidePanel.History, _w.PanelShown(PanelDock.Bottom)); // a side shows one panel: the one opened last

        foreach (var dock in PanelLayout.Docks)
        {
            _w.MovePanel(SidePanel.Commit, dock);
            _w.TogglePanel(SidePanel.Commit, true);
            Settle();
            Assert.Equal(SidePanel.Commit, _w.PanelShown(dock));
            Assert.Same(_w.PanelHost(dock), HostOf(_w, _w.CommitTool));
            Assert.True(_w.PanelHost(dock).IsVisible);
            Assert.True(commit.IsEffectivelyVisible);
            // the two halves lie side by side in a wide place (the bottom) and one above the other at a side
            Assert.Equal(dock == PanelDock.Bottom ? 3 : 1, commit.ColumnDefinitions.Count);
            Assert.Equal(dock == PanelDock.Bottom ? 1 : 3, commit.RowDefinitions.Count);
        }
        foreach (var dock in PanelLayout.Docks)
        {
            _w.MovePanel(SidePanel.History, dock);
            _w.TogglePanel(SidePanel.History, true);
            Settle();
            Assert.Equal(SidePanel.History, _w.PanelShown(dock));
            Assert.Same(_w.PanelHost(dock), HostOf(_w, _w.HistoryTool));
            Assert.Equal(dock == PanelDock.Bottom ? 3 : 1, history.ColumnDefinitions.Count);
        }
    }

    [AvaloniaFact]
    public void A_hidden_panel_is_a_button_on_its_edge_and_the_button_shows_and_hides_it()
    {
        Loc.SetLanguage("en");
        var strip = _w.PanelStrip(PanelDock.Bottom);
        Assert.False(strip.IsVisible); // nothing is docked there yet that has something to show
        Assert.True(_w.PanelStrip(PanelDock.Left).IsVisible); // the documents and the files are always there
        Assert.False(_w.StripButton(SidePanel.Commit).IsVisible);

        OpenCommit();
        var button = _w.StripButton(SidePanel.Commit);
        Assert.True(strip.IsVisible);
        Assert.True(button.IsVisible);
        Assert.Same(((Border)strip).Child, button.Parent);
        Assert.True(button.IsChecked);

        Click(_w.CommitTool.Close); // collapse
        Settle();
        Assert.False(_w.IsPanelOpen(SidePanel.Commit));
        Assert.False(_w.PanelHost(PanelDock.Bottom).IsVisible);
        Assert.True(button.IsVisible);
        Assert.False(button.IsChecked);

        Click(button); // restore
        Settle();
        Assert.True(_w.IsPanelOpen(SidePanel.Commit));
        Assert.True(_w.PanelHost(PanelDock.Bottom).IsVisible);
        Assert.True(button.IsChecked);

        Click(button);
        Assert.False(_w.IsPanelOpen(SidePanel.Commit));
        Assert.Equal("Commit", AutomationProperties.GetName(button));
    }

    [AvaloniaFact]
    public void The_button_of_a_panel_follows_it_to_the_strip_of_its_new_edge_and_an_empty_strip_goes_away()
    {
        OpenCommit();
        var button = _w.StripButton(SidePanel.Commit);
        var bottom = _w.PanelStrip(PanelDock.Bottom);
        Assert.True(bottom.IsVisible);

        _w.MovePanel(SidePanel.Commit, PanelDock.Right);
        Settle();
        Assert.Same(((Border)_w.PanelStrip(PanelDock.Right)).Child, button.Parent);
        Assert.True(_w.PanelStrip(PanelDock.Right).IsVisible);
        Assert.False(bottom.IsVisible);
        Assert.True(button.IsChecked);

        _w.MovePanel(SidePanel.Commit, PanelDock.Left);
        Settle();
        Assert.Same(((Border)_w.PanelStrip(PanelDock.Left)).Child, button.Parent);
        Assert.False(_w.PanelStrip(PanelDock.Right).IsVisible);
        var left = ((StackPanel)((Border)_w.PanelStrip(PanelDock.Left)).Child!).Children.OfType<ToggleButton>().Where(b => b.IsVisible).ToList();
        Assert.Contains(button, left);
        Assert.Contains(_w.StripButton(SidePanel.Documents), left);
        Assert.Contains(_w.StripButton(SidePanel.Files), left);
    }

    [AvaloniaFact]
    public void The_strip_buttons_of_the_documents_and_the_files_show_them_where_they_are_docked()
    {
        Click(_w.StripButton(SidePanel.Files));
        Settle();
        Assert.True(_w.FilePanelOpen);
        _w.MovePanel(SidePanel.Files, PanelDock.Right);
        Settle();
        Assert.True(_w.StripButton(SidePanel.Files).IsChecked);
        Assert.Same(((Border)_w.PanelStrip(PanelDock.Right)).Child, _w.StripButton(SidePanel.Files).Parent);
        Click(_w.StripButton(SidePanel.Files));
        Assert.False(_w.FilePanelOpen);
        Assert.False(_w.StripButton(SidePanel.Files).IsChecked);
    }

    [AvaloniaFact]
    public void The_place_of_every_panel_and_the_sizes_survive_a_restart_through_window_json()
    {
        _w.MovePanel(SidePanel.Commit, PanelDock.Left);
        _w.MovePanel(SidePanel.History, PanelDock.Right);
        _w.MovePanel(SidePanel.Files, PanelDock.Bottom);
        _w.SetPanelSize(PanelDock.Left, 300, final: true);
        _w.SetPanelSize(PanelDock.Bottom, 150, final: true);

        var store = new ProfileStore(_temp.Path);
        store.SaveWindow(_w.CaptureGeometry());
        string json = File.ReadAllText(Path.Combine(_temp.Path, "window.json"));
        Assert.Contains("\"commit\": \"left\"", json);
        Assert.Contains("\"history\": \"right\"", json);

        var restarted = _app.CreateWindow(store.LoadWindow());
        Assert.Equal(PanelDock.Left, restarted.SideLayout.DockOf(SidePanel.Commit));
        Assert.Equal(PanelDock.Right, restarted.SideLayout.DockOf(SidePanel.History));
        Assert.Equal(PanelDock.Bottom, restarted.SideLayout.DockOf(SidePanel.Files));
        Assert.Equal(300, restarted.SideLayout.SizeOf(PanelDock.Left));
        Assert.Equal(150, restarted.SideLayout.SizeOf(PanelDock.Bottom));
        Assert.Same(((Border)restarted.PanelStrip(PanelDock.Bottom)).Child, restarted.StripButton(SidePanel.Files).Parent);
    }

    [AvaloniaFact]
    public void A_profile_from_before_the_new_panels_gives_them_the_bottom_and_a_damaged_value_the_default_of_the_panel()
    {
        var old = PanelLayout.FromSaved(new SavedPanels { Documents = "right", Files = "left", Plugin = "left", Commit = "sideways", History = null! });
        Assert.Equal(PanelDock.Right, old.DockOf(SidePanel.Documents));
        Assert.Equal(PanelDock.Bottom, old.DockOf(SidePanel.Commit));
        Assert.Equal(PanelDock.Bottom, old.DockOf(SidePanel.History));
        Assert.Equal(PanelDock.Bottom, PanelLayout.FromSaved(null).DockOf(SidePanel.Commit));
        Assert.True(PanelLayout.FromSaved(null).IsDefault);
    }

    [AvaloniaFact]
    public void The_keyboard_command_of_a_panel_shows_it_then_takes_the_keyboard_there_then_hides_it()
    {
        var history = OpenHistory();
        _w.TogglePanel(SidePanel.History, false);
        Assert.Null(_w.ActiveHistory);

        Assert.Equal(Workbench.Commands.CommandOutcome.Handled, Run("nks.view.toggleHistoryPanel")); // shown, the keyboard goes in
        Settle();
        Assert.Same(history, _w.ActiveHistory);
        Assert.True(_w.HistoryTool.IsKeyboardFocusWithin);

        _w.FocusActiveView(); // the keyboard goes back to the text: the command brings it to the panel and does not hide it
        Settle();
        Assert.False(_w.HistoryTool.IsKeyboardFocusWithin);
        Run("nks.view.toggleHistoryPanel");
        Settle();
        Assert.True(_w.IsPanelOpen(SidePanel.History));
        Assert.True(_w.HistoryTool.IsKeyboardFocusWithin);

        Run("nks.view.toggleHistoryPanel"); // the keyboard is in it: hide
        Settle();
        Assert.False(_w.IsPanelOpen(SidePanel.History));
    }

    [AvaloniaFact]
    public void Alt_0_and_Alt_9_reach_the_commit_panel_and_the_history_and_Esc_hides_the_panel_the_keyboard_is_in()
    {
        var commit = OpenCommit();
        var history = OpenHistory();
        _w.TogglePanel(SidePanel.Commit, false);
        _w.TogglePanel(SidePanel.History, false);
        _w.NewDocument();
        Settle();

        TestApp.Press(_w, Key.D9, RawInputModifiers.Alt);
        Settle();
        Assert.Same(history, _w.ActiveHistory);
        TestApp.Press(_w, Key.Escape);
        Assert.False(_w.IsPanelOpen(SidePanel.History));

        TestApp.Press(_w, Key.D0, RawInputModifiers.Alt);
        Settle();
        Assert.True(_w.IsPanelOpen(SidePanel.Commit));
        Assert.True(commit.IsKeyboardFocusWithin);
        TestApp.Press(_w, Key.Escape);
        Assert.False(_w.IsPanelOpen(SidePanel.Commit));
    }

    [AvaloniaFact]
    public void The_command_of_the_commit_panel_without_a_repository_says_so_and_opens_nothing()
    {
        Loc.SetLanguage("en");
        Run("nks.view.toggleCommitPanel");
        Assert.Equal(Loc.Get(K.GitNoRepository), _w.StatusMessageText);
        Assert.False(_w.IsPanelOpen(SidePanel.Commit));
        Assert.Null(_w.CommitPanelView);
    }

    [AvaloniaFact]
    public void The_strips_the_hide_buttons_and_the_titles_have_names_in_every_language()
    {
        OpenCommit();
        OpenHistory();
        var names = new HashSet<string>();
        foreach (var language in Loc.Languages)
        {
            Loc.SetLanguage(language);
            _w.ApplyTexts();
            foreach (var panel in PanelLayout.Panels) Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(_w.StripButton(panel))), $"{language}: {panel}");
            Assert.Equal(Loc.Get(K.PanelHistoryName), AutomationProperties.GetName(_w.StripButton(SidePanel.History)));
            Assert.Equal(Loc.Get(K.A11yPanelStripBottom), AutomationProperties.GetName(_w.PanelStrip(PanelDock.Bottom)));
            Assert.Equal(Loc.Get(K.PanelToolHide, ("panel", Loc.Get(K.PanelCommitName))), AutomationProperties.GetName(_w.CommitTool.Close));
            Assert.DoesNotContain("{", AutomationProperties.GetName(_w.CommitTool.Close));
            names.Add(AutomationProperties.GetName(_w.HistoryTool.Close)!);
        }
        Assert.Equal(Loc.Languages.Count, names.Count);
    }
}
