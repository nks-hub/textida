using System.Security.Cryptography;
using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Journal;

namespace NksHub.Textida.Core.Changes;

internal sealed record BatchHunk(long Start, byte[] Old, byte[] New);

internal sealed record BatchFilePlan(int Index, string Path, string Codec, long BaseSize, string BaseSha, IReadOnlyList<BatchHunk> Hunks);

internal sealed record BatchIntent(int Index, long NewSize, string NewSha, string TempName);

/// <summary>Content arithmetic shared by apply and recovery.</summary>
internal static class BatchFiles
{
    public static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>Replaces the old bytes of ascending, disjoint hunks (positions in <paramref name="source"/>) with their new bytes.</summary>
    public static byte[] Apply(byte[] source, IReadOnlyList<BatchHunk> hunks)
    {
        var output = new MemoryStream(source.Length);
        long pos = 0;
        foreach (var h in hunks)
        {
            if (h.Start < pos || h.Start + h.Old.Length > source.Length || !source.AsSpan((int)h.Start, h.Old.Length).SequenceEqual(h.Old))
                throw new InvalidDataException("The content no longer matches the plan.");
            output.Write(source, (int)pos, (int)(h.Start - pos));
            output.Write(h.New);
            pos = h.Start + h.Old.Length;
        }
        output.Write(source, (int)pos, (int)(source.Length - pos));
        return output.ToArray();
    }

    /// <summary>Reverses <see cref="Apply"/>: finds each new text at its shifted position and puts the old one back.</summary>
    public static byte[] Revert(byte[] applied, IReadOnlyList<BatchHunk> hunks)
    {
        var inverse = new List<BatchHunk>(hunks.Count);
        long shift = 0;
        foreach (var h in hunks)
        {
            inverse.Add(new BatchHunk(h.Start + shift, h.New, h.Old));
            shift += h.New.Length - h.Old.Length;
        }
        return Apply(applied, inverse);
    }

