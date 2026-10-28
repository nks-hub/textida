using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace NksHub.Textida.Core.Agents.Transcripts;

/// <summary>What a search went through. <see cref="Skipped"/> counts the lines that were too long to read for it.</summary>
public readonly record struct TranscriptSearchSummary(long Lines, long Skipped, int Hits, bool Limited);

/// <summary>
/// A transcript file opened for reading and its index: where every line starts, what kind of record it is and which turn it belongs to (P8b.3.4). The file is read in blocks through a read-only handle
/// that allows the agent to go on writing, and is never written. The index grows on a thread while the first turns can already be shown; it keeps to <see cref="TranscriptLimits.IndexMaxBytes"/>,
/// and a line is read from the file again whenever it is shown, so a line of 54 MiB costs nothing until someone asks for it.
/// </summary>
public sealed class TranscriptIndex : IDisposable
{
    private sealed class TurnBuilder(int number, long first, string preview, bool continued)
    {
        public int Number { get; } = number;
        public long First { get; } = first;
        public long Count { get; set; } = 1;
        public string Preview { get; } = preview;
        public bool Continued { get; } = continued;
    }

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    private readonly SafeFileHandle _handle;
    private readonly object _gate = new();
    private readonly List<long> _starts = [];
    private readonly List<byte> _kinds = [];
    private readonly List<TurnBuilder> _turns = [];
    private TurnBuilder? _open;
    private int _prompts;
    private long _end;
    private long _bytes;
    private bool _complete, _truncated;
    private Exception? _error;

    private TranscriptIndex(string path, SafeFileHandle handle, TranscriptLimits limits, TranscriptMasker masker)
    {
        Path = path;
        _handle = handle;
        Limits = limits;
        Masker = masker;
        FileLength = RandomAccess.GetLength(handle);
    }

    public string Path { get; }
    public TranscriptLimits Limits { get; }
    public TranscriptMasker Masker { get; }

    /// <summary>The size of the file when it was opened; what is written after that is not read.</summary>
    public long FileLength { get; }

