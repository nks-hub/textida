using System.Text;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>The formatted JSON view (P3.1.14) through the window: switching with the place kept, folding, typing into a value, jumping to a key, saving and a broken file.</summary>
[Collection("LocState")]
public class JsonPrettyViewTests : IDisposable
{
    private const string Sample = "{\"name\":\"Ann\",\"tags\":[\"a\",\"b\"],\"address\":{\"city\":\"Brno\",\"zip\":60200},\"active\":true}";

    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public JsonPrettyViewTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        Loc.SetLanguage("en");
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private CommandOutcome Run(string id) => _app.Workbench.Commands.ExecuteAsync(id, new ShellContext(_w, forMenu: true)).AsTask().GetAwaiter().GetResult();

    private DocTab Open(string text, bool paged = false, string name = "data.json")
    {
        var tab = paged
            ? _w.AddRecovered(OpenDocument.Open(_temp.Bytes("big.json", Encoding.UTF8.GetBytes(text)), 0), _w.ActiveGroup)
            : _w.OpenFile(_temp.File(name, text), null);
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return tab;
    }

    private JsonPrettyView Show()
    {
        Assert.Equal(CommandOutcome.Handled, Run("nks.view.jsonPretty"));
        var view = Assert.IsType<JsonPrettyView>(_w.ActiveJson);
        _w.UpdateLayout();
        view.Focus();
        return view;
    }

    private static void Scan(JsonPrettyView view)
    {
        for (var sw = System.Diagnostics.Stopwatch.StartNew(); !view.Outline!.State.Complete && sw.ElapsedMilliseconds < 30_000;) Thread.Sleep(5);
        Assert.True(view.Outline!.State.Complete);
        Dispatcher.UIThread.RunJobs();
        view.Refresh();
    }

    private static string Text(DocTab tab) => Encoding.UTF8.GetString(tab.Doc.Doc.ReadBytes(new ByteRange(0, tab.Doc.Doc.ByteLength)));
    private void Press(Key key, RawInputModifiers mods = RawInputModifiers.None) => TestApp.Press(_w, key, mods);

    [AvaloniaFact]
    public void Switching_shows_one_member_per_line_and_keeps_the_place_both_ways()
    {
        var tab = Open(Sample);
        var surface = tab.Surface!;
        surface.SetSelections([Sel.At(Sample.IndexOf("\"city\"", StringComparison.Ordinal))]);
        var view = Show();
        Scan(view);
        Assert.False(tab.View.IsVisible);
        Assert.Equal("{", view.RowText(0));
        Assert.Equal("  \"name\": \"Ann\",", view.RowText(1));
        Assert.Equal("    \"a\",", view.RowText(3));
        Assert.Equal("    \"zip\": 60200", view.RowText(8));
        Assert.Equal("}", view.RowText(11));
        Assert.Equal(7, view.CaretLine); // the line of "city"
        Assert.Contains("$.address.city", _w.StatusLeftText);

        view.MoveCaret(10, 2); // "active"
        Assert.Equal(CommandOutcome.Handled, Run("nks.view.jsonPretty"));
        Assert.Null(_w.ActiveJson);
        Assert.True(tab.View.IsVisible);
        Assert.Equal(Sample.IndexOf("\"active\"", StringComparison.Ordinal), tab.Doc.TextBackend!.ToByteOffset(surface.CaretOffset));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hidden_text_stops_coloring_and_resumes_with_the_edited_json_when_shown_again(bool paged)
    {
        var tab = Open(Sample, paged);
        var syntax = tab.Syntax!;
        Assert.True(syntax.Active);
        var view = Show();
        Assert.False(syntax.Active);
        using (var frame = _w.CaptureRenderedFrame()) Assert.NotNull(frame);
        long renderedAt = tab.Paged?.RenderedAt ?? 0;
        int runs = syntax.ViewportRuns;
        view.MoveCaret(1, "  \"name\": \"An".Length);
        TestApp.Type(_w, "x");
        Dispatcher.UIThread.RunJobs();
        using (var frame = _w.CaptureRenderedFrame()) Assert.NotNull(frame);
        Assert.False(tab.View.IsVisible);
        Assert.Equal(renderedAt, tab.Paged?.RenderedAt ?? 0);
        Assert.True(view.RenderedAt > 0);
        Assert.Equal(runs, syntax.ViewportRuns);
        Assert.Contains("Anxn", Text(tab));

        Run("nks.view.jsonPretty");
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.True(syntax.Active);
        Assert.True(syntax.ViewportRuns > runs);
        if (paged) Assert.NotEmpty(tab.Paged!.TokensOf(0)!);
        else
        {
            tab.Editor!.TextArea.TextView.EnsureVisualLines();
            var expected = (Avalonia.Media.ISolidColorBrush)NksHub.Textida.App.Services.EditorLook.PaletteOf(tab.Editor).GetBrush("string")!;
            Assert.Contains(tab.Editor.TextArea.TextView.GetVisualLine(1)!.Elements,
                e => e.TextRunProperties.ForegroundBrush is Avalonia.Media.ISolidColorBrush b && b.Color == expected.Color);
        }
    }

    [AvaloniaFact]
    public void Typing_updates_the_horizontal_scroll_range_before_the_first_paint()
    {
        Open("{\"k\":\"" + new string('x', 400) + "\"}");
        var view = Show();
        Scan(view);
        view.MoveCaret(1, "  \"k\": \"".Length + 400);
        using (var frame = _w.CaptureRenderedFrame()) Assert.NotNull(frame);
        Dispatcher.UIThread.RunJobs();
        var bar = view.Children.OfType<Avalonia.Controls.Primitives.ScrollBar>().Single(b => b.Orientation == Avalonia.Layout.Orientation.Horizontal);
        double before = bar.Maximum;
        Assert.True(before > 0);

        view.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Source = view, Text = "x" });

