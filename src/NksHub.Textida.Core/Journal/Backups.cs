using System.Security.Cryptography;
using System.Text;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Journal;

/// <summary>Where the recovery journal goes: the primary directory, or the fallback when the primary cannot be written. With neither, recovery after a crash is not available.</summary>
public sealed record JournalLocationResult(string? Directory, bool UsedFallback, string? PrimaryProblem, string? FallbackProblem)
{
    public bool RecoveryAvailable => Directory is not null;

    /// <summary>Text for the status bar, or null when the primary location works.</summary>
    public string? Warning => Directory is null
        ? $"Crash recovery is off: the journal cannot be written ({PrimaryProblem}; {FallbackProblem}). Unsaved text stays only in the open tabs."
        : UsedFallback ? $"The journal location is not available ({PrimaryProblem}); using {Directory}." : null;
}

public static class JournalLocation
{
    public static JournalLocationResult Resolve(string primary, string? fallback)
    {
        var problem = Probe(primary);
        if (problem is null) return new(primary, false, null, null);
        if (fallback is null) return new(null, false, problem, "no fallback location is set");
        var fallbackProblem = Probe(fallback);
        return fallbackProblem is null ? new(fallback, true, problem, null) : new(null, false, problem, fallbackProblem);
    }

    /// <summary>Creates a file in the directory and syncs it; returns why that failed, or null.</summary>
    public static string? Probe(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".probe-" + Guid.NewGuid().ToString("N")[..8]);
            using (var fs = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                fs.WriteByte(1);
                fs.Flush(true);
            }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return $"{directory}: {e.Message}";
        }
    }
}

public enum BackupPlacement
{
    /// <summary>A folder per file inside <see cref="BackupPolicy.Directory"/>, one timestamped copy per save.</summary>
    Directory,
    /// <summary>Next to the file as <c>name.ext.bak</c>; only the last version is kept.</summary>
    BesideFile,
}

public sealed class BackupPolicy
{
    public bool Enabled { get; init; } = true;
    public BackupPlacement Placement { get; init; } = BackupPlacement.Directory;
    /// <summary>Required for <see cref="BackupPlacement.Directory"/>.</summary>
    public string? Directory { get; init; }
    /// <summary>Copies kept per file in a backup directory; older ones are deleted.</summary>
    public int KeepVersions { get; init; } = 5;
    /// <summary>No new copy when the newest one is younger than this (autosave would otherwise fill the folder).</summary>
    public TimeSpan MinInterval { get; init; } = TimeSpan.Zero;
    /// <summary>When true a failed backup stops the save; otherwise the save goes ahead and the failure is reported.</summary>
    public bool RequireBackup { get; init; }
}

public enum BackupStatus { Skipped, Created, Failed }

public sealed record BackupResult(BackupStatus Status, string? BackupPath, string? Problem);

public sealed record SaveOutcome(SaveResult? Save, BackupResult Backup);

/// <summary>Copies the file that a save is about to replace. Independent of the journal: the journal never touches user files and this class never reads the journal.</summary>
public sealed class BackupService(BackupPolicy policy)
{
    public BackupPolicy Policy => policy;

    /// <summary>Why backups cannot be written right now, or null.</summary>
    public string? CheckAvailability() => !policy.Enabled ? null
        : policy.Placement == BackupPlacement.Directory ? (policy.Directory is null ? "no backup directory is set" : JournalLocation.Probe(policy.Directory)) : null;

    /// <summary>Saves the document; the version on disk is backed up first.</summary>
    public SaveOutcome Save(Document doc, string? path = null, bool overwriteExternalChanges = false)
    {
        string? target = path ?? doc.FilePath;
        var backup = target is null ? new BackupResult(BackupStatus.Skipped, null, null) : BackupBeforeSave(target);
        if (backup.Status == BackupStatus.Failed && policy.RequireBackup) return new(null, backup);
        return new(doc.Save(path, overwriteExternalChanges), backup);
    }

