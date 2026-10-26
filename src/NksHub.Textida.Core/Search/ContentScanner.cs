using System.Buffers;
using System.Text;
using System.Text.Unicode;
using Microsoft.Win32.SafeHandles;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Search;

internal interface IChunkSource
{
    long Length { get; }
    int Read(long offset, Span<byte> destination);
}

internal sealed class FileChunkSource(SafeFileHandle handle, long length) : IChunkSource
{
    public long Length => length;
    public int Read(long offset, Span<byte> destination) => RandomAccess.Read(handle, destination, offset);
}

internal sealed class SnapshotChunkSource(DocumentSnapshot snapshot) : IChunkSource
{
    public long Length => snapshot.ByteLength;
    public int Read(long offset, Span<byte> destination) => snapshot.Read(offset, destination);
}

internal readonly record struct RawHit(long Start, int Length, int Line, int Column, string Preview);

internal sealed class ScanOutcome
{
    public long Count;
    public List<RawHit> Hits = [];
    public TextCodec? Codec;
    public bool Binary;
    /// <summary>Number of line feeds in the scanned range; the next range numbers its lines from here.</summary>
    public long LineBreaks;
}

/// <summary>
/// Searches the content of one source chunk by chunk. A chunk is cut after a line feed (at a code unit boundary for UTF-16/32),
/// so memory stays bounded for any file size. A case sensitive literal in UTF-8 text is found on the bytes; everything else
/// is decoded and matched by the regex engine, and a chunk that is not valid UTF-8, or in another encoding, is decoded with the
/// character-to-byte map of <see cref="TextCodec.Decode(ReadOnlyMemory{byte})"/> so offsets stay exact.
/// </summary>
internal static class ContentScanner
{
    private const int SampleSize = 1 << 20;
    private const int NulProbe = 8000;

    private sealed class State(int keep)
    {
        public readonly int Keep = keep;
        public long Count;
        public long LinesBefore;
        public readonly List<RawHit> Hits = [];
    }

    /// <summary>Content of one source from <paramref name="start"/> to <paramref name="end"/>, read as if it began at 0.</summary>
    private sealed class RangeSource(IChunkSource inner, long start, long end) : IChunkSource
    {
        public long Length => end - start;

        public int Read(long offset, Span<byte> destination)
        {
            long left = end - start - offset;
            return left <= 0 ? 0 : inner.Read(start + offset, destination[..(int)Math.Min(destination.Length, left)]);
        }
    }

    /// <summary>Sources larger than this are cut into line-aligned ranges that are scanned in parallel.</summary>
    internal const long LargeSource = 64L << 20;

    /// <summary>
    /// Like <see cref="Scan"/>, but a large source in a byte-oriented encoding is cut at line starts into ranges scanned by several threads,
    /// the way the paged search does; the hits come back as if the source had been scanned in one go (offsets, line numbers, order).
    /// </summary>
    public static ScanOutcome ScanLarge(IChunkSource source, CompiledPattern pattern, SearchSettings settings, TextCodec? forcedCodec, int keep, CancellationToken ct)
    {
        long length = source.Length;
        if (length < LargeSource || settings.Parallelism < 2) return Scan(source, pattern, settings, forcedCodec, keep, ct);

        var codec = forcedCodec;
        if (codec is null)
        {
            var head = new byte[SampleSize];
            int n = source.Read(0, head);
            codec = Detect(head.AsSpan(0, n), length);
            if (codec is null) return new ScanOutcome { Binary = true };
        }
        if (codec.Kind is CodecKind.Utf16 or CodecKind.Utf32) return Scan(source, pattern, settings, codec, keep, ct);

        int parts = (int)Math.Clamp(length / (16L << 20), 2, settings.Parallelism * 4);
        var bounds = new long[parts + 1];
        bounds[parts] = length;
        for (int k = 1; k < parts; k++) bounds[k] = Math.Max(bounds[k - 1], LineStartAtOrAfter(source, length * k / parts, ct));

        var outcomes = new ScanOutcome[parts];
        Parallel.For(0, parts, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = settings.Parallelism }, k =>
        {
            outcomes[k] = Scan(new RangeSource(source, bounds[k], bounds[k + 1]), pattern, settings, k == 0 ? codec : codec.WithBom(false), keep, ct);
        });

