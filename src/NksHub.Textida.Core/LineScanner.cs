using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core;

/// <summary>
/// Line terminator rules shared by the index, the reader and the tests. A terminator is a single byte:
/// every LF, plus every CR that is not directly followed by LF. A line therefore starts at offset 0
/// and right after each terminator, so a file with N terminators has N + 1 lines (the last may be empty).
/// The overloads taking a <see cref="TextCodec"/> apply the same rules to 16 and 32 bit code units; all
/// offsets stay byte offsets and a trailing partial unit is never a terminator.
/// </summary>
public static class LineScanner
{
    /// <summary>
    /// Counts terminators at positions below <paramref name="blockLen"/>. <paramref name="span"/> may hold one
    /// extra byte after the block, used only to decide whether a trailing CR is followed by LF.
    /// </summary>
    public static long CountTerminators(ReadOnlySpan<byte> span, int blockLen)
    {
        var block = span[..blockLen];
        long n = block.Count((byte)'\n');
        int from = block.IndexOf((byte)'\r');
        while (from >= 0)
        {
            if (!(from + 1 < span.Length && span[from + 1] == (byte)'\n')) n++;
            int next = block[(from + 1)..].IndexOf((byte)'\r');
            from = next < 0 ? -1 : from + 1 + next;
        }
        return n;
    }

    internal static (T Cr, T Lf) Eol<T>() where T : unmanaged => typeof(T) == typeof(byte)
        ? ((T)(object)(byte)'\r', (T)(object)(byte)'\n')
        : ((T)(object)'\r', (T)(object)'\n');

    /// <summary>Start of the line that holds <paramref name="at"/> (a position between CR and LF belongs to the line of the CR).</summary>
    internal static int LineStartOf<T>(ReadOnlySpan<T> text, int at) where T : unmanaged, IEquatable<T>
    {
        var (cr, lf) = Eol<T>();
        int end = at > 0 && at < text.Length && text[at - 1].Equals(cr) && text[at].Equals(lf) ? at - 1 : at;
        return text[..end].LastIndexOfAny(cr, lf) + 1;
    }

    /// <summary>End of the line that holds <paramref name="at"/>, before its terminator.</summary>
    internal static int LineEndOf<T>(ReadOnlySpan<T> text, int at) where T : unmanaged, IEquatable<T>
    {
        var (cr, lf) = Eol<T>();
        int i = text[at..].IndexOfAny(cr, lf);
        return i < 0 ? text.Length : at + i;
    }

    /// <summary>
    /// Terminators that end inside <c>[from, to]</c> of an ASCII-compatible text (UTF-8 bytes or chars) by the rules of this class, a CRLF
    /// counting once at its LF. A CR at <c>to - 1</c> is not counted when the LF follows at <c>to</c>, so segments of one text add up.
    /// </summary>
    internal static long CountBreaks<T>(ReadOnlySpan<T> text, int from, int to) where T : unmanaged, IEquatable<T>
    {
        var (cr, lf) = Eol<T>();
        var seg = text[from..to];
        ReadOnlySpan<T> crlf = [cr, lf];
        long n = seg.Count(lf) + seg.Count(cr) - seg.Count(crlf);
        if (seg.Length > 0 && seg[^1].Equals(cr) && to < text.Length && text[to].Equals(lf)) n--;
        return n;
    }

    /// <summary>Index of the first terminator at or after <paramref name="from"/>, or -1. A CR at the last byte of the span counts as a terminator.</summary>
    public static int FindTerminator(ReadOnlySpan<byte> span, int from)
    {
        while (true)
        {
            int i = span[from..].IndexOfAny((byte)'\n', (byte)'\r');
            if (i < 0) return -1;
            i += from;
            if (span[i] == (byte)'\n') return i;
            if (i + 1 < span.Length && span[i + 1] == (byte)'\n') { from = i + 1; continue; }
            return i;
        }
    }

    /// <summary>Index of the <paramref name="n"/>-th (1-based) terminator located below <paramref name="blockLen"/>.</summary>
    public static int FindNthTerminator(ReadOnlySpan<byte> span, int blockLen, long n)
    {
        int pos = 0;
        while (true)
        {
            int i = FindTerminator(span, pos);
            if (i < 0 || i >= blockLen) throw new InvalidOperationException("terminator not found in block");
            if (--n == 0) return i;
            pos = i + 1;
        }
    }

    // the mapped bytes are reinterpreted as host-order units, so a file of the other endianness gets swapped constants
    private static ushort Native(ushort v, bool be) => be == BitConverter.IsLittleEndian ? System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(v) : v;
    private static uint Native(uint v, bool be) => be == BitConverter.IsLittleEndian ? System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(v) : v;

