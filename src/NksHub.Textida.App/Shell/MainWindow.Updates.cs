using System.ComponentModel;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Remote;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Help, Check for Updates / Return to the Previous Version (P3.7.2). A check happens only when the user asks, or at start with <c>updates.checkOnStart</c> (off by default); it
/// is one GET of the signed feed. Installing needs the documents safe first, then the helper switches the folders while Textida is closed and starts it again; the journal restores the tabs.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The last update command; tests wait for it.</summary>
    internal Task UpdateTask { get; private set; } = Task.CompletedTask;

    /// <summary>Tests point the updater at a local feed and a temporary folder; null builds the real one.</summary>
    internal AppUpdater? UpdaterOverride { get; set; }

    internal bool HasUnsavedDocuments => _docs.Any(d => d.HasUnsavedWork);

    internal CommandOutcome CheckForUpdates()
    {
        UpdateTask = CheckUpdatesAsync(interactive: true);
        return CommandOutcome.Handled;
    }

    internal CommandOutcome ReturnToPreviousVersion()
    {
        UpdateTask = ReturnAsync();
        return CommandOutcome.Handled;
    }

    /// <summary>Called once when the first window is shown; does nothing unless the user turned the check on.</summary>
    internal void CheckUpdatesOnStart()
    {
        if (_app.Workbench.Settings.GetBool("updates.checkOnStart")) UpdateTask = CheckUpdatesAsync(interactive: false);
    }

    private AppUpdater? OpenUpdater(bool interactive)
    {
        var updater = UpdaterOverride ?? new AppUpdater(new HttpClient(_app.Remote.Policy().CreateHttpHandler()), AppUpdater.DefaultFeedUrl, AppUpdater.DefaultPublicKey, AppContext.BaseDirectory);
        if (!OperatingSystem.IsWindows() || !updater.IsInstalled) { if (interactive) SetStatusMessage(Loc.Get(K.UpdateUnavailable)); return null; }
        if (!updater.IsConfigured) { if (interactive) SetStatusMessage(Loc.Get(K.UpdateNotConfigured)); return null; }
        return updater;
    }

    private async Task CheckUpdatesAsync(bool interactive)
    {
        AppUpdater? updater;
        try { updater = OpenUpdater(interactive); }
        catch (RemoteException e) { if (interactive) SetStatusMessage(Loc.Get(K.UpdateFailed, ("reason", e.Message))); return; }
        if (updater is null) return;
        try
        {
            if (interactive) SetStatusMessage(Loc.Get(K.UpdateChecking));
            var current = AppUpdater.InstalledVersion(updater.InstallDir);
            var release = await updater.CheckAsync(current, AppUpdater.CurrentRid, CancellationToken.None);
            if (release is null)
            {
                if (interactive) SetStatusMessage(Loc.Get(K.UpdateCurrent, ("version", current)));
                return;
            }
            if (!interactive)
            {
                ShowNotice("update", Loc.Get(K.UpdateBar, ("version", release.Version)), [(Loc.Get(K.UpdateInstall), () => UpdateTask = InstallAsync(updater, release, current))]);
                return;
            }
            await InstallAsync(updater, release, current);
        }
        catch (UpdateException e)
        {
            if (interactive) SetStatusMessage(Loc.Get(K.UpdateFailed, ("reason", e.Message)));
        }
    }

    private async Task InstallAsync(AppUpdater updater, UpdateRelease release, string current)
    {
        int answer = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.UpdateTitle), Loc.Get(K.UpdateAvailable, ("version", release.Version), ("current", current)),
            [new DialogButton(Loc.Get(K.UpdateInstall), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)]));
        if (answer != 0 || !DocumentsAreSafe()) return;
        var download = Path.Combine(Path.GetTempPath(), "textida-download-" + Guid.NewGuid().ToString("N")[..12]);
        try
        {
            SetStatusMessage(Loc.Get(K.UpdateDownloading, ("version", release.Version)));
            var zip = await updater.DownloadAsync(release, download, CancellationToken.None);
            var staged = updater.Stage(zip, release);
            if (!DocumentsAreSafe()) return;
            updater.Switch(staged, restart: true);
        }
        catch (Exception e) when (e is UpdateException or Win32Exception or IOException or UnauthorizedAccessException)
        {
            SetStatusMessage(Loc.Get(K.UpdateFailed, ("reason", e.Message)));
            return;
        }
        finally { if (Directory.Exists(download)) Directory.Delete(download, recursive: true); }
        _app.Exit();
    }

    private async Task ReturnAsync()
    {
        AppUpdater? updater;
        try { updater = OpenUpdater(interactive: true); }
        catch (RemoteException e) { SetStatusMessage(Loc.Get(K.UpdateFailed, ("reason", e.Message))); return; }
        if (updater is null) return;
        if (!updater.HasPrevious) { SetStatusMessage(Loc.Get(K.UpdateNoPrevious)); return; }
        var previous = AppUpdater.InstalledVersion(updater.PreviousDir);
        int answer = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.UpdateReturnTitle), Loc.Get(K.UpdateReturnAsk, ("version", previous)),
            [new DialogButton(Loc.Get(K.UpdateReturn), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)]));
        if (answer != 0 || !DocumentsAreSafe()) return;
        try { updater.Rollback(restart: true); }
        catch (Exception e) when (e is UpdateException or Win32Exception or IOException)
        {
            SetStatusMessage(Loc.Get(K.UpdateFailed, ("reason", e.Message)));
            return;
        }
        _app.Exit();
    }

    /// <summary>
    /// Textida ends by itself for the switch, so no unsaved text may depend on a question at the end. With a working journal the text is in it (the same hot exit as closing the window);
    /// without one every window must be clean. The tab layout is written before the exit so the restart opens the same tabs.
    /// </summary>
    private bool DocumentsAreSafe()
    {
        if (!_app.Journal.IsActive && _app.Windows.Any(w => w.HasUnsavedDocuments))
        {
            SetStatusMessage(Loc.Get(K.UpdateUnsaved));
            return false;
        }
        _app.SaveSession();
        return true;
    }
}
