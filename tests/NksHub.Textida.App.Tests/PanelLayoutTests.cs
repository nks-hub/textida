using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Profile;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>P3.6.9: the model of the layout of the side panels: limits, the sides a drop lands on, and what is kept in the profile.</summary>
public class PanelLayoutModelTests
{
    [Fact]
    public void A_side_has_a_smallest_and_a_largest_size_and_the_largest_is_at_most_half_of_the_window()
    {
        Assert.Equal((140, 640), PanelLayout.Limits(PanelDock.Left, 0));
        Assert.Equal((140, 640), PanelLayout.Limits(PanelDock.Right, 3000));
        Assert.Equal((140, 400), PanelLayout.Limits(PanelDock.Left, 800));
        Assert.Equal((140, 140), PanelLayout.Limits(PanelDock.Left, 200)); // a window too narrow for a panel still gets the smallest one
        Assert.Equal((80, 480), PanelLayout.Limits(PanelDock.Bottom, 0));
        Assert.Equal((80, 250), PanelLayout.Limits(PanelDock.Bottom, 500));
    }

    [Fact]
    public void A_size_outside_the_limits_is_brought_inside_and_a_number_that_is_none_is_the_smallest()
    {
        var layout = new PanelLayout();
        Assert.Equal(140, layout.SetSize(PanelDock.Left, 10));
        Assert.Equal(640, layout.SetSize(PanelDock.Left, 5000));
        Assert.Equal(300, layout.SetSize(PanelDock.Left, 300));
        Assert.Equal(400, layout.SetSize(PanelDock.Left, 600, window: 800));
        Assert.Equal(140, layout.SetSize(PanelDock.Right, double.NaN));
        Assert.Equal(80, layout.SetSize(PanelDock.Bottom, double.NegativeInfinity));
        Assert.Equal(400, layout.SizeOf(PanelDock.Left)); // the sides do not share a size
        Assert.Equal(80, layout.SizeOf(PanelDock.Bottom));
    }

    [Theory]
    [InlineData(10, 300, "left")]
    [InlineData(240, 300, "left")]
    [InlineData(500, 300, null)]
    [InlineData(760, 300, "right")]
    [InlineData(990, 10, "right")]
    [InlineData(500, 620, "bottom")]
    [InlineData(10, 700, "bottom")] // the lower quarter wins over the left one
    [InlineData(990, 790, "bottom")]
    [InlineData(-5, 300, null)]
    [InlineData(500, 801, null)]
    public void A_drop_lands_on_the_side_whose_quarter_it_is_in_and_in_the_middle_it_lands_nowhere(double x, double y, string? expected) =>
        Assert.Equal(expected, PanelLayout.DropSide(new Point(x, y), new Size(1000, 800)) is { } side ? PanelLayout.Name(side) : null);

    [Fact]
    public void An_area_with_no_size_takes_no_drop() => Assert.Null(PanelLayout.DropSide(new Point(1, 1), new Size(0, 0)));

    [Fact]
    public void The_layout_is_the_same_after_it_was_saved_and_read_again_also_through_the_file_of_the_profile()
    {
        var layout = new PanelLayout();
        layout.Place(SidePanel.Files, PanelDock.Right);
        layout.Place(SidePanel.Plugin, PanelDock.Bottom);
        layout.SetSize(PanelDock.Right, 333);
        layout.SetSize(PanelDock.Bottom, 150);

        using var temp = new TempDir();
        var store = new ProfileStore(temp.Path);
        store.SaveWindow(new SavedWindow { Width = 900, Height = 600, Panels = layout.ToSaved() });
        var back = PanelLayout.FromSaved(store.LoadWindow()!.Panels);

        Assert.Equal(PanelDock.Left, back.DockOf(SidePanel.Documents));
        Assert.Equal(PanelDock.Right, back.DockOf(SidePanel.Files));
        Assert.Equal(PanelDock.Bottom, back.DockOf(SidePanel.Plugin));
        Assert.Equal(220, back.SizeOf(PanelDock.Left));
        Assert.Equal(333, back.SizeOf(PanelDock.Right));
        Assert.Equal(150, back.SizeOf(PanelDock.Bottom));
        Assert.False(back.IsDefault);
        Assert.Contains("\"files\": \"right\"", File.ReadAllText(Path.Combine(temp.Path, "window.json")));
    }

