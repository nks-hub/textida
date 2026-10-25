using System.IO.MemoryMappedFiles;

namespace NksPad.Core;

/// <summary>Read-only memory-mapped view of a whole file, exposed as byte spans with 64-bit offsets.</summary>
public sealed unsafe class MappedFile : IDisposable
{
    private readonly MemoryMappedFile? _mmf;
    private readonly MemoryMappedViewAccessor? _view;
    private readonly byte* _ptr;

    public string Path { get; }
    public long Length { get; }

    private MappedFile(string path, long length, MemoryMappedFile? mmf, MemoryMappedViewAccessor? view, byte* ptr)
    {
        Path = path;
        Length = length;
        _mmf = mmf;
        _view = view;
        _ptr = ptr;
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

        var mmf = MemoryMappedFile.CreateFromFile(fs, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: false);
        var view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        byte* p = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
        return new MappedFile(path, length, mmf, view, p + view.PointerOffset);
    }

    /// <summary>Returns a span over <paramref name="length"/> bytes at <paramref name="offset"/>.</summary>
    public ReadOnlySpan<byte> GetSpan(long offset, int length)
    {
        if (offset < 0 || length < 0 || offset + length > Length) throw new ArgumentOutOfRangeException(nameof(offset));
        return length == 0 ? default : new ReadOnlySpan<byte>(_ptr + offset, length);
    }

    /// <summary>Returns a span from <paramref name="offset"/> of at most <paramref name="maxLength"/> bytes, clamped to the file end.</summary>
    public ReadOnlySpan<byte> GetSpanClamped(long offset, long maxLength)
    {
        if (offset < 0 || offset > Length) throw new ArgumentOutOfRangeException(nameof(offset));
        long n = Math.Min(Math.Min(maxLength, Length - offset), int.MaxValue);
        return n <= 0 ? default : new ReadOnlySpan<byte>(_ptr + offset, (int)n);
    }

    public void Dispose()
    {
        if (_view is not null)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _view.Dispose();
        }
        _mmf?.Dispose();
    }
}