        Assert.True(bar.Maximum > before, $"Horizontal maximum stayed at {bar.Maximum} after {before}");
        Assert.Contains(new string('x', 401), view.RowText(1));
    }

    [AvaloniaFact]
    public void A_scrollbar_repaint_keeps_the_first_frame_of_the_new_text()
    {
        Open(Sample);
        var view = Show();
        Scan(view);
        view.MoveCaret(1, "  \"name\": \"An".Length);
        TestApp.Type(_w, "x");
        using (var frame = _w.CaptureRenderedFrame()) Assert.NotNull(frame);
        long first = view.RenderedAt, last = view.LastRenderedAt;
        Assert.True(first > 0);
        Assert.Contains("Anxn", view.RowText(1));
        view.Refresh();
        using (var frame = _w.CaptureRenderedFrame()) Assert.NotNull(frame);
        Assert.True(view.LastRenderedAt > last);
        Assert.Equal(first, view.RenderedAt);
        TestApp.Type(_w, "y");
        using (var frame = _w.CaptureRenderedFrame()) Assert.NotNull(frame);
        Assert.True(view.RenderedAt > first);
        Assert.Contains("Anxyn", view.RowText(1));
    }

    [AvaloniaFact]
    public void The_measurement_marker_waits_for_the_input_revision_and_does_not_replace_the_text_timestamp()
    {
        var tab = Open(Sample);
        var view = Show();
        Scan(view);
        view.MoveCaret(1, "  \"name\": \"An".Length);

        var measured = new List<(uint Marker, long Draw)>();
        view.MeasuredFrameDrawn += (marker, draw) => measured.Add((marker, draw));

        (uint First, uint Second) ReadMarker()
        {
            using var frame = _w.CaptureRenderedFrame()!;
            using var buffer = frame.Lock();
            uint Read(PixelPoint point)
            {
                var local = _w.PointToClient(point);
                int x = (int)(local.X * frame.Dpi.X / 96), y = (int)(local.Y * frame.Dpi.Y / 96);
                Assert.InRange(x, 0, frame.PixelSize.Width - 1);
                Assert.InRange(y, 0, frame.PixelSize.Height - 1);
                int at = y * buffer.RowBytes + x * 4;
                uint color = (uint)Marshal.ReadInt32(buffer.Address, at) & 0xFFFFFF;
                return frame.Format == PixelFormat.Bgra8888 ? color
                    : (color & 0xFF00) | (color >> 16) | ((color & 0xFF) << 16);
            }
            return (Read(view.MeasureMarkerPoints.First), Read(view.MeasureMarkerPoints.Second));
        }

        Assert.NotEqual((0x1753A9u, 0xE8AC56u), ReadMarker());
        view.SetMeasureMarker(0x1753A9, tab.Doc.Doc.Revision + 1);
        view.Refresh();
        Assert.NotEqual((0x1753A9u, 0xE8AC56u), ReadMarker());
        Assert.Empty(measured);
        view.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Source = view, Text = "x" });
        Assert.Equal((0x1753A9u, 0xE8AC56u), ReadMarker());
        Assert.Equal(0x1753A9u, measured[^1].Marker);
        Assert.True(measured[^1].Draw > 0);
        Assert.Contains("Anxn", view.RowText(1));
        long first = view.RenderedAt;
        view.SetMeasureMarker(0x1753AA, null);
        view.Refresh();
        Assert.Equal((0x1753A9u, 0xE8AC56u), ReadMarker());
        view.ReleaseMeasureMarker();
        Assert.Equal((0x1753AAu, 0xE8AC55u), ReadMarker());
        Assert.Equal(0x1753AAu, measured[^1].Marker);
        Assert.Equal(first, view.RenderedAt);
        int beforeUnmeasuredChange = measured.Count;
        view.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Source = view, Text = "y" });
        Assert.NotEqual((0x1753AAu, 0xE8AC55u), ReadMarker());
        Assert.Equal(beforeUnmeasuredChange, measured.Count);
        Assert.Contains("Anxyn", view.RowText(1));
    }

    [AvaloniaFact]
    public void Collapsing_and_expanding_by_keys_and_by_the_gutter()
    {
        Open(Sample);
        var view = Show();
        Scan(view);
        view.MoveCaret(6, 0); // "address": {
        Press(Key.OemOpenBrackets, TestApp.Mod | RawInputModifiers.Shift);
        Assert.Equal("  \"address\": { … },", view.RowText(6));
        Assert.Equal("  \"active\": true", view.RowText(7));
        Press(Key.OemCloseBrackets, TestApp.Mod | RawInputModifiers.Shift);
        Assert.Equal("    \"city\": \"Brno\",", view.RowText(7));
        Assert.True(view.ToggleFold(2)); // "tags": [
        Assert.Equal("  \"tags\": [ … ],", view.RowText(2));
        view.MoveCaret(4, 0); // a hidden line moves to its fold
        Assert.Equal(2, view.CaretLine);
    }

    [AvaloniaFact]
    public void Typing_into_a_value_changes_only_its_bytes_and_undo_restores_them()
    {
        var tab = Open(Sample);
        var view = Show();
        Scan(view);
        view.MoveCaret(1, "  \"name\": \"An".Length);
        TestApp.Type(_w, "ni");
        Assert.Equal(Sample.Replace("\"Ann\"", "\"Annin\""), Text(tab));
        Assert.Equal("  \"name\": \"Annin\",", view.RowText(1));
        Press(Key.Back);
        Assert.Equal(Sample.Replace("\"Ann\"", "\"Annn\""), Text(tab));
        Press(Key.Z, TestApp.Mod);
        Assert.Equal(Sample, Text(tab));

        view.MoveCaret(1, 1); // the indent
        TestApp.Type(_w, "x");
        Assert.Equal(Sample, Text(tab));
        Assert.Equal(Loc.Get(K.JsonPrettyNotHere), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public void Commands_for_an_editable_text_are_disabled_in_the_formatted_view_while_cut_and_paste_stay()
    {
        Open(Sample);
        var context = new ShellContext(_w, forMenu: true);
        bool Enabled(string id) => _app.Workbench.Commands.TryGet(id, out var command) && command.IsEnabled(context);
        string[] textOnly = ["nks.edit.deleteLine", "nks.edit.duplicateLine", "nks.edit.sortLines", "nks.edit.formatJson", "nks.search.replaceAll", "nks.format.eolLf"];
        Assert.All(textOnly, id => Assert.True(Enabled(id), id));
        Show();
        Assert.Equal(("true", "true"), (context.Get(ContextKeys.ReadOnly), context.Get(ContextKeys.JsonPretty)));
        Assert.All(textOnly, id => Assert.False(Enabled(id), id));
        Assert.All(["nks.edit.cut", "nks.edit.paste", "nks.edit.undo", "nks.edit.copy"], id => Assert.True(Enabled(id), id));
        Run("nks.view.jsonPretty");
        Assert.Equal("false", context.Get(ContextKeys.JsonPretty));
        Assert.All(textOnly, id => Assert.True(Enabled(id), id));
    }

    [AvaloniaFact]
    public async Task A_large_single_line_file_shows_its_first_screen_from_a_few_blocks_and_saves_unchanged()
    {
        var json = new StringBuilder("[");
        for (int i = 0; i < 60_000; i++) json.Append(i > 0 ? "," : "").Append("{\"id\":").Append(i).Append(",\"name\":\"item").Append(i).Append("\",\"flags\":[true,false,null]}");
        json.Append(']');
        string text = json.ToString();
        var tab = Open(text, paged: true);
        Assert.NotNull(tab.Paged);
        var view = Show();
        Assert.Equal("[", view.RowText(0));
        Assert.Equal("    \"id\": 0,", view.RowText(2));
        Assert.True(view.Outline!.BytesRead < 512 * 1024, $"the first screen read {view.Outline.BytesRead} bytes");
        Scan(view);
        Assert.Equal(60_000L * 9 + 2, view.Outline.State.Lines);

        _dialogs.Prompts.Enqueue("name");
        view.MoveCaret(9L * 500, 0);
        Assert.Equal(CommandOutcome.Handled, Run("nks.json.goToKey"));
        for (var sw = System.Diagnostics.Stopwatch.StartNew(); view.CaretLine == 9L * 500 && sw.ElapsedMilliseconds < 10_000;) { Thread.Sleep(5); Dispatcher.UIThread.RunJobs(); }
        Assert.Equal(9L * 500 + 3, view.CaretLine);
        Assert.Contains("$[500].name", _w.StatusLeftText);

        byte[] before = File.ReadAllBytes(tab.Doc.Path!);
        Assert.True(await _w.SaveAsync(tab));
        Assert.Equal(before, File.ReadAllBytes(tab.Doc.Path!));
    }

    [AvaloniaFact]
    public async Task A_search_from_the_find_bar_moves_the_caret_of_the_formatted_view()
    {
        Open(Sample);
        var view = Show();
        Scan(view);
        Press(Key.F, TestApp.Mod);
        Assert.True(_w.FindBar.IsVisible);
        _w.FindBar.FindBox.Text = "60200";
        Dispatcher.UIThread.RunJobs();
        for (var sw = System.Diagnostics.Stopwatch.StartNew(); _w.FindIsRunning && sw.ElapsedMilliseconds < 10_000;) { await Task.Delay(5); Dispatcher.UIThread.RunJobs(); }
        await _w.FindStepAsync(forward: true);
        Assert.Same(view, _w.ActiveJson);
        Assert.Equal(8, view.CaretLine); // "zip": 60200
        Assert.Equal("    \"zip\": ".Length, view.CaretColumn);
    }

    [AvaloniaFact]
    public async Task A_reload_formats_the_new_content_of_the_file()
    {
        var tab = Open(Sample);
        Show();
        File.WriteAllText(tab.Doc.Path!, "{\"fresh\":[1]}");
        Assert.True(await _w.ReloadAsync(tab));
        var view = Assert.IsType<JsonPrettyView>(_w.ActiveJson);
        Scan(view);
        Assert.Equal("  \"fresh\": [", view.RowText(1));
        Assert.Equal("}", view.RowText(4));
    }

    [AvaloniaFact]
    public void Broken_json_shows_the_source_text_from_the_error_and_says_where()
    {
        Open("{\"a\":1,\"b\":tru}");
        var view = Show();
        Scan(view);
        Assert.Equal("  \"a\": 1,", view.RowText(1));
        Assert.Equal("\"b\":tru}", view.RowText(2));
        view.MoveCaret(2, 0);
        Assert.Contains(Loc.Get(K.JsonPrettyInvalid, ("offset", "14")), _w.StatusLeftText);
    }

    private async Task<string> Clip() => (await _w.Clipboard!.TryGetTextAsync()) ?? "";

    private static string Lines(params string[] lines) => string.Join(Environment.NewLine, lines);

    [AvaloniaFact]
    public async Task Shift_selection_copies_formatted_lines_or_the_original_json()
    {
        Open(Sample);
        var view = Show();
        Scan(view);
        view.MoveCaret(1, 0);
        Press(Key.Down, RawInputModifiers.Shift);
        Press(Key.Down, RawInputModifiers.Shift);
        Assert.True(view.HasSelection);
        Run("nks.edit.copy");
        Assert.Equal(Lines("  \"name\": \"Ann\",", "  \"tags\": [", ""), await Clip());
        Assert.Equal(CommandOutcome.Handled, Run("nks.json.copySource"));
        Assert.Equal("\"name\":\"Ann\",\"tags\":[", await Clip());

        Press(Key.A, TestApp.Mod);
        Run("nks.edit.copy");
        Assert.Equal(Lines([.. Enumerable.Range(0, 12).Select(r => view.RowText(r)!)]), await Clip());

        view.CopyLimit = 10;
        view.Select(1, 0, 3, 0);
        await _w.Clipboard!.SetTextAsync("before");
        Run("nks.edit.copy");
        Assert.Equal("before", await Clip());
        Assert.Equal(Loc.Get(K.JsonPrettyTooLarge, ("limit", "10 B")), _w.StatusMessageText);
        Press(Key.A, TestApp.Mod);
        Assert.Equal(Loc.Get(K.JsonPrettySelectAllTooLarge, ("limit", "10 B")), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public async Task Cut_and_paste_work_inside_one_value_and_are_refused_across_the_structure()
    {
        var tab = Open(Sample);
        var view = Show();
        Scan(view);
        int ann = "  \"name\": \"".Length;
        view.Select(1, ann, 1, ann + 2); // "An"
        Run("nks.edit.cut");
        Assert.Equal("An", await Clip());
        Assert.Equal(Sample.Replace("\"Ann\"", "\"n\""), Text(tab));
        Assert.Equal(ann, view.CaretColumn);
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.paste"));
        Assert.Equal(Sample, Text(tab));
        Assert.Equal(ann + 2, view.CaretColumn);

        view.Select(1, ann, 1, ann + 3);
        TestApp.Type(_w, "Eve");
        Assert.Equal(Sample.Replace("\"Ann\"", "\"Eve\""), Text(tab));
        view.Select(1, ann, 1, ann + 3);
        TestApp.Type(_w, "Ann");
        Assert.Equal(Sample, Text(tab));

        view.Select(1, 0, 2, 4);
        await _w.Clipboard!.SetTextAsync("before");
        Run("nks.edit.cut");
        Assert.Equal("before", await Clip());
        Assert.Equal(Sample, Text(tab));
        Assert.Equal(Loc.Get(K.JsonPrettyCutNotHere), _w.StatusMessageText);
        Press(Key.Delete);
        Assert.Equal(Sample, Text(tab));
        Assert.Equal(Loc.Get(K.JsonPrettyNotHere), _w.StatusMessageText);

        view.MoveCaret(1, ann + 1);
        await _w.Clipboard!.SetTextAsync("x\ny");
        Run("nks.edit.paste");
        Assert.Equal(Sample, Text(tab));
        Assert.Equal(Loc.Get(K.JsonPrettyPasteControl), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public async Task A_long_value_wraps_over_rows_and_can_be_edited_to_its_end()
    {
        string value = new('x', 6000);
        string json = "{\"k\":\"" + value + "\",\"n\":1}";
        var tab = Open(json);
        var view = Show();
        Scan(view);
        Assert.EndsWith("…,", view.RowText(1));
        Assert.Equal("  \"n\": 1", view.RowText(2));

        view.MoveCaret(1, 0);
        Press(Key.End); // the end of a cut value wraps it
        Assert.Equal(1, view.ExpandedLine);
        long parts = view.CaretPart + 1;
        Assert.True(parts > 1);
        Assert.StartsWith("  \"k\": \"x", view.RowText(1));
        Assert.StartsWith("    x", view.RowText(2));
        Assert.EndsWith("x\",", view.RowText(parts));
        Assert.Equal("  \"n\": 1", view.RowText(parts + 1));

        Press(Key.Left);
        Press(Key.Left); // before the closing quote
        TestApp.Type(_w, "Z");
        Assert.Equal(json.Replace("x\",", "xZ\","), Text(tab));
        Assert.Contains("xZ", string.Concat(Enumerable.Range(1, (int)parts + 1).Select(r => view.RowText(r))));

        Assert.Equal(CommandOutcome.Handled, Run("nks.json.expandValue"));
        Assert.Equal(-1, view.ExpandedLine);
        Assert.Equal("  \"n\": 1", view.RowText(2));

        view.MoveCaret(1, 0);
        Press(Key.Down, RawInputModifiers.Shift);
        Run("nks.edit.copy");
        Assert.Equal(Lines("  \"k\": \"" + value + "Z\",", ""), await Clip());

        view.MoveCaret(2, 0);
        Run("nks.json.expandValue");
        Assert.Equal(Loc.Get(K.JsonPrettyNotLong), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public void After_a_resize_the_caret_stays_on_its_character_in_a_wrapped_value()
    {
        string json = "{\"k\":\"" + new string('x', 9000) + "\",\"n\":1}";
        Open(json);
        _w.Width = 900;
        var view = Show();
        Scan(view);
        view.MoveCaret(1, 0);
        Press(Key.End);
        Assert.Equal(1, view.ExpandedLine);
        view.MoveCaret(1, 10, part: 20);
        long offset = view.SourceOffset;
        int partBytes = view.PartBytes;
        Assert.Equal(20, view.CaretPart);

        _w.Width = 1800;
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.PartBytes > partBytes, $"{view.PartBytes} bytes per row after {partBytes}");
        Assert.True(view.CaretPart < 20);
        Assert.Equal(offset, view.SourceOffset);
    }

    [AvaloniaFact]
    public async Task A_long_key_wraps_over_rows_and_can_be_edited_and_copied_whole()
    {
        string key = new('k', 6000);
        string json = "{\"" + key + "\":1,\"n\":2}";
        var tab = Open(json);
        var view = Show();
        Scan(view);
        Assert.EndsWith("k…: 1,", view.RowText(1));

        view.MoveCaret(1, 0);
        Press(Key.End);
        Assert.Equal(1, view.ExpandedLine);
        long parts = view.CaretPart + 1;
        Assert.True(parts > 1);
        Assert.StartsWith("    k", view.RowText(2));
        Assert.EndsWith("k\": 1,", view.RowText(parts));
        Assert.Equal("  \"n\": 2", view.RowText(parts + 1));

        for (int i = 0; i < 5; i++) Press(Key.Left); // over ",", "1", " ", ":" and the closing quote
        TestApp.Type(_w, "Z");
        Assert.Equal(json.Replace("k\":", "kZ\":"), Text(tab));

        Assert.Equal(CommandOutcome.Handled, Run("nks.json.expandValue"));
        Assert.Equal(-1, view.ExpandedLine);
        view.MoveCaret(1, 0);
        Press(Key.Down, RawInputModifiers.Shift);
        Run("nks.edit.copy");
        Assert.Equal(Lines("  \"" + key + "Z\": 1,", ""), await Clip());
        Assert.Equal(CommandOutcome.Handled, Run("nks.json.copySource"));
        Assert.Equal("\"" + key + "Z\":1,", await Clip());
    }

    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Arrows_select_and_replace_a_whole_supplementary_character_in_a_wrapped_key_or_value(bool key, bool backward)
    {
        string prefix = new('k', 6000);
        string json = key ? "{\"" + prefix + "😀tail\":1}" : "{\"k\":\"" + prefix + "😀tail\"}";
        var tab = Open(json);
        var view = Show();
        Scan(view);
        long at = Encoding.UTF8.GetByteCount(json.AsSpan(0, json.IndexOf("😀", StringComparison.Ordinal)));
        long start = backward ? at + 4 : at;
        view.RevealOffset(start, start);
        Assert.Equal(1, view.ExpandedLine);
        Assert.Equal(start, view.SourceOffset);

        Press(backward ? Key.Left : Key.Right, RawInputModifiers.Shift);
        Assert.Equal(backward ? at : at + 4, view.SourceOffset);
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.copy"));
        Assert.Equal("😀", await Clip());

        TestApp.Type(_w, "Z");
        string expected = json.Replace("😀", "Z", StringComparison.Ordinal);
        Assert.Equal(expected, Text(tab));
        Assert.Equal(at + 1, view.SourceOffset);
        Assert.True(await _w.SaveAsync(tab));
        Assert.Equal(Encoding.UTF8.GetBytes(expected), File.ReadAllBytes(tab.Doc.Path!));
        Press(Key.Z, TestApp.Mod);
        Assert.Equal(json, Text(tab));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_arrow_crossing_a_wrapped_key_row_selects_a_whole_supplementary_character(bool backward)
    {
        string json = "{\"" + string.Concat(Enumerable.Repeat("😀", 2000)) + "tail\":1}";
        var tab = Open(json);
        var view = Show();
        Scan(view);
        view.MoveCaret(1, 0);
        Assert.True(view.ToggleExpand());
        long at = view.Outline!.PartStart(view.Outline.GetLine(1)!.Value, 1, view.PartBytes);
        if (backward)
        {
            view.MoveCaret(1, 0, part: 1);
            Press(Key.Home);
        }
        else view.MoveCaret(1, int.MaxValue, part: 0);
        Assert.Equal(backward ? 1 : 0, view.CaretPart);
        Assert.Equal(at, view.SourceOffset);

        Press(backward ? Key.Left : Key.Right, RawInputModifiers.Shift);
        Assert.Equal(backward ? 0 : 1, view.CaretPart);
        Assert.Equal(backward ? at - 4 : at + 4, view.SourceOffset);
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.copy"));
        Assert.Equal("😀", await Clip());
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        int from = checked((int)(backward ? at - 4 : at)), to = from + 4;
        TestApp.Type(_w, "Z");
        Assert.Equal(Encoding.UTF8.GetString([.. bytes[..from], (byte)'Z', .. bytes[to..]]), Text(tab));
        Press(Key.Z, TestApp.Mod);
        Assert.Equal(json, Text(tab));
    }

    [AvaloniaFact]
    public void A_jsonc_file_shows_its_comments_on_lines_of_their_own()
    {
        Open("{\n  // compiler options\n  \"compilerOptions\": {\"strict\": true,},\n}\n", name: "tsconfig.json");
        var view = Show();
        Scan(view);
        Assert.Null(view.Outline!.Error);
        Assert.Equal("  // compiler options", view.RowText(1));
        Assert.Equal("    \"strict\": true,", view.RowText(3));
        Assert.Equal("}", view.RowText(5));
    }

    [AvaloniaFact]
    public async Task A_comment_between_a_key_and_its_value_gets_a_line_and_saving_keeps_the_bytes()
    {
        string text = "{\n  \"a\" /* why */ : 1,\n  \"b\": // note\n    \"x\"\n}\n";
        var tab = Open(text, name: "tsconfig.json");
        var view = Show();
        Scan(view);
        Assert.Null(view.Outline!.Error);
        Assert.Equal(["{", "  \"a\":", "  /* why */", "  1,", "  \"b\":", "  // note", "  \"x\"", "}"], Enumerable.Range(0, 8).Select(r => view.RowText(r)));
        view.MoveCaret(6, 4); // after x
        Assert.Contains("$.b", _w.StatusLeftText);
        TestApp.Type(_w, "y");
        view.MoveCaret(4, 4); // after the b of the key
        TestApp.Type(_w, "c");
        string expected = text.Replace("\"x\"", "\"xy\"").Replace("\"b\"", "\"bc\"");
        Assert.Equal(expected, Text(tab));
        Assert.Equal("  \"bc\":", view.RowText(4));
        Assert.True(await _w.SaveAsync(tab));
        Assert.Equal(Encoding.UTF8.GetBytes(expected), File.ReadAllBytes(tab.Doc.Path!));
    }
}
