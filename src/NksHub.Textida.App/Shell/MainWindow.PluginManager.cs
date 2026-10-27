using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Manager;
using NksHub.Textida.Workbench.Commands;

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
            rows.Add(new QuickRow(p.Name, Loc.Get(p.Enabled ? K.PluginsOriginInstalled : K.PluginsOriginDisabled), detail, null, true, null, p.Name, () => _ = ManagePluginAsync(id)));
        }
        foreach (var (id, name) in PluginServices.BuiltIns)
        {
            var granted = string.Join(", ", plugins.Permissions.GrantsOf(id).Select(g => g.Permission).Distinct().Order(StringComparer.Ordinal));
            string detail = Loc.Get(K.PluginsRowBuiltinDetail, ("list", granted.Length > 0 ? granted : "-"));
            rows.Add(new QuickRow(name, Loc.Get(K.PluginsOriginBuiltin), detail, null, true, null, name, () => SetStatusMessage(detail)));
        }
        ShowChoice(Loc.Get(K.PluginsManagerWatermark), Loc.Get(K.A11yPluginsManager), rows);
        return CommandOutcome.Handled;
    }

    private static string PermissionMeaning(string permission) => Loc.Get(permission switch
    {
        DocumentPermissions.Read => K.PluginsPermDocumentRead,
        DocumentPermissions.EditPropose => K.PluginsPermDocumentEditPropose,
        DocumentPermissions.EditDirect => K.PluginsPermDocumentEditDirect,
        DocumentPermissions.ClipboardWrite => K.PluginsPermClipboardWrite,
        DocumentPermissions.Virtual => K.PluginsPermDocumentVirtual,
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
    /// Returns whether the plugin was installed or updated.
    /// </summary>
    internal async Task<bool> InstallPluginAsync(string? zip = null)
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
            (Loc.Get(K.PluginsManageUpdate), () => InstallPluginAsync()),
        };
        if (plugin.PreviousVersion is { } previous) actions.Add((Loc.Get(K.PluginsManageRollback, ("version", previous)), () => RollBackPluginAsync(plugin)));
        actions.Add((Loc.Get(K.PluginsManagePermissions), () => { ShowPluginPermissions(id); return Task.CompletedTask; }));
        actions.Add((Loc.Get(K.PluginsManageUninstall), () => UninstallPluginAsync(plugin)));

        string message = Loc.Get(K.PluginsManageMessage, ("name", plugin.Name), ("version", plugin.Version), ("state", Loc.Get(plugin.Enabled ? K.PluginsManageEnabled : K.PluginsManageDisabled)))
            + (plugin.PreviousVersion is { } kept ? " " + Loc.Get(K.PluginsManagePrevious, ("previous", kept)) : "");
        var buttons = actions.Select((a, i) => new DialogButton(a.Label, IsDefault: i == 0)).Append(new DialogButton(Loc.Get(K.PluginsManageClose), IsCancel: true)).ToList();
        int picked = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(plugin.Name, message, buttons));
        if (picked >= 0 && picked < actions.Count) await actions[picked].Run();
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
