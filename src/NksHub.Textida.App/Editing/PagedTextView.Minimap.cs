using NksHub.Textida.Core.Encodings;

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

    event Action? IMinimapHost.MapChanged
    {
        add => _mapChanged += value;
        remove => _mapChanged -= value;
    }

    long IMinimapHost.LineCount => LineCount;

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

    /// <summary>Decodes the lines from <paramref name="first"/> on that lie in one block of the file, each cut to what the map can show.</summary>
    /// <remarks>shortcut: byte-oriented encodings only (a line break is the byte 10 or 13), as in the block loader of the lines on screen; UTF-16 and UTF-32 files get no picture.</remarks>
    private string[] ReadMapLines(long first)
    {
        if (Doc.Codec.Kind is CodecKind.Utf16 or CodecKind.Utf32) return [];
        long origin = LineStart(first);
        if (origin < 0) return [];
        int window = (int)Math.Min(Doc.ByteLength - origin, MapBlockBytes);
        var buffer = new byte[window];
        Doc.Read(origin, buffer);
        bool toEnd = origin + window >= Doc.ByteLength;
        var codec = Doc.Codec.WithBom(false);
        var lines = new List<string>();
        for (int pos = 0; pos < window && first + lines.Count < LineCount && lines.Count < MapBlockLines;)
        {
            int end = pos;
            while (end < window && buffer[end] != 10 && buffer[end] != 13) end++;
            int next;
            if (end == window)
            {
                if (!toEnd) break; // the line goes on in the next block
                next = window;
            }
            else if (buffer[end] == 10) next = end + 1;
            else if (end + 1 < window) next = buffer[end + 1] == 10 ? end + 2 : end + 1;
            else if (toEnd) next = end + 1;
            else break; // a CR at the edge of the block may start a CRLF
            int take = Math.Min(end - pos, Minimap.MaxChars * 4);
            if (take < end - pos) take = Doc.Codec.CompleteLength(buffer.AsSpan(pos, take));
            string text = codec.Decode(buffer.AsMemory(pos, take)).Text;
            lines.Add(text.Length > Minimap.MaxChars ? text[..Minimap.MaxChars] : text);
            pos = next;
        }
        return [.. lines];
    }

    /// <summary>The line break that ends a line as text ("\r\n", "\r" or "\n"); empty for the last line and while the line index is not complete.</summary>
    private string LineEndingOf(long line)
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
