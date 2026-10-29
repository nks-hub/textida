using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Plugins.Git;

/// <summary>The end of a git command that is not an error of the plugin: its exit code and what it printed.</summary>
public sealed record GitResult(int Code, string Output, string Error)
{
    public bool Ok => Code == 0;
}

/// <summary>A branch of the repository; a remote branch is named with its remote (<c>origin/main</c>).</summary>
public sealed record BranchInfo(string Name, bool Current, bool Remote, string? Upstream, string Sha, string Subject);

/// <summary>A commit of the log.</summary>
public sealed record CommitInfo(string Sha, string Short, string Author, long Time, string Subject);

/// <summary>A file a commit changed: the status letter of <c>git diff --name-status</c> and the path, and for a rename the path it came from.</summary>
public sealed record CommitFile(char Status, string Path, string? From);

/// <summary>A stash entry: its name (<c>stash@{0}</c>) and its description.</summary>
public sealed record StashInfo(string Name, string Description);

/// <summary>How a network command ended, in the terms the user is told in: it went through, it was refused by the remote, the sign-in failed, or it failed otherwise.</summary>
public enum NetworkOutcome { Done, Rejected, AuthFailed, Failed }

/// <summary>What the plugin does to a repository beyond reading it: commits, branches, stashes, the log and the commands that talk to a remote.</summary>
public sealed partial class GitRepository
{
    private static readonly Regex UrlCredentials = new(@"(?<=://)[^/\s@]+(?::[^/\s@]*)?@", RegexOptions.Compiled);
    private static readonly Regex Percent = new(@"(\d{1,3})%", RegexOptions.Compiled);

    /// <summary>
    /// The text without the user name and password that a remote URL may carry (<c>https://user:secret@host/x</c> becomes <c>https://***@host/x</c>). Everything git prints that is shown to the
    /// user or written to a log goes through it; passwords belong to the credential helper of git and are never read here.
    /// </summary>
    public static string Scrub(string text) => UrlCredentials.Replace(text, "***@");

    /// <summary>Runs git in the repository, with an optional text for its standard input. A non-zero exit is a result, not an exception; a git that cannot be started, takes too long or prints too much is a <see cref="GitException"/>.</summary>
    public async Task<GitResult> ExecAsync(IReadOnlyList<string> args, bool readOnly, CancellationToken ct, byte[]? input = null, bool literalPathspecs = true)
    {
        var (code, output, error) = await RunAsync(Root, args, input, readOnly, ct, literalPathspecs).ConfigureAwait(false);
        return new GitResult(code, Encoding.UTF8.GetString(output), Scrub(error));
    }

    private async Task<GitResult> ExecOrThrowAsync(IReadOnlyList<string> args, CancellationToken ct, byte[]? input = null, bool literalPathspecs = true)
    {
        var result = await ExecAsync(args, readOnly: false, ct, input, literalPathspecs).ConfigureAwait(false);
        return result.Ok ? result : throw new GitException(result.Error.Length > 0 ? result.Error : $"git {args[0]} ended with code {result.Code}.");
    }

    // ---- state ----

    /// <summary>Whether <c>HEAD</c> names a commit (a repository without a commit has none).</summary>
    public async Task<bool> HasCommitsAsync(CancellationToken ct) => (await ExecAsync(["rev-parse", "--verify", "-q", "HEAD"], true, ct).ConfigureAwait(false)).Ok;

    /// <summary>The upstream of the current branch (<c>origin/main</c>), or null when it has none.</summary>
    public async Task<string?> UpstreamAsync(CancellationToken ct)
    {
        var result = await ExecAsync(["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}"], true, ct).ConfigureAwait(false);
        return result.Ok && result.Output.Trim() is { Length: > 0 } name ? name : null;
    }

