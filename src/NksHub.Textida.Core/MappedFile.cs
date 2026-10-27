using System.IO.MemoryMappedFiles;
using System.Numerics;
using System.Runtime.InteropServices;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core;

/// <summary>
/// Read-only memory-mapped view of a whole file, exposed as byte spans with 64-bit offsets. Every page a reader touches stays in the
/// working set of the process until the system needs the memory; with a <see cref="ResidentBudget"/> the view gives the touched pages back itself.
/// </summary>
public sealed unsafe class MappedFile : IDisposable
{
    private const int ChunkShift = 20;

    private readonly MemoryMappedFile? _mmf;
    private readonly MemoryMappedViewAccessor? _view;
    private readonly byte* _ptr;
    private readonly byte[]? _pinned;
    private readonly SnapshotSource _snapshotSource;
    private int _disposed;
    private readonly long[]? _touched; // one bit per 1 MiB chunk read since its pages were last given back; null for memory, which is not trimmed
    private long _touchedChunks;
    private int _trimming;
    private readonly object _trimGate = new();

    public string Path { get; }
    public long Length { get; }

    /// <summary>
    /// Bytes of mapped pages the view may keep in the working set: once the chunks read since the last trim exceed it, their pages are given back
    /// to the system on the thread pool. They stay in the file cache of the system, so a later read faults them in without the disk. 0 keeps them.
    /// </summary>
    public long ResidentBudget { get; set; }

    /// <summary>Bytes in the 1 MiB chunks read since their pages were last given back; at least what the reads added to the working set.</summary>
    public long ResidentEstimate => Interlocked.Read(ref _touchedChunks) << ChunkShift;

    private MappedFile(string path, long length, MemoryMappedFile? mmf, MemoryMappedViewAccessor? view, byte* ptr, byte[]? pinned = null, FileStream? stream = null)
    {
        _pinned = pinned;
        Path = path;
        Length = length;
        _mmf = mmf;
        _view = view;
        _ptr = ptr;
        _snapshotSource = new SnapshotSource(length, stream, pinned);
        if (view is not null) _touched = new long[(((length + (1L << ChunkShift) - 1) >> ChunkShift) + 63) >> 6];
    }

