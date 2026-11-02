using System.ComponentModel;
using Avalonia.Controls;
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

    /// <summary>Time after the start before the first automatic check, so that the start is not slowed. Decided: long enough for the window and the restored tabs to appear first.</summary>
    internal TimeSpan AutoCheckDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Time between two automatic checks while the editor runs. Decided: one request a day is enough for a release feed and is the cadence the owner asked for; a test shortens it.</summary>
    internal TimeSpan AutoCheckInterval { get; set; } = TimeSpan.FromHours(24);

    private readonly CancellationTokenSource _autoCheckStop = new();
    private (AppUpdater Updater, UpdateRelease Release, string Current)? _offered;
    private string? _noticedVersion;
    private bool _updateRunning;

    /// <summary>The loop of the automatic check; it ends when the window closes.</summary>
    internal Task AutoCheckTask { get; private set; } = Task.CompletedTask;

    /// <summary>The status bar badge of a newer version.</summary>
    internal Button UpdateItem => _updateItem;

    /// <summary>
    /// Called once when the first window is shown: tells the result of an update that just finished and starts the automatic check, which asks the signed feed after <see cref="AutoCheckDelay"/>
    /// and then every <see cref="AutoCheckInterval"/>. The setting <c>updates.checkOnStart</c> is read before every request; off means no request at all. The smoke run never calls this.
    /// </summary>
    internal void CheckUpdatesOnStart()
    {
        AnnounceUpdateResult();
        Closed += (_, _) => _autoCheckStop.Cancel();
        AutoCheckTask = AutoCheckLoopAsync();
    }

    private async Task AutoCheckLoopAsync()
    {
        try
        {
            var wait = AutoCheckDelay;
            while (!_autoCheckStop.IsCancellationRequested)
            {
                await Task.Delay(wait, _autoCheckStop.Token);
                wait = AutoCheckInterval;
                if (_app.Workbench.Settings.GetBool("updates.checkOnStart")) await CheckUpdatesAsync(interactive: false);
            }
        }
        catch (OperationCanceledException) { /* the window closed */ }
    }

    /// <summary>Starts the install of the version the badge and the bar offer.</summary>
    private void InstallOffered()
    {
        if (_offered is { } o && !_updateRunning) UpdateTask = InstallAsync(o.Updater, o.Release, o.Current);
    }

    /// <summary>
    /// Remembers what the feed offers (null: nothing newer) and shows or hides the badge; the bar appears once for each version. A failed check does not call this, so the badge stays
    /// until the version is installed or stops being newer.
    /// </summary>
    private void OfferUpdate(AppUpdater updater, UpdateRelease? release, string current, bool bar)
    {
        _offered = release is null ? null : (updater, release, current);
        _updateItem.IsVisible = release is not null;
        if (release is null) return;
        var text = Loc.Get(K.UpdateBadge, ("version", release.Version));
        _updateItem.Content = text;
        Avalonia.Automation.AutomationProperties.SetName(_updateItem, text);
        if (!bar || _noticedVersion == release.Version) return;
        _noticedVersion = release.Version;
        ShowNotice("update", Loc.Get(K.UpdateBar, ("version", release.Version)), [(Loc.Get(K.UpdateInstall), InstallOffered)]);
    }

    /// <summary>
    /// The client of the update check: it goes through the proxy of the network policy and follows a redirect only to another page of the host it asked; a policy that cannot be read throws
    /// <see cref="RemoteException"/>.
    /// </summary>
    internal HttpClient CreateUpdateHttpClient()
    {
        var handler = _app.Remote.Policy().CreateHttpHandler();
        handler.AllowAutoRedirect = false;
        return new HttpClient(new SameHostRedirectHandler(handler));
    }

    private AppUpdater? OpenUpdater(bool interactive)
    {
        var updater = UpdaterOverride ?? new AppUpdater(CreateUpdateHttpClient(), AppUpdater.DefaultFeedUrl, AppUpdater.DefaultPublicKey, AppUpdater.InstallationFolder(AppContext.BaseDirectory));
        if (updater.UsesSystemPackageManager) { if (interactive) SetStatusMessage(Loc.Get(K.UpdatePackageManager)); return null; }
        if (!updater.IsInstalled) { if (interactive) SetStatusMessage(Loc.Get(K.UpdateUnavailable)); return null; }
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
            OfferUpdate(updater, release, current, bar: !interactive);
            if (release is null)
            {
                if (interactive) SetStatusMessage(Loc.Get(K.UpdateCurrent, ("version", current)));
                return;
            }
            if (!interactive) return;
            await InstallAsync(updater, release, current);
        }
        catch (Exception e) when (e is UpdateException or HttpRequestException or FormatException or UriFormatException)
        {
            if (interactive) SetStatusMessage(Loc.Get(K.UpdateFailed, ("reason", e.Message)));
        }
    }

    private const string UpdateProgressKey = "update:progress";
    private const string UpdateFailedKey = "update:failed";
    private string UpdatedFromFile => Path.Combine(_app.ProfileDirectory, "updated-from.txt");

    private async Task InstallAsync(AppUpdater updater, UpdateRelease release, string current)
    {
        int answer = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.UpdateTitle), Loc.Get(K.UpdateAvailable, ("version", release.Version), ("current", current)),
            [new DialogButton(Loc.Get(K.UpdateInstall), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)], HelpTopic: "help.updates"));
        if (answer != 0 || !DocumentsAreSafe() || _updateRunning) return;
        _updateRunning = true;
        var download = Path.Combine(Path.GetTempPath(), "textida-download-" + Guid.NewGuid().ToString("N")[..12]);
        using var cancel = new CancellationTokenSource();
        DismissNotice(UpdateFailedKey);
        var bar = ShowNotice(UpdateProgressKey, ProgressText(release, 0), [(Loc.Get(K.DialogCancel), cancel.Cancel)]);
        long shown = Environment.TickCount64;
        bool switched = false;
        try
        {
            var zip = await updater.DownloadAsync(release, download, cancel.Token, done =>
            {
                if (done < release.Size && Environment.TickCount64 - shown < 300) return; // a few updates a second are enough
                shown = Environment.TickCount64;
                bar.SetMessage(ProgressText(release, done));
            });
            ShowNotice(UpdateProgressKey, Loc.Get(K.UpdateInstalling, ("version", release.Version)));
            var staged = await Task.Run(() => updater.Stage(zip, release));
            if (!DocumentsAreSafe()) return;
            try { File.WriteAllText(UpdatedFromFile, current); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* only the confirmation after the restart is lost */ }
            updater.Switch(staged, restart: true);
            switched = true;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            SetStatusMessage(Loc.Get(K.UpdateCancelled, ("version", release.Version)));
            return;
        }
        catch (Exception e)
        {
            ReportUpdateFailure(release, e);
            return;
        }
        finally
        {
            _updateRunning = false;
            if (!switched) DismissNotice(UpdateProgressKey);
            try { if (Directory.Exists(download)) Directory.Delete(download, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the next start removes it */ }
        }
        _app.Exit();
    }

    private static string ProgressText(UpdateRelease release, long done) => Loc.Get(K.UpdateProgress, ("version", release.Version),
        ("done", (done / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture)), ("total", (release.Size / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture)),
        ("percent", (done * 100 / Math.Max(1, release.Size)).ToString()));

    /// <summary>A bar that names the reason and offers the release page, and the details in the Tasks output; any exception type ends here.</summary>
    private void ReportUpdateFailure(UpdateRelease release, Exception e)
    {
        _app.Output.Append(OutputChannel.Tasks, Loc.Get(K.UpdateTitle), e.ToString());
        ShowNotice(UpdateFailedKey, Loc.Get(K.UpdateFailed, ("reason", e.Message)), [(Loc.Get(K.UpdateReleasePage), () => _ = OpenInBrowserAsync(new Uri(AppUpdater.ReleasePage(release))))], warning: true);
    }

    /// <summary>
    /// After a restart into a new version: tells where the update came from and offers the release notes. Also removes download folders that an earlier run left behind (older than a day).
    /// </summary>
    internal void AnnounceUpdateResult()
    {
        try
        {
            foreach (var old in new DirectoryInfo(Path.GetTempPath()).EnumerateDirectories("textida-download-*").Where(d => DateTime.UtcNow - d.LastWriteTimeUtc > TimeSpan.FromDays(1)))
                old.Delete(recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* a locked leftover stays for the next start */ }
        try
        {
            if (!File.Exists(UpdatedFromFile)) return;
            var from = File.ReadAllText(UpdatedFromFile).Trim();
            File.Delete(UpdatedFromFile);
            var to = AppUpdater.InstalledVersion(AppUpdater.InstallationFolder(AppContext.BaseDirectory));
            if (from.Length == 0 || from == to) return;
            ShowNotice("update:done", Loc.Get(K.UpdateDone, ("from", from), ("to", to)),
                [(Loc.Get(K.UpdateNotes), () => _ = OpenInBrowserAsync(new Uri($"https://github.com/nks-hub/textida/releases/tag/v{to}")))]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* no confirmation, nothing else lost */ }
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
            [new DialogButton(Loc.Get(K.UpdateReturn), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)], HelpTopic: "help.updates"));
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
