using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Remote;

/// <summary>What the server held when the local copy was last in step with it: size, modification time (whole seconds) and the hash of the content.</summary>
public sealed record RemoteRevision(long Size, DateTime? ModifiedUtc, string Sha256)
{
    /// <summary>The file does not exist on the server.</summary>
    public static readonly RemoteRevision None = new(-1, null, "");

    public bool IsNone => Size < 0;

    /// <summary>Same size and same modification time to the second. A server that reports no time is judged by size alone, which <see cref="VersionCheck.Content"/> backs up.</summary>
    public bool Matches(RemoteStat stat) =>
        !IsNone && stat.Size == Size && (ModifiedUtc is null || stat.ModifiedUtc is null || Whole(ModifiedUtc.Value) == Whole(stat.ModifiedUtc.Value));

    internal static DateTime Whole(DateTime t) => new(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
}

/// <summary>A remote file edited through a local copy.</summary>
public sealed record WorkingCopy(string ConnectionId, string RemotePath, string LocalPath, RemoteRevision Base);

public enum LocalState
{
    /// <summary>The local file is the version that was downloaded or last uploaded.</summary>
    InSync,
    /// <summary>Saved locally and not sent to the server yet.</summary>
    Unsent,
    /// <summary>The local file was deleted.</summary>
    Missing,
}

public enum ServerState
{
    InSync,
    /// <summary>The server file differs from the revision the copy was made from.</summary>
    Changed,
    /// <summary>The server no longer has the file.</summary>
    Missing,
}

public readonly record struct CopyStatus(WorkingCopy Copy, LocalState Local, ServerState? Server)
{
    /// <summary>Both sides moved on; the user has to decide (diff).</summary>
    public bool IsConflict => Local == LocalState.Unsent && Server is ServerState.Changed or ServerState.Missing;
}

/// <summary>Limits of the cache. Both are open product decisions, so nothing is pruned until the user sets them.</summary>
public sealed record CachePolicy(long? MaxBytes = null, TimeSpan? MaxAge = null);

internal sealed class WorkingCopyMeta
{
    public string ConnectionId { get; set; } = "";
    public string RemotePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public DateTime? ModifiedUtc { get; set; }
    public string Sha256 { get; set; } = "";
    public DateTime LastUsedUtc { get; set; }
}

[JsonSerializable(typeof(WorkingCopyMeta))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class WorkingCopyJson : JsonSerializerContext;

/// <summary>
/// The local copies of remote files and their remote revision, kept on disk next to each copy. Everything except the download and the server comparison works
/// without a connection. Whether a copy has unsent changes is derived from the file on disk (its hash against the downloaded one), so unsent changes survive
/// a closed editor and a crash without any extra state to lose.
/// </summary>
public sealed class WorkingCopyStore(string root)
{
    private const string MetaName = ".remote.json";

    public string Root { get; } = root;

    private string FolderOf(string connectionId, string remotePath) =>
        Path.Combine(Root, connectionId, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(remotePath)))[..16]);

    private static string FileNameOf(string remotePath)
    {
        string name = remotePath[(remotePath.LastIndexOf('/') + 1)..];
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Length == 0 ? "file" : name;
    }

    private static WorkingCopy ToCopy(string folder, WorkingCopyMeta m) =>
        new(m.ConnectionId, m.RemotePath, Path.Combine(folder, m.FileName), new RemoteRevision(m.Size, m.ModifiedUtc, m.Sha256));

