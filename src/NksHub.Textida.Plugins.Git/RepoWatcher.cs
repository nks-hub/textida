namespace NksHub.Textida.Plugins.Git;

/// <summary>
/// Calls <c>changed</c> once, <paramref name="debounceMs"/> after the last of a burst of changes in a working tree or in the files of its git directory that change what
/// <c>git status</c> says (<c>index</c> and <c>HEAD</c>). The rest of the git directory is not looked at: git writes objects and logs there while it works, and the plugin's own reads do not
/// touch it. An overflow of the buffer of the system counts as a change, so nothing is missed.
/// </summary>
public sealed class RepoWatcher : IDisposable
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly FileSystemWatcher _tree;
    private readonly FileSystemWatcher _git;
    private readonly Timer _timer;
    private readonly string _gitPrefix;
    private readonly int _debounceMs;
    private volatile bool _disposed;

    public string Root { get; }

    public RepoWatcher(string root, string gitDirectory, int debounceMs, Action changed)
    {
        Root = root;
        _debounceMs = debounceMs;
        _gitPrefix = Path.GetFullPath(gitDirectory).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        _timer = new Timer(_ => { if (!_disposed) changed(); });
        _tree = Watch(root, subdirectories: true, NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size, OnTreeEvent);
        _git = Watch(gitDirectory, subdirectories: false, NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size, OnGitEvent);
    }

    private FileSystemWatcher Watch(string path, bool subdirectories, NotifyFilters filters, FileSystemEventHandler handler)
    {
        var watcher = new FileSystemWatcher(path) { IncludeSubdirectories = subdirectories, NotifyFilter = filters, InternalBufferSize = 64 * 1024 };
        watcher.Created += handler;
        watcher.Changed += handler;
        watcher.Deleted += handler;
        watcher.Renamed += (s, e) => handler(s, e);
        watcher.Error += (_, _) => Poke();
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void OnTreeEvent(object? sender, FileSystemEventArgs e)
    {
        // the git directory inside the tree is the other watcher's business
        if (e.FullPath.StartsWith(_gitPrefix, PathComparison) || string.Equals(e.FullPath + Path.DirectorySeparatorChar, _gitPrefix, PathComparison)) return;
        Poke();
    }

    private void OnGitEvent(object? sender, FileSystemEventArgs e)
    {
        if (e.Name is "index" or "HEAD") Poke();
    }

    /// <summary>Starts or restarts the wait.</summary>
    public void Poke()
    {
        try { if (!_disposed) _timer.Change(_debounceMs, Timeout.Infinite); }
        catch (ObjectDisposedException) { /* ended while an event was on its way */ }
    }

    public void Dispose()
    {
        _disposed = true;
        _tree.Dispose();
        _git.Dispose();
        _timer.Dispose();
    }
}
