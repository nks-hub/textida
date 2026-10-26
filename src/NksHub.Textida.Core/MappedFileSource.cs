using System.Diagnostics;
using System.Text;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core;

/// <summary>Tuning knobs for <see cref="MappedFileSource"/>.</summary>
public sealed class MappedFileSourceOptions
{
    /// <summary>Bytes per line-index block; the index keeps one counter per block.</summary>
    public int IndexBlockSize { get; init; } = 1 << 20;
    public int IndexParallelism { get; init; } = Math.Max(1, Environment.ProcessorCount - 2);
    public bool StartIndexing { get; init; } = true;
    /// <summary>Overrides detection. Reinterpreting a document updates it, but only to an encoding that shares the line index (see <c>MappedFileSource.SharesLineIndex</c>).</summary>
    public TextCodec? Codec { get; set; }
}

/// <summary>
/// Read-only paged text source over a memory-mapped file. Lines are indexed lazily on the thread pool
/// with a block table (cumulative terminator counts), so memory stays proportional to file size / block size.
/// Terminators are found per code unit, so UTF-16 and UTF-32 (either byte order) work like the byte encodings.
/// </summary>
public sealed class MappedFileSource : IPagedTextSource
{
    private readonly MappedFile _file;
    private readonly MappedFileSourceOptions _options;
    private int _blockSize;
    private int _blocks;
    private long[] _cum = [];
    private int[] _blockCounts = [];
    private bool[] _ready = [];
    private readonly object _pub = new();
    private CancellationTokenSource _cts = new();
    private int _published;
    private int _disposed;
    private long _lastProgress;
    private int _bomLength;

    public Task IndexTask { get; private set; } = Task.CompletedTask;
    public MappedFile File => _file;
    public string Path => _file.Path;
    public TextCodec Codec { get; private set; }
    public long ByteLength => _file.Length;
    public event EventHandler? IndexProgress;

    private MappedFileSource(MappedFile file, MappedFileSourceOptions options)
    {
        _file = file;
        _options = options;
        bool whole = file.Length <= 1 << 20;
        var codec = options.Codec ?? EncodingGuesser.Guess(file.GetSpanClamped(0, 1 << 20), whole).Codec;
        Codec = codec;
        AllocateIndex();
        _bomLength = codec.BomLengthIn(file.GetSpanClamped(0, 4));
    }

    /// <summary>Sizes the block table for the current code unit; blocks must start on a code unit.</summary>
    private void AllocateIndex()
    {
        int unit = Codec.Alignment;
        _blockSize = (_options.IndexBlockSize + unit - 1) / unit * unit;
        _blocks = (int)((_file.Length + _blockSize - 1) / _blockSize);
        _cum = new long[_blocks + 1];
        _blockCounts = new int[_blocks];
        _ready = new bool[_blocks];
        _published = 0;
    }

    /// <summary>True when both encodings split the bytes into the same code units, so a line index built for one is valid for the other.</summary>
    internal static bool SharesLineIndex(TextCodec a, TextCodec b) =>
        a.Alignment == b.Alignment && (a.Alignment == 1 || a.BigEndian == b.BigEndian);

    /// <summary>Switches the text view of the same bytes. When the code unit size or byte order differs the line index is dropped and built again.</summary>
    internal void SetCodec(TextCodec codec)
    {
        bool reindex = !SharesLineIndex(Codec, codec);
        if (reindex)
        {
            _cts.Cancel();
            try { IndexTask.Wait(); } catch (AggregateException) { }
            _cts.Dispose();
            _cts = new();
        }
        Codec = codec;
        _bomLength = codec.BomLengthIn(_file.GetSpanClamped(0, 4));
        if (!reindex) return;
        AllocateIndex();
        if (_options.StartIndexing) StartIndexing(_options.IndexParallelism);
        else IndexTask = Task.CompletedTask;
    }

    /// <summary>Opens and maps the file, detects its encoding and starts background line indexing.</summary>
    public static MappedFileSource Open(string path, MappedFileSourceOptions? options = null)
    {
        return Open(MappedFile.Open(path), options);
    }

    internal static MappedFileSource Open(MappedFile file, MappedFileSourceOptions? options)
    {
        options ??= new();
        MappedFileSource src;
        try { src = new MappedFileSource(file, options); }
        catch { file.Dispose(); throw; }
        if (options.StartIndexing) src.StartIndexing(options.IndexParallelism);
        return src;
    }

    public (long Lines, bool Complete) LineIndexState
    {
        get
        {
            int pub = Volatile.Read(ref _published);
            return (_cum[pub] + 1, pub == _blocks);
        }
    }

