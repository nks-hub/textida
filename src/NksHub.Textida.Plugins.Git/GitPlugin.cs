using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Files;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugins.Git;

/// <summary>Limits of the plugin; the editor passes them on the command line, so no number is fixed in the plugin.</summary>
/// <param name="TimeoutMs">Time after which a git command is ended.</param>
/// <param name="MaxBytes">Largest blob read from the repository; the editor refuses a virtual document larger than its own limit.</param>
/// <param name="MaxEntries">Files listed in the status panel.</param>
/// <param name="ChunkBytes">Bytes read from a document per request; must not exceed what the document API returns in one read.</param>
/// <param name="WatchDebounceMs">Quiet time after a change in the repository before the status is read again; 0 does not watch the repository.</param>
/// <param name="MaxTreeEntries">Entries <see cref="GitPlugin.TreeStatusCommand"/> answers with; 0 takes <paramref name="MaxEntries"/>.</param>
public sealed record GitSettings(int TimeoutMs, long MaxBytes, int MaxEntries, int ChunkBytes, int WatchDebounceMs = 0, int MaxTreeEntries = 0);

/// <summary>
/// The built-in Git: the status of the working tree in a panel (read again when files change outside the editor, in every repository that has an open file), the file against the index or against <c>HEAD</c> in a comparison tab,
/// the changes of a file for the gutter, and staging the change under the caret or at an offset the editor names (a click in the gutter or in the comparison tab).
/// The index and <c>HEAD</c> versions of a file are virtual documents the plugin provides; staging goes through <c>diff/stage</c> into the index document and is then written to the
/// repository, but only when the index is still what the plugin last read. Otherwise nothing is written, the index document is read again and the answer is <c>conflict</c>.
/// </summary>
public sealed class GitPlugin : PluginBase
{
    public const string StatusCommand = "git.status";
    public const string DiffIndexCommand = "git.diffIndex";
    public const string DiffHeadCommand = "git.diffHead";
    public const string StageHunkCommand = "git.stageHunk";
    public const string UnstageHunkCommand = "git.unstageHunk";
    public const string ConflictOursCommand = "git.conflictOurs";
    public const string ConflictTheirsCommand = "git.conflictTheirs";
    public const string ConflictBothCommand = "git.conflictBoth";
    public const string MarkResolvedCommand = "git.markResolved";
    public const string ConflictDeleteCommand = "git.conflictDelete";
    public const string HunksCommand = "git.hunks";
    /// <summary>The editor sends it when the last tab of a document closed; nothing else is asked or done.</summary>
    public const string ClosedCommand = "git.closed";
    public const string StatusPanel = "git:status";
    /// <summary>Asked for a folder, not a document: the state of the files below it, for the file tree. Nothing is shown to the user and nothing is watched.</summary>
    public const string TreeStatusCommand = "git.treeStatus";

    private readonly GitSettings _settings;

    /// <summary>The blob each virtual document (by key) was filled from; empty for a path that is not in the tree of that revision.</summary>
    private readonly ConcurrentDictionary<string, (string Document, string Sha)> _loaded = new();

    /// <summary>The watch of one repository and the open documents that keep it.</summary>
    private sealed class Watched(RepoWatcher watcher)
    {
        public readonly RepoWatcher Watcher = watcher;
        public readonly HashSet<Guid> Documents = [];
    }

    private readonly object _watchGate = new();
    private readonly SemaphoreSlim _refreshing = new(1, 1);
    private readonly Dictionary<string, Watched> _watched = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> _documentRoots = [];
    /// <summary>The root of the repository whose status the panel shows; the panel is kept up to date only for it.</summary>
    private volatile string? _panelRoot;
    private volatile string _language = "en";

    public GitPlugin(GitSettings settings) : base("git", apiVersion: 1)
    {
        _settings = settings;
        On("command/execute", ExecuteAsync);
    }

    private async Task<JsonElement> CallAsync(string method, string paramsJson, CancellationToken ct) =>
        (await Rpc.RequestAsync(method, Encoding.UTF8.GetBytes(paramsJson), null, ct)).Clone();

    private static string Quote(string text) => JsonEncodedText.Encode(text).ToString();

    private async Task NotifyAsync(NoticeLevel level, string message, CancellationToken ct) =>
        await CallAsync(UiMethods.Notify, $"{{\"level\":\"{level.ToString().ToLowerInvariant()}\",\"message\":\"{Quote(message)}\"}}", ct);

    private static byte[] Status(string status) => Encoding.UTF8.GetBytes($"{{\"status\":\"{status}\"}}");

    /// <param name="Offset">A byte offset of the document the command acts at instead of the caret; the editor sends it for a click in the gutter or in a comparison tab.</param>
    private sealed record Request(string Command, string Document, long Revision, string Ui, string? Path, long? Offset);

