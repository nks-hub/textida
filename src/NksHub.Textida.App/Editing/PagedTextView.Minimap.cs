using NksHub.Textida.Core;
using NksHub.Textida.Core.Secrets;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// The paged surface as a host of the minimap: it reads a few kilobytes around the lines the map shows, never the file. While the line
/// index is still being built the map covers the lines indexed so far, as the scroll bar does.
/// </summary>
internal sealed partial class PagedTextView : IMinimapHost
{
    private const int MapBlockBytes = 32 * 1024;
    private const int MapBlockLines = 700;

    private Action? _mapChanged;
    private (long Revision, long First, string[] Lines) _mapBlock = (-1, 0, []);
    private readonly Dictionary<long, string> _endings = [];
    private long _endingsRevision = -1;
    private long _mapBytesRead;

    /// <summary>Bytes of the file the minimap has read so far, for tests: a few blocks, never the file.</summary>
    internal long MapBytesRead => Interlocked.Read(ref _mapBytesRead);

    /// <summary>Lines a view beyond the index has scanned for their starts so far, for tests; 0 outside such a view.</summary>
    internal int DetourLinesScanned => _detour?.Starts.Count ?? 0;

    event Action? IMinimapHost.MapChanged
    {
        add => _mapChanged += value;
        remove => _mapChanged -= value;
    }

    event Action? IMinimapHost.ContentChanged
    {
        add { }
        remove { }
    }

    long IMinimapHost.LineCount => LineCount;

    bool IMinimapHost.Sampled => true;

    bool IMinimapHost.IndexComplete => Indexed;

    long IMinimapHost.MapBytes => Doc.ByteLength;

    (long First, long Count) IMinimapHost.Visible => (TopLine, Math.Max(1, _canvas.VisibleLines));

    void IMinimapHost.ScrollToLine(long line) => _bar.Value = Math.Clamp(line, 0, _bar.Maximum);

    private ((long Revision, long Top, int Visible, object? Detour) Key, (long Start, long End) Bytes) _visibleBytes = ((-1, -1, 0, null), (-1, -1));

    /// <summary>
    /// The bytes of the lines on screen. The start of the first line is a search in the block table and one block, so it is noted per position: the minimap asks for it with every frame.
    /// While a start is not known (the first line lies past the indexed part) the last answer stands.
    /// </summary>
    (long Start, long End) IMinimapHost.VisibleBytes
    {
        get
        {
            int visible = Math.Max(1, _canvas.VisibleLines);
            var key = (Doc.Revision, TopLine, visible, (object?)_detour);
            if (_visibleBytes.Key != key)
            {
                long start = LineStart(TopLine);
                if (start >= 0)
                {
                    long end = LineStart(TopLine + visible);
                    _visibleBytes = (key, (start, end >= start ? end : start));
                }
            }
            return _visibleBytes.Bytes;
        }
    }

    /// <summary>
    /// The start of the line that holds <paramref name="offset"/>, found without the view's own line numbers (a view beyond the index counts its lines by scanning, a minimap that sampled the
    /// file through it would scan the file). Inside the indexed part the index tells it; beyond it the line start is looked for backwards, at most <see cref="DetourLineScanBytes"/>; -1 when unknown.
    /// </summary>
    private long MapOriginAt(long offset)
    {
        offset = Math.Clamp(offset, Doc.BomLength, Math.Max(Doc.BomLength, Doc.ByteLength));
        if (offset <= Doc.BomLength) return Doc.BomLength;
        if (Paged.TryGetLineOfOffset(offset, out long line)) return line == 0 ? Doc.BomLength : Paged.TryGetLineStart(line, out long start) ? start : -1;
        return Paged.IsModified ? -1 : Paged.Source.AlignToLineStart(offset, DetourLineScanBytes);
    }

    string?[] IMinimapHost.SampleStrip(long offset, int rows)
    {
        long origin = MapOriginAt(offset);
        return origin < 0 ? [] : ReadMapLinesFrom(origin, -1, rows);
    }

    void IMinimapHost.ScrollToOffset(long offset)
    {
        offset = Math.Clamp(offset, Doc.BomLength, Math.Max(Doc.BomLength, Doc.ByteLength));
        if (!Indexed && offset >= Paged.IndexedBytes)
        {
            // the line numbers up to the offset are not known yet: the view continues from the line at the offset and numbers its lines by estimate until the index catches up
            if (_detour is { } d && offset >= d.BaseStart && offset <= d.Starts[^1]) _bar.Value = Math.Clamp(DetourLineOf(d, offset), _bar.Minimum, _bar.Maximum);
            else EnterDetour(offset, keepSelection: true, reveal: false);
            return;
        }
        if (!Paged.TryGetLineOfOffset(offset, out long line)) return;
        if (_detour is not null) LeaveDetour(line);
        else _bar.Value = Math.Clamp(line, 0, _bar.Maximum);
    }