    /// <summary>The commits the current branch is ahead of its upstream and behind it; (0, 0) without an upstream. Uses what was fetched last, and asks no remote.</summary>
    public async Task<(int Ahead, int Behind)> AheadBehindAsync(CancellationToken ct)
    {
        var result = await ExecAsync(["rev-list", "--left-right", "--count", "HEAD...@{u}"], true, ct).ConfigureAwait(false);
        var parts = result.Output.Split(['\t', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return result.Ok && parts.Length == 2 && int.TryParse(parts[0], out int ahead) && int.TryParse(parts[1], out int behind) ? (ahead, behind) : (0, 0);
    }

    /// <summary>The names of the remotes.</summary>
    public async Task<IReadOnlyList<string>> RemotesAsync(CancellationToken ct) =>
        [.. (await ExecAsync(["remote"], true, ct).ConfigureAwait(false)).Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>The messages of the last <paramref name="count"/> commits, newest first, for the box of the commit panel.</summary>
    public async Task<IReadOnlyList<string>> RecentMessagesAsync(int count, CancellationToken ct)
    {
        var result = await ExecAsync(["log", $"-n{count}", "--format=%B%x00"], true, ct).ConfigureAwait(false);
        return result.Ok ? [.. result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(m => m.Trim()).Where(m => m.Length > 0)] : [];
    }

    // ---- staging and committing ----

    /// <summary>Puts paths (relative to the top of the working tree) in the index as they are on disk; a deleted file is staged as deleted.</summary>
    public async Task StagePathsAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        if (paths.Count > 0) await ExecOrThrowAsync(["add", "-A", "--", .. paths], ct).ConfigureAwait(false);
    }

    /// <summary>Takes paths out of the index again: back to what <c>HEAD</c> has, or out of it altogether where <c>HEAD</c> has nothing (also in a repository without a commit).</summary>
    public async Task UnstagePathsAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        if (paths.Count == 0) return;
        if (await HasCommitsAsync(ct).ConfigureAwait(false)) await ExecOrThrowAsync(["reset", "-q", "HEAD", "--", .. paths], ct).ConfigureAwait(false);
        else await ExecOrThrowAsync(["rm", "--cached", "-r", "-q", "--ignore-unmatch", "--", .. paths], ct).ConfigureAwait(false);
    }

    /// <summary>Whether the index differs from <c>HEAD</c> (from the empty tree in a repository without a commit).</summary>
    public async Task<bool> HasStagedChangesAsync(CancellationToken ct)
    {
        var result = await ExecAsync(["diff", "--cached", "--quiet"], true, ct).ConfigureAwait(false);
        return result.Code == 1;
    }

    /// <summary>Commits the index with the message (read from standard input, so its length and characters do not matter); with <paramref name="amend"/> the last commit is replaced. Returns the new commit.</summary>
    public async Task<CommitInfo> CommitAsync(string message, bool amend, CancellationToken ct)
    {
        List<string> args = ["commit", "-F", "-", "--cleanup=strip"];
        if (amend) args.Add("--amend");
        if (amend && message.Trim().Length == 0) args = ["commit", "--amend", "--no-edit"];
        await ExecOrThrowAsync(args, ct, Encoding.UTF8.GetBytes(message)).ConfigureAwait(false);
        return (await LogAsync(null, 1, ct).ConfigureAwait(false))[0];
    }

    // ---- log ----

    /// <summary>The newest <paramref name="max"/> commits of the repository, or of one file (followed through renames).</summary>
    public async Task<IReadOnlyList<CommitInfo>> LogAsync(string? path, int max, CancellationToken ct)
    {
        List<string> args = ["log", $"-n{max}", "--no-color", "--format=%H%x09%h%x09%an%x09%at%x09%s"];
        if (path is not null) args.AddRange(["--follow", "--", path]);
        var result = await ExecAsync(args, true, ct).ConfigureAwait(false);
        var list = new List<CommitInfo>();
        if (!result.Ok) return list; // a repository without a commit has no log
        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\t', 5);
            if (f.Length == 5 && long.TryParse(f[3], out long time)) list.Add(new CommitInfo(f[0], f[1], f[2], time, f[4].TrimEnd('\r')));
        }
        return list;
    }

