using System.Buffers;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Syntax;

/// <summary>
/// Lines of a <see cref="Document"/> whose line index is complete, decoded one at a time. Reading the lines in order costs one scan of the bytes;
/// a jump to another line costs one position lookup of the document.
/// </summary>
public sealed class DocumentLineSource(Document document) : ILineSource
{
    private const int Block = 1 << 16;
    private const byte Lf = 10, Cr = 13;
    private readonly bool _byteNewlines = document.Codec.Kind is not (CodecKind.Utf16 or CodecKind.Utf32);
    private byte[] _buffer = new byte[Block];
    private long _bufferStart;
    private int _bufferLength;
    private long _revision = -1;
    private long _nextLine = -1;
    private long _nextOffset;

    public long LineCount => document.LineIndexState.Lines;

    public string GetLine(long line)
    {
        if (_revision != document.Revision || _nextLine != line)
        {
            _nextOffset = StartOf(line);
            _revision = document.Revision;
            _bufferLength = 0;
        }

        string text = _byteNewlines ? ReadByteLine(_nextOffset, out long next) : ReadWideLine(line, _nextOffset, out next);
        (_nextLine, _nextOffset) = (line + 1, next);
        return text;
    }

    private string ReadByteLine(long start, out long next)
    {
        long length = document.ByteLength;
        int want = Block;
        while (true)
        {
            if (start < _bufferStart || start >= _bufferStart + _bufferLength || (_bufferStart + _bufferLength < length && _bufferStart + _bufferLength - start < want / 2))
                Fill(start, want);
            int rel = (int)(start - _bufferStart);
            int term = _buffer.AsSpan(rel, _bufferLength - rel).IndexOfAny(Lf, Cr);
            bool more = _bufferStart + _bufferLength < length;
            if (term < 0 && more || term >= 0 && more && _buffer[rel + term] == Cr && rel + term + 1 == _bufferLength)
            {
                want = (int)Math.Min((long)_bufferLength * 2, Array.MaxLength);
                Fill(start, want);
                continue;
            }

            int bytes = term < 0 ? _bufferLength - rel : term;
            int terminator = term < 0 ? 0 : _buffer[rel + term] == Cr && rel + term + 1 < _bufferLength && _buffer[rel + term + 1] == Lf ? 2 : 1;
            next = start + bytes + terminator;
            return document.Codec.GetString(_buffer.AsSpan(rel, bytes));
        }
    }

    private void Fill(long from, int want)
    {
        if (_buffer.Length < want) _buffer = new byte[want];
        _bufferStart = from;
        _bufferLength = document.Read(from, _buffer.AsSpan(0, (int)Math.Min(want, document.ByteLength - from)));
    }

    private string ReadWideLine(long line, long start, out long next)
    {
        next = line + 1 < LineCount ? StartOf(line + 1) : document.ByteLength;
        var text = document.ReadText(new ByteRange(start, next));
        int end = text.Length;
        if (end > 0 && text[end - 1] == (char)10) end--;
        if (end > 0 && text[end - 1] == (char)13) end--;
        return text[..end];
    }

    private long StartOf(long line) =>
        document.TryGetOffset(new LinePosition(line, 0), out long offset) ? offset : throw new InvalidOperationException($"Line {line} has no known start.");
}

/// <summary>
/// Keeps the line states of a <see cref="Document"/> up to date while it changes and tokenizes viewports on demand.
/// For documents whose line index is complete and that are small enough to scan; a paged document of any size is read through
/// <see cref="ViewportTokenizer"/> instead. Not thread safe.
/// </summary>
public sealed class SyntaxSession : IDisposable
{
    /// <summary>Largest document a session follows; above it the cost of catching the states up to a viewport is not bounded by a keystroke budget.</summary>
    public const long DenseLimitBytes = 64L << 20;

    private const int MaxCountedInsert = 1 << 20;
    private readonly Document _document;
    private readonly IncrementalTokenizer _tokens;
    private readonly bool _byteNewlines;

    public SyntaxSession(Document document, Tokenizer tokenizer)
    {
        if (!CanFollow(document)) throw new NotSupportedException("The document is too large or its line index is not complete; use ViewportTokenizer.");
        _document = document;
        _byteNewlines = document.Codec.Kind is not (CodecKind.Utf16 or CodecKind.Utf32);
        _tokens = new IncrementalTokenizer(tokenizer, new DocumentLineSource(document));
        document.Changed += OnChanged;
    }