    private async ValueTask<byte[]?> ExecuteAsync(JsonElement p, CancellationToken ct)
    {
        if (p.TryGetProperty("command", out var first) && first.GetString() == TreeStatusCommand) return await TreeStatusAsync(p, ct);
        var r = new Request(p.GetProperty("command").GetString()!, p.GetProperty("document").GetString()!, p.GetProperty("revision").GetInt64(),
            p.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString()! : "en",
            p.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String ? path.GetString() : null,
            p.TryGetProperty("offset", out var offset) && offset.ValueKind == JsonValueKind.Number ? offset.GetInt64() : null);
        if (r.Command is not (StatusCommand or DiffIndexCommand or DiffHeadCommand or StageHunkCommand or UnstageHunkCommand or ConflictOursCommand or ConflictTheirsCommand or ConflictBothCommand
            or MarkResolvedCommand or ConflictDeleteCommand or HunksCommand or ClosedCommand))
            throw new RpcException(RpcErrorCodes.InvalidParams, "Unknown command.");
        if (r.Command == ClosedCommand)
        {
            if (Guid.TryParse(r.Document, out var closed)) Release(closed);
            return Status("closed");
        }
        _language = r.Ui;
        if (r.Command == HunksCommand) return await MarksAsync(r, ct);
        try
        {
            if (string.IsNullOrEmpty(r.Path)) return await TellAsync(r, "noFile", NoticeLevel.Warning, ct);
            GitRepository? repo;
            try { repo = await GitRepository.OpenAsync(r.Path, _settings.TimeoutMs, _settings.MaxBytes, ct); }
            catch (GitException e) { return await TellAsync(r, "noGit", NoticeLevel.Error, ct, ("reason", e.Message)); }
            if (repo is null) return await TellAsync(r, "noRepository", NoticeLevel.Warning, ct);
            var file = Path.GetFileName(r.Path);
            var answer = r.Command switch
            {
                StatusCommand => await ShowStatusAsync(r, repo, ct),
                StageHunkCommand => await StageHunkAsync(r, repo, file, unstage: false, ct),
                UnstageHunkCommand => await StageHunkAsync(r, repo, file, unstage: true, ct),
                ConflictOursCommand => await ResolveBlockAsync(r, repo, file, "ours", ct),
                ConflictTheirsCommand => await ResolveBlockAsync(r, repo, file, "theirs", ct),
                ConflictBothCommand => await ResolveBlockAsync(r, repo, file, "both", ct),
                MarkResolvedCommand => await MarkResolvedAsync(r, repo, file, ct),
                ConflictDeleteCommand => await DeleteConflictAsync(r, repo, file, ct),
                _ => await OpenDiffAsync(r, repo, file, r.Command == DiffHeadCommand, ct),
            };
            if (r.Command != StatusCommand) await ShowStatusBarAsync(r.Ui, repo, null, ct); // the status command has shown it
            Watch(r.Document, repo);
            return answer;
        }
        catch (RpcException e) when (e.Code == DocumentErrorCodes.RevisionConflict) { return await TellAsync(r, "stale", NoticeLevel.Warning, ct); }
        catch (RpcException e) when (e.Code == DocumentErrorCodes.VirtualQuotaExceeded) { return await TellAsync(r, "quota", NoticeLevel.Error, ct); }
        catch (RpcException e) when (e.Code is DocumentErrorCodes.UnknownDocument or DocumentErrorCodes.InvalidRange or DocumentErrorCodes.PermissionDenied or DocumentErrorCodes.NotProvider or RpcErrorCodes.InvalidParams)
        {
            return await TellAsync(r, "failed", NoticeLevel.Error, ct, ("reason", e.Message));
        }
        catch (GitException e) { return await TellAsync(r, "failed", NoticeLevel.Error, ct, ("reason", e.Message)); }
    }

    private async Task<byte[]> TellAsync(Request r, string key, NoticeLevel level, CancellationToken ct, params (string, string)[] arguments)
    {
        await NotifyAsync(level, Texts.Get(r.Ui, key, arguments), ct);
        return Status(key);
    }

    // ---- status ----

    private async Task<byte[]> ShowStatusAsync(Request r, GitRepository repo, CancellationToken ct)
    {
        var entries = await repo.StatusAsync(ct);
        var shown = await SetPanelAsync(r.Ui, repo, entries, reveal: true, ct);
        _panelRoot = repo.Root;
        if (entries.Count == 0) await NotifyAsync(NoticeLevel.Info, Texts.Get(r.Ui, "clean"), ct);
        else if (entries.Count > shown) await NotifyAsync(NoticeLevel.Warning, Texts.Get(r.Ui, "truncated", ("max", _settings.MaxEntries.ToString())), ct);
        await ShowStatusBarAsync(r.Ui, repo, entries, ct);
        return Encoding.UTF8.GetBytes($"{{\"status\":\"status\",\"count\":{entries.Count},\"shown\":{shown}}}");
    }

