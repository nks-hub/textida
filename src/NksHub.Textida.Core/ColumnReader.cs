using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core;

/// <summary>Up to <c>maxLength</c> bytes at <c>position</c>, fewer at the end of the data.</summary>
internal delegate ReadOnlySpan<byte> WindowReader(long position, long maxLength);

/// <summary>
/// Sparse column index of long lines: byte positions at known UTF-16 columns, one every <see cref="Spacing"/> columns, plus the
/// point where the last read started. A jump to a far column then resumes from the nearest known point instead of the line start.
/// Holds at most <see cref="MaxPoints"/> points over all lines (16 bytes each); the least recently used line is dropped first.
/// </summary>
internal sealed class ColumnIndex
{
    public const long Spacing = 1 << 20;
    public const int MaxPoints = 16384;

    private readonly object _gate = new();
    private readonly Dictionary<long, List<(long Column, long Position)>> _lines = [];
    private readonly List<long> _recent = [];
    private readonly Dictionary<long, long> _lengths = [];
    private int _points;
    private (long Line, long Column, long Position) _last = (-1, 0, 0);

    public int PointCount { get { lock (_gate) return _points + (_last.Line >= 0 ? 1 : 0); } }

    /// <summary>Known point at the largest column not above <paramref name="column"/> on the line starting at <paramref name="lineStart"/>.</summary>
    public bool TryNearest(long lineStart, long column, out long foundColumn, out long position)
    {
        lock (_gate)
        {
            (foundColumn, position) = (-1, 0);
            if (_last.Line == lineStart && _last.Column <= column) (foundColumn, position) = (_last.Column, _last.Position);
            if (_lines.TryGetValue(lineStart, out var points))
            {
                int i = ~points.BinarySearch((column, long.MaxValue));
                if (i > 0 && points[i - 1].Column > foundColumn) (foundColumn, position) = points[i - 1];
                Touch(lineStart);
            }
            return foundColumn >= 0;
        }
    }

    /// <summary>Known point at the largest position not above <paramref name="position"/> on the line starting at <paramref name="lineStart"/>.</summary>
    public bool TryNearestAtPosition(long lineStart, long position, out long foundColumn, out long foundPosition)
    {
        lock (_gate)
        {
            (foundColumn, foundPosition) = (-1, 0);
            if (_last.Line == lineStart && _last.Position <= position) (foundColumn, foundPosition) = (_last.Column, _last.Position);
            if (_lines.TryGetValue(lineStart, out var points))
            {
                int lo = 0, hi = points.Count;
                while (lo < hi)
                {
                    int mid = (lo + hi) >>> 1;
                    if (points[mid].Position <= position) lo = mid + 1; else hi = mid;
                }
                if (lo > 0 && points[lo - 1].Column > foundColumn) (foundColumn, foundPosition) = points[lo - 1];
                Touch(lineStart);
            }
            return foundColumn >= 0;
        }
    }

    /// <summary>
    /// Forgets what an edit at <paramref name="offset"/> can have changed: the points of lines that start at or after it and the points of
    /// earlier lines beyond it. Points at or before the offset describe bytes that did not move and stay.
    /// </summary>
    public void DropFrom(long offset)
    {
        lock (_gate)
        {
            _lengths.Clear();
            if (_last.Line >= offset || _last.Position > offset) _last = (-1, 0, 0);
            if (_lines.Count == 0) return;
            foreach (long key in _lines.Keys.ToArray())
            {
                var points = _lines[key];
                if (key >= offset)
                {
                    _points -= points.Count;
                    _lines.Remove(key);
                    _recent.Remove(key);
                    continue;
                }
                int lo = 0, hi = points.Count;
                while (lo < hi)
                {
                    int mid = (lo + hi) >>> 1;
                    if (points[mid].Position <= offset) lo = mid + 1; else hi = mid;
                }
                _points -= points.Count - lo;
                points.RemoveRange(lo, points.Count - lo);
            }
        }
    }

