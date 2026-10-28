using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>P8.1.1 through the command registry of the running window: recording, refusal, playback over several selections as one undo step, and saved macros.</summary>
[Collection("LocState")]
public class MacroTests : IDisposable
{
    private const string Original = "foo 1\nfoo 2\nfoo 3\n";

    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public MacroTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private CommandRegistry Registry => _app.Workbench.Commands;

    private DocTab OpenWithFirstWordSelected()
    {
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = Original;
        Dispatcher.UIThread.RunJobs();
        tab.Doc.FlushEol();
        tab.Editor.TextArea.Focus();
        tab.Editor.SetSelections([new Sel(0, 3)]);
        return tab;
    }

    private CommandOutcome Run(string id) => Registry.ExecuteAsync(id, new ShellContext(_w, forMenu: true)).AsTask().GetAwaiter().GetResult();

    private static int Steps(DocTab t) => t.Doc.Doc.UndoDepth - 1;

    private DocTab OpenDocument(string text, Sel selection)
    {
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = text;
        Dispatcher.UIThread.RunJobs();
        tab.Doc.FlushEol();
        Reselect(tab, selection);
        return tab;
    }

    private static void Reselect(DocTab tab, Sel selection)
    {
        tab.Editor!.TextArea.Focus();
        tab.Editor.SetSelections([selection]);
    }

    /// <summary>Puts the search and the replacement into the find bar, as the help says to do before recording.</summary>
    private async Task SetBarAsync(string find, string replacement, bool inSelection = false)
    {
        _w.ShowFind(replace: true);
        _w.FindBar.FindBox.Text = find;
        _w.FindBar.ReplaceBox.Text = replacement;
        _w.FindBar.SelectionToggle.IsChecked = inSelection;
        await Settle(() => !_w.FindIsRunning);
    }

    /// <summary>The steps of the manual run: the same commands, typed text through the keyboard path.</summary>
    private void DoByHand()
    {
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.addNextOccurrence"));
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.addNextOccurrence"));
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.upperCase"));
        TestApp.Type(_w, "x");
    }

    [AvaloniaFact]
    public void Playback_over_several_selections_equals_the_manual_steps_and_is_one_undo_step()
    {
        var manual = OpenWithFirstWordSelected();
        Assert.Equal(CommandOutcome.Handled, Run("nks.macro.record"));
        DoByHand();
        Assert.Equal(CommandOutcome.Handled, Run("nks.macro.stop"));
        string byHand = manual.Editor!.Document.Text;
        Assert.Equal("x 1\nx 2\nx 3\n", byHand);
        Assert.Equal(
            ["nks.edit.addNextOccurrence", "nks.edit.addNextOccurrence", "nks.edit.upperCase", null],
            Registry.LastMacro.Select(s => s.Command));
        Assert.Equal("x", Registry.LastMacro[^1].Text);

        var played = OpenWithFirstWordSelected();
        int before = Steps(played);
        Assert.Equal(CommandOutcome.Handled, Run("nks.macro.playLast"));
        Assert.Equal(byHand, played.Editor!.Document.Text);
        Assert.Equal(before + 1, Steps(played));

        Run("nks.edit.undo");
        Assert.Equal(Original, played.Editor.Document.Text);
    }

    [AvaloniaFact]
    public void A_command_a_macro_cannot_replay_is_refused_while_recording_and_not_recorded()
    {
        OpenWithFirstWordSelected();
        var refused = new List<string>();
        Registry.RecordingRefused += refused.Add;
        Run("nks.macro.record");
        Assert.Equal(CommandOutcome.NotRecordable, Run("nks.edit.convert"));
        Assert.Equal(["nks.edit.convert"], refused);
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.selectLine"));
        Run("nks.macro.stop");
        Assert.Equal(["nks.edit.selectLine"], Registry.LastMacro.Select(s => s.Command));
        Assert.Empty(_dialogs.Asked);
    }

