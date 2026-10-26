namespace NksHub.Textida.Core.Documents;

public sealed partial class Document
{
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private (ExternalChangeKind Kind, string? Hash)? _lastReported;

    /// <summary>Test seam: runs after the temporary file is written and before the final check.</summary>
    internal Action? BeforeSaveCommit;

    /// <summary>File the document was opened from or last saved to; null for an unnamed document.</summary>
    public string? FilePath { get; private set; }
    /// <summary>What the file looked like when it was loaded or saved by this document.</summary>
    public FileFingerprint? Fingerprint { get; private set; }

    /// <summary>Raised (possibly on a thread-pool thread) when the file no longer matches <see cref="Fingerprint"/>; the event carries the revision.</summary>
    public event EventHandler<ExternalChange>? ExternalChangeDetected;

    /// <summary>Raised when the new file is complete and about to replace the target; a journal records the intent here, so a crash during the swap is understood.</summary>
    public event EventHandler<SavingEventArgs>? Saving;

    /// <summary>Raised after a successful save, when <see cref="FilePath"/> and <see cref="Fingerprint"/> describe the new file.</summary>
    public event EventHandler? Saved;

    /// <summary>
    /// Compares the file with the recorded fingerprint, content included, so a change with the same size and time is found.
    /// A conflict raises <see cref="ExternalChangeDetected"/> once per distinct state of the file.
    /// </summary>
    public ExternalChange? CheckExternalChange()
    {
        if (FilePath is not { } path || Fingerprint is not { } expected) return null;
        var actual = FileFingerprint.TryRead(path);
        ExternalChange? change = null;
        if (actual is null) change = new(Id, _revision, path, expected, null, ExternalChangeKind.Missing);
        else if (!actual.SameContent(expected)) change = new(Id, _revision, path, expected, actual, ExternalChangeKind.Modified);
        else if (actual.LastWriteUtc != expected.LastWriteUtc) Fingerprint = expected with { LastWriteUtc = actual.LastWriteUtc };

        var key = change is null ? ((ExternalChangeKind, string?)?)null : (change.Kind, change.Actual?.Sha256);
        if (key is null) _lastReported = null;
        else if (_lastReported != key)
        {
            _lastReported = key;
            ExternalChangeDetected?.Invoke(this, change!);
        }
        return change;
    }

    /// <summary>Watches the file and runs <see cref="CheckExternalChange"/> shortly after it changes.</summary>
    public void StartWatching(TimeSpan? debounce = null)
    {
        if (FilePath is not { } path) throw new InvalidOperationException("An unnamed document has no file to watch.");
        StopWatching();
        var delay = debounce ?? TimeSpan.FromMilliseconds(150);
        var timer = _debounce = new Timer(_ => CheckExternalChange());
        var watcher = _watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
        };
        bool Mine(string? p) => p is not null && SamePath(p, path);
        void Poke(object? _, FileSystemEventArgs e)
        {
            if (Mine(e.FullPath) || (e is RenamedEventArgs r && Mine(r.OldFullPath))) timer.Change(delay, Timeout.InfiniteTimeSpan);
        }
        watcher.Changed += Poke;
        watcher.Created += Poke;
        watcher.Deleted += Poke;
        watcher.Renamed += (s, e) => Poke(s, e);
        watcher.EnableRaisingEvents = true;
    }

    public void StopWatching()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
        _watcher = null;
        _debounce = null;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// Writes the bytes to a temporary file next to the target and swaps it in. Saving over the document's own file first
    /// checks that the file still matches the fingerprint, and checks again right before the swap; a mismatch returns a
    /// conflict and writes nothing. <paramref name="overwriteExternalChanges"/> is the user's explicit decision to skip the checks.
    /// </summary>
    public SaveResult Save(string? path = null, bool overwriteExternalChanges = false)
    {
        ThrowIfDisposed();
        string target = Path.GetFullPath(path ?? FilePath ?? throw new InvalidOperationException("An unnamed document needs a path."));
        bool inPlace = FilePath is not null && SamePath(target, FilePath);
        bool guard = inPlace && !overwriteExternalChanges;
        if (guard && CheckExternalChange() is { } early) return new(false, early);

        string temp = target + ".nkspad-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            string hash;
            using (var fs = new FileStream(temp, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                PreallocationSize = ByteLength,
                BufferSize = 0,
            }))
            {
                var hashing = new HashingStream(fs);
                _backend.CopyTo(hashing);
                fs.Flush(true);
                hash = hashing.Finish();
            }

            BeforeSaveCommit?.Invoke();
            if (guard && CheckExternalChange() is { } late) return new(false, late);

            Saving?.Invoke(this, new SavingEventArgs(target, ByteLength, hash));
            if (_backend.BackingPath is { } mapped && SamePath(mapped, target)) _backend.ReplaceBackingFile(temp, target);
            else FileSwap.Replace(temp, target);

            FilePath = target;
            Fingerprint = new FileFingerprint(ByteLength, File.GetLastWriteTimeUtc(target), hash);
            _lastReported = null;
            MarkSaved();
            Saved?.Invoke(this, EventArgs.Empty);
            return new(true, null);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
