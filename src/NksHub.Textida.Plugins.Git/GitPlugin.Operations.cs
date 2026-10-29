using System.Globalization;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Compare;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugins.Git;

/// <summary>
/// What the built-in Git does besides showing the changes of one file: the commit (what is in the index is committed; the commit panel of the editor ticks files and changes into the index), the
/// commands that talk to a remote, the branches, the stashes and the log. They name the repository by a path (of a file or of a folder in it) and need no open document. Nothing runs by itself:
/// each command is a request of the user, and the ones that use the network show a task that can be cancelled. Passwords are never read here; git asks its credential helper.
/// </summary>
public sealed partial class GitPlugin
{
    public const string ChangesCommand = "git.changes";
    public const string StagePathsCommand = "git.stagePaths";
    public const string UnstagePathsCommand = "git.unstagePaths";
    public const string StageFileHunkCommand = "git.stageFileHunk";
    public const string BlobCommand = "git.blob";
    public const string CommitCommand = "git.commit";
    public const string FetchCommand = "git.fetch";
    public const string PullCommand = "git.pull";
    public const string PushCommand = "git.push";
    public const string SwitchBranchCommand = "git.switchBranch";
    public const string CreateBranchCommand = "git.createBranch";
    public const string DeleteBranchCommand = "git.deleteBranch";
    public const string StashSaveCommand = "git.stashSave";
    public const string StashApplyCommand = "git.stashApply";
    public const string StashDropCommand = "git.stashDrop";
    public const string LogFileCommand = "git.logFile";
    public const string LogRepoCommand = "git.logRepo";

    /// <summary>The panel of the log of a file or of the repository; an item is a commit, and opening it shows what it changed.</summary>
    public const string LogPanel = "git:log";

    private static readonly HashSet<string> RepoCommands =
    [
        ChangesCommand, StagePathsCommand, UnstagePathsCommand, StageFileHunkCommand, BlobCommand, CommitCommand, FetchCommand, PullCommand, PushCommand, SwitchBranchCommand, CreateBranchCommand,
        DeleteBranchCommand, StashSaveCommand, StashApplyCommand, StashDropCommand, LogFileCommand, LogRepoCommand,
    ];

    /// <summary>A command on a repository: <see cref="Path"/> names a file or a folder in it, the other members are in <see cref="P"/>.</summary>
    private sealed record RepoRequest(string Command, string Ui, string? Path, JsonElement P)
    {
        public string? Text(string name) => P.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        public bool Flag(string name) => P.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
        public int? Number(string name) => P.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
    }

    private sealed record LogContext(string Root, string? File);

    /// <summary>What the log panel shows now, so that an item of it can be opened.</summary>
    private volatile LogContext? _log;

