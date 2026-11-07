using System.Text;
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
