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
    private readonly App _app;
    private readonly MainWindow _w;

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