    /// <summary>Counts terminator units that start below <paramref name="blockLen"/>; <paramref name="span"/> may hold one extra unit after the block.</summary>
    public static long CountTerminators(ReadOnlySpan<byte> span, int blockLen, TextCodec codec) => codec.Alignment switch
    {
        2 => CountUnits(MemoryMarshal.Cast<byte, ushort>(span), blockLen / 2, Native((ushort)10, codec.BigEndian), Native((ushort)13, codec.BigEndian)),
        4 => CountUnits(MemoryMarshal.Cast<byte, uint>(span), blockLen / 4, Native(10u, codec.BigEndian), Native(13u, codec.BigEndian)),
        _ => CountTerminators(span, blockLen),
    };

    private static long CountUnits<T>(ReadOnlySpan<T> span, int blockUnits, T lf, T cr) where T : unmanaged, IEquatable<T>
    {
        var block = span[..Math.Min(blockUnits, span.Length)];
        long n = block.Count(lf);
        int from = block.IndexOf(cr);
        while (from >= 0)
        {
            if (!(from + 1 < span.Length && span[from + 1].Equals(lf))) n++;
            int next = block[(from + 1)..].IndexOf(cr);
            from = next < 0 ? -1 : from + 1 + next;
        }
        return n;
    }

    /// <summary>Byte index of the first terminator at or after byte index <paramref name="from"/> (a multiple of the unit width), or -1.</summary>
    public static int FindTerminator(ReadOnlySpan<byte> span, int from, TextCodec codec) => codec.Alignment switch
    {
        2 => FindUnits(MemoryMarshal.Cast<byte, ushort>(span), from / 2, Native((ushort)10, codec.BigEndian), Native((ushort)13, codec.BigEndian)),
        4 => FindUnits(MemoryMarshal.Cast<byte, uint>(span), from / 4, Native(10u, codec.BigEndian), Native(13u, codec.BigEndian)),
        _ => FindTerminator(span, from),
    };

    /// <returns>The byte index (unit index times the unit size) or -1.</returns>
    private static int FindUnits<T>(ReadOnlySpan<T> span, int from, T lf, T cr) where T : unmanaged, IEquatable<T>
    {
        while (true)
        {
            int i = span[from..].IndexOfAny(lf, cr);
            if (i < 0) return -1;
            i += from;
            if (span[i].Equals(lf) || !(i + 1 < span.Length && span[i + 1].Equals(lf))) return i * Unsafe.SizeOf<T>();
            from = i + 1;
        }
    }

    internal static uint Unit(ReadOnlySpan<byte> d, int i, TextCodec c) => c.Alignment switch
    {
        2 => c.BigEndian ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(d[i..]) : System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(d[i..]),
        4 => c.BigEndian ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(d[i..]) : System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(d[i..]),
        _ => d[i],
    };

    /// <summary>Byte index of the <paramref name="n"/>-th (1-based) terminator located below <paramref name="blockLen"/>.</summary>
    public static int FindNthTerminator(ReadOnlySpan<byte> span, int blockLen, long n, TextCodec codec)
    {
        int pos = 0;
        while (true)
        {
            int i = FindTerminator(span, pos, codec);
            if (i < 0 || i >= blockLen) throw new InvalidOperationException("terminator not found in block");
            if (--n == 0) return i;
            pos = i + codec.Alignment;
        }
    }

    /// <summary>
    /// Decodes whole lines from <paramref name="data"/>. When <paramref name="atEof"/> is false the span is
    /// only a window: an unterminated tail and a CR at the last byte are left undecoded.
    /// </summary>
    public static List<string> Decode(ReadOnlySpan<byte> data, TextCodec enc, int maxLines, bool atEof)
    {
        int w = enc.Alignment;
        var lines = new List<string>(Math.Min(maxLines, 256));
        int pos = 0;
        while (lines.Count < maxLines)
        {
            int i = FindTerminator(data, pos, enc);
            if (i < 0)
            {
                if (atEof) lines.Add(enc.GetString(data[pos..]));
                break;
            }
            bool cr = Unit(data, i, enc) == 13;
            if (!atEof && cr && i + w >= data.Length) break;
            int end = i;
            if (!cr && end >= pos + w && Unit(data, end - w, enc) == 13) end -= w;
            lines.Add(enc.GetString(data[pos..end]));
            pos = i + w;
        }
        return lines;
    }
}