    /// <summary>Replaces this index by the points of <paramref name="other"/> that lie at or before <paramref name="offset"/> on lines that start before it.</summary>
    public void CopyBefore(ColumnIndex other, long offset)
    {
        var copied = new List<(long Line, List<(long Column, long Position)> Points)>();
        (long Line, long Column, long Position) last;
        lock (other._gate)
        {
            foreach (long key in other._recent)
            {
                if (key >= offset || !other._lines.TryGetValue(key, out var points)) continue;
                copied.Add((key, points.Where(p => p.Position <= offset).ToList()));
            }
            last = other._last;
        }
        lock (_gate)
        {
            _lines.Clear();
            _recent.Clear();
            _lengths.Clear();
            _points = 0;
            foreach (var (line, points) in copied)
            {
                _lines[line] = points;
                _recent.Add(line);
                _points += points.Count;
            }
            _last = last.Line >= 0 && last.Line < offset && last.Position <= offset ? last : (-1, 0, 0);
        }
    }

    public void Add(long lineStart, long column, long position)
    {
        lock (_gate)
        {
            if (!_lines.TryGetValue(lineStart, out var points))
            {
                while (_points >= MaxPoints && _recent.Count > 0) Evict();
                if (_points >= MaxPoints) return;
                _lines[lineStart] = points = [];
            }
            Touch(lineStart);
            int i = ~points.BinarySearch((column, long.MinValue));
            if (_points >= MaxPoints || (i < points.Count && points[i].Column == column)) return;
            points.Insert(i, (column, position));
            _points++;
        }
    }

    /// <summary>Notes the length in UTF-16 columns of the line that starts at <paramref name="lineStart"/>, once a read has reached its end.</summary>
    public void SetLength(long lineStart, long columns)
    {
        lock (_gate)
        {
            if (_lengths.Count >= MaxPoints) _lengths.Clear();
            _lengths[lineStart] = columns;
        }
    }

    public bool TryGetLength(long lineStart, out long columns)
    {
        lock (_gate) return _lengths.TryGetValue(lineStart, out columns);
    }

    public void SetLast(long lineStart, long column, long position)
    {
        lock (_gate) _last = (lineStart, column, position);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
            _recent.Clear();
            _lengths.Clear();
            _points = 0;
            _last = (-1, 0, 0);
        }
    }

    private void Touch(long lineStart)
    {
        _recent.Remove(lineStart);
        _recent.Add(lineStart);
    }

    private void Evict()
    {
        long victim = _recent[0];
        _recent.RemoveAt(0);
        if (_lines.Remove(victim, out var points)) _points -= points.Count;
    }
}

/// <summary>Reads a window of columns from one line of any <see cref="WindowReader"/> data without decoding the part before it.</summary>
internal static class ColumnReader
{
    /// <summary>
    /// At most <paramref name="maxColumns"/> UTF-16 columns starting at <paramref name="startColumn"/> of the line that starts at
    /// <paramref name="lineStart"/>; a start inside a supplementary character is rounded to the next character. Skipped columns are
    /// counted window by window and noted in <paramref name="index"/>.
    /// </summary>
    public static string Read(WindowReader window, long length, TextCodec codec, int bomLength, ColumnIndex index,
        long lineStart, long startColumn, int maxColumns, CancellationToken ct)
    {
        if (!Skip(window, length, codec, bomLength, index, lineStart, startColumn, ct, out long pos, out long reachedColumn)) return "";
        if (startColumn > 0) index.SetLast(lineStart, reachedColumn, pos);

        var read = window(pos, (long)maxColumns * 4 + 4);
        int stop = LineScanner.FindTerminator(read, 0, codec);
        if (stop >= 0) read = read[..stop];
        if (!(stop >= 0 || pos + read.Length >= length)) read = read[..codec.CompleteLength(read)];
        var text = codec.GetString(read);
        return text.Length <= maxColumns ? text : text[..maxColumns];
    }

    /// <summary>
    /// Byte position of the UTF-16 column <paramref name="column"/> of the line that starts at <paramref name="lineStart"/>; a column inside a
    /// supplementary character gives the position after it and a column past the end of the line the end of the line.
    /// </summary>
    public static long OffsetOf(WindowReader window, long length, TextCodec codec, int bomLength, ColumnIndex index,
        long lineStart, long column, CancellationToken ct)
    {
        bool reached = Skip(window, length, codec, bomLength, index, lineStart, column, ct, out long pos, out long reachedColumn);
        if (reached && column > 0) index.SetLast(lineStart, reachedColumn, pos);
        return pos;
    }

