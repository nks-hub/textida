namespace NksHub.Textida.Core.Changes;

/// <summary>The files a watched folder reported since the last batch. <paramref name="Dropped"/> counts the reports of changes that did not fit under the limit (a file can report more than once) and are not in the list.</summary>
public sealed record FolderChangeBatch(IReadOnlyList<string> Files, int Dropped);

/// <summary>
/// Watches folders (with everything below them) for files that another program wrote or created (P6.1.1). A burst of events becomes one <see cref="FolderChangeBatch"/> after the folder has been quiet
/// for the delay. What <see cref="ReviewIgnore"/> leaves out never reaches the batch, a path that is not a file any more is dropped, and a batch holds at most the given number of files, so a build
/// that writes thousands of files costs a counter and not memory.
/// </summary>
public sealed class FolderChangeWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly ReviewIgnore[] _filters;
    private readonly TimeSpan _delay;
    private readonly int _maxFiles;
    private readonly Dictionary<string, int> _pending;
    private readonly object _gate = new();
    private Timer? _timer;
    private int _dropped;
    private bool _disposed;

    public FolderChangeWatcher(IEnumerable<string> roots, TimeSpan delay, int maxFiles)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFiles, 1);
        _filters = [.. roots.Select(r => new ReviewIgnore(r)).DistinctBy(f => f.Root)];
        _delay = delay;
        _maxFiles = maxFiles;
        _pending = new(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    /// <summary>Raised on a pool thread, never with an empty batch.</summary>
    public event Action<FolderChangeBatch>? Changed;

    /// <summary>Starts watching the folders that exist. Does nothing when it already runs.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _timer is not null) return;
            _timer = new Timer(_ => Flush());
            for (int i = 0; i < _filters.Length; i++)
                if (Directory.Exists(_filters[i].Root)) Watch(i);
        }
    }

    private void Watch(int index)
    {
        FileSystemWatcher watcher;
        try
        {
            watcher = new FileSystemWatcher(_filters[index].Root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                InternalBufferSize = 64 * 1024,
            };
        }
        catch (Exception e) when (e is ArgumentException or IOException or PlatformNotSupportedException)
        {
            return;
        }
        watcher.Changed += (_, e) => Poke(e.FullPath, index);
        watcher.Created += (_, e) => Poke(e.FullPath, index);
        watcher.Renamed += (_, e) => Poke(e.FullPath, index);
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
    }

    private void Poke(string path, int index)
    {
        // the ignore check is cheap and keeps a noisy folder that is left out (a build output) from filling the limit with files nobody wants
        try { if (_filters[index].IsIgnored(path)) return; }
        catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException) { return; }
        lock (_gate)
        {
            if (_disposed) return;
            if (!_pending.ContainsKey(path))
            {
                if (_pending.Count >= _maxFiles) _dropped++;
                else _pending[path] = index;
            }
            _timer?.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Flush()
    {
        Dictionary<string, int> batch;
        int dropped;
        lock (_gate)
        {
            if (_disposed) return;
            batch = new(_pending, _pending.Comparer);
            dropped = _dropped;
            _pending.Clear();
            _dropped = 0;
        }
        foreach (var filter in _filters) filter.Refresh(); // an ignore file that changed meanwhile applies to this batch
        var files = batch.Where(p => File.Exists(p.Key) && !_filters[p.Value].IsIgnored(p.Key)).Select(p => p.Key).Order(StringComparer.Ordinal).ToList();
        if (files.Count == 0 && dropped == 0) return;
        try { Changed?.Invoke(new FolderChangeBatch(files, dropped)); }
        catch (ObjectDisposedException) { /* the owner went away while the batch was on its way */ }
    }

    public void Dispose()
    {
        FileSystemWatcher[] watchers;
        lock (_gate)
        {
            _disposed = true;
            watchers = [.. _watchers];
            _watchers.Clear();
            _timer?.Dispose();
            _pending.Clear();
        }
        foreach (var watcher in watchers) watcher.Dispose(); // outside the lock: a handler that waits for it would block the disposal
    }
}
