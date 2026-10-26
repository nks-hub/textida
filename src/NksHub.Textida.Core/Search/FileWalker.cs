using System.IO.Enumeration;

namespace NksHub.Textida.Core.Search;

/// <summary>What the file walk includes. Defaults follow what a developer expects from a code search.</summary>
public sealed record FileWalkOptions
{
    /// <summary>Apply the ignore files named in <see cref="IgnoreFileNames"/>, also outside a git repository.</summary>
    public bool UseIgnoreFiles { get; init; } = true;

    /// <summary>Ignore files read in every directory, lowest precedence first (a later file overrides an earlier one in the same directory).</summary>
    public IReadOnlyList<string> IgnoreFileNames { get; init; } = [".gitignore", ".ignore"];

    /// <summary>Patterns in gitignore syntax relative to the walk root with the lowest precedence of all; for rules an agent or a plugin adds.</summary>
    public IReadOnlyList<string> ExtraIgnorePatterns { get; init; } = [];

    /// <summary>Directory names that are never entered, even when a negation would re-include them.</summary>
    public IReadOnlyList<string> AlwaysSkipDirectories { get; init; } = [".git", ".codegraph"];

    /// <summary>Include names starting with a dot and, on Windows, files and directories with the hidden attribute.</summary>
    public bool IncludeHidden { get; init; }

    /// <summary>Follow symbolic links and junctions. A link back into a directory that is being walked is skipped as a loop.</summary>
    public bool FollowLinks { get; init; }

    /// <summary>Files larger than this are left out; larger files belong to the paged search.</summary>
    public long MaxFileSize { get; init; } = 256L << 20;

    /// <summary>Match ignore patterns without regard to case (git's core.ignorecase).</summary>
    public bool IgnoreCase { get; init; }
}

/// <summary>Why entries were left out of a walk; filled while the walk runs.</summary>
public sealed class FileWalkStats
{
    private long _ignored, _hidden, _tooLarge, _links, _loops, _unreadable;

    public long Ignored => Interlocked.Read(ref _ignored);
    public long Hidden => Interlocked.Read(ref _hidden);
    public long TooLarge => Interlocked.Read(ref _tooLarge);
    /// <summary>Symbolic links skipped because links are not followed or the target is missing.</summary>
    public long Links => Interlocked.Read(ref _links);
    public long LinkLoops => Interlocked.Read(ref _loops);
    public long Unreadable => Interlocked.Read(ref _unreadable);

    internal void AddIgnored() => Interlocked.Increment(ref _ignored);
    internal void AddHidden() => Interlocked.Increment(ref _hidden);
    internal void AddTooLarge() => Interlocked.Increment(ref _tooLarge);
    internal void AddLink() => Interlocked.Increment(ref _links);
    internal void AddLoop() => Interlocked.Increment(ref _loops);
    internal void AddUnreadable() => Interlocked.Increment(ref _unreadable);
}

public readonly record struct FileEntry(string Path, long Length, DateTime LastWriteUtc);

/// <summary>
/// Walks a directory tree in a stable order (per directory: files by name, then subdirectories by name) and leaves out what the
/// ignore rules, the hidden-file option, the size limit and the link rules exclude. Ignore files apply from the directory they
/// sit in downwards; a deeper file overrides a shallower one. The global git excludes and <c>.git/info/exclude</c> are not read,
/// and the rules apply whether or not the tree is a git repository.
/// </summary>
public static class FileWalker
{
    private readonly record struct Item(string Name, bool IsDirectory, FileAttributes Attributes, long Length, DateTime LastWriteUtc);

    private sealed record Level(int PrefixLength, IgnoreRules[] Sets);

    private sealed record Frame(string Dir, string Relative, Level[] Levels, HashSet<string> Ancestors);

    public static IEnumerable<FileEntry> Walk(string root, FileWalkOptions? options = null, FileWalkStats? stats = null, CancellationToken ct = default)
    {
        options ??= new();
        stats ??= new();
        root = Path.GetFullPath(root);
        var comparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var skipDirs = new HashSet<string>(options.AlwaysSkipDirectories, comparer);

        var baseLevels = new List<Level>();
        if (options.UseIgnoreFiles && options.ExtraIgnorePatterns.Count > 0)
            baseLevels.Add(new Level(0, [IgnoreRules.Parse(options.ExtraIgnorePatterns, options.IgnoreCase)]));

        var stack = new Stack<Frame>();
        stack.Push(new Frame(root, "", [.. baseLevels], new HashSet<string>(comparer) { root }));
        var enumOptions = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = false, ReturnSpecialDirectories = false };

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var frame = stack.Pop();
            Item[] items;
            try
            {
                items = [.. new FileSystemEnumerable<Item>(frame.Dir,
                    (ref FileSystemEntry e) => new Item(e.FileName.ToString(), e.IsDirectory, e.Attributes, e.IsDirectory ? 0 : e.Length, e.LastWriteTimeUtc.UtcDateTime), enumOptions)];
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                stats.AddUnreadable();
                continue;
            }
            Array.Sort(items, (a, b) => string.CompareOrdinal(a.Name, b.Name));

