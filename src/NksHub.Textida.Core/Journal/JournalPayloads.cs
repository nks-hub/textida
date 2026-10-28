using System.Buffers.Binary;
using Microsoft.Win32.SafeHandles;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Journal;

/// <summary>
/// OPEN DECISIONS (P7.1.4): the owner has not decided from which size on a change is recorded as a reference or a list instead of inline bytes,
/// nor from which size on a recovery asks before it replays. The values below are placeholders, not decided numbers.
/// </summary>
public static class JournalPlaceholders
{
    /// <summary>Placeholder, OPEN: inserted bytes of one edit above which the journal records a reference to a payload file.</summary>
    public const int MaxInlineEditBytes = 1 << 20;

    /// <summary>Placeholder, OPEN: inserted bytes of a document that its records may hold inline between two saves; past it every edit of more than <see cref="SmallEditBytes"/> goes to a payload file, so a recovery holds a bounded part of the journal in memory.</summary>
    public const long MaxInlineDocumentBytes = 32L << 20;

    /// <summary>Placeholder, OPEN: inserted bytes of an edit that always stay inline, whatever the inline budget of the document: typing.</summary>
    public const int SmallEditBytes = 4096;

    /// <summary>Placeholder, OPEN: edits of one change set above which the journal writes them to a payload file as a list instead of into the record (Replace All over a large file).</summary>
    public const int MaxInlineEditCount = 4096;

    /// <summary>Placeholder, OPEN: inserted bytes of the edits of one change set that may stand inline in its record; past it the change set is written as a list.</summary>
    public const int MaxInlineRecordBytes = 8 << 20;

    /// <summary>Placeholder, OPEN: bytes of changes of one document (inserted and removed) above which a recovery waits for the choice to restore or discard instead of replaying them at start.</summary>
    public const long AskBeforeRecoverBytes = 64L << 20;

    /// <summary>Placeholder, OPEN: most edits of one change set that a recovery rebuilds in memory; a longer list is reported as not restorable and stays in the journal.</summary>
    public const long MaxRecoveredEdits = 20_000_000;
}

/// <summary>
/// Where the bytes of a large edit live: <paramref name="Length"/> bytes at <paramref name="Offset"/> in the file <paramref name="File"/> of the
/// payload directory. The checksums of the first and the last 4 KiB and the length catch a missing, short or swapped file; <paramref name="FullCrc"/>, the checksum of every byte, catches a bit flip in the middle.
/// A reference of a journal before layout 8, or one written before the checksum of the whole block was ready, has no <paramref name="FullCrc"/> and is checked at its ends only.
/// </summary>
public sealed record PayloadRef(string File, long Offset, long Length, uint HeadCrc, uint TailCrc, uint? FullCrc = null);

/// <summary>
/// The edits of one change set that has too many of them for a record (Replace All): <paramref name="Count"/> entries one after the other in
/// <paramref name="File"/>, <paramref name="Length"/> bytes at <paramref name="Offset"/>. Unlike a <see cref="PayloadRef"/> the checksum covers every byte.
/// An entry is the start, the removed length and the inserted length as three little endian 64 bit integers, then the inserted bytes.
/// </summary>
public sealed record EditListRef(string File, long Offset, long Length, long Count, long InsertedBytes, long DeletedBytes, uint Crc);

/// <summary>
/// Payload files next to the journals (<c>payload</c> directory): append-only files that hold the inserted text of large edits. They are
/// named <c>&lt;session&gt;-&lt;document&gt;-&lt;kind&gt;.payload</c>, so a file belongs to a document and is deleted with it.
/// </summary>
internal static class JournalPayloads
{
    public const string DirectoryName = "payload";
    public const int SampleBytes = 4096;

    public static string DirectoryOf(string journalDirectory) => Path.Combine(journalDirectory, DirectoryName);

    public static string Prefix(string sessionId, Guid docId) => $"{sessionId}-{docId:N}";

    public static uint SampleCrc(ReadOnlySpan<byte> sample) => Crc32.Finish(Crc32.Update(Crc32.Initial, sample));