    /// <summary>Fills the status panel with the first entries; returns how many it shows.</summary>
    private async Task<int> SetPanelAsync(string ui, GitRepository repo, IReadOnlyList<StatusEntry> entries, bool reveal, CancellationToken ct)
    {
        var shown = entries.Take(_settings.MaxEntries).ToList();
        var items = string.Join(',', shown.Select(e =>
            $"{{\"id\":\"{Quote(Path.GetFullPath(Path.Combine(repo.Root, e.Path)))}\",\"label\":\"{Quote(e.Path)}\",\"detail\":\"{Quote(Texts.Describe(ui, e))}\"}}"));
        var title = "Git: " + Path.GetFileName(repo.Root.TrimEnd('/', '\\'));
        await CallAsync(UiMethods.PanelSet, $"{{\"panel\":\"{StatusPanel}\",\"title\":\"{Quote(title)}\",\"kind\":\"list\",\"items\":[{items}],\"reveal\":{(reveal ? "true" : "false")}}}", ct);
        return shown.Count;
    }

    /// <summary>The status bar item: the branch and how many files are staged, changed, untracked and in conflict. A status that cannot be shown must not break the command.</summary>
    private async Task ShowStatusBarAsync(string ui, GitRepository repo, IReadOnlyList<StatusEntry>? entries, CancellationToken ct)
    {
        try
        {
            var c = StatusCounts.Of(entries ?? await repo.StatusAsync(ct));
            var branch = await repo.BranchAsync(ct);
            var parts = new List<string> { branch.Length > 0 ? branch : "Git" };
            if (c.Staged > 0) parts.Add("+" + c.Staged);
            if (c.Changed > 0) parts.Add("~" + c.Changed);
            if (c.Untracked > 0) parts.Add("?" + c.Untracked);
            if (c.Conflicts > 0) parts.Add("!" + c.Conflicts);
            var tip = Texts.Get(ui, "statusTip", ("branch", branch), ("staged", c.Staged.ToString()), ("changed", c.Changed.ToString()), ("untracked", c.Untracked.ToString()), ("conflicts", c.Conflicts.ToString()));
            await CallAsync(UiMethods.StatusSet, $"{{\"text\":\"{Quote(string.Join(' ', parts))}\",\"tooltip\":\"{Quote(tip)}\"}}", ct);
        }
        catch (Exception e) when (e is RpcException or GitException or IOException) { }
    }

    // ---- state of the files of a folder, for the file tree ----

    /// <summary>
    /// The state of the files below the folder <c>path</c>: <c>{"status":"tree","count":N,"entries":[["M","dir/a.txt"],["I","bin/"]]}</c>, a letter of <see cref="GitFileStatus.Code"/> and a path relative to
    /// that folder; a path with a closing slash is a folder Git names whole. The heaviest states come first when there are more than the limit. A folder outside a repository, and a failure, are an answer
    /// without entries: the tree asks in the background and says nothing to the user.
    /// </summary>
    private async Task<byte[]> TreeStatusAsync(JsonElement p, CancellationToken ct)
    {
        var folder = p.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String ? path.GetString() : null;
        if (string.IsNullOrEmpty(folder)) return Status("noFile");
        try
        {
            if (await GitRepository.OpenAsync(folder, _settings.TimeoutMs, _settings.MaxBytes, ct) is not { } repo) return Status("noRepository");
            var shown = TreeStatus.Of(await repo.TreeStatusAsync(ct), repo.Prefix);
            int limit = _settings.MaxTreeEntries > 0 ? _settings.MaxTreeEntries : _settings.MaxEntries;
            if (shown.Count > limit) shown = [.. shown.OrderByDescending(e => (int)e.State).Take(limit)];
            var items = string.Join(',', shown.Select(e => $"[\"{GitFileStatus.Code(e.State)}\",\"{Quote(e.Path)}\"]"));
            return Encoding.UTF8.GetBytes($"{{\"status\":\"tree\",\"count\":{shown.Count},\"entries\":[{items}]}}");
        }
        catch (GitException) { return Status("failed"); }
    }

    // ---- files changed outside the editor ----

