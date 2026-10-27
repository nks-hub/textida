using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Search;
using NksHub.Textida.Core.Secrets;

namespace NksHub.Textida.Core.History;

/// <summary>Why a revision was taken.</summary>
public enum HistorySource
{
    /// <summary>The editor saved the file.</summary>
    Save,
    /// <summary>Another program changed the file.</summary>
    External,
    /// <summary>The user asked for a revision.</summary>
    Manual,
    /// <summary>The state found when the file was first opened.</summary>
    Initial,
    /// <summary>The state that a restore is about to replace.</summary>
    BeforeRestore,
}

public enum HistoryStatus
{
    Recorded,
    /// <summary>The newest revision already holds exactly these bytes.</summary>
    Unchanged,
    /// <summary>History is off, the file is a secrets file, or the file is above the size limit.</summary>
    Skipped,
    /// <summary>Nothing was stored (see <see cref="HistoryRecordResult.Problem"/>); existing revisions are untouched.</summary>
    Failed,
}

/// <param name="Pins">Names of the owners that need this revision to stay (for example a pending change review). Retention never deletes a revision with a pin.</param>
public sealed record HistoryRevision(string Id, DateTime TimeUtc, HistorySource Source, long Size, string Sha256, string? Label, IReadOnlyList<string> Pins)
{
    public bool Pinned => Pins.Count > 0;
}

public sealed record HistoryRecordResult(HistoryStatus Status, HistoryRevision? Revision, string? Problem = null, bool NoSpace = false);

/// <summary>
/// Limits of the history. A null limit is not applied. Which values ship as defaults is an open product decision, so none is invented here.
/// The newest revision of a file is never deleted by retention, and neither is a pinned one.
/// </summary>
public sealed class HistoryPolicy
{
    public bool Enabled { get; init; } = true;
    /// <summary>
    /// Open product decision: whether revisions of secrets files (<see cref="SecretValues.IsSecretFile"/>) may be kept in plain text on disk.
    /// Until it is decided they are not stored.
    /// </summary>
    public bool StoreSecretFiles { get; init; }
    public TimeSpan? MaxAge { get; init; }
    public int? MaxRevisionsPerFile { get; init; }
    public long? MaxBytesPerFile { get; init; }
    public long? MaxTotalBytes { get; init; }
    /// <summary>Files larger than this get no revisions.</summary>
    public long? MaxRevisionBytes { get; init; }
}

public sealed class HistoryFormatException(string message) : Exception(message);

public sealed record HistorySearchHit(HistoryRevision Revision, long Matches, int FirstLine, string Preview);

/// <summary>
/// Local history of files. Layout under the root: <c>format</c> (the version), then a folder per file with <c>path.txt</c>, <c>blobs/&lt;id&gt;.rev</c> (the bytes)
/// and <c>&lt;id&gt;.json</c> (label, pins, hash). A revision exists once its .json is in place, and that is written after the blob; a crash leaves at most
/// an orphan blob or a temp file, which the next access of that file removes. There is no shared index to corrupt. Deleting writes in the opposite order.
/// </summary>
public sealed partial class FileHistory
{
    public const int FormatVersion = 1;

    private readonly string _root;
    private volatile HistoryPolicy _policy;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly HashSet<string> _swept = [];
    private bool _rootChecked;

    /// <summary>Test seam: called with "blob" after the bytes are in place and with "meta" before the revision becomes visible.</summary>
    internal Action<string>? Stage;

    public FileHistory(string root, HistoryPolicy? policy = null, TimeProvider? clock = null)
    {
        _root = Path.GetFullPath(root);
        _policy = policy ?? new HistoryPolicy();
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>The limits in force; replaced when the settings change, and applied from the next record or prune.</summary>
    public HistoryPolicy Policy { get => _policy; set => _policy = value; }

    internal sealed class MetaDto
    {
        public string Id { get; set; } = "";
        public long Ticks { get; set; }
        public HistorySource Source { get; set; }
        public long Size { get; set; }
        public string Sha256 { get; set; } = "";
        public string? Label { get; set; }
        public List<string> Pins { get; set; } = [];
    }

    [JsonSerializable(typeof(MetaDto))]
    internal sealed partial class MetaContext : JsonSerializerContext;

    private static HistoryRevision ToRevision(MetaDto m) => new(m.Id, new DateTime(m.Ticks, DateTimeKind.Utc), m.Source, m.Size, m.Sha256, m.Label, m.Pins.ToArray());

    // ---- paths ----

    private string FolderOf(string path)
    {
        var full = Path.GetFullPath(path);
        var key = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? full.ToLowerInvariant() : full;
        return Path.Combine(_root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16]);
    }

    private static string BlobOf(string folder, string id) => Path.Combine(folder, "blobs", id + ".rev");
    private static string MetaOf(string folder, string id) => Path.Combine(folder, id + ".json");