    /// <summary>Reads a referenced block and checks it against the reference; an <see cref="InvalidDataException"/> says why it cannot be used.</summary>
    public static byte[] Load(string payloadDirectory, PayloadRef r)
    {
        if (r.File.Length == 0 || r.File != Path.GetFileName(r.File) || r.Offset < 0 || r.Length < 0)
            throw new InvalidDataException("A large change refers to an invalid payload file.");
        if (r.Length > Array.MaxLength) throw new NotSupportedException("A large change over 2 GiB cannot be restored.");
        string path = Path.Combine(payloadDirectory, r.File);
        SafeFileHandle handle;
        try { handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new InvalidDataException($"The data of a large change is missing ({r.File}).");
        }
        using (handle)
        {
            if (RandomAccess.GetLength(handle) < r.Offset + r.Length) throw new InvalidDataException($"The data of a large change is incomplete ({r.File} is shorter than the journal says).");
            var bytes = new byte[r.Length];
            for (long done = 0; done < bytes.Length;)
            {
                int n = RandomAccess.Read(handle, bytes.AsSpan((int)done), r.Offset + done);
                if (n <= 0) throw new InvalidDataException($"The data of a large change is incomplete ({r.File}).");
                done += n;
            }
            int sample = (int)Math.Min(SampleBytes, bytes.Length);
            if (SampleCrc(bytes.AsSpan(0, sample)) != r.HeadCrc || SampleCrc(bytes.AsSpan(bytes.Length - sample)) != r.TailCrc)
                throw new InvalidDataException($"The data of a large change differs from what the journal recorded ({r.File}).");
            if (r.FullCrc is { } full && Crc32.Finish(Crc32.Update(Crc32.Initial, bytes)) != full)
                throw new InvalidDataException($"The data of a large change differs from what the journal recorded, inside the block ({r.File}).");
            return bytes;
        }
    }

    /// <summary>The checksum of <paramref name="length"/> bytes at <paramref name="offset"/> of a payload file, read through a handle of its own in steps of 1 MiB.</summary>
    public static uint BlockCrc(string payloadDirectory, string file, long offset, long length)
    {
        using var handle = File.OpenHandle(Path.Combine(payloadDirectory, file), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan);
        var buffer = new byte[(int)Math.Min(1 << 20, Math.Max(1, length))];
        uint crc = Crc32.Initial;
        for (long done = 0; done < length;)
        {
            int n = (int)Math.Min(buffer.Length, length - done);
            ReadAt(handle, buffer.AsSpan(0, n), offset + done, file);
            crc = Crc32.Update(crc, buffer.AsSpan(0, n));
            done += n;
        }
        return Crc32.Finish(crc);
    }

    /// <summary>Bytes of the header of one entry of an edit list: start, removed length, inserted length.</summary>
    public const int ListEntryHeaderBytes = 24;

    /// <summary>Inserted blocks up to this size are compared with the one before and share its array when equal (Replace All inserts the same text many times).</summary>
    private const int ListSharedBlockBytes = 64 << 10;

    /// <summary>
    /// Reads an edit list back as the edits it was made of. The whole list is checked against its checksum first, so nothing is built from a damaged
    /// file; an <see cref="InvalidDataException"/> says why a list cannot be used. Memory is the edits themselves: equal short blocks in a row share one array.
    /// </summary>
    public static List<ByteEdit> LoadList(string payloadDirectory, EditListRef r)
    {
        if (r.File.Length == 0 || r.File != Path.GetFileName(r.File) || r.Offset < 0 || r.Length < 0 || r.Count < 0 || r.InsertedBytes < 0 || r.DeletedBytes < 0)
            throw new InvalidDataException("A large change refers to an invalid payload file.");
        if (r.Count > r.Length / ListEntryHeaderBytes) throw new InvalidDataException($"A large change claims more edits than its data can hold ({r.File}).");
        if (r.Count > JournalPlaceholders.MaxRecoveredEdits) throw new NotSupportedException($"A change of more than {JournalPlaceholders.MaxRecoveredEdits} edits cannot be restored.");
        string path = Path.Combine(payloadDirectory, r.File);
        SafeFileHandle handle;
        try { handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new InvalidDataException($"The data of a large change is missing ({r.File}).");
        }
        using (handle)
        {
            if (RandomAccess.GetLength(handle) < r.Offset + r.Length) throw new InvalidDataException($"The data of a large change is incomplete ({r.File} is shorter than the journal says).");
            var buffer = new byte[1 << 20];
            uint crc = Crc32.Initial;
            for (long done = 0; done < r.Length;)
            {
                int n = (int)Math.Min(buffer.Length, r.Length - done);
                ReadAt(handle, buffer.AsSpan(0, n), r.Offset + done, r.File);
                crc = Crc32.Update(crc, buffer.AsSpan(0, n));
                done += n;
            }
            if (Crc32.Finish(crc) != r.Crc) throw new InvalidDataException($"The data of a large change differs from what the journal recorded ({r.File}).");

            var reader = new RegionReader(handle, r.Offset, r.Length, buffer, r.File);
            var edits = new List<ByteEdit>((int)Math.Min(r.Count, int.MaxValue));
            Span<byte> head = stackalloc byte[ListEntryHeaderBytes];
            byte[]? previous = null;
            var scratch = new byte[ListSharedBlockBytes];
            long inserted = 0, deleted = 0;
            for (long i = 0; i < r.Count; i++)
            {
                reader.ReadExactly(head);
                long start = BinaryPrimitives.ReadInt64LittleEndian(head), remove = BinaryPrimitives.ReadInt64LittleEndian(head[8..]), length = BinaryPrimitives.ReadInt64LittleEndian(head[16..]);
                if (start < 0 || remove < 0 || length < 0 || length > Array.MaxLength || start > long.MaxValue - remove) throw new InvalidDataException($"A large change holds an edit that cannot be right ({r.File}).");
                ReadOnlyMemory<byte> insert = default;
                if (length > 0 && length <= ListSharedBlockBytes)
                {
                    var block = scratch.AsSpan(0, (int)length);
                    reader.ReadExactly(block);
                    if (previous is null || !previous.AsSpan().SequenceEqual(block)) previous = block.ToArray();
                    insert = previous;
                }
                else if (length > 0)
                {
                    var big = new byte[length];
                    reader.ReadExactly(big);
                    insert = big;
                }
                edits.Add(new ByteEdit(new ByteRange(start, start + remove), insert));
                inserted += length;
                deleted += remove;
            }
            if (reader.Remaining != 0 || inserted != r.InsertedBytes || deleted != r.DeletedBytes)
                throw new InvalidDataException($"The data of a large change does not match what the journal recorded ({r.File}).");
            return edits;
        }
    }

