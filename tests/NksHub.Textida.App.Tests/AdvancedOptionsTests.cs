using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Workbench;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.App.Tests;

/// <summary>The "Advanced" settings (P9.2.1): every one is listed here with the test that shows its effect.</summary>
[Collection("LocState")]
public class AdvancedOptionsTests : IDisposable
{
    // OPEN: list to confirm. The owner has not approved the set of advanced options yet; a new option is added here together with its effect test.
    private static readonly Dictionary<string, string> Covered = new()
    {
        ["advanced.scrollPastEnd"] = nameof(Scroll_past_end_reaches_the_editor),
        ["advanced.cutCopyWholeLine"] = nameof(Cut_copy_whole_line_reaches_the_editor),
        ["advanced.hideMouseWhileTyping"] = nameof(Hide_mouse_while_typing_reaches_the_editor),
        ["advanced.linkNeedsCtrl"] = nameof(Link_needs_ctrl_reaches_the_editor),
        ["advanced.wrapIndent"] = nameof(Wrap_indent_reaches_the_editor),
        ["advanced.recentFilesCount"] = nameof(Recent_files_count_cuts_the_list),
        ["advanced.autosaveIntervalSeconds"] = nameof(Autosave_interval_comes_from_the_setting),
        ["advanced.caretBlinkMilliseconds"] = nameof(Caret_blink_time_switches_between_the_caret_of_the_editor_and_the_one_drawn_here),
        ["advanced.smoothScrolling"] = nameof(Smooth_scrolling_glides_to_where_the_wheel_moves_the_text),
        ["advanced.undoLimit"] = nameof(Undo_limit_reaches_open_and_new_documents),
        ["advanced.highlightMaxLines"] = nameof(Highlight_limits_lower_the_window_around_the_screen_but_never_the_screen),
        ["advanced.highlightMaxColumns"] = nameof(Highlight_limits_lower_the_window_around_the_screen_but_never_the_screen),
    };

    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public AdvancedOptionsTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        _temp.Dispose();
    }

    [AvaloniaFact]
    public void Every_advanced_option_has_an_effect_test()
    {
        var advanced = WorkbenchHost.CreateBuiltin().Settings.Definitions.Where(d => d.Category == "cat.advanced").Select(d => d.Key).Order(StringComparer.Ordinal).ToList();
        var listed = Covered.Keys.Order(StringComparer.Ordinal).ToList();
        var missing = advanced.Except(listed).ToList();
        var stale = listed.Except(advanced).ToList();
        Assert.True(missing.Count == 0 && stale.Count == 0,
            $"Advanced options without a test entry: [{string.Join(", ", missing)}]; test entries without an option: [{string.Join(", ", stale)}].");
        Assert.All(Covered.Values, name => Assert.NotNull(typeof(AdvancedOptionsTests).GetMethod(name)));
    }

    private EditorSurface Surface() => _w.OpenFile(_temp.File("a.txt", "one two"), null).Editor!;

    [AvaloniaFact]
    public void Scroll_past_end_reaches_the_editor()
    {
        var surface = Surface();
        Assert.False(surface.Options.AllowScrollBelowDocument);
        Assert.Null(_app.Workbench.Set("advanced.scrollPastEnd", JsonValue.Create(true)));
        Assert.True(surface.Options.AllowScrollBelowDocument);
    }

    [AvaloniaFact]
    public void Cut_copy_whole_line_reaches_the_editor()
    {
        var surface = Surface();
        Assert.False(surface.Options.CutCopyWholeLine);
        Assert.Null(_app.Workbench.Set("advanced.cutCopyWholeLine", JsonValue.Create(true)));
        Assert.True(surface.Options.CutCopyWholeLine);
    }

    [AvaloniaFact]
    public void Hide_mouse_while_typing_reaches_the_editor()
    {
        var surface = Surface();
        Assert.True(surface.Options.HideCursorWhileTyping);
        Assert.Null(_app.Workbench.Set("advanced.hideMouseWhileTyping", JsonValue.Create(false)));
        Assert.False(surface.Options.HideCursorWhileTyping);
    }

    [AvaloniaFact]
    public void Link_needs_ctrl_reaches_the_editor()
    {
        var surface = Surface();
        Assert.True(surface.Options.RequireControlModifierForHyperlinkClick);
        Assert.Null(_app.Workbench.Set("advanced.linkNeedsCtrl", JsonValue.Create(false)));
        Assert.False(surface.Options.RequireControlModifierForHyperlinkClick);
    }

    [AvaloniaFact]
    public void Wrap_indent_reaches_the_editor()
    {
        var surface = Surface();
        Assert.Equal(0, surface.Options.WordWrapIndentation);
        Assert.Null(_app.Workbench.Set("advanced.wrapIndent", JsonValue.Create(8)));
        Assert.Equal(8, surface.Settings.WrapIndent);
        Assert.True(surface.Options.WordWrapIndentation > 0);
    }

    [AvaloniaFact]
    public void Recent_files_count_cuts_the_list()
    {
        Assert.Null(_app.Workbench.Set("advanced.recentFilesCount", JsonValue.Create(2)));
        foreach (var name in new[] { "r1.txt", "r2.txt", "r3.txt" }) _app.Recent.Add(_temp.File(name, "x"));
        Assert.Equal(2, _app.Recent.Files.Count);
        Assert.EndsWith("r3.txt", _app.Recent.Files[0]);
    }

    [AvaloniaFact]
    public async Task Autosave_interval_comes_from_the_setting()
    {
        Assert.Null(_app.Workbench.Set("advanced.autosaveIntervalSeconds", JsonValue.Create(1)));
        _app.Workbench.Set("files.autoSave", "afterDelay");
        var path = _temp.File("auto.txt", "a");
        var tab = _w.OpenFile(path, null);
        tab.Editor!.Document.Insert(1, "b");

        for (int i = 0; i < 80 && File.ReadAllText(path) == "a"; i++)
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal("ab", File.ReadAllText(path));
    }

    [AvaloniaFact]
    public void Caret_blink_time_switches_between_the_caret_of_the_editor_and_the_one_drawn_here()
    {
        var surface = Surface();
        Assert.False(surface.DrawsOwnCarets); // the usual time: the caret of AvaloniaEdit, untouched
        Assert.False(surface.CaretBlinking);

        Assert.Null(_app.Workbench.Set("advanced.caretBlinkMilliseconds", JsonValue.Create(250)));
        Assert.Equal(250, surface.Settings.CaretBlinkMs);
        Assert.True(surface.DrawsOwnCarets);
        Assert.Same(Brushes.Transparent, surface.TextArea.CaretBrush);
        Assert.True(surface.CaretBlinking);
        Assert.Equal(TimeSpan.FromMilliseconds(250), surface.CaretBlinkInterval);

        Assert.Null(_app.Workbench.Set("advanced.caretBlinkMilliseconds", JsonValue.Create(0)));
        Assert.True(surface.DrawsOwnCarets);
        Assert.False(surface.CaretBlinking); // 0: on, without blinking

        surface.ToggleBlockMode(); // block mode needs the caret of the editor, which stands beyond the end of a line
        Assert.False(surface.DrawsOwnCarets);
        Assert.NotSame(Brushes.Transparent, surface.TextArea.CaretBrush);
        surface.ToggleBlockMode();
        Assert.True(surface.DrawsOwnCarets);

        Assert.Null(_app.Workbench.Set("advanced.caretBlinkMilliseconds", JsonValue.Create(500)));
        Assert.False(surface.DrawsOwnCarets);
        Assert.False(surface.CaretBlinking);
        Assert.NotSame(Brushes.Transparent, surface.TextArea.CaretBrush);
    }

    [AvaloniaFact]
    public void Several_carets_blink_with_the_time_of_the_setting_too()
    {
        var surface = Surface();
        Assert.Null(_app.Workbench.Set("advanced.caretBlinkMilliseconds", JsonValue.Create(700)));
        surface.SetCarets(NksHub.Textida.AvaloniaEditAdapter.Carets.Create([Sel.At(0), Sel.At(4)], 1));
        Assert.True(surface.CaretBlinking);
        Assert.Equal(TimeSpan.FromMilliseconds(700), surface.CaretBlinkInterval);

        Assert.Null(_app.Workbench.Set("advanced.caretBlinkMilliseconds", JsonValue.Create(0)));
        Assert.False(surface.CaretBlinking);
        surface.ClearCarets();
        Assert.True(surface.DrawsOwnCarets); // one caret again, still drawn here because 0 is not the usual time
    }

    private static string Lines(int count) => string.Join('\n', Enumerable.Range(0, count).Select(i => "line " + i));

    [AvaloniaFact]
    public async Task Smooth_scrolling_glides_to_where_the_wheel_moves_the_text()
    {
        var tab = _w.OpenFile(_temp.File("long.txt", Lines(400)), null);
        _w.Width = 900;
        _w.Height = 600;
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var surface = tab.Editor!;
        var inside = surface.TranslatePoint(new Point(200, 100), _w)!.Value;
        double notch = PluginLimitsPlaceholders.WheelLinesPerNotch * surface.TextArea.TextView.DefaultLineHeight;

        // off: the wheel is left to the scroll viewer and nothing glides
        _w.MouseWheel(inside, new Vector(0, -1), RawInputModifiers.None);
        _w.UpdateLayout();
        Assert.False(surface.Gliding);
        surface.ScrollToVerticalOffset(0);
        _w.UpdateLayout();

        Assert.Null(_app.Workbench.Set("advanced.smoothScrolling", JsonValue.Create(true)));
        Assert.True(surface.Settings.SmoothScrolling);
        _w.MouseWheel(inside, new Vector(0, -2), RawInputModifiers.None);
        Assert.True(surface.Gliding);
        Assert.True(surface.VerticalOffset < 2 * notch, "the text must not jump to the target");

        bool between = false;
        for (int i = 0; i < 300 && surface.Gliding; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
            _w.UpdateLayout();
            between |= surface.VerticalOffset > 0 && surface.VerticalOffset < 2 * notch;
        }
        Assert.False(surface.Gliding);
        Assert.True(between, "the text must be seen between the start and the target");
        Assert.InRange(surface.VerticalOffset, 2 * notch - 1, 2 * notch + 1);

        // a notch back, and a key or the scroll bar moving the text ends a glide that is under way
        _w.MouseWheel(inside, new Vector(0, 1), RawInputModifiers.None);
        Assert.True(surface.Gliding);
        surface.ScrollToVerticalOffset(10 * notch);
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.False(surface.Gliding);
        Assert.InRange(surface.VerticalOffset, 10 * notch - 1, 10 * notch + 1);
    }

    [AvaloniaFact]
    public void Undo_limit_reaches_open_and_new_documents()
    {
        Assert.Null(_app.Workbench.Set("advanced.undoLimit", JsonValue.Create(3)));
        var tab = _w.OpenFile(_temp.File("u.txt", ""), null);
        Assert.Equal(3, tab.Doc.Doc.UndoLimit);
        var fresh = _w.NewDocument();
        Assert.Equal(3, fresh.Doc.Doc.UndoLimit);

        for (int i = 0; i < 6; i++) tab.Editor!.Document.Insert(tab.Editor.Document.TextLength, "x");
        int steps = 0;
        while (tab.Doc.Doc.CanUndo) { tab.Doc.Doc.Undo(); steps++; }
        Assert.Equal(3, steps);

        // lowering the limit reaches the documents that are open, and 0 keeps every step again
        for (int i = 0; i < 3; i++) tab.Editor!.Document.Insert(tab.Editor.Document.TextLength, "y");
        Assert.Null(_app.Workbench.Set("advanced.undoLimit", JsonValue.Create(1)));
        Assert.Equal(1, tab.Doc.Doc.UndoLimit);
        Assert.Equal(1, fresh.Doc.Doc.UndoLimit);
        Assert.Null(_app.Workbench.Set("advanced.undoLimit", JsonValue.Create(0)));
        Assert.Equal(0, tab.Doc.Doc.UndoLimit);
    }

    [AvaloniaFact]
    public void Highlight_limits_lower_the_window_around_the_screen_but_never_the_screen()
    {
        var syntax = Surface().Syntax;
        Assert.Equal((SyntaxFeeder.MaxRows, SyntaxFeeder.MaxColumns), (syntax.RowLimit, syntax.ColumnLimit));
        syntax.SetVisible(1000, 1040, 0, 80); // 41 lines on screen and the same number above and below
        Assert.Equal((959L, 123), (syntax.Requested.First, syntax.Requested.Count));

        Assert.Null(_app.Workbench.Set("advanced.highlightMaxLines", JsonValue.Create(100)));
        Assert.Null(_app.Workbench.Set("advanced.highlightMaxColumns", JsonValue.Create(1000)));
        Assert.Equal((100, 1000), (syntax.RowLimit, syntax.ColumnLimit));
        syntax.SetVisible(1000, 1040, 0, 80);
        var window = syntax.Requested;
        Assert.Equal(100, window.Count);
        Assert.True(window.First <= 1000 && 1040 < window.First + window.Count);

        // far to the right 800 columns are on screen: the window is 2400 columns wide (800 on each side), and the limit takes the margin down to 400 on the left
        syntax.SetLimits(SyntaxFeeder.MaxRows, SyntaxFeeder.MaxColumns);
        syntax.SetVisible(1000, 1040, 2000, 2800);
        Assert.Equal((1200, 2400), (syntax.Requested.Column, syntax.Requested.Columns));
        syntax.SetLimits(SyntaxFeeder.MaxRows, 1000);
        syntax.SetVisible(1000, 1040, 2000, 2800);
        window = syntax.Requested;
        Assert.Equal(1600, window.Columns);
        Assert.True(window.Column <= 2000 && 2800 <= window.Column + window.Columns);

        // a limit below the screen leaves the screen colored
        syntax.SetLimits(1, 1);
        syntax.SetVisible(1000, 1040, 0, 80);
        window = syntax.Requested;
        Assert.True(window.First <= 1000 && 1040 < window.First + window.Count);
        Assert.True(window.Column == 0 && window.Columns >= 80);

        // the settings cannot raise a limit above what the syntax session can do
        syntax.SetLimits(100_000, 10_000_000);
        Assert.Equal((SyntaxFeeder.MaxRows, SyntaxFeeder.MaxColumns), (syntax.RowLimit, syntax.ColumnLimit));
    }

    [AvaloniaFact]
    public void The_ranges_of_the_new_settings_stay_inside_what_the_code_can_do()
    {
        var definitions = WorkbenchHost.CreateBuiltin().Settings.Definitions;
        var lines = definitions.Single(d => d.Key == "advanced.highlightMaxLines");
        var columns = definitions.Single(d => d.Key == "advanced.highlightMaxColumns");
        Assert.Equal(SyntaxFeeder.MaxRows, (int)lines.Maximum!.Value);
        Assert.Equal(SyntaxFeeder.MaxRows, lines.Default!.GetValue<int>());
        Assert.Equal(SyntaxFeeder.MaxColumns, (int)columns.Maximum!.Value);
        Assert.Equal(SyntaxFeeder.MaxColumns, columns.Default!.GetValue<int>());
        Assert.Equal(EditorOptions.DefaultCaretBlinkMs, definitions.Single(d => d.Key == "advanced.caretBlinkMilliseconds").Default!.GetValue<int>());
    }
}
