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

    (long First, long Count) IMinimapHost.Visible => (TopLine, Math.Max(1, _canvas.VisibleLines));

    void IMinimapHost.ScrollToLine(long line) => _bar.Value = Math.Clamp(line, 0, _bar.Maximum);

    string? IMinimapHost.LineText(long line)
    {
        if (line < 0 || line >= LineCount) return null;
        if (_mapBlock.Revision != Doc.Revision || line < _mapBlock.First || line >= _mapBlock.First + _mapBlock.Lines.Length)
            _mapBlock = (Doc.Revision, line, ReadMapLines(line));
        long at = line - _mapBlock.First;
        return at < _mapBlock.Lines.Length ? _mapBlock.Lines[at] : null;
    }

    /// <summary>Decodes the lines from <paramref name="first"/> on that lie in one block of the file, each cut to what the map can show. Line breaks are found in code units, as the block loader of the lines on screen finds them, so UTF-16 and UTF-32 files have a picture too.</summary>
    private string[] ReadMapLines(long first)
    {
        long origin = LineStart(first);
        if (origin < 0) return [];
        int window = (int)Math.Min(Doc.ByteLength - origin, MapBlockBytes);
        var buffer = new byte[window];
        Doc.Read(origin, buffer);
        Interlocked.Add(ref _mapBytesRead, window);
        bool toEnd = origin + window >= Doc.ByteLength;
        var codec = Doc.Codec.WithBom(false);
        int w = Doc.Codec.Alignment;
        var lines = new List<string>();
        var hidden = _secrets.Hidden ? _secrets.Ranges() : null;
        for (int pos = 0; pos < window && first + lines.Count < LineCount && lines.Count < MapBlockLines;)
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
