using System.Diagnostics;
using System.Text;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Plugins.Git;

/// <summary>A git command ended with a non-zero code, or output was larger than the plugin reads.</summary>
public sealed class GitException(string message) : Exception(message);

/// <summary>One line of <c>git status</c>: <paramref name="Index"/> and <paramref name="Tree"/> are the two status letters (space = unchanged, <c>?</c> = untracked).</summary>
public sealed record StatusEntry(char Index, char Tree, string Path, string? From);

/// <summary>An entry of the index: the file mode, the blob and the merge stage (0 unless the path is in conflict).</summary>
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

    private GitRepository(string git, string root, string prefix, int timeoutMs, long maxBytes) =>
        (_git, Root, Prefix, _timeoutMs, _maxBytes) = (git, root, prefix, timeoutMs, maxBytes);

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
        var probe = new GitRepository(git, folder, "", timeoutMs, maxBytes);
        var (code, output, _) = await probe.RunAsync(folder, ["rev-parse", "--show-toplevel", "--show-prefix"], null, readOnly: true, ct);
        if (code != 0) return null;
        var lines = Encoding.UTF8.GetString(output).Split('\n');
        return new GitRepository(git, lines[0].TrimEnd('\r'), lines.Length > 1 ? lines[1].TrimEnd('\r') : "", timeoutMs, maxBytes);
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
    /// Returns the new blob.
    /// </summary>
    public async Task<string> WriteIndexBlobAsync(string path, string mode, byte[] content, CancellationToken ct)
    {
        var sha = Encoding.ASCII.GetString(await RunOrThrowAsync(["hash-object", "-w", "--stdin"], content, readOnly: false, ct).ConfigureAwait(false)).Trim();
        await RunOrThrowAsync(["update-index", "--add", "--cacheinfo", $"{mode},{sha},{path}"], null, readOnly: false, ct).ConfigureAwait(false);
        return sha;
    }
}