    [Fact]
    public void A_profile_from_before_panels_could_be_moved_and_a_damaged_one_give_the_default()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "window.json"), """{ "x": 10, "y": 20, "width": 900, "height": 600, "maximized": false }""");
        var old = new ProfileStore(temp.Path).LoadWindow()!;
        Assert.Null(old.Panels);
        Assert.True(PanelLayout.FromSaved(old.Panels).IsDefault);

        var damaged = PanelLayout.FromSaved(new SavedPanels { Documents = "sideways", Files = "", Plugin = null!, LeftWidth = double.NaN, RightWidth = -40, BottomHeight = 1e9 });
        Assert.All(PanelLayout.Panels, p => Assert.Equal(PanelDock.Left, damaged.DockOf(p)));
        Assert.Equal(140, damaged.SizeOf(PanelDock.Left));
        Assert.Equal(140, damaged.SizeOf(PanelDock.Right));
        Assert.Equal(480, damaged.SizeOf(PanelDock.Bottom));

        File.WriteAllText(Path.Combine(temp.Path, "window.json"), """{ "panels": { "files": "right", "leftWidth": "wide" } }""");
        Assert.Null(new ProfileStore(temp.Path).LoadWindow()); // not readable: the window opens with its defaults, and so do the panels
    }

    [Fact]
    public void The_copy_of_a_layout_does_not_follow_the_original()
    {
        var layout = new PanelLayout();
        var copy = layout.Copy();
        layout.Place(SidePanel.Documents, PanelDock.Bottom);
        layout.SetSize(PanelDock.Left, 300);
        Assert.True(copy.IsDefault);
        Assert.False(layout.IsDefault);
    }
}

