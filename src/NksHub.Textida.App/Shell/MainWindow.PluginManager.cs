using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Manager;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Keys;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The plugin manager (P3b.4.2): a list of the built-in and installed plugins in the quick list, and for the chosen one a question with the actions that apply (enable or disable, update, roll back,
/// permissions, uninstall). A ZIP is installed only after the user has seen the permissions it asks for and agreed; the dialogs are the ones of the app, so the look is the calm one of the rest.
/// </summary>
internal sealed partial class MainWindow
{
    internal CommandOutcome ShowPluginManager()
    {
        var plugins = _app.Plugins;
        string install = Loc.Get(K.PluginsInstallRow);
        var rows = new List<QuickRow> { new(install, null, Loc.Get(K.PluginsInstallHint), null, true, null, install, () => _ = InstallPluginAsync()) };
        foreach (var p in plugins.Manager.Installed())
        {
            string detail = p.Problem is { } why ? Loc.Get(K.PluginsRowProblem, ("reason", why)) : Loc.Get(K.PluginsRowDetail, ("version", p.Version), ("allowed", p.Granted.Count));
            string id = p.Id;
            var state = StateOf(id);
            rows.Add(new QuickRow(p.Name, Loc.Get(p.Enabled ? K.PluginsOriginInstalled : K.PluginsOriginDisabled), state is null ? detail : state + ". " + detail, null, true, null,
                state is null ? p.Name : p.Name + ", " + state, () => _ = ManagePluginAsync(id)));
        }
        foreach (var (id, name) in PluginServices.BuiltIns)
        {
            var granted = string.Join(", ", plugins.Permissions.GrantsOf(id).Select(g => g.Permission).Distinct().Order(StringComparer.Ordinal));
            string detail = Loc.Get(K.PluginsRowBuiltinDetail, ("list", granted.Length > 0 ? granted : "-"));
            var state = StateOf(id);
            rows.Add(new QuickRow(name, Loc.Get(K.PluginsOriginBuiltin), state is null ? detail : state + ". " + detail, null, true, null,
                state is null ? name : name + ", " + state, () => _ = ManageBuiltInAsync(id, name)));
        }
        ShowChoice(Loc.Get(K.PluginsManagerWatermark), Loc.Get(K.A11yPluginsManager), rows);
        return CommandOutcome.Handled;
    }

    /// <summary>The state of the process of a plugin in words; null for a plugin without a process.</summary>
    private string? StateOf(string pluginId) => _app.Plugins.StatusOf(pluginId) is { } status ? PluginServices.StateText(status) : null;

    /// <summary>
    /// The actions that fit the state of the process of a plugin: start when it does not run (a plugin that was turned off gets a clean slate, one that waits for its restart starts at once),
    /// stop while it runs or waits, restart while it runs. A plugin that is on its way out, or has no process, has none.
    /// </summary>
    private List<(string Label, Func<Task> Run)> ProcessActions(string id, string name)
    {
        var plugins = _app.Plugins;
        var actions = new List<(string Label, Func<Task> Run)>();
        if (plugins.StatusOf(id) is not { } status) return actions;
        if (status.State is PluginState.Inactive or PluginState.Stopped or PluginState.Disabled or PluginState.WaitingRestart)
            actions.Add((Loc.Get(K.PluginsManageStart), () => ChangeProcessAsync(name, plugins.StartPluginAsync(id), K.PluginsStatusStarted)));
        if (status.State is PluginState.Running or PluginState.Starting or PluginState.WaitingRestart)
            actions.Add((Loc.Get(K.PluginsManageStop), () => ChangeProcessAsync(name, plugins.StopPluginAsync(id), K.PluginsStatusStopped)));
        if (status.State == PluginState.Running)
            actions.Add((Loc.Get(K.PluginsManageRestart), () => ChangeProcessAsync(name, plugins.RestartPluginAsync(id), K.PluginsStatusRestarted)));
        return actions;
    }

    /// <summary>Waits for a start, stop or restart and says in the status bar what came of it.</summary>
    private async Task ChangeProcessAsync(string name, Task<string?> change, string doneKey)
    {
        var problem = await change;
        SetStatusMessage(problem is null ? Loc.Get(doneKey, ("name", name)) : Loc.Get(K.PluginsStatusFailed, ("name", name), ("reason", problem)));
    }

    /// <summary>A plugin that comes with the editor: what its process is doing and what it may do, with the start, stop and restart that fit.</summary>
    internal async Task ManageBuiltInAsync(string id, string name)
    {
        var plugins = _app.Plugins;
        var state = StateOf(id);
        var message = state is null ? name : Loc.Get(K.PluginsManageProcess, ("name", name), ("state", state));
        var allowed = plugins.Permissions.GrantsOf(id).Select(g => g.Permission).Distinct().Order(StringComparer.Ordinal).Select(p => $"{p}: {PermissionMeaning(p)}").ToList();
        var actions = ProcessActions(id, name);
        var buttons = actions.Select((a, i) => new DialogButton(a.Label, IsDefault: i == 0)).Append(new DialogButton(Loc.Get(K.PluginsManageClose), IsCancel: true)).ToList();
        int picked = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(name, message, buttons, allowed));
        if (picked >= 0 && picked < actions.Count) await actions[picked].Run();
    }

    private static string PermissionMeaning(string permission) => Loc.Get(permission switch
    {
        DocumentPermissions.Read => K.PluginsPermDocumentRead,
        DocumentPermissions.EditPropose => K.PluginsPermDocumentEditPropose,
        DocumentPermissions.EditDirect => K.PluginsPermDocumentEditDirect,
        DocumentPermissions.ClipboardWrite => K.PluginsPermClipboardWrite,
        DocumentPermissions.Virtual => K.PluginsPermDocumentVirtual,
        DocumentPermissions.Source => K.PluginsPermDocumentSource,
        DocumentPermissions.Preview => K.PluginsPermDocumentPreview,
        DocumentPermissions.AgentPanel => K.PluginsPermAgentPanel,
        DocumentPermissions.LanguageServer => K.PluginsPermLanguageServer,
        PluginPermissionNames.Network => K.PluginsPermNetwork,
        _ => K.PluginsPermWorkspaceFiles,
    });

    private static string ErrorText(PluginInstallError error) => Loc.Get(error switch
    {
        PluginInstallError.NotAZip => K.PluginsErrorNotAZip,
        PluginInstallError.UnsafePath => K.PluginsErrorUnsafePath,
        PluginInstallError.SymbolicLink => K.PluginsErrorSymbolicLink,
        PluginInstallError.TooLarge => K.PluginsErrorTooLarge,
        PluginInstallError.TooManyEntries => K.PluginsErrorTooManyEntries,
        PluginInstallError.DuplicateEntry => K.PluginsErrorDuplicateEntry,
        PluginInstallError.NoManifest => K.PluginsErrorNoManifest,
        PluginInstallError.InvalidManifest => K.PluginsErrorInvalidManifest,
        PluginInstallError.IdCollision => K.PluginsErrorIdCollision,
        PluginInstallError.NotNewer => K.PluginsErrorNotNewer,
        _ => K.PluginsErrorFailed,
    });

    private async Task ShowPluginErrorAsync(Exception e)
    {
        var (text, details) = e is PluginInstallException pe ? (ErrorText(pe.Error), [pe.Message, .. pe.Details]) : (Loc.Get(K.PluginsErrorFailed), new List<string> { e.Message });
        await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.PluginsErrorTitle), text, [new DialogButton(Loc.Get(K.DialogOk), IsDefault: true)], details));
    }

    private static bool IsInstallFailure(Exception e) => e is PluginInstallException or IOException or UnauthorizedAccessException;

    /// <summary>Makes the app see a plugin that was installed, updated, switched or removed: its commands, and the clips, converters and highlighters of a data plugin.</summary>
    private void AfterPluginChange(string id)
    {
        _app.Plugins.BindInstalled(_app.Plugins.Manager.Loaded(id));
        _app.ReloadClips();
    }

    /// <summary>
    /// Unpacks and checks a ZIP, shows the permissions it asks for and installs it when the user agrees. <paramref name="zip"/> is asked for when it is null.
    /// Returns whether the plugin was installed or updated. With <paramref name="updateOf"/> the ZIP has to hold that plugin.
    /// </summary>
    internal async Task<bool> InstallPluginAsync(string? zip = null, string? updateOf = null)
    {
        var manager = _app.Plugins.Manager;
        zip ??= (await _app.Dialogs.PickFilesAsync(this)).FirstOrDefault();
        if (zip is null) return false;
        PluginPackage package;
        try { package = manager.Prepare(zip); }
        catch (Exception e) when (IsInstallFailure(e))
        {
            await ShowPluginErrorAsync(e);
            return false;
        }
        if (updateOf is not null && package.Id != updateOf)
        {
            manager.Discard(package);
            await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.PluginsErrorTitle), Loc.Get(K.PluginsErrorOtherPlugin, ("id", package.Id)), [new DialogButton(Loc.Get(K.DialogOk), IsDefault: true)]));
            return false;
        }

        bool update = package.InstalledVersion is not null;
        var lines = package.Requested.Select(p => Loc.Get(package.NewPermissions.Contains(p) ? K.PluginsConsentNew : K.PluginsConsentKnown, ("permission", p), ("meaning", PermissionMeaning(p)))).ToList();
        if (lines.Count == 0) lines.Add(Loc.Get(K.PluginsConsentNone));
        string message = Loc.Get(update ? K.PluginsConsentUpdate : K.PluginsConsentInstall, ("name", package.Name), ("old", package.InstalledVersion ?? ""), ("version", package.Version))
            + " " + Loc.Get(K.PluginsConsentNotice) + (package.FromInternet ? " " + Loc.Get(K.PluginsConsentDownloaded) : "");
        var request = new ChoiceRequest(Loc.Get(update ? K.PluginsConsentTitleUpdate : K.PluginsConsentTitleInstall), message,
            [new DialogButton(Loc.Get(update ? K.PluginsConsentAllowUpdate : K.PluginsConsentAllow), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)], lines);
        if (await _app.Dialogs.ChooseAsync(this, request) != 0)
        {
            manager.Discard(package);
            return false;
        }
        try { await manager.CommitAsync(package, package.NewPermissions); }
        catch (Exception e) when (IsInstallFailure(e))
        {
            manager.Discard(package);
            await ShowPluginErrorAsync(e);
            return false;
        }
        AfterPluginChange(package.Id);
        SetStatusMessage(update ? Loc.Get(K.PluginsStatusUpdated, ("name", package.Name), ("version", package.Version)) : Loc.Get(K.PluginsStatusInstalled, ("name", package.Name), ("version", package.Version)));
        return true;
    }

    internal async Task ManagePluginAsync(string id)
    {
        var manager = _app.Plugins.Manager;
        if (manager.Installed().FirstOrDefault(p => p.Id == id) is not { } plugin) return;
        var actions = new List<(string Label, Func<Task> Run)>
        {
            plugin.Enabled ? (Loc.Get(K.PluginsManageDisable), () => SetPluginEnabledAsync(plugin, false)) : (Loc.Get(K.PluginsManageEnable), () => SetPluginEnabledAsync(plugin, true)),
        };
        if (plugin.Enabled) actions.AddRange(ProcessActions(id, plugin.Name));
        actions.Add((Loc.Get(K.PluginsManageUpdate), () => InstallPluginAsync(updateOf: id)));
        if (plugin.PreviousVersion is { } previous) actions.Add((Loc.Get(K.PluginsManageRollback, ("version", previous)), () => RollBackPluginAsync(plugin)));
        actions.Add((Loc.Get(K.PluginsManagePermissions), () => { ShowPluginPermissions(id); return Task.CompletedTask; }));
        actions.Add((Loc.Get(K.PluginsManageUninstall), () => UninstallPluginAsync(plugin)));

        string message = Loc.Get(K.PluginsManageMessage, ("name", plugin.Name), ("version", plugin.Version), ("state", Loc.Get(plugin.Enabled ? K.PluginsManageEnabled : K.PluginsManageDisabled)))
            + (plugin.PreviousVersion is { } kept ? " " + Loc.Get(K.PluginsManagePrevious, ("previous", kept)) : "")
            + (plugin.Enabled && StateOf(id) is { } process ? " " + Loc.Get(K.PluginsManageProcess, ("name", plugin.Name), ("state", process)) : "");
        var buttons = actions.Select((a, i) => new DialogButton(a.Label, IsDefault: i == 0)).Append(new DialogButton(Loc.Get(K.PluginsManageClose), IsCancel: true)).ToList();
        var conflicts = _app.Workbench.KeyConflictsOf(id).Select(KeyConflictText).ToList();
        if (conflicts.Count > 0) message += " " + Loc.Get(K.PluginsKeysMessage, ("count", conflicts.Count));
        int picked = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(plugin.Name, message, buttons, conflicts));
        if (picked >= 0 && picked < actions.Count) await actions[picked].Run();
    }

    /// <summary>One line for a clash of keys: a binding of a built-in, profile or user layer wins over a plugin's, so the plugin's keys then do nothing.</summary>
    private static string KeyConflictText(KeyConflict c)
    {
        string Origin(KeyBinding b) => $"{b.Layer}: {b.Source}";
        return c.Kind switch
        {
            KeyConflictKind.Overridden => Loc.Get(K.PluginsKeysOverridden, ("keys", c.Loser.Sequence.ToString()), ("command", c.Loser.CommandId), ("winner", c.Winner.CommandId), ("origin", Origin(c.Winner))),
            KeyConflictKind.Prefix => Loc.Get(K.PluginsKeysPrefix, ("keys", c.Winner.Sequence.ToString()), ("command", c.Winner.CommandId), ("other", c.Loser.CommandId), ("otherKeys", c.Loser.Sequence.ToString())),
            _ => Loc.Get(K.PluginsKeysClash, ("keys", c.Winner.Sequence.ToString()), ("command", c.Loser.CommandId), ("other", c.Winner.CommandId), ("origin", Origin(c.Winner))),
        };
    }

    private async Task SetPluginEnabledAsync(ManagedPlugin plugin, bool enabled)
    {
        await _app.Plugins.Manager.SetEnabledAsync(plugin.Id, enabled);
        AfterPluginChange(plugin.Id);
        SetStatusMessage(Loc.Get(enabled ? K.PluginsStatusEnabled : K.PluginsStatusDisabled, ("name", plugin.Name)));
    }

    private async Task RollBackPluginAsync(ManagedPlugin plugin)
    {
        var manager = _app.Plugins.Manager;
        var fresh = manager.RollBackPermissions(plugin.Id);
        if (fresh.Count > 0)
        {
            var request = new ChoiceRequest(Loc.Get(K.PluginsRollbackTitle), Loc.Get(K.PluginsRollbackMessage, ("name", plugin.Name), ("version", plugin.PreviousVersion ?? "")),
                [new DialogButton(Loc.Get(K.PluginsConsentAllow), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)],
                [.. fresh.Select(p => Loc.Get(K.PluginsConsentNew, ("permission", p), ("meaning", PermissionMeaning(p))))]);
            if (await _app.Dialogs.ChooseAsync(this, request) != 0) return;
        }
        try { await manager.RollBackAsync(plugin.Id, fresh); }
        catch (Exception e) when (IsInstallFailure(e))
        {
            await ShowPluginErrorAsync(e);
            return;
        }
        AfterPluginChange(plugin.Id);
        SetStatusMessage(Loc.Get(K.PluginsStatusRolledBack, ("name", plugin.Name), ("version", plugin.PreviousVersion ?? "")));
    }

    private async Task UninstallPluginAsync(ManagedPlugin plugin)
    {
        var request = new ChoiceRequest(Loc.Get(K.PluginsUninstallTitle), Loc.Get(K.PluginsUninstallMessage, ("name", plugin.Name)),
            [new DialogButton(Loc.Get(K.PluginsManageUninstall), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)]);
        if (await _app.Dialogs.ChooseAsync(this, request) != 0) return;
        try { await _app.Plugins.Manager.UninstallAsync(plugin.Id); }
        catch (Exception e) when (IsInstallFailure(e))
        {
            await ShowPluginErrorAsync(e);
            return;
        }
        _app.ReloadClips();
        SetStatusMessage(Loc.Get(K.PluginsStatusUninstalled, ("name", plugin.Name)));
    }

    /// <summary>The permissions the plugin asks for, each allowed or not; Enter flips one. Taking a permission back ends the requests of the plugin that depended on it.</summary>
    internal void ShowPluginPermissions(string id)
    {
        var manager = _app.Plugins.Manager;
        if (manager.Installed().FirstOrDefault(p => p.Id == id) is not { } plugin) return;
        if (plugin.Requested.Count == 0)
        {
            SetStatusMessage(Loc.Get(K.PluginsPermsNone, ("name", plugin.Name)));
            return;
        }
        var rows = plugin.Requested.Select(permission =>
        {
            bool allowed = plugin.Granted.Contains(permission);
            return new QuickRow(permission, Loc.Get(allowed ? K.PluginsPermsAllowed : K.PluginsPermsDenied), PermissionMeaning(permission), null, true, null, permission, () =>
            {
                manager.SetPermission(id, permission, !allowed);
                ShowPluginPermissions(id);
            });
        }).ToList();
        ShowChoice(Loc.Get(K.PluginsPermsWatermark, ("name", plugin.Name)), Loc.Get(K.A11yPluginsPerms), rows);
    }
}
