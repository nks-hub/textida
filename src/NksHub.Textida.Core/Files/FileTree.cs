using System.IO.Enumeration;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Files;

/// <param name="Hidden">The name starts with a dot, or (Windows) the entry has the hidden attribute.</param>
public readonly record struct DirectoryEntry(string Name, string Path, bool IsDirectory, long Length, bool Hidden = false);

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
    private readonly List<FileSystemWatcher> _watchers = [];
    private Timer? _debounce;
    private Timer? _contentTimer;
    private int _contentPending;
    private bool _showHidden = true;
    private readonly HashSet<string> _dirty = new(PathComparer);

    /// <summary>Quiet time after the first change of a file before <see cref="ContentChanged"/> is raised.</summary>
    private const int ContentDelayMilliseconds = 400;

    /// <param name="root">The first folder; with <paramref name="more"/> the tree shows every folder as a top row (a project of several folders).</param>
    public FileTree(string root, IEnumerable<string>? more = null)
    {
        var roots = new List<string>();
        foreach (var r in new[] { root }.Concat(more ?? []).Select(r => Path.TrimEndingDirectorySeparator(Path.GetFullPath(r))))
            if (!roots.Contains(r, PathComparer)) roots.Add(r);
        Roots = roots;
    }

    public string Root => Roots[0];

    public IReadOnlyList<string> Roots { get; }

    /// <summary>True when <paramref name="path"/> is one of the folders of the tree or below one.</summary>
    public bool Contains(string path) => RootOf(path) is not null;

    private string? RootOf(string path) => Roots.FirstOrDefault(r => PathMove.IsInside(path, r));

    public IReadOnlyList<FileTreeRow> Rows => _rows;

    /// <summary>Whether hidden entries (<see cref="DirectoryEntry.Hidden"/>) have a row. Setting it rebuilds the rows.</summary>
    public bool ShowHidden
    {
        get => _showHidden;
        set
        {
            if (_showHidden == value) return;
            _showHidden = value;
            if (_children.Count > 0) Rebuild();
        }
    }

    /// <summary>Raised on the UI thread after <see cref="Rows"/> changed.</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised on a pool thread, at most once per <see cref="ContentDelayMilliseconds"/>, after a file below a folder of the tree was written, created, deleted or renamed.
    /// It carries nothing: it tells whoever reads the state of the files (Git) to look again. Nothing is watched for it while nobody listens.
    /// </summary>
    public event Action? ContentChanged;

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
        foreach (var root in Roots)
        {
            // only the first folder must be readable; a missing folder of a project shows as empty
            _children[root] = await Task.Run(() => List(root, throwIfUnreadable: root == Root), ct);
            _expanded.Add(root);
        }
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
        if ((Roots.Count > 1 || !PathComparer.Equals(directory, Root)) && _expanded.Remove(directory)) Rebuild();
    }

    /// <summary>Opens every folder from the root down to <paramref name="path"/> so its row exists. False when the path is not below the root.</summary>
    public async Task<bool> RevealAsync(string path, CancellationToken ct = default)
    {
        path = Path.GetFullPath(path);
        if (RootOf(path) is not { } root) return false;
        string relative = Path.GetRelativePath(root, path);
        string current = root;
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

    /// <summary>Starts watching the folders for changes made by other programs.</summary>
    public void Watch()
    {
        if (_watchers.Count > 0) return;
        _debounce = new Timer(_ => Flush());
        _contentTimer = new Timer(_ =>
        {
            Interlocked.Exchange(ref _contentPending, 0);
            ContentChanged?.Invoke();
        });
        foreach (var root in Roots.Where(Directory.Exists)) WatchFolder(root);
    }

    private void WatchFolder(string root)
    {
        FileSystemWatcher watcher;
        try
        {
            watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                InternalBufferSize = 64 * 1024,
            };
        }
        catch (Exception e) when (e is ArgumentException or IOException or PlatformNotSupportedException)
        {
            return;
        }
        void Poke(string? path)
        {
            lock (_dirty) _dirty.Add(path is null ? root : Path.GetDirectoryName(path) ?? root);
            _debounce!.Change(200, Timeout.Infinite);
            PokeContent();
        }
        void PokeContent()
        {
            if (ContentChanged is null || Interlocked.Exchange(ref _contentPending, 1) == 1) return;
            try { _contentTimer!.Change(ContentDelayMilliseconds, Timeout.Infinite); }
            catch (ObjectDisposedException) { /* the tree ended while an event was on its way */ }
        }
        watcher.Created += (_, e) => Poke(e.FullPath);
        watcher.Deleted += (_, e) => Poke(e.FullPath);
        watcher.Renamed += (_, e) => { Poke(e.OldFullPath); Poke(e.FullPath); };
        watcher.Changed += (_, _) => PokeContent(); // the names did not change: no listing to read again
        watcher.Error += (_, _) => Poke(null);
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
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
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
        _debounce?.Dispose();
        _contentTimer?.Dispose();
    }

    private void Rebuild()
    {
        var rows = new List<FileTreeRow>();
        if (Roots.Count == 1) Add(Root, 0);
        else
            foreach (var root in Roots)
            {
                bool open = _expanded.Contains(root);
                rows.Add(new FileTreeRow(new DirectoryEntry(Path.GetFileName(root) is { Length: > 0 } name ? name : root, root, true, 0), 0, open));
                if (open) Add(root, 1);
            }
        _rows = rows;
        Changed?.Invoke();

        void Add(string directory, int depth)
        {
            if (!_children.TryGetValue(directory, out var entries)) return;
            foreach (var entry in entries)
            {
                if (entry.Hidden && !_showHidden) continue;
                bool open = entry.IsDirectory && _expanded.Contains(entry.Path);
                rows.Add(new FileTreeRow(entry, depth, open));
                if (open) Add(entry.Path, depth + 1);
            }
        }
    }

    private static bool IsHidden(ref FileSystemEntry entry) =>
        (entry.FileName.Length > 0 && entry.FileName[0] == '.') || (OperatingSystem.IsWindows() && (entry.Attributes & FileAttributes.Hidden) != 0);

    /// <summary>The entries of one folder: folders first, then files, each by name without regard to case.</summary>
    public static DirectoryEntry[] List(string directory, bool throwIfUnreadable)
    {
        try
        {
            var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = false, ReturnSpecialDirectories = false };
            var entries = new FileSystemEnumerable<DirectoryEntry>(directory,
                (ref FileSystemEntry e) => new DirectoryEntry(e.FileName.ToString(), e.ToFullPath(), e.IsDirectory, e.IsDirectory ? 0 : e.Length, IsHidden(ref e)), options).ToArray();
            Array.Sort(entries, (a, b) => a.IsDirectory != b.IsDirectory ? (a.IsDirectory ? -1 : 1) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return entries;
        }
        catch (Exception e) when (!throwIfUnreadable && e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
