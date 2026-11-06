using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Data;

/// <summary>What a line of the formatted JSON view holds.</summary>
public enum JsonLineKind : byte
{
    /// <summary>A string, number or literal, with its key in an object.</summary>
    Value,
    /// <summary>The opening brace or bracket of a container that has members, with its key in an object.</summary>
    Open,
    /// <summary>The closing brace or bracket of a container that has members.</summary>
    Close,
    /// <summary>An empty object or array, drawn as <c>{}</c> or <c>[]</c>.</summary>
    Empty,
    /// <summary>Source text from the point where the JSON stopped being valid, one source line (or one slice of a long one) per line.</summary>
    Raw,
    /// <summary>A comment of a JSONC file between members, one source line of it (or one slice of a long one) per line.</summary>
    Comment,
}

/// <summary>Kind of a piece of a rendered line, for colors and for the edits it takes.</summary>
public enum JsonTokenKind : byte { Punctuation, Key, String, Number, Literal, Raw, Comment }

/// <summary>A container open at some point of the text. Frames hold no offsets, so they stay true when an edit before them shifts the text.</summary>
public sealed class JsonFrame
{
    internal JsonFrame(JsonFrame? parent, bool isObject, string? key, long index)
    {
        Parent = parent;
        IsObject = isObject;
        Key = key;
        Index = index;
        Depth = (parent?.Depth ?? 0) + 1;
    }

    public JsonFrame? Parent { get; }
    public bool IsObject { get; }
    /// <summary>The decoded key of the container in its parent object; null in an array or at the top.</summary>
    public string? Key { get; }
    /// <summary>The position of the container in its parent array; -1 in an object or at the top.</summary>
    public long Index { get; }
    /// <summary>Containers from the top down to this one, this one included.</summary>
    public int Depth { get; }
}

/// <summary>
/// One line of the formatted view. Offsets are bytes of the document; <see cref="End"/> is where the next line starts to scan.
/// <see cref="Frame"/> is the container the line belongs to (for a closing line the container it closes); <see cref="Index"/> its position in an array.
/// </summary>
public readonly record struct JsonLine(
    long Number, int Depth, JsonLineKind Kind, long KeyStart, long KeyEnd, long ValueStart, long ValueEnd, JsonTokenKind ValueKind, bool Comma, long End,
    JsonFrame? Frame, long Index)
{
    public bool HasKey => KeyStart >= 0;
}

/// <summary>An edit of the document reached the outline: lines from <see cref="FirstLine"/> on may have changed; when <see cref="Shifted"/> none changed its number or shape.</summary>
public sealed class JsonOutlineEdit(long firstLine, bool shifted) : EventArgs
{
    public long FirstLine { get; } = firstLine;
    public bool Shifted { get; } = shifted;
}

/// <summary>
/// Collapsed containers of the formatted view: a fold hides the lines after its opening line up to and including the closing one. Folds inside a
/// collapsed one are kept and come back when it is expanded. Rows are the lines that stay visible.
/// </summary>
public sealed class JsonFolds
{
    private readonly List<(long Open, long Close)> _folds = [];

    public int Count => _folds.Count;

    public bool IsFolded(long open) => IndexOf(open) >= 0;

    /// <summary>The closing line of the fold that starts at <paramref name="open"/>, or -1.</summary>
    public long CloseOf(long open) => IndexOf(open) is int i and >= 0 ? _folds[i].Close : -1;

    private int IndexOf(long open)
    {
        int i = _folds.BinarySearch((open, long.MinValue), Comparer<(long Open, long Close)>.Create((a, b) => a.Open.CompareTo(b.Open)));
        return i >= 0 ? i : -1;
    }

    public void Fold(long open, long close)
    {
        if (close <= open || IsFolded(open)) return;
        int at = 0;
        while (at < _folds.Count && _folds[at].Open < open) at++;
        _folds.Insert(at, (open, close));
    }

    public bool Unfold(long open)
    {
        int i = IndexOf(open);
        if (i < 0) return false;
        _folds.RemoveAt(i);
        return true;
    }

    public void Clear() => _folds.Clear();

    /// <summary>Drops the folds that reach line <paramref name="line"/> or later: an edit there may have moved their lines.</summary>
    public void DropFrom(long line) => _folds.RemoveAll(f => f.Close >= line);

    private IEnumerable<(long Open, long Close)> Outer()
    {
        long reach = -1;
        foreach (var f in _folds)
        {
            if (f.Open <= reach) continue;
            reach = f.Close;
            yield return f;
        }
    }

    /// <summary>Lines hidden by the folds.</summary>
    public long Hidden => Outer().Sum(f => f.Close - f.Open);

    /// <summary>The line shown in visible row <paramref name="row"/>.</summary>
    public long LineOfRow(long row)
    {
        long line = row;
        foreach (var f in Outer())
        {
            if (f.Open >= line) break;
            line += f.Close - f.Open;
        }
        return line;
    }

    /// <summary>The visible row of <paramref name="line"/>; a hidden line gives the row of the fold that hides it.</summary>
    public long RowOfLine(long line)
    {
        long hidden = 0;
        foreach (var f in Outer())
        {
            if (line <= f.Open) break;
            if (line <= f.Close) return f.Open - hidden;
            hidden += f.Close - f.Open;
        }
        return line - hidden;
    }
}

/// <summary>A piece of a rendered line: <see cref="Length"/> characters from <see cref="Column"/> show the bytes of the document from <see cref="Source"/> to <see cref="End"/>.</summary>
public readonly record struct JsonSpan(int Column, int Length, long Source, JsonTokenKind Kind, long End);

/// <summary>The text of a formatted line and where its pieces come from.</summary>
public sealed record JsonRenderedLine(JsonLine Line, string Text, IReadOnlyList<JsonSpan> Spans)
{
    /// <summary>The span that holds <paramref name="column"/> (its end included), or null.</summary>
    public JsonSpan? SpanAt(int column, bool editableOnly = true)
    {
        foreach (var span in Spans)
            if (column >= span.Column && column <= span.Column + span.Length && (!editableOnly || span.Kind != JsonTokenKind.Punctuation)) return span;
        return null;
    }
}

/// <summary>
/// The formatted (pretty) view of a JSON document, computed from the bytes and never written back: every value, key and bracket gets a line of
/// its own with an indent by depth, while the document keeps its bytes. One sequential scan on a background thread notes the scanner state at
/// every <see cref="SegmentLines"/>-th line (offset, open containers, what comes next); a line is found by decoding its segment from that point,
/// so the memory follows the number of segments and a screen of lines reads a few blocks of the file whatever its size. The containers the
/// noted states share are kept once. Several values in a row (JSON Lines) are accepted; from the first error on the lines show the source text.
/// With comments on (JSONC) a comment between members gets lines of its own and a comma before a closing bracket is accepted.
/// An edit inside one value shifts the noted states after it; any other edit drops the states after it and the scan resumes from the last one before it.
/// An edit inside a string that adds or removes no quote, backslash or control byte shifts the decoded lines too, so it reads a few bytes whatever the length of the string.
/// </summary>
public sealed class JsonOutline : IDisposable
{
    public const int SegmentLines = 512;
    /// <summary>A raw or comment line holds up to this many bytes; a value longer than that is shown cut with an ellipsis unless it is wrapped by <see cref="RenderPart"/>.</summary>
    public const int MaxShownBytes = 4096;
    private const int BlockSize = 1 << 16, KeyChars = 256, CachedSegments = 8;