    public static bool CanFollow(Document document) => document.LineIndexState.Complete && document.ByteLength <= DenseLimitBytes;

    public IncrementalTokenizer Tokens => _tokens;

    public IReadOnlyList<LineTokens> TokenizeViewport(long firstLine, int count, CancellationToken ct = default) =>
        _tokens.TokenizeViewport(firstLine, count, ct);

    public void Dispose() => _document.Changed -= OnChanged;

    private void OnChanged(object? sender, DocumentChangedEventArgs e)
    {
        var change = e.Change;
        if (change.Kind == ChangeKind.View || change.Edits.Count == 0 || !_document.LineIndexState.Complete)
        {
            _tokens.Reset();
            return;
        }

        var span = change.Edits[0];
        if (!TryLineOf(span.Start, out long line))
        {
            _tokens.Invalidate(0);
            return;
        }

        // One line of margin on each side covers a CR/LF pair that the edit joined or split.
        long first = Math.Max(0, line - 1);
        if (change.Edits.Count != 1 || !TryLineAfter(span, line, out long endLine))
        {
            _tokens.Invalidate(first);
            return;
        }

        long newCount = _document.LineIndexState.Lines;
        long last = Math.Min(endLine + 1, newCount - 1);
        long inserted = last - first + 1;
        long removed = inserted - (newCount - _tokens.LineCount);
        _tokens.OnEdit(first, removed, inserted);
    }

    /// <summary>Line of an offset; an offset between the CR and the LF of a pair is on the line of the CR.</summary>
    private bool TryLineOf(long offset, out long line)
    {
        line = 0;
        if (_document.TryGetPosition(offset, out var p) || (_byteNewlines && offset > 0 && _document.TryGetPosition(offset - 1, out p)))
        {
            line = p.Line;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Line the inserted text ends on. With byte newlines it is counted from the inserted bytes (a trailing CR counts as a break, which can only
    /// over-estimate); a position lookup serves the wide encodings.
    /// </summary>
    private bool TryLineAfter(ChangeSet.Span span, long startLine, out long endLine)
    {
        endLine = startLine;
        if (!_byteNewlines) return TryLineOf(span.Start + span.Inserted, out endLine);
        if (span.Inserted > MaxCountedInsert) return TryLineOf(span.Start + span.Inserted, out endLine);
        var bytes = new byte[span.Inserted];
        _document.Read(span.Start, bytes);
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == 10 || (bytes[i] == 13 && (i + 1 == bytes.Length || bytes[i + 1] != 10))) endLine++;
        }
        return true;
    }
}

/// <summary>One line read from a document by byte offset.</summary>
/// <param name="ByteStart">Offset of the first byte of the line.</param>
/// <param name="ByteLength">Bytes of the line without its terminator (of the part that was read, for a truncated line).</param>
/// <param name="Truncated">The line is longer than the read limit; only its beginning was tokenized. Use <see cref="LongLineTokenizer"/> for the rest.</param>
public sealed record ViewportLine(long ByteStart, int ByteLength, IReadOnlyList<Token> Tokens, bool Truncated);

/// <param name="NextOffset">Offset of the line after the last one returned, or the document length.</param>
/// <param name="EndState">State after the last line; approximate when a line was truncated.</param>
public sealed record ViewportResult(IReadOnlyList<ViewportLine> Lines, long NextOffset, LineState EndState);

/// <summary>
/// Tokenizes a few lines from a byte offset without any index: the cost depends on the lines asked for, never on the size of the document.
/// The state at the first line is the caller's to give (a checkpoint, or <see cref="Tokenizer.InitialState"/> when none is known, which can
/// misplace a comment or string that began above the viewport). Reads only through <see cref="Document.Read"/>.
/// </summary>
public static class ViewportTokenizer
{
    private const int FirstBlock = 1 << 16;

