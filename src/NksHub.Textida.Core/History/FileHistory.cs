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
    /// <summary>History is off, or the file is a secrets file.</summary>
    Skipped,
    /// <summary>The file is above <see cref="HistoryPolicy.MaxRevisionBytes"/>; no revision is kept, and the older ones are untouched.</summary>
    TooLarge,
    /// <summary>Nothing was stored (see <see cref="HistoryRecordResult.Problem"/>); existing revisions are untouched.</summary>
    Failed,
}

/// <param name="Pins">Names of the owners that need this revision to stay (for example a pending change review). Retention never deletes a revision with a pin.</param>
/// <param name="BaseId">For a revision of a large file that is kept as a patch: the whole copy it is a patch against. Null for a whole copy.</param>
/// <param name="PatchBytes">Bytes the patch takes on disk; 0 for a whole copy.</param>
public sealed record HistoryRevision(string Id, DateTime TimeUtc, HistorySource Source, long Size, string Sha256, string? Label, IReadOnlyList<string> Pins, string? BaseId = null, long PatchBytes = 0)
{
    public bool Pinned => Pins.Count > 0;
    /// <summary>True when the revision is kept as a patch against the whole copy named by <see cref="BaseId"/>.</summary>
    public bool IsPatch => BaseId is not null;
    /// <summary>Bytes the revision takes on disk: its size for a whole copy, the patch for a patched one.</summary>
    public long StoredBytes => IsPatch ? PatchBytes : Size;
}

public sealed record HistoryRecordResult(HistoryStatus Status, HistoryRevision? Revision, string? Problem = null, bool NoSpace = false);

/// <summary>
/// Limits of the history. A null limit is not applied. Which values ship as defaults is an open product decision, so none is invented here.
/// The newest revision of a file is never deleted by retention, and neither is a pinned one, nor a whole copy that a kept patch is made against.
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
    /// <summary>Bytes the revisions of one file may take on disk (a patch counts as its own size).</summary>
    public long? MaxBytesPerFile { get; init; }
    public long? MaxTotalBytes { get; init; }
    /// <summary>Files larger than this get no revisions.</summary>
    public long? MaxRevisionBytes { get; init; }
    /// <summary>
    /// A file larger than this is kept as a patch against the newest whole copy that is still there, when a patch is possible and shorter than
    /// <see cref="MaxPatchPercent"/> percent of the file; else as a new whole copy. Null: every revision is a whole copy.
    /// </summary>
    public long? PatchAboveBytes { get; init; }
    /// <summary>Largest patch, in percent of the file, that is kept instead of a whole copy.</summary>
    public int MaxPatchPercent { get; init; } = 50;
}

public sealed class HistoryFormatException(string message) : Exception(message);

public sealed record HistorySearchHit(HistoryRevision Revision, long Matches, int FirstLine, string Preview);

