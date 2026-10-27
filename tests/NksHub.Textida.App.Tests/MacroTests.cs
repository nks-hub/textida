using Avalonia.Headless.XUnit;
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

    [AvaloniaFact]
    public void A_saved_macro_that_names_a_command_outside_macros_changes_nothing()
    {
        var tab = OpenWithFirstWordSelected();
        _w.PlayMacroAsync([new MacroStep("nks.edit.upperCase", null), new MacroStep("nks.edit.convert", null)]).GetAwaiter().GetResult();
        Assert.Equal(Original, tab.Editor!.Document.Text);
    }
}
