using System.Security.Cryptography;
using System.Text;

namespace NksHub.Textida.Core.Compare;

/// <summary>A file of a folder, by its path relative to the folder (with <c>/</c>), its size and its modification time (null when the side cannot say).</summary>
public sealed record FolderFile(string Path, long Size, DateTime? ModifiedUtc);

/// <summary>One side of a folder comparison: a local folder, or a folder on a server. The code that compares and copies knows nothing else about it.</summary>
public interface IFolderSource
{
    /// <summary>The name shown for the side (the path of the folder, or the connection and path).</summary>
    string Label { get; }

    /// <summary>Every file below the folder, folders themselves left out.</summary>
    Task<IReadOnlyList<FolderFile>> ListAsync(CancellationToken ct);

    /// <summary>Opens a file for reading; the stream is the caller's to dispose.</summary>
    Task<Stream> OpenReadAsync(string path, CancellationToken ct);

    /// <summary>Puts the content of the local file <paramref name="source"/> at <paramref name="path"/>, replacing a file that is there, so that a reader never sees half of it. Throws <see cref="IOException"/> when it cannot.</summary>
    Task WriteFromFileAsync(string path, string source, DateTime? modifiedUtc, CancellationToken ct);
}

public enum FolderCompareBy { Size, Time, Content }

public enum FolderState { Same, Different, OnlyLeft, OnlyRight }

public sealed record FolderDifference(string Path, FolderState State, FolderFile? Left, FolderFile? Right);

public enum CopyDirection { LeftToRight, RightToLeft }

/// <summary>A copy the preview shows: a file that is new on the other side (<see cref="Overwrites"/> false) or replaces a different one.</summary>
public sealed record FolderCopyStep(string Path, bool Overwrites, long Bytes);

/// <summary>What a copy did: the files it copied and the ones it could not, with the reason.</summary>
public sealed record FolderCopyResult(int Copied, IReadOnlyList<(string Path, string Reason)> Failed);

/// <summary>A local folder as a side of a comparison.</summary>
public sealed class LocalFolderSource(string root) : IFolderSource
{
    public string Label => root;

