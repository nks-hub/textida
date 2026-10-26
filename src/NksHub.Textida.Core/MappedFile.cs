using System.IO.MemoryMappedFiles;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core;

/// <summary>Read-only memory-mapped view of a whole file, exposed as byte spans with 64-bit offsets.</summary>
public sealed unsafe class MappedFile : IDisposable
{
    private readonly MemoryMappedFile? _mmf;
    private readonly MemoryMappedViewAccessor? _view;
    private readonly byte* _ptr;
    private readonly byte[]? _pinned;
    private readonly SnapshotSource _snapshotSource;
    private int _disposed;

    public string Path { get; }
    public long Length { get; }

    private MappedFile(string path, long length, MemoryMappedFile? mmf, MemoryMappedViewAccessor? view, byte* ptr, byte[]? pinned = null, FileStream? stream = null)
    {
        _pinned = pinned;
        Path = path;
        Length = length;
        _mmf = mmf;
        _view = view;
        _ptr = ptr;
        _snapshotSource = new SnapshotSource(length, stream, pinned);
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
        return length == 0 ? default : new ReadOnlySpan<byte>(_ptr + offset, length);
    }

    /// <summary>Returns a span from <paramref name="offset"/> of at most <paramref name="maxLength"/> bytes, clamped to the file end.</summary>
    public ReadOnlySpan<byte> GetSpanClamped(long offset, long maxLength)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (offset < 0 || offset > Length) throw new ArgumentOutOfRangeException(nameof(offset));
        long n = Math.Min(Math.Min(maxLength, Length - offset), int.MaxValue);
        return n <= 0 ? default : new ReadOnlySpan<byte>(_ptr + offset, (int)n);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_view is not null)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _view.Dispose();
        }
        _mmf?.Dispose();
        _snapshotSource.Release();
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
        private readonly DateTime lastWriteUtc = stream is null ? default : File.GetLastWriteTimeUtc(stream.SafeFileHandle);

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

        public int Read(long offset, Span<byte> destination)
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
            return done;
        }

        private void Validate()
        {
            if (Volatile.Read(ref invalidated) != 0) throw new SnapshotInvalidatedException();
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
            if (currentLength != length || currentWriteUtc != lastWriteUtc)
            {
                Interlocked.Exchange(ref invalidated, 1);
                throw new SnapshotInvalidatedException();
            }
        }
    }

    // Snapshots have no Dispose contract, so their lease releases the original file handle when collected.
    internal sealed class SnapshotLease : IDisposable
    {
        private SnapshotSource? source;

        internal SnapshotLease(SnapshotSource source)
        {
            source.Retain();
            this.source = source;
        }

        ~SnapshotLease() => Release();

        internal int Read(long offset, Span<byte> destination)
        {
            try
            {
                return (source ?? throw new ObjectDisposedException(nameof(SnapshotLease))).Read(offset, destination);
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

        private void Release() => Interlocked.Exchange(ref source, null)?.Release();
    }
}