    private static WorkingCopyMeta? ReadMeta(string folder)
    {
        try { return JsonSerializer.Deserialize(File.ReadAllBytes(Path.Combine(folder, MetaName)), WorkingCopyJson.Default.WorkingCopyMeta); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private static void WriteMeta(string folder, WorkingCopyMeta m)
    {
        string path = Path.Combine(folder, MetaName);
        string temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(m, WorkingCopyJson.Default.WorkingCopyMeta));
        FileSwap.Replace(temp, path);
    }

    public WorkingCopy? Find(string connectionId, string remotePath)
    {
        string folder = FolderOf(connectionId, remotePath);
        return ReadMeta(folder) is { } m ? ToCopy(folder, m) : null;
    }

    /// <summary>The copy whose local file is <paramref name="localPath"/>, or null when the path is not a working copy of this store.</summary>
    public WorkingCopy? FindByLocalPath(string localPath)
    {
        string full = Path.GetFullPath(localPath);
        if (!full.StartsWith(Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase)) return null;
        return All().FirstOrDefault(c => string.Equals(Path.GetFullPath(c.LocalPath), full, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<WorkingCopy> All()
    {
        var list = new List<WorkingCopy>();
        if (!Directory.Exists(Root)) return list;
        foreach (var folder in Directory.EnumerateDirectories(Root).SelectMany(Directory.EnumerateDirectories))
            if (ReadMeta(folder) is { } m) list.Add(ToCopy(folder, m));
        return list;
    }

    public LocalState LocalStateOf(WorkingCopy copy)
    {
        if (!File.Exists(copy.LocalPath)) return LocalState.Missing;
        return RemoteTransfer.HashOf(copy.LocalPath) == copy.Base.Sha256 ? LocalState.InSync : LocalState.Unsent;
    }

    /// <summary>The copies with changes that were saved locally and never sent. Works offline; this is what the editor lists after a start.</summary>
    public IReadOnlyList<WorkingCopy> Unsent() => All().Where(c => LocalStateOf(c) != LocalState.InSync).ToList();

    /// <summary>
    /// Returns the local copy of a remote file; downloads it only when there is none yet. An existing copy, with or without unsent changes, is returned as it is,
    /// so reopening a file never overwrites local work.
    /// </summary>
    public async Task<WorkingCopy> OpenAsync(IRemoteClient client, string remotePath, CancellationToken cancellation, TransferOptions? options = null) =>
        Find(client.Connection.Id, remotePath) is { } existing ? Rebase(existing, existing.Base) : await DownloadAsync(client, remotePath, cancellation, options).ConfigureAwait(false);

    /// <summary>
    /// Downloads the current server version into the copy and makes it the new base. This replaces the local file; callers check <see cref="LocalStateOf"/>
    /// first when unsent changes must not be lost. The bytes arrive in a partial file (<see cref="RemoteTransfer.DownloadPartAsync"/>): a cancelled or broken download is continued
    /// by the next call while the server file keeps its size and time, so a retry does not start over.
    /// </summary>
    public async Task<WorkingCopy> DownloadAsync(IRemoteClient client, string remotePath, CancellationToken cancellation, TransferOptions? options = null)
    {
        string connectionId = client.Connection.Id;
        string folder = FolderOf(connectionId, remotePath);
        Directory.CreateDirectory(folder);
        string name = FileNameOf(remotePath);
        string local = Path.Combine(folder, name);

        var before = await client.StatAsync(remotePath, cancellation).ConfigureAwait(false)
            ?? throw new RemoteException(RemoteError.NotFound, $"'{remotePath}' does not exist on the server.");
        if (before.IsDirectory) throw new RemoteException(RemoteError.Failed, $"'{remotePath}' is a folder.");
        string? part = null;
        try
        {
            part = await RemoteTransfer.DownloadPartAsync(client, remotePath, local, before, options, cancellation).ConfigureAwait(false);
            var after = await client.StatAsync(remotePath, cancellation).ConfigureAwait(false);
            if (after is null || after.Value.Size != before.Size || !SameTime(before.ModifiedUtc, after.Value.ModifiedUtc) || new FileInfo(part).Length != after.Value.Size)
                throw new RemoteException(RemoteError.ChangedOnServer, "The file changed on the server while it was downloaded. Open it again.");
            string hash = await RemoteTransfer.HashOfAsync(part, cancellation).ConfigureAwait(false);
            FileSwap.Replace(part, local);
            var meta = new WorkingCopyMeta
            {
                ConnectionId = connectionId, RemotePath = remotePath, FileName = name, Size = before.Size, ModifiedUtc = before.ModifiedUtc, Sha256 = hash, LastUsedUtc = DateTime.UtcNow,
            };
            WriteMeta(folder, meta);
            return ToCopy(folder, meta);
        }
        finally
        {
            // a complete part that failed a check is of no use; a part the transfer left unfinished never gets here (part is still null) and waits for the next call
            if (part is not null) FileSwap.TryDelete(part);
        }
    }

    private static bool SameTime(DateTime? a, DateTime? b) => a is null || b is null || RemoteRevision.Whole(a.Value) == RemoteRevision.Whole(b.Value);

    /// <summary>
    /// Puts the bytes of <paramref name="source"/>, a file that lives outside the store (a file of a project with a default server), into the copy of <paramref name="remotePath"/>, for <see cref="RemoteSync.UploadAsync"/>.
    /// The base stays what the last upload left. A first copy has the base <see cref="RemoteRevision.None"/>: the project never sent the file, so a file already on the server is a conflict until the user accepts it.
    /// </summary>
    public WorkingCopy Stage(string connectionId, string remotePath, string source)
    {
        string folder = FolderOf(connectionId, remotePath);
        Directory.CreateDirectory(folder);
        var meta = ReadMeta(folder) ?? new WorkingCopyMeta { ConnectionId = connectionId, RemotePath = remotePath, FileName = FileNameOf(remotePath), Size = RemoteRevision.None.Size, Sha256 = RemoteRevision.None.Sha256 };
        meta.LastUsedUtc = DateTime.UtcNow;
        string local = Path.Combine(folder, meta.FileName);
        string temp = local + ".textida-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.Copy(source, temp);
            FileSwap.Replace(temp, local);
        }
        finally { FileSwap.TryDelete(temp); }
        WriteMeta(folder, meta);
        return ToCopy(folder, meta);
    }

    /// <summary>Records a new base after an upload: the copy now equals what the server holds.</summary>
    internal WorkingCopy Rebase(WorkingCopy copy, RemoteRevision revision)
    {
        string folder = Path.GetDirectoryName(copy.LocalPath)!;
        WriteMeta(folder, new WorkingCopyMeta
        {
            ConnectionId = copy.ConnectionId, RemotePath = copy.RemotePath, FileName = Path.GetFileName(copy.LocalPath),
            Size = revision.Size, ModifiedUtc = revision.ModifiedUtc, Sha256 = revision.Sha256, LastUsedUtc = DateTime.UtcNow,
        });
        return copy with { Base = revision };
    }

    /// <summary>Compares a copy with the server (stat only, no download). The local side needs no connection.</summary>
    public async Task<CopyStatus> CheckAsync(IRemoteClient client, WorkingCopy copy, CancellationToken cancellation)
    {
        var stat = await client.StatAsync(copy.RemotePath, cancellation).ConfigureAwait(false);
        var server = stat is null ? ServerState.Missing : copy.Base.Matches(stat.Value) ? ServerState.InSync : ServerState.Changed;
        return new CopyStatus(copy, LocalStateOf(copy), server);
    }

    /// <summary>
    /// After a restart: pairs every copy of the connection with the server by its stored revision. Nothing is downloaded or overwritten; the result says which
    /// copies are in step, which server files moved on and which copies have unsent work.
    /// </summary>
    public async Task<IReadOnlyList<CopyStatus>> PairAsync(IRemoteClient client, CancellationToken cancellation)
    {
        var result = new List<CopyStatus>();
        foreach (var copy in All().Where(c => c.ConnectionId == client.Connection.Id))
            result.Add(await CheckAsync(client, copy, cancellation).ConfigureAwait(false));
        return result;
    }

    public void Forget(WorkingCopy copy)
    {
        string folder = Path.GetDirectoryName(copy.LocalPath)!;
        if (Directory.Exists(folder) && folder.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) Directory.Delete(folder, true);
    }

    /// <summary>Removes copies that exceed the policy, least recently used first. A copy with unsent changes is never removed. Returns what was removed.</summary>
    public IReadOnlyList<WorkingCopy> Prune(CachePolicy policy, DateTime nowUtc)
    {
        var removed = new List<WorkingCopy>();
        var entries = All().Select(c => (Copy: c, Meta: ReadMeta(Path.GetDirectoryName(c.LocalPath)!)!))
            .Where(e => LocalStateOf(e.Copy) == LocalState.InSync).OrderBy(e => e.Meta.LastUsedUtc).ToList();
        if (policy.MaxAge is { } age)
            foreach (var e in entries.Where(e => nowUtc - e.Meta.LastUsedUtc > age).ToList()) { Forget(e.Copy); removed.Add(e.Copy); entries.Remove(e); }
        if (policy.MaxBytes is { } max)
        {
            long total = All().Sum(c => File.Exists(c.LocalPath) ? new FileInfo(c.LocalPath).Length : 0);
            foreach (var e in entries)
            {
                if (total <= max) break;
                total -= new FileInfo(e.Copy.LocalPath).Length;
                Forget(e.Copy);
                removed.Add(e.Copy);
            }
        }
        return removed;
    }
}
