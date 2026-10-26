using System.IO.Enumeration;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Files;

public readonly record struct DirectoryEntry(string Name, string Path, bool IsDirectory, long Length);

/// <param name="Depth">0 for the entries of the root folder.</param>
public readonly record struct FileTreeRow(DirectoryEntry Entry, int Depth, bool Expanded);

/// <summary>
/// The folder tree of the file panel as a flat list of the visible rows. A folder is read from disk only when it is opened, off the calling
/// thread, so a folder with a hundred thousand files costs one listing and not a walk of everything below. Folders opened earlier keep their
/// listing until a change on disk or <see cref="RefreshAsync"/> reads them again. All members except <see cref="DiskChanged"/> belong to one thread (the UI).
/// </summary>
public sealed class FileTree : IDisposable
{
    private static StringComparer PathComparer => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly Dictionary<string, DirectoryEntry[]> _children = new(PathComparer);
    private readonly HashSet<string> _expanded = new(PathComparer);
    private List<FileTreeRow> _rows = [];
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private readonly HashSet<string> _dirty = new(PathComparer);

    public FileTree(string root) => Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

    public string Root { get; }

    public IReadOnlyList<FileTreeRow> Rows => _rows;

    /// <summary>Raised on the UI thread after <see cref="Rows"/> changed.</summary>
    public event Action? Changed;

    /// <summary>Raised on a pool thread, shortly after something changed on disk, with the folders whose listing is probably stale. Call <see cref="RefreshAsync"/> from the UI thread.</summary>
    public event Action<IReadOnlyCollection<string>>? DiskChanged;

    public bool IsExpanded(string path) => _expanded.Contains(path);

    public bool IsLoaded(string directory) => _children.ContainsKey(directory);

    public int IndexOf(string path)
    {
        for (int i = 0; i < _rows.Count; i++)
            if (PathComparer.Equals(_rows[i].Entry.Path, path)) return i;
        return -1;
    }

    /// <summary>Reads the root folder; throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when it cannot be read.</summary>
    public async Task LoadRootAsync(CancellationToken ct = default)
    {
        _children[Root] = await Task.Run(() => List(Root, throwIfUnreadable: true), ct);
        _expanded.Add(Root);
        Rebuild();
    }

    public async Task ExpandAsync(string directory, CancellationToken ct = default)
    {
        if (!_children.ContainsKey(directory)) _children[directory] = await Task.Run(() => List(directory, throwIfUnreadable: false), ct);
        _expanded.Add(directory);
        Rebuild();
    }

    public void Collapse(string directory)
    {
        if (!PathComparer.Equals(directory, Root) && _expanded.Remove(directory)) Rebuild();
    }

    /// <summary>Opens every folder from the root down to <paramref name="path"/> so its row exists. False when the path is not below the root.</summary>
    public async Task<bool> RevealAsync(string path, CancellationToken ct = default)
    {
        path = Path.GetFullPath(path);
        if (!PathMove.IsInside(path, Root)) return false;
        string relative = Path.GetRelativePath(Root, path);
        string current = Root;
        var parts = relative == "." ? [] : relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var part in parts.Take(Math.Max(0, parts.Length - 1)))
        {
            current = Path.Combine(current, part);
            await ExpandAsync(current, ct);
        }
        return true;
    }

    /// <summary>Reads a folder again (one that is not loaded is left alone) and keeps the opened folders below it opened.</summary>
    public async Task RefreshAsync(string directory, CancellationToken ct = default)
    {
        if (!_children.ContainsKey(directory)) return;
        var fresh = await Task.Run(() => List(directory, throwIfUnreadable: false), ct);
        _children[directory] = fresh;
        foreach (var gone in _expanded.Where(e => !PathComparer.Equals(e, directory) && PathMove.IsInside(e, directory) && !Directory.Exists(e)).ToList())
        {
            _expanded.Remove(gone);
            _children.Remove(gone);
        }
        Rebuild();
    }

    public async Task RefreshAllAsync(CancellationToken ct = default)
    {
        foreach (var directory in _children.Keys.Where(Directory.Exists).ToList()) await RefreshAsync(directory, ct);
        foreach (var gone in _children.Keys.Where(d => !Directory.Exists(d)).ToList())
        {
            _children.Remove(gone);
            _expanded.Remove(gone);
        }
        Rebuild();
    }

    /// <summary>Starts watching the root for changes made by other programs.</summary>
    public void Watch()
    {
        if (_watcher is not null || !Directory.Exists(Root)) return;
        _debounce = new Timer(_ => Flush());
        try
        {
            _watcher = new FileSystemWatcher(Root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
        }
        catch (Exception e) when (e is ArgumentException or IOException or PlatformNotSupportedException)
        {
            return;
        }
        void Poke(string? path)
        {
            lock (_dirty) _dirty.Add(path is null ? Root : Path.GetDirectoryName(path) ?? Root);
            _debounce.Change(200, Timeout.Infinite);
        }
        _watcher.Created += (_, e) => Poke(e.FullPath);
        _watcher.Deleted += (_, e) => Poke(e.FullPath);
        _watcher.Renamed += (_, e) => { Poke(e.OldFullPath); Poke(e.FullPath); };
        _watcher.Error += (_, _) => Poke(null);
        _watcher.EnableRaisingEvents = true;
    }

    private void Flush()
    {
        string[] dirs;
        lock (_dirty)
        {
            dirs = [.. _dirty];
            _dirty.Clear();
        }
        if (dirs.Length > 0) DiskChanged?.Invoke(dirs);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
        _watcher = null;
    }

    private void Rebuild()
    {
        var rows = new List<FileTreeRow>();
        Add(Root, 0);
        _rows = rows;
        Changed?.Invoke();

        void Add(string directory, int depth)
        {
            if (!_children.TryGetValue(directory, out var entries)) return;
            foreach (var entry in entries)
            {
                bool open = entry.IsDirectory && _expanded.Contains(entry.Path);
                rows.Add(new FileTreeRow(entry, depth, open));
                if (open) Add(entry.Path, depth + 1);
            }
        }
    }

    /// <summary>The entries of one folder: folders first, then files, each by name without regard to case.</summary>
    public static DirectoryEntry[] List(string directory, bool throwIfUnreadable)
    {
        try
        {
            var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = false, ReturnSpecialDirectories = false };
            var entries = new FileSystemEnumerable<DirectoryEntry>(directory,
                (ref FileSystemEntry e) => new DirectoryEntry(e.FileName.ToString(), e.ToFullPath(), e.IsDirectory, e.IsDirectory ? 0 : e.Length), options).ToArray();
            Array.Sort(entries, (a, b) => a.IsDirectory != b.IsDirectory ? (a.IsDirectory ? -1 : 1) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return entries;
        }
        catch (Exception e) when (!throwIfUnreadable && e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