/// <summary>P3.6.9: the side panels in a window. They resize by a handle with limits and the resize cursor, dock left, right or at the bottom by drag and by command, hide, come back as they were after a restart, and say what they did.</summary>
[Collection("LocState")]
public class PanelLayoutTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public PanelLayoutTests()
    {
        _app = TestApp.Prepare(_temp, new ScriptedDialogs());
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private void Show(MainWindow? window = null)
    {
        window ??= _w;
        window.Width = 1000;
        window.Height = 700;
        window.Show();
        window.UpdateLayout();
    }

    /// <summary>Lets the jobs that hand the keyboard to a panel run, so that a test which then moves the keyboard elsewhere is not overtaken by them.</summary>
    private void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        _w.UpdateLayout();
    }

    private static Point CentreOf(MainWindow window, Control control) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    /// <summary>A point of the area the panels and the text share, as a share of its width and height, in the coordinates of the window.</summary>
    private Point AtBody(double x, double y) => _w.Body.TranslatePoint(new Point(_w.Body.Bounds.Width * x, _w.Body.Bounds.Height * y), _w)!.Value;

    private Workbench.Commands.CommandOutcome Run(string id) => _app.Workbench.Commands.ExecuteAsync(id, new ShellContext(_w, forMenu: true)).AsTask().GetAwaiter().GetResult();

    [AvaloniaFact]
    public void Every_panel_starts_on_the_left_at_the_default_size_and_only_the_one_asked_for_is_shown()
    {
        Show();
        Assert.All(PanelLayout.Panels, p => Assert.Equal(PanelDock.Left, _w.SideLayout.DockOf(p)));
        Assert.All(PanelLayout.Docks, d => Assert.Null(_w.PanelShown(d)));
        Assert.False(_w.PanelHost(PanelDock.Left).IsVisible);

        _w.ToggleDocuments(true);
        _w.UpdateLayout();
        Assert.Equal(SidePanel.Documents, _w.PanelShown(PanelDock.Left));
        Assert.Equal(220, _w.PanelHost(PanelDock.Left).Bounds.Width);
        Assert.True(_w.SplitterOf(PanelDock.Left).IsVisible);
        Assert.False(_w.PanelHost(PanelDock.Right).IsVisible);

        _w.ToggleFiles(true); // the same side: the files replace the documents
        Assert.Equal(SidePanel.Files, _w.PanelShown(PanelDock.Left));
        Assert.False(_w.DocumentsPanelOpen);
        Assert.True(_w.FilePanelOpen);

        _w.ToggleFiles(false);
        _w.UpdateLayout();
        Assert.Null(_w.PanelShown(PanelDock.Left));
        Assert.False(_w.PanelHost(PanelDock.Left).IsVisible);
        Assert.False(_w.SplitterOf(PanelDock.Left).IsVisible);
    }

    [AvaloniaFact]
    public void Dragging_the_handle_changes_the_width_inside_the_limits_and_the_pointer_over_it_is_the_resize_cursor()
    {
        Show();
        _w.ToggleFiles(true);
        _w.UpdateLayout();
        var handle = _w.SplitterOf(PanelDock.Left);
        Assert.True(handle.IsVisible && handle.Bounds.Width == 4 && handle.Bounds.Height > 100, $"the handle is {handle.Bounds}");
        Assert.Equal(StandardCursorType.SizeWestEast, handle.CursorType);
        Assert.NotNull(handle.Cursor);
        Assert.Equal(StandardCursorType.SizeNorthSouth, _w.SplitterOf(PanelDock.Bottom).CursorType);
        Assert.Equal(StandardCursorType.SizeWestEast, _w.SplitterOf(PanelDock.Right).CursorType);

        var from = CentreOf(_w, handle);
        _w.MouseDown(from, MouseButton.Left);
        _w.MouseMove(from + new Vector(60, 0), RawInputModifiers.LeftMouseButton);
        Assert.Equal(280, _w.SideLayout.SizeOf(PanelDock.Left));
        _w.UpdateLayout();
        Assert.Equal(280, _w.PanelHost(PanelDock.Left).Bounds.Width);

        _w.MouseMove(from + new Vector(900, 0), RawInputModifiers.LeftMouseButton);
        Assert.Equal(handle.Range.Max, _w.SideLayout.SizeOf(PanelDock.Left));
        Assert.True(handle.Range.Max <= 500, "no more than half of the window");
        _w.MouseMove(from - new Vector(900, 0), RawInputModifiers.LeftMouseButton);
        Assert.Equal(140, _w.SideLayout.SizeOf(PanelDock.Left));
        _w.MouseMove(from + new Vector(100, 0), RawInputModifiers.LeftMouseButton);
        _w.MouseUp(from + new Vector(100, 0), MouseButton.Left);
        Assert.Equal(320, _w.SideLayout.SizeOf(PanelDock.Left));
        Assert.Contains("320", _w.AnnouncedText); // the end of the drag says the size

        _w.MouseMove(from + new Vector(300, 0)); // the button is up: the pointer no longer drags
        Assert.Equal(320, _w.SideLayout.SizeOf(PanelDock.Left));
    }

    [AvaloniaFact]
    public void The_arrow_keys_on_the_handle_change_the_size_by_a_step_and_a_screen_reader_hears_the_size()
    {
        Show();
        _w.ToggleFiles(true);
        _w.MovePanel(SidePanel.Files, PanelDock.Right);
        Settle();
        var handle = _w.SplitterOf(PanelDock.Right);
        Assert.True(handle.Focus(), $"the handle is {handle.Bounds}, visible {handle.IsVisible}, effective {handle.IsEffectivelyVisible}");
        Assert.True(handle.IsFocused);

        TestApp.Press(_w, Key.Left); // on the right side the panel grows towards the text
        Assert.Equal(240, _w.SideLayout.SizeOf(PanelDock.Right));
        Assert.Contains("240", _w.AnnouncedText);
        Assert.Contains(Loc.Get(K.PanelFilesTitle), _w.AnnouncedText);
        TestApp.Press(_w, Key.Right, RawInputModifiers.Shift);
        Assert.Equal(140, _w.SideLayout.SizeOf(PanelDock.Right));
        TestApp.Press(_w, Key.Right);
        Assert.Equal(140, _w.SideLayout.SizeOf(PanelDock.Right));
        Assert.Contains(Loc.Get(K.A11yPanelLimit), _w.AnnouncedText); // it cannot go further, and the reader is told
        TestApp.Press(_w, Key.End);
        Assert.Equal(handle.Range.Max, _w.SideLayout.SizeOf(PanelDock.Right));
        TestApp.Press(_w, Key.Home);
        Assert.Equal(140, _w.SideLayout.SizeOf(PanelDock.Right));

        var peer = Assert.IsType<PanelSplitterAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(handle));
        Assert.Equal(AutomationControlType.Slider, peer.GetAutomationControlType());
        Assert.Equal(140, peer.Value);
        Assert.Equal(140, peer.Minimum);
        Assert.Equal(handle.Range.Max, peer.Maximum);
        peer.SetValue(300);
        Assert.Equal(300, _w.SideLayout.SizeOf(PanelDock.Right));
        Assert.Equal(300, peer.Value);
    }

    [AvaloniaFact]
    public void The_bottom_handle_changes_the_height_and_its_keys_are_up_and_down()
    {
        Show();
        _w.ToggleFiles(true);
        _w.MovePanel(SidePanel.Files, PanelDock.Bottom);
        Settle();
        Assert.Equal(200, _w.PanelHost(PanelDock.Bottom).Bounds.Height);
        var handle = _w.SplitterOf(PanelDock.Bottom);
        handle.Focus();
        TestApp.Press(_w, Key.Up);
        Assert.Equal(220, _w.SideLayout.SizeOf(PanelDock.Bottom));
        TestApp.Press(_w, Key.Down, RawInputModifiers.Shift);
        Assert.Equal(120, _w.SideLayout.SizeOf(PanelDock.Bottom));
        Assert.Contains(Loc.Get(K.A11yPanelHeight, ("panel", Loc.Get(K.PanelFilesTitle)), ("size", 120)), _w.AnnouncedText);

        var from = CentreOf(_w, handle);
        _w.MouseDown(from, MouseButton.Left);
        _w.MouseMove(from - new Vector(0, 50), RawInputModifiers.LeftMouseButton); // dragging up makes the panel higher
        _w.MouseUp(from - new Vector(0, 50), MouseButton.Left);
        Assert.Equal(170, _w.SideLayout.SizeOf(PanelDock.Bottom));
    }

    [AvaloniaFact]
    public void The_commands_dock_the_open_panel_to_each_side_and_say_where_it_went()
    {
        Show();
        _w.ToggleFiles(true);

        Run("nks.panel.dockRight");
        Assert.Equal(PanelDock.Right, _w.SideLayout.DockOf(SidePanel.Files));
        Assert.Equal(SidePanel.Files, _w.PanelShown(PanelDock.Right));
        Assert.Null(_w.PanelShown(PanelDock.Left));
        Assert.Equal(Loc.Get(K.A11yPanelDockedRight, ("panel", Loc.Get(K.PanelFilesTitle))), _w.AnnouncedText);
        _w.UpdateLayout();
        Assert.True(_w.PanelHost(PanelDock.Right).IsVisible);
        Assert.Same(_w.PanelHost(PanelDock.Right), _w.FilePanelControl.Parent?.Parent);
        Assert.Equal(220, _w.PanelHost(PanelDock.Right).Bounds.Width);
        Assert.False(_w.PanelHost(PanelDock.Left).IsVisible);

        Run("nks.panel.dockBottom");
        Assert.Equal(SidePanel.Files, _w.PanelShown(PanelDock.Bottom));
        Assert.Equal(Loc.Get(K.A11yPanelDockedBottom, ("panel", Loc.Get(K.PanelFilesTitle))), _w.AnnouncedText);
        _w.UpdateLayout();
        Assert.Equal(200, _w.PanelHost(PanelDock.Bottom).Bounds.Height);
        Assert.True(_w.PanelHost(PanelDock.Bottom).Bounds.Width > 900, "the bottom panel is as wide as the window");

        Run("nks.panel.dockLeft");
        Assert.Equal(SidePanel.Files, _w.PanelShown(PanelDock.Left));
        Assert.Equal(Loc.Get(K.A11yPanelDockedLeft, ("panel", Loc.Get(K.PanelFilesTitle))), _w.AnnouncedText);
        Assert.True(_w.FilePanelOpen);
    }

    [AvaloniaFact]
    public void Panels_on_different_sides_are_open_together_and_each_one_closes_alone()
    {
        Show();
        _w.ToggleDocuments(true);
        _w.ToggleFiles(true); // replaces the documents on the left
        Run("nks.panel.dockRight"); // the files go right
        _w.ToggleDocuments(true); // and the documents come back on the left

        Assert.Equal(SidePanel.Documents, _w.PanelShown(PanelDock.Left));
        Assert.Equal(SidePanel.Files, _w.PanelShown(PanelDock.Right));
        Assert.True(_w.DocumentsPanelOpen && _w.FilePanelOpen);

        TestApp.Press(_w, Key.E, TestApp.Mod | RawInputModifiers.Shift);
        Assert.False(_w.FilePanelOpen);
        Assert.True(_w.DocumentsPanelOpen);
        TestApp.Press(_w, Key.B, TestApp.Mod);
        Assert.False(_w.DocumentsPanelOpen);
        TestApp.Press(_w, Key.E, TestApp.Mod | RawInputModifiers.Shift);
        Assert.Equal(SidePanel.Files, _w.PanelShown(PanelDock.Right)); // it opens where it is docked
    }

    [AvaloniaFact]
    public void A_closed_panel_that_is_moved_opens_on_its_new_side_the_next_time()
    {
        Show();
        _w.ToggleFiles(true);
        _w.ToggleFiles(false);
        _w.MovePanel(SidePanel.Files, PanelDock.Bottom);
        Assert.Null(_w.PanelShown(PanelDock.Bottom));
        Assert.Equal(PanelDock.Bottom, _w.SideLayout.DockOf(SidePanel.Files));
        _w.ToggleFiles(true);
        Assert.Equal(SidePanel.Files, _w.PanelShown(PanelDock.Bottom));
    }

    [AvaloniaFact]
    public void Dragging_the_title_of_a_panel_to_a_side_docks_it_there_and_a_drop_in_the_middle_changes_nothing()
    {
        Show();
        _w.ToggleFiles(true);
        _w.UpdateLayout();
        var title = _w.PanelHeader(SidePanel.Files);
        var from = CentreOf(_w, title);

        _w.MouseDown(from, MouseButton.Left);
        _w.MouseMove(from + new Vector(2, 1), RawInputModifiers.LeftMouseButton);
        Assert.False(_w.DropHint.IsVisible, "a small move is a click, not a drag");
        _w.MouseMove(AtBody(0.5, 0.4), RawInputModifiers.LeftMouseButton);
        Assert.False(_w.DropHint.IsVisible, "the middle of the window takes no panel");
        _w.MouseMove(AtBody(0.95, 0.4), RawInputModifiers.LeftMouseButton);
        Assert.True(_w.DropHint.IsVisible);
        Assert.Equal(HorizontalAlignmentOf(PanelDock.Right), _w.DropHint.HorizontalAlignment);
        _w.MouseMove(AtBody(0.5, 0.4), RawInputModifiers.LeftMouseButton);
        _w.MouseUp(AtBody(0.5, 0.4), MouseButton.Left);
        Assert.False(_w.DropHint.IsVisible);
        Assert.Equal(SidePanel.Files, _w.PanelShown(PanelDock.Left));

        _w.UpdateLayout();
        from = CentreOf(_w, _w.PanelHeader(SidePanel.Files));
        _w.MouseDown(from, MouseButton.Left);
        _w.MouseMove(AtBody(0.95, 0.4), RawInputModifiers.LeftMouseButton);
        _w.MouseUp(AtBody(0.95, 0.4), MouseButton.Left);
        Assert.Equal(SidePanel.Files, _w.PanelShown(PanelDock.Right));
        Assert.Null(_w.PanelShown(PanelDock.Left));
        Assert.False(_w.DropHint.IsVisible);
        Assert.Contains(Loc.Get(K.A11yPanelDockedRight, ("panel", Loc.Get(K.PanelFilesTitle))), _w.AnnouncedText);

        _w.UpdateLayout();
        from = CentreOf(_w, _w.PanelHeader(SidePanel.Files));
        _w.MouseDown(from, MouseButton.Left);
        _w.MouseMove(AtBody(0.5, 0.9), RawInputModifiers.LeftMouseButton);
        _w.MouseUp(AtBody(0.5, 0.9), MouseButton.Left);
        Assert.Equal(SidePanel.Files, _w.PanelShown(PanelDock.Bottom));
    }

    private static Avalonia.Layout.HorizontalAlignment HorizontalAlignmentOf(PanelDock dock) => dock switch
    {
        PanelDock.Left => Avalonia.Layout.HorizontalAlignment.Left, PanelDock.Right => Avalonia.Layout.HorizontalAlignment.Right, _ => Avalonia.Layout.HorizontalAlignment.Stretch,
    };

    [AvaloniaFact]
    public void The_header_of_each_panel_offers_the_sides_hiding_and_the_reset()
    {
        foreach (var panel in PanelLayout.Panels)
        {
            var menu = _w.PanelHeader(panel).ContextMenu;
            Assert.NotNull(menu);
            Assert.Equal(["Ukotvit panel vlevo", "Ukotvit panel vpravo", "Ukotvit panel dole", "Skrýt panel", "Vrátit výchozí rozložení panelů"], menu.Items.OfType<MenuItem>().Select(m => (string)m.Header!));
        }
        _w.ToggleDocuments(true);
        ((MenuItem)_w.PanelHeader(SidePanel.Documents).ContextMenu!.Items[1]!).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(SidePanel.Documents, _w.PanelShown(PanelDock.Right));
    }

    [AvaloniaFact]
    public void Hide_closes_the_panel_the_keyboard_is_in_and_says_so_and_the_commands_are_off_when_no_panel_is_open()
    {
        Show();
        _w.NewDocument();
        _w.ShowPalette();
        _w.SetPaletteQuery("Ukotvit panel");
        var rows = _w.PaletteResults.Where(r => r.CommandId.StartsWith("nks.panel.dock", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal("Není otevřený žádný panel", r.DisabledReason));
        _w.ClosePalette();
        Assert.Equal(Workbench.Commands.CommandOutcome.Disabled, Run("nks.panel.dockLeft"));

        _w.ToggleDocuments(true);
        _w.ToggleFiles(true);
        _w.MovePanel(SidePanel.Files, PanelDock.Right);
        _w.FileList.Focus();
        Assert.Equal(SidePanel.Files, _w.CurrentSidePanel());
        Run("nks.panel.hide");
        Assert.False(_w.FilePanelOpen);
        Assert.Equal(Loc.Get(K.A11yPanelHidden, ("panel", Loc.Get(K.PanelFilesTitle))), _w.AnnouncedText);
        Assert.Null(_w.PanelShown(PanelDock.Right));

        Assert.Equal(Workbench.Commands.CommandOutcome.Handled, Run("nks.panel.resetLayout")); // always possible
    }

    [AvaloniaFact]
    public void The_commands_grow_and_shrink_take_the_panel_by_a_step_and_the_keys_are_chords_of_the_view_commands()
    {
        Show();
        _w.ToggleFiles(true);
        Run("nks.panel.grow");
        Assert.Equal(240, _w.SideLayout.SizeOf(PanelDock.Left));
        Run("nks.panel.shrink");
        Run("nks.panel.shrink");
        Assert.Equal(200, _w.SideLayout.SizeOf(PanelDock.Left));
        for (int i = 0; i < 10; i++) Run("nks.panel.shrink");
        Assert.Equal(140, _w.SideLayout.SizeOf(PanelDock.Left));
        Assert.Contains(Loc.Get(K.A11yPanelLimit), _w.AnnouncedText);

        var keys = _app.Workbench.Keymap.Bindings.Where(b => b.CommandId.StartsWith("nks.panel.", StringComparison.Ordinal)).ToDictionary(b => b.CommandId, b => b.Sequence.ToString());
        Assert.Equal(["nks.panel.dockBottom", "nks.panel.dockLeft", "nks.panel.dockRight", "nks.panel.grow", "nks.panel.shrink"], keys.Keys.Order(StringComparer.Ordinal));

        // a chord of the keyboard reaches the command
        TestApp.Press(_w, Key.K, TestApp.Mod);
        TestApp.Press(_w, Key.Right, TestApp.Mod);
        Assert.Equal(SidePanel.Files, _w.PanelShown(PanelDock.Right));
    }

    [AvaloniaFact]
    public void The_reset_puts_every_panel_on_the_left_at_the_default_size_and_the_open_one_stays_open()
    {
        Show();
        _w.ToggleFiles(true);
        _w.MovePanel(SidePanel.Files, PanelDock.Bottom);
        _w.SetPanelSize(PanelDock.Bottom, 350, final: true);
        _w.MovePanel(SidePanel.Documents, PanelDock.Right);
        _w.SetPanelSize(PanelDock.Right, 300, final: true);
        Assert.False(_w.SideLayout.IsDefault);

        Run("nks.panel.resetLayout");

        Assert.True(_w.SideLayout.IsDefault);
        Assert.True(_w.FilePanelOpen);
        Assert.Equal(SidePanel.Files, _w.PanelShown(PanelDock.Left));
        Assert.Null(_w.PanelShown(PanelDock.Bottom));
        Assert.Equal(Loc.Get(K.A11yPanelReset), _w.AnnouncedText);
        _w.UpdateLayout();
        Assert.Equal(220, _w.PanelHost(PanelDock.Left).Bounds.Width);
        Assert.True(_w.CaptureGeometry().Panels is { Files: "left", Documents: "left", LeftWidth: 220, RightWidth: 220, BottomHeight: 200 });
    }

    [AvaloniaFact]
    public void The_layout_goes_with_the_geometry_of_the_window_through_the_profile_and_comes_back_in_a_new_window()
    {
        Show();
        _w.MovePanel(SidePanel.Files, PanelDock.Right);
        _w.MovePanel(SidePanel.Plugin, PanelDock.Bottom);
        _w.SetPanelSize(PanelDock.Right, 310, final: true);
        _w.SetPanelSize(PanelDock.Bottom, 130, final: true);

        var store = new ProfileStore(_temp.Path);
        store.SaveWindow(_w.CaptureGeometry());
        var restarted = _app.CreateWindow(store.LoadWindow());

        Assert.Equal(PanelDock.Left, restarted.SideLayout.DockOf(SidePanel.Documents));
        Assert.Equal(PanelDock.Right, restarted.SideLayout.DockOf(SidePanel.Files));
        Assert.Equal(PanelDock.Bottom, restarted.SideLayout.DockOf(SidePanel.Plugin));
        Assert.Equal(310, restarted.SideLayout.SizeOf(PanelDock.Right));
        Assert.Equal(130, restarted.SideLayout.SizeOf(PanelDock.Bottom));
        restarted.ToggleFiles(true);
        Assert.Equal(SidePanel.Files, restarted.PanelShown(PanelDock.Right));
        Show(restarted);
        Assert.Equal(310, restarted.PanelHost(PanelDock.Right).Bounds.Width);
        Assert.True(restarted.SplitterOf(PanelDock.Right).IsVisible);

        // and a window made from this one starts the same way
        var copy = _app.NewWindow(_w);
        Assert.Equal(PanelDock.Right, copy.SideLayout.DockOf(SidePanel.Files));
    }

    [AvaloniaFact]
    public void The_handles_and_the_messages_have_names_in_every_language()
    {
        Show();
        _w.ToggleFiles(true);
        var names = new List<string>();
        foreach (var language in Loc.Languages)
        {
            Loc.SetLanguage(language);
            var left = AutomationProperties.GetName(_w.SplitterOf(PanelDock.Left));
            Assert.Equal(Loc.Get(K.A11yPanelSplitterLeft), left);
            Assert.Equal(Loc.Get(K.A11yPanelSplitterRight), AutomationProperties.GetName(_w.SplitterOf(PanelDock.Right)));
            Assert.Equal(Loc.Get(K.A11yPanelSplitterBottom), AutomationProperties.GetName(_w.SplitterOf(PanelDock.Bottom)));
            names.Add(left);
            Run("nks.panel.grow");
            Assert.Contains(Loc.Get(K.PanelFilesTitle), _w.AnnouncedText);
            Assert.DoesNotContain("{", _w.AnnouncedText);
            Assert.Equal(Loc.Get(K.A11yPanelWidth, ("panel", Loc.Get(K.PanelFilesTitle)), ("size", (int)_w.SideLayout.SizeOf(PanelDock.Left))), _w.AnnouncedText);
        }
        Assert.Equal(Loc.Languages.Count, names.Distinct().Count());
        Assert.Equal(Loc.Languages.Count, Loc.Languages.Select(l => { Loc.SetLanguage(l); return Loc.Get(K.A11yPanelDockedBottom, ("panel", "x")); }).Distinct().Count());
    }

    [AvaloniaFact]
    public void F6_goes_through_the_panels_on_every_side_in_reading_order()
    {
        Show();
        _w.NewDocument();
        _w.ToggleFiles(true);
        _w.MovePanel(SidePanel.Files, PanelDock.Right);
        _w.ToggleDocuments(true);
        _w.MovePanel(SidePanel.Documents, PanelDock.Bottom);
        Settle();
        Assert.Equal(SidePanel.Files, _w.PanelShown(PanelDock.Right));
        Assert.Equal(SidePanel.Documents, _w.PanelShown(PanelDock.Bottom));

        var order = new List<PanelDock?>();
        for (int i = 0; i < 4; i++)
        {
            _w.FocusNextPane(forward: true);
            Dispatcher.UIThread.RunJobs();
            order.Add(PanelLayout.Docks.Cast<PanelDock?>().FirstOrDefault(d => _w.PanelHost(d!.Value).IsKeyboardFocusWithin));
        }
        Assert.Contains(PanelDock.Right, order);
        Assert.Contains(PanelDock.Bottom, order);
        Assert.True(order.IndexOf(PanelDock.Right) < order.IndexOf(PanelDock.Bottom), "the right side comes before the bottom");
    }

    [AvaloniaFact]
    public void Esc_closes_the_files_or_the_documents_wherever_they_are_docked()
    {
        Show();
        _w.NewDocument();
        _w.ToggleFiles(true);
        _w.MovePanel(SidePanel.Files, PanelDock.Right);
        TestApp.Press(_w, Key.Escape);
        Assert.False(_w.FilePanelOpen);
    }
}
