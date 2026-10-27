using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Macros: recording and playback live in the command registry (<see cref="CommandRegistry.StartRecording"/>, <see cref="CommandRegistry.PlayAsync"/>);
/// this part gives them the window: messages, the one undo step of a playback, saving under a name and the list of saved macros.
/// </summary>
internal sealed partial class MainWindow
{
    private CommandRegistry Commands => _app.Workbench.Commands;

    internal CommandOutcome RecordMacro()
    {
        SetStatusMessage(Loc.Get(Commands.StartRecording() ? K.MacroRecording : K.MacroBusy));
        return CommandOutcome.Handled;
    }

    internal CommandOutcome StopMacro()
    {
        if (!Commands.IsRecording)
        {
            SetStatusMessage(Loc.Get(K.MacroNotRecording));
            return CommandOutcome.Handled;
        }
        var steps = Commands.StopRecording();
        SetStatusMessage(steps.Count == 0 ? Loc.Get(K.MacroStoppedEmpty) : Loc.Plural(K.MacroStopped, steps.Count));
        return CommandOutcome.Handled;
    }

    internal CommandOutcome PlayLastMacro()
    {
        if (ActiveEditor is not { IsReadOnly: false }) return CommandOutcome.NotHandled;
        if (Commands.LastMacro.Count == 0)
        {
            SetStatusMessage(Loc.Get(K.MacroNone));
            return CommandOutcome.Handled;
        }
        _ = PlayMacroAsync(Commands.LastMacro);
        return CommandOutcome.Handled;
    }

    /// <summary>
    /// Plays the steps over the selections of the active editor as one undo step. A command that is not meant for macros (a saved file may name any) stops the macro before the first change;
    /// a step that does not run stops it there, and what it changed so far stays one undo step.
    /// </summary>
    internal async Task PlayMacroAsync(IReadOnlyList<MacroStep> steps)
    {
        if (ActiveEditor is not { IsReadOnly: false } surface) return;
        if (Commands.IsRecording)
        {
            SetStatusMessage(Loc.Get(K.MacroBusy));
            return;
        }
        foreach (var id in steps.Select(s => s.Command).OfType<string>())
        {
            if (Commands.TryGet(id, out var command) && command.Macro) continue;
            SetStatusMessage(Loc.Get(K.MacroRefused, ("title", command is null ? id : Loc.Get(command.TitleKey))));
            return;
        }

        var before = surface.GetSelections();
        MacroResult result;
        using (surface.Doc.Doc.BeginUndoGroup(ToBytes(surface, before), () => ToBytes(surface, surface.GetSelections())))
            result = await Commands.PlayAsync(steps, new ShellContext(this, forMenu: true), surface.TypeText);

        if (result.Outcome == CommandOutcome.Handled)
        {
            SetStatusMessage(Loc.Plural(K.MacroPlayed, steps.Count));
            return;
        }
        var step = steps[result.Done];
        var title = step.Command is { } cmd && Commands.TryGet(cmd, out var failed) ? Loc.Get(failed.TitleKey) : step.Text ?? "";
        SetStatusMessage(Loc.Get(K.MacroFailed, ("step", result.Done + 1), ("title", title)));
    }

    /// <summary>Tools, Save Last Macro As: asks for a name and writes the last recorded macro to the macro folder.</summary>
    internal CommandOutcome SaveMacro()
    {
        if (Commands.LastMacro.Count == 0)
        {
            SetStatusMessage(Loc.Get(K.MacroNone));
            return CommandOutcome.Handled;
        }
        _ = SaveMacroAsync(Commands.LastMacro);
        return CommandOutcome.Handled;
    }

    private async Task SaveMacroAsync(IReadOnlyList<MacroStep> steps)
    {
        var name = (await _app.Dialogs.PromptAsync(this, Loc.Get(K.MacroSaveTitle), Loc.Get(K.MacroSaveLabel), ""))?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        SetStatusMessage(_app.Macros.Save(name, steps, out var why)
            ? Loc.Get(K.MacroSaved, ("name", name))
            : Loc.Get(K.MacroSaveFailed, ("reason", why)));
    }

    /// <summary>Tools, Run Saved Macro: the list of saved macros; the chosen one plays over the selections.</summary>
    internal CommandOutcome ShowMacros()
    {
        if (ActiveEditor is not { IsReadOnly: false }) return CommandOutcome.NotHandled;
        var problems = new List<(string File, string Reason)>();
        var rows = _app.Macros.Load(problems).Select(m => new QuickRow(m.Name, null, Loc.Plural(K.MacroDetail, m.Steps.Count), null, true, null, m.Name,
            () => _ = PlayMacroAsync(m.Steps))).ToList();
        if (problems.Count > 0) SetStatusMessage(Loc.Get(K.MacroDamaged, ("file", problems[0].File), ("reason", problems[0].Reason)));
        ShowChoice(Loc.Get(K.MacroWatermark), Loc.Get(K.A11yMacros), rows, emptyText: Loc.Get(K.MacroEmpty));
        return CommandOutcome.Handled;
    }
}
