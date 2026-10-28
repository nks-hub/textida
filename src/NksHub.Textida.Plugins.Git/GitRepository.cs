using System.Diagnostics;
using System.Text;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Plugins.Git;

/// <summary>A git command ended with a non-zero code, or output was larger than the plugin reads.</summary>
public sealed class GitException(string message) : Exception(message);

/// <summary>One line of <c>git status</c>: <paramref name="Index"/> and <paramref name="Tree"/> are the two status letters (space = unchanged, <c>?</c> = untracked).</summary>
public sealed record StatusEntry(char Index, char Tree, string Path, string? From);

/// <summary>What the status bar shows: files with a change in the index, in the tree, untracked, and in conflict.</summary>
public sealed record StatusCounts(int Staged, int Changed, int Untracked, int Conflicts)
{
    /// <summary>An unmerged path: one of the seven pairs of letters git status prints for it (<c>UU</c>, <c>AA</c>, <c>DD</c>, <c>AU</c>, <c>UA</c>, <c>DU</c>, <c>UD</c>).</summary>
    public static bool IsConflict(StatusEntry e) => e.Index == 'U' || e.Tree == 'U' || (e.Index, e.Tree) is ('A', 'A') or ('D', 'D');

    public static StatusCounts Of(IEnumerable<StatusEntry> entries)
    {
        int staged = 0, changed = 0, untracked = 0, conflicts = 0;
        foreach (var e in entries)
        {
            if (e.Index == '?') untracked++;
            else if (IsConflict(e)) conflicts++;
            else
            {
                if (e.Index != ' ') staged++;
                if (e.Tree != ' ') changed++;
            }
        }
        return new StatusCounts(staged, changed, untracked, conflicts);
    }
}

/// <summary>An entry of the index:the file mode, the blob and the merge stage (0 unless the path is in conflict).</summary>
public sealed record IndexEntry(string Mode, string Sha, int Stage);

/// <summary>
/// A repository seen through the git program, started with an argument list and no shell. Reads never take the optional locks (a plain <c>git status</c> would refresh the
/// index file); the only writes are <see cref="WriteIndexBlobAsync"/>, which the user asks for by staging.
/// </summary>
public sealed class GitRepository
{
    /// <summary>The mode of a file that is not in the index yet.</summary>
    public const string NewFileMode = "100644";

    private readonly string _git;
    private readonly int _timeoutMs;
    private readonly long _maxBytes;

    /// <summary>The top directory of the working tree.</summary>
    public string Root { get; }

    /// <summary>The path of the folder the file was found in, relative to <see cref="Root"/>, with a closing slash (empty at the top).</summary>
    public string Prefix { get; }

    /// <summary>The folder with the index file and <c>HEAD</c> of this working tree; for a linked worktree it is not <c>.git</c> of the root.</summary>
    public string GitDirectory { get; }

    private GitRepository(string git, string root, string prefix, string gitDirectory, int timeoutMs, long maxBytes) =>
        (_git, Root, Prefix, GitDirectory, _timeoutMs, _maxBytes) = (git, root, prefix, gitDirectory, timeoutMs, maxBytes);