    private void StartIndexing(int parallelism)
    {
        if (_blocks == 0) { IndexTask = Task.CompletedTask; return; }
        IndexTask = Task.Run(() =>
        {
            var po = new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = _cts.Token };
            try
            {
                Parallel.For(0, _blocks, po, IndexBlock);
            }
            catch (OperationCanceledException) { return; }
            IndexProgress?.Invoke(this, EventArgs.Empty);
        });
    }

    private void IndexBlock(int b)
    {
        long start = (long)b * _blockSize;
        int len = (int)Math.Min(_blockSize, _file.Length - start);
        var codec = Codec;
        var span = _file.GetSpan(start, (int)Math.Min(len + (long)codec.Alignment, _file.Length - start));
        int count = (int)LineScanner.CountTerminators(span, len, codec);
        lock (_pub)
        {
            _blockCounts[b] = count;
            _ready[b] = true;
            int pub = _published;
            while (pub < _blocks && _ready[pub])
            {
                _cum[pub + 1] = _cum[pub] + _blockCounts[pub];
                pub++;
                Volatile.Write(ref _published, pub);
            }
        }
        long now = Stopwatch.GetTimestamp();
        long last = Interlocked.Read(ref _lastProgress);
        if (now - last > Stopwatch.Frequency / 10 && Interlocked.CompareExchange(ref _lastProgress, now, last) == last)
            IndexProgress?.Invoke(this, EventArgs.Empty);
    }

    public bool TryGetLineStart(long line, out long byteOffset)
    {
        byteOffset = 0;
        if (line < 0) return false;
        if (line == 0) return true;
        int pub = Volatile.Read(ref _published);
        if (line > _cum[pub]) return false;

        int lo = 0, hi = pub - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >>> 1;
            if (_cum[mid] < line) lo = mid; else hi = mid - 1;
        }
        long start = (long)lo * _blockSize;
        int len = (int)Math.Min(_blockSize, _file.Length - start);
        var span = _file.GetSpan(start, (int)Math.Min(len + (long)Codec.Alignment, _file.Length - start));
        byteOffset = start + LineScanner.FindNthTerminator(span, len, line - _cum[lo], Codec) + Codec.Alignment;
        return true;
    }

    /// <summary>Number of line terminators at offsets below <paramref name="offset"/>; scans at most one index block.</summary>
    /// <exception cref="InvalidOperationException">The index does not reach <paramref name="offset"/> yet.</exception>
    public long CountTerminatorsBefore(long offset)
    {
        if (offset < 0 || offset > _file.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        int b = (int)(offset / _blockSize);
        if (b > Volatile.Read(ref _published)) throw new InvalidOperationException("The line index does not cover this offset yet.");
        long start = (long)b * _blockSize;
        int len = (int)(offset - start);
        if (len == 0) return _cum[b];
        var span = _file.GetSpan(start, (int)Math.Min(len + (long)Codec.Alignment, _file.Length - start));
        return _cum[b] + LineScanner.CountTerminators(span, len, Codec);
    }

    public ValueTask<IReadOnlyList<string>> ReadLinesAtAsync(long byteOffset, int maxLines, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new(Task.Run(() => ReadLines(byteOffset, maxLines), ct));
    }

    /// <summary>Synchronous variant of <see cref="ReadLinesAtAsync"/>.</summary>
    public IReadOnlyList<string> ReadLines(long byteOffset, int maxLines)
    {
        if (byteOffset < 0 || byteOffset > _file.Length) throw new ArgumentOutOfRangeException(nameof(byteOffset));
        long from = byteOffset == 0 ? Math.Min(_bomLength, _file.Length) : byteOffset;
        var span = _file.GetSpanClamped(from, long.MaxValue);
        bool atEof = from + span.Length == _file.Length;
        var lines = LineScanner.Decode(span, Codec, maxLines, atEof);
        if (!atEof && lines.Count < maxLines) throw new NotSupportedException("A line longer than 2 GiB cannot be decoded in one piece; use ReadColumns.");
        return lines;
    }

    /// <summary>Largest line start at or before <paramref name="byteOffset"/>.</summary>
    public long AlignToLineStart(long byteOffset)
    {
        if (byteOffset <= 0) return 0;
        int w = Codec.Alignment;
        long end = Math.Min(byteOffset, _file.Length);
        while (end > 0)
        {
            long start = Math.Max(0, end - 65536);
            var span = _file.GetSpanClamped(start, end - start + w);
            if (w == 1)
            {
                int j = (int)(end - start);
                while ((j = span[..j].LastIndexOfAny((byte)'\n', (byte)'\r')) >= 0)
                {
                    if (span[j] == (byte)'\n' || !(j + 1 < span.Length && span[j + 1] == (byte)'\n'))
                        return start + j + 1;
                }
            }
            else
            {
                for (int p = (int)((end - start - w) / w * w); p >= 0; p -= w)
                {
                    uint u = LineScanner.Unit(span, p, Codec);
                    if (u == 10 || (u == 13 && !(p + 2 * w <= span.Length && LineScanner.Unit(span, p + w, Codec) == 10)))
                        return start + p + w;
                }
            }
            end = start;
        }
        return 0;
    }

    /// <summary>Offset of the terminator ending the line that starts at <paramref name="lineStart"/>, or the file length.</summary>
    public long FindLineEnd(long lineStart)
    {
        int w = Codec.Alignment;
        long pos = lineStart;
        while (pos < _file.Length)
        {
            var span = _file.GetSpanClamped(pos, 1 << 20);
            int t = LineScanner.FindTerminator(span, 0, Codec);
            if (t >= 0)
            {
                long at = pos + t;
                if (LineScanner.Unit(span, t, Codec) == 10 && at > lineStart && LineScanner.Unit(_file.GetSpan(at - w, w), 0, Codec) == 13) at -= w;
                return at;
            }
            pos += span.Length;
        }
        return _file.Length;
    }

    /// <summary>
    /// Reads at most <paramref name="maxColumns"/> UTF-16 columns of one line starting at column
    /// <paramref name="startColumn"/> without materializing the rest of the line. A start inside a
    /// supplementary character is rounded to the next character.
    /// </summary>
    public ValueTask<string> ReadColumnsAsync(long lineStart, long startColumn, int maxColumns, CancellationToken ct = default) =>
        new(Task.Run(() => ReadColumns(lineStart, startColumn, maxColumns, ct), ct));

    public string ReadColumns(long lineStart, long startColumn, int maxColumns, CancellationToken ct = default)
    {
        bool utf8 = Codec.Kind == CodecKind.Utf8;
        long pos = lineStart == 0 ? Math.Min(_bomLength, _file.Length) : lineStart;
        long skip = startColumn;
        while (skip > 0)
        {
            ct.ThrowIfCancellationRequested();
            var window = _file.GetSpanClamped(pos, 1 << 20);
            if (window.IsEmpty) return "";
            int t = LineScanner.FindTerminator(window, 0, Codec);
            var seg = t < 0 ? window : window[..t];
            if (seg.IsEmpty) return "";
            int step;
            if (Codec.Kind == CodecKind.SingleByte || (utf8 && System.Text.Ascii.IsValid(seg)))
            {
                step = (int)Math.Min(skip, seg.Length);
                skip -= step;
            }
            else
            {
                var whole = t < 0 ? seg[..Codec.CompleteLength(seg)] : seg;
                int units = Codec.CountChars(whole);
                if (units <= skip) { step = whole.Length; skip -= units; }
                else step = utf8 ? SkipUtf8Units(whole, ref skip) : SkipUnits(whole, ref skip);
            }
            if (step == 0 || (t >= 0 && step == seg.Length && skip > 0)) return "";
            pos += step;
        }

        var read = _file.GetSpanClamped(pos, (long)maxColumns * 4 + 4);
        int stop = LineScanner.FindTerminator(read, 0, Codec);
        if (stop >= 0) read = read[..stop];
        if (!(stop >= 0 || pos + read.Length >= _file.Length)) read = read[..Codec.CompleteLength(read)];
        var text = Codec.GetString(read);
        return text.Length <= maxColumns ? text : text[..maxColumns];
    }

    private int SkipUnits(ReadOnlySpan<byte> seg, ref long skip)
    {
        int i = 0;
        while (skip > 0 && i < seg.Length)
        {
            i += Codec.ReadUnit(seg[i..], out int chars);
            skip -= chars;
        }
        return i;
    }

    private static int SkipUtf8Units(ReadOnlySpan<byte> seg, ref long skip)
    {
        int i = 0;
        while (skip > 0 && i < seg.Length)
        {
            byte lead = seg[i];
            int len = lead < 0x80 ? 1 : lead < 0xE0 ? 2 : lead < 0xF0 ? 3 : 4;
            if (lead >= 0x80 && lead < 0xC0) len = 1;
            if (i + len > seg.Length) break;
            skip -= lead >= 0xF0 ? 2 : 1;
            i += len;
        }
        return i;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        try { IndexTask.Wait(); } catch (AggregateException) { }
        _file.Dispose();
        _cts.Dispose();
    }
}
