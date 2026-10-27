using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
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
public sealed record GitSettings(int TimeoutMs, long MaxBytes, int MaxEntries, int ChunkBytes);

/// <summary>
/// The built-in Git: the status of the working tree in a panel, the file against the index or against <c>HEAD</c> in a comparison tab, and staging the change under the caret.
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
    public const string StatusPanel = "git:status";

    private readonly GitSettings _settings;

    /// <summary>The blob each virtual document (by key) was filled from; empty for a path that is not in the tree of that revision.</summary>
    private readonly ConcurrentDictionary<string, (string Document, string Sha)> _loaded = new();

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

    private sealed record Request(string Command, string Document, long Revision, string Ui, string? Path);

    private async ValueTask<byte[]?> ExecuteAsync(JsonElement p, CancellationToken ct)
    {
        var r = new Request(p.GetProperty("command").GetString()!, p.GetProperty("document").GetString()!, p.GetProperty("revision").GetInt64(),
            p.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString()! : "en",
            p.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String ? path.GetString() : null);
        if (r.Command is not (StatusCommand or DiffIndexCommand or DiffHeadCommand or StageHunkCommand or UnstageHunkCommand or ConflictOursCommand or ConflictTheirsCommand or ConflictBothCommand or MarkResolvedCommand))
            throw new RpcException(RpcErrorCodes.InvalidParams, "Unknown command.");
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
                ConflictOursCommand => await ResolveBlockAsync(r, "ours", ct),
                ConflictTheirsCommand => await ResolveBlockAsync(r, "theirs", ct),
                ConflictBothCommand => await ResolveBlockAsync(r, "both", ct),
                MarkResolvedCommand => await MarkResolvedAsync(r, repo, file, ct),
                _ => await OpenDiffAsync(r, repo, file, r.Command == DiffHeadCommand, ct),
            };
            await ShowStatusBarAsync(r, repo, ct);
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
        var shown = entries.Take(_settings.MaxEntries).ToList();
        var items = string.Join(',', shown.Select(e =>
            $"{{\"id\":\"{Quote(Path.GetFullPath(Path.Combine(repo.Root, e.Path)))}\",\"label\":\"{Quote(e.Path)}\",\"detail\":\"{Quote(Texts.Describe(r.Ui, e))}\"}}"));
        var title = "Git: " + Path.GetFileName(repo.Root.TrimEnd('/', '\\'));
        await CallAsync(UiMethods.PanelSet, $"{{\"panel\":\"{StatusPanel}\",\"title\":\"{Quote(title)}\",\"kind\":\"list\",\"items\":[{items}],\"reveal\":true}}", ct);
        if (entries.Count == 0) await NotifyAsync(NoticeLevel.Info, Texts.Get(r.Ui, "clean"), ct);
        else if (entries.Count > shown.Count) await NotifyAsync(NoticeLevel.Warning, Texts.Get(r.Ui, "truncated", ("max", _settings.MaxEntries.ToString())), ct);
        return Encoding.UTF8.GetBytes($"{{\"status\":\"status\",\"count\":{entries.Count},\"shown\":{shown.Count}}}");
    }

    /// <summary>The status bar item: the branch and how many files are staged, changed, untracked and in conflict. A status that cannot be shown must not break the command.</summary>
    private async Task ShowStatusBarAsync(Request r, GitRepository repo, CancellationToken ct)
    {
        try
        {
            var c = StatusCounts.Of(await repo.StatusAsync(ct));
            var branch = await repo.BranchAsync(ct);
            var parts = new List<string> { branch.Length > 0 ? branch : "Git" };
            if (c.Staged > 0) parts.Add("+" + c.Staged);
            if (c.Changed > 0) parts.Add("~" + c.Changed);
            if (c.Untracked > 0) parts.Add("?" + c.Untracked);
            if (c.Conflicts > 0) parts.Add("!" + c.Conflicts);
            var tip = Texts.Get(r.Ui, "statusTip", ("branch", branch), ("staged", c.Staged.ToString()), ("changed", c.Changed.ToString()), ("untracked", c.Untracked.ToString()), ("conflicts", c.Conflicts.ToString()));
            await CallAsync(UiMethods.StatusSet, $"{{\"text\":\"{Quote(string.Join(' ', parts))}\",\"tooltip\":\"{Quote(tip)}\"}}", ct);
        }
        catch (Exception e) when (e is RpcException or GitException or IOException) { }
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

    private async Task<byte[]> ReadBlobAsync(GitRepository repo, string? sha, CancellationToken ct) =>
        sha is null ? [] : await repo.ReadBlobAsync(sha, ct);

    private async Task<byte[]> OpenDiffAsync(Request r, GitRepository repo, string file, bool head, CancellationToken ct)
    {
        var rel = repo.RelativePath(file);
        var tree = await TreeAsync(r, ct);
        string? sha = head ? await repo.HeadBlobAsync(rel, ct) : (await repo.IndexEntryAsync(rel, ct)) is { Stage: 0 } entry ? entry.Sha : null;
        var content = await ReadBlobAsync(repo, sha, ct);
        var key = (head ? "git-head:" : "git-index:") + $"{repo.Root}:{rel}";
        var title = Texts.Get(r.Ui, head ? "headTitle" : "indexTitle", ("file", file));
        var (id, revision) = await EnsureAsync(key, file, title, tree.Codec, content, sha ?? "", ct);
        var opened = await CallAsync(DocumentMethods.DiffOpen,
            $"{{\"left\":{{\"document\":\"{id}\",\"revision\":{revision}}},\"right\":{{\"document\":\"{r.Document}\",\"revision\":{r.Revision}}}," +
            $"\"title\":\"{Quote(title)}\",\"leftTitle\":\"{Quote(title)}\",\"rightTitle\":\"{Quote(Texts.Get(r.Ui, "treeTitle", ("file", file)))}\"}}", ct);
        return Encoding.UTF8.GetBytes($"{{\"status\":\"opened\",\"shown\":{(opened.GetProperty("opened").GetBoolean() ? "true" : "false")}}}");
    }

    // ---- stage one hunk ----

    private async Task<long> CaretAsync(Request r, CancellationToken ct)
    {
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
    /// Stages the change at the caret: the hunk of the index document against the working tree. With <paramref name="unstage"/> it takes back the staged change at the caret instead:
    /// the caret is moved from the tree to the index and the hunk of the index document against <c>HEAD</c> there is turned back into what <c>HEAD</c> has.
    /// </summary>
    private async Task<byte[]> StageHunkAsync(Request r, GitRepository repo, string file, bool unstage, CancellationToken ct)
    {
        var rel = repo.RelativePath(file);
        var tree = await TreeAsync(r, ct);
        var entry = await repo.IndexEntryAsync(rel, ct);
        if (entry is { Stage: not 0 }) return await TellAsync(r, "unmerged", NoticeLevel.Warning, ct);
        var key = IndexKey(repo, rel);
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
            (source, sourceRevision) = await EnsureAsync($"git-head:{repo.Root}:{rel}", file, Texts.Get(r.Ui, "headTitle", ("file", file)), tree.Codec, await ReadBlobAsync(repo, headSha, ct), headSha ?? "", ct);
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
            else _loaded[key] = (index, await repo.WriteIndexBlobAsync(rel, entry?.Mode ?? GitRepository.NewFileMode, content, ct));
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

    private async Task<byte[]> ResolveBlockAsync(Request r, string side, CancellationToken ct)
    {
        await TreeAsync(r, ct);
        var block = Conflicts.At(Conflicts.Find(await ReadWholeAsync(r.Document, r.Revision, ct)), await CaretAsync(r, ct));
        if (block is null) return await TellAsync(r, "noConflictMarkers", NoticeLevel.Info, ct);
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
        if (Conflicts.Find(content).Count > 0) return await TellAsync(r, "markersLeft", NoticeLevel.Warning, ct);
        // git adds what is on disk, so the document must be what is saved
        var disk = File.Exists(r.Path) ? File.ReadAllBytes(r.Path!) : [];
        if (disk.AsSpan().StartsWith("﻿"u8) && !content.AsSpan().StartsWith("﻿"u8)) disk = disk[3..];
        if (!disk.AsSpan().SequenceEqual(content)) return await TellAsync(r, "unsaved", NoticeLevel.Warning, ct, ("file", file));
        await repo.AddAsync(rel, ct);
        await NotifyAsync(NoticeLevel.Info, Texts.Get(r.Ui, "markedResolved", ("file", file)), ct);
        return Status("markedResolved");
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
