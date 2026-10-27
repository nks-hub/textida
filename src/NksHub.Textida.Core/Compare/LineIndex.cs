using System.Buffers;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Compare;

/// <summary>One side of a comparison: the bytes of one revision read through a snapshot, from <paramref name="Start"/> (after the BOM) on.</summary>
public sealed record DiffSource(IByteSnapshot Snapshot, TextCodec Codec, long Start = 0)
{
    public long Length => Snapshot.ByteLength;

    /// <summary>The bytes of a document as they are now; nothing is decoded or copied beyond what the snapshot of its backend holds.</summary>
    public static DiffSource Of(Document doc) => new(doc.Backend.CreateSnapshot(), doc.Codec, doc.BomLength);

    public static DiffSource OfText(string text, TextCodec? codec = null)
    {
        codec ??= TextCodec.Utf8;
        return new(new ArraySnapshot(codec.Encode(text)), codec);
    }
}

/// <summary>Bytes of a file on disk, read in place; the file is opened shared so the editor and other programs keep working with it.</summary>
public sealed class FileByteSnapshot : IByteSnapshot, IDisposable
{
    private readonly Microsoft.Win32.SafeHandles.SafeFileHandle _handle;

    public FileByteSnapshot(string path)
    {
        _handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
        ByteLength = RandomAccess.GetLength(_handle);
    }

    public long ByteLength { get; }

    public int Read(long offset, Span<byte> destination) => offset >= ByteLength ? 0 : RandomAccess.Read(_handle, destination[..(int)Math.Min(destination.Length, ByteLength - offset)], offset);

    public void Dispose() => _handle.Dispose();
}

/// <summary>
/// Streams the lines of a byte range with the terminator rules of <see cref="LineScanner"/> without ever holding more than a buffer
/// (and the line being read). The visitor gets the offset of the line, its bytes without the terminator and the size of the
/// terminator in bytes (0 for the last line of the file); returning false stops the scan.
/// </summary>
internal static class LineScan
{
    public delegate bool Visitor(long start, ReadOnlySpan<byte> content, int terminator);

    public static void Run(DiffSource source, long from, Visitor visit, int bufferSize = 1 << 20, CancellationToken ct = default)
    {
        var codec = source.Codec;
        int w = codec.Alignment;
        long length = source.Length;
        var buf = ArrayPool<byte>.Shared.Rent(Math.Max(bufferSize, 16));
        try
        {
            int have = 0;
            long bufStart = from, read = from;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                while (have < buf.Length && read < length)
                {
                    int n = source.Snapshot.Read(read, buf.AsSpan(have, (int)Math.Min(buf.Length - have, length - read)));
                    if (n <= 0) break;
                    have += n;
                    read += n;
                }
                bool eof = read >= length;
                int pos = 0;
                while (true)
                {
                    int i = LineScanner.FindTerminator(buf.AsSpan(0, have), pos, codec);
                    if (i < 0 || i + w > have) break;
                    bool cr = LineScanner.Unit(buf, i, codec) == 13;
                    if (!eof && cr && i + w >= have) break;
                    int end = i, term = w;
                    if (!cr && end >= pos + w && LineScanner.Unit(buf, end - w, codec) == 13)
                    {
                        end -= w;
                        term += w;
                    }
                    if (!visit(bufStart + pos, buf.AsSpan(pos, end - pos), term)) return;
                    pos = i + w;
                }
                if (eof)
                {
                    visit(bufStart + pos, buf.AsSpan(pos, have - pos), 0);
                    return;
                }
                if (pos == 0 && have == buf.Length)
                {
                    var bigger = ArrayPool<byte>.Shared.Rent(buf.Length * 2);
                    buf.AsSpan(0, have).CopyTo(bigger);
                    ArrayPool<byte>.Shared.Return(buf);
                    buf = bigger;
                }
                else
                {
                    buf.AsSpan(pos, have - pos).CopyTo(buf);
                    bufStart += pos;
                    have -= pos;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }
}

/// <summary>
/// Where the lines of a source start, kept for every <see cref="Stride"/>th line only (8 bytes per 256 lines), so any line can be found
/// and read again by scanning at most one stride, whatever the size of the file.
/// </summary>
public sealed class LineIndex
{
    public const int Stride = 256;
    private const int DisplayBytes = 1 << 14;

    private readonly List<long> _marks;

    private LineIndex(DiffSource source, List<long> marks, long lines, byte[]? eol)
    {
        Source = source;
        _marks = marks;
        LineCount = lines;
        Eol = eol;
    }

    public DiffSource Source { get; }
    /// <summary>Lines of the source after the BOM; a source that ends with a terminator has an empty last line.</summary>
    public long LineCount { get; }
    /// <summary>The first terminator of the source (in its own encoding); null when it has none.</summary>
    public byte[]? Eol { get; }

    public static LineIndex Build(DiffSource source, CancellationToken ct = default)
    {
        var marks = new List<long>();
        long line = 0;
        byte[]? eol = null;
        LineScan.Run(source, source.Start, (start, content, term) =>
        {
            if (line++ % Stride == 0) marks.Add(start);
            if (eol is null && term > 0)
            {
                eol = new byte[term];
                source.Snapshot.Read(start + content.Length, eol);
            }
            return true;
        }, ct: ct);
        return new LineIndex(source, marks, line, eol);
    }

    /// <summary>Byte offset where the line starts; the length of the source for the line after the last one.</summary>
    public long OffsetOf(long line)
    {
        if (line >= LineCount) return Source.Length;
        if (line < 0) return Source.Start;
        long current = line / Stride * Stride, offset = _marks[(int)(line / Stride)];
        if (current == line) return offset;
        LineScan.Run(Source, offset, (start, _, _) =>
        {
            if (current == line)
            {
                offset = start;
                return false;
            }
            current++;
            return true;
        }, 1 << 16);
        return offset;
    }

    /// <summary>The line that contains the byte at <paramref name="offset"/>, the greatest line that starts at or before it.</summary>
    public long LineOf(long offset)
    {
        if (offset <= Source.Start) return 0;
        int lo = 0, hi = _marks.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_marks[mid] <= offset) lo = mid; else hi = mid - 1;
        }
        long line = (long)lo * Stride, result = line;
        LineScan.Run(Source, _marks[lo], (start, _, _) =>
        {
            if (start > offset) return false;
            result = line++;
            return true;
        }, 1 << 16);
        return result;
    }

    /// <summary>The first line that starts at or after <paramref name="offset"/>; <see cref="LineCount"/> when there is none.</summary>
    public long LineAtOrAfter(long offset)
    {
        long line = LineOf(offset);
        return line >= LineCount ? LineCount : OffsetOf(line) >= offset ? line : line + 1;
    }

    /// <summary>Decoded text of up to <paramref name="count"/> lines from <paramref name="first"/>; a very long line is cut for display.</summary>
    public List<string> ReadLines(long first, int count)
    {
        var lines = new List<string>(Math.Min(count, 256));
        if (first < 0 || first >= LineCount || count <= 0) return lines;
        long line = first / Stride * Stride;
        int w = Source.Codec.Alignment;
        LineScan.Run(Source, _marks[(int)(first / Stride)], (_, content, _) =>
        {
            if (line++ < first) return true;
            lines.Add(Source.Codec.GetString(content[..Math.Min(content.Length, DisplayBytes / w * w)]));
            return lines.Count < count;
        }, 1 << 16);
        return lines;
    }
}