    public static ViewportResult Tokenize(Document document, Tokenizer tokenizer, long lineStart, int maxLines,
        LineState? startState = null, int maxLineBytes = 1 << 20, CancellationToken ct = default)
    {
        RequireByteNewlines(document.Codec);
        var codec = document.Codec;
        var lines = new List<ViewportLine>(maxLines);
        var state = startState ?? tokenizer.InitialState;
        long length = document.ByteLength;
        long pos = lineStart;
        byte[] buf = ArrayPool<byte>.Shared.Rent(FirstBlock);
        try
        {
            long winStart = -1;
            int winLen = 0;

            void Fill(long from, int want)
            {
                if (buf.Length < want)
                {
                    ArrayPool<byte>.Shared.Return(buf);
                    buf = ArrayPool<byte>.Shared.Rent(want);
                }
                winStart = from;
                winLen = document.Read(from, buf.AsSpan(0, (int)Math.Min(want, length - from)));
            }

            while (lines.Count < maxLines && pos < length)
            {
                ct.ThrowIfCancellationRequested();
                if (pos < winStart || pos >= winStart + winLen || (winStart + winLen < length && winLen - (pos - winStart) < 4096)) Fill(pos, Math.Min(FirstBlock, maxLineBytes));
                int rel = (int)(pos - winStart);
                int term;
                bool truncated = false;
                while (true)
                {
                    term = buf.AsSpan(rel, winLen - rel).IndexOfAny((byte)'\n', (byte)'\r');
                    bool more = winStart + winLen < length;
                    if (term >= 0 && buf[rel + term] == '\r' && rel + term + 1 == winLen && more)
                    {
                        Fill(pos, Math.Min(winLen + 1, maxLineBytes));
                        rel = 0;
                        continue;
                    }
                    if (term < 0 && more)
                    {
                        if (winLen < maxLineBytes)
                        {
                            Fill(pos, (int)Math.Min((long)winLen * 2, maxLineBytes));
                            rel = 0;
                            continue;
                        }
                        truncated = true;
                    }
                    break;
                }

                int bytes = term >= 0 ? term : winLen - rel;
                long lineByteStart = winStart + rel;
                long newPos;
                if (truncated)
                {
                    bytes = WholeChars(codec, buf.AsSpan(rel, bytes));
                    newPos = SkipLine(document, winStart + winLen, length, ct);
                }
                else
                {
                    int terminator = term < 0 ? 0 : buf[rel + term] == (byte)'\r' && rel + term + 1 < winLen && buf[rel + term + 1] == (byte)'\n' ? 2 : 1;
                    newPos = lineByteStart + bytes + terminator;
                }

                var tokens = new List<Token>();
                state = tokenizer.TokenizeLine(codec.GetString(buf.AsSpan(rel, bytes)), state, tokens);
                lines.Add(new ViewportLine(lineByteStart, bytes, tokens, truncated));
                pos = newPos;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
        return new ViewportResult(lines, pos, state);
    }

    /// <summary>Offset after the terminator of the line that contains <paramref name="from"/>, reading forward in blocks.</summary>
    private static long SkipLine(Document document, long from, long length, CancellationToken ct)
    {
        var block = ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
            for (long at = from; at < length;)
            {
                ct.ThrowIfCancellationRequested();
                int n = document.Read(at, block.AsSpan(0, (int)Math.Min(block.Length, length - at)));
                int i = block.AsSpan(0, n).IndexOfAny((byte)'\n', (byte)'\r');
                if (i >= 0)
                {
                    long term = at + i;
                    int terminator = 1;
                    if (block[i] == '\r')
                    {
                        Span<byte> one = stackalloc byte[1];
                        if (term + 1 < length && document.Read(term + 1, one) == 1 && one[0] == '\n') terminator = 2;
                    }
                    return term + terminator;
                }
                at += n;
            }
            return length;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(block);
        }
    }

    /// <summary>Length of the leading part of <paramref name="bytes"/> that does not end inside a UTF-8 sequence.</summary>
    internal static int WholeChars(TextCodec codec, ReadOnlySpan<byte> bytes)
    {
        if (codec.Kind != CodecKind.Utf8) return bytes.Length;
        int j = bytes.Length - 1, back = 0;
        while (j >= 0 && back < 3 && (bytes[j] & 0xC0) == 0x80)
        {
            j--;
            back++;
        }
        if (j < 0) return bytes.Length;
        byte lead = bytes[j];
        int need = lead >= 0xF0 ? 4 : lead >= 0xE0 ? 3 : lead >= 0xC0 ? 2 : 1;
        return j + need > bytes.Length ? j : bytes.Length;
    }

    internal static void RequireByteNewlines(TextCodec codec)
    {
        if (codec.Kind is CodecKind.Utf16 or CodecKind.Utf32) throw new NotSupportedException("Viewport tokenizing reads newlines per byte, so UTF-16 and UTF-32 are not supported.");
    }
}

/// <summary>One window of a very long line: tokens in columns of the whole line and the state the line is in after the window.</summary>
public sealed record LineWindow(int Index, long ByteStart, long CharStart, int Chars, IReadOnlyList<Token> Tokens, LineState EndState, bool IsLast);

/// <summary>
/// Tokenizes a line that is too long to hold as a string (minified code, a data dump on one line) in windows of a fixed number of bytes.
/// A window costs the same however far into the line it is: the state at each window start is kept, so a window already passed is
/// tokenized again from its checkpoint, and a window not yet reached is scanned up to. A token that crosses a window edge is
/// split only when it has no space for 4096 characters. Not thread safe.
/// </summary>
public sealed class LongLineTokenizer
{
    private sealed record Checkpoint(long ReadOffset, long CharStart, char Prefix, string Tail, LineState State);