    /// <summary>Writes the content next to the target and moves it into place; the temporary file never stays behind.</summary>
    public static void WriteReplacing(string target, string temp, byte[] content, Action? beforeSwap = null)
    {
        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
            {
                fs.Write(content);
                fs.Flush(true);
            }
            beforeSwap?.Invoke();
            FileSwap.Replace(temp, target);
        }
        finally
        {
            try { File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>The file a link points to, so the swap replaces the content and not the link.</summary>
    public static string ResolveTarget(string path)
    {
        try { return new FileInfo(path).ResolveLinkTarget(true)?.FullName ?? path; }
        catch (IOException) { return path; }
    }

    public static byte[]? TryRead(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
            var bytes = new byte[fs.Length];
            fs.ReadExactly(bytes);
            return bytes;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>Writes the journal of one batch (see FORMAT.md): one file per batch, locked while the batch runs.</summary>
internal sealed class BatchJournalWriter : IDisposable
{
    private const byte Layout = 1;
    private readonly JournalWriter _writer;
    private readonly FileStream _lock;
    private readonly Guid _id;

    private BatchJournalWriter(Guid id, JournalWriter writer, FileStream lockFile)
    {
        _id = id;
        _writer = writer;
        _lock = lockFile;
    }

    public string Path => _writer.Path;
    public Exception? Fault => _writer.Fault;

    public static string PathOf(string directory, Guid id) => System.IO.Path.Combine(directory, $"batch-{id:N}.njl");
    public static string LockPathOf(string journal) => System.IO.Path.ChangeExtension(journal, ".lock");

    public static BatchJournalWriter Create(string directory, Guid id)
    {
        Directory.CreateDirectory(directory);
        string path = PathOf(directory, id);
        var lockFile = new FileStream(LockPathOf(path), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        try
        {
            return new BatchJournalWriter(id, new JournalWriter(path, new JournalOptions { Fsync = FsyncPolicy.Manual }), lockFile);
        }
        catch
        {
            lockFile.Dispose();
            throw;
        }
    }

    private static byte[] Build(Action<BinaryWriter> fill)
    {
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Layout);
            fill(w);
        }
        return ms.ToArray();
    }

    public void Begin(string title, string origin, int files) =>
        _writer.Append(RecordType.BatchBegin, Build(w => { w.Write(_id.ToByteArray()); w.Write(title); w.Write(origin); w.Write(DateTime.UtcNow.Ticks); w.Write(files); }));

    public void Plan(BatchFilePlan p) => _writer.Append(RecordType.BatchFilePlan, Build(w =>
    {
        w.Write(_id.ToByteArray());
        w.Write(p.Index);
        w.Write(p.Path);
        w.Write(p.Codec);
        w.Write(p.BaseSize);
        w.Write(p.BaseSha);
        w.Write(p.Hunks.Count);
        foreach (var h in p.Hunks)
        {
            w.Write(h.Start);
            w.Write(h.Old.Length);
            w.Write(h.Old);
            w.Write(h.New.Length);
            w.Write(h.New);
        }
    }));

    public void Intent(BatchIntent i) =>
        _writer.Append(RecordType.BatchFileIntent, Build(w => { w.Write(_id.ToByteArray()); w.Write(i.Index); w.Write(i.NewSize); w.Write(i.NewSha); w.Write(i.TempName); }));

    public void Done(int index) => _writer.Append(RecordType.BatchFileDone, Build(w => { w.Write(_id.ToByteArray()); w.Write(index); }));

    public void Skipped(int index, string reason) =>
        _writer.Append(RecordType.BatchFileSkipped, Build(w => { w.Write(_id.ToByteArray()); w.Write(index); w.Write(reason); }));

    public void Flush() => _writer.Flush();

    /// <summary>Marks the batch finished and deletes its journal.</summary>
    public void Complete()
    {
        _writer.Append(RecordType.BatchEnd, Build(w => w.Write(_id.ToByteArray())));
        _writer.Flush();
        Dispose();
        BatchRecovery.TryDelete(_writer.Path);
    }

    public void Dispose()
    {
        _writer.Dispose();
        _lock.Dispose();
    }
}

public enum BatchFileState
{
    /// <summary>The file still has the content the plan is based on.</summary>
    NotStarted,
    /// <summary>The file has the content the plan produces.</summary>
    Applied,
    /// <summary>The file matches neither; someone else changed it. It is left alone.</summary>
    Diverged,
    Missing,
}

public sealed record PendingBatchFile(int Index, string Path, BatchFileState State, int Hunks);

/// <summary>A batch whose process died before it ended. Choose <see cref="Finish"/> or <see cref="RollBack"/>; either may be repeated after a crash.</summary>
public sealed class PendingBatch : IDisposable
{
    private readonly List<BatchFilePlan> _plans;
    private readonly Dictionary<int, BatchIntent> _intents;
    private FileStream? _lock;

    internal PendingBatch(Guid id, string title, string origin, DateTime created, string journalPath, List<BatchFilePlan> plans, Dictionary<int, BatchIntent> intents, FileStream lockFile)
    {
        Id = id;
        Title = title;
        Origin = origin;
        CreatedUtc = created;
        JournalPath = journalPath;
        _plans = plans;
        _intents = intents;
        _lock = lockFile;
    }

    public Guid Id { get; }
    public string Title { get; }
    public string Origin { get; }
    public DateTime CreatedUtc { get; }
    public string JournalPath { get; }

    /// <summary>The state of every file of the batch, derived from the content on disk now.</summary>
    public IReadOnlyList<PendingBatchFile> Files => [.. _plans.Select(p => new PendingBatchFile(p.Index, p.Path, Classify(p), p.Hunks.Count))];

    private BatchFileState Classify(BatchFilePlan p)
    {
        var bytes = BatchFiles.TryRead(BatchFiles.ResolveTarget(p.Path));
        if (bytes is null) return File.Exists(p.Path) ? BatchFileState.Diverged : BatchFileState.Missing;
        string sha = BatchFiles.Sha(bytes);
        if (bytes.Length == p.BaseSize && sha == p.BaseSha) return BatchFileState.NotStarted;
        if (_intents.TryGetValue(p.Index, out var intent) && bytes.Length == intent.NewSize && sha == intent.NewSha) return BatchFileState.Applied;
        return BatchFileState.Diverged;
    }

    /// <summary>Applies the plans of files that are still untouched; files that are applied stay; a file someone else changed is reported and left alone.</summary>
    public IReadOnlyList<FileOutcome> Finish() => Run(finish: true);

    /// <summary>Puts the old content back into files the batch had already changed (checked against the hash the batch wrote); untouched files stay as they are.</summary>
    public IReadOnlyList<FileOutcome> RollBack() => Run(finish: false);

    private IReadOnlyList<FileOutcome> Run(bool finish)
    {
        var outcomes = new List<FileOutcome>();
        foreach (var p in _plans)
        {
            CleanTemp(p);
            var state = Classify(p);
            string target = BatchFiles.ResolveTarget(p.Path);
            FileOutcome Make(ChangeStatus s, ChangeConflict c, string? msg = null) => new(p.Path, p.Path, s, c, s == ChangeStatus.Applied ? p.Hunks.Count : 0, s == ChangeStatus.Applied ? 0 : p.Hunks.Count, msg);
            try
            {
                switch (state, finish)
                {
                    case (BatchFileState.NotStarted, true):
                        {
                            var bytes = BatchFiles.TryRead(target) ?? throw new IOException("The file cannot be read.");
                            BatchFiles.WriteReplacing(target, TempOf(target, p.Index), BatchFiles.Apply(bytes, p.Hunks));
                            outcomes.Add(Make(ChangeStatus.Applied, ChangeConflict.None, "finished after the interruption"));
                            break;
                        }
                    case (BatchFileState.Applied, true):
                        outcomes.Add(Make(ChangeStatus.Applied, ChangeConflict.None, "was already applied"));
                        break;
                    case (BatchFileState.Applied, false):
                        {
                            var bytes = BatchFiles.TryRead(target) ?? throw new IOException("The file cannot be read.");
                            var old = BatchFiles.Revert(bytes, p.Hunks);
                            if (old.Length != p.BaseSize || BatchFiles.Sha(old) != p.BaseSha) throw new InvalidDataException("The restored content does not match the original.");
                            BatchFiles.WriteReplacing(target, TempOf(target, p.Index), old);
                            outcomes.Add(Make(ChangeStatus.Rejected, ChangeConflict.None, "restored to the original content"));
                            break;
                        }
                    case (BatchFileState.NotStarted, false):
                        outcomes.Add(Make(ChangeStatus.Rejected, ChangeConflict.None, "was never changed"));
                        break;
                    case (BatchFileState.Missing, _):
                        outcomes.Add(Make(ChangeStatus.Conflict, ChangeConflict.Missing));
                        break;
                    default:
                        outcomes.Add(Make(ChangeStatus.Conflict, ChangeConflict.ExternalChange, "changed by someone else; left as it is"));
                        break;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                outcomes.Add(Make(ChangeStatus.Failed, ChangeConflict.None, e.Message));
            }
        }
        // a failed file keeps the journal so the user can try again
        if (outcomes.All(o => o.Status != ChangeStatus.Failed)) Discard();
        return outcomes;
    }

    private static string TempOf(string target, int index) => target + ".textida-recover-" + index + ".tmp";

    private void CleanTemp(BatchFilePlan p)
    {
        if (!_intents.TryGetValue(p.Index, out var intent)) return;
        BatchRecovery.TryDelete(intent.TempName);
    }

    /// <summary>Deletes the journal without touching any file.</summary>
    public void Discard()
    {
        _lock?.Dispose();
        _lock = null;
        BatchRecovery.TryDelete(JournalPath);
    }

    public void Dispose()
    {
        _lock?.Dispose();
        _lock = null;
    }
}

/// <summary>Finds batches that were interrupted (the process died, the machine lost power) in a journal directory.</summary>
public static class BatchRecovery
{
    internal static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Batches whose process is gone and that did not end. Journals of a running editor are skipped; journals that are finished or empty are deleted.</summary>
    public static IReadOnlyList<PendingBatch> Find(string directory)
    {
        var found = new List<PendingBatch>();
        if (!Directory.Exists(directory)) return found;
        foreach (var path in Directory.GetFiles(directory, "batch-*.njl").Order(StringComparer.Ordinal))
        {
            FileStream lockFile;
            try
            {
                lockFile = new FileStream(BatchJournalWriter.LockPathOf(path), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException)
            {
                continue;
            }
            var batch = TryRead(path, lockFile);
            if (batch is null)
            {
                lockFile.Dispose();
                TryDelete(path);
            }
            else found.Add(batch);
        }
        return found;
    }

    private static PendingBatch? TryRead(string path, FileStream lockFile)
    {
        try
        {
            using var reader = new JournalReader(path);
            Guid id = default;
            string title = "", origin = "";
            DateTime created = default;
            bool begun = false, ended = false;
            var plans = new List<BatchFilePlan>();
            var intents = new Dictionary<int, BatchIntent>();
            int declared = 0;
            while (reader.TryRead(out var rec))
            {
                using var r = new BinaryReader(new MemoryStream(rec.Payload), Encoding.UTF8);
                if (r.ReadByte() != 1) return null;
                var batchId = new Guid(r.ReadBytes(16));
                switch (rec.Type)
                {
                    case RecordType.BatchBegin:
                        id = batchId;
                        title = r.ReadString();
                        origin = r.ReadString();
                        created = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
                        declared = r.ReadInt32();
                        begun = true;
                        break;
                    case RecordType.BatchFilePlan:
                        {
                            int index = r.ReadInt32();
                            string file = r.ReadString(), codec = r.ReadString();
                            long size = r.ReadInt64();
                            string sha = r.ReadString();
                            var hunks = new List<BatchHunk>();
                            for (int n = r.ReadInt32(); n > 0; n--)
                            {
                                long start = r.ReadInt64();
                                var old = r.ReadBytes(r.ReadInt32());
                                var neu = r.ReadBytes(r.ReadInt32());
                                hunks.Add(new BatchHunk(start, old, neu));
                            }
                            plans.Add(new BatchFilePlan(index, file, codec, size, sha, hunks));
                            break;
                        }
                    case RecordType.BatchFileIntent:
                        {
                            int index = r.ReadInt32();
                            intents[index] = new BatchIntent(index, r.ReadInt64(), r.ReadString(), r.ReadString());
                            break;
                        }
                    case RecordType.BatchEnd:
                        ended = true;
                        break;
                }
            }
            // a torn plan means the process died before any file was touched
            if (!begun || ended || plans.Count == 0 || plans.Count != declared) return null;
            return new PendingBatch(id, title, origin, created, path, plans, intents, lockFile);
        }
        catch (Exception e) when (e is JournalFormatException or IOException or EndOfStreamException)
        {
            return null;
        }
    }
}
