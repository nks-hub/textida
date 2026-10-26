using System.Buffers.Binary;
using NksHub.Textida.Core.Journal;

namespace NksHub.Textida.Core.Documents;

/// <summary>Test seams of the in-place write: where it can stop and how slow its flush is.</summary>
internal sealed class PatchHooks
{
    /// <summary>Called after a step (<c>journal</c>, <c>data-partial</c>, <c>data</c>, <c>flush</c>, <c>commit-partial</c>, <c>commit</c>); throwing <see cref="PatchCrash"/> stops the write as a crash would.</summary>
    public Action<string>? AfterStep;
    /// <summary>Runs before the data flush; a long wait here is the slow flush of a real disk.</summary>
    public Action<CancellationToken>? BeforeFlush;
}

/// <summary>Stops an in-place write without any cleanup, like a process that dies there.</summary>
internal sealed class PatchCrash : Exception;

/// <summary>What <see cref="InPlacePatch.Recover"/> found next to a file.</summary>
public enum PatchRecovery
{
    /// <summary>No journal.</summary>
    None,
    /// <summary>The journal was incomplete, so no data had been written; the file is as it was before the save.</summary>
    Discarded,
    /// <summary>The write was committed; only the journal was left.</summary>
    Committed,
    /// <summary>The write was not committed; the original bytes were put back.</summary>
    RolledBack,
    /// <summary>The file no longer has the length the journal expects; it was left alone.</summary>
    Foreign,
}

/// <summary>
/// Writes changed bytes into a file where they are, with a journal next to it so a crash at any step can be completed or undone:
/// the journal (old and new bytes of every changed chunk) is flushed first, then the data, then a commit mark, then the journal goes.
/// Recovery without the commit mark puts the old bytes back, so the file is either what it was or what the save wrote.
/// </summary>
internal static class InPlacePatch
{
    public const int ChunkBytes = 1 << 20;
    /// <summary>Above this many changed bytes a save streams the whole file instead (the journal would be as large as the change).</summary>
    public const long MaxPatchBytes = 256L << 20;
    private static ReadOnlySpan<byte> Magic => "NKSPATCH"u8;
    private static ReadOnlySpan<byte> CommitMark => "COMMITOK"u8;
    private const int HeaderSize = 8 + 8 + 4 + 4;
    private const int RecordHeader = 8 + 4 + 4;

    public static string JournalPathOf(string target) => target + ".textida-patch";

    private readonly record struct Chunk(long Offset, int Length);

    /// <summary>The chunks of <paramref name="ranges"/> whose bytes in the file differ from <paramref name="source"/>.</summary>
    private static List<Chunk> Plan(FileStream file, IReadOnlyList<ByteRange> ranges, IByteSnapshot source, CancellationToken ct)
    {
        var chunks = new List<Chunk>();
        var old = new byte[ChunkBytes];
        var now = new byte[ChunkBytes];
        foreach (var range in ranges)
            for (long at = range.Start; at < range.End;)
            {
                ct.ThrowIfCancellationRequested();
                int n = (int)Math.Min(ChunkBytes, range.End - at);
                ReadExactly(file, at, old.AsSpan(0, n));
                ReadExactly(source, at, now.AsSpan(0, n));
                if (!old.AsSpan(0, n).SequenceEqual(now.AsSpan(0, n))) chunks.Add(new Chunk(at, n));
                at += n;
            }
        return chunks;
    }

    private static void ReadExactly(FileStream file, long offset, Span<byte> buffer)
    {
        file.Position = offset;
        file.ReadExactly(buffer);
    }

    private static void ReadExactly(IByteSnapshot source, long offset, Span<byte> buffer)
    {
        for (int done = 0; done < buffer.Length;)
        {
            int n = source.Read(offset + done, buffer[done..]);
            if (n == 0) throw new EndOfStreamException("The snapshot ended before its declared length.");
            done += n;
        }
    }

    /// <summary>Writes the bytes of <paramref name="ranges"/> from <paramref name="source"/> into <paramref name="target"/>; the length of the file must not change.</summary>
    public static void Apply(string target, IReadOnlyList<ByteRange> ranges, IByteSnapshot source, CancellationToken ct, PatchHooks? hooks = null)
    {
        string journalPath = JournalPathOf(target);
        using var file = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.RandomAccess);
        if (file.Length != source.ByteLength) throw new IOException("The file has another length than the document.");
        var chunks = Plan(file, ranges, source, ct);
        if (chunks.Count == 0) return;