    private readonly Document _document;
    private readonly Tokenizer _tokenizer;
    private readonly int _windowBytes;
    private readonly List<Checkpoint> _checkpoints;
    private bool _complete;

    public LongLineTokenizer(Document document, Tokenizer tokenizer, long lineStart, LineState? startState = null, int windowBytes = 1 << 20)
    {
        ViewportTokenizer.RequireByteNewlines(document.Codec);
        if (windowBytes < 4 * Tokenizer.Margin) throw new ArgumentOutOfRangeException(nameof(windowBytes));
        _document = document;
        _tokenizer = tokenizer;
        _windowBytes = windowBytes;
        _checkpoints = [new Checkpoint(lineStart, 0, '\0', "", startState ?? tokenizer.InitialState)];
    }

    /// <summary>Windows reached so far; the line has exactly this many once <see cref="IsComplete"/>.</summary>
    public int KnownWindows => _checkpoints.Count;
    public bool IsComplete => _complete;

    /// <summary>Tokens of window <paramref name="index"/>, scanning the windows before it first when they are not reached yet; null when the line is shorter.</summary>
    public LineWindow? GetWindow(int index, CancellationToken ct = default)
    {
        while (_checkpoints.Count <= index)
        {
            if (_complete) return null;
            ct.ThrowIfCancellationRequested();
            Scan(_checkpoints.Count - 1, null);
        }
        var tokens = new List<Token>();
        return Scan(index, tokens);
    }

    private LineWindow Scan(int index, List<Token>? collect)
    {
        var cp = _checkpoints[index];
        var codec = _document.Codec;
        long length = _document.ByteLength;
        var buf = ArrayPool<byte>.Shared.Rent(_windowBytes);
        try
        {
            int read = _document.Read(cp.ReadOffset, buf.AsSpan(0, (int)Math.Min(_windowBytes, length - cp.ReadOffset)));
            int term = buf.AsSpan(0, read).IndexOfAny((byte)'\n', (byte)'\r');
            bool final = term >= 0 || cp.ReadOffset + read >= length;
            int cut = term >= 0 ? term : read;
            if (!final) cut = ViewportTokenizer.WholeChars(codec, buf.AsSpan(0, cut));

            string fresh = codec.GetString(buf.AsSpan(0, cut));
            string text = cp.Prefix == '\0' ? cp.Tail + fresh : cp.Prefix + cp.Tail + fresh;
            int begin = cp.Prefix == '\0' ? 0 : 1;
            var state = cp.State;
            var tokens = collect ?? [];
            int pos = _tokenizer.Scan(text, begin, final, ref state, tokens, (int)cp.CharStart - begin);
            if (!final && pos == begin)
            {
                // Nothing could be consumed (a window shorter than the safety margin); treat the rest as the end so the scan always advances.
                final = true;
                pos = _tokenizer.Scan(text, begin, true, ref state, tokens, (int)cp.CharStart - begin);
            }

            int consumed = pos - begin;
            long nextOffset = cp.ReadOffset + cut;
            if (final)
            {
                if (index == _checkpoints.Count - 1) _complete = true;
            }
            else if (index == _checkpoints.Count - 1)
            {
                _checkpoints.Add(new Checkpoint(nextOffset, cp.CharStart + consumed, text[pos - 1], text[pos..], state));
            }

            return new LineWindow(index, cp.ReadOffset, cp.CharStart, consumed, tokens, state, final);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }
}
