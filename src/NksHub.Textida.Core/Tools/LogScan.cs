using System.Buffers;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Tools;

/// <summary>
/// Finds the next or previous line of a given level in a log of any size. The log is read in blocks of <see cref="BlockBytes"/> from a snapshot, so the
/// memory used does not depend on the size of the file and the search stops at the first hit; nothing before the starting point (backwards: after it)
/// is read. Offsets are byte offsets of the start of a line.
/// </summary>
public static class LogScan
{
    public const int BlockBytes = 1 << 20;
    private const int ChunkBytes = 1 << 16;

    /// <summary>
    /// The start of the first line of <paramref name="level"/> that begins at or after <paramref name="from"/> (forward) or the last that begins before it (backward);
    /// null when there is none. <paramref name="from"/> is a line start; <paramref name="firstLineStart"/> is where the text begins (after a BOM).
    /// <paramref name="scanned"/> receives the offset the search has reached.
    /// </summary>
    public static long? Find(IByteSnapshot bytes, TextCodec codec, long firstLineStart, LogLevelRules rules, LogLevel level, long from, bool forward, Action<long>? scanned = null, CancellationToken ct = default)
    {
        if (rules.RuleOf(level) is null) return null;
        var buffer = ArrayPool<byte>.Shared.Rent(BlockBytes);
        try { return forward ? Forward(bytes, codec, rules, level, Math.Max(from, firstLineStart), buffer, scanned, ct) : Backward(bytes, codec, firstLineStart, rules, level, Math.Min(from, bytes.ByteLength), buffer, scanned, ct); }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static long? Forward(IByteSnapshot bytes, TextCodec codec, LogLevelRules rules, LogLevel level, long pos, byte[] buffer, Action<long>? scanned, CancellationToken ct)
    {
        int w = codec.Alignment;
        long length = bytes.ByteLength;
        while (pos < length)
        {
            ct.ThrowIfCancellationRequested();
            int got = bytes.Read(pos, buffer.AsSpan(0, (int)Math.Min(BlockBytes, length - pos)));
            got -= got % w;
            if (got <= 0) break;
            var span = buffer.AsSpan(0, got);
            int cut = pos + got >= length ? got : WholeLines(span, codec);
            if (cut <= 0) cut = got;
            if (Hit(span[..cut], pos, codec, rules, level, last: false) is { } hit) return hit;
            pos += cut;
            scanned?.Invoke(pos);
        }
        return null;
    }

    private static long? Backward(IByteSnapshot bytes, TextCodec codec, long first, LogLevelRules rules, LogLevel level, long end, byte[] buffer, Action<long>? scanned, CancellationToken ct)
    {
        int w = codec.Alignment;
        while (end > first)
        {
            ct.ThrowIfCancellationRequested();
            long start = Math.Max(first, end - BlockBytes);
            start -= start % w;
            int got = bytes.Read(start, buffer.AsSpan(0, (int)(end - start)));
            got -= got % w;
            if (got <= 0) break;
            var span = buffer.AsSpan(0, got);
            // the first line of the window may have begun before it; that line belongs to the next window
            int skip = start > first ? EndOfFirstTerminator(span, codec) : 0;
            if (skip <= 0 || skip >= got) skip = 0;
            if (Hit(span[skip..], start + skip, codec, rules, level, last: true) is { } hit) return hit;
            end = start + skip;
            scanned?.Invoke(end);
        }
        return null;
    }

    /// <summary>Length of the part of <paramref name="span"/> that ends with a line terminator; all of it when it holds none (a line longer than a block is cut where it stands).</summary>
    internal static int WholeLines(ReadOnlySpan<byte> span, TextCodec codec)
    {
        int w = codec.Alignment, last = 0, pos = 0;
        while (LineScanner.FindTerminator(span, pos, codec) is var i && i >= 0)
        {
            last = i + w;
            pos = last;
        }
        return last > 0 ? last : codec.CompleteLength(span);
    }

    private static int EndOfFirstTerminator(ReadOnlySpan<byte> span, TextCodec codec) =>
        LineScanner.FindTerminator(span, 0, codec) is var i && i >= 0 ? i + codec.Alignment : -1;

    /// <summary>Start of the first (or, with <paramref name="last"/>, the last) line of the block that holds the level, as a byte offset of the file.</summary>
    private static long? Hit(ReadOnlySpan<byte> block, long blockStart, TextCodec codec, LogLevelRules rules, LogLevel level, bool last)
    {
        var rule = rules.RuleOf(level)!;
        string text = codec.GetString(block);
        int resume = 0, found = -1;
        foreach (var m in rule.EnumerateMatches(text))
        {
            if (m.Index < resume) continue;
            int lineStart = text.AsSpan(0, m.Index).LastIndexOfAny('\n', '\r') + 1;
            int next = text.AsSpan(m.Index).IndexOfAny('\n', '\r');
            int lineEnd = next < 0 ? text.Length : m.Index + next;
            resume = lineEnd;
            if (rules.Classify(text.AsSpan(lineStart, lineEnd - lineStart)) != level) continue;
            found = lineStart;
            if (!last) break;
        }
        return found < 0 ? null : blockStart + ByteOffsetOfLine(block, text, found, codec);
    }

    /// <summary>Byte offset inside the block of the line that begins at character <paramref name="lineStart"/> of its text: the terminators before it are counted in the text and found again in the bytes.</summary>
    private static int ByteOffsetOfLine(ReadOnlySpan<byte> block, string text, int lineStart, TextCodec codec)
    {
        long terminators = 0;
        for (int i = 0; i < lineStart; i++)
            if (text[i] == '\n' || (text[i] == '\r' && !(i + 1 < text.Length && text[i + 1] == '\n'))) terminators++;
        return terminators == 0 ? 0 : LineScanner.FindNthTerminator(block, block.Length, terminators, codec) + codec.Alignment;
    }

    /// <summary>Start of the line that holds <paramref name="offset"/>: the end of the last line terminator before it, or <paramref name="firstLineStart"/>.</summary>
    public static long LineStart(IByteSnapshot bytes, TextCodec codec, long firstLineStart, long offset)
    {
        int w = codec.Alignment;
        var buffer = new byte[ChunkBytes];
        long end = Math.Min(offset, bytes.ByteLength);
        while (end > firstLineStart)
        {
            long start = Math.Max(firstLineStart, end - ChunkBytes);
            start -= start % w;
            int got = bytes.Read(start, buffer.AsSpan(0, (int)(end - start)));
            got -= got % w;
            long lastEnd = -1;
            for (int pos = 0; LineScanner.FindTerminator(buffer.AsSpan(0, got), pos, codec) is var i && i >= 0; pos = i + w) lastEnd = start + i + w;
            if (lastEnd >= 0) return lastEnd;
            end = start;
        }
        return firstLineStart;
    }

    /// <summary>Start of the line after the one that holds <paramref name="offset"/>; the length of the file when it is the last line.</summary>
    public static long NextLineStart(IByteSnapshot bytes, TextCodec codec, long offset)
    {
        int w = codec.Alignment;
        var buffer = new byte[ChunkBytes];
        long length = bytes.ByteLength;
        for (long pos = offset - offset % w; pos < length;)
        {
            int got = bytes.Read(pos, buffer.AsSpan(0, (int)Math.Min(ChunkBytes, length - pos)));
            got -= got % w;
            if (got <= 0) break;
            if (LineScanner.FindTerminator(buffer.AsSpan(0, got), 0, codec) is var i && i >= 0) return pos + i + w;
            pos += got;
        }
        return length;
    }
}