    public static TranscriptIndex Open(string path, TranscriptLimits limits, TranscriptMasker masker) =>
        new(path, File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.None), limits, masker);

    public void Dispose() => _handle.Dispose();

    public long LineCount { get { lock (_gate) return _kinds.Count; } }
    public int TurnCount { get { lock (_gate) return _turns.Count; } }

    /// <summary>The whole file has been indexed, or the index stopped (<see cref="IsTruncated"/>, <see cref="Error"/>).</summary>
    public bool IsComplete { get { lock (_gate) return _complete; } }

    /// <summary>The index reached its memory limit and holds only the start of the file.</summary>
    public bool IsTruncated { get { lock (_gate) return _truncated; } }

    public Exception? Error { get { lock (_gate) return _error; } }

    /// <summary>Bytes of the file the index has gone through.</summary>
    public long BytesIndexed { get { lock (_gate) return _end; } }

    /// <summary>Memory the index uses, as the limit counts it.</summary>
    public long IndexBytes { get { lock (_gate) return _bytes; } }

    public Task BuildAsync(CancellationToken cancellation) => Task.Run(() => Build(cancellation), cancellation);

    /// <summary>Goes through the file once. Runs on the thread of the caller.</summary>
    public void Build(CancellationToken cancellation)
    {
        var context = new TranscriptContext(Limits, Masker);
        byte[] block = ArrayPool<byte>.Shared.Rent(1 << 20), line = ArrayPool<byte>.Shared.Rent(Limits.WholeLineBytes);
        try
        {
            long offset = 0, lineStart = 0, lineBytes = 0;
            int kept = 0;
            while (offset < FileLength)
            {
                cancellation.ThrowIfCancellationRequested();
                int read = RandomAccess.Read(_handle, block.AsSpan(0, (int)Math.Min(block.Length, FileLength - offset)), offset);
                if (read <= 0) break;
                int pos = 0;
                while (pos < read)
                {
                    int newline = block.AsSpan(pos, read - pos).IndexOf((byte)'\n');
                    int take = newline < 0 ? read - pos : newline;
                    int copy = Math.Min(line.Length - kept, take);
                    block.AsSpan(pos, copy).CopyTo(line.AsSpan(kept));
                    kept += copy;
                    lineBytes += take;
                    if (newline < 0) break;
                    pos += newline + 1;
                    if (!Add(lineStart, offset + pos, line.AsMemory(0, kept), lineBytes, context)) return;
                    lineStart = offset + pos;
                    kept = 0;
                    lineBytes = 0;
                }
                offset += read;
            }
            if (lineBytes > 0) Add(lineStart, FileLength, line.AsMemory(0, kept), lineBytes, context);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            lock (_gate) _error = e;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(block);
            ArrayPool<byte>.Shared.Return(line);
            lock (_gate) _complete = true;
        }
    }

    /// <summary>Registers one line. False when the index is full.</summary>
    private bool Add(long start, long end, ReadOnlyMemory<byte> kept, long length, TranscriptContext context)
    {
        long number;
        lock (_gate) number = _kinds.Count;
        var bytes = kept;
        if (number == 0 && bytes.Span.StartsWith(Utf8Bom)) bytes = bytes[3..];
        if (bytes.Length > 0 && length == kept.Length && bytes.Span[^1] == (byte)'\r') bytes = bytes[..^1];

        var kind = TranscriptKind.Other;
        string preview = "";
        bool blank = true;
        foreach (var b in bytes.Span) if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r')) { blank = false; break; }
        if (!blank)
        {
            if (length <= Limits.WholeLineBytes)
            {
                try
                {
                    using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = Limits.MaxDepth });
                    kind = TranscriptParser.Classify(doc.RootElement, Limits.PreviewChars, out preview);
                    TranscriptParser.ReadParsed(number, doc.RootElement, kind, context, null, () => "");
                }
                catch (JsonException) { kind = TranscriptKind.Invalid; }
            }
            else kind = TranscriptParser.ClassifyPrefix(bytes.Span[..Math.Min(bytes.Length, Limits.PeekBytes)], Limits.PreviewChars, out preview);
        }

        lock (_gate)
        {
            _starts.Add(start);
            _kinds.Add((byte)kind);
            _end = end;
            _bytes += 9;
            if (!blank)
            {
                if (kind == TranscriptKind.UserPrompt) StartTurn(number, ++_prompts, preview, false);
                else if (_open is null) StartTurn(number, 0, "", false);
                else if (_open.Count >= Limits.MaxLinesPerTurn) StartTurn(number, _open.Number, "", true);
            }
            if (_open is not null) _open.Count = number - _open.First + 1;
            if (_bytes > Limits.IndexMaxBytes)
            {
                _truncated = true;
                return false;
            }
        }
        return true;
    }

    private void StartTurn(long first, int number, string preview, bool continued)
    {
        _open = new TurnBuilder(number, first, preview, continued);
        _turns.Add(_open);
        _bytes += 48 + preview.Length * 2L;
    }

    // ---- the turns ----

    /// <summary>A turn of the index so far. The title is masked.</summary>
    public TranscriptTurn Turn(int index)
    {
        TurnBuilder t;
        lock (_gate) t = _turns[index];
        long count;
        lock (_gate) count = t.Count;
        return new TranscriptTurn(t.Number, t.First, count, Masker.Mask(t.Preview), t.Continued);
    }

    public TranscriptKind KindOf(long line)
    {
        lock (_gate) return (TranscriptKind)_kinds[(int)line];
    }

    /// <summary>The number of the turn a line belongs to, or -1 for a line before the first turn.</summary>
    public int TurnOf(long line)
    {
        lock (_gate)
        {
            int lo = 0, hi = _turns.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (_turns[mid].First <= line) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return found;
        }
    }

    // ---- reading lines ----

    /// <summary>The bytes of a line without its line break, at most <paramref name="maxBytes"/> of them; <paramref name="cut"/> says there were more.</summary>
    public byte[] ReadLineBytes(long line, int maxBytes, out bool cut)
    {
        long start, end;
        lock (_gate)
        {
            start = _starts[(int)line];
            end = line + 1 < _starts.Count ? _starts[(int)line + 1] : _end;
        }
        long length = end - start;
        var buffer = new byte[(int)Math.Min(length, (long)maxBytes + 2)];
        int read = RandomAccess.Read(_handle, buffer, start);
        int used = read;
        // the line break may lie within the bytes read, or beyond them for a long line
        if (used > 0 && length <= buffer.Length && buffer[used - 1] == (byte)'\n') used--;
        if (used > 0 && length <= buffer.Length && buffer[used - 1] == (byte)'\r') used--;
        long lineLength = length <= buffer.Length ? used : length - 1;
        cut = lineLength > maxBytes;
        int skip = line == 0 && used >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF ? 3 : 0;
        return buffer.AsSpan(skip, Math.Max(0, Math.Min(used, maxBytes) - skip)).ToArray();
    }

    /// <summary>
    /// What a turn holds: its lines read again, each one parsed (a line that is too long is shown by its start), the values of secrets files hidden. At most
    /// <see cref="TranscriptLimits.MaxLinesPerTurn"/> lines are read.
    /// </summary>
    public TurnContent ReadTurn(int index)
    {
        var turn = Turn(index);
        var content = new TurnContent();
        var context = new TranscriptContext(Limits, Masker);
        for (long line = turn.FirstLine; line < turn.FirstLine + turn.LineCount; line++)
        {
            var bytes = ReadLineBytes(line, Limits.WholeLineBytes, out bool cut);
            if (cut)
            {
                content.OversizedLines++;
                content.Rows.Add(new TranscriptRow(line, TranscriptRowKind.Raw, TranscriptParser.Cut(Masker.Mask(Encoding.UTF8.GetString(bytes)), Limits.MaxRowChars)));
                continue;
            }
            TranscriptParser.ReadLine(line, bytes, context, content);
        }
        return content;
    }

    private static readonly JavaScriptEncoder Relaxed = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>The line as the file has it, indented when it is JSON, with the values of secrets files hidden; cut at <see cref="TranscriptLimits.MaxRawBytes"/>.</summary>
    public string ReadRaw(long line)
    {
        var bytes = ReadLineBytes(line, Limits.MaxRawBytes, out bool cut);
        string text = Encoding.UTF8.GetString(bytes);
        if (!cut)
        {
            try
            {
                using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = Limits.MaxDepth });
                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = Relaxed, NewLine = "\n" })) doc.RootElement.WriteTo(writer);
                text = Encoding.UTF8.GetString(stream.ToArray());
            }
            catch (JsonException) { /* not JSON: the line as it is */ }
        }
        text = Masker.Mask(text);
        return cut ? text + "…" : text;
    }

    // ---- search ----

    private static string Snippet(string text, int at, int length)
    {
        int from = Math.Max(0, at - 40), to = Math.Min(text.Length, at + length + 60);
        var flat = string.Join(' ', text[from..to].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return (from > 0 ? "…" : "") + flat + (to < text.Length ? "…" : "");
    }

    /// <summary>
    /// Looks for <paramref name="needle"/> (any case) in what the lines show: the rows of every line, with the values of secrets files already hidden, so the hits never give a value away. It follows
    /// the index while that is still being built. One hit per line; the search stops at <see cref="TranscriptLimits.MaxSearchHits"/>.
    /// </summary>
    public TranscriptSearchSummary Search(string needle, Action<TranscriptHit> onHit, CancellationToken cancellation)
    {
        var context = new TranscriptContext(Limits, Masker);
        int hits = 0;
        long skipped = 0, line = 0;
        if (needle.Length == 0) return new TranscriptSearchSummary(0, 0, 0, false);
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            if (line >= LineCount)
            {
                if (IsComplete && line >= LineCount) break;
                Thread.Sleep(20);
                continue;
            }
            var bytes = ReadLineBytes(line, Limits.WholeLineBytes, out bool cut);
            if (cut) skipped++;
            else
            {
                var rows = new TurnContent();
                TranscriptParser.ReadLine(line, bytes, context, rows);
                foreach (var row in rows.Rows)
                {
                    int at = row.Text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                    if (at < 0) continue;
                    onHit(new TranscriptHit(line, TurnOf(line), Snippet(row.Text, at, needle.Length)));
                    if (++hits >= Limits.MaxSearchHits) return new TranscriptSearchSummary(line + 1, skipped, hits, true);
                    break;
                }
            }
            line++;
        }
        return new TranscriptSearchSummary(line, skipped, hits, false);
    }
}