    /// <summary>The files a commit changed, against its first parent (against nothing for the first commit).</summary>
    public async Task<IReadOnlyList<CommitFile>> CommitFilesAsync(string sha, CancellationToken ct)
    {
        bool hasParent = (await ExecAsync(["rev-parse", "--verify", "-q", sha + "^1"], true, ct).ConfigureAwait(false)).Ok;
        IReadOnlyList<string> args = hasParent ? ["diff", "--name-status", "-z", "-M", sha + "^1", sha] : ["diff-tree", "--root", "-r", "--no-commit-id", "--name-status", "-z", "-M", sha];
        var result = await ExecAsync(args, true, ct).ConfigureAwait(false);
        var files = new List<CommitFile>();
        var tokens = result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 1 < tokens.Length; i++)
        {
            char status = tokens[i][0];
            if (status is 'R' or 'C' && i + 2 < tokens.Length)
            {
                files.Add(new CommitFile(status, tokens[i + 2], tokens[i + 1]));
                i += 2;
            }
            else
            {
                files.Add(new CommitFile(status, tokens[i + 1], null));
                i++;
            }
        }
        return files;
    }

    /// <summary>The bytes of a file at a revision (a commit, <c>HEAD</c>, or <c>:</c> for the index); null when the revision has no such file.</summary>
    public async Task<byte[]?> BlobAtAsync(string revision, string path, CancellationToken ct)
    {
        var (code, output, _) = await RunAsync(Root, ["cat-file", "blob", $"{revision}:{path}"], null, true, ct).ConfigureAwait(false);
        return code == 0 ? output : null;
    }

    // ---- branches ----

    /// <summary>The local branches, then the branches of the remotes (without the <c>HEAD</c> pointer of each).</summary>
    public async Task<IReadOnlyList<BranchInfo>> BranchesAsync(CancellationToken ct)
    {
        var list = new List<BranchInfo>();
        var local = await ExecAsync(["for-each-ref", "--format=%(HEAD)%09%(refname)%09%(upstream:short)%09%(objectname:short)%09%(contents:subject)", "refs/heads"], true, ct).ConfigureAwait(false);
        foreach (var line in local.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\t', 5);
            if (f.Length == 5) list.Add(new BranchInfo(f[1]["refs/heads/".Length..], f[0] == "*", false, f[2].Length > 0 ? f[2] : null, f[3], f[4].TrimEnd('\r')));
        }
        var remote = await ExecAsync(["for-each-ref", "--format=%(refname)%09%(objectname:short)%09%(contents:subject)", "refs/remotes"], true, ct).ConfigureAwait(false);
        foreach (var line in remote.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\t', 3);
            if (f.Length == 3 && !f[0].EndsWith("/HEAD", StringComparison.Ordinal)) list.Add(new BranchInfo(f[0]["refs/remotes/".Length..], false, true, null, f[1], f[2].TrimEnd('\r')));
        }
        return list;
    }

    /// <summary>Switches to a branch; for a branch of a remote (<c>origin/feature</c>) a local branch that tracks it is made.</summary>
    public Task SwitchAsync(BranchInfo branch, CancellationToken ct) =>
        ExecOrThrowAsync(branch.Remote ? ["checkout", "--track", "-q", branch.Name] : ["checkout", "-q", branch.Name], ct);

    /// <summary>Makes a branch from the current commit and switches to it.</summary>
    public async Task CreateBranchAsync(string name, CancellationToken ct)
    {
        var check = await ExecAsync(["check-ref-format", "--branch", name], true, ct).ConfigureAwait(false);
        if (!check.Ok) throw new GitException($"'{name}' is not a name a branch can have.");
        await ExecOrThrowAsync(["checkout", "-q", "-b", name], ct).ConfigureAwait(false);
    }

    /// <summary>Deletes a local branch; without <paramref name="force"/> only when it is merged. The error says when it is not.</summary>
    public Task DeleteBranchAsync(string name, bool force, CancellationToken ct) => ExecOrThrowAsync(["branch", force ? "-D" : "-d", name], ct);

    // ---- stash ----

    public async Task<IReadOnlyList<StashInfo>> StashesAsync(CancellationToken ct)
    {
        var result = await ExecAsync(["stash", "list", "--format=%gd%x09%gs"], true, ct).ConfigureAwait(false);
        var list = new List<StashInfo>();
        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\t', 2);
            list.Add(new StashInfo(f[0], f.Length > 1 ? f[1].TrimEnd('\r') : ""));
        }
        return list;
    }

    /// <summary>Stashes the changes of the tree, the untracked files included; false when there was nothing to stash.</summary>
    public async Task<bool> StashPushAsync(string? message, CancellationToken ct)
    {
        List<string> args = ["stash", "push", "--include-untracked"];
        if (!string.IsNullOrWhiteSpace(message)) args.AddRange(["-m", message.Trim()]);
        var result = await ExecOrThrowAsync(args, ct, literalPathspecs: false).ConfigureAwait(false); // no path of ours is given, and --include-untracked needs the pathspec git makes itself
        return !result.Output.Contains("No local changes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Applies a stash and keeps it. A conflict is a result (<see cref="GitResult.Code"/> is not 0), not an exception.</summary>
    public Task<GitResult> StashApplyAsync(string name, CancellationToken ct) => ExecAsync(["stash", "apply", name], readOnly: false, ct);

    public Task StashDropAsync(string name, CancellationToken ct) => ExecOrThrowAsync(["stash", "drop", name], ct);

    // ---- the remote ----

    /// <summary>
    /// The commits on their way out, one group per branch: the current branch against its upstream, and the other branches that are ahead of theirs. Uses what was fetched last and asks no remote.
    /// A branch without an upstream counts all its commits that no remote branch has.
    /// </summary>
    public async Task<IReadOnlyList<(string Branch, string? Upstream, IReadOnlyList<CommitInfo> Commits)>> OutgoingAsync(string current, int max, CancellationToken ct)
    {
        var groups = new List<(string, string?, IReadOnlyList<CommitInfo>)>();
        foreach (var branch in (await BranchesAsync(ct).ConfigureAwait(false)).Where(b => !b.Remote))
        {
            var range = branch.Upstream is not null ? $"{branch.Upstream}..{branch.Name}" : branch.Name;
            List<string> args = ["log", $"-n{max}", "--no-color", "--format=%H%x09%h%x09%an%x09%at%x09%s", range];
            if (branch.Upstream is null) args.AddRange(["--not", "--remotes"]);
            var result = await ExecAsync(args, true, ct).ConfigureAwait(false);
            if (!result.Ok) continue;
            var commits = new List<CommitInfo>();
            foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var f = line.Split('\t', 5);
                if (f.Length == 5 && long.TryParse(f[3], out long time)) commits.Add(new CommitInfo(f[0], f[1], f[2], time, f[4].TrimEnd('\r')));
            }
            if (commits.Count > 0 || branch.Name == current) groups.Add((branch.Name, branch.Upstream, commits));
        }
        return groups;
    }

    /// <summary>How the text a network command printed is told to the user.</summary>
    public static NetworkOutcome Classify(GitResult result)
    {
        if (result.Ok) return NetworkOutcome.Done;
        string text = result.Error;
        if (text.Contains("[rejected]", StringComparison.Ordinal) || text.Contains("non-fast-forward", StringComparison.Ordinal) || text.Contains("fetch first", StringComparison.Ordinal)) return NetworkOutcome.Rejected;
        string[] signs = ["Authentication failed", "terminal prompts disabled", "could not read Username", "could not read Password", "Permission denied (publickey", "Host key verification failed", "HTTP 401", "HTTP 403", "The requested URL returned error: 403", "The requested URL returned error: 401"];
        return signs.Any(s => text.Contains(s, StringComparison.OrdinalIgnoreCase)) ? NetworkOutcome.AuthFailed : NetworkOutcome.Failed;
    }

    /// <summary>
    /// Runs a command that talks to a remote. It runs only on a request of the user, never prompts on a terminal (a password is asked of the credential helper of git, which this process
    /// never sees), reports how far it is as git prints it (<paramref name="progress"/>: a fraction from 0 to 1 or null, and the line), ends at <paramref name="timeout"/> (zero is no limit)
    /// and is killed with all it started when <paramref name="ct"/> fires, which throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public async Task<GitResult> NetworkAsync(IReadOnlyList<string> args, TimeSpan timeout, Action<double?, string>? progress, CancellationToken ct)
    {
        ProcessStartInfo info;
        try { info = ProcessLaunch.CreateStartInfo(_git, ["--literal-pathspecs", .. args]); }
        catch (ToolException e) { throw new GitException(e.Message); }
        info.WorkingDirectory = Root;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardErrorEncoding = Encoding.UTF8;
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GIT_EDITOR"] = ":";
        using var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { throw new GitException(e.Message); }
        ProcessLaunch.Track(process);
        using var limit = timeout > TimeSpan.Zero ? new CancellationTokenSource(timeout) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, limit.Token);
        var error = new StringBuilder();
        var stdout = process.StandardOutput.ReadToEndAsync(linked.Token);
        var stderr = Task.Run(async () =>
        {
            var buffer = new char[1024];
            var segment = new StringBuilder();
            long lastReport = 0;
            void Flush()
            {
                if (segment.Length == 0) return;
                string line = Scrub(segment.ToString().Trim());
                segment.Clear();
                if (line.Length == 0) return;
                error.Append(line).Append('\n');
                if (progress is null) return;
                var percent = Percent.Match(line);
                long now = Stopwatch.GetTimestamp();
                if (percent.Success && now - lastReport < Stopwatch.Frequency / 10) return;
                lastReport = now;
                progress(percent.Success && int.Parse(percent.Groups[1].Value) is <= 100 and var p ? p / 100.0 : null, line.Length > 200 ? line[..200] : line);
            }
            int n;
            while ((n = await process.StandardError.ReadAsync(buffer.AsMemory(), linked.Token).ConfigureAwait(false)) > 0)
                for (int i = 0; i < n; i++)
                {
                    if (buffer[i] is '\r' or '\n') Flush();
                    else segment.Append(buffer[i]);
                }
            Flush();
        });
        try
        {
            process.StandardInput.Close();
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* ended by itself */ }
            ct.ThrowIfCancellationRequested();
            throw new GitException(limit.IsCancellationRequested ? "git did not finish in time." : e.Message);
        }
        return new GitResult(process.ExitCode, Scrub(stdout.Result), error.ToString().Trim());
    }
}