    public BackupResult BackupBeforeSave(string path)
    {
        if (!policy.Enabled || !File.Exists(path)) return new(BackupStatus.Skipped, null, null);
        path = Path.GetFullPath(path);
        try
        {
            if (policy.Placement == BackupPlacement.BesideFile)
            {
                var beside = path + ".bak";
                File.Copy(path, beside, overwrite: true);
                return new(BackupStatus.Created, beside, null);
            }

            if (policy.Directory is null) return new(BackupStatus.Failed, null, "no backup directory is set");
            var folder = Path.Combine(policy.Directory, FolderName(path));
            Directory.CreateDirectory(folder);
            var existing = Directory.GetFiles(folder, "*.bak");
            Array.Sort(existing, StringComparer.Ordinal);
            if (policy.MinInterval > TimeSpan.Zero && existing.Length > 0 && DateTime.UtcNow - File.GetCreationTimeUtc(existing[^1]) < policy.MinInterval)
                return new(BackupStatus.Skipped, null, null);

            string stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff"), copy = Path.Combine(folder, stamp + "-00.bak");
            for (int n = 1; File.Exists(copy); n++) copy = Path.Combine(folder, $"{stamp}-{n:D2}.bak");
            File.Copy(path, copy);
            var all = Directory.GetFiles(folder, "*.bak");
            Array.Sort(all, StringComparer.Ordinal);
            foreach (var old in all.Take(Math.Max(0, all.Length - Math.Max(1, policy.KeepVersions)))) File.Delete(old);
            return new(BackupStatus.Created, copy, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(BackupStatus.Failed, null, e.Message);
        }
    }

    private static string FolderName(string fullPath)
    {
        string key = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? fullPath.ToLowerInvariant() : fullPath;
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..8];
        var name = string.Concat(Path.GetFileName(fullPath).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return $"{name}-{hash}";
    }
}

public sealed class AutosaveOptions
{
    /// <summary>Off by default: a file is only written when the user saves.</summary>
    public bool Enabled { get; init; }
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// Optional save of the document to its own file. It only calls <see cref="Document.Save"/>, so the fingerprint guard of the
/// document applies: a file changed on disk is reported through <see cref="Conflict"/> and left alone.
/// </summary>
public sealed class AutosaveService : IDisposable
{
    private readonly Document _doc;
    private readonly AutosaveOptions _options;
    private readonly BackupService? _backups;
    private readonly Action<Action> _invoke;
    private readonly Timer? _timer;

    /// <param name="invoke">Runs the save on the thread that owns the document (a document is not thread safe); default: on the timer thread.</param>
    public AutosaveService(Document doc, AutosaveOptions options, BackupService? backups = null, Action<Action>? invoke = null)
    {
        _doc = doc;
        _options = options;
        _backups = backups;
        _invoke = invoke ?? (a => a());
        if (options.Enabled) _timer = new Timer(_ => _invoke(Tick), null, options.Interval, options.Interval);
    }

    /// <summary>When set, the timer calls this instead of <see cref="RunOnce"/>, so the app can put its save actions around the save (which then calls <see cref="RunOnce"/> itself). A tick that finds the previous one still running does nothing.</summary>
    public Func<Task>? Pipeline { get; set; }

    private int _busy;

    private void Tick()
    {
        if (Pipeline is not { } pipeline) { RunOnce(); return; }
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        _ = Run(pipeline);
    }

    private async Task Run(Func<Task> pipeline)
    {
        try { await pipeline().ConfigureAwait(true); }
        finally { Volatile.Write(ref _busy, 0); }
    }

    public event EventHandler<ExternalChange>? Conflict;

    /// <summary>Saves when autosave is on, the document has a file and unsaved changes. Returns true when a file was written.</summary>
    public bool RunOnce()
    {
        if (!_options.Enabled || _doc.FilePath is null || !_doc.IsModified) return false;
        var saved = _backups is null ? _doc.Save() : _backups.Save(_doc).Save;
        if (saved is { Conflict: { } c }) Conflict?.Invoke(this, c);
        return saved is { Saved: true };
    }

    public void Dispose() => _timer?.Dispose();
}