    /// <summary>
    /// UTF-16 columns between the start of the line that starts at <paramref name="lineStart"/> and the character boundary
    /// <paramref name="offset"/> of that line. The count resumes from the nearest noted point and notes the offset it reaches.
    /// </summary>
    public static long ColumnOf(WindowReader window, long length, TextCodec codec, int bomLength, ColumnIndex index,
        long lineStart, long offset, CancellationToken ct)
    {
        long pos = lineStart == 0 ? Math.Min(bomLength, length) : lineStart, column = 0, lastMark = 0;
        if (offset <= pos) return 0;
        if (index.TryNearestAtPosition(lineStart, offset, out long known, out long at)) (pos, column, lastMark) = (at, known, known);
        while (pos < offset)
        {
            ct.ThrowIfCancellationRequested();
            var data = window(pos, Math.Min(1 << 20, offset - pos));
            if (data.IsEmpty) break;
            int t = LineScanner.FindTerminator(data, 0, codec);
            var seg = t < 0 ? data : data[..t];
            if (t < 0 && pos + seg.Length < offset) seg = seg[..codec.CompleteLength(seg)];
            if (seg.IsEmpty) break;
            column += codec.CountChars(seg);
            pos += seg.Length;
            if (t >= 0) break;
            if (column - lastMark >= ColumnIndex.Spacing)
            {
                index.Add(lineStart, column, pos);
                lastMark = column;
            }
        }
        index.SetLast(lineStart, column, pos);
        return column;
    }

    /// <summary>
    /// Moves over <paramref name="startColumn"/> columns of the line, noting points in <paramref name="index"/>. False when the line ends first;
    /// <paramref name="pos"/> is where the move stopped.
    /// </summary>
    private static bool Skip(WindowReader window, long length, TextCodec codec, int bomLength, ColumnIndex index,
        long lineStart, long startColumn, CancellationToken ct, out long pos, out long reachedColumn)
    {
        reachedColumn = 0;
        bool utf8 = codec.Kind == CodecKind.Utf8;
        pos = lineStart == 0 ? Math.Min(bomLength, length) : lineStart;
        long skip = startColumn;
        long lastMark = 0;
        if (skip > 0 && index.TryNearest(lineStart, startColumn, out long known, out long at))
            (pos, skip, lastMark) = (at, startColumn - known, known);
        while (skip > 0)
        {
            ct.ThrowIfCancellationRequested();
            var data = window(pos, 1 << 20);
            if (data.IsEmpty) return EndOfLine(index, lineStart, startColumn - skip);
            int t = LineScanner.FindTerminator(data, 0, codec);
            var seg = t < 0 ? data : data[..t];
            if (seg.IsEmpty) return EndOfLine(index, lineStart, startColumn - skip);
            int step;
            if (codec.Kind == CodecKind.SingleByte || (utf8 && System.Text.Ascii.IsValid(seg)))
            {
                step = (int)Math.Min(skip, seg.Length);
                skip -= step;
            }
            else
            {
                var whole = t < 0 ? seg[..codec.CompleteLength(seg)] : seg;
                int units = codec.CountChars(whole);
                if (units <= skip) { step = whole.Length; skip -= units; }
                else step = utf8 ? SkipUtf8Units(whole, ref skip) : SkipUnits(codec, whole, ref skip);
            }
            if (step == 0) return false;
            pos += step;
            if (t >= 0 && step == seg.Length && skip > 0) return EndOfLine(index, lineStart, startColumn - skip);
            long column = startColumn - skip;
            reachedColumn = column;
            if (column - lastMark >= ColumnIndex.Spacing)
            {
                index.Add(lineStart, column, pos);
                lastMark = column;
            }
        }
        reachedColumn = startColumn - skip;
        return true;
    }

    private static bool EndOfLine(ColumnIndex index, long lineStart, long columns)
    {
        index.SetLength(lineStart, columns);
        return false;
    }

    private static int SkipUnits(TextCodec codec, ReadOnlySpan<byte> seg, ref long skip)
    {
        int i = 0;
        while (skip > 0 && i < seg.Length)
        {
            i += codec.ReadUnit(seg[i..], out int chars);
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
}