/// <summary>
/// Local history of files. Layout under the root: <c>format</c> (the version), then a folder per file with <c>path.txt</c>, <c>blobs/&lt;id&gt;.rev</c> (the bytes of a whole copy),
/// <c>blobs/&lt;id&gt;.dlt</c> (a patch of a large file against a whole copy, see <see cref="BinaryDelta"/>) and <c>&lt;id&gt;.json</c> (label, pins, hash, the copy a patch is made against).
/// A revision exists once its .json is in place, and that is written after the blob; a crash leaves at most
/// an orphan blob or a temp file, which the next access of that file removes. There is no shared index to corrupt. Deleting writes in the opposite order.
/// A patch is always against a whole copy, never against another patch, so a revision is read with one patch; the whole copy stays as long as a patch needs it.
/// </summary>
public sealed partial class FileHistory
{
    /// <summary>Version 2 added the patches; an editor of version 1 would not know that a whole copy is needed and would delete it.</summary>
    public const int FormatVersion = 2;

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
        public string? BaseId { get; set; }
        public long PatchBytes { get; set; }
    }

    [JsonSerializable(typeof(MetaDto))]
    internal sealed partial class MetaContext : JsonSerializerContext;

    private static HistoryRevision ToRevision(MetaDto m) => new(m.Id, new DateTime(m.Ticks, DateTimeKind.Utc), m.Source, m.Size, m.Sha256, m.Label, m.Pins.ToArray(), m.BaseId, m.BaseId is null ? 0 : m.PatchBytes);

    // ---- paths ----

    private string FolderOf(string path)
    {
        var full = Path.GetFullPath(path);
        var key = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? full.ToLowerInvariant() : full;
        return Path.Combine(_root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16]);
    }

    private static string BlobOf(string folder, string id) => Path.Combine(folder, "blobs", id + ".rev");
    private static string PatchOf(string folder, string id) => Path.Combine(folder, "blobs", id + ".dlt");
    private static string BlobOf(string folder, HistoryRevision r) => r.IsPatch ? PatchOf(folder, r.Id) : BlobOf(folder, r.Id);
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
        // 1 to 2: nothing to convert, every revision of version 1 is a whole copy and version 2 reads those.
        if (found != FormatVersion) WriteAtomic(file, Encoding.ASCII.GetBytes(FormatVersion.ToString()));
        _rootChecked = true;
    }

    // ---- writing ----

    internal static void WriteAtomic(string path, byte[] bytes)
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
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.RandomAccess);
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
        var result = RecordCore(path, content, source, label);
        if (result.Status == HistoryStatus.Recorded) RaiseChanged(path);
        return result;
    }

    private HistoryRecordResult RecordCore(string path, Stream content, HistorySource source, string? label)
    {
        if (!_policy.Enabled) return new(HistoryStatus.Skipped, null);
        if (!_policy.StoreSecretFiles && SecretValues.IsSecretFile(path)) return new(HistoryStatus.Skipped, null);
        if (_policy.MaxRevisionBytes is { } max && content.CanSeek && content.Length - content.Position > max) return new(HistoryStatus.TooLarge, null);

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
                var existing = ListFolder(folder);

                temp = Path.Combine(folder, "." + Guid.NewGuid().ToString("N")[..12] + ".tmp");
                string hash;
                long size, patchBytes = 0;
                string? baseId = null;
                var patched = TryMakePatch(content, existing, folder, temp);
                if (patched is { Unchanged: not null } same)
                {
                    FileSwap.TryDelete(temp);
                    return new(HistoryStatus.Unchanged, same.Unchanged);
                }
                if (patched is { } made)
                {
                    (hash, size, patchBytes, baseId) = (made.Hash, made.Size, made.PatchBytes, made.BaseId);
                }
                else
                {
                    using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                    using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                    {
                        var buf = new byte[1 << 16];
                        size = 0;
                        for (int n; (n = content.Read(buf, 0, buf.Length)) > 0;)
                        {
                            if (_policy.MaxRevisionBytes is { } cap && size + n > cap) { fs.Dispose(); FileSwap.TryDelete(temp); return new(HistoryStatus.TooLarge, null); }
                            fs.Write(buf, 0, n);
                            sha.AppendData(buf, 0, n);
                            size += n;
                        }
                        fs.Flush(true);
                        hash = Convert.ToHexString(sha.GetHashAndReset());
                    }
                    if (existing.Count > 0 && existing[0].Sha256 == hash)
                    {
                        FileSwap.TryDelete(temp);
                        return new(HistoryStatus.Unchanged, existing[0]);
                    }
                }

                var now = _clock.GetUtcNow().UtcDateTime;
                string id = NewId(now, existing.Count > 0 ? existing[0].Id : null);
                for (; ; id = Bump(id))
                {
                    blob = baseId is null ? BlobOf(folder, id) : PatchOf(folder, id);
                    if (File.Exists(MetaOf(folder, id))) continue;
                    try { File.Move(temp, blob); break; }
                    catch (IOException) when (File.Exists(blob)) { /* another instance took this id */ }
                }
                temp = null;
                Stage?.Invoke("blob");

                var meta = new MetaDto { Id = id, Ticks = now.Ticks, Source = source, Size = size, Sha256 = hash, Label = label, BaseId = baseId, PatchBytes = patchBytes };
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

    private sealed record PatchMade(string Hash, long Size, long PatchBytes, string? BaseId, HistoryRevision? Unchanged);

    /// <summary>
    /// For a large file: the patch against the newest whole copy, written to <paramref name="temp"/>, or the newest revision when it already holds the bytes. Null when the
    /// file is not large enough, is not a file, has no whole copy to be a patch against, differs too much, or changed while it was read; the caller then keeps a whole copy.
    /// </summary>
    private PatchMade? TryMakePatch(Stream content, List<HistoryRevision> existing, string folder, string temp)
    {
        if (_policy.PatchAboveBytes is not { } above || content is not FileStream { CanSeek: true } file || file.Position != 0 || file.Length <= above) return null;
        var anchor = existing.FirstOrDefault(r => !r.IsPatch);
        if (anchor is null || anchor.Size < BinaryDelta.BlockFor(anchor.Size)) return null;
        long length = file.Length;
        string hash = HashOf(file, length);
        file.Position = 0;
        if (existing.Count > 0 && existing[0].Sha256 == hash) return new(hash, length, 0, null, existing[0]);

        long limit = length / 100 * Math.Clamp(_policy.MaxPatchPercent, 1, 100);
        long patchBytes;
        using (var anchorFile = File.OpenHandle(BlobOf(folder, anchor.Id), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess))
        {
            using (var patch = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.WriteThrough))
            {
                patchBytes = BinaryDelta.Create(anchorFile, anchor.Size, file.SafeFileHandle, length, patch, limit);
                patch.Flush(true);
            }
            if (patchBytes >= 0)
            {
                // the patch must make exactly the bytes that were hashed: a file that changed between the two readings, or a damaged whole copy, gives a whole copy instead
                using var read = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
                using var sink = new HashingStream(Stream.Null);
                try { BinaryDelta.Apply(anchorFile, anchor.Size, read, sink); }
                catch (InvalidDataException) { patchBytes = -1; }
                if (patchBytes >= 0 && (sink.Written != length || sink.Hash != hash)) patchBytes = -1;
            }
        }
        if (patchBytes < 0)
        {
            FileSwap.TryDelete(temp);
            file.Position = 0;
            return null;
        }
        return new(hash, length, patchBytes, anchor.Id, null);
    }

    private static string HashOf(Stream stream, long length)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buf = new byte[1 << 20];
        for (long left = length; left > 0;)
        {
            int n = stream.Read(buf, 0, (int)Math.Min(buf.Length, left));
            if (n <= 0) throw new IOException("The file became shorter while the revision was taken.");
            sha.AppendData(buf, 0, n);
            left -= n;
        }
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    /// <summary>A write-only stream that passes everything on and keeps the length and the SHA-256 of it.</summary>
    private sealed class HashingStream(Stream inner) : Stream
    {
        private readonly IncrementalHash _sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private string? _hash;

        /// <summary>Bytes written so far.</summary>
        public long Written { get; private set; }
        public string Hash => _hash ??= Convert.ToHexString(_sha.GetHashAndReset());

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Written;
        public override long Position { get => Written; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _sha.AppendData(buffer);
            Written += buffer.Length;
            inner.Write(buffer);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _sha.Dispose();
            base.Dispose(disposing);
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
        foreach (var b in Directory.EnumerateFiles(blobs))
            if (Path.GetExtension(b) is ".rev" or ".dlt" && !File.Exists(MetaOf(folder, Path.GetFileNameWithoutExtension(b))) && File.GetLastWriteTimeUtc(b) < fresh) FileSwap.TryDelete(b);
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
                if (m is null || m.Id != Path.GetFileNameWithoutExtension(json)) continue;
                var revision = ToRevision(m);
                if (File.Exists(BlobOf(folder, revision))) list.Add(revision);
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
        if (rev.IsPatch)
        {
            if (rev.Size > Array.MaxLength) throw new IOException("The revision is larger than 2 GiB; export it to a file instead.");
            using var ms = new MemoryStream((int)rev.Size);
            Materialize(folder, rev, ms);
            return ms.ToArray();
        }
        var bytes = File.ReadAllBytes(BlobOf(folder, id));
        if (bytes.Length != rev.Size || Convert.ToHexString(SHA256.HashData(bytes)) != rev.Sha256)
            throw new InvalidDataException("The stored revision " + id + " does not match its hash.");
        return bytes;
    }

    /// <summary>The first bytes of a revision, for a preview; the hash is not checked.</summary>
    public byte[] ReadPrefix(string path, string id, int max)
    {
        string folder = FolderOf(path);
        var rev = List(path).FirstOrDefault(r => r.Id == id) ?? throw new FileNotFoundException("No such revision: " + id);
        if (rev.IsPatch)
        {
            using var ms = new MemoryStream();
            Materialize(folder, rev, ms, max);
            return ms.ToArray();
        }
        using var fs = new FileStream(BlobOf(folder, id), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buf = new byte[(int)Math.Min(max, fs.Length)];
        fs.ReadExactly(buf);
        return buf;
    }

    /// <summary>Copies a revision to <paramref name="destination"/> (for handing it to a compare tool, or to keep it as a file); a revision is never held in memory. The file is removed when the copy fails.</summary>
    public void Export(string path, string id, string destination)
    {
        string folder = FolderOf(path);
        var rev = List(path).FirstOrDefault(r => r.Id == id) ?? throw new FileNotFoundException("No such revision: " + id);
        using var fs = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        try
        {
            Materialize(folder, rev, fs);
            fs.Flush(true);
        }
        catch
        {
            fs.Dispose();
            FileSwap.TryDelete(destination);
            throw;
        }
    }

    /// <summary>
    /// Writes the bytes of a revision, or its first <paramref name="limit"/> bytes, to <paramref name="output"/>. A whole revision is checked against its size and hash
    /// (<see cref="InvalidDataException"/>); a patch is applied to the whole copy it was made against.
    /// </summary>
    private void Materialize(string folder, HistoryRevision rev, Stream output, long limit = long.MaxValue)
    {
        bool whole = limit >= rev.Size;
        using var sink = whole ? new HashingStream(output) : null;
        Stream target = sink ?? output;
        if (!rev.IsPatch)
        {
            using var fs = new FileStream(BlobOf(folder, rev.Id), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
            var buf = new byte[1 << 16];
            for (long copied = 0, end = Math.Min(limit, rev.Size); copied < end;)
            {
                int n = fs.Read(buf, 0, (int)Math.Min(buf.Length, end - copied));
                if (n <= 0) break;
                target.Write(buf, 0, n);
                copied += n;
            }
        }
        else
        {
            var anchor = ListFolder(folder).FirstOrDefault(r => r.Id == rev.BaseId && !r.IsPatch)
                ?? throw new InvalidDataException("The whole copy that the revision " + rev.Id + " is a patch against is gone.");
            using var source = File.OpenHandle(BlobOf(folder, anchor.Id), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
            using var patch = new FileStream(PatchOf(folder, rev.Id), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
            BinaryDelta.Apply(source, RandomAccess.GetLength(source), patch, target, limit);
        }
        if (sink is not null && (sink.Written != rev.Size || sink.Hash != rev.Sha256))
            throw new InvalidDataException("The stored revision " + rev.Id + " does not match its hash.");
    }

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
        RaiseChanged(path);
    }

    public void SetLabel(string path, string id, string? label) => Update(path, id, m => m.Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim());

    /// <summary>Keeps the revision out of reach of retention until <see cref="Unpin"/> is called with the same owner.</summary>
    public void Pin(string path, string id, string owner) => Update(path, id, m => { if (!m.Pins.Contains(owner)) m.Pins.Add(owner); });

    public void Unpin(string path, string id, string owner) => Update(path, id, m => m.Pins.Remove(owner));

    // ---- retention ----

    private void Delete(string folder, HistoryRevision r)
    {
        FileSwap.TryDelete(MetaOf(folder, r.Id));
        FileSwap.TryDelete(BlobOf(folder, r));
    }

    private void PruneFolder(string folder)
    {
        var revs = ListFolder(folder); // newest first
        var drop = new List<HistoryRevision>();
        // a whole copy stays as long as a patch that is kept is made against it; the patches are newer, so they are decided first
        bool Needed(HistoryRevision r) => revs.Any(x => x.BaseId == r.Id && !drop.Contains(x));
        bool Candidate(HistoryRevision r) => !r.Pinned && r.Id != revs[0].Id && !drop.Contains(r) && !Needed(r);
        var now = _clock.GetUtcNow().UtcDateTime;

        if (_policy.MaxAge is { } age)
            foreach (var r in revs.Where(r => now - r.TimeUtc > age).ToList())
                if (Candidate(r)) drop.Add(r);
        if (_policy.MaxRevisionsPerFile is { } count)
            for (int excess = revs.Count - drop.Count - Math.Max(1, count); excess > 0; excess--)
                if (revs.LastOrDefault(Candidate) is { } oldest) drop.Add(oldest); else break;
        if (_policy.MaxBytesPerFile is { } cap)
            while (revs.Where(r => !drop.Contains(r)).Sum(r => r.StoredBytes) > cap && revs.LastOrDefault(Candidate) is { } oldest) drop.Add(oldest);

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
        RaiseChanged(path);
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
                long sum = all.Sum(x => x.Rev.StoredBytes);
                // a pass goes from the oldest on; a whole copy that a patch still needs is passed over and found free by a later pass
                for (bool deleted = true; deleted && sum > total;)
                {
                    deleted = false;
                    foreach (var x in all.Where(x => !x.Rev.Pinned && !x.Newest).OrderBy(x => x.Rev.Id, StringComparer.Ordinal).ToList())
                    {
                        if (sum <= total) break;
                        if (all.Any(y => y.Folder == x.Folder && y.Rev.BaseId == x.Rev.Id)) continue;
                        Delete(x.Folder, x.Rev);
                        all.Remove(x);
                        sum -= x.Rev.StoredBytes;
                        deleted = true;
                    }
                }
            }
            return before - TotalBytesCore();
        }
    }

    private long TotalBytesCore() => Directory.Exists(_root) ? Directory.EnumerateDirectories(_root).Sum(f => ListFolder(f).Sum(r => r.StoredBytes)) : 0;

    public long TotalBytes()
    {
        lock (_gate) return TotalBytesCore();
    }

    // ---- search ----

    /// <summary>
    /// Looks for text in the older revisions of a file; newest revision first. Revisions of a secrets file are not searched. A revision kept as a patch is built again in a
    /// scratch file, one at a time, checked against its hash on the way, and searched there; one that cannot be built (its whole copy is gone, or a stored byte is damaged) is left out.
    /// </summary>
    public async Task<IReadOnlyList<HistorySearchHit>> SearchAsync(string path, SearchPattern pattern, CancellationToken ct = default)
    {
        if (SecretValues.IsSecretFile(path)) return [];
        var revisions = List(path);
        if (revisions.Count == 0) return [];
        string folder = FolderOf(path);
        var byId = revisions.ToDictionary(r => r.Id);
        var settings = new SearchSettings { MaxResults = int.MaxValue, StopAtLimit = false, Walk = new FileWalkOptions { UseIgnoreFiles = false, IncludeHidden = true } };
        var found = new List<HistorySearchHit>();
        if (revisions.Any(r => !r.IsPatch))
        {
            var (hits, _) = await SearchWorker.CollectAsync(pattern, new SearchScope([Path.Combine(folder, "blobs")]), settings, ct);
            found.AddRange(PerRevision(hits, byId));
        }

        string? scratch = null;
        try
        {
            foreach (var patched in revisions.Where(r => r.IsPatch))
            {
                ct.ThrowIfCancellationRequested();
                scratch ??= Directory.CreateTempSubdirectory("textida-history-").FullName;
                string copy = Path.Combine(scratch, patched.Id + ".rev");
                try
                {
                    using (var fs = new FileStream(copy, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16)) Materialize(folder, patched, fs);
                }
                catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    FileSwap.TryDelete(copy);
                    continue;
                }
                var (hits, _) = await SearchWorker.CollectAsync(pattern, new SearchScope([copy]), settings, ct);
                found.AddRange(PerRevision(hits, byId));
                FileSwap.TryDelete(copy);
            }
        }
        finally
        {
            if (scratch is not null)
                try { Directory.Delete(scratch, recursive: true); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the next search makes its own folder; the system cleans the temp folder */ }
        }
        return [.. found.OrderByDescending(h => h.Revision.Id, StringComparer.Ordinal)];
    }

    /// <summary>The hits grouped by the revision whose copy (<c>&lt;id&gt;.rev</c>) they were found in.</summary>
    private static IEnumerable<HistorySearchHit> PerRevision(IReadOnlyList<SearchHit> hits, Dictionary<string, HistoryRevision> byId) => hits
        .Where(h => h.Source.Path is not null && Path.GetExtension(h.Source.Path) == ".rev" && byId.ContainsKey(Path.GetFileNameWithoutExtension(h.Source.Path)))
        .GroupBy(h => Path.GetFileNameWithoutExtension(h.Source.Path!))
        .Select(g => new HistorySearchHit(byId[g.Key], g.LongCount(), g.Min(h => h.Line), g.OrderBy(h => h.Line).First().Preview));
}