    public Task<IReadOnlyList<FolderFile>> ListAsync(CancellationToken ct) => Task.Run<IReadOnlyList<FolderFile>>(() =>
    {
        var result = new List<FolderFile>();
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, ReturnSpecialDirectories = false };
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", options))
        {
            ct.ThrowIfCancellationRequested();
            result.Add(new FolderFile(Path.GetRelativePath(root, file.FullName).Replace('\\', '/'), file.Length, file.LastWriteTimeUtc));
        }
        return result;
    }, ct);

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct) =>
        Task.FromResult<Stream>(new FileStream(Resolve(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true));

    public async Task WriteFromFileAsync(string path, string source, DateTime? modifiedUtc, CancellationToken ct)
    {
        string target = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temp = target + ".textida-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            await using (var from = File.OpenRead(source))
            await using (var to = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await from.CopyToAsync(to, ct).ConfigureAwait(false);
            if (modifiedUtc is { } time) File.SetLastWriteTimeUtc(temp, time);
            File.Move(temp, target, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    /// <summary>The full path of a relative path, which must stay inside the folder.</summary>
    private string Resolve(string path)
    {
        string full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        string folder = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(folder, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)) throw new IOException("The path leaves the folder: " + path);
        return full;
    }
}

/// <summary>Compares two folders and copies from one to the other, never deleting anything.</summary>
public static class FolderCompare
{
    /// <summary>Two modification times closer than this are the same time (FAT keeps two seconds, FTP one).</summary>
    public static readonly TimeSpan TimeTolerance = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Every path either side has, with its state. <paramref name="by"/> says what counts as a difference for a path both have: the size; the modification time (a side that does not know it is compared by size);
    /// or the content (equal size first, then the hash of both files, read in full).
    /// </summary>
    public static async Task<IReadOnlyList<FolderDifference>> CompareAsync(IFolderSource left, IFolderSource right, FolderCompareBy by, CancellationToken ct)
    {
        var leftFiles = (await left.ListAsync(ct).ConfigureAwait(false)).ToDictionary(f => f.Path, StringComparer.Ordinal);
        var rightFiles = (await right.ListAsync(ct).ConfigureAwait(false)).ToDictionary(f => f.Path, StringComparer.Ordinal);
        var result = new List<FolderDifference>();
        foreach (var path in leftFiles.Keys.Union(rightFiles.Keys).Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            leftFiles.TryGetValue(path, out var l);
            rightFiles.TryGetValue(path, out var r);
            if (l is null) result.Add(new(path, FolderState.OnlyRight, null, r));
            else if (r is null) result.Add(new(path, FolderState.OnlyLeft, l, null));
            else result.Add(new(path, await SameAsync(left, right, l, r, by, ct).ConfigureAwait(false) ? FolderState.Same : FolderState.Different, l, r));
        }
        return result;
    }

    private static async Task<bool> SameAsync(IFolderSource left, IFolderSource right, FolderFile l, FolderFile r, FolderCompareBy by, CancellationToken ct)
    {
        switch (by)
        {
            case FolderCompareBy.Size:
                return l.Size == r.Size;
            case FolderCompareBy.Time:
                return l.ModifiedUtc is { } a && r.ModifiedUtc is { } b ? (a - b).Duration() <= TimeTolerance : l.Size == r.Size;
            default:
                if (l.Size != r.Size) return false;
                return await HashAsync(left, l.Path, ct).ConfigureAwait(false) == await HashAsync(right, r.Path, ct).ConfigureAwait(false);
        }
    }

    private static async Task<string> HashAsync(IFolderSource side, string path, CancellationToken ct)
    {
        await using var stream = await side.OpenReadAsync(path, ct).ConfigureAwait(false);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
    }

    /// <summary>The copies that would make the other side have what this side has: the files only this side has (new) and the ones that differ (replacing). Nothing is deleted, files only the other side has are left.</summary>
    public static IReadOnlyList<FolderCopyStep> PlanCopy(IReadOnlyList<FolderDifference> differences, CopyDirection direction)
    {
        var steps = new List<FolderCopyStep>();
        foreach (var d in differences)
        {
            var source = direction == CopyDirection.LeftToRight ? d.Left : d.Right;
            bool onlyHere = direction == CopyDirection.LeftToRight ? d.State == FolderState.OnlyLeft : d.State == FolderState.OnlyRight;
            if (source is null) continue;
            if (onlyHere) steps.Add(new(d.Path, false, source.Size));
            else if (d.State == FolderState.Different) steps.Add(new(d.Path, true, source.Size));
        }
        return steps;
    }

    /// <summary>
    /// Carries out the steps: each file is read from <paramref name="from"/> into a temporary local file and written to <paramref name="to"/> from there, with its modification time. A file that cannot be copied
    /// is recorded and the rest goes on. <paramref name="modified"/> says the time of the source file of a path.
    /// </summary>
    public static async Task<FolderCopyResult> CopyAsync(IFolderSource from, IFolderSource to, IReadOnlyList<FolderCopyStep> steps, Func<string, DateTime?> modified, Action<FolderCopyStep>? onStep, CancellationToken ct)
    {
        int copied = 0;
        var failed = new List<(string, string)>();
        string temp = Path.Combine(Path.GetTempPath(), "textida-foldercopy-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(temp);
        try
        {
            foreach (var step in steps)
            {
                ct.ThrowIfCancellationRequested();
                onStep?.Invoke(step);
                string local = Path.Combine(temp, "file");
                try
                {
                    await using (var source = await from.OpenReadAsync(step.Path, ct).ConfigureAwait(false))
                    await using (var target = new FileStream(local, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                        await source.CopyToAsync(target, ct).ConfigureAwait(false);
                    await to.WriteFromFileAsync(step.Path, local, modified(step.Path), ct).ConfigureAwait(false);
                    copied++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    failed.Add((step.Path, e.Message));
                }
            }
        }
        finally { try { Directory.Delete(temp, true); } catch (IOException) { } }
        return new FolderCopyResult(copied, failed);
    }

    private static string Stamp(DateTime? time) => time is { } t ? t.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) : "?";

    /// <summary>The report of a comparison as text: the two sides, what was compared, the counts and one line per file that is not the same (<c>&lt;</c> only left, <c>&gt;</c> only right, <c>!</c> different).</summary>
    public static string Report(IFolderSource left, IFolderSource right, FolderCompareBy by, IReadOnlyList<FolderDifference> differences, string newLine, Func<FolderCompareBy, string> byName)
    {
        var sb = new StringBuilder();
        sb.Append("< ").Append(left.Label).Append(newLine).Append("> ").Append(right.Label).Append(newLine).Append(byName(by)).Append(newLine).Append(newLine);
        foreach (var d in differences.Where(d => d.State != FolderState.Same))
        {
            string mark = d.State switch { FolderState.OnlyLeft => "<", FolderState.OnlyRight => ">", _ => "!" };
            sb.Append(mark).Append(' ').Append(d.Path);
            if (d.Left is { } l && d.Right is { } r) sb.Append("  (").Append(l.Size).Append(" B ").Append(Stamp(l.ModifiedUtc)).Append(" / ").Append(r.Size).Append(" B ").Append(Stamp(r.ModifiedUtc)).Append(')');
            sb.Append(newLine);
        }
        return sb.ToString();
    }
}
