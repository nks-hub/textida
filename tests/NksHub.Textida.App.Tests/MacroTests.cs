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
    public void A_saved_macro_that_names_a_command_outside_macros_changes_nothing()
    {
        var tab = OpenWithFirstWordSelected();
        _w.PlayMacroAsync([new MacroStep("nks.edit.upperCase", null), new MacroStep("nks.edit.convert", null)]).GetAwaiter().GetResult();
        Assert.Equal(Original, tab.Editor!.Document.Text);
    }
}