    /// <summary>Finds the repository a path (a file or a folder) belongs to; null when it is in none. Throws <see cref="GitException"/> when git is not found.</summary>
    /// <param name="timeoutMs">Time after which a git command is ended.</param>
    /// <param name="maxBytes">Largest output read from one command (a blob).</param>
    public static async Task<GitRepository?> OpenAsync(string path, int timeoutMs, long maxBytes, CancellationToken ct)
    {
        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path));
        if (folder is null || !Directory.Exists(folder)) return null;
        string git;
        try { git = ToolRunner.Resolve(new ToolSpec("git", "git", []), _ => null, folder).Executable; }
        catch (ToolException e) { throw new GitException(e.Message); }
        var probe = new GitRepository(git, folder, "", "", timeoutMs, maxBytes);
        var (code, output, _) = await probe.RunAsync(folder, ["rev-parse", "--show-toplevel", "--show-prefix", "--absolute-git-dir"], null, readOnly: true, ct);
        if (code != 0) return null;
        var lines = Encoding.UTF8.GetString(output).Split('\n');
        if (lines.Length < 3) return null;
        return new GitRepository(git, lines[0].TrimEnd('\r'), lines[1].TrimEnd('\r'), lines[2].TrimEnd('\r'), timeoutMs, maxBytes);
    }

    /// <summary>The path of a file of the folder this repository was opened from, as git names it.</summary>
    public string RelativePath(string fileName) => Prefix + fileName;

    private async Task<(int Code, byte[] Output, string Error)> RunAsync(string directory, IReadOnlyList<string> args, byte[]? input, bool readOnly, CancellationToken ct)
    {
        ProcessStartInfo info;
        try { info = ProcessLaunch.CreateStartInfo(_git, ["--literal-pathspecs", .. args]); } // a file named like a glob is that file
        catch (ToolException e) { throw new GitException(e.Message); }
        info.WorkingDirectory = directory;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardErrorEncoding = Encoding.UTF8;
        if (readOnly) info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { throw new GitException(e.Message); }
        using var limit = new CancellationTokenSource(_timeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, limit.Token);
        var output = new MemoryStream();
        var stdout = Task.Run(async () =>
        {
            var buffer = new byte[64 * 1024];
            int n;
            while ((n = await process.StandardOutput.BaseStream.ReadAsync(buffer, linked.Token).ConfigureAwait(false)) > 0)
            {
                if (output.Length + n > _maxBytes) throw new GitException($"git printed more than {_maxBytes} bytes.");
                output.Write(buffer, 0, n);
            }
        });
        var stderr = process.StandardError.ReadToEndAsync(linked.Token);
        try
        {
            if (input is not null) await process.StandardInput.BaseStream.WriteAsync(input, linked.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is OperationCanceledException or GitException or IOException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* ended by itself */ }
            ct.ThrowIfCancellationRequested();
            throw e as GitException ?? new GitException(limit.IsCancellationRequested ? "git did not finish in time." : e.Message);
        }
        return (process.ExitCode, output.ToArray(), stderr.Result.Trim());
    }

    private async Task<byte[]> RunOrThrowAsync(IReadOnlyList<string> args, byte[]? input, bool readOnly, CancellationToken ct)
    {
        var (code, output, error) = await RunAsync(Root, args, input, readOnly, ct).ConfigureAwait(false);
        return code == 0 ? output : throw new GitException(error.Length > 0 ? error : $"git {args[0]} ended with code {code}.");
    }

    /// <summary>The status of every path of the working tree, as <c>git status</c> shows it (untracked files one by one).</summary>
    public async Task<IReadOnlyList<StatusEntry>> StatusAsync(CancellationToken ct)
    {
        var raw = await RunOrThrowAsync(["status", "--porcelain=v1", "-z", "--untracked-files=all"], null, readOnly: true, ct).ConfigureAwait(false);
        var fields = Encoding.UTF8.GetString(raw).Split('\0');
        var list = new List<StatusEntry>();
        for (int i = 0; i < fields.Length; i++)
        {
            var f = fields[i];
            if (f.Length < 4) continue;
            char x = f[0], y = f[1];
            string? from = x is 'R' or 'C' || y is 'R' or 'C' ? (i + 1 < fields.Length ? fields[++i] : null) : null;
            list.Add(new StatusEntry(x, y, f[3..], from));
        }
        return list;
    }

    /// <summary>The current branch; the short commit when <c>HEAD</c> is detached, empty in a repository without a commit.</summary>
    public async Task<string> BranchAsync(CancellationToken ct)
    {
        var (code, output, _) = await RunAsync(Root, ["branch", "--show-current"], null, readOnly: true, ct).ConfigureAwait(false);
        var name = code == 0 ? Encoding.UTF8.GetString(output).Trim() : "";
        if (name.Length > 0) return name;
        (code, output, _) = await RunAsync(Root, ["rev-parse", "--short", "HEAD"], null, readOnly: true, ct).ConfigureAwait(false);
        return code == 0 ? Encoding.UTF8.GetString(output).Trim() : "";
    }

    /// <summary>Marks a path resolved (or stages the whole file): <c>git add</c> of what is on disk, with the filters and line ending rules of the repository.</summary>
    public async Task AddAsync(string path, CancellationToken ct) => await RunOrThrowAsync(["add", "--", path], null, readOnly: false, ct).ConfigureAwait(false);

    /// <summary>Takes a path out of the index; the file in the working tree stays.</summary>
    public async Task RemoveFromIndexAsync(string path, CancellationToken ct) =>
        await RunOrThrowAsync(["update-index", "--force-remove", "--", path], null, readOnly: false, ct).ConfigureAwait(false);

    /// <summary>Resolves a conflict by deleting the file: takes the path out of the index, every stage of it, and deletes it from the working tree.</summary>
    public async Task DeleteAsync(string path, CancellationToken ct) =>
        await RunOrThrowAsync(["rm", "-f", "--", path], null, readOnly: false, ct).ConfigureAwait(false);

    /// <summary>
    /// What kind of conflict a path is in, as the two letters <c>git status --porcelain</c> uses (<c>UU</c> both changed, <c>AA</c> added by both, <c>DU</c> deleted by us, <c>UD</c> deleted by them,
    /// <c>AU</c> added by us, <c>UA</c> added by them, <c>DD</c> deleted by both); null when the path is not in conflict. Only the first four kinds leave the file with markers, and not even always.
    /// </summary>
    public async Task<string?> ConflictCodeAsync(string path, CancellationToken ct)
    {
        var raw = await RunOrThrowAsync(["ls-files", "--unmerged", "-z", "--", path], null, readOnly: true, ct).ConfigureAwait(false);
        int stages = 0;
        foreach (var entry in Encoding.UTF8.GetString(raw).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = entry.IndexOf('\t');
            if (tab > 0 && int.TryParse(entry[..tab].Split(' ')[^1], out var stage) && stage is >= 1 and <= 3) stages |= 1 << stage;
        }
        return stages switch
        {
            0b1110 => "UU",
            0b1100 => "AA",
            0b1010 => "DU",
            0b0110 => "UD",
            0b0100 => "AU",
            0b1000 => "UA",
            0b0010 => "DD",
            _ => null,
        };
    }

    /// <summary>
    /// Whether adding the file would change its line endings: git turns the CRLF of the working tree into LF (<c>core.autocrlf</c> or an <c>eol</c> attribute) and the index copy has no CR.
    /// A path that is not in the index yet is not told apart: the caller treats it as one that does.
    /// </summary>
    public async Task<bool> CleansLineEndingsAsync(string path, CancellationToken ct)
    {
        var raw = await RunOrThrowAsync(["ls-files", "--eol", "-z", "--", path], null, readOnly: true, ct).ConfigureAwait(false);
        var text = Encoding.UTF8.GetString(raw);
        var tab = text.IndexOf('\t');
        if (tab < 0) return false;
        var fields = text[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (fields.Contains("w/crlf") || fields.Contains("w/mixed")) && fields.Contains("i/lf");
    }

    /// <summary>The index entry of a path; null when the path is not in the index.</summary>
    public async Task<IndexEntry?> IndexEntryAsync(string path, CancellationToken ct)
    {
        var raw = await RunOrThrowAsync(["ls-files", "--stage", "-z", "--", path], null, readOnly: true, ct).ConfigureAwait(false);
        var text = Encoding.UTF8.GetString(raw);
        var tab = text.IndexOf('\t');
        if (tab < 0) return null;
        var parts = text[..tab].Split(' ');
        return new IndexEntry(parts[0], parts[1], int.Parse(parts[2]));
    }

    /// <summary>The blob of a path in <c>HEAD</c>, or null when there is none (a new file, or a repository without a commit).</summary>
    public async Task<string?> HeadBlobAsync(string path, CancellationToken ct)
    {
        var (code, output, _) = await RunAsync(Root, ["ls-tree", "-z", "HEAD", "--", path], null, readOnly: true, ct).ConfigureAwait(false);
        if (code != 0) return null;
        var text = Encoding.UTF8.GetString(output);
        var tab = text.IndexOf('\t');
        return tab < 0 ? null : text[..tab].Split(' ')[2];
    }

    /// <summary>The bytes of a blob, as stored (no line ending or other conversion).</summary>
    public Task<byte[]> ReadBlobAsync(string sha, CancellationToken ct) => RunOrThrowAsync(["cat-file", "blob", sha], null, readOnly: true, ct);

    /// <summary>
    /// Puts the content in the index as the file at <paramref name="path"/>: stores the blob and points the index entry at it. The one place the plugin writes to the repository.
    /// With <paramref name="clean"/> the content goes through the conversions <c>git add</c> would apply to that path (line endings, filters), so a working tree with CRLF does not put CRLF in the blob.
    /// Returns the new blob.
    /// </summary>
    public async Task<string> WriteIndexBlobAsync(string path, string mode, byte[] content, bool clean, CancellationToken ct)
    {
        IReadOnlyList<string> hash = clean ? ["hash-object", "-w", $"--path={path}", "--stdin"] : ["hash-object", "-w", "--stdin"];
        var sha = Encoding.ASCII.GetString(await RunOrThrowAsync(hash, content, readOnly: false, ct).ConfigureAwait(false)).Trim();
        await RunOrThrowAsync(["update-index", "--add", "--cacheinfo", $"{mode},{sha},{path}"], null, readOnly: false, ct).ConfigureAwait(false);
        return sha;
    }
}