    private enum Expect : byte { TopLevel, Value, ValueOrClose, Key, KeyOrClose, CommaOrClose }

    /// <summary><see cref="Comment"/>: 0 outside a comment, 1 inside a line comment cut by the length limit, 2 inside a block comment.</summary>
    private record struct ScanState(long Offset, Expect Expect, JsonFrame? Frame, long Count, bool Raw, long ErrorAt, string? Error, byte Comment = 0);

    private readonly object _gate = new();
    private readonly Document? _doc;
    private readonly TextCodec _codec;
    private readonly bool _comments;
    private IByteSnapshot _source;
    private readonly List<ScanState> _points = [];
    private readonly List<int> _minDepth = []; // per segment; int.MaxValue while the segment is not complete
    private long _lines, _scanned;
    private bool _complete;
    private (long Offset, string Message)? _error;
    private int _generation;
    private CancellationTokenSource? _cts;
    private readonly List<(int Generation, int Segment, JsonLine[] Lines)> _cache = [];
    private long _bytesRead;
    private bool _started;

    /// <summary>Formats a document and follows its edits; call <see cref="Start"/> to scan it in the background. <paramref name="comments"/>: the file is JSONC.</summary>
    public JsonOutline(Document document, bool comments = false)
        : this(document.GetSnapshot(), document.BomLength, document.Codec, comments)
    {
        _doc = document;
        document.Changed += OnChanged;
    }

    /// <summary>Formats fixed bytes (tests, previews).</summary>
    public JsonOutline(IByteSnapshot source, int bomLength, TextCodec codec, bool comments = false)
    {
        if (!Supports(codec)) throw new NotSupportedException("The formatted JSON view reads UTF-8 and single-byte encodings only.");
        _source = source;
        _codec = codec;
        _comments = comments;
        _points.Add(new ScanState(Math.Min(bomLength, source.ByteLength), Expect.TopLevel, null, 0, false, -1, null));
        _minDepth.Add(int.MaxValue);
    }

    /// <summary>True for encodings whose structural characters are single ASCII bytes that never occur inside another character.</summary>
    public static bool Supports(TextCodec codec) => codec.Kind is CodecKind.Utf8 or CodecKind.SingleByte;

    /// <summary>Raised on a background thread while the scan goes on (at most every 50 ms) and when it ends.</summary>
    public event EventHandler? Progress;

    /// <summary>Raised on the thread of the document after an edit was taken in.</summary>
    public event EventHandler<JsonOutlineEdit>? Edited;

    /// <summary>Lines found so far and whether the scan reached the end.</summary>
    public (long Lines, bool Complete) State
    {
        get { lock (_gate) return (_lines, _complete); }
    }

    /// <summary>Bytes of the document the scan has passed.</summary>
    public long ScannedBytes
    {
        get { lock (_gate) return _scanned; }
    }

    /// <summary>Where the JSON stops being valid and why (an English sentence for logs; the view shows its own text), once the scan got there.</summary>
    public (long Offset, string Message)? Error
    {
        get { lock (_gate) return _error; }
    }

    /// <summary>Bytes read by the calls of this thread-unaware counter outside the background scan: what showing lines cost.</summary>
    public long BytesRead => Interlocked.Read(ref _bytesRead);

    /// <summary>True when comments and trailing commas are read as JSONC.</summary>
    public bool Comments => _comments;

    /// <summary>Noted scanner states, one per segment.</summary>
    public int SegmentCount
    {
        get { lock (_gate) return _points.Count; }
    }

    public void Dispose()
    {
        if (_doc is not null) _doc.Changed -= OnChanged;
        lock (_gate)
        {
            _generation++;
            _cts?.Cancel();
        }
    }

    // ---- scan ----

    /// <summary>Starts (or resumes) the scan on a background thread.</summary>
    public void Start()
    {
        _started = true;
        int generation;
        CancellationToken ct;
        lock (_gate)
        {
            if (_complete) return;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            ct = _cts.Token;
            generation = _generation;
        }
        Task.Run(() =>
        {
            try { Scan(generation, ct); }
            catch (OperationCanceledException) { }
            catch (Exception) when (Volatile.Read(ref _generation) != generation) { } // the snapshot of an old revision may be gone
        }, CancellationToken.None);
    }

    /// <summary>Runs the scan on the calling thread up to the end.</summary>
    public void ScanToEnd() => Scan(Volatile.Read(ref _generation), CancellationToken.None);

