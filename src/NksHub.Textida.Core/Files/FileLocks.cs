using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NksHub.Textida.Core.Files;

public enum LockState
{
    /// <summary>Nobody else holds the file; this instance holds the editor lock until it is disposed.</summary>
    Writable,
    /// <summary>Another program has the file open in a way that forbids writing (a sharing lock). The file opens for reading only.</summary>
    ReadOnlyLockedByProcess,
    /// <summary>Another instance of the editor holds the editor lock of the file. The file opens for reading only.</summary>
    ReadOnlyOtherInstance,
    /// <summary>The user chose concurrent writing for this file: no lock is taken or honoured, and every save checks the file on disk first.</summary>
    ConcurrentWrite,
}

/// <summary>Who holds the editor lock of a file; shown in the explanation.</summary>
public sealed record LockOwner(int ProcessId, string Machine, string Instance, DateTime SinceUtc);

/// <summary>The outcome of asking for a file. Dispose it when the document closes; that gives the editor lock back.</summary>
public sealed class FileLockResult : IDisposable
{
    private readonly Action? _release;

    internal FileLockResult(LockState state, LockOwner? owner, Action? release)
    {
        State = state;
        Owner = owner;
        _release = release;
    }

    public LockState State { get; }

    /// <summary>The other instance for <see cref="LockState.ReadOnlyOtherInstance"/> when it left its details; null otherwise.</summary>
    public LockOwner? Owner { get; }

    /// <summary>Opens for reading only, and the reason is one of the two lock states.</summary>
    public bool IsReadOnly => State is LockState.ReadOnlyLockedByProcess or LockState.ReadOnlyOtherInstance;

    /// <summary>
    /// A save of this file must never overwrite changes made behind the editor's back. This holds in every state; in <see cref="LockState.ConcurrentWrite"/>
    /// it is the only protection, so callers pass <c>overwriteExternalChanges: false</c> to every save and ask the user on a conflict.
    /// </summary>
    public bool SaveChecksDiskFirst => true;

    public void Dispose() => _release?.Invoke();
}

/// <summary>
/// Two kinds of lock decide how a file opens. A sharing lock of another program (it has the file open and forbids writers) and the editor lock of another editor
/// instance both open the file read only. The editor lock is a file in <c>lockDirectory</c> that the holding process keeps open exclusively, so the operating
/// system gives it back when that process ends, even by a crash: there are no stale locks to clean up. Nothing is written next to the document, and a
/// <c>.lck</c> file of another editor beside the document is neither read nor deleted.
/// </summary>
public sealed class FileLocks(string lockDirectory, string instanceId)
{
    private readonly Dictionary<string, FileLockResult> _held = new(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private static string KeyOf(string path)
    {
        string full = Path.GetFullPath(path);
        if (!OperatingSystem.IsLinux()) full = full.ToUpperInvariant();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..32];
    }

    public FileLockResult Acquire(string path, bool concurrentWrite = false)
    {
        path = Path.GetFullPath(path);
        if (concurrentWrite) return new FileLockResult(LockState.ConcurrentWrite, null, null);

        lock (_gate)
        {
            if (_held.TryGetValue(path, out var again)) return new FileLockResult(again.State, null, null); // the same instance opens a file it holds already

            if (HasSharingLock(path)) return new FileLockResult(LockState.ReadOnlyLockedByProcess, null, null);

            Directory.CreateDirectory(lockDirectory);
            string key = KeyOf(path);
            string lockFile = Path.Combine(lockDirectory, key + ".lock");
            string ownerFile = Path.Combine(lockDirectory, key + ".owner");
            FileStream handle;
            try { handle = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { return new FileLockResult(LockState.ReadOnlyOtherInstance, ReadOwner(ownerFile), null); }

            WriteOwner(ownerFile, new LockOwner(Environment.ProcessId, Environment.MachineName, instanceId, DateTime.UtcNow));
            FileLockResult? result = null;
            result = new FileLockResult(LockState.Writable, null, () =>
            {
                lock (_gate)
                {
                    _held.Remove(path);
                    handle.Dispose();
                    try { File.Delete(ownerFile); File.Delete(lockFile); } catch (IOException) { /* another instance took it at once */ } catch (UnauthorizedAccessException) { }
                }
            });
            _held[path] = result;
            return result;
        }
    }

    /// <summary>True when another program has the file open and does not let others write to it. The probe opens it for writing and closes it at once; nothing is changed.</summary>
    private static bool HasSharingLock(string path)
    {
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            return false;
        }
        catch (IOException) { return File.Exists(path); }
        catch (UnauthorizedAccessException) { return false; /* a read-only attribute or missing rights is not a lock; the save reports it */ }
    }

    private static LockOwner? ReadOwner(string file)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            var r = doc.RootElement;
            return new LockOwner(r.GetProperty("pid").GetInt32(), r.GetProperty("machine").GetString() ?? "", r.GetProperty("instance").GetString() ?? "", r.GetProperty("since").GetDateTime());
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException or UnauthorizedAccessException) { return null; }
    }

    private static void WriteOwner(string file, LockOwner owner)
    {
        try
        {
            string temp = file + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            using (var fs = File.Create(temp))
            using (var w = new Utf8JsonWriter(fs))
            {
                w.WriteStartObject();
                w.WriteNumber("pid", owner.ProcessId);
                w.WriteString("machine", owner.Machine);
                w.WriteString("instance", owner.Instance);
                w.WriteString("since", owner.SinceUtc);
                w.WriteEndObject();
            }
            File.Move(temp, file, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the lock itself works without the details */ }
    }
}
