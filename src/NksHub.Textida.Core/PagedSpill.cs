using System.Buffers.Binary;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace NksHub.Textida.Core;

/// <summary>
/// OPEN DECISION (P7.1.2): the owner has not decided how much memory the add buffer and the undo history of one paged document may hold
/// before they move to disk. The value below is a placeholder, not a decided number.
/// </summary>
public static class PagedSpillPlaceholders
{
    /// <summary>Placeholder: bytes of inserted text, and separately of undo records, a paged document keeps in memory before spilling to disk.</summary>
    public const long MemoryBudgetBytes = 64L << 20;
}

/// <summary>
/// Scratch file for a paged document. It is opened delete-on-close, so a crash on Windows removes it with the process; elsewhere
/// <see cref="SweepOrphans"/> removes what dead processes left. Nothing in it is needed after a crash: the journal holds every edit.
/// </summary>
internal sealed class SpillFile : IDisposable
{
    private static readonly object SweepGate = new();
    private static readonly HashSet<string> Swept = new(StringComparer.OrdinalIgnoreCase);
    private readonly SafeFileHandle _handle;

    private SpillFile(SafeFileHandle handle) => _handle = handle;

    public static string DefaultDirectory => Path.Combine(Path.GetTempPath(), "textida-spill");

    public static SpillFile Create(string? directory, string kind)
    {
        string dir = directory ?? DefaultDirectory;
        Directory.CreateDirectory(dir);
        lock (SweepGate)
            if (Swept.Add(Path.GetFullPath(dir))) SweepOrphans(dir);
        string path = Path.Combine(dir, $"{Environment.ProcessId}-{Guid.NewGuid():N}-{kind}.spill");
        return new(File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete, FileOptions.DeleteOnClose | FileOptions.RandomAccess));
    }

    /// <summary>Deletes spill files whose owner process is gone. shortcut: a reused process id keeps a file until that process ends too.</summary>
    public static void SweepOrphans(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.spill"))
        {
            string name = Path.GetFileName(file);
            int dash = name.IndexOf('-');
            if (dash <= 0 || !int.TryParse(name.AsSpan(0, dash), out int pid) || pid == Environment.ProcessId || IsRunning(pid)) continue;
            try { File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException) { return false; }
    }

    public void Write(long position, ReadOnlySpan<byte> bytes) => RandomAccess.Write(_handle, bytes, position);

    public void ReadExactly(long position, Span<byte> destination)
    {
        while (!destination.IsEmpty)
        {
            int n = RandomAccess.Read(_handle, destination, position);
            if (n <= 0) throw new EndOfStreamException("The spill file is shorter than the data recorded in it.");
            destination = destination[n..];
            position += n;
        }
    }

    public void Truncate(long length) => RandomAccess.SetLength(_handle, length);

    public void Dispose() => _handle.Dispose();
}

/// <summary>
/// The append-only add buffer of a paged document. Up to the memory budget it is one array; the first append past it moves everything to a
/// spill file and later appends go there, so memory stays at the budget however much text is inserted. Appends come from the document's
/// thread, reads also from snapshots on other threads.
/// </summary>
internal sealed class AddBuffer(long budget, string? spillDirectory)
{
    private readonly object _gate = new();
    private byte[] _mem = new byte[4096];
    private SpillFile? _file;
    private long _length;

    public long Length => Volatile.Read(ref _length);
    public bool IsSpilled => _file is not null;

    public long Append(ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
        {
            long start = _length, need = start + bytes.Length;
            if (_file is null && need > Math.Min(budget, Array.MaxLength))
            {
                // a failed write leaves the buffer in memory and unchanged, so the edit that wanted the bytes can be refused cleanly
                var file = SpillFile.Create(spillDirectory, "add");
                try { file.Write(0, _mem.AsSpan(0, (int)start)); }
                catch
                {
                    file.Dispose();
                    throw;
                }
                _file = file;
                _mem = [];
            }
            if (_file is not null) _file.Write(start, bytes);
            else
            {
                if (need > _mem.Length) Array.Resize(ref _mem, (int)Math.Min(Array.MaxLength, Math.Max(need, _mem.Length * 2L)));
                bytes.CopyTo(_mem.AsSpan((int)start));
            }
            _length = need;
            return start;
        }
    }

    /// <summary>The bytes straight from memory, or false once the buffer lives on disk. For the document's own thread only.</summary>
    public bool TryGetSpan(long start, int length, out ReadOnlySpan<byte> span)
    {
        span = _file is null ? _mem.AsSpan((int)start, length) : default;
        return _file is null;
    }