            var levels = frame.Levels;
            if (options.UseIgnoreFiles && LoadLevel(frame, items, options) is { } level) levels = [.. levels, level];

            var subdirs = new List<Frame>();
            foreach (var item in items)
            {
                if (!Include(frame, item, levels, options, skipDirs, stats, out var target, out bool isDir)) continue;
                string full = Path.Combine(frame.Dir, item.Name);
                if (isDir)
                {
                    var ancestors = frame.Ancestors;
                    if (target is not null)
                    {
                        if (ancestors.Contains(target.FullName)) { stats.AddLoop(); continue; }
                        ancestors = new HashSet<string>(ancestors, comparer) { target.FullName };
                    }
                    else if (options.FollowLinks) ancestors = new HashSet<string>(ancestors, comparer) { full };
                    subdirs.Add(new Frame(full, frame.Relative + item.Name + "/", levels, ancestors));
                }
                else
                {
                    long length = target is FileInfo fi ? fi.Length : item.Length;
                    if (length > options.MaxFileSize) { stats.AddTooLarge(); continue; }
                    yield return new FileEntry(full, length, target is FileInfo f2 ? f2.LastWriteTimeUtc : item.LastWriteUtc);
                }
            }
            for (int i = subdirs.Count - 1; i >= 0; i--) stack.Push(subdirs[i]);
        }
    }

    private static bool IsLink(Item item) => (item.Attributes & FileAttributes.ReparsePoint) != 0;

    private static Level? LoadLevel(Frame frame, Item[] items, FileWalkOptions options)
    {
        List<IgnoreRules>? sets = null;
        foreach (var name in options.IgnoreFileNames)
        {
            var found = Array.Find(items, i => !i.IsDirectory && string.Equals(i.Name, name, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            if (found.Name is null) continue;
            if (IgnoreRules.TryLoad(Path.Combine(frame.Dir, found.Name), options.IgnoreCase) is { Count: > 0 } rules) (sets ??= []).Add(rules);
        }
        return sets is null ? null : new Level(frame.Relative.Length, [.. sets]);
    }

    /// <summary>Decides one entry; resolves links when they are followed (<paramref name="target"/> is then the link target).</summary>
    private static bool Include(Frame frame, Item item, Level[] levels, FileWalkOptions options, HashSet<string> skipDirs, FileWalkStats stats,
        out FileSystemInfo? target, out bool isDirectory)
    {
        target = null;
        isDirectory = item.IsDirectory;
        if (item.IsDirectory && skipDirs.Contains(item.Name)) return false;

        if (!options.IncludeHidden && (item.Name.StartsWith('.') || (item.Attributes & FileAttributes.Hidden) != 0))
        {
            stats.AddHidden();
            return false;
        }

        if (IsLink(item))
        {
            var link = ResolveLink(Path.Combine(frame.Dir, item.Name), item.IsDirectory, options.FollowLinks, out target);
            if (link == LinkState.Skipped) { stats.AddLink(); return false; }
            if (link == LinkState.Followed) isDirectory = target is DirectoryInfo;
        }

        if (levels.Length > 0 && IsIgnored(frame, item.Name, isDirectory, levels))
        {
            stats.AddIgnored();
            return false;
        }
        return true;
    }

    private enum LinkState { NotALink, Skipped, Followed }

    /// <summary>A reparse point that is not a link (cloud placeholder, dedup) is an ordinary entry.</summary>
    private static LinkState ResolveLink(string path, bool isDirectory, bool follow, out FileSystemInfo? target)
    {
        target = null;
        FileSystemInfo info = isDirectory ? new DirectoryInfo(path) : new FileInfo(path);
        try
        {
            if (info.LinkTarget is null) return LinkState.NotALink;
            if (!follow) return LinkState.Skipped;
            target = info.ResolveLinkTarget(returnFinalTarget: true);
        }
        catch (IOException)
        {
            return LinkState.Skipped;
        }
        return target is { Exists: true } ? LinkState.Followed : LinkState.Skipped;
    }

    private static bool IsIgnored(Frame frame, string name, bool isDirectory, Level[] levels)
    {
        string rel = frame.Relative + name;
        bool? verdict = null;
        foreach (var level in levels)
        {
            string local = rel[level.PrefixLength..];
            foreach (var set in level.Sets) verdict = set.Evaluate(local, isDirectory, verdict);
        }
        return verdict == true;
    }
}
