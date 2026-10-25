using System.Diagnostics;
using System.Text;

namespace NksPad.Core;

/// <summary>Tuning knobs for <see cref="MappedFileSource"/>.</summary>
public sealed class MappedFileSourceOptions
{
    /// <summary>Bytes per line-index block; the index keeps one counter per block.</summary>
    public int IndexBlockSize { get; init; } = 1 << 20;
    public int IndexParallelism { get; init; } = Math.Max(1, Environment.ProcessorCount - 2);
    public bool StartIndexing { get; init; } = true;
    /// <summary>Overrides detection; must be an ASCII-compatible encoding (UTF-8 or a single-byte code page).</summary>
    public Encoding? Encoding { get; init; }
}

/// <summary>
/// Read-only paged text source over a memory-mapped file. Lines are indexed lazily on the thread pool
/// with a block table (cumulative terminator counts), so memory stays proportional to file size / block size.
/// </summary>
public sealed class MappedFileSource : IPagedTextSource
{
    private readonly MappedFile _file;
    private readonly int _blockSize;
    private readonly int _blocks;
    private readonly long[] _cum;
    private readonly int[] _blockCounts;
    private readonly bool[] _ready;
    private readonly object _pub = new();
    private readonly CancellationTokenSource _cts = new();
    private int _published;
    private long _lastProgress;
    private readonly int _bomLength;

    public Task IndexTask { get; private set; } = Task.CompletedTask;
    public MappedFile File => _file;
    public string Path => _file.Path;
    public DetectedEncoding Detected { get; }
    public long ByteLength => _file.Length;
    public Encoding Encoding => Detected.Encoding;
    public event EventHandler? IndexProgress;

    private MappedFileSource(MappedFile file, MappedFileSourceOptions options)
    {
        _file = file;
        _blockSize = options.IndexBlockSize;
        _blocks = (int)((file.Length + _blockSize - 1) / _blockSize);
        _cum = new long[_blocks + 1];
        _blockCounts = new int[_blocks];
        _ready = new bool[_blocks];

        bool whole = file.Length <= 1 << 20;
        var detected = EncodingDetector.Detect(file.GetSpanClamped(0, 1 << 20), whole);
        if (options.Encoding is { } forced)
            detected = new(forced, forced.CodePage == 65001 ? EncodingKind.Utf8 : EncodingKind.SingleByte, detected.Kind == EncodingKind.Utf8 ? detected.BomLength : 0);
        if (detected.Kind is EncodingKind.Utf16LE or EncodingKind.Utf16BE or EncodingKind.Utf32LE or EncodingKind.Utf32BE)
            throw new NotSupportedException($"{detected.Encoding.WebName} is not supported by the paged source: newline scanning is byte based.");
        Detected = detected;
        _bomLength = detected.BomLength;
    }

    /// <summary>Opens and maps the file, detects its encoding and starts background line indexing.</summary>
    public static MappedFileSource Open(string path, MappedFileSourceOptions? options = null)
    {
        options ??= new();
        var file = MappedFile.Open(path);
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
        var span = _file.GetSpan(start, (int)Math.Min(len + 1L, _file.Length - start));
        int count = (int)LineScanner.CountTerminators(span, len);
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
        var span = _file.GetSpan(start, (int)Math.Min(len + 1L, _file.Length - start));
        byteOffset = start + LineScanner.FindNthTerminator(span, len, line - _cum[lo]) + 1;
        return true;
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
        var lines = LineScanner.Decode(span, Encoding, maxLines, atEof);
        if (!atEof && lines.Count < maxLines) throw new NotSupportedException("A line longer than 2 GiB cannot be decoded in one piece; use ReadColumns.");
        return lines;
    }

    /// <summary>Largest line start at or before <paramref name="byteOffset"/>.</summary>
    public long AlignToLineStart(long byteOffset)
    {
        if (byteOffset <= 0) return 0;
        long end = Math.Min(byteOffset, _file.Length);
        while (end > 0)
        {
            long start = Math.Max(0, end - 65536);
            var span = _file.GetSpanClamped(start, end - start + 1);
            int j = (int)(end - start);
            while ((j = span[..j].LastIndexOfAny((byte)'\n', (byte)'\r')) >= 0)
            {
                if (span[j] == (byte)'\n' || !(j + 1 < span.Length && span[j + 1] == (byte)'\n'))
                    return start + j + 1;
            }
            end = start;
        }
        return 0;
    }

    /// <summary>Offset of the terminator ending the line that starts at <paramref name="lineStart"/>, or the file length.</summary>
    public long FindLineEnd(long lineStart)
    {
        long pos = lineStart;
        while (pos < _file.Length)
        {
            var span = _file.GetSpanClamped(pos, 1 << 20);
            int t = LineScanner.FindTerminator(span, 0);
            if (t >= 0)
            {
                long at = pos + t;
                if (span[t] == (byte)'\n' && at > lineStart && _file.GetSpan(at - 1, 1)[0] == (byte)'\r') at--;
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
        bool utf8 = Detected.Kind is EncodingKind.Utf8 or EncodingKind.Ascii;
        long pos = lineStart == 0 ? Math.Min(_bomLength, _file.Length) : lineStart;
        long skip = startColumn;
        while (skip > 0)
        {
            ct.ThrowIfCancellationRequested();
            var window = _file.GetSpanClamped(pos, 1 << 20);
            if (window.IsEmpty) return "";
            int t = LineScanner.FindTerminator(window, 0);
            var seg = t < 0 ? window : window[..t];
            if (seg.IsEmpty) return "";
            int step;
            if (!utf8 || System.Text.Ascii.IsValid(seg))
            {
                step = (int)Math.Min(skip, seg.Length);
                skip -= step;
            }
            else
                step = SkipUtf8Units(seg, ref skip);
            if (step == 0 || (t >= 0 && step == seg.Length && skip > 0)) return "";
            pos += step;
        }

        var read = _file.GetSpanClamped(pos, (long)maxColumns * 3 + 4);
        int stop = LineScanner.FindTerminator(read, 0);
        if (stop >= 0) read = read[..stop];
        var chars = new char[maxColumns + 2];
        var decoder = Encoding.GetDecoder();
        decoder.Convert(read, chars, flush: stop >= 0 || pos + read.Length >= _file.Length, out _, out int written, out _);
        return new string(chars, 0, Math.Min(written, maxColumns));
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
        _cts.Cancel();
        try { IndexTask.Wait(); } catch (AggregateException) { }
        _file.Dispose();
        _cts.Dispose();
    }
}
