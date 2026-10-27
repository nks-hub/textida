using System.Globalization;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Actions around a save (P8.3.1): formatters before it, tools and the upload after it. The order, the filter for an autosave and the guard against a loop are in
/// <see cref="SaveActionRunner"/>; this part reads the lists (the profile and the <c>.textida</c> folder of the file), runs the tool and the upload through the
/// existing launcher and the existing upload, and tells the user what did not happen.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The order of a save with actions; a test replaces it to stand in for the formatters of the catalog.</summary>
    internal SaveActionRunner SaveActionRunner { get; set; } = new();

    private string SaveActionsFilePath => Path.Combine(_app.ProfileDirectory, SaveActionConfig.FileName);

    internal SaveActionSet LoadSaveActions(string? documentPath) =>
        SaveActionConfig.Load(SaveActionsFilePath, SaveActionConfig.FindProjectFile(documentPath is null ? null : Path.GetDirectoryName(Path.GetFullPath(documentPath))));

    /// <summary>Runs <paramref name="save"/> with the actions of the document around it and reports what did not go well. Returns whether the save went through.</summary>
    private async Task<bool> SaveWithActionsAsync(DocTab tab, SaveOrigin origin, Func<Task<bool>> save)
    {
        var od = tab.Doc;
        var set = LoadSaveActions(od.Path);
        var report = await SaveActionRunner.RunAsync(od.Doc, set, origin, save, action => RunAfterSaveAsync(tab, action, interactive: origin == SaveOrigin.Manual));
        ReportSaveActions(set, report);
        return report.Saved;
    }

    /// <summary>The save of the timer: it saves only a document that has unsaved changes and a file, and puts the actions that an autosave runs around it.</summary>
    private async Task AutosaveWithActionsAsync(OpenDocument od)
    {
        if (od.Autosave is not { } autosave || od.Doc.FilePath is null || !od.Doc.IsModified || od.Views.FirstOrDefault() is not { } tab) return;
        await SaveWithActionsAsync(tab, SaveOrigin.Autosave, () => Task.FromResult(autosave.RunOnce()));
    }

    private async Task<string?> RunAfterSaveAsync(DocTab tab, SaveAction action, bool interactive)
    {
        if (action.Kind == SaveActionKind.Upload)
        {
            if (tab.Doc.RemoteCopy is null) return Loc.Get(K.SaveActionsUploadNoServer);
            return await UploadRemoteAsync(tab, saveFirst: false, interactive) ? null : Loc.Get(K.SaveActionsUploadNotDone);
        }
        var entry = _app.Tools.Load(tab.Path is null ? null : Path.GetDirectoryName(Path.GetFullPath(tab.Path))).Tools.FirstOrDefault(t => t.Spec.Id == action.Id);
        if (entry is null) return Loc.Get(K.SaveActionsToolUnknown, ("id", action.Id));
        return await RunToolAsync(entry, tab, interactive) ? null : Loc.Get(K.SaveActionsToolFailed, ("title", entry.Title));
    }

    private void ReportSaveActions(SaveActionSet set, SaveActionReport report)
    {
        string? status = null;
        foreach (var note in report.Notes)
        {
            string name = note.Action.Kind == SaveActionKind.Format ? ConfigFormatters.Find(note.Action.Id)?.Title ?? note.Action.Id : ActionName(note.Action);
            string? text = (note.Action.Kind, note.State) switch
            {
                (_, SaveActionState.Done or SaveActionState.Unchanged) => null,
                (SaveActionKind.Format, SaveActionState.TimedOut) => Loc.Get(K.SaveActionsFormatTimedOut, ("name", name), ("seconds", (set.FormatTimeout?.TotalSeconds ?? 0).ToString("0.##", CultureInfo.CurrentCulture))),
                (SaveActionKind.Format, SaveActionState.Skipped) => note.Reason.Length == 0 ? null : Loc.Get(K.SaveActionsFormatSkipped, ("reason", note.Reason)),
                (SaveActionKind.Format, _) => Loc.Get(K.SaveActionsFormatFailed, ("reason", note.Reason)),
                (SaveActionKind.Upload, SaveActionState.Skipped) => Loc.Get(K.SaveActionsUploadSkipped, ("reason", note.Reason)),
                _ => Loc.Get(K.SaveActionsActionFailed, ("name", name), ("reason", note.Reason)),
            };
            if (text is null) continue;
            _app.Output.Append(OutputChannel.Tasks, "save", text);
            // a tool or an upload that failed has already said why in the status bar; only what the save itself left unsaid goes there
            if (note.Action.Kind == SaveActionKind.Format || note.State == SaveActionState.Skipped) status ??= text;
        }
        if (status is not null) SetStatusMessage(status);
    }

    private string ActionName(SaveAction action) => action.Kind switch
    {
        SaveActionKind.Format => Loc.Get(K.SaveActionsFormat, ("name", ConfigFormatters.Find(action.Id)?.Title ?? action.Id)),
        SaveActionKind.Tool => Loc.Get(K.SaveActionsTool, ("name", _app.Tools.Load().Tools.FirstOrDefault(t => t.Spec.Id == action.Id)?.Title ?? action.Id)),
        _ => Loc.Get(K.SaveActionsUpload),
    };

    // ---- the list the user turns on and off ----

    /// <summary>Lists the actions of the open file and the formatters whose configuration exists next to it; a row switches an action on or off, or "only on manual save".</summary>
    internal CommandOutcome ShowSaveActions()
    {
        var tab = Active is { Custom: null, Hex: null } active ? active : null;
        var set = LoadSaveActions(tab?.Path);
        var rows = new List<QuickRow>();
        foreach (var error in set.Errors)
        {
            string problem = Loc.Get(K.SaveActionsProblem, ("reason", error));
            rows.Add(new QuickRow(problem, null, null, null, false, Loc.Get(K.ToolsInvalid), problem, () => { }));
        }
        foreach (var action in set.Actions)
        {
            string name = ActionName(action);
            bool editable = action.Source == SaveActionSource.Profile;
            string? locked = editable ? null : Loc.Get(K.SaveActionsProjectRow);
            string state = Loc.Get(action.Enabled ? K.SaveActionsOn : K.SaveActionsOff) + (action.OnlyManual ? ", " + Loc.Get(K.SaveActionsOnlyManual) : "") + (editable ? "" : ", " + Loc.Get(K.SaveActionsFromProject));
            rows.Add(new QuickRow(name, null, state, null, editable, locked, name + ", " + state, () => SwitchSaveAction(action, e => e.Enabled = !action.Enabled, name, !action.Enabled)));
            string manual = Loc.Get(K.SaveActionsToggleManual, ("name", name));
            rows.Add(new QuickRow(manual, null, null, null, editable, locked, manual, () => SwitchSaveAction(action, e => e.OnlyManual = !action.OnlyManual, manual, !action.OnlyManual)));
        }
        if (tab?.Path is { } path)
            foreach (var formatter in ConfigFormatters.Offered(path).Where(f => !set.Actions.Any(a => a.Kind == SaveActionKind.Format && a.Id == f.Id)))
            {
                string name = Loc.Get(K.SaveActionsFormat, ("name", formatter.Title));
                string detail = Loc.Get(K.SaveActionsOffered, ("file", Path.GetFileName(ConfigFormatters.ConfigFor(formatter, path)!)));
                rows.Add(new QuickRow(name, null, detail, null, true, null, name + ", " + detail,
                    () => SwitchSaveAction(new SaveAction(SaveActionKind.Format, formatter.Id, false, false), e => e.Enabled = true, name, true)));
            }
        if (tab?.Doc.RemoteCopy is not null && !set.Actions.Any(a => a.Kind == SaveActionKind.Upload))
        {
            string name = Loc.Get(K.SaveActionsUpload), detail = Loc.Get(K.SaveActionsOfferedUpload);
            rows.Add(new QuickRow(name, null, detail, null, true, null, name + ", " + detail,
                () => SwitchSaveAction(new SaveAction(SaveActionKind.Upload, "", false, false), e => e.Enabled = true, name, true)));
        }
        string open = Loc.Get(K.SaveActionsOpen);
        rows.Add(new QuickRow(open, null, SaveActionsFilePath, null, true, null, open, OpenSaveActionsFile));
        ShowChoice(Loc.Get(K.SaveActionsWatermark), Loc.Get(K.A11ySaveActions), rows);
        return CommandOutcome.Handled;
    }

    private void SwitchSaveAction(SaveAction action, Action<SaveActionEntry> change, string name, bool nowOn)
    {
        try { SaveActionConfig.Edit(SaveActionsFilePath, action.Kind, action.Id, change); }
        catch (Exception e) when (e is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            SetStatusMessage(Loc.Get(K.SaveActionsFileFailed, ("reason", e.Message)));
            return;
        }
        SetStatusMessage(Loc.Get(K.SaveActionsSwitched, ("name", name), ("state", Loc.Get(nowOn ? K.SaveActionsOn : K.SaveActionsOff))));
        ShowSaveActions();
    }

    /// <summary>Opens the file of the actions; a first call writes an empty one to start from (it sets no time limit for formatters: that value is yours to choose).</summary>
    private void OpenSaveActionsFile()
    {
        try
        {
            if (!File.Exists(SaveActionsFilePath)) SaveActionConfig.Write(SaveActionsFilePath, new SaveActionsFile());
            OpenFile(SaveActionsFilePath, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SetStatusMessage(e.Message); }
    }
}