    public void Read(long start, Span<byte> destination)
    {
        lock (_gate)
        {
            if (_file is null) _mem.AsSpan((int)start, destination.Length).CopyTo(destination);
            else _file.ReadExactly(start, destination);
        }
    }
}

internal sealed record PieceEdit(long Offset, Piece[] Removed, Piece[] Inserted);

/// <summary>
/// Undo or redo history. The newest records stay in memory up to the budget; older ones are written to a spill file, which works as a
/// stack (each record is followed by its length), so popping reads the last record and cuts the file back.
/// </summary>
internal sealed class EditStack(long budget, string? spillDirectory) : IDisposable
{
    private readonly List<PieceEdit> _hot = [];
    private long _hotCost;
    private SpillFile? _file;
    private long _fileEnd;
    private int _spilled;

    public int Count => _spilled + _hot.Count;

    public void Push(PieceEdit edit)
    {
        _hot.Add(edit);
        _hotCost += Cost(edit);
        if (_hotCost > budget) SpillOldest();
    }

    public PieceEdit Pop()
    {
        if (_hot.Count == 0) LoadLast();
        var edit = _hot[^1];
        _hot.RemoveAt(_hot.Count - 1);
        _hotCost -= Cost(edit);
        return edit;
    }

    public void Clear()
    {
        _hot.Clear();
        _hotCost = 0;
        _spilled = 0;
        _fileEnd = 0;
        _file?.Truncate(0);
    }

    public void Dispose() => _file?.Dispose();

    private static long Cost(PieceEdit e)
    {
        long cost = 64 + 48L * (e.Removed.Length + e.Inserted.Length);
        foreach (var p in e.Removed) cost += p.Pattern?.Length ?? 0;
        foreach (var p in e.Inserted) cost += p.Pattern?.Length ?? 0;
        return cost;
    }

    private void SpillOldest()
    {
        _file ??= SpillFile.Create(spillDirectory, "undo");
        long cost = _hotCost;
        int count = 0;
        // spill until half the budget is left, so the next pushes do not spill again at once
        while (count < _hot.Count - 1 && cost > budget / 2) cost -= Cost(_hot[count++]);
        using var ms = new MemoryStream();
        for (int i = 0; i < count; i++)
        {
            ms.SetLength(0);
            Serialize(ms, _hot[i]);
            _file.Write(_fileEnd, ms.GetBuffer().AsSpan(0, (int)ms.Length));
            _fileEnd += ms.Length;
            _spilled++;
        }
        _hot.RemoveRange(0, count);
        _hotCost = cost;
    }

    private void LoadLast()
    {
        Span<byte> tail = stackalloc byte[4];
        _file!.ReadExactly(_fileEnd - 4, tail);
        int length = BinaryPrimitives.ReadInt32LittleEndian(tail);
        var payload = new byte[length];
        _file.ReadExactly(_fileEnd - 4 - length, payload);
        _fileEnd -= 4L + length;
        _file.Truncate(_fileEnd);
        _spilled--;
        var edit = Deserialize(payload);
        _hot.Add(edit);
        _hotCost += Cost(edit);
    }

    /// <summary>Record: offset, removed pieces, inserted pieces, then the payload length as a trailer.</summary>
    private static void Serialize(MemoryStream ms, PieceEdit e)
    {
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(e.Offset);
            foreach (var pieces in new[] { e.Removed, e.Inserted })
            {
                w.Write(pieces.Length);
                foreach (var p in pieces)
                {
                    w.Write(p.Original);
                    w.Write(p.Start);
                    w.Write(p.Length);
                    w.Write(p.Pattern?.Length ?? -1);
                    if (p.Pattern is not null) w.Write(p.Pattern);
                }
            }
        }
        Span<byte> trailer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(trailer, (int)ms.Length);
        ms.Write(trailer);
    }

    private static PieceEdit Deserialize(byte[] payload)
    {
        using var r = new BinaryReader(new MemoryStream(payload));
        long offset = r.ReadInt64();
        var sets = new Piece[2][];
        for (int s = 0; s < 2; s++)
        {
            sets[s] = new Piece[r.ReadInt32()];
            for (int i = 0; i < sets[s].Length; i++)
            {
                bool original = r.ReadBoolean();
                long start = r.ReadInt64(), length = r.ReadInt64();
                int pattern = r.ReadInt32();
                sets[s][i] = new Piece(original, start, length, pattern < 0 ? null : r.ReadBytes(pattern));
            }
        }
        return new PieceEdit(offset, sets[0], sets[1]);
    }
}
