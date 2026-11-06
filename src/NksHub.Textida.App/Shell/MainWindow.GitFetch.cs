using System.Net.NetworkInformation;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Shell;

/// <summary>What one look of the background fetch did.</summary>
internal enum GitAutoFetch { Off, Offline, NoRepository, Busy, Skipped, Fetched }

/// <summary>
/// The fetch the editor runs by itself every <c>git.autoFetchMinutes</c> minutes, so that the Git item of the status bar shows the commits coming in (↓) and going out (↑) without a pull. It runs only in
/// the editor the user started (the smoke run and the tests never call <see cref="StartGitAutoFetch"/>), never while the machine has no network or another Git command runs, and only for a branch
/// with an upstream (the plugin skips the rest); it changes no file and no branch. New incoming commits are offered for an update once, with a notice.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly CancellationTokenSource _gitFetchStop = new();
    private (string? Upstream, int Behind) _gitIncomingTold;

    /// <summary>Whether the machine has a network connection at all; tests put their own answer here.</summary>
    internal Func<bool> NetworkAvailable { get; set; } = NetworkInterface.GetIsNetworkAvailable;

    /// <summary>Called once when the first window is shown: the first fetch comes after the interval, not at the start.</summary>
    internal void StartGitAutoFetch()
    {
        Closed += (_, _) => _gitFetchStop.Cancel();
        _ = GitAutoFetchLoopAsync();
    }

    private async Task GitAutoFetchLoopAsync()
    {
        try
        {
            while (!_gitFetchStop.IsCancellationRequested)
            {
                // switched off: the setting is read again a minute later, so switching it on needs no restart
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, _app.Workbench.Settings.GetInt("git.autoFetchMinutes"))), _gitFetchStop.Token);
                await AutoFetchOnceAsync();
            }
        }
        catch (OperationCanceledException) { /* the window closed */ }
    }

    /// <summary>One background fetch of the repository the Git commands act on (<see cref="GitRepoPath"/>), unless the setting, the network or a running command says no.</summary>
    internal async Task<GitAutoFetch> AutoFetchOnceAsync()
    {
        if (_app.Workbench.Settings.GetInt("git.autoFetchMinutes") <= 0) return GitAutoFetch.Off;
        if (!NetworkAvailable()) return GitAutoFetch.Offline;
        if (GitRepoPath() is not { } path) return GitAutoFetch.NoRepository;
        if (_gitCommandRunning) return GitAutoFetch.Busy;
        _gitCommandRunning = true;
        System.Text.Json.JsonElement? answer;
        try { answer = await _app.Plugins.RunGitRepoAsync(this, "git.fetch", path, ",\"background\":true", notify: false); }
        finally { _gitCommandRunning = false; }
        if (StatusOf(answer) != "fetched" || answer!.Value.GetProperty("upstream").GetString() is not { } upstream) return GitAutoFetch.Skipped;
        ReloadCommitViews();
        int behind = answer.Value.GetProperty("behind").GetInt32();
        if (behind > 0 && (upstream, behind) != _gitIncomingTold)
            ShowNotice("git-incoming", Loc.Plural(K.GitIncoming, behind, ("upstream", upstream), ("branch", answer.Value.GetProperty("branch").GetString() ?? "")),
                [(Loc.Get(K.GitUpdate), () => _ = PullAsync(path))]);
        _gitIncomingTold = (upstream, behind);
        return GitAutoFetch.Fetched;
    }
}