    /// <summary>
    /// Keeps the repository of an open file watched so the panel and the status bar follow changes made by other programs. Every repository that has an open file is watched, one watch
    /// each; a file that is looked at again changes nothing, and a file that turns up in another repository (a path that was renamed) moves over. <see cref="Release"/> ends a watch.
    /// </summary>
    private void Watch(string document, GitRepository repo)
    {
        if (_settings.WatchDebounceMs <= 0 || !Guid.TryParse(document, out var id)) return;
        lock (_watchGate)
        {
            if (_documentRoots.TryGetValue(id, out var known))
            {
                if (_watched.ContainsKey(repo.Root) && known == repo.Root) return;
                Release(id);
            }
            if (!_watched.TryGetValue(repo.Root, out var watched))
            {
                try { watched = new Watched(new RepoWatcher(repo.Root, repo.GitDirectory, _settings.WatchDebounceMs, () => _ = RefreshAsync(repo))); }
                catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException) { return; } // nothing to watch: the next command still reads the status
                _watched[repo.Root] = watched;
            }
            watched.Documents.Add(id);
            _documentRoots[id] = repo.Root;
        }
    }

    /// <summary>The document is closed: the repository it kept watched is not watched any more when no other open file of it is left.</summary>
    private void Release(Guid document)
    {
        lock (_watchGate)
        {
            if (!_documentRoots.Remove(document, out var root) || !_watched.TryGetValue(root, out var watched)) return;
            watched.Documents.Remove(document);
            if (watched.Documents.Count > 0) return;
            _watched.Remove(root);
            watched.Watcher.Dispose();
        }
    }

    private async Task RefreshAsync(GitRepository repo)
    {
        await _refreshing.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_watchGate)
            {
                if (!_watched.ContainsKey(repo.Root)) return;
            }
            var entries = await repo.StatusAsync(default).ConfigureAwait(false);
            if (_panelRoot == repo.Root) await SetPanelAsync(_language, repo, entries, reveal: false, default).ConfigureAwait(false);
            await ShowStatusBarAsync(_language, repo, entries, default).ConfigureAwait(false);
        }
        catch (Exception e) when (e is RpcException or GitException or IOException or OperationCanceledException or ObjectDisposedException) { }
        finally { _refreshing.Release(); }
    }

    // ---- documents of the index and of HEAD ----

    private sealed record Tree(string Codec, long Length);

    private async Task<Tree> TreeAsync(Request r, CancellationToken ct)
    {
        var info = await CallAsync(DocumentMethods.Info, $"{{\"document\":\"{r.Document}\"}}", ct);
        if (info.GetProperty("revision").GetInt64() != r.Revision) throw new RpcException(DocumentErrorCodes.RevisionConflict, "Stale.");
        return new Tree(info.GetProperty("codec").GetString()!, info.GetProperty("byteLength").GetInt64());
    }

    /// <summary>
    /// Opens the virtual document of <paramref name="key"/> with the content of the blob (empty without one), or, when it is open already, brings it to that content.
    /// Opening the same key returns the open document and ignores the content, so a document filled from another blob is updated.
    /// </summary>
    private async Task<(string Id, long Revision)> EnsureAsync(string key, string name, string title, string codec, byte[]? content, string sha, CancellationToken ct)
    {
        var bytes = Convert.ToBase64String(content ?? []);
        var open = await CallAsync(DocumentMethods.VirtualOpen, $"{{\"key\":\"{Quote(key)}\",\"name\":\"{Quote(name)}\",\"title\":\"{Quote(title)}\",\"encoding\":\"{Quote(codec)}\",\"bytes\":\"{bytes}\"}}", ct);
        var id = open.GetProperty("document").GetString()!;
        long revision = open.GetProperty("revision").GetInt64();
        if (!open.GetProperty("created").GetBoolean() && !(_loaded.TryGetValue(key, out var seen) && seen.Document == id && seen.Sha == sha))
            revision = (await CallAsync(DocumentMethods.VirtualUpdate, $"{{\"document\":\"{id}\",\"bytes\":\"{bytes}\"}}", ct)).GetProperty("revision").GetInt64();
        _loaded[key] = (id, sha);
        return (id, revision);
    }

    private static string IndexKey(GitRepository repo, string path) => $"git-index:{repo.Root}:{path}";

    private static string HeadKey(GitRepository repo, string path) => $"git-head:{repo.Root}:{path}";

    private async Task<byte[]> ReadBlobAsync(GitRepository repo, string? sha, CancellationToken ct) =>
        sha is null ? [] : await repo.ReadBlobAsync(sha, ct);

    private async Task<byte[]> OpenDiffAsync(Request r, GitRepository repo, string file, bool head, CancellationToken ct)
    {
        var rel = repo.RelativePath(file);
        var tree = await TreeAsync(r, ct);
        string? sha = head ? await repo.HeadBlobAsync(rel, ct) : (await repo.IndexEntryAsync(rel, ct)) is { Stage: 0 } entry ? entry.Sha : null;
        var content = await ReadBlobAsync(repo, sha, ct);
        var key = head ? HeadKey(repo, rel) : IndexKey(repo, rel);
        var title = Texts.Get(r.Ui, head ? "headTitle" : "indexTitle", ("file", file));
        var (id, revision) = await EnsureAsync(key, file, title, tree.Codec, content, sha ?? "", ct);
        var opened = await CallAsync(DocumentMethods.DiffOpen,
            $"{{\"left\":{{\"document\":\"{id}\",\"revision\":{revision}}},\"right\":{{\"document\":\"{r.Document}\",\"revision\":{r.Revision}}}," +
            $"\"title\":\"{Quote(title)}\",\"leftTitle\":\"{Quote(title)}\",\"rightTitle\":\"{Quote(Texts.Get(r.Ui, "treeTitle", ("file", file)))}\"," +
            // the buttons act on the difference the tab is on, then open the same comparison again in the place of this tab (same key)
            $"\"key\":\"{Quote("git-diff:" + (head ? "head" : "index") + $":{repo.Root}:{rel}")}\",\"actions\":[" +
            $"{{\"command\":\"{StageHunkCommand}\",\"title\":\"{Quote(Texts.Get(r.Ui, "actionStage"))}\",\"refresh\":\"{r.Command}\"}}," +
            $"{{\"command\":\"{UnstageHunkCommand}\",\"title\":\"{Quote(Texts.Get(r.Ui, "actionUnstage"))}\",\"refresh\":\"{r.Command}\"}}]}}", ct);
        return Encoding.UTF8.GetBytes($"{{\"status\":\"opened\",\"shown\":{(opened.GetProperty("opened").GetBoolean() ? "true" : "false")}}}");
    }

    // ---- stage one hunk ----

    private async Task<long> CaretAsync(Request r, CancellationToken ct)
    {
        if (r.Offset is { } at) return at;
        var read = await CallAsync(DocumentMethods.Selections, $"{{\"document\":\"{r.Document}\"}}", ct);
        if (read.GetProperty("revision").GetInt64() != r.Revision) throw new RpcException(DocumentErrorCodes.RevisionConflict, "Stale.");
        var primary = read.GetProperty("primary").GetInt32();
        var selections = read.GetProperty("selections");
        if (selections.GetArrayLength() == 0) throw new RpcException(DocumentErrorCodes.NoTextView, "The document has no caret.");
        return selections[primary >= 0 && primary < selections.GetArrayLength() ? primary : 0].GetProperty("caret").GetInt64();
    }

    /// <summary>All hunks of the diff of one document against another, with their byte range on both sides.</summary>
    private async Task<List<HunkPair>> ListHunksAsync(string left, long leftRevision, string right, long rightRevision, CancellationToken ct)
    {
        var hunks = new List<HunkPair>();
        for (long from = 0; ;)
        {
            var diff = await CallAsync(DocumentMethods.DiffCompute,
                $"{{\"left\":{{\"document\":\"{left}\",\"revision\":{leftRevision}}},\"right\":{{\"document\":\"{right}\",\"revision\":{rightRevision}}},\"from\":{from}}}", ct);
            foreach (var h in diff.GetProperty("hunks").EnumerateArray())
            {
                var l = h.GetProperty("left");
                var r = h.GetProperty("right");
                hunks.Add(new HunkPair(h.GetProperty("index").GetInt64(), l.GetProperty("start").GetInt64(), l.GetProperty("end").GetInt64(), r.GetProperty("start").GetInt64(), r.GetProperty("end").GetInt64()));
            }
            if (!diff.GetProperty("truncated").GetBoolean() || hunks.Count == 0) return hunks;
            from = hunks[^1].Index + 1;
        }
    }

    private async Task<byte[]> ReadWholeAsync(string document, long revision, CancellationToken ct)
    {
        var info = await CallAsync(DocumentMethods.Info, $"{{\"document\":\"{document}\"}}", ct);
        if (info.GetProperty("revision").GetInt64() != revision) throw new RpcException(DocumentErrorCodes.RevisionConflict, "Stale.");
        long length = info.GetProperty("byteLength").GetInt64();
        if (length > _settings.MaxBytes) throw new GitException($"The file is larger than {_settings.MaxBytes} bytes.");
        var bytes = new byte[length];
        for (long at = 0; at < length;)
        {
            int count = (int)Math.Min(_settings.ChunkBytes, length - at);
            var read = await CallAsync(DocumentMethods.ReadBytes, $"{{\"document\":\"{document}\",\"revision\":{revision},\"start\":{at},\"end\":{at + count}}}", ct);
            read.GetProperty("bytes").GetBytesFromBase64().CopyTo(bytes.AsSpan((int)at));
            at += count;
        }
        return bytes;
    }

    private async Task<long> LengthAsync(string document, CancellationToken ct) =>
        (await CallAsync(DocumentMethods.Info, $"{{\"document\":\"{document}\"}}", ct)).GetProperty("byteLength").GetInt64();

    /// <summary>
    /// <see cref="StageCoreAsync"/>; when the editor names the place (a click in the gutter or in a comparison tab) the documents of the index and of <c>HEAD</c> it had to open
    /// for it are closed again, so a click leaves no tab behind. Documents that were open before (a comparison the user looks at) stay.
    /// </summary>
    private async Task<byte[]> StageHunkAsync(Request r, GitRepository repo, string file, bool unstage, CancellationToken ct)
    {
        var rel = repo.RelativePath(file);
        var keys = r.Offset is null ? [] : new[] { IndexKey(repo, rel), HeadKey(repo, rel) }.Where(k => !_loaded.ContainsKey(k)).ToList();
        try { return await StageCoreAsync(r, repo, file, unstage, ct); }
        finally
        {
            foreach (var key in keys)
            {
                if (!_loaded.TryRemove(key, out var opened)) continue;
                try { await CallAsync(DocumentMethods.VirtualClose, $"{{\"document\":\"{opened.Document}\"}}", CancellationToken.None); }
                catch (RpcException) { /* closed by the user meanwhile */ }
            }
        }
    }

    /// <summary>
    /// Stages the change at the caret: the hunk of the index document against the working tree. With <paramref name="unstage"/> it takes back the staged change at the caret instead:
    /// the caret is moved from the tree to the index and the hunk of the index document against <c>HEAD</c> there is turned back into what <c>HEAD</c> has.
    /// </summary>
    private async Task<byte[]> StageCoreAsync(Request r, GitRepository repo, string file, bool unstage, CancellationToken ct)
    {
        var rel = repo.RelativePath(file);
        var tree = await TreeAsync(r, ct);
        var entry = await repo.IndexEntryAsync(rel, ct);
        if (entry is { Stage: not 0 }) return await TellAsync(r, "unmerged", NoticeLevel.Warning, ct);
        var key = IndexKey(repo, rel);
        // git adds a file with CRLF as LF under autocrlf; the blob must be written the same way, or the lines of the hunk would be the only ones with CR in it
        bool clean = entry is null || await repo.CleansLineEndingsAsync(rel, ct);
        // an index document read earlier from another blob means someone else staged in between: read it again and write nothing
        bool moved = _loaded.TryGetValue(key, out var seen) && seen.Sha != (entry?.Sha ?? "");
        var (index, indexRevision) = await EnsureAsync(key, file, Texts.Get(r.Ui, "indexTitle", ("file", file)), tree.Codec, await ReadBlobAsync(repo, entry?.Sha, ct), entry?.Sha ?? "", ct);
        if (moved) return await TellAsync(r, "conflict", NoticeLevel.Warning, ct, ("file", file));

        var caret = await CaretAsync(r, ct);
        var inTree = await ListHunksAsync(index, indexRevision, r.Document, r.Revision, ct);
        string source = r.Document;
        long sourceRevision = r.Revision;
        HunkSpan? hunk;
        string? headSha = null;
        if (!unstage) hunk = Hunks.At(inTree.Select(h => h.Right), caret, tree.Length);
        else
        {
            headSha = await repo.HeadBlobAsync(rel, ct);
            (source, sourceRevision) = await EnsureAsync(HeadKey(repo, rel), file, Texts.Get(r.Ui, "headTitle", ("file", file)), tree.Codec, await ReadBlobAsync(repo, headSha, ct), headSha ?? "", ct);
            hunk = Hunks.At((await ListHunksAsync(index, indexRevision, source, sourceRevision, ct)).Select(h => h.Left), Hunks.ToLeft(inTree, caret), await LengthAsync(index, ct));
        }
        if (hunk is null) return await TellAsync(r, unstage ? "noStagedHunk" : "noHunk", NoticeLevel.Info, ct);
        var staged = (await CallAsync(DocumentMethods.DiffStage,
            $"{{\"base\":{{\"document\":\"{index}\",\"revision\":{indexRevision}}},\"source\":{{\"document\":\"{source}\",\"revision\":{sourceRevision}}},\"hunk\":{hunk.Index}}}", ct)).GetProperty("revision").GetInt64();
        var content = await ReadWholeAsync(index, staged, ct);

        // the last look before writing: the index must still be the blob the hunk was staged into
        var now = await repo.IndexEntryAsync(rel, ct);
        if ((now?.Sha ?? "") != (entry?.Sha ?? "") || now is { Stage: not 0 }) return await ConflictAsync(r, repo, rel, file, tree, ct);
        try
        {
            if (unstage && headSha is null && content.Length == 0)
            {
                await repo.RemoveFromIndexAsync(rel, ct); // a new file with nothing staged left is not in the index
                _loaded[key] = (index, "");
            }
            else
            {
                var sha = await repo.WriteIndexBlobAsync(rel, entry?.Mode ?? GitRepository.NewFileMode, content, clean, ct);
                // the document holds the lines as the working tree has them; show what git stored
                if (clean) await EnsureAsync(key, file, Texts.Get(r.Ui, "indexTitle", ("file", file)), tree.Codec, await repo.ReadBlobAsync(sha, ct), sha, ct);
                else _loaded[key] = (index, sha);
            }
        }
        catch (GitException)
        {
            await ConflictAsync(r, repo, rel, file, tree, ct, tell: false); // the document is ahead of the index: bring it back
            throw;
        }
        var done = unstage ? "unstaged" : "staged";
        await NotifyAsync(NoticeLevel.Info, Texts.Get(r.Ui, done, ("hunk", (hunk.Index + 1).ToString()), ("file", file)), ct);
        return Encoding.UTF8.GetBytes($"{{\"status\":\"{done}\",\"hunk\":{hunk.Index}}}");
    }

    // ---- merge conflict: take a side of the block at the caret, then mark the file resolved ----

    private static TextCodec CodecOf(Tree tree) => TextCodec.ByName(tree.Codec) ?? TextCodec.Utf8;

    private async Task<byte[]> ResolveBlockAsync(Request r, GitRepository repo, string file, string side, CancellationToken ct)
    {
        var tree = await TreeAsync(r, ct);
        var blocks = Conflicts.Find(await ReadWholeAsync(r.Document, r.Revision, ct), CodecOf(tree));
        var block = Conflicts.At(blocks, await CaretAsync(r, ct));
        if (block is null)
        {
            // a file deleted by one side or added by both is in conflict with no marker in it: say what it is instead of where the caret is
            var kind = blocks.Count == 0 ? await repo.ConflictCodeAsync(repo.RelativePath(file), ct) : null;
            return kind is null or "UU" ? await TellAsync(r, "noConflictMarkers", NoticeLevel.Info, ct) : await TellAsync(r, "noMarkers", NoticeLevel.Info, ct, ("kind", Texts.Get(r.Ui, kind)));
        }
        async Task<string> TextAsync(long start, long end)
        {
            var read = await CallAsync(DocumentMethods.ReadText, $"{{\"document\":\"{r.Document}\",\"revision\":{r.Revision},\"start\":{start},\"end\":{end}}}", ct);
            return read.GetProperty("invalid").GetArrayLength() == 0 ? read.GetProperty("text").GetString()! : throw new GitException("The text has bytes that are not valid in its encoding.");
        }
        var ours = side == "theirs" ? "" : await TextAsync(block.OursStart, block.OursEnd);
        var theirs = side == "ours" ? "" : await TextAsync(block.TheirsStart, block.TheirsEnd);
        await CallAsync(DocumentMethods.EditApply,
            $"{{\"document\":\"{r.Document}\",\"revision\":{r.Revision},\"batchId\":\"{Guid.NewGuid():N}\",\"edits\":[{{\"start\":{block.Start},\"end\":{block.End},\"text\":\"{Quote(ours + theirs)}\"}}]}}", ct);
        await NotifyAsync(NoticeLevel.Info, Texts.Get(r.Ui, "resolved", ("side", Texts.Get(r.Ui, side))), ct);
        return Encoding.UTF8.GetBytes($"{{\"status\":\"resolved\",\"side\":\"{side}\"}}");
    }

    private async Task<byte[]> MarkResolvedAsync(Request r, GitRepository repo, string file, CancellationToken ct)
    {
        var rel = repo.RelativePath(file);
        if (await repo.IndexEntryAsync(rel, ct) is not { Stage: not 0 }) return await TellAsync(r, "notInConflict", NoticeLevel.Info, ct, ("file", file));
        var content = await ReadWholeAsync(r.Document, r.Revision, ct);
        var codec = CodecOf(await TreeAsync(r, ct));
        if (Conflicts.Find(content, codec).Count > 0) return await TellAsync(r, "markersLeft", NoticeLevel.Warning, ct);
        // git adds what is on disk, so the document must be what is saved
        var disk = File.Exists(r.Path) ? File.ReadAllBytes(r.Path!) : [];
        if (disk.AsSpan().StartsWith("﻿"u8) && !content.AsSpan().StartsWith("﻿"u8)) disk = disk[3..];
        if (!disk.AsSpan().SequenceEqual(content)) return await TellAsync(r, "unsaved", NoticeLevel.Warning, ct, ("file", file));
        await repo.AddAsync(rel, ct);
        await NotifyAsync(NoticeLevel.Info, Texts.Get(r.Ui, "markedResolved", ("file", file)), ct);
        return Status("markedResolved");
    }

    /// <summary>Resolves a conflict in which one side deleted the file (or both added it and nothing can be merged) by deleting it: from the index and from the working tree.</summary>
    private async Task<byte[]> DeleteConflictAsync(Request r, GitRepository repo, string file, CancellationToken ct)
    {
        var rel = repo.RelativePath(file);
        if (await repo.ConflictCodeAsync(rel, ct) is null) return await TellAsync(r, "notInConflict", NoticeLevel.Info, ct, ("file", file));
        await repo.DeleteAsync(rel, ct);
        _loaded.TryRemove(IndexKey(repo, rel), out _);
        await NotifyAsync(NoticeLevel.Info, Texts.Get(r.Ui, "conflictDeleted", ("file", file)), ct);
        return Status("conflictDeleted");
    }

    /// <summary>
    /// The user opened an item of the status panel whose path has no file (the editor does not open it itself). The one thing to do for it is a conflict that both sides deleted, which has
    /// no file to open and no command for a tab: after a question it is resolved with <c>git rm</c>. Any other missing file is only said to be missing.
    /// </summary>
    protected override async ValueTask OnEventAsync(JsonElement eventPayload)
    {
        if (!eventPayload.TryGetProperty("name", out var name) || name.GetString() != UiEvents.PanelActivated || !eventPayload.TryGetProperty("payload", out var payload)
            || payload.GetProperty("panel").GetString() != StatusPanel || payload.GetProperty("item").GetString() is not { } item) return;
        var ui = _language;
        try
        {
            if (_panelRoot is not { } root || await GitRepository.OpenAsync(root, _settings.TimeoutMs, _settings.MaxBytes, default) is not { } repo) return;
            var rel = Path.GetRelativePath(repo.Root, item).Replace('\\', '/');
            if (rel.StartsWith("../", StringComparison.Ordinal) || rel == "..") return;
            var file = Path.GetFileName(item);
            if (await repo.ConflictCodeAsync(rel, default) != "DD")
            {
                await NotifyAsync(NoticeLevel.Warning, Texts.Get(ui, "missingFile", ("file", rel)), default);
                return;
            }
            var answer = await CallAsync(UiMethods.Confirm, $"{{\"title\":\"{Quote(Texts.Get(ui, "bothDeletedTitle"))}\",\"message\":\"{Quote(Texts.Get(ui, "bothDeletedMessage", ("file", rel)))}\"}}", default);
            if (answer.GetProperty("confirmed").ValueKind != JsonValueKind.True) return;
            await repo.DeleteAsync(rel, default);
            _loaded.TryRemove(IndexKey(repo, rel), out _);
            await NotifyAsync(NoticeLevel.Info, Texts.Get(ui, "conflictDeleted", ("file", file)), default);
            var entries = await repo.StatusAsync(default);
            await SetPanelAsync(ui, repo, entries, reveal: false, default);
            await ShowStatusBarAsync(ui, repo, entries, default);
        }
        catch (Exception e) when (e is RpcException or GitException or IOException)
        {
            try { await NotifyAsync(NoticeLevel.Error, Texts.Get(ui, "failed", ("reason", e.Message)), default); }
            catch (RpcException) { /* the editor is gone */ }
        }
    }

    // ---- changes of the file for the gutter ----

    private static byte[] NoMarks(Request r) => Encoding.UTF8.GetBytes($"{{\"status\":\"none\",\"revision\":{r.Revision},\"hunks\":[]}}");

    /// <summary>
    /// The lines of the working tree document that differ from the index, and those that differ from <c>HEAD</c> only (staged), for the gutter. The editor asks in the background, so nothing is shown
    /// to the user: a file outside a repository, a file in conflict, an untracked file and any failure are the answer <c>none</c>. Nothing is opened and nothing is written.
    /// </summary>
    private async Task<byte[]> MarksAsync(Request r, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(r.Path) || await GitRepository.OpenAsync(r.Path, _settings.TimeoutMs, _settings.MaxBytes, ct) is not { } repo) return NoMarks(r);
            var rel = repo.RelativePath(Path.GetFileName(r.Path));
            var tree = await TreeAsync(r, ct);
            var entry = await repo.IndexEntryAsync(rel, ct);
            var headSha = await repo.HeadBlobAsync(rel, ct);
            if (entry is { Stage: not 0 } || (entry is null && headSha is null)) return NoMarks(r);
            var bytes = await ReadWholeAsync(r.Document, r.Revision, ct);
            var marks = GutterMarks.Compute(bytes, await ReadBlobAsync(repo, entry?.Sha, ct), await ReadBlobAsync(repo, headSha, ct), CodecOf(tree), ct);
            Watch(r.Document, repo);
            var items = string.Join(',', marks.Select(m => $"{{\"line\":{m.Line},\"lines\":{m.Lines},\"kind\":\"{m.Kind}\",\"staged\":{(m.Staged ? "true" : "false")}}}"));
            return Encoding.UTF8.GetBytes($"{{\"status\":\"hunks\",\"revision\":{r.Revision},\"hunks\":[{items}]}}");
        }
        catch (Exception e) when (e is RpcException or GitException or IOException) { return NoMarks(r); }
    }


    /// <summary>Reads the index of the file again into its document; nothing was written.</summary>
    private async Task<byte[]> ConflictAsync(Request r, GitRepository repo, string rel, string file, Tree tree, CancellationToken ct, bool tell = true)
    {
        var entry = await repo.IndexEntryAsync(rel, ct);
        _loaded.TryRemove(IndexKey(repo, rel), out _);
        await EnsureAsync(IndexKey(repo, rel), file, Texts.Get(r.Ui, "indexTitle", ("file", file)), tree.Codec, entry is { Stage: 0 } ? await repo.ReadBlobAsync(entry.Sha, ct) : [], entry?.Sha ?? "", ct);
        return tell ? await TellAsync(r, "conflict", NoticeLevel.Warning, ct, ("file", file)) : Status("conflict");
    }
}