        bool dataStarted = false;
        try
        {
            WriteJournal(journalPath, file, chunks, source, ct);
            hooks?.AfterStep?.Invoke("journal");

            dataStarted = true;
            var buffer = new byte[ChunkBytes];
            for (int i = 0; i < chunks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var c = chunks[i];
                ReadExactly(source, c.Offset, buffer.AsSpan(0, c.Length));
                file.Position = c.Offset;
                file.Write(buffer, 0, c.Length);
                if (i == 0 && chunks.Count > 1) hooks?.AfterStep?.Invoke("data-partial");
            }
            hooks?.AfterStep?.Invoke("data");
            hooks?.BeforeFlush?.Invoke(ct);
            ct.ThrowIfCancellationRequested();
            file.Flush(true);
            hooks?.AfterStep?.Invoke("flush");
            ct.ThrowIfCancellationRequested();
            Commit(journalPath, hooks);
        }
        catch (Exception e) when (e is not PatchCrash)
        {
            if (dataStarted) TryRollback(journalPath, file);
            else TryDelete(journalPath);
            throw;
        }
        hooks?.AfterStep?.Invoke("commit");
        TryDelete(journalPath);
    }

    private static void WriteJournal(string path, FileStream file, List<Chunk> chunks, IByteSnapshot source, CancellationToken ct)
    {
        using var j = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1);
        var header = new byte[HeaderSize];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(8), file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), chunks.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), Crc32.Finish(Crc32.Update(Crc32.Initial, header.AsSpan(0, 20))));
        j.Write(header);
        var old = new byte[ChunkBytes];
        var now = new byte[ChunkBytes];
        var rh = new byte[RecordHeader];
        foreach (var c in chunks)
        {
            ct.ThrowIfCancellationRequested();
            ReadExactly(file, c.Offset, old.AsSpan(0, c.Length));
            ReadExactly(source, c.Offset, now.AsSpan(0, c.Length));
            BinaryPrimitives.WriteInt64LittleEndian(rh, c.Offset);
            BinaryPrimitives.WriteInt32LittleEndian(rh.AsSpan(8), c.Length);
            uint crc = Crc32.Update(Crc32.Initial, rh.AsSpan(0, 12));
            crc = Crc32.Update(crc, old.AsSpan(0, c.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(rh.AsSpan(12), Crc32.Finish(Crc32.Update(crc, now.AsSpan(0, c.Length))));
            j.Write(rh);
            j.Write(old, 0, c.Length);
            j.Write(now, 0, c.Length);
        }
        j.Flush(true);
    }

    private static void Commit(string journalPath, PatchHooks? hooks)
    {
        var mark = new byte[CommitMark.Length + 4];
        CommitMark.CopyTo(mark);
        BinaryPrimitives.WriteUInt32LittleEndian(mark.AsSpan(8), Crc32.Finish(Crc32.Update(Crc32.Initial, mark.AsSpan(0, 8))));
        using var j = new FileStream(journalPath, FileMode.Append, FileAccess.Write, FileShare.None, 1);
        j.Write(mark, 0, 6);
        j.Flush(true);
        hooks?.AfterStep?.Invoke("commit-partial");
        j.Write(mark, 6, mark.Length - 6);
        j.Flush(true);
    }

    private sealed record JournalContent(long TargetLength, List<(long Offset, byte[] Old)> Records, bool Committed);

    /// <summary>The records of a complete journal; null when the journal is cut short or damaged, which means the data was never touched.</summary>
    private static JournalContent? Read(string path)
    {
        using var j = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1);
        var header = new byte[HeaderSize];
        if (j.Read(header) != HeaderSize || !header.AsSpan(0, 8).SequenceEqual(Magic)) return null;
        if (BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(20)) != Crc32.Finish(Crc32.Update(Crc32.Initial, header.AsSpan(0, 20)))) return null;
        long length = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(8));
        int count = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(16));
        var records = new List<(long, byte[])>(Math.Min(count, 1024));
        var rh = new byte[RecordHeader];
        for (int i = 0; i < count; i++)
        {
            if (j.Read(rh) != RecordHeader) return null;
            long offset = BinaryPrimitives.ReadInt64LittleEndian(rh);
            int n = BinaryPrimitives.ReadInt32LittleEndian(rh.AsSpan(8));
            if (n is <= 0 or > ChunkBytes || offset < 0 || offset + n > length) return null;
            var old = new byte[n];
            var now = new byte[n];
            if (j.Read(old) != n || j.Read(now) != n) return null;
            uint crc = Crc32.Update(Crc32.Initial, rh.AsSpan(0, 12));
            crc = Crc32.Update(crc, old);
            if (BinaryPrimitives.ReadUInt32LittleEndian(rh.AsSpan(12)) != Crc32.Finish(Crc32.Update(crc, now))) return null;
            records.Add((offset, old));
        }
        var mark = new byte[CommitMark.Length + 4];
        bool committed = j.Read(mark) == mark.Length && mark.AsSpan(0, 8).SequenceEqual(CommitMark)
            && BinaryPrimitives.ReadUInt32LittleEndian(mark.AsSpan(8)) == Crc32.Finish(Crc32.Update(Crc32.Initial, mark.AsSpan(0, 8)));
        return new JournalContent(length, records, committed);
    }

    private static void Restore(JournalContent content, FileStream file)
    {
        foreach (var (offset, old) in content.Records)
        {
            file.Position = offset;
            file.Write(old);
        }
        file.Flush(true);
    }

    private static void TryRollback(string journalPath, FileStream file)
    {
        try
        {
            if (Read(journalPath) is { } content) Restore(content, file);
            TryDelete(journalPath);
        }
        catch (IOException) { } // the journal stays, so the next start finishes the rollback
    }

    /// <summary>Finishes or undoes an in-place write that a crash interrupted; call before the file is opened.</summary>
    public static PatchRecovery Recover(string target)
    {
        string journalPath = JournalPathOf(target);
        if (!File.Exists(journalPath)) return PatchRecovery.None;
        JournalContent? content;
        try { content = Read(journalPath); }
        catch (IOException) { return PatchRecovery.None; }
        if (content is null)
        {
            TryDelete(journalPath);
            return PatchRecovery.Discarded;
        }
        if (content.Committed)
        {
            TryDelete(journalPath);
            return PatchRecovery.Committed;
        }
        if (!File.Exists(target) || new FileInfo(target).Length != content.TargetLength)
        {
            TryDelete(journalPath);
            return PatchRecovery.Foreign;
        }
        using (var file = new FileStream(target, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            Restore(content, file);
        TryDelete(journalPath);
        return PatchRecovery.RolledBack;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
