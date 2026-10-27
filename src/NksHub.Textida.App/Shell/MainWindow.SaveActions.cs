using System.Globalization;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Remote;
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

    /// <summary>The actions for a file; without <c>formatTimeoutSeconds</c> a formatter gets the provisional limit of <see cref="PluginLimitsPlaceholders.FormatOnSaveTimeoutSeconds"/>.</summary>
    internal SaveActionSet LoadSaveActions(string? documentPath)
    {
        var set = SaveActionConfig.Load(SaveActionsFilePath, SaveActionConfig.FindProjectFile(documentPath is null ? null : Path.GetDirectoryName(Path.GetFullPath(documentPath))));
        return set with { FormatTimeout = set.FormatTimeout ?? TimeSpan.FromSeconds(PluginLimitsPlaceholders.FormatOnSaveTimeoutSeconds) };
    }

    /// <summary>Runs <paramref name="save"/> with the actions of the document around it and reports what did not go well. Returns whether the save went through.</summary>
    /// <param name="path">The file a Save As writes; the actions of its folder apply.</param>
    private async Task<bool> SaveWithActionsAsync(DocTab tab, SaveOrigin origin, Func<Task<bool>> save, string? path = null)
    {
        var od = tab.Doc;
        var set = LoadSaveActions(path ?? od.Path);
        var report = await SaveActionRunner.RunAsync(od.Doc, set, origin, save, action => RunAfterSaveAsync(tab, action, interactive: origin == SaveOrigin.Manual), path: path);
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
            if (tab.Doc.RemoteCopy is not null) return await UploadRemoteAsync(tab, saveFirst: false, interactive) ? null : Loc.Get(K.SaveActionsUploadNotDone);
            if (action.Server is { } server) return await UploadToProjectServerAsync(tab, server, interactive);
            return Loc.Get(K.SaveActionsUploadNoServer);
        }
        if (action.Kind == SaveActionKind.Lint)
        {
            if (Linters.Find(action.Id) is not { } linter) return Loc.Get(K.SaveActionsToolUnknown, ("id", action.Id));
            // the run is not awaited: a slow linter must not hold the save, and it reports for itself
            if (tab.Path is { } path && linter.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) _ = RunLinterAsync(tab, linter, interactive, manual: false);
            return null;
        }
        var entry = _app.Tools.Load(tab.Path is null ? null : Path.GetDirectoryName(Path.GetFullPath(tab.Path))).Tools.FirstOrDefault(t => t.Spec.Id == action.Id);
        if (entry is null) return Loc.Get(K.SaveActionsToolUnknown, ("id", action.Id));
        return await RunToolAsync(entry, tab, interactive) ? null : Loc.Get(K.SaveActionsToolFailed, ("title", entry.Title));
    }

    private RemoteConnection? ConnectionNamed(string nameOrId)
    {
        var all = _app.Remote.Connections.Load();
        return all.FirstOrDefault(c => c.Id == nameOrId) ?? all.FirstOrDefault(c => string.Equals(c.Name, nameOrId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Sends a saved file of a project to the default server the project file names. The project file comes with the project, so sending its files to one of your connections waits for a yes
    /// (after an autosave nobody is there to say it, and nothing is sent). The bytes go through a copy in <see cref="RemoteServices.Deployments"/> that keeps the revision of the last upload,
    /// so a file that changed on the server since then, or that the project never sent, is a conflict and is not overwritten without a yes.
    /// </summary>
    /// <returns>Null when the server holds the saved bytes now, otherwise why not.</returns>
    private async Task<string?> UploadToProjectServerAsync(DocTab tab, ServerTarget server, bool interactive)
    {
        var od = tab.Doc;
        string Say(string text)
        {
            SetStatusMessage(text);
            return text;
        }
        if (tab.Path is not { } path || server.RemotePathOf(path) is not { } remotePath) return Say(Loc.Get(K.SaveActionsServerOutside, ("folder", server.ProjectFolder)));
        if (ConnectionNamed(server.Connection) is not { } connection) return Say(Loc.Get(K.SaveActionsServerUnknown, ("name", server.Connection)));
        string approval = server.ApprovalKey(connection.Id);
        if (!_app.ToolApprovals.Has(approval))
        {
            string refused = Loc.Get(K.SaveActionsServerNotApproved, ("folder", server.ProjectFolder), ("name", connection.Name));
            if (!interactive) return Say(refused);
            var request = new ChoiceRequest(Loc.Get(K.SaveActionsServerConfirmTitle),
                Loc.Get(K.SaveActionsServerConfirm, ("folder", server.ProjectFolder), ("name", connection.Name), ("remote", server.RemoteFolder)),
                [new DialogButton(Loc.Get(K.SaveActionsServerConfirmUpload), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)],
                [Loc.Get(K.SaveActionsServerConfirmFile, ("file", Path.Combine(server.ProjectFolder, SaveActionConfig.ProjectFolderName, SaveActionConfig.FileName)))]);
            if (await _app.Dialogs.ChooseAsync(this, request) != 0) return Say(refused);
            _app.ToolApprovals.Approve(approval);
        }

        WorkingCopy copy;
        string staged;
        try
        {
            copy = _app.Remote.Deployments.Stage(connection.Id, remotePath, path);
            staged = RemoteTransfer.HashOf(copy.LocalPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Say(Loc.Get(K.RemoteUploadFailed, ("title", od.Title), ("reason", e.Message))); }
        // the copy has to be the bytes this save wrote, not what another program put in the file since
        if (od.Doc.Fingerprint is not { HasHash: true } written || !string.Equals(staged, written.Sha256, StringComparison.OrdinalIgnoreCase))
            return Say(Loc.Get(K.SaveActionsUploadChanged));

        await using var client = await ConnectAsync(connection);
        if (client is null) return Loc.Get(K.SaveActionsUploadNotDone);
        var mode = _app.Workbench.Settings.GetString("remote.versionCheck") == "content" ? VersionCheck.Content : VersionCheck.Stat;
        var options = new TransferOptions { Log = e => RemoteLog(connection, DescribeStep(e)) };
        _acceptedServerVersion.TryGetValue(od, out var accepted);
        try
        {
            var outcome = await RemoteSync.UploadAsync(client, _app.Remote.Deployments, copy, mode, accepted, options, CancellationToken.None);
            // the server already holds exactly these bytes (a first save of a project that was deployed before): no conflict, the upload only records them as the base
            if (outcome is { Status: UploadStatus.Conflict, Server: { } found } && found.Sha256 == staged)
                outcome = await RemoteSync.UploadAsync(client, _app.Remote.Deployments, copy, mode, found, options, CancellationToken.None);
            switch (outcome.Status)
            {
                case UploadStatus.Uploaded:
                    _acceptedServerVersion.Remove(od);
                    SetStatusMessage(Loc.Get(K.RemoteUploaded, ("title", od.Title), ("name", connection.Name)));
                    return null;
                case UploadStatus.NothingToSend:
                    SetStatusMessage(Loc.Get(K.RemoteNothingToSend, ("title", od.Title)));
                    return null;
                default:
                    if (interactive) await ResolveUploadConflictAsync(tab, connection, client, outcome, async () => await UploadToProjectServerAsync(tab, server, interactive: true));
                    else
                    {
                        RemoteLog(connection, Loc.Get(K.RemoteLogConflict, ("path", outcome.Copy.RemotePath)));
                        SetStatusMessage(Loc.Get(K.SaveActionsUploadConflict, ("title", od.Title)));
                    }
                    return Loc.Get(K.SaveActionsUploadNotDone);
            }
        }
        catch (Exception e) when (e is RemoteException or IOException)
        {
            RemoteLog(connection, Loc.Get(K.RemoteUploadFailed, ("title", od.Title), ("reason", ReasonOf(e))));
            SetStatusMessage(Loc.Get(K.RemoteUploadFailed, ("title", od.Title), ("reason", ReasonOf(e))));
            return ReasonOf(e);
        }
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
        SaveActionKind.Lint => Loc.Get(K.SaveActionsLint, ("name", Linters.Find(action.Id)?.Title ?? action.Id)),
        SaveActionKind.Tool => Loc.Get(K.SaveActionsTool, ("name", _app.Tools.Load().Tools.FirstOrDefault(t => t.Spec.Id == action.Id)?.Title ?? action.Id)),
        _ => action.Server is { } server ? Loc.Get(K.SaveActionsUploadTo, ("name", server.Connection), ("folder", server.RemoteFolder)) : Loc.Get(K.SaveActionsUpload),
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
        if (tab?.Path is { } linted)
            foreach (var linter in Linters.For(linted).Where(l => !set.Actions.Any(a => a.Kind == SaveActionKind.Lint && a.Id == l.Id)))
            {
                string name = Loc.Get(K.SaveActionsLint, ("name", linter.Title)), detail = Loc.Get(K.SaveActionsOfferedLint);
                rows.Add(new QuickRow(name, null, detail, null, true, null, name + ", " + detail,
                    () => SwitchSaveAction(new SaveAction(SaveActionKind.Lint, linter.Id, false, false), e => e.Enabled = true, name, true)));
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