    [AvaloniaFact]
    public void A_saved_macro_is_written_to_the_profile_and_runs_again()
    {
        OpenWithFirstWordSelected();
        Run("nks.macro.record");
        DoByHand();
        Run("nks.macro.stop");

        _dialogs.Prompts.Enqueue("Foo to x");
        Assert.Equal(CommandOutcome.Handled, Run("nks.macro.save"));
        Dispatcher.UIThread.RunJobs();
        Assert.True(File.Exists(Path.Combine(_app.ProfileDirectory, "macros", "foo-to-x.json")));

        var problems = new List<(string, string)>();
        var saved = Assert.Single(_app.Macros.Load(problems));
        Assert.Empty(problems);
        Assert.Equal("Foo to x", saved.Name);
        Assert.Equal(Registry.LastMacro, saved.Steps);

        var tab = OpenWithFirstWordSelected();
        _w.PlayMacroAsync(saved.Steps).GetAwaiter().GetResult();
        Assert.Equal("x 1\nx 2\nx 3\n", tab.Editor!.Document.Text);
    }

    /// <summary>End, Backspace, Shift+Left and a typed character at three carets.</summary>
    private void KeysByHand()
    {
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.addNextOccurrence"));
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.addNextOccurrence"));
        TestApp.Press(_w, Key.End);
        TestApp.Press(_w, Key.Back);
        TestApp.Press(_w, Key.Left, RawInputModifiers.Shift);
        TestApp.Type(_w, "-");
    }

    [AvaloniaFact]
    public void Caret_and_delete_keys_are_recorded_as_commands_and_play_back_like_the_keys_without_a_macro()
    {
        var plain = OpenWithFirstWordSelected();
        KeysByHand();
        Assert.Equal("foo-\nfoo-\nfoo-\n", plain.Editor!.Document.Text);

        var recorded = OpenWithFirstWordSelected();
        Run("nks.macro.record");
        KeysByHand();
        Run("nks.macro.stop");
        Assert.Equal(plain.Editor.Document.Text, recorded.Editor!.Document.Text);
        Assert.Equal(
            ["nks.edit.addNextOccurrence", "nks.edit.addNextOccurrence", "nks.edit.cursorLineEnd", "nks.edit.deleteLeft", "nks.edit.cursorLeftSelect", null],
            Registry.LastMacro.Select(s => s.Command));

        var played = OpenWithFirstWordSelected();
        int before = Steps(played);
        Run("nks.macro.playLast");
        Assert.Equal(plain.Editor.Document.Text, played.Editor!.Document.Text);
        Assert.Equal(before + 1, Steps(played));
        Run("nks.edit.undo");
        Assert.Equal(Original, played.Editor.Document.Text);
    }

    [AvaloniaFact]
    public void A_caret_key_with_one_caret_plays_back_where_the_key_took_it()
    {
        var tab = OpenWithFirstWordSelected();
        tab.Editor!.SetSelections([Sel.At(2)]);
        Run("nks.macro.record");
        TestApp.Press(_w, Key.Right, OperatingSystem.IsMacOS() ? RawInputModifiers.Alt : RawInputModifiers.Control);
        TestApp.Press(_w, Key.Delete);
        Run("nks.macro.stop");
        Assert.Equal(["nks.edit.cursorWordRight", "nks.edit.deleteRight"], Registry.LastMacro.Select(s => s.Command));
        string byHand = tab.Editor.Document.Text;

        var played = OpenWithFirstWordSelected();
        played.Editor!.SetSelections([Sel.At(2)]);
        Run("nks.macro.playLast");
        Assert.Equal(byHand, played.Editor.Document.Text);
        Assert.Equal(tab.Editor.CaretOffset, played.Editor.CaretOffset);
    }

    [AvaloniaFact]
    public async Task Copy_and_paste_in_a_macro_wait_for_the_clipboard_and_give_the_manual_result()
    {
        var ctx = new ShellContext(_w, forMenu: true);
        var manual = OpenWithFirstWordSelected();
        Run("nks.macro.record");
        Run("nks.edit.addNextOccurrence");
        Run("nks.edit.addNextOccurrence");
        Assert.Equal(CommandOutcome.Handled, await Registry.ExecuteAsync("nks.edit.copy", ctx));
        TestApp.Press(_w, Key.End);
        Assert.Equal(CommandOutcome.Handled, await Registry.ExecuteAsync("nks.edit.paste", ctx));
        Run("nks.macro.stop");
        Assert.Equal("foo 1foo\nfoo 2foo\nfoo 3foo\n", manual.Editor!.Document.Text);
        Assert.Equal(["nks.edit.addNextOccurrence", "nks.edit.addNextOccurrence", "nks.edit.copy", "nks.edit.cursorLineEnd", "nks.edit.paste"],
            Registry.LastMacro.Select(s => s.Command));

        await _w.Clipboard!.SetTextAsync("other");
        var played = OpenWithFirstWordSelected();
        int before = Steps(played);
        await _w.PlayMacroAsync(Registry.LastMacro);
        Assert.Equal(manual.Editor.Document.Text, played.Editor!.Document.Text);
        Assert.Equal(before + 1, Steps(played));
    }

