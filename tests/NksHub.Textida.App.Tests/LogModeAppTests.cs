using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>P4.3.2 in the app: lines colored by level on both surfaces and the jump to the next or previous error.</summary>
[Collection("LocState")]
public class LogModeAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private App _app;
    private MainWindow _w;

    public LogModeAppTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
        _w.Width = 1200;
        _w.Height = 800;
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private void Settle(int rounds = 30)
    {
        for (int i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    private DocTab Open(string name, string content, bool paged = false)
    {
        if (paged) _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        var tab = _w.OpenFile(_temp.File(name, content), null);
        Assert.Equal(paged, tab.Doc.IsLarge);
        if (paged) ToolTestKit.WaitFor(() => tab.Paged!.Index.Complete, "line index");
        _w.UpdateLayout();
        Settle();
        return tab;
    }

    private sealed record Shot(byte[] Data, int Stride);

    private Shot Capture()
    {
        _w.UpdateLayout();
        Settle(10);
        var frame = _w.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        var data = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, data, 0, data.Length);
        return new Shot(data, buffer.RowBytes);
    }

    private static int Matching(Shot shot, Rect area, Color color, int tolerance = 3)
    {
        int count = 0;
        for (int y = (int)area.Top; y < (int)area.Bottom; y++)
            for (int x = (int)area.Left; x < (int)area.Right; x++)
            {
                int at = y * shot.Stride + x * 4;
                if (at + 3 >= shot.Data.Length) continue;
                byte b = shot.Data[at], g = shot.Data[at + 1], r = shot.Data[at + 2];
                // the headless frame is RGBA or BGRA depending on the platform: accept both orders
                if (Math.Abs(color.G - g) <= tolerance && ((Math.Abs(color.R - r) <= tolerance && Math.Abs(color.B - b) <= tolerance) || (Math.Abs(color.R - b) <= tolerance && Math.Abs(color.B - r) <= tolerance))) count++;
            }
        return count;
    }

    private static Color Level(LogLevel level) => ((ISolidColorBrush)SyntaxPalette.Light.GetBrush(LogLevelKinds.Of(level))!).Color;

    private Rect InWindow(Visual visual, Rect local)
    {
        var origin = visual.TranslatePoint(default, _w)!.Value;
        return new Rect(origin.X + local.X, origin.Y + local.Y, local.Width, local.Height);
    }

    /// <summary>The given text line of the editor, from the third column to the right edge.</summary>
    private Rect EditorRow(DocTab tab, int row)
    {
        var view = tab.Editor!.TextArea.TextView;
        view.EnsureVisualLines();
        var line = view.VisualLines[row];
        double x = 2 * view.WideSpaceWidth;
        return InWindow(view, new Rect(x, line.VisualTop - view.ScrollOffset.Y, Math.Max(10, view.Bounds.Width - x), line.Height));
    }

    private Rect PagedRow(DocTab tab, int row) => InWindow(tab.Paged!, new Rect(70, row * 17, 500, 17));

    private static readonly string Sample = "ERROR disk is full\nINFO all fine here\nWARN low memory\nDEBUG details\nnothing special on this line\n";

    [AvaloniaFact]
    public void A_log_file_in_the_editor_has_its_lines_colored_by_level_and_a_text_file_has_not()
    {
        var plain = Open("a.txt", Sample);
        var plainShot = Capture();
        int plainErrors = Matching(plainShot, EditorRow(plain, 0), Level(LogLevel.Error));
        _w.CloseTab(plain);

        var log = Open("a.log", Sample);
        Assert.True(log.Doc.IsLog);
        var shot = Capture();
        Assert.Equal(0, plainErrors);
        Assert.True(Matching(shot, EditorRow(log, 0), Level(LogLevel.Error)) > 10, "error line");
        Assert.True(Matching(shot, EditorRow(log, 1), Level(LogLevel.Info)) > 10, "info line");
        Assert.True(Matching(shot, EditorRow(log, 2), Level(LogLevel.Warning)) > 10, "warning line");
        Assert.True(Matching(shot, EditorRow(log, 3), Level(LogLevel.Debug)) > 10, "debug line");
        foreach (var level in Enum.GetValues<LogLevel>()) Assert.Equal(0, Matching(shot, EditorRow(log, 4), Level(level)));
        Assert.Equal(0, Matching(shot, EditorRow(log, 0), Level(LogLevel.Info)));
    }

    [AvaloniaFact]
    public void Choosing_the_language_log_colors_a_document_and_changing_it_back_removes_the_colors()
    {
        var tab = Open("b.txt", Sample);
        _w.UpdateLayout();
        Assert.Equal(0, Matching(Capture(), EditorRow(tab, 0), Level(LogLevel.Error)));
        tab.Doc.ChooseLanguage(LanguageModes.ById(LanguageModes.LogId));
        Assert.True(Matching(Capture(), EditorRow(tab, 0), Level(LogLevel.Error)) > 10);
        tab.Doc.ChooseLanguage(LanguageModes.Plain);
        Assert.Equal(0, Matching(Capture(), EditorRow(tab, 0), Level(LogLevel.Error)));
    }

    private static string BigLog(out List<long> errorOffsets, int lines = 60_000)
    {
        var sb = new StringBuilder();
        errorOffsets = [];
        for (int i = 0; i < lines; i++)
        {
            if (i == 0 || i == 20_000 || i == 45_000) errorOffsets.Add(sb.Length);
            string level = errorOffsets.Count > 0 && errorOffsets[^1] == sb.Length ? "ERROR" : "INFO ";
            sb.Append($"2026-01-01 12:00:00 {level} request number {i} handled in a few milliseconds\n");
        }
        return sb.ToString();
    }

    [AvaloniaFact]
    public void A_log_file_on_the_paged_surface_has_its_lines_colored_by_level()
    {
        var log = Open("big.log", BigLog(out _), paged: true);
        Assert.True(log.Doc.IsLog);
        var shot = Capture();
        Assert.True(Matching(shot, PagedRow(log, 0), Level(LogLevel.Error)) > 10, "error line");
        Assert.True(Matching(shot, PagedRow(log, 1), Level(LogLevel.Info)) > 10, "info line");
        Assert.Equal(0, Matching(shot, PagedRow(log, 1), Level(LogLevel.Error)));
    }

    private static readonly Color AnsiRed = Color.FromRgb(0xCD, 0x31, 0x31); // the standard red of AnsiColor.Indexed(1)

    [AvaloniaFact]
    public void An_ansi_style_left_open_colors_the_next_lines_of_a_log_in_the_editor_until_a_code_resets_it()
    {
        var log = Open("carry.log", "\x1b[31mred starts here\nsecond line goes on\n\x1b[0mthird line is plain\nfourth line is plain\n");
        var shot = Capture();
        Assert.True(Matching(shot, EditorRow(log, 0), AnsiRed) > 10, "the line with the code");
        Assert.True(Matching(shot, EditorRow(log, 1), AnsiRed) > 10, "the line after it, which has no code of its own");
        Assert.Equal(0, Matching(shot, EditorRow(log, 2), AnsiRed));
        Assert.Equal(0, Matching(shot, EditorRow(log, 3), AnsiRed));
        Assert.StartsWith("\x1b[31m", log.Editor!.Document.Text, StringComparison.Ordinal); // the text keeps its escapes

        // the style of a line is read again after an edit: a reset typed at the start of the second line ends the red there
        log.Editor.Document.Insert(log.Editor.Document.GetLineByNumber(2).Offset, "\x1b[0m");
        shot = Capture();
        Assert.True(Matching(shot, EditorRow(log, 0), AnsiRed) > 10);
        Assert.Equal(0, Matching(shot, EditorRow(log, 1), AnsiRed));

        // the escapes shown as they are in the file: no colors at all
        log.Editor.AnsiRaw = true;
        Assert.Equal(0, Matching(Capture(), EditorRow(log, 0), AnsiRed));
        log.Editor.AnsiRaw = false;
        Assert.True(Matching(Capture(), EditorRow(log, 0), AnsiRed) > 10);
    }

    [AvaloniaFact]
    public void An_ansi_style_left_open_colors_the_next_lines_on_the_paged_surface_whatever_line_the_view_starts_at()
    {
        var text = new StringBuilder("\x1b[31mred starts here\n");
        for (int i = 1; i < 80_000; i++) text.Append(i == 100 ? "\x1b[0mstops here\n" : $"line number {i} goes on\n");
        var log = Open("carry.log", text.ToString(), paged: true);
        var shot = Capture();
        Assert.True(Matching(shot, PagedRow(log, 0), AnsiRed) > 10, "the line with the code");
        Assert.True(Matching(shot, PagedRow(log, 1), AnsiRed) > 10, "the line after it");

        // the first line on screen is far below the code: its style comes from the lines above it
        ((IMinimapHost)log.Paged!).ScrollToLine(50);
        shot = Capture();
        Assert.Equal(50, log.Paged.TopLine);
        Assert.True(Matching(shot, PagedRow(log, 0), AnsiRed) > 10);

        ((IMinimapHost)log.Paged).ScrollToLine(99);
        shot = Capture();
        Assert.True(Matching(shot, PagedRow(log, 0), AnsiRed) > 10, "the line before the reset");
        Assert.Equal(0, Matching(shot, PagedRow(log, 1), AnsiRed));
        Assert.Equal(0, Matching(shot, PagedRow(log, 2), AnsiRed));
    }

    private void Step(bool forward)
    {
        Assert.Equal(CommandOutcome.Handled, _w.LogStep(forward));
        ToolTestKit.WaitFor(() => !_w.LogSearchRunning, "the search did not end");
    }

    [AvaloniaFact]
    public void Next_and_previous_error_walk_a_paged_log_without_loading_it()
    {
        var log = Open("big.log", BigLog(out var errors), paged: true);
        log.Paged!.GoToOffset(0);
        Assert.Equal(0, log.Paged.CaretOffset);

        Step(true);
        Assert.Equal(errors[1], log.Paged.CaretOffset);
        Step(true);
        Assert.Equal(errors[2], log.Paged.CaretOffset);
        Step(true);
        Assert.Equal(errors[2], log.Paged.CaretOffset);
        Assert.Equal(Loc.Get(K.LogNoNext), _w.StatusMessageText);
        Step(false);
        Assert.Equal(errors[1], log.Paged.CaretOffset);
        Step(false);
        Assert.Equal(errors[0], log.Paged.CaretOffset);
        Step(false);
        Assert.Equal(errors[0], log.Paged.CaretOffset);
        Assert.Equal(Loc.Get(K.LogNoPrevious), _w.StatusMessageText);
        Assert.False(_w.LogSearchRunning);
    }

    [AvaloniaFact]
    public void Next_and_previous_error_in_the_editor_move_the_caret_to_the_line()
    {
        string text = "INFO a\nERROR one\nINFO b\nWARN c\nERROR two\nINFO d\n";
        var tab = Open("e.log", text);
        tab.Editor!.CaretOffset = 0;
        Step(true);
        Assert.Equal((2L, 1L), tab.CaretPosition());
        Step(true);
        Assert.Equal((5L, 1L), tab.CaretPosition());
        Step(true);
        Assert.Equal(Loc.Get(K.LogNoNext), _w.StatusMessageText);
        tab.Editor.CaretOffset = text.IndexOf("WARN", StringComparison.Ordinal) + 2;
        Step(false);
        Assert.Equal((2L, 1L), tab.CaretPosition());
    }

    [AvaloniaFact]
    public void The_commands_say_so_in_a_document_that_is_not_in_log_mode()
    {
        var tab = Open("n.txt", Sample);
        Assert.Equal(CommandOutcome.Handled, _w.LogStep(true));
        Assert.Equal(Loc.Get(K.LogNotLogMode), _w.StatusMessageText);
        Assert.False(_w.LogSearchRunning);
        Assert.False(tab.Doc.IsLog);
    }

    [AvaloniaFact]
    public void Levels_from_a_pattern_of_the_catalog_replace_the_default_words_and_turn_log_mode_on()
    {
        File.WriteAllText(_app.Tools.FilePath, new JsonObject
        {
            ["logPatterns"] = new JsonArray(new JsonObject { ["id"] = "mine", ["title"] = "My log", ["levels"] = new JsonObject { ["error"] = @"\|E\|", ["warning"] = @"\|W\|" } }),
        }.ToJsonString());
        string text = "12:00 |E| bad thing\n12:00 ERROR in the old style\n12:01 |W| careful\n";
        var tab = Open("m.txt", text);
        Assert.False(tab.Doc.IsLog);

        _w.ChooseLogLevels();
        var rows = _w.QuickResults;
        Assert.Equal([Loc.Get(K.LogLevelsDefault), "My log"], rows.Select(r => r.Title));
        rows[1].Run();
        _w.ClosePalette();

        Assert.True(tab.Doc.IsLog);
        Assert.Equal("My log", tab.Doc.LogLevelsName);
        Assert.Equal(LogLevel.Error, tab.Doc.LogLevels.Classify("12:00 |E| bad thing"));
        Assert.Null(tab.Doc.LogLevels.Classify("12:00 ERROR in the old style"));
        var shot = Capture();
        Assert.True(Matching(shot, EditorRow(tab, 0), Level(LogLevel.Error)) > 10);
        Assert.Equal(0, Matching(shot, EditorRow(tab, 1), Level(LogLevel.Error)));
        Assert.True(Matching(shot, EditorRow(tab, 2), Level(LogLevel.Warning)) > 10);
        Assert.Equal(Loc.Get(K.LogLevelsApplied, ("name", "My log")), _w.StatusMessageText);

        tab.Editor!.CaretOffset = 0;
        Step(true);
        Assert.Equal((1L, 1L), tab.CaretPosition()); // no further |E| line below the first one
        Assert.Equal(Loc.Get(K.LogNoNext), _w.StatusMessageText);
    }

    private static Color Highlight => ((ISolidColorBrush)SyntaxPalette.Light.GetBrush(LogHighlightColorizer.Kind)!).Color;

    private void SaveViewWithHighlight(string highlight)
    {
        var dialogs = (ScriptedDialogs)_app.Dialogs;
        foreach (string answer in new[] { "marked", "", highlight, "" }) dialogs.Prompts.Enqueue(answer);
        Assert.Equal(CommandOutcome.Handled, _w.SaveLogView());
        ToolTestKit.WaitFor(() => _w.StatusMessageText == Loc.Get(K.LogViewSaved, ("name", "marked")), "the view was not saved");
    }

    [AvaloniaFact]
    public void The_highlight_spans_are_the_matches_of_the_expression_and_a_slow_search_ends_quietly()
    {
        var spans = LogHighlightColorizer.Spans(new System.Text.RegularExpressions.Regex("an+|x*"), "banana xx an");
        Assert.Equal([(1, 2), (3, 2), (7, 2), (10, 2)], spans);
        var slow = new System.Text.RegularExpressions.Regex("(a+)+$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(5));
        Assert.Empty(LogHighlightColorizer.Spans(slow, new string('a', 40) + "b"));
    }

    [AvaloniaFact]
    public void The_highlight_of_a_saved_view_is_drawn_in_the_editor_and_kept_in_the_file()
    {
        var tab = Open("h.log", Sample);
        Assert.Equal(0, Matching(Capture(), EditorRow(tab, 0), Highlight));
        SaveViewWithHighlight("disk|memory");
        Assert.Equal("disk|memory", _app.LogViews.Load(out _).Single().Highlight);
        var shot = Capture();
        Assert.True(Matching(shot, EditorRow(tab, 0), Highlight) > 10, "line with disk");
        Assert.True(Matching(shot, EditorRow(tab, 2), Highlight) > 10, "line with memory");
        Assert.Equal(0, Matching(shot, EditorRow(tab, 1), Highlight));
        Assert.Equal(0, Matching(shot, EditorRow(tab, 4), Highlight));
        Assert.True(Matching(shot, EditorRow(tab, 0), Level(LogLevel.Error)) > 10, "the level color stays");
    }

    [AvaloniaFact]
    public void The_highlight_of_a_saved_view_is_drawn_on_the_paged_surface()
    {
        var log = Open("hbig.log", BigLog(out _), paged: true);
        Assert.Equal(0, Matching(Capture(), PagedRow(log, 1), Highlight));
        SaveViewWithHighlight("number 1 ");
        var shot = Capture();
        Assert.True(Matching(shot, PagedRow(log, 1), Highlight) > 10, "line 1");
        Assert.Equal(0, Matching(shot, PagedRow(log, 0), Highlight));
        Assert.Equal(0, Matching(shot, PagedRow(log, 2), Highlight));
    }

    private void SaveViewWithFilter(string filter)
    {
        var dialogs = (ScriptedDialogs)_app.Dialogs;
        foreach (string answer in new[] { "kept", filter, "", "" }) dialogs.Prompts.Enqueue(answer);
        Assert.Equal(CommandOutcome.Handled, _w.SaveLogView());
    }

    [AvaloniaFact]
    public void The_lines_a_live_view_keeps_are_marked_in_the_minimap_strip_by_their_level()
    {
        var tab = Open("marks.log", "INFO start\nERROR disk full\nINFO fine\nWARN disk slow\nDEBUG disk x\n");
        SaveViewWithFilter("disk");
        ToolTestKit.WaitFor(() => tab.Minimap!.FilterMarks.Count == 3, "the kept lines were not marked");
        Assert.Equal([new MinimapMark(1, MinimapMarkKind.Error), new MinimapMark(3, MinimapMarkKind.Warning), new MinimapMark(4, MinimapMarkKind.Match)], tab.Minimap!.FilterMarks);
    }

    [AvaloniaFact]
    public void The_marks_of_a_live_view_on_a_paged_log_are_on_the_lines_of_its_matches()
    {
        var log = Open("mbig.log", BigLog(out _), paged: true);
        SaveViewWithFilter("ERROR");
        ToolTestKit.WaitFor(() => log.Minimap!.FilterMarks.Count == 3, "the error lines were not marked");
        Assert.Equal([0L, 20_000L, 45_000L], log.Minimap!.FilterMarks.Select(m => m.Line));
        Assert.All(log.Minimap.FilterMarks, m => Assert.Equal(MinimapMarkKind.Error, m.Kind));
        Assert.True(log.Minimap.IsSampled);
    }

    [AvaloniaFact]
    [Trait("Category", "Large")]
    public void A_10_GiB_log_shows_the_marks_of_its_view_while_the_rest_of_it_is_still_being_read()
    {
        string path = Path.Combine(_temp.Path, "huge.log");
        const long length = 10L << 30;
        File.WriteAllBytes(path, []);
        if (OperatingSystem.IsWindows())
        {
            var command = new System.Diagnostics.ProcessStartInfo("fsutil") { CreateNoWindow = true, UseShellExecute = false };
            foreach (string arg in new[] { "sparse", "setflag", path }) command.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(command)!;
            process.WaitForExit();
        }
        using (var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write))
        {
            stream.SetLength(length);
            stream.Write("INFO start\nERROR first failure\nWARN slow failure\n"u8);
            stream.Position = length - 20;
            stream.Write("\nERROR last failure\n"u8);
        }
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        var tab = _w.OpenFile(path, null);
        Assert.True(tab.Doc.IsLarge);
        Settle();

        SaveViewWithFilter("failure");
        ToolTestKit.WaitFor(() => tab.Minimap!.FilterMarks.Count > 0, "no mark before the end of the reading", rounds: 6000);
        // the failure at the very end is 10 GiB away: what was marked is what the reading had found by then
        Assert.Equal([new MinimapMark(1, MinimapMarkKind.Error), new MinimapMark(2, MinimapMarkKind.Warning)], tab.Minimap!.FilterMarks);

        ToolTestKit.WaitFor(() => tab.Minimap.FilterMarks.Count == 3, "the last failure was not marked", rounds: 6000);
        Assert.Equal(MinimapMarkKind.Error, tab.Minimap.FilterMarks[2].Kind);
        Assert.Equal(4, tab.Minimap.FilterMarks[2].Line);
    }

    // ---- P9.4.1 as a user lives it: save a view, close the app, open the log again, pick the view, walk its matches ----

    private void RestartApp()
    {
        _app = TestApp.Restart(_temp, _app, (ScriptedDialogs)_app.Dialogs);
        _w = _app.CreateWindow(null);
        _w.Width = 1200;
        _w.Height = 800;
    }

    private void Command(string id)
    {
        var run = _app.Workbench.Commands.ExecuteAsync(id, new ShellContext(_w, forMenu: false)).AsTask();
        ToolTestKit.WaitFor(() => run.IsCompleted, id);
        Assert.Equal(CommandOutcome.Handled, run.Result);
    }

    /// <summary>The user answers the four questions of "Save View": name, filter, highlight and levels.</summary>
    private void SaveView(string name, string filter, string highlight, string levels)
    {
        var dialogs = (ScriptedDialogs)_app.Dialogs;
        foreach (string answer in new[] { name, filter, highlight, levels }) dialogs.Prompts.Enqueue(answer);
        Command("nks.log.saveView");
        ToolTestKit.WaitFor(() => _w.StatusMessageText == Loc.Get(K.LogViewSaved, ("name", name)), "the view was not saved");
    }

    [AvaloniaFact]
    public void A_saved_view_comes_back_after_a_restart_with_its_filter_highlight_and_place_and_the_matches_lead_to_their_lines_even_in_a_log_that_grew()
    {
        var lines = Enumerable.Range(0, 30).Select(i => i is 2 or 11 or 24 ? $"ERROR disk is full on node {i}" : i == 7 ? "WARN disk slow on node 7" : $"INFO all fine on node {i}").ToList();
        string text = string.Concat(lines.Select(l => l + "\n"));
        long LineStart(int index) => lines.Take(index).Sum(l => l.Length + 1L);
        var tab = Open("scenario.log", text);
        string path = tab.Path!;
        tab.Editor!.CaretOffset = (int)LineStart(11);

        SaveView("errors", "disk", "slow|full", "error");

        // what the profile keeps: the same filter, levels, highlight and the byte the caret stood on
        var stored = _app.LogViews.Load(out var problem).Single();
        Assert.Null(problem);
        Assert.Equal(("errors", "disk", true, "slow|full", path, LineStart(11)), (stored.Name, stored.Filter.Text, stored.Filter.TextIsRegex, stored.Highlight, stored.FilePath, stored.Position));
        Assert.Equal([LogLevel.Error], stored.Filter.Levels);

        RestartApp();
        tab = _w.OpenFile(path, null);
        _w.UpdateLayout();
        Settle();
        Assert.Equal((1L, 1L), tab.CaretPosition());
        var plain = Capture();
        Assert.True(Matching(plain, EditorRow(tab, 2), Level(LogLevel.Error)) > 10, "the level colour of the reopened log");
        Assert.Equal(0, Matching(plain, EditorRow(tab, 2), Highlight));

        Command("nks.log.views");
        var row = Assert.Single(_w.QuickResults);
        Assert.Equal("errors", row.Title);
        Assert.Equal("error · disk · scenario.log", row.Detail);
        row.Run();
        _w.ClosePalette(); // the list stays open over the editor after a row ran, and a dimmed editor has no colours to find
        ToolTestKit.WaitFor(() => tab.Minimap!.FilterMarks.Count == 3, "the kept lines were not marked");
        Assert.Equal([new MinimapMark(2, MinimapMarkKind.Error), new MinimapMark(11, MinimapMarkKind.Error), new MinimapMark(24, MinimapMarkKind.Error)], tab.Minimap!.FilterMarks);
        ToolTestKit.WaitFor(() => _w.StatusMessageText == Loc.Get(K.LogViewApplied, ("name", "errors"), ("matches", 3)), "the view was not applied");

        // the place the view was saved at, and the highlight drawn on the lines that hold it, whatever the filter keeps
        Assert.Equal((12L, 1L), tab.CaretPosition());
        var shot = Capture();
        Assert.True(Matching(shot, EditorRow(tab, 2), Highlight) > 10, "line with full");
        Assert.True(Matching(shot, EditorRow(tab, 7), Highlight) > 10, "line with slow");
        Assert.Equal(0, Matching(shot, EditorRow(tab, 3), Highlight));

        // the matches lead to their lines: forward from the saved place, then back to the first one
        Command("nks.log.nextMatch");
        Assert.Equal((25L, 1L), tab.CaretPosition());
        Command("nks.log.nextMatch");
        Assert.Equal(Loc.Get(K.LogViewNoNext), _w.StatusMessageText);
        Command("nks.log.previousMatch");
        Assert.Equal((12L, 1L), tab.CaretPosition());
        Command("nks.log.previousMatch");
        Assert.Equal((3L, 1L), tab.CaretPosition());
        Command("nks.log.previousMatch");
        Assert.Equal(Loc.Get(K.LogViewNoPrevious), _w.StatusMessageText);

        // another program appends an error line: the view finds it in the added bytes and the step reads the file again to reach it
        File.AppendAllText(path, "ERROR disk is full on node 30\n");
        ToolTestKit.WaitFor(() => { _w.LogFollowTick(); return tab.Minimap!.FilterMarks.Count == 4; }, "the new line was not marked");
        Command("nks.log.nextMatch");
        Assert.Equal((12L, 1L), tab.CaretPosition());
        Command("nks.log.nextMatch");
        Command("nks.log.nextMatch");
        Assert.Equal((31L, 1L), tab.CaretPosition());
        Assert.Equal("ERROR disk is full on node 30", tab.Editor.Document.Text.Split('\n')[30]);
    }

    [AvaloniaFact]
    public void A_saved_view_of_a_paged_log_comes_back_after_a_restart_at_its_byte_and_draws_its_highlight_there()
    {
        var log = Open("scenario-big.log", BigLog(out var errors), paged: true);
        string path = log.Path!;
        log.Paged!.GoToOffset(errors[1]);

        SaveView("errors", "", "ERROR", "error");
        Assert.Equal(errors[1], _app.LogViews.Load(out _).Single().Position);

        RestartApp();
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        log = _w.OpenFile(path, null);
        Assert.True(log.Doc.IsLarge);
        ToolTestKit.WaitFor(() => log.Paged!.Index.Complete, "line index");
        _w.UpdateLayout();
        Settle();
        Assert.Equal(0, log.Paged!.CaretOffset);
        Assert.Equal(0, Enumerable.Range(0, 40).Count(r => Matching(Capture(), PagedRow(log, r), Highlight) > 10));

        Command("nks.log.views");
        _w.QuickResults.Single().Run();
        _w.ClosePalette();
        ToolTestKit.WaitFor(() => log.Minimap!.FilterMarks.Count == 3, "the error lines were not marked");
        Assert.Equal([0L, 20_000L, 45_000L], log.Minimap!.FilterMarks.Select(m => m.Line));
        Assert.Equal(errors[1], log.Paged.CaretOffset);

        // the one error line on the screen is the line of the saved place, and it is the one that is drawn in the colour of the highlight
        var shot = Capture();
        Assert.Equal(1, Enumerable.Range(0, 40).Count(r => Matching(shot, PagedRow(log, r), Highlight) > 10));

        Command("nks.log.nextMatch");
        Assert.Equal(errors[2], log.Paged.CaretOffset);
        Command("nks.log.nextMatch");
        Assert.Equal(Loc.Get(K.LogViewNoNext), _w.StatusMessageText);
        Command("nks.log.previousMatch");
        Assert.Equal(errors[1], log.Paged.CaretOffset);
        Command("nks.log.previousMatch");
        Assert.Equal(errors[0], log.Paged.CaretOffset);
        Command("nks.log.previousMatch");
        Assert.Equal(Loc.Get(K.LogViewNoPrevious), _w.StatusMessageText);
        Assert.Equal(path, log.Path);
    }

    [AvaloniaFact]
    public void Applying_a_view_without_a_highlight_removes_the_marks_and_a_broken_expression_is_refused()
    {
        var tab = Open("g.log", Sample);
        SaveViewWithHighlight("disk");
        Assert.True(Matching(Capture(), EditorRow(tab, 0), Highlight) > 10);
        tab.Doc.SetLogHighlight(null, TimeSpan.FromMilliseconds(50));
        Assert.Equal(0, Matching(Capture(), EditorRow(tab, 0), Highlight));

        var dialogs = (ScriptedDialogs)_app.Dialogs;
        foreach (string answer in new[] { "broken", "", "(", "" }) dialogs.Prompts.Enqueue(answer);
        _w.SaveLogView();
        ToolTestKit.WaitFor(() => _w.StatusMessageText.Length > 0 && _w.StatusMessageText != Loc.Get(K.LogViewSaved, ("name", "marked")), "no message");
        Assert.DoesNotContain(_app.LogViews.Load(out _), v => v.Name == "broken");
    }
}