    private static void ReadAt(SafeFileHandle handle, Span<byte> destination, long offset, string file)
    {
        while (!destination.IsEmpty)
        {
            int n = RandomAccess.Read(handle, destination, offset);
            if (n <= 0) throw new InvalidDataException($"The data of a large change is incomplete ({file}).");
            destination = destination[n..];
            offset += n;
        }
    }

    /// <summary>Sequential reads of one region of a file through a buffer; a read as large as the buffer goes straight into its target.</summary>
    private sealed class RegionReader(SafeFileHandle handle, long start, long length, byte[] buffer, string file)
    {
        private long _next = start;
        private readonly long _end = start + length;
        private int _have, _used;

        public long Remaining => _end - _next + (_have - _used);

        public void ReadExactly(Span<byte> destination)
        {
            while (!destination.IsEmpty)
            {
                if (_used < _have)
                {
                    int n = Math.Min(_have - _used, destination.Length);
                    buffer.AsSpan(_used, n).CopyTo(destination);
                    _used += n;
                    destination = destination[n..];
                    continue;
                }
                long left = _end - _next;
                if (left <= 0) throw new InvalidDataException($"The data of a large change is incomplete ({file}).");
                if (destination.Length >= buffer.Length)
                {
                    int n = (int)Math.Min(destination.Length, left);
                    ReadAt(handle, destination[..n], _next, file);
                    _next += n;
                    destination = destination[n..];
                    continue;
                }
                _have = (int)Math.Min(buffer.Length, left);
                _used = 0;
                ReadAt(handle, buffer.AsSpan(0, _have), _next, file);
                _next += _have;
            }
        }
    }

    /// <summary>Deletes the payload files of documents that are no longer alive, except those of a session whose journal still exists (it decides for itself).</summary>
    public static void Sweep(string journalDirectory, string ownSession, IReadOnlySet<Guid> aliveDocs)
    {
        string dir = DirectoryOf(journalDirectory);
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir, "*.payload"))
        {
            if (!TryParse(Path.GetFileName(file), out string session, out var doc) || aliveDocs.Contains(doc)) continue;
            if (session != ownSession && File.Exists(Path.Combine(journalDirectory, $"session-{session}.njl"))) continue;
            TryDelete(file);
        }
    }

    /// <summary>Deletes the files of one document that <paramref name="session"/> made, except the named ones.</summary>
    public static void DeleteOf(string journalDirectory, string session, Guid doc, IReadOnlySet<string>? keep = null)
    {
        string dir = DirectoryOf(journalDirectory);
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir, $"{session}-{doc:N}-*.payload"))
            if (keep is null || !keep.Contains(Path.GetFileName(file))) TryDelete(file);
    }

    /// <summary>Deletes the files of a document from every session (the document was closed).</summary>
    public static void DeleteAllOf(string journalDirectory, Guid doc)
    {
        string dir = DirectoryOf(journalDirectory);
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir, $"*-{doc:N}-*.payload")) TryDelete(file);
    }

    private static bool TryParse(string name, out string session, out Guid doc)
    {
        session = "";
        doc = default;
        var parts = name.Split('-');
        if (parts.Length < 3 || parts[0].Length != 32) return false;
        session = parts[0];
        return Guid.TryParseExact(parts[1], "N", out doc);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>An append-only payload file for blocks that no backend keeps on disk itself; the journal copies them here.</summary>
internal sealed class PayloadBlob : IDisposable
{
    private readonly SafeFileHandle _handle;

    public PayloadBlob(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        Name = name;
        _handle = File.OpenHandle(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
    }

    public string Name { get; }
    public long Length { get; private set; }

    public void Append(ReadOnlySpan<byte> bytes)
    {
        RandomAccess.Write(_handle, bytes, Length);
        Length += bytes.Length;
    }

    public void Sync() => RandomAccess.FlushToDisk(_handle);

    public void Dispose() => _handle.Dispose();
}