    private static async Task Settle(Func<bool> done)
    {
        for (var sw = System.Diagnostics.Stopwatch.StartNew(); !done() && sw.ElapsedMilliseconds < 10_000;)
        {
            await Task.Delay(5);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(done(), "timed out");
    }

    [AvaloniaFact]
    public async Task Find_next_keeps_its_search_with_the_step_so_a_saved_macro_finds_it_with_the_bar_closed()
    {
        var ctx = new ShellContext(_w, forMenu: true);
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = "x foo\ny foo\n";
        Dispatcher.UIThread.RunJobs();
        _w.ShowFind(replace: false);
        _w.FindBar.FindBox.Text = "foo";
        await Settle(() => !_w.FindIsRunning);
        tab.Editor.TextArea.Focus();
        tab.Editor.SetSelections([Sel.At(0)]);

        Run("nks.macro.record");
        Assert.Equal(CommandOutcome.Handled, await Registry.ExecuteAsync("nks.search.findNext", ctx));
        TestApp.Type(_w, "bar");
        Run("nks.macro.stop");
        Assert.Equal("x bar\ny foo\n", tab.Editor.Document.Text);
        var find = Registry.LastMacro[0];
        Assert.Equal("nks.search.findNext", find.Command);
        Assert.Contains("\"foo\"", find.Argument);

        Assert.True(_app.Macros.Save("Find foo", Registry.LastMacro, out _));
        var saved = Assert.Single(_app.Macros.Load([]));
        Assert.Equal(Registry.LastMacro, saved.Steps);

        _w.FindBar.FindBox.Text = "";
        _w.HideFind();
        var other = _w.NewDocument();
        other.Editor!.Document.Text = "foo foo\n";
        Dispatcher.UIThread.RunJobs();
        other.Editor.TextArea.Focus();
        other.Editor.SetSelections([Sel.At(0)]);
        await _w.PlayMacroAsync(saved.Steps);
        Assert.Equal("bar foo\n", other.Editor.Document.Text);
    }

    [AvaloniaFact]
    public void Find_next_without_a_search_in_the_bar_is_not_recorded()
    {
        OpenWithFirstWordSelected();
        Run("nks.macro.record");
        Assert.Equal(CommandOutcome.NotRecordable, Run("nks.search.findNext"));
        Assert.False(_w.FindBar.IsVisible);
        Run("nks.edit.selectLine");
        Run("nks.macro.stop");
        Assert.Equal(["nks.edit.selectLine"], Registry.LastMacro.Select(s => s.Command));
    }

    [AvaloniaFact]
    public async Task Arrows_a_selection_the_clipboard_and_replace_replay_on_another_document_to_the_manual_bytes()
    {
        var ctx = new ShellContext(_w, forMenu: true);
        var manual = OpenDocument(Original, Sel.At(0));
        await SetBarAsync("foo", "bar");
        Reselect(manual, Sel.At(0));

        Run("nks.macro.record");
        for (int i = 0; i < 3; i++) TestApp.Press(_w, Key.Right);
        for (int i = 0; i < 2; i++) TestApp.Press(_w, Key.Right, RawInputModifiers.Shift);
        Assert.Equal(CommandOutcome.Handled, await Registry.ExecuteAsync("nks.edit.copy", ctx));
        TestApp.Press(_w, Key.End);
        Assert.Equal(CommandOutcome.Handled, await Registry.ExecuteAsync("nks.edit.paste", ctx));
        TestApp.Press(_w, Key.Down);
        Assert.Equal(CommandOutcome.Handled, await Registry.ExecuteAsync("nks.search.replaceNext", ctx));
        Assert.Equal(CommandOutcome.Handled, await Registry.ExecuteAsync("nks.search.replaceAll", ctx));
        Run("nks.macro.stop");
        string byHand = manual.Editor!.Document.Text;
        Assert.Equal("bar 1 1\nbar 2\nbar 3\n", byHand);
        Assert.Equal(
            ["nks.edit.cursorRight", "nks.edit.cursorRight", "nks.edit.cursorRight", "nks.edit.cursorRightSelect", "nks.edit.cursorRightSelect", "nks.edit.copy",
             "nks.edit.cursorLineEnd", "nks.edit.paste", "nks.edit.cursorDown", "nks.search.replaceNext", "nks.search.replaceAll"],
            Registry.LastMacro.Select(s => s.Command));
        Assert.Contains("\"replace\":\"bar\"", Registry.LastMacro[^1].Argument);

        await _w.Clipboard!.SetTextAsync("other");
        var played = OpenDocument(Original, Sel.At(0));
        await SetBarAsync("zzz", "q");
        Reselect(played, Sel.At(0));
        int before = Steps(played);
        await _w.PlayMacroAsync(Registry.LastMacro);
        Assert.Equal(byHand, played.Editor!.Document.Text);
        Assert.Equal(before + 1, Steps(played));
        Assert.Equal("foo", _w.FindBar.FindBox.Text);
        Run("nks.edit.undo");
        Assert.Equal(Original, played.Editor.Document.Text);
    }

    [AvaloniaFact]
    public async Task Replace_next_steps_over_the_matches_and_a_replay_does_the_same()
    {
        var ctx = new ShellContext(_w, forMenu: true);
        var manual = OpenDocument("a a a a\n", Sel.At(0));
        await SetBarAsync("a", "b");
        Reselect(manual, Sel.At(0));
        Run("nks.macro.record");
        Assert.Equal(CommandOutcome.Handled, await Registry.ExecuteAsync("nks.search.replaceNext", ctx));
        Assert.Equal(CommandOutcome.Handled, await Registry.ExecuteAsync("nks.search.replaceNext", ctx));
        Run("nks.macro.stop");
        Assert.Equal("b b a a\n", manual.Editor!.Document.Text);

        var played = OpenDocument("a a a a\n", Sel.At(0));
        await SetBarAsync("zzz", "q");
        Reselect(played, Sel.At(0));
        await _w.PlayMacroAsync(Registry.LastMacro);
        Assert.Equal("b b a a\n", played.Editor!.Document.Text);
    }

    [AvaloniaFact]
    public async Task Replace_all_in_the_selection_works_on_the_selection_the_macro_has_when_it_plays()
    {
        var ctx = new ShellContext(_w, forMenu: true);
        const string text = "foo\nfoo\nfoo\n";
        var manual = OpenDocument(text, new Sel(4, 7));
        await SetBarAsync("foo", "bar", inSelection: true);
        Reselect(manual, new Sel(4, 7));
        Run("nks.macro.record");
        Assert.Equal(CommandOutcome.Handled, await Registry.ExecuteAsync("nks.search.replaceAll", ctx));
        Run("nks.macro.stop");
        Assert.Equal("foo\nbar\nfoo\n", manual.Editor!.Document.Text);
        Assert.Contains("\"selection\":true", Registry.LastMacro[0].Argument);

        var played = OpenDocument(text, new Sel(8, 11));
        await SetBarAsync("zzz", "q");
        Reselect(played, new Sel(8, 11));
        await _w.PlayMacroAsync(Registry.LastMacro);
        Assert.Equal("foo\nfoo\nbar\n", played.Editor!.Document.Text);
    }

    [AvaloniaFact]
    public async Task A_saved_replace_step_runs_with_the_find_bar_closed()
    {
        var ctx = new ShellContext(_w, forMenu: true);
        var manual = OpenDocument("foo foo\n", Sel.At(0));
        await SetBarAsync("foo", "bar");
        Reselect(manual, Sel.At(0));
        Run("nks.macro.record");
        Assert.Equal(CommandOutcome.Handled, await Registry.ExecuteAsync("nks.search.replaceAll", ctx));
        Run("nks.macro.stop");
        Assert.True(_app.Macros.Save("Foo to bar", Registry.LastMacro, out _));
        var saved = Assert.Single(_app.Macros.Load([]));
        Assert.Equal(Registry.LastMacro, saved.Steps);

        _w.FindBar.FindBox.Text = "";
        _w.FindBar.ReplaceBox.Text = "";
        _w.HideFind();
        var other = OpenDocument("foo x foo\n", Sel.At(0));
        await _w.PlayMacroAsync(saved.Steps);
        Assert.Equal("bar x bar\n", other.Editor!.Document.Text);
    }

    [AvaloniaFact]
    public async Task Replace_without_a_search_in_the_bar_is_not_recorded()
    {
        var ctx = new ShellContext(_w, forMenu: true);
        OpenWithFirstWordSelected();
        Run("nks.macro.record");
        Assert.Equal(CommandOutcome.NotRecordable, await Registry.ExecuteAsync("nks.search.replaceAll", ctx));
        Assert.Equal(CommandOutcome.NotRecordable, await Registry.ExecuteAsync("nks.search.replaceNext", ctx));
        Assert.False(_w.FindBar.IsVisible);
        Run("nks.edit.selectLine");
        Run("nks.macro.stop");
        Assert.Equal(["nks.edit.selectLine"], Registry.LastMacro.Select(s => s.Command));
    }

    private void SaveTwoMacros()
    {
        Assert.True(_app.Macros.Save("First", [new MacroStep("nks.edit.upperCase", null)], out _));
        Assert.True(_app.Macros.Save("Second", [new MacroStep("nks.edit.lowerCase", null)], out _));
    }

    [AvaloniaFact]
    public async Task A_saved_macro_is_renamed_from_the_list_and_its_old_file_goes()
    {
        SaveTwoMacros();
        _dialogs.Prompts.Enqueue("Renamed one");
        Assert.Equal(CommandOutcome.Handled, Run("nks.macro.rename"));
        Assert.Equal(PaletteMode.Choice, _w.PaletteModeNow);
        Assert.Equal(["First", "Second"], _w.QuickResults.Select(r => r.Title));
        _w.QuickResults[0].Run();
        string dir = _app.Macros.Directory;
        await Settle(() => File.Exists(Path.Combine(dir, "renamed-one.json")));
        Assert.False(File.Exists(Path.Combine(dir, "first.json")));
        var left = _app.Macros.Load([]);
        Assert.Equal(["Renamed one", "Second"], left.Select(m => m.Name));
        Assert.Equal([new MacroStep("nks.edit.upperCase", null)], left[0].Steps);
    }

    [AvaloniaFact]
    public void Renaming_to_the_name_of_another_macro_changes_nothing()
    {
        SaveTwoMacros();
        _dialogs.Prompts.Enqueue("second");
        Run("nks.macro.rename");
        _w.QuickResults[0].Run();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["First", "Second"], _app.Macros.Load([]).Select(m => m.Name));
        Assert.Equal(Loc.Get(K.MacroNameTaken, ("name", "second")), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public async Task A_saved_macro_is_deleted_only_after_the_question_is_answered_yes()
    {
        SaveTwoMacros();
        _dialogs.Answers.Enqueue(1);
        Run("nks.macro.delete");
        _w.QuickResults[0].Run();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("First", _dialogs.Asked[^1].Message);
        Assert.Equal(2, _app.Macros.Load([]).Count);

        _dialogs.Answers.Enqueue(0);
        Run("nks.macro.delete");
        _w.QuickResults[1].Run();
        await Settle(() => _app.Macros.Load([]).Count == 1);
        Assert.Equal("First", Assert.Single(_app.Macros.Load([])).Name);
    }

    [AvaloniaFact]
    public void A_macro_file_over_the_limit_is_listed_as_damaged_and_not_read()
    {
        Directory.CreateDirectory(_app.Macros.Directory);
        File.WriteAllText(Path.Combine(_app.Macros.Directory, "big.json"), "{\"name\":\"Big\",\"steps\":[]}" + new string(' ', PluginLimitsPlaceholders.MacroMaxFileBytes));
        var problems = new List<(string File, string Reason)>();
        Assert.Empty(_app.Macros.Load(problems));
        Assert.Equal("big.json", Assert.Single(problems).File);
    }

    [AvaloniaFact]
    public void A_saved_macro_that_names_a_command_outside_macros_changes_nothing()
    {
        var tab = OpenWithFirstWordSelected();
        _w.PlayMacroAsync([new MacroStep("nks.edit.upperCase", null), new MacroStep("nks.edit.convert", null)]).GetAwaiter().GetResult();
        Assert.Equal(Original, tab.Editor!.Document.Text);
    }
}
