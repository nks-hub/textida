using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>A caret or delete key of the editor: while a macro is recorded it runs as its command, so the macro records it; otherwise false and the key acts as always.</summary>
    private bool RunKeyCommand(string id)
    {
        if (!Commands.IsRecording) return false;
        var pending = Commands.ExecuteAsync(id, new ShellContext(this, forMenu: false));
        return !pending.IsCompletedSuccessfully || pending.Result == CommandOutcome.Handled;
    }

    /// <summary>
    /// Find Next and Find Previous. A macro records the search of the find bar with the step and plays the step with that search, so a saved macro
    /// finds the same text later; without a search in the find bar the step is not recorded. The task ends when the match is selected.
    /// </summary>
    internal async ValueTask<CommandOutcome> FindStepCommandAsync(bool forward)
    {
        if (Commands.IsPlaying)
        {
            if (Commands.StepArgument is not { } argument || !ApplyFindArgument(argument)) return CommandOutcome.NotHandled;
        }
        else if (Commands.IsRecording)
        {
            if (FindArgument() is not { } argument)
            {
                SetStatusMessage(Loc.Get(K.MacroFindNoQuery));
                return CommandOutcome.NotRecordable;
            }
            Commands.RecordArgument(argument);
        }
        await FindStepAsync(forward);
        return CommandOutcome.Handled;
    }

    /// <summary>The search of the find bar as the argument of a macro step; null unless the bar searches the text document.</summary>
    private string? FindArgument()
    {
        if (Active is not { Custom: null, Hex: null, Surface: not null } || !_find.IsVisible || _find.FilesMode || string.IsNullOrEmpty(_find.FindBox.Text)) return null;
        return new JsonObject
        {
            ["text"] = _find.FindBox.Text,
            ["regex"] = _find.RegexToggle.IsChecked == true,
            ["case"] = _find.CaseToggle.IsChecked == true,
            ["word"] = _find.WordToggle.IsChecked == true,
            ["selection"] = _find.SelectionToggle.IsChecked == true,
        }.ToJsonString();
    }

    /// <summary>Puts a recorded search into the find bar without starting a search of its own; false when the argument is not one.</summary>
    private bool ApplyFindArgument(string argument)
    {
        JsonObject? o;
        try { o = JsonNode.Parse(argument) as JsonObject; }
        catch (JsonException) { return false; }
        if (o?["text"] is not JsonValue value || !value.TryGetValue<string>(out var text) || text.Length == 0) return false;
        bool Flag(string key) => o[key] is JsonValue flag && flag.TryGetValue<bool>(out bool on) && on;
        _findSeeding = true;
        try
        {
            if (!_find.IsVisible || _find.FilesMode) _find.Open(files: false, _find.ReplaceShown);
            _find.FindBox.Text = text;
            _find.RegexToggle.IsChecked = Flag("regex");
            _find.CaseToggle.IsChecked = Flag("case");
            _find.WordToggle.IsChecked = Flag("word");
            _find.SelectionToggle.IsChecked = Flag("selection");
        }
        finally { _findSeeding = false; }
        return true;
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
