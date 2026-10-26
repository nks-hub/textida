namespace NksPad.Core.Documents;

/// <summary>Creates <see cref="PagedBackend"/> instances.</summary>
public sealed class PagedBackendFactory(bool startIndexing = true, int indexBlockSize = 1 << 20) : IBackendFactory
{
    public static PagedBackendFactory Instance { get; } = new();
    public BackendCapabilities Capabilities => PagedBackend.Caps;
    public bool MapsFiles => true;
    public IByteBackend Create(ReadOnlyMemory<byte> bytes, DetectedEncoding format) => PagedBackend.FromMemory(bytes.Span, format, startIndexing, indexBlockSize);
    public IByteBackend OpenFile(string path, DetectedEncoding format) => PagedBackend.FromFile(path, format, startIndexing, indexBlockSize);
}

/// <summary>
/// <see cref="IByteBackend"/> over <see cref="PagedDocument"/>: positions are <c>long</c>, the original file stays mapped,
/// line numbers after edits come from the piece tree. There is no text of the whole document, only ranges.
/// </summary>
public sealed class PagedBackend : IByteBackend
{
    internal static readonly BackendCapabilities Caps = new("paged", long.MaxValue, SupportsUtf16: false, PositionsMayBeUnknown: true, MaterializesText: false);
    private const int Chunk = 1 << 16;

    private readonly ByteCodec _codec;
    private readonly MappedFileSourceOptions _options;
    private readonly List<WeakReference<PagedSnapshot>> _snapshots = [];
    private PagedDocument _doc;
    private string? _path;

    private PagedBackend(PagedDocument doc, string? path, DetectedEncoding format, MappedFileSourceOptions options)
    {
        _doc = doc;
        _path = path;
        _options = options;
        Format = format;
        _codec = new ByteCodec(format);
    }

    public static PagedBackend FromFile(string path, DetectedEncoding format, bool startIndexing = true, int indexBlockSize = 1 << 20)
    {
        var options = OptionsFor(format, startIndexing, indexBlockSize);
        return new(PagedDocument.Open(path, options), Path.GetFullPath(path), format, options);
    }

    public static PagedBackend FromMemory(ReadOnlySpan<byte> bytes, DetectedEncoding format, bool startIndexing = true, int indexBlockSize = 1 << 20)
    {
        var options = OptionsFor(format, startIndexing, indexBlockSize);
        return new(PagedDocument.OpenMemory(bytes, options), null, format, options);
    }

    private static MappedFileSourceOptions OptionsFor(DetectedEncoding format, bool startIndexing, int indexBlockSize)
    {
        if (format.Kind is not (EncodingKind.Ascii or EncodingKind.Utf8 or EncodingKind.SingleByte))
            throw new NotSupportedException($"The paged backend cannot hold {format.Kind}: newline scanning is byte based.");
        return new() { Encoding = format.Encoding, StartIndexing = startIndexing, IndexBlockSize = indexBlockSize };
    }

    /// <summary>Forbidden: a paged document has no whole text.</summary>
    [Obsolete("The paged backend has no Text: read a range with ReadText (PLAN 5.1).", error: true)]
    public string Text => throw new NotSupportedException();

    public BackendCapabilities Capabilities => Caps;
    public DetectedEncoding Format { get; }
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
        return _codec.Decode(bytes);
    }

    public IByteSnapshot CreateSnapshot()
    {
        var snapshot = _doc.CreateSnapshot();
        _snapshots.RemoveAll(w => !w.TryGetTarget(out _));
        _snapshots.Add(new(snapshot));
        return snapshot;
    }

    public void ReplaceBackingFile(string tempPath, string targetPath)
    {
        foreach (var weak in _snapshots)
            if (weak.TryGetTarget(out var s)) s.Materialize();
        _snapshots.Clear();
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
        if (line == 0) return _codec.BomLength;
        return _doc.TryGetLineStart(line, out long start) ? start : -1;
    }

    public bool TryGetPosition(long offset, out LinePosition position)
    {
        position = default;
        var (lines, complete) = _doc.LineIndexState;
        if (!complete || offset < _codec.BomLength || offset > ByteLength) return false;
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
            var tail = new byte[2];
            int n = (int)Math.Min(2, next - start);
            _doc.Read(next - n, tail.AsSpan(0, n));
            end = next - (n == 2 && tail[0] == 13 && tail[1] == 10 ? 2 : 1);
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
            while (stop < end && cut > 0 && !_codec.IsBoundary(buf, cut)) cut--;
            total += _codec.CountChars(buf.AsSpan(0, cut));
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
            while (stop < end && cut > 0 && !_codec.IsBoundary(buf, cut)) cut--;
            var span = buf.AsSpan(0, cut);
            if (_codec.CountChars(span) <= remaining)
            {
                remaining -= _codec.CountChars(span);
                offset += cut;
                continue;
            }
            int i = 0;
            while (i < cut && remaining > 0)
            {
                i += _codec.ReadUnit(span[i..], out int chars);
                remaining -= chars;
            }
            if (remaining < 0) return false;
            offset += i;
        }
        return remaining == 0;
    }

    public void Dispose()
    {
        foreach (var weak in _snapshots)
            if (weak.TryGetTarget(out var s)) s.Materialize();
        _snapshots.Clear();
        _doc.Dispose();
    }
}