    double IMinimapHost.ShareOfLine(long line) =>
        Doc.ByteLength > 0 && Paged.TryGetApproxLineStart(line, out long offset) ? offset / (double)Doc.ByteLength : double.NaN;

    string? IMinimapHost.LineText(long line)
    {
        if (line < 0 || line >= LineCount) return null;
        // a view beyond the index finds line starts by scanning forward from where it began; a picture spread over the whole file would scan the file (gigabytes, a minute)
        if (_detour is { } d && line - d.BaseLine >= d.Starts.Count) return null;
        if (_mapBlock.Revision != Doc.Revision || line < _mapBlock.First || line >= _mapBlock.First + _mapBlock.Lines.Length)
            _mapBlock = (Doc.Revision, line, ReadMapLines(line));
        long at = line - _mapBlock.First;
        return at < _mapBlock.Lines.Length ? _mapBlock.Lines[at] : null;
    }

    /// <summary>Decodes the lines from <paramref name="first"/> on that lie in one block of the file, each cut to what the map can show. Line breaks are found in code units, as the block loader of the lines on screen finds them, so UTF-16 and UTF-32 files have a picture too.</summary>
    private string[] ReadMapLines(long first)
    {
        long origin = LineStart(first);
        return origin < 0 ? [] : ReadMapLinesFrom(origin, first, MapBlockLines);
    }

    /// <summary>The same from the line that starts at the byte <paramref name="origin"/>, at most <paramref name="maxLines"/> of them; <paramref name="first"/> is the number of that line, or -1 when it is not known (the line count then does not limit the answer).</summary>
    private string[] ReadMapLinesFrom(long origin, long first, int maxLines)
    {
        int window = (int)Math.Min(Doc.ByteLength - origin, MapBlockBytes);
        var buffer = new byte[window];
        Doc.Read(origin, buffer);
        Interlocked.Add(ref _mapBytesRead, window);
        bool toEnd = origin + window >= Doc.ByteLength;
        var codec = Doc.Codec.WithBom(false);
        int w = Doc.Codec.Alignment;
        var lines = new List<string>();
        var hidden = _secrets.Hidden ? _secrets.Ranges() : null;
        for (int pos = 0; pos < window && (first < 0 || first + lines.Count < LineCount) && lines.Count < maxLines;)
        {
            int end = LineScanner.FindTerminator(buffer, pos, Doc.Codec);
            int next;
            if (end < 0)
            {
                if (!toEnd) break; // the line goes on in the next block
                end = next = window;
            }
            else if (end + w > window) break; // a partial unit at the edge of the block
            else
            {
                next = end + w;
                uint unit = LineScanner.Unit(buffer, end, Doc.Codec);
                if (unit == 13 && next >= window && !toEnd) break; // a CR at the edge of the block may start a CRLF
                if (unit == 10 && end >= pos + w && LineScanner.Unit(buffer, end - w, Doc.Codec) == 13) end -= w;
            }
            int take = Math.Min(end - pos, Minimap.MaxChars * 4);
            if (take < end - pos) take = Doc.Codec.CompleteLength(buffer.AsSpan(pos, take));
            var decoded = codec.Decode(buffer.AsMemory(pos, take));
            string text = hidden is null ? decoded.Text : SecretValues.Mask(decoded.Text, SecretValues.SpansIn(decoded, origin + pos, hidden));
            lines.Add(text.Length > Minimap.MaxChars ? text[..Minimap.MaxChars] : text);
            pos = next;
        }
        return [.. lines];
    }

    /// <summary>The line break that ends a line as text ("\r\n", "\r" or "\n"); empty for the last line and while the line index is not complete.</summary>
    internal string LineEndingOf(long line)
    {
        if (_endingsRevision != Doc.Revision || _endings.Count > 400)
        {
            _endings.Clear();
            _endingsRevision = Doc.Revision;
        }
        if (_endings.TryGetValue(line, out var known)) return known;
        string ending = "";
        if (TryBounds(line, out _, out long end, out long next) && next > end && next - end <= 8)
        {
            var bytes = new byte[next - end];
            Doc.Read(end, bytes);
            ending = Doc.Codec.WithBom(false).Decode(bytes).Text;
        }
        return _endings[line] = ending;
    }
}