    /// <summary>Maps the file for reading; other processes may keep reading, writing and deleting it.</summary>
    public static MappedFile Open(string path)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.RandomAccess);
        long length = fs.Length;
        if (length == 0)
        {
            fs.Dispose();
            return new MappedFile(path, 0, null, null, null);
        }

        MemoryMappedFile? mmf = null;
        MemoryMappedViewAccessor? view = null;
        bool pointerAcquired = false;
        try
        {
            mmf = MemoryMappedFile.CreateFromFile(fs, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: true);
            view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            byte* p = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
            pointerAcquired = true;
            return new MappedFile(path, length, mmf, view, p + view.PointerOffset, stream: fs);
        }
        catch
        {
            if (pointerAcquired) view!.SafeMemoryMappedViewHandle.ReleasePointer();
            view?.Dispose();
            mmf?.Dispose();
            fs.Dispose();
            throw;
        }
    }

    /// <summary>Wraps a copy of <paramref name="data"/> in pinned memory so an unsaved document can use the paged path without touching the disk.</summary>
    public static MappedFile FromMemory(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return new MappedFile("", 0, null, null, null);
        var pinned = GC.AllocateUninitializedArray<byte>(data.Length, pinned: true);
        data.CopyTo(pinned);
        return new MappedFile("", data.Length, null, null, (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(pinned)), pinned);
    }

    /// <summary>Returns a span over <paramref name="length"/> bytes at <paramref name="offset"/>.</summary>
    public ReadOnlySpan<byte> GetSpan(long offset, int length)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (offset < 0 || length < 0 || offset + length > Length) throw new ArgumentOutOfRangeException(nameof(offset));
        Touch(offset, length);
        return length == 0 ? default : new ReadOnlySpan<byte>(_ptr + offset, length);
    }

    /// <summary>Returns a span from <paramref name="offset"/> of at most <paramref name="maxLength"/> bytes, clamped to the file end.</summary>
    public ReadOnlySpan<byte> GetSpanClamped(long offset, long maxLength)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (offset < 0 || offset > Length) throw new ArgumentOutOfRangeException(nameof(offset));
        long n = Math.Min(Math.Min(maxLength, Length - offset), int.MaxValue);
        // shortcut: a clamped span counts for its first 1 MiB, since its readers stop at the first line breaks; ReadLines over longer lines is not counted beyond that
        Touch(offset, Math.Min(n, 1L << ChunkShift));
        return n <= 0 ? default : new ReadOnlySpan<byte>(_ptr + offset, (int)n);
    }

    /// <summary>Notes the chunks of a read; the read that takes the count over the budget starts a trim.</summary>
    private void Touch(long offset, long length)
    {
        if (_touched is null || length <= 0) return;
        long added = 0;
        for (long c = offset >> ChunkShift, last = (offset + length - 1) >> ChunkShift; c <= last; c++)
        {
            long bit = 1L << (int)(c & 63);
            ref long word = ref _touched[c >> 6];
            if ((Volatile.Read(ref word) & bit) == 0 && (Interlocked.Or(ref word, bit) & bit) == 0) added++;
        }
        if (added > 0 && OverBudget(Interlocked.Add(ref _touchedChunks, added)) && Interlocked.CompareExchange(ref _trimming, 1, 0) == 0)
            ThreadPool.UnsafeQueueUserWorkItem(static file => file.TrimWhileOverBudget(), this, preferLocal: false);
    }

    private bool OverBudget(long chunks) => ResidentBudget > 0 && chunks << ChunkShift > ResidentBudget;

    private void TrimWhileOverBudget()
    {
        do
        {
            Trim();
            Volatile.Write(ref _trimming, 0);
        } while (OverBudget(Interlocked.Read(ref _touchedChunks)) && Volatile.Read(ref _disposed) == 0 && Interlocked.CompareExchange(ref _trimming, 1, 0) == 0);
    }

    /// <summary>
    /// Gives the pages of every chunk read since the last trim back to the system. A reader that still holds a span of such a chunk only faults
    /// its pages in again: the mapping stays, and the bytes are the same.
    /// </summary>
    internal void Trim()
    {
        if (_touched is null) return;
        lock (_trimGate)
        {
            for (int w = 0; w < _touched.Length; w++)
            {
                if (Volatile.Read(ref _touched[w]) == 0) continue;
                long bits = Interlocked.Exchange(ref _touched[w], 0);
                Interlocked.Add(ref _touchedChunks, -BitOperations.PopCount((ulong)bits));
                for (; bits != 0; bits &= bits - 1)
                {
                    if (Volatile.Read(ref _disposed) != 0) return; // Dispose waits on the gate for this chunk only
                    long at = ((long)w << 6 | (long)BitOperations.TrailingZeroCount(bits)) << ChunkShift;
                    PageResidency.Release(_ptr + at, Math.Min(1L << ChunkShift, Length - at));
                }
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_trimGate)
        {
            if (_view is not null)
            {
                _view.SafeMemoryMappedViewHandle.ReleasePointer();
                _view.Dispose();
            }
            _mmf?.Dispose();
        }
        _snapshotSource.Release();
    }

    /// <summary>Opens the window in which this file is overwritten in place by the document that maps it; see <see cref="IInPlaceWriteScope"/>.</summary>
    internal IInPlaceWriteScope BeginInPlaceWrite()
    {
        _snapshotSource.BeginWrite();
        return new WriteScope(_snapshotSource);
    }

    private sealed class WriteScope(SnapshotSource source) : IInPlaceWriteScope
    {
        private int _done;

        public void Preserve(long offset, ReadOnlySpan<byte> old) => source.Preserve(offset, old);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0) source.EndWrite();
        }
    }

    internal SnapshotLease RetainSnapshot()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return new SnapshotLease(_snapshotSource);
    }

    internal sealed class SnapshotSource(long length, FileStream? stream, byte[]? pinned)
    {
        private int references = 1;
        private int invalidated;
        private DateTime lastWriteUtc = stream is null ? default : File.GetLastWriteTimeUtc(stream.SafeFileHandle);

        // In-place writes by the document itself: the old bytes of each overwritten range are kept for as long as a snapshot that
        // started before the write is alive, and the write time the file has afterwards is the new baseline, not a foreign change.
        private readonly object patchGate = new();
        private readonly List<(long Seq, long Offset, byte[] Old)> patches = [];
        private readonly SortedDictionary<long, int> activeStarts = [];
        private long patchSeq;
        private int writing;
        private long writeEpoch; // changes at the start and the end of every own write; guarded by patchGate

        public long BeginLease()
        {
            lock (patchGate)
            {
                activeStarts[patchSeq] = activeStarts.GetValueOrDefault(patchSeq) + 1;
                return patchSeq;
            }
        }

        public void EndLease(long start)
        {
            lock (patchGate)
            {
                if (--activeStarts[start] == 0) activeStarts.Remove(start);
                long oldest = activeStarts.Count == 0 ? long.MaxValue : activeStarts.Keys.First();
                patches.RemoveAll(p => p.Seq < oldest);
            }
        }

        public void BeginWrite()
        {
            lock (patchGate)
            {
                writing++;
                writeEpoch++;
            }
        }

        public void Preserve(long offset, ReadOnlySpan<byte> old)
        {
            lock (patchGate)
            {
                long seq = patchSeq++;
                if (activeStarts.Count > 0) patches.Add((seq, offset, old.ToArray()));
            }
        }

        public void EndWrite()
        {
            // the new baseline and the end of the write become visible together
            lock (patchGate)
            {
                if (stream is not null) lastWriteUtc = File.GetLastWriteTimeUtc(stream.SafeFileHandle);
                writing--;
                writeEpoch++;
            }
        }

        /// <summary>Puts the old bytes of the ranges written after the snapshot started over what was read, newest write first.</summary>
        private void Overlay(long offset, Span<byte> data, long start)
        {
            lock (patchGate)
            {
                for (int i = patches.Count - 1; i >= 0 && patches[i].Seq >= start; i--)
                {
                    var (_, at, old) = patches[i];
                    long from = Math.Max(at, offset), to = Math.Min(at + old.Length, offset + data.Length);
                    if (to > from) old.AsSpan((int)(from - at), (int)(to - from)).CopyTo(data.Slice((int)(from - offset), (int)(to - from)));
                }
            }
        }

        public void Retain()
        {
            int count;
            do
            {
                count = Volatile.Read(ref references);
                ObjectDisposedException.ThrowIf(count == 0, this);
            } while (Interlocked.CompareExchange(ref references, checked(count + 1), count) != count);
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref references) == 0) stream?.Dispose();
        }

        public int Read(long offset, Span<byte> destination, long start = long.MaxValue)
        {
            if (offset < 0 || offset > length) throw new ArgumentOutOfRangeException(nameof(offset));
            int count = (int)Math.Min(destination.Length, length - offset);
            if (stream is null)
            {
                if (count > 0) pinned.AsSpan((int)offset, count).CopyTo(destination);
                return count;
            }
            Validate();
            int done = 0;
            try
            {
                while (done < count)
                {
                    int read = RandomAccess.Read(stream.SafeFileHandle, destination.Slice(done, count - done), offset + done);
                    if (read == 0) throw new EndOfStreamException("The snapshot backing file was truncated.");
                    done += read;
                }
            }
            catch (IOException error)
            {
                Interlocked.Exchange(ref invalidated, 1);
                throw new SnapshotInvalidatedException("The snapshot backing file could not be read.", error);
            }
            Validate();
            Overlay(offset, destination[..done], start);
            return done;
        }

        private void Validate()
        {
            if (Volatile.Read(ref invalidated) != 0) throw new SnapshotInvalidatedException();
            while (true)
            {
                long epoch;
                lock (patchGate) epoch = writeEpoch;
                long currentLength;
                DateTime currentWriteUtc;
                try
                {
                    currentLength = RandomAccess.GetLength(stream!.SafeFileHandle);
                    currentWriteUtc = File.GetLastWriteTimeUtc(stream.SafeFileHandle);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Interlocked.Exchange(ref invalidated, 1);
                    throw new SnapshotInvalidatedException("The snapshot backing file metadata could not be read.", error);
                }
                bool changed;
                lock (patchGate)
                {
                    // an own write started or ended while the metadata was read: the values may belong to either side of it, so read them again
                    if (epoch != writeEpoch) continue;
                    changed = currentLength != length || (currentWriteUtc != lastWriteUtc && writing == 0);
                }
                if (!changed) return;
                Interlocked.Exchange(ref invalidated, 1);
                throw new SnapshotInvalidatedException();
            }
        }
    }

    // Snapshots have no Dispose contract, so their lease releases the original file handle when collected.
    internal sealed class SnapshotLease : IDisposable
    {
        private SnapshotSource? source;
        private readonly long start;

        internal SnapshotLease(SnapshotSource source)
        {
            source.Retain();
            start = source.BeginLease();
            this.source = source;
        }

        ~SnapshotLease() => Release();

        internal int Read(long offset, Span<byte> destination)
        {
            try
            {
                return (source ?? throw new ObjectDisposedException(nameof(SnapshotLease))).Read(offset, destination, start);
            }
            finally
            {
                GC.KeepAlive(this);
            }
        }

        public void Dispose()
        {
            Release();
            GC.SuppressFinalize(this);
        }

        private void Release()
        {
            if (Interlocked.Exchange(ref source, null) is not { } s) return;
            s.EndLease(start);
            s.Release();
        }
    }
}

/// <summary>Takes pages of a read-only file mapping out of the working set of the process; the system keeps them in its file cache.</summary>
internal static unsafe partial class PageResidency
{
    private const int MadvDontNeed = 4; // the same value on Linux and macOS

    public static void Release(byte* at, long length)
    {
        if (length <= 0) return;
        if (OperatingSystem.IsWindows())
        {
            // on pages that are not locked the call fails with ERROR_NOT_LOCKED and still removes them from the working set
            VirtualUnlock(at, (nuint)length);
            return;
        }
        long start = (long)at & -(long)Environment.SystemPageSize;
        madvise((void*)start, (nuint)((long)at + length - start), MadvDontNeed);
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool VirtualUnlock(void* address, nuint size);

    [LibraryImport("libc")]
    private static partial int madvise(void* address, nuint length, int advice);
}
