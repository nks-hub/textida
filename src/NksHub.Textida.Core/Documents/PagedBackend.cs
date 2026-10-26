using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Documents;

/// <summary>Creates <see cref="PagedBackend"/> instances.</summary>
public sealed class PagedBackendFactory(bool startIndexing = true, int indexBlockSize = 1 << 20) : IBackendFactory
{
    public static PagedBackendFactory Instance { get; } = new();
    public BackendCapabilities Capabilities => PagedBackend.Caps;
    public bool MapsFiles => true;
    public IByteBackend Create(ReadOnlyMemory<byte> bytes, TextCodec codec) => PagedBackend.FromMemory(bytes.Span, codec, startIndexing, indexBlockSize);
    public IByteBackend OpenFile(string path, TextCodec codec) => PagedBackend.FromFile(path, codec, startIndexing, indexBlockSize);
}

/// <summary>
/// <see cref="IByteBackend"/> over <see cref="PagedDocument"/>: positions are <c>long</c>, the original file stays mapped,
/// line numbers after edits come from the piece tree. There is no text of the whole document, only ranges.
/// </summary>
public sealed class PagedBackend : IByteBackend
{
    internal static readonly BackendCapabilities Caps = new("paged", long.MaxValue, SupportsUtf16: true, PositionsMayBeUnknown: true, MaterializesText: false);
    private const int Chunk = 1 << 16;

    private readonly MappedFileSourceOptions _options;
    private PagedDocument _doc;
    private string? _path;
    private int _bom;

    private PagedBackend(PagedDocument doc, string? path, MappedFileSourceOptions options)
    {
        _doc = doc;
        _path = path;
        _options = options;
        _bom = Codec.BomLengthIn(Head());
    }

    private ReadOnlySpan<byte> Head()
    {
        var head = new byte[(int)Math.Min(4, ByteLength)];
        _doc.Read(0, head);
        return head;
    }

    public static PagedBackend FromFile(string path, TextCodec codec, bool startIndexing = true, int indexBlockSize = 1 << 20)
    {
        var options = OptionsFor(codec, startIndexing, indexBlockSize);
        return new(PagedDocument.Open(path, options), Path.GetFullPath(path), options);
    }

    public static PagedBackend FromMemory(ReadOnlySpan<byte> bytes, TextCodec codec, bool startIndexing = true, int indexBlockSize = 1 << 20)
    {
        var options = OptionsFor(codec, startIndexing, indexBlockSize);
        return new(PagedDocument.OpenMemory(bytes, options), null, options);
    }

    private static MappedFileSourceOptions OptionsFor(TextCodec codec, bool startIndexing, int indexBlockSize)
    {
        return new() { Codec = codec, StartIndexing = startIndexing, IndexBlockSize = indexBlockSize };
    }

    /// <summary>Forbidden: a paged document has no whole text.</summary>
    [Obsolete("The paged backend has no Text: read a range with ReadText (PLAN 5.1).", error: true)]
    public string Text => throw new NotSupportedException();

    public BackendCapabilities Capabilities => Caps;
    public TextCodec Codec => _doc.Codec;

    public bool Supports(TextCodec codec) => MappedFileSource.SharesLineIndex(Codec, codec);

    public void Reinterpret(TextCodec codec)
    {
        _doc.Reinterpret(codec);
        _bom = codec.BomLengthIn(Head());
    }
    public long ByteLength => _doc.ByteLength;
    public string? BackingPath => _path;
    public (long Lines, bool Complete) LineIndexState => _doc.LineIndexState;
#pragma warning disable CS0067
    public event Action<BackendEdit>? EditedByBackend;
#pragma warning restore CS0067

    /// <summary>The underlying paged document, for scrolling and search code that works on its pieces.</summary>
    public PagedDocument Paged => _doc;

    public int Read(long offset, Span<byte> destination) => _doc.Read(offset, destination);
    public void Replace(long offset, long deleteLength, ReadOnlySpan<byte> insert) => _doc.Replace(offset, deleteLength, insert);
    public void CopyTo(Stream destination) => _doc.WriteTo(destination);

    public string ReadText(ByteRange range)
    {
        if (range.Length > Array.MaxLength) throw new NotSupportedException("A range over 2 GiB cannot be decoded in one piece.");
        var bytes = new byte[range.Length];
        _doc.Read(range.Start, bytes);
        return Codec.GetString(bytes);
    }

