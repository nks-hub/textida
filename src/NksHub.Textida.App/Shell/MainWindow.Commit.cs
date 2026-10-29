using System.Text.Json;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The window's side of the Git commands that work on a repository and not on a document: the commit panel (a tab per repository), commit, fetch, pull, push, the branches, the stashes and the
/// history. The Git plugin does the work, asks its questions and tells the user how it went; the window finds the repository, runs one such command at a time and reads the commit panels again
/// afterwards. Nothing here talks to a remote by itself: every command is one the user chose.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly Dictionary<string, DocTab> _commitPanels = new(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private bool _gitCommandRunning;

    /// <summary>The commit panels of this window.</summary>
    internal IEnumerable<CommitView> CommitViews => Tabs.Select(t => t.Custom).OfType<CommitView>();

    private void ApplyCommitTexts()
    {
        foreach (var view in CommitViews) view.ApplyTexts();
    }

    private void ReloadCommitViews()
    {
        foreach (var view in CommitViews) view.RequestReload();
    }

    /// <summary>
    /// The path the Git commands act on: the repository of the commit panel that is shown, else the file of the active tab, else the folder of the file panel; null when none of them is in a repository.
    /// </summary>
    internal string? GitRepoPath()
    {
        if (Active?.Custom is CommitView view) return view.Root;
        if (Active?.Path is { } file && PluginServices.InRepository(file)) return file;
        if (FileRootFolder is { } root && PluginServices.InRepositoryFolder(root)) return root;
        return null;
    }

    private static string Quoted(string text) => $"\"{JsonEncodedText.Encode(text)}\"";

    private static string Flag(bool value) => value ? "true" : "false";

    private static string StatusOf(JsonElement? answer) =>
        answer is { ValueKind: JsonValueKind.Object } a && a.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";

    private Task<JsonElement?> RepoAnswerAsync(string command, string path, string extra = "", bool notify = true) => _app.Plugins.RunGitRepoAsync(this, command, path, extra, notify);

    /// <summary>"Git: Commit" and the Commit button of the toolbar: the commit panel of the repository, with the keyboard in the box of the message.</summary>
    internal CommandOutcome ShowCommit()
    {
        if (GitRepoPath() is not { } path)
        {
            SetStatusMessage(Loc.Get(K.GitNoRepository));
            return CommandOutcome.Handled;
        }
        _ = OpenCommitAsync(path);
        return CommandOutcome.Handled;
    }

    /// <summary>Opens the commit panel of the repository that holds <paramref name="path"/> as a tab, or goes to the one that is open. Null when the path is in no repository.</summary>
    internal async Task<CommitView?> OpenCommitAsync(string path)
    {
        var changes = CommitChanges.Parse(await RepoAnswerAsync("git.changes", path) ?? default);
        if (changes is null)
        {
            SetStatusMessage(Loc.Get(K.GitNoRepository));
            return null;
        }
        string root = changes.Root, key = Path.GetFullPath(root);
        if (_commitPanels.TryGetValue(key, out var open) && open.Group is not null && open.Custom is CommitView shown)
        {
            SelectTab(open);
            shown.RequestReload();
            shown.FocusMessage();
            return shown;
        }
        var view = new CommitView(root, CommitActionsOf(root), () => Options);
        AddCommitTab(view);
        await view.ReloadAsync();
        view.FocusMessage();
        return view;
    }

    /// <summary>Puts a commit panel in a tab of its own; the tab and the view go away together.</summary>
    internal DocTab AddCommitTab(CommitView view)
    {
        string key = Path.GetFullPath(view.Root), name = Path.GetFileName(view.Root.TrimEnd('/', '\\'));
        var od = OpenDocument.NewResults();
        od.TitleOverride = () => Loc.Get(K.CommitTitle, ("repo", name));
        Adopt(od);
        var panel = new DocTab(od, Options, view);
        panel.ViewClosed += () =>
        {
            view.Dispose();
            _commitPanels.Remove(key);
        };
        _commitPanels[key] = panel;
        _activeGroup.Add(panel, null);
        OnActiveTabChanged();
        _app.SessionChanged();
        return panel;
    }

    /// <summary>What the commit panel of <paramref name="root"/> asks of the plugin; the reads say nothing to the user, the commit and the writes let the plugin tell how they went.</summary>
    private CommitViewActions CommitActionsOf(string root) => new(
        Read: async () => CommitChanges.Parse(await RepoAnswerAsync("git.changes", root, "", notify: false) ?? default),
        Stage: async (paths, unstage) => await RepoAnswerAsync(unstage ? "git.unstagePaths" : "git.stagePaths", root, $",\"paths\":[{string.Join(',', paths.Select(Quoted))}]"),
        StageHunk: async (file, unstage, line) =>
            StatusOf(await RepoAnswerAsync("git.stageFileHunk", root, $",\"file\":{Quoted(file)},\"line\":{line},\"unstage\":{Flag(unstage)}")) is "staged" or "unstaged",
        Blob: async (file, head) =>
        {
            var answer = await RepoAnswerAsync("git.blob", root, $",\"file\":{Quoted(file)},\"rev\":\"{(head ? "head" : "index")}\"", notify: false);
            return StatusOf(answer) switch
            {
                "blob" => new CommitBlob(answer!.Value.GetProperty("exists").GetBoolean(), false, answer.Value.GetProperty("bytes").GetBytesFromBase64()),
                "tooLarge" => new CommitBlob(true, true, []),
                _ => null,
            };
        },
        Commit: async (message, amend) =>
        {
            if (_gitCommandRunning)
            {
                SetStatusMessage(Loc.Get(K.GitBusy));
                return false;
            }
            _gitCommandRunning = true;
            try { return StatusOf(await RepoAnswerAsync("git.commit", root, $",\"message\":{Quoted(message)},\"amend\":{Flag(amend)}")) is "committed" or "amended"; }
            finally { _gitCommandRunning = false; }
        },
        Push: () => RunGitRepoCommandAsync("git.push", root));

    /// <summary>
    /// Runs one Git command on the repository (see <see cref="GitRepoPath"/>), or on <paramref name="path"/>. One such command runs at a time; a second one is refused with a message in the status line.
    /// Afterwards the commit panels and the change marks of the open files are read again.
    /// </summary>
    internal async Task RunGitRepoCommandAsync(string command, string? path = null, string extra = "")
    {
        path ??= GitRepoPath();
        if (path is null)
        {
            SetStatusMessage(Loc.Get(K.GitNoRepository));
            return;
        }
        if (_gitCommandRunning)
        {
            SetStatusMessage(Loc.Get(K.GitBusy));
            return;
        }
        _gitCommandRunning = true;
        try { await _app.Plugins.RunGitRepoAsync(this, command, path, extra); }
        finally
        {
            _gitCommandRunning = false;
            ReloadCommitViews();
            foreach (var tab in Tabs) ScheduleGitGutter(tab);
        }
    }

    /// <summary>The command of the menu, the palette or the toolbar: runs the plugin command on the repository.</summary>
    internal CommandOutcome GitCommand(string command, string extra = "")
    {
        _ = RunGitRepoCommandAsync(command, null, extra);
        return CommandOutcome.Handled;
    }

    /// <summary>"Git: Pull": the setting <c>git.pullMode</c> says how the new commits are brought in; <c>ask</c> lets the plugin ask each time.</summary>
    internal CommandOutcome GitPull() =>
        GitCommand("git.pull", _app.Workbench.Settings.GetString("git.pullMode") is "merge" or "rebase" ? $",\"mode\":\"{_app.Workbench.Settings.GetString("git.pullMode")}\"" : "");

    /// <summary>"Git: History of the File": the file of the active tab; a folder in its place is refused by the plugin with a notice.</summary>
    internal CommandOutcome GitFileHistory() => GitCommand("git.logFile");

    /// <summary>A click on the Git item of the status bar: the branch list of the repository.</summary>
    internal void ClickGitStatus() => _ = RunGitRepoCommandAsync("git.switchBranch");
}
