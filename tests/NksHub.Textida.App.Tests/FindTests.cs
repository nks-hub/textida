using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Search;

namespace NksHub.Textida.App.Tests;

/// <summary>P2.4: the find bar, stepping, replacing, go to and the matching bracket, through the same keys and fields a user touches.</summary>
[Collection("LocState")]
public class FindTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public FindTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static async Task Settle(Func<bool> done, int timeoutMs = 10_000)
    {
        for (var sw = Stopwatch.StartNew(); !done() && sw.ElapsedMilliseconds < timeoutMs;)
        {
            await Task.Delay(5);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(done(), "timed out");
    }

    private void Query(string text)
    {
        _w.FindBar.FindBox.Text = text;
        Dispatcher.UIThread.RunJobs();
    }

    private static string Text(DocTab t) => t.Editor!.Document.Text;

    private static string Selected(DocTab t)
    {
        var s = t.Editor!.GetSelections()[0];
        return t.Editor.Document.GetText(s.Start, s.Length);
    }

    private async Task<DocTab> OpenAndFind(string content, string query, string name = "f.txt")
    {
        var tab = _w.OpenFile(_temp.File(name, content), null);
        TestApp.Press(_w, Key.F, TestApp.Mod);
        Assert.True(_w.FindBar.IsVisible);
        Query(query);
        await Settle(() => !_w.FindIsRunning);
        return tab;
    }

    [AvaloniaFact]
    public async Task Ctrl_F_opens_the_bar_and_a_query_selects_the_first_match_and_counts()
    {
        var tab = await OpenAndFind("foo bar\nfoo\nBar foo\n", "foo");
        Assert.Equal("1 z 3", _w.FindBar.CountText.Text);
        Assert.Equal("foo", Selected(tab));
        Assert.Equal(0, tab.Editor!.GetSelections()[0].Start);
    }

    [AvaloniaFact]
    public async Task Ctrl_H_with_the_bar_open_keeps_the_query_and_moves_to_the_replacement_field()
    {
        await OpenAndFind("produkt a produkt\n", "produkt");
        Assert.Equal("produkt", Selected(_w.Active!));
        _w.FindBar.CaseToggle.IsChecked = false;
        TestApp.Press(_w, Key.H, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("produkt", _w.FindBar.FindBox.Text);
        Assert.True(_w.FindBar.ReplaceShown);
        Assert.True(_w.FindBar.ReplaceBox.IsFocused);
    }

    [AvaloniaFact]
    public async Task F3_and_Shift_F3_walk_the_matches_forward_and_backward_and_wrap()
    {
        var tab = await OpenAndFind("foo bar\nfoo\nBar foo\n", "foo");
        var starts = new List<int>();
        for (int i = 0; i < 4; i++)
        {
            TestApp.Press(_w, Key.F3);
            await Settle(() => !_w.FindIsRunning);
            starts.Add(tab.Editor!.GetSelections()[0].Start);
        }
        Assert.Equal([8, 16, 0, 8], starts);
        Assert.Equal("2 z 3", _w.FindBar.CountText.Text);

        var back = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            TestApp.Press(_w, Key.F3, RawInputModifiers.Shift);
            await Settle(() => !_w.FindIsRunning);
            back.Add(tab.Editor!.GetSelections()[0].Start);
        }
        Assert.Equal([0, 16, 8], back);
    }

    [AvaloniaFact]
    public async Task Case_whole_word_and_regex_switches_change_what_is_found_and_a_bad_pattern_says_why()
    {
        await OpenAndFind("Foo foo foot\n", "foo");
        Assert.Equal("1 z 3", _w.FindBar.CountText.Text);
        _w.FindBar.CaseToggle.IsChecked = true;
        await Settle(() => !_w.FindIsRunning);
        Assert.Equal("1 z 2", _w.FindBar.CountText.Text);
        _w.FindBar.WordToggle.IsChecked = true;
        await Settle(() => !_w.FindIsRunning);
        Assert.Equal("1 z 1", _w.FindBar.CountText.Text);
        _w.FindBar.WordToggle.IsChecked = false;
        _w.FindBar.RegexToggle.IsChecked = true;
        Query("fo+t?");
        await Settle(() => !_w.FindIsRunning);
        Assert.Equal("1 z 2", _w.FindBar.CountText.Text);
        Query("(foo");
        Assert.StartsWith("Neplatný výraz", _w.FindBar.CountText.Text);
        Assert.True(_w.FindBar.CountText.Classes.Contains("error"));
    }

    [AvaloniaFact]
    public async Task Typing_into_the_field_replaces_the_running_search_and_only_the_last_one_counts()
    {
        var tab = _w.OpenFile(_temp.File("big.txt", string.Concat(Enumerable.Repeat("alpha beta gamma\n", 40_000))), null);
        TestApp.Press(_w, Key.F, TestApp.Mod);
        _w.FindBar.FindBox.Focus();
        var sw = Stopwatch.StartNew();
        TestApp.Type(_w, "gam");
        Assert.True(sw.ElapsedMilliseconds < 1000, "typing must not wait for the search");
        TestApp.Type(_w, "ma");
        await Settle(() => !_w.FindIsRunning);
        Assert.Equal("gamma", _w.FindBar.FindBox.Text);
        Assert.EndsWith("z 40000", _w.FindBar.CountText.Text);
        Assert.Equal("gamma", Selected(tab));
    }

    [AvaloniaFact]
    public async Task Replace_one_replaces_the_selected_match_and_moves_on_and_replace_all_is_one_undo_step()
    {
        var tab = await OpenAndFind("foo bar\nfoo\nBar foo\n", "foo");
        _w.FindBar.Open(files: false, replace: true);
        _w.FindBar.ReplaceBox.Text = "x";
        _w.FindBar.ReplaceOneButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        await Settle(() => Text(tab) == "x bar\nfoo\nBar foo\n");
        await Settle(() => !_w.FindIsRunning && Selected(tab) == "foo");
        Assert.Equal(6, tab.Editor!.GetSelections()[0].Start);

        _w.FindBar.ReplaceAllButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        await Settle(() => Text(tab) == "x bar\nx\nBar x\n");
        _w.Undo();
        Assert.Equal("x bar\nfoo\nBar foo\n", Text(tab));
        _w.Undo();
        Assert.Equal("foo bar\nfoo\nBar foo\n", Text(tab));
    }

    [AvaloniaFact]
    public async Task Replace_in_the_selection_leaves_the_rest_alone()
    {
        var tab = _w.OpenFile(_temp.File("s.txt", "foo\nfoo\nfoo\nfoo\n"), null);
        tab.Editor!.SetSelections([new NksHub.Textida.AvaloniaEditAdapter.Sel(4, 12)]);
        TestApp.Press(_w, Key.H, RawInputModifiers.Control);
        Assert.True(_w.FindBar.ReplaceShown);
        _w.FindBar.SelectionToggle.IsChecked = true;
        Query("foo");
        _w.FindBar.ReplaceBox.Text = "bar";
        await Settle(() => !_w.FindIsRunning);
        Assert.Equal("1 z 2", _w.FindBar.CountText.Text);
        _w.FindBar.ReplaceAllButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        await Settle(() => Text(tab) == "foo\nbar\nbar\nfoo\n");
    }

    [AvaloniaFact]
    public async Task Replace_while_the_document_is_edited_gives_the_right_result_in_one_step()
    {
        var tab = await OpenAndFind(string.Concat(Enumerable.Repeat("foo ", 5000)), "foo");
        _w.FindBar.Open(files: false, replace: true);
        _w.FindBar.ReplaceBox.Text = "b";
        _w.FindBar.ReplaceAllButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        tab.Editor!.Document.Insert(0, "!");
        await Settle(() => !Text(tab).Contains("foo"));
        Assert.Equal("!" + string.Concat(Enumerable.Repeat("b ", 5000)), Text(tab));
        _w.Undo();
        Assert.Equal("!" + string.Concat(Enumerable.Repeat("foo ", 5000)), Text(tab));
    }

    [AvaloniaFact]
    public async Task Regex_replacement_changes_case_of_a_group()
    {
        var tab = await OpenAndFind("name: ab\n", @"(\w+): (\w+)");
        _w.FindBar.RegexToggle.IsChecked = true;
        Query(@"(\w+): (\w+)");
        await Settle(() => !_w.FindIsRunning);
        _w.FindBar.Open(files: false, replace: true);
        _w.FindBar.ReplaceBox.Text = @"\U$2\E=$1";
        _w.FindBar.ReplaceAllButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        await Settle(() => Text(tab) == "AB=name\n");
    }

    [AvaloniaFact]
    public async Task Ctrl_F3_finds_the_word_under_the_caret_and_Escape_closes_the_bar()
    {
        var tab = _w.OpenFile(_temp.File("w.txt", "alpha beta alpha gamma alpha"), null);
        tab.Editor!.CaretOffset = 2;
        TestApp.Press(_w, Key.F3, RawInputModifiers.Control);
        await Settle(() => !_w.FindIsRunning && Selected(tab) == "alpha" && tab.Editor.GetSelections()[0].Start == 11);
        Assert.Equal("alpha", _w.FindBar.FindBox.Text);
        TestApp.Press(_w, Key.Escape);
        Assert.False(_w.FindBar.IsVisible);
    }

    [AvaloniaFact]
    [Trait("Category", "Perf")]
    public async Task Typing_in_the_field_cancels_the_stale_search_of_a_100_MiB_file_the_ui_answers_within_100_ms_and_the_worker_ends_within_a_second()
    {
        string path = Path.Combine(_temp.Path, "big.log");
        byte[] line = "2026-10-26 12:00:00 INFO request handled in 12 ms path=/api/items/42\n"u8.ToArray();
        using (var fs = File.Create(path))
        {
            var block = new byte[line.Length * 4096];
            for (int i = 0; i < 4096; i++) line.CopyTo(block, i * line.Length);
            for (long written = 0; written < 100L << 20; written += block.Length) fs.Write(block);
        }
        var tab = _w.OpenFile(path, null);
        Assert.True(tab.Doc.IsLarge);
        TestApp.Press(_w, Key.F, TestApp.Mod);
        _w.FindBar.FindBox.Focus();

        var times = new List<double>();
        SearchRun? previous = null;
        double worker = 0;
        foreach (var query in new[] { "r", "re", "req", "requ", "reque" })
        {
            long t0 = Stopwatch.GetTimestamp();
            Query(query);
            times.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
            if (previous is not null)
            {
                long t1 = Stopwatch.GetTimestamp();
                await previous.Completion;
                worker = Math.Max(worker, Stopwatch.GetElapsedTime(t1).TotalMilliseconds + times[^1]);
            }
            previous = _w.FindRun;
            await Task.Delay(20);
        }
        await Settle(() => !_w.FindIsRunning, 60_000);
        Assert.True(times.Max() <= 100, "the field took " + string.Join(", ", times.Select(t => t.ToString("0"))) + " ms to answer a keystroke");
        Assert.True(worker <= 1000, $"the stale worker took {worker:0} ms to end");
        long lines = (long)Math.Ceiling((100L << 20) / (double)(line.Length * 4096)) * 4096;
        Assert.Contains(lines.ToString(), _w.FindBar.CountText.Text.Replace(" ", "").Replace(" ", ""));
    }

    [AvaloniaFact]
    public async Task Go_to_takes_a_line_with_a_column_a_percentage_and_a_byte_offset()
    {
        var tab = _w.OpenFile(_temp.File("g.txt", "l1\nl2 long\nl3\nl4\n"), null);
        _w.GoTo("2:4");
        Assert.Equal((2, 4), tab.Editor!.CaretLineColumn());
        _w.GoTo("@11");
        Assert.Equal((3, 1), tab.Editor.CaretLineColumn());
        _w.GoTo("50%");
        Assert.Equal((2, 6), tab.Editor.CaretLineColumn());
        _w.GoTo("99");
        Assert.Equal(5, tab.Editor.CaretLineColumn().Line);
        _w.GoTo("nope");
        Assert.Contains("nope", _w.StatusLeftText);
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public void Go_to_bracket_pairs_code_and_skips_brackets_in_strings_and_comments()
    {
        var tab = _w.OpenFile(_temp.File("b.js", "f(\"a)\", /* ) */ b);\n"), null);
        tab.Editor!.CaretOffset = 1;
        Assert.Equal(NksHub.Textida.Workbench.Commands.CommandOutcome.Handled, _w.GoToBracket());
        Assert.Equal(17, tab.Editor.CaretOffset);
        tab.Editor.CaretOffset = 18;
        Assert.Equal(NksHub.Textida.Workbench.Commands.CommandOutcome.Handled, _w.GoToBracket());
        Assert.Equal(1, tab.Editor.CaretOffset);
    }

    [AvaloniaFact]
    public void The_bracket_at_the_caret_and_its_partner_are_highlighted_outside_strings_and_comments()
    {
        var tab = _w.OpenFile(_temp.File("h.js", "f(\"a)\", /* ) */ b);\nx;\n"), null);
        var editor = tab.Editor!;
        editor.CaretOffset = 1;
        Assert.Equal((1, 17), editor.MatchingBrackets());
        editor.CaretOffset = 18; // behind the closing bracket
        Assert.Equal((17, 1), editor.MatchingBrackets());
        editor.CaretOffset = 5; // behind the bracket in the string
        Assert.Null(editor.MatchingBrackets());
        editor.CaretOffset = 21;
        Assert.Null(editor.MatchingBrackets());

        editor.CaretOffset = 18;
        editor.Document.Insert(0, "(");
        Assert.Equal(19, editor.CaretOffset);
        Assert.Equal((18, 2), editor.MatchingBrackets()); // the text changed: the pair is looked for again

        editor.SetSelections([new Sel(1, 3)]);
        Assert.Null(editor.MatchingBrackets());
        _app.Workbench.Set("editor.matchBrackets", System.Text.Json.Nodes.JsonValue.Create(false));
        Dispatcher.UIThread.RunJobs();
        editor.SetSelections([Sel.At(2)]);
        Assert.Null(editor.MatchingBrackets());
    }

    [AvaloniaFact]
    public void Ctrl_slash_comments_two_selections_with_the_markers_of_html_and_its_script_as_one_undo_step()
    {
        string original = "<p>a</p>\n<script>\n  var x;\n</script>\n";
        var tab = _w.OpenFile(_temp.File("p.html", original), null);
        var d = tab.Editor!.Document;
        int script = d.GetLineByNumber(3).Offset + 4;
        tab.Editor.SetCarets(Carets.Create([Sel.At(2), Sel.At(script)], 1));
        TestApp.Press(_w, Key.OemQuestion, TestApp.Mod);
        Assert.Equal("<!-- <p>a</p> -->\n<script>\n  // var x;\n</script>\n", Text(tab));
        Assert.Equal(d.GetLineByNumber(3).Offset + 7, tab.Editor.GetSelections()[1].Start);
        _w.Undo();
        Assert.Equal(original, Text(tab));
    }

    [AvaloniaFact]
    public void Shift_alt_right_grows_the_selection_from_the_word_to_the_bracket_content()
    {
        var tab = _w.OpenFile(_temp.File("g.js", "f(a, [b + c]);\n"), null);
        tab.Editor!.CaretOffset = 6;
        TestApp.Press(_w, Key.Right, RawInputModifiers.Shift | RawInputModifiers.Alt);
        Assert.Equal("b", Selected(tab));
        TestApp.Press(_w, Key.Right, RawInputModifiers.Shift | RawInputModifiers.Alt);
        Assert.Equal("b + c", Selected(tab));
        TestApp.Press(_w, Key.Right, RawInputModifiers.Shift | RawInputModifiers.Alt);
        Assert.Equal("[b + c]", Selected(tab));
    }

    [AvaloniaFact]
    public void Search_commands_have_keys_and_menu_entries()
    {
        var keys = _app.Workbench.Keymap.Bindings.GroupBy(b => b.CommandId).ToDictionary(g => g.Key, g => g.First().Sequence.Display(_app.Workbench.Platform), StringComparer.Ordinal);
        Assert.Equal("Ctrl+F", keys["nks.search.find"]);
        Assert.Equal("Ctrl+H", keys["nks.search.replace"]);
        Assert.Equal("Ctrl+Shift+F", keys["nks.search.findInFiles"]);
        Assert.Equal("F3", keys["nks.search.findNext"]);
        Assert.Contains("nks.search.regexTester", _w.MenuModel.SelectMany(g => g.Items).OfType<MenuCommand>().Select(c => c.CommandId));
    }

    [AvaloniaFact]
    public void The_regex_tester_runs_the_search_engine_over_a_sample_and_explains_the_pattern()
    {
        var window = new RegexTesterWindow(new SearchPattern(@"(\w+) (\w+)", true));
        window.SampleText = "hello world\nsecond line"; Dispatcher.UIThread.RunJobs();
        window.ReplacementText = @"\U$1\E $2"; Dispatcher.UIThread.RunJobs();
        Assert.Equal(["1. „hello world“ od znaku 0", "    skupina 1: „hello“", "    skupina 2: „world“", "2. „second line“ od znaku 12", "    skupina 1: „second“", "    skupina 2: „line“"], window.MatchLines);
        Assert.Equal("HELLO world\nSECOND line", window.ReplacedText);
        Assert.Contains(window.ExplainLines, l => l.Contains("Začátek zachycující skupiny 1"));
        window.PatternText = @"a++\K("; Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("Neplatný výraz", window.NoteText);
        Assert.Contains(window.ExplainLines, l => l.Contains("Nepodporováno"));
        window.Close();
    }
}