    public IByteSnapshot CreateSnapshot() => _doc.CreateSnapshot();

    public void ReplaceBackingFile(string tempPath, string targetPath)
    {
        _doc.Dispose();
        try
        {
            FileSwap.Replace(tempPath, targetPath);
        }
        catch
        {
            // the swap failed: keep the edited content (the temp file holds it) instead of reopening the stale file
            _doc = PagedDocument.OpenMemory(File.ReadAllBytes(tempPath), _options);
            _path = null;
            throw;
        }
        _doc = PagedDocument.Open(targetPath, _options);
        _path = Path.GetFullPath(targetPath);
    }

    // ---- positions ----

    private long LineStartOf(long line)
    {
        if (line == 0) return _bom;
        return _doc.TryGetLineStart(line, out long start) ? start : -1;
    }

    public bool TryGetPosition(long offset, out LinePosition position)
    {
        position = default;
        var (lines, complete) = _doc.LineIndexState;
        if (!complete || offset < _bom || offset > ByteLength) return false;
        long lo = 0, hi = lines - 1;
        while (lo < hi)
        {
            long mid = lo + (hi - lo + 1) / 2;
            long start = LineStartOf(mid);
            if (start < 0) return false;
            if (start <= offset) lo = mid; else hi = mid - 1;
        }
        long lineStart = LineStartOf(lo);
        if (lineStart < 0) return false;
        position = new(lo, CountChars(lineStart, offset));
        return true;
    }

    public bool TryGetOffset(LinePosition position, out long offset)
    {
        offset = 0;
        var (lines, complete) = _doc.LineIndexState;
        if (!complete || position.Line < 0 || position.Column < 0 || position.Line >= lines) return false;
        long start = LineStartOf(position.Line);
        if (start < 0) return false;
        long end = ByteLength;
        if (position.Line + 1 < lines)
        {
            long next = LineStartOf(position.Line + 1);
            if (next < 0) return false;
            int w = Codec.Alignment;
            var tail = new byte[2 * w];
            int n = (int)Math.Min(2 * w, next - start);
            _doc.Read(next - n, tail.AsSpan(0, n));
            bool crlf = n == 2 * w && LineScanner.Unit(tail, 0, Codec) == 13 && LineScanner.Unit(tail, w, Codec) == 10;
            end = next - (crlf ? 2 * w : w);
        }
        return WalkColumns(start, end, position.Column, out offset);
    }

    private byte[] ReadWindow(long from, long to)
    {
        var buf = new byte[Math.Min(ByteLength, to + 4) - from];
        _doc.Read(from, buf);
        return buf;
    }

    /// <summary>UTF-16 units between two boundaries, read in chunks that end on a boundary.</summary>
    private long CountChars(long start, long end)
    {
        long total = 0;
        for (long pos = start; pos < end;)
        {
            long stop = Math.Min(end, pos + Chunk);
            var buf = ReadWindow(pos, stop);
            int cut = (int)(stop - pos);
            while (stop < end && cut > 0 && !Codec.IsBoundary(buf, cut)) cut--;
            total += Codec.CountChars(buf.AsSpan(0, cut));
            pos += cut;
        }
        return total;
    }

    /// <summary>Walks units from <paramref name="start"/> until <paramref name="column"/> chars are passed; false when the column is past the line or splits a surrogate pair.</summary>
    private bool WalkColumns(long start, long end, long column, out long offset)
    {
        offset = start;
        long remaining = column;
        while (offset < end && remaining > 0)
        {
            long stop = Math.Min(end, offset + Chunk);
            var buf = ReadWindow(offset, stop);
            int cut = (int)(stop - offset);
            while (stop < end && cut > 0 && !Codec.IsBoundary(buf, cut)) cut--;
            var span = buf.AsSpan(0, cut);
            if (Codec.CountChars(span) <= remaining)
            {
                remaining -= Codec.CountChars(span);
                offset += cut;
                continue;
            }
            int i = 0;
            while (i < cut && remaining > 0)
            {
                i += Codec.ReadUnit(span[i..], out int chars);
                remaining -= chars;
            }
            if (remaining < 0) return false;
            offset += i;
        }
        return remaining == 0;
    }

    public void Dispose()
    {
        _doc.Dispose();
    }
}