    private void Scan(int generation, CancellationToken ct)
    {
        ScanState state;
        long number;
        IByteSnapshot source;
        lock (_gate)
        {
            if (generation != _generation || _complete) return;
            state = _points[^1];
            number = (_points.Count - 1) * (long)SegmentLines;
            source = _source;
        }
        var cursor = new Cursor(source, _codec, null, _comments);
        int minDepth = int.MaxValue;
        var clock = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            bool more = Next(ref state, cursor, number, out var line);
            if (more)
            {
                minDepth = Math.Min(minDepth, line.Depth);
                number++;
            }
            if (!more || number % SegmentLines == 0 || clock.ElapsedMilliseconds >= 50)
            {
                bool raise;
                lock (_gate)
                {
                    if (generation != _generation) return;
                    if (more && number % SegmentLines == 0)
                    {
                        _minDepth[^1] = minDepth;
                        _points.Add(state);
                        _minDepth.Add(int.MaxValue);
                        minDepth = int.MaxValue;
                    }
                    _lines = number;
                    _scanned = state.Offset;
                    if (state.Error is not null && _error is null) _error = (state.ErrorAt, state.Error);
                    if (!more)
                    {
                        _minDepth[^1] = minDepth;
                        _complete = true;
                        _scanned = source.ByteLength;
                    }
                    raise = !more || clock.ElapsedMilliseconds >= 50;
                }
                if (raise)
                {
                    clock.Restart();
                    Progress?.Invoke(this, EventArgs.Empty);
                }
                if (!more) return;
            }
        }
    }

    // ---- lines ----

    /// <summary>
    /// Up to <paramref name="count"/> lines from <paramref name="first"/>. Lines past the scan are decoded from the last noted state, so the first screen
    /// does not wait for the scan; fewer come back when the text ends or the scan has not noted the segment yet.
    /// </summary>
    public IReadOnlyList<JsonLine> GetLines(long first, int count)
    {
        var result = new List<JsonLine>(Math.Clamp(count, 0, SegmentLines));
        if (first < 0 || count <= 0) return result;
        for (long line = first; result.Count < count;)
        {
            int segment = (int)Math.Min(int.MaxValue, line / SegmentLines);
            var lines = Segment(segment);
            if (lines is null) break;
            int at = (int)(line - (long)segment * SegmentLines);
            if (at >= lines.Length) break;
            for (; at < lines.Length && result.Count < count; at++) result.Add(lines[at]);
            if (lines.Length < SegmentLines) break;
            line = (long)(segment + 1) * SegmentLines;
        }
        return result;
    }

    /// <summary>The line <paramref name="number"/>, when it can be decoded yet.</summary>
    public JsonLine? GetLine(long number) => GetLines(number, 1) is [var line] ? line : null;

    private JsonLine[]? Segment(int segment)
    {
        ScanState start;
        IByteSnapshot source;
        int generation;
        lock (_gate)
        {
            foreach (var c in _cache)
                if (c.Generation == _generation && c.Segment == segment) return c.Lines;
            if (segment >= _points.Count) return null;
            start = _points[segment];
            source = _source;
            generation = _generation;
        }
        var lines = Decode(source, start, (long)segment * SegmentLines, SegmentLines, out _, out _);
        lock (_gate)
        {
            if (generation == _generation)
            {
                _cache.RemoveAll(c => c.Generation != _generation || c.Segment == segment);
                if (_cache.Count >= CachedSegments) _cache.RemoveAt(0);
                _cache.Add((generation, segment, lines));
            }
        }
        return lines;
    }

    private JsonLine[] Decode(IByteSnapshot source, ScanState state, long number, int count, out ScanState end, out bool ended)
    {
        var cursor = new Cursor(source, _codec, this, _comments);
        var lines = new List<JsonLine>(count);
        ended = false;
        while (lines.Count < count)
        {
            if (!Next(ref state, cursor, number + lines.Count, out var line))
            {
                ended = true;
                break;
            }
            lines.Add(line);
        }
        end = state;
        return [.. lines];
    }

    /// <summary>
    /// The line that closes the container opened on <paramref name="openLine"/>; null when that line opens nothing or the scan has not got that far.
    /// It decodes at most the segment of the open line and the one segment whose shallowest line is at the depth of the open line.
    /// </summary>
    public long? FindClose(long openLine)
    {
        if (GetLine(openLine) is not { Kind: JsonLineKind.Open } open) return null;
        int segment = (int)(openLine / SegmentLines);
        foreach (var line in GetLines(openLine + 1, (int)((long)(segment + 1) * SegmentLines - openLine - 1)))
            if (line.Depth <= open.Depth) return line.Number;
        while (true)
        {
            segment++;
            bool known;
            lock (_gate)
            {
                if (segment >= _points.Count) return null;
                known = _minDepth[segment] != int.MaxValue;
                if (known && _minDepth[segment] > open.Depth) continue;
            }
            if (Segment(segment) is not { } lines) return null;
            foreach (var line in lines)
                if (line.Depth <= open.Depth) return line.Number;
            if (known || lines.Length < SegmentLines) return null;
        }
    }

    /// <summary>The line that shows the byte <paramref name="offset"/> of the document, within the part the scan has noted.</summary>
    public long LineOfOffset(long offset)
    {
        int segment;
        lock (_gate)
        {
            int lo = 0, hi = _points.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_points[mid].Offset <= offset) lo = mid;
                else hi = mid - 1;
            }
            segment = lo;
        }
        var lines = Segment(segment);
        if (lines is null || lines.Length == 0) return (long)segment * SegmentLines;
        foreach (var line in lines)
            if (line.End > offset) return line.Number;
        return lines[^1].Number;
    }

    /// <summary>Where a caret on <paramref name="line"/> best sits in the source text: the start of its key or value.</summary>
    public static long SourceOffsetOf(JsonLine line) => line.HasKey ? line.KeyStart : line.ValueStart;

    // ---- path and keys ----

    /// <summary>The path to the value on the line, as <c>$.key[3]["odd key"]</c>; null for source text after an error.</summary>
    public string? PathOf(JsonLine line)
    {
        if (line.Kind == JsonLineKind.Raw) return null;
        var parts = new List<string>();
        JsonFrame? chain;
        if (line.Kind is JsonLineKind.Close or JsonLineKind.Comment) chain = line.Frame;
        else
        {
            if (line.HasKey) parts.Add(KeyPart(ReadKey(_source, line.KeyStart, line.KeyEnd)));
            else if (line.Index >= 0) parts.Add($"[{line.Index}]");
            chain = line.Frame;
        }
        for (var f = chain; f is not null; f = f.Parent)
        {
            if (f.Key is not null) parts.Add(KeyPart(f.Key));
            else if (f.Index >= 0) parts.Add($"[{f.Index}]");
        }
        parts.Add("$");
        parts.Reverse();
        return string.Concat(parts);
    }

    private static string KeyPart(string key)
    {
        bool plain = key.Length > 0 && !char.IsDigit(key[0]);
        foreach (char ch in key)
            if (!(char.IsLetterOrDigit(ch) || ch is '_' or '$')) { plain = false; break; }
        return plain ? "." + key : "[\"" + JsonEncodedText.Encode(key, System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping) + "\"]";
    }

    /// <summary>The decoded text of the key token at the range, at most <see cref="KeyChars"/> characters.</summary>
    public string KeyText(JsonLine line) => line.HasKey ? ReadKey(_source, line.KeyStart, line.KeyEnd) : "";

    private string ReadKey(IByteSnapshot source, long start, long end)
    {
        int length = (int)Math.Min(end - start, KeyChars * 4L);
        var bytes = ArrayPool<byte>.Shared.Rent(Math.Max(1, length));
        try
        {
            int n = source.Read(start, bytes.AsSpan(0, length));
            Interlocked.Add(ref _bytesRead, n);
            return Unquote(_codec.GetString(bytes.AsSpan(0, n)), whole: n == end - start);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    private static string Unquote(string token, bool whole)
    {
        if (whole && token.Contains('\\'))
        {
            try
            {
                var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(token));
                if (reader.Read() && reader.TokenType == JsonTokenType.String) return Cap(reader.GetString() ?? "");
            }
            catch (JsonException) { }
        }
        string inner = token.Length > 0 && token[0] == '"' ? token[1..] : token;
        if (whole && inner.EndsWith('"')) inner = inner[..^1];
        return Cap(inner);
    }

    private static string Cap(string text) => text.Length > KeyChars ? text[..KeyChars] : text;

    /// <summary>
    /// The first line after <paramref name="afterLine"/> whose key is <paramref name="key"/> (decoded, exact), wrapping around to the start; -1 when no
    /// key matches. It scans on its own from the noted state before the line to the end, so it does not wait for the background scan. Safe on any thread.
    /// </summary>
    public long FindKey(string key, long afterLine, CancellationToken ct = default)
    {
        ScanState start, origin;
        IByteSnapshot source;
        long firstNumber;
        long from = Math.Max(0, afterLine + 1);
        lock (_gate)
        {
            int segment = (int)Math.Min(_points.Count - 1, from / SegmentLines);
            start = _points[segment];
            origin = _points[0];
            source = _source;
            firstNumber = (long)segment * SegmentLines;
        }
        var plain = _codec.EncodeLossy(key);
        long found = ScanKeys(source, start, firstNumber, from, long.MaxValue, key, plain, ct);
        if (found >= 0) return found;
        return ScanKeys(source, origin, 0, 0, Math.Max(0, afterLine + 1), key, plain, ct);
    }

    private long ScanKeys(IByteSnapshot source, ScanState state, long number, long from, long until, string key, byte[] plain, CancellationToken ct)
    {
        var cursor = new Cursor(source, _codec, null, _comments);
        var buffer = new byte[plain.Length + 2];
        for (; number < until; number++)
        {
            if ((number & 1023) == 0) ct.ThrowIfCancellationRequested();
            if (!Next(ref state, cursor, number, out var line)) return -1;
            if (number < from || !line.HasKey) continue;
            long length = line.KeyEnd - line.KeyStart;
            if (length == plain.Length + 2)
            {
                source.Read(line.KeyStart, buffer);
                if (buffer.AsSpan(1, plain.Length).SequenceEqual(plain)) return number;
            }
            else if (length > plain.Length + 2 && length <= KeyChars * 6L && ReadKey(source, line.KeyStart, line.KeyEnd) == key) return number;
        }
        return -1;
    }

    // ---- rendering ----

    /// <summary>The text of a line with <paramref name="indent"/> per level; values and raw text longer than <see cref="MaxShownBytes"/> end with an ellipsis.</summary>
    public JsonRenderedLine Render(JsonLine line, string indent = "  ") => Build(line, indent, MaxShownBytes, line.ValueStart, line.ValueEnd, head: true, tail: true);

    /// <summary>The text of a line with the whole value, for copying; the caller keeps the value within a size it can hold.</summary>
    public JsonRenderedLine RenderFull(JsonLine line, string indent = "  ") => Build(line, indent, int.MaxValue, line.ValueStart, line.ValueEnd, head: true, tail: true);

    /// <summary>True when the line holds a value longer than <see cref="MaxShownBytes"/>, which <see cref="Render"/> shows cut.</summary>
    public static bool IsLong(JsonLine line) => line.Kind == JsonLineKind.Value && line.ValueEnd - line.ValueStart > MaxShownBytes;

    /// <summary>How many rows a value takes when it is wrapped by <paramref name="partBytes"/> bytes; 1 for every line that holds no value.</summary>
    public static long PartCount(JsonLine line, int partBytes)
    {
        partBytes = Math.Max(16, partBytes);
        return line.Kind == JsonLineKind.Value ? Math.Max(1, (line.ValueEnd - line.ValueStart + partBytes - 1) / partBytes) : 1;
    }

    /// <summary>Where part <paramref name="part"/> of a wrapped value starts: every <paramref name="partBytes"/> bytes, moved back to the start of a character.</summary>
    public long PartStart(JsonLine line, long part, int partBytes)
    {
        partBytes = Math.Max(16, partBytes);
        if (part <= 0) return line.ValueStart;
        if (part >= PartCount(line, partBytes)) return line.ValueEnd;
        long at = line.ValueStart + part * partBytes;
        if (_codec.Kind != CodecKind.Utf8) return at;
        Span<byte> bytes = stackalloc byte[4];
        int n = _source.Read(at - 3, bytes);
        Interlocked.Add(ref _bytesRead, n);
        int k = 3;
        while (k > 0 && (bytes[k] & 0xC0) == 0x80) k--;
        return at - 3 + k;
    }

    /// <summary>The part of a wrapped value that shows <paramref name="offset"/>; the end of the value is in the last part.</summary>
    public long PartOf(JsonLine line, long offset, int partBytes)
    {
        long count = PartCount(line, partBytes);
        if (count <= 1) return 0;
        long part = Math.Clamp((offset - line.ValueStart) / Math.Max(16, partBytes), 0, count - 1);
        if (part > 0 && PartStart(line, part, partBytes) > offset) part--;
        else if (part + 1 < count && PartStart(line, part + 1, partBytes) <= offset) part++;
        return part;
    }

    /// <summary>
    /// One row of a value wrapped by <paramref name="partBytes"/> bytes: the first holds the key and the start of the value, the next ones the rest of
    /// the value indented one level deeper, and the last one the comma. A line without a long value comes back whole.
    /// </summary>
    public JsonRenderedLine RenderPart(JsonLine line, long part, int partBytes, string indent = "  ")
    {
        long count = PartCount(line, partBytes);
        if (count <= 1) return Build(line, indent, int.MaxValue, line.ValueStart, line.ValueEnd, head: true, tail: true);
        part = Math.Clamp(part, 0, count - 1);
        long to = part + 1 >= count ? line.ValueEnd : PartStart(line, part + 1, partBytes);
        return Build(line, indent, int.MaxValue, PartStart(line, part, partBytes), to, head: part == 0, tail: part + 1 >= count);
    }

    /// <summary>The decoded text of the document bytes from <paramref name="start"/> to <paramref name="end"/>; the caller keeps the range within a size it can hold.</summary>
    public string ReadText(long start, long end)
    {
        if (end <= start) return "";
        var bytes = new byte[end - start];
        int n = _source.Read(start, bytes);
        Interlocked.Add(ref _bytesRead, n);
        return _codec.GetString(bytes.AsSpan(0, n));
    }

    private JsonRenderedLine Build(JsonLine line, string indent, int max, long valueFrom, long valueTo, bool head, bool tail)
    {
        var text = new StringBuilder();
        var spans = new List<JsonSpan>(4);
        for (int i = head ? 0 : -1; i < line.Depth; i++) text.Append(indent);
        void Piece(long start, long end, JsonTokenKind kind)
        {
            long length = Math.Min(end - start, max);
            var bytes = new byte[length];
            int n = _source.Read(start, bytes);
            Interlocked.Add(ref _bytesRead, n);
            int complete = n < end - start ? _codec.CompleteLength(bytes.AsSpan(0, n)) : n;
            string s = _codec.GetString(bytes.AsSpan(0, complete));
            spans.Add(new JsonSpan(text.Length, s.Length, start, kind, start + complete));
            text.Append(s);
            if (complete < end - start) text.Append('…');
        }
        void Mark(string s, long source, long end)
        {
            spans.Add(new JsonSpan(text.Length, s.Length, source, JsonTokenKind.Punctuation, end));
            text.Append(s);
        }
        if (line.Kind is JsonLineKind.Raw or JsonLineKind.Comment)
        {
            long end = line.ValueEnd;
            if (line.Kind == JsonLineKind.Raw)
            {
                var last = new byte[Math.Min(2, end - line.ValueStart)];
                _source.Read(end - last.Length, last);
                if (last is [.., 10]) end -= last is [13, 10] ? 2 : 1;
            }
            Piece(line.ValueStart, end, line.Kind == JsonLineKind.Raw ? JsonTokenKind.Raw : JsonTokenKind.Comment);
            return new JsonRenderedLine(line, text.ToString(), spans);
        }
        if (line.HasKey && head)
        {
            Piece(line.KeyStart, line.KeyEnd, JsonTokenKind.Key);
            text.Append(": ");
        }
        switch (line.Kind)
        {
            case JsonLineKind.Value: Piece(valueFrom, valueTo, line.ValueKind); break;
            case JsonLineKind.Open: Mark(line.Frame?.IsObject == true ? "{" : "[", line.ValueStart, line.ValueStart + 1); break;
            case JsonLineKind.Close: Mark(line.Frame?.IsObject == true ? "}" : "]", line.ValueStart, line.ValueEnd); break;
            case JsonLineKind.Empty: Mark(line.ValueKind == JsonTokenKind.Punctuation && IsObjectByte(line.ValueStart) ? "{}" : "[]", line.ValueStart, line.ValueEnd); break;
        }
        if (line.Comma && tail) text.Append(',');
        return new JsonRenderedLine(line, text.ToString(), spans);
    }

    private bool IsObjectByte(long offset)
    {
        Span<byte> one = stackalloc byte[1];
        return _source.Read(offset, one) == 1 && one[0] == (byte)'{';
    }

    /// <summary>The document offset shown at <paramref name="column"/> inside a key, value or raw piece of the line; -1 elsewhere (indent, brackets, separators).</summary>
    public long SourceOffsetAt(JsonRenderedLine rendered, int column)
    {
        if (rendered.SpanAt(column) is not { } span) return -1;
        return span.Source + _codec.EncodeLossy(rendered.Text.AsSpan(span.Column, column - span.Column)).Length;
    }

    /// <summary>The column that shows the document <paramref name="offset"/> inside a key, value, raw or comment piece of the line; -1 when no piece shows it.</summary>
    public int ColumnOfOffset(JsonRenderedLine rendered, long offset)
    {
        foreach (var span in rendered.Spans)
        {
            if (span.Kind == JsonTokenKind.Punctuation || offset < span.Source || offset > span.End) continue;
            int column = span.Column, end = span.Column + span.Length;
            long at = span.Source;
            while (column < end && at < offset)
            {
                int width = char.IsHighSurrogate(rendered.Text[column]) && column + 1 < end ? 2 : 1;
                at += _codec.Kind == CodecKind.Utf8 ? Encoding.UTF8.GetByteCount(rendered.Text.AsSpan(column, width)) : 1;
                column += width;
            }
            return column;
        }
        return -1;
    }

    // ---- edits ----

    private void OnChanged(object? sender, DocumentChangedEventArgs e)
    {
        var spans = e.Change.Edits;
        if (spans.Count == 0) return;
        var next = _doc!.GetSnapshot();
        long from = spans.Min(s => s.Start);
        bool shifted = spans.Count == 1 ? Update(next, from, spans[0].Deleted, spans[0].Inserted) : Update(next, from, -1, 0);
        if (_started) Start();
        Edited?.Invoke(this, new JsonOutlineEdit(FirstLineAt(from), shifted));
    }

    /// <summary>
    /// The document changed: <paramref name="deleted"/> bytes at <paramref name="start"/> became <paramref name="inserted"/> bytes (deleted -1: several
    /// places from <paramref name="start"/> on). When the lines of the segment around the edit keep their shape, the noted states after it only shift;
    /// otherwise they are dropped and the scan resumes from the last one before the edit. Call <see cref="Start"/> afterwards to resume the scan.
    /// </summary>
    /// <returns>True when the lines kept their numbers and shape and only the offsets after the edit moved.</returns>
    public bool Update(IByteSnapshot next, long start, long deleted, long inserted)
    {
        if (deleted >= 0 && ShiftInsideString(next, start, deleted, inserted)) return true;
        IByteSnapshot old;
        int segment;
        ScanState from;
        lock (_gate)
        {
            old = _source;
            segment = SegmentBefore(start);
            from = _points[segment];
        }
        bool shifted = false;
        long delta = inserted - deleted;
        if (deleted >= 0 && from.Offset <= start)
        {
            long number = (long)segment * SegmentLines;
            var before = Decode(old, from, number, SegmentLines, out var oldEnd, out bool oldEnded);
            var after = Decode(next, from, number, SegmentLines, out var newEnd, out bool newEnded);
            shifted = oldEnded == newEnded && before.Length == after.Length && before.Length > 0 && start + deleted <= before[^1].End
                && SameShape(before, after, start, deleted, delta) && SameState(oldEnd, newEnd, start, deleted, delta);
        }
        lock (_gate)
        {
            _generation++;
            _cts?.Cancel();
            _cache.Clear();
            _source = next;
            if (shifted)
            {
                ShiftPoints(segment, start, delta, next);
                return true;
            }
            _points.RemoveRange(segment + 1, _points.Count - segment - 1);
            _minDepth.RemoveRange(segment + 1, _minDepth.Count - segment - 1);
            _minDepth[segment] = int.MaxValue;
            _lines = (long)segment * SegmentLines;
            _scanned = _points[segment].Offset;
            _complete = false;
            if (_error is { } e && e.Offset >= _points[segment].Offset) _error = null;
        }
        return false;
    }

    /// <summary>The first line of the segment whose noted state lies before <paramref name="offset"/>: lines before it cannot change by an edit there.</summary>
    private long FirstLineAt(long offset)
    {
        lock (_gate)
        {
            int segment = 0;
            while (segment + 1 < _points.Count && _points[segment + 1].Offset < offset) segment++;
            return (long)segment * SegmentLines;
        }
    }

    /// <summary>The last segment whose noted state lies before <paramref name="offset"/>. Call under the gate.</summary>
    private int SegmentBefore(long offset)
    {
        int lo = 0, hi = _points.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_points[mid].Offset < offset) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>Moves the noted states after <paramref name="segment"/> by <paramref name="delta"/>. Call under the gate.</summary>
    private void ShiftPoints(int segment, long start, long delta, IByteSnapshot next)
    {
        for (int i = segment + 1; i < _points.Count; i++) _points[i] = _points[i] with { Offset = _points[i].Offset + delta, ErrorAt = Shift(_points[i].ErrorAt, start, delta) };
        if (_error is { } error && error.Offset >= start) _error = (error.Offset + delta, error.Message);
        if (_complete) _scanned = next.ByteLength;
        else _scanned += delta;
    }

    /// <summary>
    /// The fast path of an edit inside the quotes of one string (a value, or a key that opens no container): when neither the removed nor the added bytes
    /// hold a quote, a backslash or a control byte and no backslash stands before the edit, the structure cannot change, so the decoded lines and the noted
    /// states only shift. It needs the segment of the edit decoded already (the screen did that) and reads the edited bytes and one more, so typing into
    /// a string of hundreds of MiB does not scan the string again.
    /// </summary>
    private bool ShiftInsideString(IByteSnapshot next, long start, long deleted, long inserted)
    {
        IByteSnapshot old;
        int segment;
        JsonLine[]? lines = null;
        lock (_gate)
        {
            old = _source;
            segment = SegmentBefore(start);
            foreach (var c in _cache)
                if (c.Generation == _generation && c.Segment == segment) lines = c.Lines;
        }
        if (lines is null || start < 1) return false;
        long end = start + deleted;
        bool inside = false;
        foreach (var l in lines)
            if ((l.Kind == JsonLineKind.Value && l.ValueKind == JsonTokenKind.String && l.ValueStart < start && end < l.ValueEnd)
                || (l.HasKey && l.Kind != JsonLineKind.Open && l.KeyStart < start && end < l.KeyEnd)) { inside = true; break; }
        if (!inside) return false;
        Span<byte> before = stackalloc byte[1];
        if (old.Read(start - 1, before) != 1 || before[0] == '\\') return false;
        if (!Plain(old, start, deleted) || !Plain(next, start, inserted)) return false;
        long delta = inserted - deleted;
        lock (_gate)
        {
            if (!ReferenceEquals(old, _source)) return false;
            int generation = ++_generation;
            _cts?.Cancel();
            var kept = _cache.Where(c => c.Generation == generation - 1).Select(c => (generation, c.Segment, c.Segment < segment ? c.Lines : ShiftLines(c.Lines, start, delta))).ToList();
            _cache.Clear();
            _cache.AddRange(kept);
            _source = next;
            ShiftPoints(segment, start, delta, next);
        }
        return true;
    }

    /// <summary>True when the bytes hold no quote, backslash or control byte, so they cannot end a string or start an escape.</summary>
    private bool Plain(IByteSnapshot source, long start, long length)
    {
        var buffer = ArrayPool<byte>.Shared.Rent((int)Math.Min(Math.Max(1, length), BlockSize));
        try
        {
            for (long at = start; at < start + length;)
            {
                int n = source.Read(at, buffer.AsSpan(0, (int)Math.Min(buffer.Length, start + length - at)));
                Interlocked.Add(ref _bytesRead, n);
                if (n <= 0 || buffer.AsSpan(0, n).IndexOfAny(StringStops) >= 0) return false;
                at += n;
            }
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static JsonLine[] ShiftLines(JsonLine[] lines, long start, long delta)
    {
        long S(long o) => o > start ? o + delta : o;
        var shifted = new JsonLine[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            var l = lines[i];
            shifted[i] = l with { KeyStart = S(l.KeyStart), KeyEnd = S(l.KeyEnd), ValueStart = S(l.ValueStart), ValueEnd = S(l.ValueEnd), End = S(l.End) };
        }
        return shifted;
    }

    private static long Shift(long offset, long start, long delta) => offset >= start ? offset + delta : offset;

    private static bool SameShape(JsonLine[] before, JsonLine[] after, long start, long deleted, long delta)
    {
        long Map(long o) => o < start ? o : o >= start + deleted ? o + delta : long.MinValue;
        bool Same(long o, long n) => o < 0 ? n == o : o <= start || o >= start + deleted ? Map(o) == n : true;
        for (int i = 0; i < before.Length; i++)
        {
            var (a, b) = (before[i], after[i]);
            if (a.Kind != b.Kind || a.Depth != b.Depth || a.ValueKind != b.ValueKind || a.Comma != b.Comma || a.Index != b.Index) return false;
            if (!Same(a.End, b.End) || !Same(a.KeyStart, b.KeyStart) || !Same(a.KeyEnd, b.KeyEnd) || !Same(a.ValueStart, b.ValueStart) || !Same(a.ValueEnd, b.ValueEnd)) return false;
            // a container whose key changed would leave the old key in the frames noted after it
            if (a.Kind == JsonLineKind.Open && a.HasKey && a.KeyEnd > start && a.KeyStart < start + Math.Max(1, deleted)) return false;
        }
        return true;
    }

    private static bool SameState(ScanState a, ScanState b, long start, long deleted, long delta)
    {
        if (a.Expect != b.Expect || a.Count != b.Count || a.Raw != b.Raw || a.Error != b.Error || a.Comment != b.Comment) return false;
        if ((a.Offset >= start + deleted ? a.Offset + delta : a.Offset) != b.Offset) return false;
        for (JsonFrame? x = a.Frame, y = b.Frame; x != y; x = x.Parent, y = y.Parent)
            if (x is null || y is null || x.IsObject != y.IsObject || x.Key != y.Key || x.Index != y.Index || x.Depth != y.Depth) return false;
        return true;
    }

    // ---- scanner ----

    private static readonly SearchValues<byte> StringStops = SearchValues.Create([(byte)'"', (byte)'\\', 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31]);
    private static readonly SearchValues<byte> Space = SearchValues.Create(" \t\r\n"u8);
    private static readonly SearchValues<byte> NumberBytes = SearchValues.Create("0123456789+-.eE"u8);

    /// <summary>A window of the source read in blocks; it can go back inside the block it holds without reading again.</summary>
    private sealed class Cursor(IByteSnapshot source, TextCodec codec, JsonOutline? counter, bool comments)
    {
        public bool Comments => comments;

        private readonly byte[] _buffer = new byte[BlockSize];
        private long _start = -1;
        private int _length;

        public long Position;
        public TextCodec Codec => codec;
        public long Length { get; } = source.ByteLength;
        public bool AtEnd => Position >= Length;

        private bool Fill()
        {
            if (Position >= _start && Position < _start + _length) return true;
            if (Position >= Length) return false;
            _start = Position;
            _length = source.Read(Position, _buffer.AsSpan(0, (int)Math.Min(BlockSize, Length - Position)));
            if (counter is not null) Interlocked.Add(ref counter._bytesRead, _length);
            return _length > 0;
        }

        /// <summary>The bytes held from <see cref="Position"/> on; empty at the end.</summary>
        public ReadOnlySpan<byte> Window => Fill() ? _buffer.AsSpan((int)(Position - _start), (int)(_start + _length - Position)) : default;

        public int Peek() => Fill() ? _buffer[Position - _start] : -1;

        public void SkipSpace()
        {
            while (true)
            {
                var w = Window;
                if (w.IsEmpty) return;
                int i = w.IndexOfAnyExcept(Space);
                if (i >= 0) { Position += i; return; }
                Position += w.Length;
            }
        }

        public int PeekAt(long at)
        {
            long keep = Position;
            Position = at;
            int b = Peek();
            Position = keep;
            return b;
        }

        /// <summary>Skips white space and, in JSONC, comments too; an unclosed block comment stays for the caller to report.</summary>
        public void SkipGap()
        {
            while (true)
            {
                SkipSpace();
                long p = Position;
                if (!comments || Peek() != '/') return;
                int kind = PeekAt(p + 1);
                if (kind == '/')
                {
                    Position = p + 2;
                    while (Window is { IsEmpty: false } w)
                    {
                        int i = w.IndexOf((byte)'\n');
                        if (i >= 0) { Position += i; break; }
                        Position += w.Length;
                    }
                }
                else if (kind == '*')
                {
                    Position = p + 2;
                    bool closed = false;
                    while (!closed && Window is { IsEmpty: false } w)
                    {
                        int i = w.IndexOf((byte)'*');
                        if (i < 0) { Position += w.Length; continue; }
                        Position += i + 1;
                        if (Peek() == '/') { Position++; closed = true; }
                    }
                    if (!closed) { Position = p; return; }
                }
                else return;
            }
        }
    }

    /// <summary>Reads the next line from <paramref name="s"/>; false at the end of the text.</summary>
    private static bool Next(ref ScanState s, Cursor c, long number, out JsonLine line)
    {
        line = default;
        c.Position = s.Offset;
        if (s.Raw) return NextRaw(ref s, c, number, out line);
        if (s.Comment != 0) return NextComment(ref s, c, number, fresh: false, out line);
        while (true)
        {
            c.SkipSpace();
            long p = c.Position;
            int b = c.Peek();
            if (b < 0)
            {
                if (s.Frame is null && s.Expect is Expect.TopLevel) return false;
                return Fail(ref s, c, p, p, s.Frame is null ? "a value is missing at the end" : "the text ends inside an object or array", number, out line);
            }
            if (b == '/' && c.Comments && c.PeekAt(p + 1) is '/' or '*')
            {
                s.Comment = (byte)(c.PeekAt(p + 1) == '/' ? 1 : 2);
                s.Offset = p;
                return NextComment(ref s, c, number, fresh: true, out line);
            }
            switch (s.Expect)
            {
                case Expect.CommaOrClose:
                    if (b == ',')
                    {
                        // a comma the line before did not take (only after an edit made it appear)
                        c.Position++;
                        s.Offset = c.Position;
                        s.Expect = s.Frame!.IsObject ? Expect.Key : Expect.Value;
                        continue;
                    }
                    if (b is '}' or ']') return Close(ref s, c, p, (byte)b, number, out line);
                    return Fail(ref s, c, p, p, "a comma or a closing bracket is missing", number, out line);
                case Expect.KeyOrClose when b == '}':
                case Expect.ValueOrClose when b == ']':
                case Expect.Key when b == '}' && c.Comments: // a trailing comma of JSONC
                case Expect.Value when b == ']' && c.Comments && s.Frame is { IsObject: false }:
                    return Close(ref s, c, p, (byte)b, number, out line);
                case Expect.Key or Expect.KeyOrClose:
                {
                    if (b != '"') return Fail(ref s, c, p, p, "a key in quotes is expected", number, out line);
                    if (!ScanString(c)) return Fail(ref s, c, p, c.AtEnd ? p : c.Position, "the string is not closed or holds a control character", number, out line);
                    long keyEnd = c.Position;
                    c.SkipGap();
                    if (c.Peek() != ':') return Fail(ref s, c, p, c.Position, "a colon is missing after the key", number, out line);
                    c.Position++;
                    c.SkipGap();
                    return Value(ref s, c, p, p, keyEnd, number, out line);
                }
                default:
                    return Value(ref s, c, p, -1, -1, number, out line);
            }
        }
    }

    private static bool Value(ref ScanState s, Cursor c, long lineStart, long keyStart, long keyEnd, long number, out JsonLine line)
    {
        line = default;
        long p = c.Position;
        int b = c.Peek();
        int depth = s.Frame?.Depth ?? 0;
        long index = s.Frame is { IsObject: false } ? s.Count : -1;
        JsonTokenKind kind;
        switch (b)
        {
            case '"':
                if (!ScanString(c)) return Fail(ref s, c, lineStart, c.AtEnd ? p : c.Position, "the string is not closed or holds a control character", number, out line);
                kind = JsonTokenKind.String;
                break;
            case '-' or (>= '0' and <= '9'):
            {
                var w = c.Window;
                while (true)
                {
                    int i = w.IndexOfAnyExcept(NumberBytes);
                    if (i >= 0) { c.Position += i; break; }
                    c.Position += w.Length;
                    w = c.Window;
                    if (w.IsEmpty) break;
                }
                kind = JsonTokenKind.Number;
                break;
            }
            case 't' or 'f' or 'n':
            {
                var word = b == 't' ? "true"u8 : b == 'f' ? "false"u8 : "null"u8;
                for (int i = 0; i < word.Length; i++, c.Position++)
                    if (c.Peek() != word[i]) return Fail(ref s, c, lineStart, c.Position, "an unknown word", number, out line);
                kind = JsonTokenKind.Literal;
                break;
            }
            case '{' or '[':
            {
                bool isObject = b == '{';
                c.Position++;
                long afterOpen = c.Position;
                c.SkipSpace();
                if (c.Peek() == (isObject ? '}' : ']'))
                {
                    c.Position++;
                    if (s.Frame is { IsObject: false }) s.Count++;
                    s.Expect = s.Frame is null ? Expect.TopLevel : Expect.CommaOrClose;
                    bool comma = TakeComma(ref s, c, out long end);
                    line = new JsonLine(number, depth, JsonLineKind.Empty, keyStart, keyEnd, p, c.Position, JsonTokenKind.Punctuation, comma, end, s.Frame, index);
                    s.Offset = end;
                    return true;
                }
                if (s.Frame is { IsObject: false }) s.Count++;
                var parent = s.Frame;
                string? key = keyStart >= 0 ? KeyOf(c, keyStart, keyEnd) : null;
                var frame = new JsonFrame(parent, isObject, key, index);
                s.Frame = frame;
                s.Count = 0;
                s.Expect = isObject ? Expect.KeyOrClose : Expect.ValueOrClose;
                s.Offset = afterOpen;
                line = new JsonLine(number, depth, JsonLineKind.Open, keyStart, keyEnd, p, afterOpen, JsonTokenKind.Punctuation, false, afterOpen, frame, index);
                return true;
            }
            default:
                return Fail(ref s, c, lineStart, p, b is '}' or ']' ? "a value is missing before the closing bracket" : "a value is expected", number, out line);
        }
        long valueEnd = c.Position;
        if (s.Frame is { IsObject: false }) s.Count++;
        s.Expect = s.Frame is null ? Expect.TopLevel : Expect.CommaOrClose;
        bool hasComma = TakeComma(ref s, c, out long lineEnd);
        line = new JsonLine(number, depth, JsonLineKind.Value, keyStart, keyEnd, p, valueEnd, kind, hasComma, lineEnd, s.Frame, index);
        s.Offset = lineEnd;
        return true;
    }

    private static bool Close(ref ScanState s, Cursor c, long p, byte b, long number, out JsonLine line)
    {
        line = default;
        var frame = s.Frame;
        if (frame is null || frame.IsObject != (b == '}')) return Fail(ref s, c, p, p, "the closing bracket does not match", number, out line);
        c.Position = p + 1;
        s.Frame = frame.Parent;
        s.Count = frame.Index + 1;
        s.Expect = s.Frame is null ? Expect.TopLevel : Expect.CommaOrClose;
        bool comma = TakeComma(ref s, c, out long end);
        line = new JsonLine(number, frame.Depth - 1, JsonLineKind.Close, -1, -1, p, p + 1, JsonTokenKind.Punctuation, comma, end, frame, frame.Index);
        s.Offset = end;
        return true;
    }

    /// <summary>Takes the comma after a member when one follows; the line ends after it, or right after the member.</summary>
    private static bool TakeComma(ref ScanState s, Cursor c, out long end)
    {
        end = c.Position;
        if (s.Frame is null) return false;
        c.SkipSpace();
        if (c.Peek() != ',')
        {
            c.Position = end;
            return false;
        }
        c.Position++;
        end = c.Position;
        s.Expect = s.Frame.IsObject ? Expect.Key : Expect.Value;
        return true;
    }

    private static string KeyOf(Cursor c, long start, long end)
    {
        long at = c.Position;
        int length = (int)Math.Min(end - start, KeyChars * 4L);
        var bytes = new byte[length];
        for (int i = 0; i < length; i++)
        {
            c.Position = start + i;
            bytes[i] = (byte)c.Peek();
        }
        c.Position = at;
        return Unquote(c.Codec.GetString(bytes), whole: length == end - start);
    }

    /// <summary>Moves past a string that starts at the cursor; false when it does not end or holds a raw control character (the cursor then stops at the fault).</summary>
    private static bool ScanString(Cursor c)
    {
        c.Position++;
        while (true)
        {
            var w = c.Window;
            if (w.IsEmpty) return false;
            int i = w.IndexOfAny(StringStops);
            if (i < 0)
            {
                c.Position += w.Length;
                continue;
            }
            c.Position += i;
            byte b = w[i];
            if (b == '"')
            {
                c.Position++;
                return true;
            }
            if (b != '\\') return false;
            c.Position += 2;
            if (c.Position > c.Length) { c.Position = c.Length; return false; }
        }
    }

    private static bool Fail(ref ScanState s, Cursor c, long from, long at, string message, long number, out JsonLine line)
    {
        s = new ScanState(from, Expect.TopLevel, null, 0, true, at, message);
        c.Position = from;
        return NextRaw(ref s, c, number, out line);
    }

    private static bool NextRaw(ref ScanState s, Cursor c, long number, out JsonLine line)
    {
        line = default;
        long start = s.Offset;
        if (start >= c.Length) return false;
        c.Position = start;
        long end = -1;
        for (long scanned = 0; scanned < MaxShownBytes;)
        {
            var w = c.Window;
            if (w.IsEmpty) break;
            int take = (int)Math.Min(w.Length, MaxShownBytes - scanned);
            int i = w[..take].IndexOf((byte)'\n');
            if (i >= 0) { end = c.Position + i + 1; break; }
            c.Position += take;
            scanned += take;
        }
        if (end < 0) end = CharStart(c, Math.Min(c.Length, start + MaxShownBytes), start);
        line = new JsonLine(number, 0, JsonLineKind.Raw, -1, -1, start, end, JsonTokenKind.Raw, false, end, null, -1);
        s.Offset = end;
        return true;
    }

    /// <summary>Moves a cut at <paramref name="end"/> back so it does not split a UTF-8 character.</summary>
    private static long CharStart(Cursor c, long end, long start)
    {
        for (int k = 0; k < 3 && end < c.Length && end > start + 1; k++)
        {
            c.Position = end;
            if ((c.Peek() & 0xC0) != 0x80) break;
            end--;
        }
        return end;
    }

    /// <summary>
    /// One line of a JSONC comment: a line comment, or one source line of a block comment (its leading spaces dropped); a piece longer than
    /// <see cref="MaxShownBytes"/> goes on in the next line. <paramref name="fresh"/>: the comment starts at the state offset with its two marks.
    /// </summary>
    private static bool NextComment(ref ScanState s, Cursor c, long number, bool fresh, out JsonLine line)
    {
        line = default;
        bool block = s.Comment == 2;
        long start = s.Offset;
        c.Position = start;
        if (!fresh && block)
        {
            while (c.Peek() is ' ' or '\t') c.Position++;
            start = c.Position;
            if (c.AtEnd) return Fail(ref s, c, s.Offset, s.Offset, "the comment is not closed", number, out line);
        }
        long limit = Math.Min(c.Length, start + MaxShownBytes), textEnd = -1, next = -1;
        bool ended = false, newline = false;
        for (long at = fresh ? start + 2 : start; at < limit;)
        {
            c.Position = at;
            var w = c.Window;
            if (w.IsEmpty) break;
            int take = (int)Math.Min(w.Length, limit - at);
            int i = block ? w[..take].IndexOfAny((byte)'\n', (byte)'*') : w[..take].IndexOf((byte)'\n');
            if (i < 0) { at += take; continue; }
            long q = at + i;
            if (w[i] == '\n')
            {
                textEnd = q;
                next = block ? q + 1 : q;
                ended = !block;
                newline = true;
                break;
            }
            if (c.PeekAt(q + 1) == '/')
            {
                textEnd = next = q + 2;
                ended = true;
                break;
            }
            at = q + 1;
        }
        if (textEnd < 0)
        {
            if (limit >= c.Length)
            {
                if (block) return Fail(ref s, c, start, start, "the comment is not closed", number, out line);
                textEnd = next = c.Length;
                ended = true;
            }
            else
            {
                textEnd = CharStart(c, limit, start);
                if (block && textEnd - 1 > start && c.PeekAt(textEnd - 1) == '*') textEnd--; // keep "*/" together
                next = textEnd;
            }
        }
        if (newline && textEnd > start && c.PeekAt(textEnd - 1) == '\r') textEnd--;
        line = new JsonLine(number, s.Frame?.Depth ?? 0, JsonLineKind.Comment, -1, -1, start, textEnd, JsonTokenKind.Comment, false, next, s.Frame, -1);
        s.Offset = next;
        if (ended) s.Comment = 0;
        return true;
    }
}