    // ---- format ----

    private void EnsureRoot()
    {
        if (_rootChecked) return;
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "format");
        int found = 0;
        if (File.Exists(file))
        {
            if (!int.TryParse(File.ReadAllText(file).Trim(), out found) || found < 1)
                throw new HistoryFormatException($"The history format marker {file} is not readable.");
            if (found > FormatVersion)
                throw new HistoryFormatException($"The history was written by a newer version of the editor (format {found}); this one reads format {FormatVersion}. Nothing was changed.");
        }
        // Migrations go here, one step per version, each written so that a crash in the middle can be repeated: for (v = found; v < FormatVersion; v++) ...
        if (found != FormatVersion) WriteAtomic(file, Encoding.ASCII.GetBytes(FormatVersion.ToString()));
        _rootChecked = true;
    }

    // ---- writing ----

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var temp = Path.Combine(Path.GetDirectoryName(path)!, "." + Guid.NewGuid().ToString("N")[..12] + ".tmp");
        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Write(bytes);
                fs.Flush(true);
            }
            FileSwap.Replace(temp, path);
        }
        catch
        {
            FileSwap.TryDelete(temp);
            throw;
        }
    }

    private static bool IsNoSpace(IOException e) => (e.HResult & 0xFFFF) is 112 or 39 || e.HResult == 28 || e.Message.Contains("not enough space", StringComparison.OrdinalIgnoreCase);

    /// <summary>Stores the bytes of the file at <paramref name="path"/> as read from <paramref name="path"/> itself.</summary>
    public HistoryRecordResult RecordFile(string path, HistorySource source, string? label = null)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
            return Record(path, fs, source, label);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(HistoryStatus.Failed, null, e.Message);
        }
    }

    /// <summary>Stores a revision of <paramref name="path"/> with the bytes read from <paramref name="content"/>.</summary>
    public HistoryRecordResult Record(string path, ReadOnlyMemory<byte> content, HistorySource source, string? label = null)
    {
        using var ms = new MemoryStream(content.ToArray(), writable: false);
        return Record(path, ms, source, label);
    }

    public HistoryRecordResult Record(string path, Stream content, HistorySource source, string? label = null)
    {
        if (!_policy.Enabled) return new(HistoryStatus.Skipped, null);
        if (!_policy.StoreSecretFiles && SecretValues.IsSecretFile(path)) return new(HistoryStatus.Skipped, null);
        if (_policy.MaxRevisionBytes is { } max && content.CanSeek && content.Length - content.Position > max) return new(HistoryStatus.Skipped, null);

        lock (_gate)
        {
            string? temp = null;
            string? blob = null;
            try
            {
                EnsureRoot();
                var folder = FolderOf(path);
                Directory.CreateDirectory(Path.Combine(folder, "blobs"));
                if (!File.Exists(Path.Combine(folder, "path.txt"))) WriteAtomic(Path.Combine(folder, "path.txt"), Encoding.UTF8.GetBytes(Path.GetFullPath(path)));
                Sweep(folder);

                temp = Path.Combine(folder, "." + Guid.NewGuid().ToString("N")[..12] + ".tmp");
                string hash;
                long size;
                using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    var buf = new byte[1 << 16];
                    size = 0;
                    for (int n; (n = content.Read(buf, 0, buf.Length)) > 0;)
                    {
                        if (_policy.MaxRevisionBytes is { } cap && size + n > cap) { fs.Dispose(); FileSwap.TryDelete(temp); return new(HistoryStatus.Skipped, null); }
                        fs.Write(buf, 0, n);
                        sha.AppendData(buf, 0, n);
                        size += n;
                    }
                    fs.Flush(true);
                    hash = Convert.ToHexString(sha.GetHashAndReset());
                }

                var existing = ListFolder(folder);
                if (existing.Count > 0 && existing[0].Sha256 == hash)
                {
                    FileSwap.TryDelete(temp);
                    return new(HistoryStatus.Unchanged, existing[0]);
                }

                var now = _clock.GetUtcNow().UtcDateTime;
                string id = NewId(now, existing.Count > 0 ? existing[0].Id : null);
                for (; ; id = Bump(id))
                {
                    blob = BlobOf(folder, id);
                    if (File.Exists(MetaOf(folder, id))) continue;
                    try { File.Move(temp, blob); break; }
                    catch (IOException) when (File.Exists(blob)) { /* another instance took this id */ }
                }
                temp = null;
                Stage?.Invoke("blob");

                var meta = new MetaDto { Id = id, Ticks = now.Ticks, Source = source, Size = size, Sha256 = hash, Label = label };
                Stage?.Invoke("meta");
                WriteAtomic(MetaOf(folder, id), JsonSerializer.SerializeToUtf8Bytes(meta, MetaContext.Default.MetaDto));
                blob = null;
                var revision = ToRevision(meta);
                PruneFolder(folder);
                return new(HistoryStatus.Recorded, revision);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (temp is not null) FileSwap.TryDelete(temp);
                if (blob is not null) FileSwap.TryDelete(blob);
                return new(HistoryStatus.Failed, null, e.Message, e is IOException io && IsNoSpace(io));
            }
        }
    }

    private static string NewId(DateTime now, string? newest)
    {
        var id = now.ToString("yyyyMMddTHHmmssfff") + "-00";
        return newest is not null && string.CompareOrdinal(id, newest) <= 0 ? Bump(newest) : id;
    }

    private static string Bump(string id)
    {
        int n = int.Parse(id[^2..]) + 1;
        return n > 99 ? id + "-00" : id[..^2] + n.ToString("D2");
    }

    /// <summary>Removes temp files and blobs without a revision, except fresh ones that an instance may still be writing.</summary>
    private void Sweep(string folder)
    {
        if (!_swept.Add(folder)) return;
        var fresh = DateTime.UtcNow - TimeSpan.FromMinutes(5); // file times are real time, not the injected clock
        foreach (var tmp in Directory.EnumerateFiles(folder, "*.tmp"))
            if (File.GetLastWriteTimeUtc(tmp) < fresh) FileSwap.TryDelete(tmp);
        var blobs = Path.Combine(folder, "blobs");
        if (!Directory.Exists(blobs)) return;
        foreach (var b in Directory.EnumerateFiles(blobs, "*.rev"))
            if (!File.Exists(MetaOf(folder, Path.GetFileNameWithoutExtension(b))) && File.GetLastWriteTimeUtc(b) < fresh) FileSwap.TryDelete(b);
    }

    // ---- reading ----

    private static List<HistoryRevision> ListFolder(string folder)
    {
        var list = new List<HistoryRevision>();
        if (!Directory.Exists(folder)) return list;
        foreach (var json in Directory.EnumerateFiles(folder, "*.json"))
        {
            try
            {
                var m = JsonSerializer.Deserialize(File.ReadAllBytes(json), MetaContext.Default.MetaDto);
                if (m is not null && m.Id == Path.GetFileNameWithoutExtension(json) && File.Exists(BlobOf(folder, m.Id))) list.Add(ToRevision(m));
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { /* an unreadable entry is not a revision */ }
        }
        list.Sort((a, b) => string.CompareOrdinal(b.Id, a.Id));
        return list;
    }

    /// <summary>The revisions of a file, newest first. Optional filter on the label (case-insensitive, contains).</summary>
    public IReadOnlyList<HistoryRevision> List(string path, string? labelContains = null)
    {
        lock (_gate)
        {
            EnsureRoot();
            var all = ListFolder(FolderOf(path));
            return string.IsNullOrEmpty(labelContains) ? all : all.FindAll(r => r.Label?.Contains(labelContains, StringComparison.CurrentCultureIgnoreCase) == true);
        }
    }

    /// <summary>The bytes of a revision. Throws <see cref="InvalidDataException"/> when the stored bytes no longer match their hash.</summary>
    public byte[] Read(string path, string id)
    {
        string folder = FolderOf(path);
        var rev = List(path).FirstOrDefault(r => r.Id == id) ?? throw new FileNotFoundException("No such revision: " + id);
        var bytes = File.ReadAllBytes(BlobOf(folder, id));
        if (bytes.Length != rev.Size || Convert.ToHexString(SHA256.HashData(bytes)) != rev.Sha256)
            throw new InvalidDataException("The stored revision " + id + " does not match its hash.");
        return bytes;
    }

    /// <summary>The first bytes of a revision, for a preview; the hash is not checked.</summary>
    public byte[] ReadPrefix(string path, string id, int max)
    {
        using var fs = new FileStream(BlobOf(FolderOf(path), id), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buf = new byte[(int)Math.Min(max, fs.Length)];
        fs.ReadExactly(buf);
        return buf;
    }

    /// <summary>Copies a revision to <paramref name="destination"/> (for handing it to a compare tool).</summary>
    public void Export(string path, string id, string destination) => File.WriteAllBytes(destination, Read(path, id));

    // ---- labels and pins ----

    private void Update(string path, string id, Action<MetaDto> change)
    {
        lock (_gate)
        {
            EnsureRoot();
            var folder = FolderOf(path);
            var file = MetaOf(folder, id);
            if (!File.Exists(file)) throw new FileNotFoundException("No such revision: " + id);
            var m = JsonSerializer.Deserialize(File.ReadAllBytes(file), MetaContext.Default.MetaDto)!;
            change(m);
            WriteAtomic(file, JsonSerializer.SerializeToUtf8Bytes(m, MetaContext.Default.MetaDto));
        }
    }

    public void SetLabel(string path, string id, string? label) => Update(path, id, m => m.Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim());

    /// <summary>Keeps the revision out of reach of retention until <see cref="Unpin"/> is called with the same owner.</summary>
    public void Pin(string path, string id, string owner) => Update(path, id, m => { if (!m.Pins.Contains(owner)) m.Pins.Add(owner); });

    public void Unpin(string path, string id, string owner) => Update(path, id, m => m.Pins.Remove(owner));

    // ---- retention ----

    private void Delete(string folder, HistoryRevision r)
    {
        FileSwap.TryDelete(MetaOf(folder, r.Id));
        FileSwap.TryDelete(BlobOf(folder, r.Id));
    }

    private void PruneFolder(string folder)
    {
        var revs = ListFolder(folder); // newest first
        var drop = new List<HistoryRevision>();
        bool Candidate(HistoryRevision r) => !r.Pinned && r.Id != revs[0].Id && !drop.Contains(r);
        var now = _clock.GetUtcNow().UtcDateTime;

        if (_policy.MaxAge is { } age) drop.AddRange(revs.Where(r => Candidate(r) && now - r.TimeUtc > age));
        if (_policy.MaxRevisionsPerFile is { } count)
            for (int excess = revs.Count - drop.Count - Math.Max(1, count); excess > 0; excess--)
                if (revs.LastOrDefault(Candidate) is { } oldest) drop.Add(oldest); else break;
        if (_policy.MaxBytesPerFile is { } cap)
            while (revs.Where(r => !drop.Contains(r)).Sum(r => r.Size) > cap && revs.LastOrDefault(Candidate) is { } oldest) drop.Add(oldest);

        foreach (var r in drop) Delete(folder, r);
    }

    /// <summary>Applies the limits to one file.</summary>
    public void Prune(string path)
    {
        lock (_gate)
        {
            EnsureRoot();
            PruneFolder(FolderOf(path));
        }
    }

    /// <summary>Applies the per-file limits to every file and then the total size limit, oldest unpinned revision first. Returns the bytes freed.</summary>
    public long PruneAll()
    {
        lock (_gate)
        {
            EnsureRoot();
            long before = TotalBytesCore();
            foreach (var folder in Directory.EnumerateDirectories(_root)) PruneFolder(folder);
            if (_policy.MaxTotalBytes is { } total)
            {
                var all = Directory.EnumerateDirectories(_root).SelectMany(f => ListFolder(f).Select((r, i) => (Folder: f, Rev: r, Newest: i == 0))).ToList();
                long sum = all.Sum(x => x.Rev.Size);
                foreach (var x in all.Where(x => !x.Rev.Pinned && !x.Newest).OrderBy(x => x.Rev.Id, StringComparer.Ordinal))
                {
                    if (sum <= total) break;
                    Delete(x.Folder, x.Rev);
                    sum -= x.Rev.Size;
                }
            }
            return before - TotalBytesCore();
        }
    }

    private long TotalBytesCore() => Directory.Exists(_root) ? Directory.EnumerateDirectories(_root).Sum(f => ListFolder(f).Sum(r => r.Size)) : 0;

    public long TotalBytes()
    {
        lock (_gate) return TotalBytesCore();
    }

    // ---- search ----

    /// <summary>Looks for text in the older revisions of a file; newest revision first. Revisions of a secrets file are not searched.</summary>
    public async Task<IReadOnlyList<HistorySearchHit>> SearchAsync(string path, SearchPattern pattern, CancellationToken ct = default)
    {
        if (SecretValues.IsSecretFile(path)) return [];
        IReadOnlyList<HistoryRevision> revisions = List(path);
        if (revisions.Count == 0) return [];
        var blobs = Path.Combine(FolderOf(path), "blobs");
        var settings = new SearchSettings { MaxResults = int.MaxValue, StopAtLimit = false, Walk = new FileWalkOptions { UseIgnoreFiles = false, IncludeHidden = true } };
        var (hits, _) = await SearchWorker.CollectAsync(pattern, new SearchScope([blobs]), settings, ct);
        var byId = revisions.ToDictionary(r => r.Id);
        return hits
            .Where(h => h.Source.Path is not null && byId.ContainsKey(Path.GetFileNameWithoutExtension(h.Source.Path)))
            .GroupBy(h => Path.GetFileNameWithoutExtension(h.Source.Path!))
            .Select(g => new HistorySearchHit(byId[g.Key], g.LongCount(), g.Min(h => h.Line), g.OrderBy(h => h.Line).First().Preview))
            .OrderByDescending(h => h.Revision.Id, StringComparer.Ordinal)
            .ToList();
    }
}