    private static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);

    private static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);

    private int PickLimit => _settings.MaxPickItems > 0 ? _settings.MaxPickItems : _settings.MaxEntries;

    private int TextLimit => _settings.MaxTextLength > 0 ? _settings.MaxTextLength : int.MaxValue;

    private string Cut(string text) => text.Length <= TextLimit ? text : text[..Math.Max(0, TextLimit - 1)] + "…";

    private async Task<byte[]> TellRepoAsync(RepoRequest r, string status, string key, NoticeLevel level, CancellationToken ct, params (string, string)[] arguments)
    {
        await NotifyAsync(level, Cut(Texts.Get(r.Ui, key, arguments)), ct);
        return Status(status);
    }

    private async Task<byte[]> RepoCommandAsync(JsonElement p, CancellationToken ct)
    {
        var r = new RepoRequest(p.GetProperty("command").GetString()!, p.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString()! : "en",
            p.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String ? path.GetString() : null, p.Clone());
        _language = r.Ui;
        // the data commands of the commit panel answer without a notice; a command the user ran says what is wrong
        bool silent = r.Command is ChangesCommand or BlobCommand or StagePathsCommand or UnstagePathsCommand or StageFileHunkCommand;
        try
        {
            if (string.IsNullOrEmpty(r.Path)) return silent ? Status("noFile") : await TellRepoAsync(r, "noFile", "noFile", NoticeLevel.Warning, ct);
            GitRepository? repo;
            try { repo = await GitRepository.OpenAsync(r.Path, _settings.TimeoutMs, _settings.MaxBytes, ct); }
            catch (GitException e) { return await TellRepoAsync(r, "noGit", "noGit", NoticeLevel.Error, ct, ("reason", e.Message)); }
            if (repo is null) return silent ? Status("noRepository") : await TellRepoAsync(r, "noRepository", "noRepository", NoticeLevel.Warning, ct);
            var answer = r.Command switch
            {
                ChangesCommand => await ChangesAsync(repo, ct),
                StagePathsCommand => await StagePathsAsync(r, repo, unstage: false, ct),
                UnstagePathsCommand => await StagePathsAsync(r, repo, unstage: true, ct),
                StageFileHunkCommand => await StageFileHunkAsync(r, repo, ct),
                BlobCommand => await BlobAsync(r, repo, ct),
                CommitCommand => await CommitAsync(r, repo, ct),
                FetchCommand => await FetchAsync(r, repo, ct),
                PullCommand => await PullAsync(r, repo, ct),
                PushCommand => await PushAsync(r, repo, ct),
                SwitchBranchCommand => await SwitchBranchAsync(r, repo, ct),
                CreateBranchCommand => await CreateBranchAsync(r, repo, ct),
                DeleteBranchCommand => await DeleteBranchAsync(r, repo, ct),
                StashSaveCommand => await StashSaveAsync(r, repo, ct),
                StashApplyCommand => await StashApplyAsync(r, repo, ct),
                StashDropCommand => await StashDropAsync(r, repo, ct),
                LogFileCommand => await LogAsync(r, repo, file: true, ct),
                _ => await LogAsync(r, repo, file: false, ct),
            };
            if (r.Command is not (ChangesCommand or BlobCommand)) WatchRepo(repo);
            return answer;
        }
        catch (GitException e) { return await TellRepoAsync(r, "failed", "failed", NoticeLevel.Error, ct, ("reason", GitRepository.Scrub(e.Message))); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return await TellRepoAsync(r, "cancelled", "cancelled", NoticeLevel.Info, ct); }
        catch (RpcException e) when (e.Code == DocumentErrorCodes.VirtualQuotaExceeded) { return await TellRepoAsync(r, "quota", "quota", NoticeLevel.Error, ct); }
        catch (RpcException e) when (e.Code is DocumentErrorCodes.UnknownDocument or DocumentErrorCodes.InvalidRange or DocumentErrorCodes.PermissionDenied or DocumentErrorCodes.NotProvider or RpcErrorCodes.InvalidParams)
        {
            return await TellRepoAsync(r, "failed", "failed", NoticeLevel.Error, ct, ("reason", e.Message));
        }
    }

    /// <summary>A path of the request relative to the top of the working tree, as git names it; one that leaves the tree is refused.</summary>
    private static string Relative(GitRepository repo, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new GitException("A path is empty.");
        var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(repo.Root, path));
        var rel = Path.GetRelativePath(repo.Root, full).Replace('\\', '/');
        if (rel == ".." || rel.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(rel)) throw new GitException($"{path} is outside the repository.");
        return rel;
    }

    private static List<string> PathsOf(RepoRequest r, GitRepository repo)
    {
        var list = new List<string>();
        if (r.P.TryGetProperty("paths", out var paths) && paths.ValueKind == JsonValueKind.Array)
            foreach (var item in paths.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String) list.Add(Relative(repo, item.GetString()!));
        return list;
    }

    // ---- the commit panel: the changes, staging and the commit ----

    /// <summary>
    /// What the commit panel shows: the files git lists as changed (the letters of the index and of the tree), the branch, its upstream and how far it is from it (by what was fetched last),
    /// and the messages of the last commits for the box of the message. Nothing is written, and no remote is asked.
    /// </summary>
    private async Task<byte[]> ChangesAsync(GitRepository repo, CancellationToken ct)
    {
        var entries = await repo.StatusAsync(ct);
        string branch = await repo.BranchAsync(ct);
        string? upstream = await repo.UpstreamAsync(ct);
        var (ahead, behind) = upstream is null ? (0, 0) : await repo.AheadBehindAsync(ct);
        bool hasCommits = await repo.HasCommitsAsync(ct);
        var messages = hasCommits && _settings.MessageHistory > 0 ? await repo.RecentMessagesAsync(_settings.MessageHistory, ct) : [];
        var items = string.Join(',', entries.Take(_settings.MaxEntries).Select(e =>
            $"{{\"path\":\"{Quote(e.Path)}\",\"index\":\"{Quote(e.Index.ToString())}\",\"tree\":\"{Quote(e.Tree.ToString())}\",\"from\":{(e.From is null ? "null" : $"\"{Quote(e.From)}\"")},\"conflict\":{Bool(StatusCounts.IsConflict(e))}}}"));
        return Bytes($"{{\"status\":\"changes\",\"root\":\"{Quote(repo.Root)}\",\"branch\":\"{Quote(branch)}\",\"upstream\":{(upstream is null ? "null" : $"\"{Quote(upstream)}\"")},\"ahead\":{ahead},\"behind\":{behind}," +
            $"\"hasCommits\":{Bool(hasCommits)},\"total\":{entries.Count},\"entries\":[{items}],\"messages\":[{string.Join(',', messages.Select(m => $"\"{Quote(m)}\""))}]}}");
    }

    private async Task<byte[]> StagePathsAsync(RepoRequest r, GitRepository repo, bool unstage, CancellationToken ct)
    {
        var paths = PathsOf(r, repo);
        if (unstage) await repo.UnstagePathsAsync(paths, ct); else await repo.StagePathsAsync(paths, ct);
        await RefreshAfterAsync(r.Ui, repo, ct);
        return Bytes($"{{\"status\":\"{(unstage ? "unstaged" : "staged")}\",\"count\":{paths.Count}}}");
    }

    /// <summary>The panel and the status bar are read again after a command changed the repository; a view that cannot be drawn must not break the command.</summary>
    private async Task RefreshAfterAsync(string ui, GitRepository repo, CancellationToken ct)
    {
        try
        {
            var entries = await repo.StatusAsync(ct);
            if (_panelRoot == repo.Root) await SetPanelAsync(ui, repo, entries, reveal: false, ct);
            await ShowStatusBarAsync(ui, repo, entries, ct);
        }
        catch (Exception e) when (e is RpcException or GitException or IOException) { }
    }

    private static DiffSource SourceOf(byte[] bytes, TextCodec codec) => new(new ArraySnapshot(bytes), codec, codec.BomLengthIn(bytes));

    private static TextCodec GuessCodec(byte[] bytes) => EncodingGuesser.Guess(bytes.AsSpan(0, Math.Min(bytes.Length, 1 << 20)), bytes.Length <= 1 << 20).Codec;

    /// <summary>The bytes of <paramref name="target"/> with its range [<paramref name="from"/>, <paramref name="to"/>) replaced by the range of <paramref name="source"/>.</summary>
    private static byte[] Splice(byte[] target, long from, long to, byte[] source, long sourceFrom, long sourceTo) =>
        [.. target.AsSpan(0, (int)from), .. source.AsSpan((int)sourceFrom, (int)(sourceTo - sourceFrom)), .. target.AsSpan((int)to)];

    /// <summary>The position of the hunk whose new side starts at <paramref name="line"/>; -1 when there is none.</summary>
    private static int HunkAt(IReadOnlyList<DiffHunk> hunks, long line)
    {
        for (int i = 0; i < hunks.Count; i++)
            if (hunks[i].BStart == line) return i;
        return -1;
    }

    /// <summary>
    /// Stages one change of a file, or takes one back from the index, without any open document. The change is the one that starts at the line <c>line</c> of the file on disk (for staging, found by
    /// comparing the index with the file) or of the index (for taking back, found by comparing <c>HEAD</c> with the index). The index is written only when it is still what the change was cut from.
    /// </summary>
    private async Task<byte[]> StageFileHunkAsync(RepoRequest r, GitRepository repo, CancellationToken ct)
    {
        var rel = Relative(repo, r.Text("file") ?? "");
        long line = r.Number("line") ?? -1;
        bool unstage = r.Flag("unstage");
        var entry = await repo.IndexEntryAsync(rel, ct);
        if (entry is { Stage: not 0 }) return await TellRepoAsync(r, "unmerged", "unmerged", NoticeLevel.Warning, ct);
        byte[] index = await ReadBlobAsync(repo, entry?.Sha, ct);
        byte[] result;
        if (!unstage)
        {
            var file = Path.Combine(repo.Root, rel);
            if (!File.Exists(file)) return Status("noFile");
            byte[] work = await File.ReadAllBytesAsync(file, ct);
            var codec = GuessCodec(work);
            var diff = LineDiff.Compute(SourceOf(index, codec), SourceOf(work, codec), null, ct);
            int at = HunkAt(diff.Hunks, line);
            if (at < 0) return Status("noHunk");
            var h = diff.Hunks[at];
            result = Splice(index, diff.A.OffsetOf(h.AStart), diff.A.OffsetOf(h.AStart + h.ACount), work, diff.B.OffsetOf(h.BStart), diff.B.OffsetOf(h.BStart + h.BCount));
        }
        else
        {
            var headSha = await repo.HeadBlobAsync(rel, ct);
            byte[] head = await ReadBlobAsync(repo, headSha, ct);
            var codec = GuessCodec(index.Length > 0 ? index : head);
            var diff = LineDiff.Compute(SourceOf(head, codec), SourceOf(index, codec), null, ct);
            int at = HunkAt(diff.Hunks, line);
            if (at < 0) return Status("noHunk");
            var h = diff.Hunks[at];
            result = Splice(index, diff.B.OffsetOf(h.BStart), diff.B.OffsetOf(h.BStart + h.BCount), head, diff.A.OffsetOf(h.AStart), diff.A.OffsetOf(h.AStart + h.ACount));
            if (result.Length == 0 && headSha is null)
            {
                await repo.RemoveFromIndexAsync(rel, ct); // a new file with nothing staged left is not in the index
                await RefreshAfterAsync(r.Ui, repo, ct);
                return Status("unstaged");
            }
        }
        var now = await repo.IndexEntryAsync(rel, ct);
        if ((now?.Sha ?? "") != (entry?.Sha ?? "") || now is { Stage: not 0 }) return await TellRepoAsync(r, "conflict", "conflict", NoticeLevel.Warning, ct, ("file", Path.GetFileName(rel)));
        bool clean = entry is null || await repo.CleansLineEndingsAsync(rel, ct);
        await repo.WriteIndexBlobAsync(rel, entry?.Mode ?? GitRepository.NewFileMode, result, clean, ct);
        await RefreshAfterAsync(r.Ui, repo, ct);
        return Status(unstage ? "unstaged" : "staged");
    }

    /// <summary>A file as <c>HEAD</c> or the index has it, for the comparison of the commit panel: the bytes in base64, or that the file is not there.</summary>
    private async Task<byte[]> BlobAsync(RepoRequest r, GitRepository repo, CancellationToken ct)
    {
        var rel = Relative(repo, r.Text("file") ?? "");
        var bytes = await repo.BlobAtAsync(r.Text("rev") == "head" ? "HEAD" : "", rel, ct);
        if (bytes is null) return Bytes("{\"status\":\"blob\",\"exists\":false,\"bytes\":\"\"}");
        // one answer is one message: a larger file is not sent, the panel says so
        return bytes.Length > _settings.ChunkBytes ? Status("tooLarge") : Bytes($"{{\"status\":\"blob\",\"exists\":true,\"bytes\":\"{Convert.ToBase64String(bytes)}\"}}");
    }

    private async Task<byte[]> CommitAsync(RepoRequest r, GitRepository repo, CancellationToken ct)
    {
        string message = r.Text("message") ?? "";
        bool amend = r.Flag("amend");
        if (!amend && message.Trim().Length == 0) return await TellRepoAsync(r, "noMessage", "commitNoMessage", NoticeLevel.Warning, ct);
        if (amend ? !await repo.HasCommitsAsync(ct) : !await repo.HasStagedChangesAsync(ct)) return await TellRepoAsync(r, "nothingStaged", "commitNothing", NoticeLevel.Warning, ct);
        CommitInfo commit;
        try { commit = await repo.CommitAsync(message, amend, ct); }
        catch (GitException e) when (e.Message.Contains("who you are", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("unable to auto-detect", StringComparison.OrdinalIgnoreCase)
            || e.Message.Contains("empty ident", StringComparison.OrdinalIgnoreCase))
        {
            return await TellRepoAsync(r, "identity", "commitIdentity", NoticeLevel.Error, ct);
        }
        await NotifyAsync(NoticeLevel.Info, Cut(Texts.Get(r.Ui, amend ? "amended" : "committed", ("sha", commit.Short), ("subject", commit.Subject))), ct);
        await RefreshAfterAsync(r.Ui, repo, ct);
        return Bytes($"{{\"status\":\"{(amend ? "amended" : "committed")}\",\"sha\":\"{Quote(commit.Sha)}\",\"short\":\"{Quote(commit.Short)}\",\"subject\":\"{Quote(commit.Subject)}\"}}");
    }

    // ---- tasks that can be cancelled, and the commands that talk to a remote ----

    private TimeSpan NetworkTimeout => TimeSpan.FromMilliseconds(Math.Max(0, _settings.NetworkTimeoutMs));

    private static async Task SafeReportAsync(PluginTask task, double fraction, string message)
    {
        try { await task.ReportAsync(fraction, message); }
        catch (RpcException) { /* the task ended while this report was on its way */ }
    }

    /// <summary>
    /// Runs a command that talks to a remote inside a task the user sees and can cancel. <paramref name="done"/> tells the user how it went when git ended with 0; for any other end
    /// <paramref name="failed"/> may answer instead of the general message (a pull that stopped at conflicts).
    /// </summary>
    private async Task<byte[]> RunNetworkAsync(RepoRequest r, GitRepository repo, string titleKey, (string, string)[] titleArguments, IReadOnlyList<string> args,
        Func<GitResult, Task<byte[]>> done, Func<GitResult, Task<byte[]?>>? failed, CancellationToken ct)
    {
        GitResult result;
        try
        {
            await using var task = await StartTaskAsync(Cut(Texts.Get(r.Ui, titleKey, titleArguments)), ct);
            double last = 0;
            result = await repo.NetworkAsync(args, NetworkTimeout, (fraction, message) =>
            {
                last = fraction ?? last;
                _ = SafeReportAsync(task, last, message);
            }, task.Cancellation);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return await TellRepoAsync(r, "cancelled", "cancelled", NoticeLevel.Info, ct);
        }
        catch (GitException e) when (e.Message.Contains("in time", StringComparison.Ordinal))
        {
            return await TellRepoAsync(r, "timeout", "timeout", NoticeLevel.Error, ct);
        }
        if (result.Ok) return await done(result);
        if (failed is not null && await failed(result) is { } answer) return answer;
        switch (GitRepository.Classify(result))
        {
            case NetworkOutcome.Rejected: return await TellRepoAsync(r, "rejected", "pushRejected", NoticeLevel.Error, ct);
            case NetworkOutcome.AuthFailed: return await TellRepoAsync(r, "authFailed", "authFailed", NoticeLevel.Error, ct);
            default:
                var reason = result.Error.Length > 600 ? result.Error[^600..] : result.Error;
                return await TellRepoAsync(r, "failed", "networkFailed", NoticeLevel.Error, ct, ("reason", reason.Length > 0 ? reason : $"git {result.Code}"));
        }
    }

    private static async Task<string?> CurrentBranchAsync(GitRepository repo, CancellationToken ct) =>
        (await repo.BranchesAsync(ct)).FirstOrDefault(b => b is { Current: true, Remote: false })?.Name;

    private async Task<byte[]> FetchAsync(RepoRequest r, GitRepository repo, CancellationToken ct)
    {
        var remotes = await repo.RemotesAsync(ct);
        if (remotes.Count == 0) return await TellRepoAsync(r, "noRemote", "noRemote", NoticeLevel.Warning, ct);
        string remote = (await repo.UpstreamAsync(ct))?.Split('/')[0] is { } known && remotes.Contains(known) ? known : remotes.Contains("origin") ? "origin" : remotes[0];
        return await RunNetworkAsync(r, repo, "taskFetch", [("remote", remote)], ["fetch", "--progress", "--prune", remote], async _ =>
        {
            await NotifyAsync(NoticeLevel.Info, Cut(Texts.Get(r.Ui, "fetchDone", ("remote", remote))), ct);
            await RefreshAfterAsync(r.Ui, repo, ct);
            return Status("fetched");
        }, null, ct);
    }

    /// <summary>Brings the commits of the upstream into the current branch (git pull). How: the request says merge or rebase, or the user is asked. A stop at conflicts leaves them for the status panel.</summary>
    private async Task<byte[]> PullAsync(RepoRequest r, GitRepository repo, CancellationToken ct)
    {
        string? branch = await CurrentBranchAsync(repo, ct);
        if (branch is null) return await TellRepoAsync(r, "noBranch", "noBranch", NoticeLevel.Warning, ct);
        if (await repo.UpstreamAsync(ct) is null) return await TellRepoAsync(r, "noUpstream", "pullNoUpstream", NoticeLevel.Warning, ct, ("branch", branch));
        string? mode = r.Text("mode") is "merge" or "rebase" ? r.Text("mode") : null;
        if (mode is null)
        {
            var picked = await CallAsync(UiMethods.QuickPick,
                $"{{\"title\":\"{Quote(Texts.Get(r.Ui, "pullModeTitle"))}\",\"placeholder\":\"{Quote(Texts.Get(r.Ui, "pullModePlaceholder"))}\",\"items\":[" +
                $"{{\"id\":\"merge\",\"label\":\"{Quote(Texts.Get(r.Ui, "pullModeMerge"))}\",\"detail\":\"{Quote(Texts.Get(r.Ui, "pullModeMergeDetail"))}\"}}," +
                $"{{\"id\":\"rebase\",\"label\":\"{Quote(Texts.Get(r.Ui, "pullModeRebase"))}\",\"detail\":\"{Quote(Texts.Get(r.Ui, "pullModeRebaseDetail"))}\"}}]}}", ct);
            mode = picked.GetProperty("id").ValueKind == JsonValueKind.String ? picked.GetProperty("id").GetString() : null;
            if (mode is null) return Status("dismissed");
        }
        List<string> args = ["pull", "--progress", "--no-edit", .. mode == "rebase" ? (string[])["--rebase", "--autostash"] : ["--no-rebase"]];
        return await RunNetworkAsync(r, repo, "taskPull", [("branch", branch)], args, async result =>
        {
            await RefreshAfterAsync(r.Ui, repo, ct);
            bool same = result.Output.Contains("Already up to date", StringComparison.OrdinalIgnoreCase) || result.Output.Contains("Already up-to-date", StringComparison.OrdinalIgnoreCase);
            await NotifyAsync(NoticeLevel.Info, Cut(Texts.Get(r.Ui, same ? "pullUpToDate" : "pullDone", ("branch", branch))), ct);
            return Status(same ? "upToDate" : "pulled");
        }, async _ =>
        {
            var entries = await repo.StatusAsync(ct);
            int conflicts = entries.Count(StatusCounts.IsConflict);
            if (conflicts == 0) return null;
            await SetPanelAsync(r.Ui, repo, entries, reveal: true, ct);
            _panelRoot = repo.Root;
            await ShowStatusBarAsync(r.Ui, repo, entries, ct);
            await NotifyAsync(NoticeLevel.Warning, Cut(Texts.Get(r.Ui, mode == "rebase" ? "pullRebaseStopped" : "pullConflicts", ("count", Num(conflicts)))), ct);
            return Bytes($"{{\"status\":\"conflicts\",\"count\":{conflicts}}}");
        }, ct);
    }

    /// <summary>
    /// Sends the current branch to its upstream (or, for a branch without one, to the remote, which then tracks it). Before it does, the user is shown the commits that go out, with the
    /// branches that are ahead of theirs; a rejection by the remote (it has commits the branch has not) is told as such.
    /// </summary>
    private async Task<byte[]> PushAsync(RepoRequest r, GitRepository repo, CancellationToken ct)
    {
        var remotes = await repo.RemotesAsync(ct);
        if (remotes.Count == 0) return await TellRepoAsync(r, "noRemote", "noRemote", NoticeLevel.Warning, ct);
        if (!await repo.HasCommitsAsync(ct)) return await TellRepoAsync(r, "empty", "logNone", NoticeLevel.Info, ct);
        string? branch = await CurrentBranchAsync(repo, ct);
        if (branch is null) return await TellRepoAsync(r, "noBranch", "noBranch", NoticeLevel.Warning, ct);
        string? upstream = await repo.UpstreamAsync(ct);
        if (upstream is not null && (await repo.AheadBehindAsync(ct)).Ahead == 0) return await TellRepoAsync(r, "nothingToPush", "pushNothing", NoticeLevel.Info, ct, ("branch", branch), ("upstream", upstream));

        string remote = upstream?.Split('/')[0] ?? (remotes.Contains("origin") ? "origin" : remotes[0]);
        if (upstream is null && remotes.Count > 1 && !remotes.Contains("origin"))
        {
            var picked = await PickAsync(Texts.Get(r.Ui, "pushConfirmTitle", ("branch", branch)), "", remotes.Select(x => (x, x, (string?)null)), ct);
            if (picked is null) return Status("dismissed");
            remote = picked;
        }
        string target = upstream ?? $"{remote}/{branch}";
        if (!r.Flag("confirmed"))
        {
            const int Listed = 10;
            var outgoing = await repo.OutgoingAsync(branch, Listed, ct);
            var text = new StringBuilder();
            text.AppendLine(Texts.Get(r.Ui, "pushTarget", ("target", upstream is null ? $"{target} ({Texts.Get(r.Ui, "pushNewBranch", ("remote", remote))})" : target)));
            var own = outgoing.FirstOrDefault(g => g.Branch == branch).Commits ?? [];
            foreach (var commit in own) text.Append("  ").Append(commit.Short).Append(' ').AppendLine(commit.Subject);
            if (own.Count == Listed) text.AppendLine("  …");
            var also = outgoing.Where(g => g.Branch != branch && g.Commits.Count > 0).Select(g => g.Branch).ToList();
            if (also.Count > 0) text.AppendLine().Append(Texts.Get(r.Ui, "pushAlsoAhead", ("names", string.Join(", ", also)), ("branch", branch)));
            if (!await ConfirmAsync(Texts.Get(r.Ui, "pushConfirmTitle", ("branch", branch)), text.ToString().TrimEnd(), ct)) return Status("dismissed");
        }
        List<string> args = ["push", "--progress", .. upstream is null ? (string[])["--set-upstream", remote, branch] : []];
        return await RunNetworkAsync(r, repo, "taskPush", [("branch", branch)], args, async _ =>
        {
            await NotifyAsync(NoticeLevel.Info, Cut(Texts.Get(r.Ui, "pushDone", ("branch", branch), ("target", target))), ct);
            await RefreshAfterAsync(r.Ui, repo, ct);
            return Status("pushed");
        }, null, ct);
    }

    // ---- questions the commands ask ----

    private async Task<string?> PickAsync(string title, string placeholder, IEnumerable<(string Id, string Label, string? Detail)> items, CancellationToken ct)
    {
        var list = string.Join(',', items.Take(PickLimit).Select(i =>
            $"{{\"id\":\"{Quote(i.Id)}\",\"label\":\"{Quote(Cut(i.Label))}\"" + (i.Detail is null ? "}" : $",\"detail\":\"{Quote(Cut(i.Detail))}\"}}")));
        var picked = await CallAsync(UiMethods.QuickPick, $"{{\"title\":\"{Quote(Cut(title))}\",\"placeholder\":\"{Quote(Cut(placeholder))}\",\"items\":[{list}]}}", ct);
        return picked.GetProperty("id").ValueKind == JsonValueKind.String ? picked.GetProperty("id").GetString() : null;
    }

    private async Task<bool> ConfirmAsync(string title, string message, CancellationToken ct) =>
        (await CallAsync(UiMethods.Confirm, $"{{\"title\":\"{Quote(Cut(title))}\",\"message\":\"{Quote(Cut(message))}\"}}", ct)).GetProperty("confirmed").ValueKind == JsonValueKind.True;

    private async Task<string?> AskAsync(string title, string label, CancellationToken ct)
    {
        var answer = await CallAsync(UiMethods.Input, $"{{\"title\":\"{Quote(Cut(title))}\",\"label\":\"{Quote(Cut(label))}\",\"value\":\"\"}}", ct);
        return answer.GetProperty("value").ValueKind == JsonValueKind.String ? answer.GetProperty("value").GetString() : null;
    }

    // ---- branches ----

    private async Task<byte[]> SwitchBranchAsync(RepoRequest r, GitRepository repo, CancellationToken ct)
    {
        var branches = await repo.BranchesAsync(ct);
        string? name = r.Text("branch");
        if (name is null)
        {
            var others = branches.Where(b => !b.Current).ToList();
            if (others.Count == 0) return await TellRepoAsync(r, "noBranches", "branchNone", NoticeLevel.Info, ct);
            name = await PickAsync(Texts.Get(r.Ui, "branchPickTitle"), Texts.Get(r.Ui, "branchPickPlaceholder"),
                others.Select(b => (b.Name, b.Name, (string?)(b.Remote ? Texts.Get(r.Ui, "branchRemote") : b.Subject))), ct);
            if (name is null) return Status("dismissed");
        }
        var target = branches.FirstOrDefault(b => b.Name == name) ?? throw new GitException($"There is no branch {name}.");
        var dirty = (await repo.StatusAsync(ct)).Count(e => e.Index != '?');
        if (dirty > 0 && !r.Flag("confirmed") && !await ConfirmAsync(Texts.Get(r.Ui, "dirtyTitle"), Texts.Get(r.Ui, "dirtyMessage", ("count", Num(dirty))), ct)) return Status("dismissed");
        await repo.SwitchAsync(target, ct); // git itself refuses where the changes would be overwritten, and says why
        await NotifyAsync(NoticeLevel.Info, Cut(Texts.Get(r.Ui, "branchSwitched", ("branch", target.Remote ? target.Name[(target.Name.IndexOf('/') + 1)..] : target.Name))), ct);
        await RefreshAfterAsync(r.Ui, repo, ct);
        return Bytes($"{{\"status\":\"switched\",\"branch\":\"{Quote(target.Name)}\"}}");
    }

    private async Task<byte[]> CreateBranchAsync(RepoRequest r, GitRepository repo, CancellationToken ct)
    {
        string? name = r.Text("branch") ?? await AskAsync(Texts.Get(r.Ui, "newBranchTitle"), Texts.Get(r.Ui, "newBranchLabel"), ct);
        if (string.IsNullOrWhiteSpace(name)) return Status("dismissed");
        name = name.Trim();
        await repo.CreateBranchAsync(name, ct);
        await NotifyAsync(NoticeLevel.Info, Cut(Texts.Get(r.Ui, "branchCreated", ("branch", name))), ct);
        await RefreshAfterAsync(r.Ui, repo, ct);
        return Bytes($"{{\"status\":\"created\",\"branch\":\"{Quote(name)}\"}}");
    }

    private async Task<byte[]> DeleteBranchAsync(RepoRequest r, GitRepository repo, CancellationToken ct)
    {
        string? name = r.Text("branch");
        if (name is null)
        {
            var locals = (await repo.BranchesAsync(ct)).Where(b => !b.Remote && !b.Current).ToList();
            if (locals.Count == 0) return await TellRepoAsync(r, "noBranches", "branchNone", NoticeLevel.Info, ct);
            name = await PickAsync(Texts.Get(r.Ui, "deletePickTitle"), Texts.Get(r.Ui, "branchPickPlaceholder"), locals.Select(b => (b.Name, b.Name, (string?)b.Subject)), ct);
            if (name is null) return Status("dismissed");
        }
        try { await repo.DeleteBranchAsync(name, force: false, ct); }
        catch (GitException e) when (e.Message.Contains("not fully merged", StringComparison.OrdinalIgnoreCase))
        {
            if (!await ConfirmAsync(Texts.Get(r.Ui, "deleteUnmergedTitle"), Texts.Get(r.Ui, "deleteUnmergedMessage", ("branch", name)), ct)) return Status("dismissed");
            await repo.DeleteBranchAsync(name, force: true, ct);
        }
        await NotifyAsync(NoticeLevel.Info, Cut(Texts.Get(r.Ui, "branchDeleted", ("branch", name))), ct);
        return Bytes($"{{\"status\":\"deleted\",\"branch\":\"{Quote(name)}\"}}");
    }

    // ---- stash ----

    private async Task<byte[]> StashSaveAsync(RepoRequest r, GitRepository repo, CancellationToken ct)
    {
        if ((await repo.StatusAsync(ct)).Count == 0) return await TellRepoAsync(r, "nothingToStash", "stashNothing", NoticeLevel.Info, ct);
        string? message = r.Text("message") ?? await AskAsync(Texts.Get(r.Ui, "stashTitle"), Texts.Get(r.Ui, "stashLabel"), ct);
        if (message is null) return Status("dismissed");
        if (!await repo.StashPushAsync(message, ct)) return await TellRepoAsync(r, "nothingToStash", "stashNothing", NoticeLevel.Info, ct);
        await NotifyAsync(NoticeLevel.Info, Cut(Texts.Get(r.Ui, "stashSaved")), ct);
        await RefreshAfterAsync(r.Ui, repo, ct);
        return Status("stashed");
    }

    private async Task<StashInfo?> PickStashAsync(RepoRequest r, GitRepository repo, string titleKey, CancellationToken ct)
    {
        var stashes = await repo.StashesAsync(ct);
        if (stashes.Count == 0)
        {
            await NotifyAsync(NoticeLevel.Info, Cut(Texts.Get(r.Ui, "stashNone")), ct);
            return null;
        }
        string? name = r.Text("stash") ?? await PickAsync(Texts.Get(r.Ui, titleKey), Texts.Get(r.Ui, "stashPickPlaceholder"), stashes.Select(s => (s.Name, s.Name, (string?)s.Description)), ct);
        return stashes.FirstOrDefault(s => s.Name == name);
    }

    private async Task<byte[]> StashApplyAsync(RepoRequest r, GitRepository repo, CancellationToken ct)
    {
        if (await PickStashAsync(r, repo, "stashApplyTitle", ct) is not { } stash) return Status("dismissed");
        var result = await repo.StashApplyAsync(stash.Name, ct);
        var entries = await repo.StatusAsync(ct);
        bool conflict = entries.Any(StatusCounts.IsConflict);
        if (!result.Ok && !conflict) throw new GitException(result.Error.Length > 0 ? result.Error : "git stash apply failed.");
        if (conflict)
        {
            await SetPanelAsync(r.Ui, repo, entries, reveal: true, ct);
            _panelRoot = repo.Root;
        }
        await ShowStatusBarAsync(r.Ui, repo, entries, ct);
        await NotifyAsync(conflict ? NoticeLevel.Warning : NoticeLevel.Info, Cut(Texts.Get(r.Ui, conflict ? "stashConflict" : "stashApplied", ("name", stash.Name))), ct);
        return Status(conflict ? "conflicts" : "applied");
    }

    private async Task<byte[]> StashDropAsync(RepoRequest r, GitRepository repo, CancellationToken ct)
    {
        if (await PickStashAsync(r, repo, "stashDropTitle", ct) is not { } stash) return Status("dismissed");
        if (!r.Flag("confirmed") && !await ConfirmAsync(Texts.Get(r.Ui, "stashDropTitle"), Texts.Get(r.Ui, "stashDropConfirmMessage", ("name", stash.Name), ("description", stash.Description)), ct)) return Status("dismissed");
        await repo.StashDropAsync(stash.Name, ct);
        await NotifyAsync(NoticeLevel.Info, Cut(Texts.Get(r.Ui, "stashDropped", ("name", stash.Name))), ct);
        return Status("dropped");
    }

    // ---- log ----

    /// <summary>Shows the log of the file or of the repository in a panel; opening a commit there shows what it changed.</summary>
    private async Task<byte[]> LogAsync(RepoRequest r, GitRepository repo, bool file, CancellationToken ct)
    {
        if (file && Directory.Exists(r.Path)) return await TellRepoAsync(r, "noFile", "noFile", NoticeLevel.Warning, ct);
        string? rel = file ? Relative(repo, r.Path!) : null;
        var commits = await repo.LogAsync(rel, _settings.LogEntries > 0 ? _settings.LogEntries : _settings.MaxEntries, ct);
        if (commits.Count == 0) return await TellRepoAsync(r, "empty", "logNone", NoticeLevel.Info, ct);
        string name = file ? Path.GetFileName(rel!) : Path.GetFileName(repo.Root.TrimEnd('/', '\\'));
        _log = new LogContext(repo.Root, rel);
        var items = string.Join(',', commits.Select(c =>
            $"{{\"id\":\"{Quote(c.Sha)}\",\"label\":\"{Quote(Cut(c.Short + " " + c.Subject))}\",\"detail\":\"{Quote(Cut(c.Author + ", " + DateTimeOffset.FromUnixTimeSeconds(c.Time).LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)))}\"}}"));
        await CallAsync(UiMethods.PanelSet, $"{{\"panel\":\"{LogPanel}\",\"title\":\"{Quote(Cut(Texts.Get(r.Ui, file ? "logPanelFile" : "logPanelRepo", ("name", name))))}\",\"kind\":\"list\",\"items\":[{items}],\"reveal\":true}}", ct);
        return Bytes($"{{\"status\":\"log\",\"count\":{commits.Count}}}");
    }

    /// <summary>The user opened a commit of the log: its change of the file (log of a file), or of a file of the commit chosen from a list (log of the repository), in a comparison.</summary>
    private async Task OpenCommitAsync(string sha, CancellationToken ct)
    {
        if (_log is not { } log) return;
        var ui = _language;
        var repo = await GitRepository.OpenAsync(log.Root, _settings.TimeoutMs, _settings.MaxBytes, ct) ?? throw new GitException("The repository is gone.");
        string shortSha = sha.Length > 8 ? sha[..8] : sha;
        string? file = log.File;
        if (file is null)
        {
            var files = await repo.CommitFilesAsync(sha, ct);
            if (files.Count == 0)
            {
                await NotifyAsync(NoticeLevel.Info, Cut(Texts.Get(ui, "logNoFile", ("commit", shortSha))), ct);
                return;
            }
            file = files.Count == 1 ? files[0].Path : await PickAsync(Texts.Get(ui, "logFilesTitle", ("commit", shortSha)), Texts.Get(ui, "logFilesPlaceholder"), files.Select(f => (f.Path, f.Path, (string?)f.Status.ToString())), ct);
            if (file is null) return;
        }
        var after = await repo.BlobAtAsync(sha, file, ct);
        bool hasParent = (await repo.ExecAsync(["rev-parse", "--verify", "-q", sha + "^1"], true, ct)).Ok;
        var before = hasParent ? await repo.BlobAtAsync(sha + "^1", file, ct) : null;
        string name = Path.GetFileName(file);
        var codec = GuessCodec(after ?? before ?? []).Name;
        string leftTitle = Texts.Get(ui, "logBefore", ("file", name), ("commit", shortSha)), rightTitle = Texts.Get(ui, "logAfter", ("file", name), ("commit", shortSha));
        var (left, leftRevision) = await EnsureAsync($"git-log:{log.Root}:{sha}:{file}:before", name, leftTitle, codec, before, "b" + sha, ct);
        var (right, rightRevision) = await EnsureAsync($"git-log:{log.Root}:{sha}:{file}:after", name, rightTitle, codec, after, "a" + sha, ct);
        await CallAsync(DocumentMethods.DiffOpen,
            $"{{\"left\":{{\"document\":\"{left}\",\"revision\":{leftRevision}}},\"right\":{{\"document\":\"{right}\",\"revision\":{rightRevision}}}," +
            $"\"title\":\"{Quote(rightTitle)}\",\"leftTitle\":\"{Quote(leftTitle)}\",\"rightTitle\":\"{Quote(rightTitle)}\",\"key\":\"{Quote($"git-log-diff:{log.Root}:{sha}:{file}")}\"}}", ct);
    }

    /// <summary>The repository of a command that changes it is watched like that of an open file, so the panel and the status bar follow what other programs do to it afterwards.</summary>
    private void WatchRepo(GitRepository repo)
    {
        if (_settings.WatchDebounceMs <= 0) return;
        lock (_watchGate)
        {
            if (_watched.ContainsKey(repo.Root)) return;
            try { _watched[repo.Root] = new Watched(new RepoWatcher(repo.Root, repo.GitDirectory, _settings.WatchDebounceMs, () => _ = RefreshAsync(repo))); }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException) { }
        }
    }
}
