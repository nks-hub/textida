using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

    private DocTab Open(string text, bool paged = false)
    {
        var tab = paged
            ? _w.AddRecovered(OpenDocument.Open(_temp.Bytes("big.json", Encoding.UTF8.GetBytes(text)), 0), _w.ActiveGroup)
            : _w.OpenFile(_temp.File("data.json", text), null);
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
}