        var merged = new ScanOutcome { Codec = codec };
        long lines = 0;
        for (int k = 0; k < parts; k++)
        {
            merged.Count += outcomes[k].Count;
            foreach (var h in outcomes[k].Hits)
                if (merged.Hits.Count < keep) merged.Hits.Add(h with { Start = h.Start + bounds[k], Line = (int)(h.Line + lines) });
            lines += outcomes[k].LineBreaks;
        }
        merged.LineBreaks = lines;
        return merged;
    }

    /// <summary>Offset of the first line start at or after <paramref name="raw"/> (the length when no line feed follows).</summary>
    private static long LineStartAtOrAfter(IChunkSource source, long raw, CancellationToken ct)
    {
        var window = new byte[1 << 16];
        for (long pos = Math.Max(0, raw - 1); pos < source.Length;)
        {
            ct.ThrowIfCancellationRequested();
            int n = source.Read(pos, window);
            if (n <= 0) break;
            int i = window.AsSpan(0, n).IndexOf((byte)'\n');
            if (i >= 0) return pos + i + 1;
            pos += n;
        }
        return source.Length;
    }

    /// <param name="forcedCodec">The codec of an open document; null detects it from the content and skips binary files.</param>
    /// <param name="keep">Number of hits to describe (offset, line, preview); further matches are only counted.</param>
    public static ScanOutcome Scan(IChunkSource source, CompiledPattern pattern, SearchSettings settings, TextCodec? forcedCodec, int keep, CancellationToken ct)
    {
        long length = source.Length;
        int cap = (int)Math.Min(length, Math.Max(settings.ChunkBytes, 1 << 16));
        var outcome = new ScanOutcome();
        if (cap == 0) { outcome.Codec = forcedCodec ?? TextCodec.Utf8; return outcome; }

        var state = new State(Math.Max(0, keep));
        byte[] buf = ArrayPool<byte>.Shared.Rent(cap);
        try
        {
            TextCodec? codec = forcedCodec;
            long nextRead = 0, bufOffset = 0;
            int have = 0;
            bool first = true;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int want = (int)Math.Min(cap - have, length - nextRead);
                while (want > 0)
                {
                    int n = source.Read(nextRead, buf.AsSpan(have, want));
                    if (n <= 0) { length = nextRead; break; } // the file shrank while it was read
                    have += n;
                    nextRead += n;
                    want -= n;
                }
                bool eof = nextRead >= length;

                if (first)
                {
                    if (codec is null)
                    {
                        codec = Detect(buf.AsSpan(0, have), length);
                        if (codec is null) { outcome.Binary = true; return outcome; }
                    }
                    outcome.Codec = codec;
                }

                int cut = eof ? have : CutPoint(buf.AsSpan(0, have), codec!);
                ScanChunk(buf, cut, bufOffset, first, codec!, pattern, settings, state, ct);
                Buffer.BlockCopy(buf, cut, buf, 0, have - cut);
                have -= cut;
                bufOffset += cut;
                first = false;
                if (eof) break;
            }
            outcome.Count = state.Count;
            outcome.Hits = state.Hits;
            outcome.LineBreaks = state.LinesBefore;
            return outcome;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    /// <summary>Codec of a file from its first chunk, or null for a binary file (a NUL byte in text that is not UTF-16 or UTF-32).</summary>
    internal static TextCodec? Detect(ReadOnlySpan<byte> head, long fileLength)
    {
        var sample = head[..Math.Min(head.Length, SampleSize)];
        bool nul = sample[..Math.Min(sample.Length, NulProbe)].Contains((byte)0);
        if (!nul && Utf8.IsValid(sample)) return sample.StartsWith(TextCodec.Utf8Bom.Bom) ? TextCodec.Utf8Bom : TextCodec.Utf8;
        var codec = EncodingGuesser.Guess(sample, fileLength <= SampleSize).Codec;
        if (nul && codec.Kind is not (CodecKind.Utf16 or CodecKind.Utf32)) return null;
        return codec;
    }

    private static int CutPoint(ReadOnlySpan<byte> data, TextCodec codec)
    {
        if (codec.Kind is not (CodecKind.Utf16 or CodecKind.Utf32))
        {
            int lf = data.LastIndexOf((byte)'\n');
            if (lf >= 0) return lf + 1;
        }
        int cut = codec.CompleteLength(data);
        return cut > 0 ? cut : data.Length;
    }

    private static void ScanChunk(byte[] buf, int length, long baseOffset, bool first, TextCodec codec, CompiledPattern q, SearchSettings s, State st, CancellationToken ct)
    {
        var data = buf.AsSpan(0, length);
        int skip = first ? codec.BomLengthIn(data) : 0;

        if (q.Utf8Literal is { } lit && codec.Kind == CodecKind.Utf8)
        {
            ScanUtf8Literal(data, skip, baseOffset, lit, s, st, ct);
            st.LinesBefore += data.Count((byte)'\n');
            return;
        }

        DecodedText? map = null;
        char[]? pooled = null;
        try
        {
            ReadOnlySpan<char> text;
            if (codec.Kind == CodecKind.Utf8 && Utf8.IsValid(data[skip..]))
            {
                pooled = ArrayPool<char>.Shared.Rent(length - skip + 1);
                int n = Encoding.UTF8.GetChars(data[skip..], pooled);
                text = pooled.AsSpan(0, n);
            }
            else
            {
                map = (first ? codec : codec.WithBom(false)).Decode(buf.AsMemory(0, length));
                text = map.Text;
            }
            ScanText(text, map, skip, baseOffset, q, s, st, ct);
            st.LinesBefore += text.Count('\n');
        }
        finally
        {
            if (pooled is not null) ArrayPool<char>.Shared.Return(pooled);
        }
    }

    private static void ScanUtf8Literal(ReadOnlySpan<byte> data, int skip, long baseOffset, byte[] lit, SearchSettings s, State st, CancellationToken ct)
    {
        int pos = skip, cursor = 0;
        long lines = st.LinesBefore;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int i = data[pos..].IndexOf(lit);
            if (i < 0) break;
            st.Count++;
            int start = pos + i;
            if (st.Hits.Count < st.Keep)
            {
                lines += data[cursor..start].Count((byte)'\n');
                cursor = start;
                int lineStart = Math.Max(skip, data[..start].LastIndexOf((byte)'\n') + 1);
                int lf = data[start..].IndexOf((byte)'\n');
                int lineEnd = lf < 0 ? data.Length : start + lf;
                if (lineEnd > lineStart && data[lineEnd - 1] == (byte)'\r') lineEnd--;
                int column = Encoding.UTF8.GetCharCount(data[lineStart..start]);
                var line = data[lineStart..Math.Max(lineStart, lineEnd)];
                if (line.Length > s.PreviewChars * 4) line = line[..Utf8Validator.CompleteLength(line[..(s.PreviewChars * 4)])];
                st.Hits.Add(new(baseOffset + start, lit.Length, (int)lines + 1, column, Clip(Encoding.UTF8.GetString(line), s.PreviewChars)));
            }
            pos = start + lit.Length;
        }
    }

    private static void ScanText(ReadOnlySpan<char> text, DecodedText? map, int skip, long baseOffset, CompiledPattern q, SearchSettings s, State st, CancellationToken ct)
    {
        int lastChar = 0, cursor = 0;
        long lastByte = baseOffset + skip, lines = st.LinesBefore;
        long seen = 0;
        foreach (var m in q.Regex.EnumerateMatches(text))
        {
            if (m.Length == 0 || SplitsPair(text, m.Index) || SplitsPair(text, m.Index + m.Length)) continue;
            st.Count++;
            if ((++seen & 255) == 0) ct.ThrowIfCancellationRequested();
            if (st.Hits.Count >= st.Keep) continue;

            long start, end;
            if (map is not null)
            {
                start = baseOffset + map.ByteOffsetOf(m.Index);
                end = baseOffset + map.ByteOffsetOf(m.Index + m.Length);
            }
            else
            {
                lastByte += Encoding.UTF8.GetByteCount(text[lastChar..m.Index]);
                lastChar = m.Index;
                start = lastByte;
                end = start + Encoding.UTF8.GetByteCount(text.Slice(m.Index, m.Length));
            }
            lines += text[cursor..m.Index].Count('\n');
            cursor = m.Index;
            int lineStart = text[..m.Index].LastIndexOf('\n') + 1;
            int lf = text[m.Index..].IndexOf('\n');
            int lineEnd = lf < 0 ? text.Length : m.Index + lf;
            if (lineEnd > lineStart && text[lineEnd - 1] == '\r') lineEnd--;
            var line = text[lineStart..Math.Max(lineStart, lineEnd)];
            st.Hits.Add(new(start, (int)(end - start), (int)lines + 1, m.Index - lineStart, Clip(line, s.PreviewChars)));
        }
    }

    private static bool SplitsPair(ReadOnlySpan<char> text, int index) =>
        index > 0 && index < text.Length && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1]);

    private static string Clip(ReadOnlySpan<char> line, int max)
    {
        if (line.Length <= max) return line.ToString();
        int n = max > 0 && char.IsHighSurrogate(line[max - 1]) ? max - 1 : max;
        return line[..n].ToString();
    }

    private static string Clip(string line, int max) => Clip(line.AsSpan(), max);
}
