using System.Text;

namespace NksPad.Core;

/// <summary>
/// Line terminator rules shared by the index, the reader and the tests. A terminator is a single byte:
/// every LF, plus every CR that is not directly followed by LF. A line therefore starts at offset 0
/// and right after each terminator, so a file with N terminators has N + 1 lines (the last may be empty).
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

    /// <summary>
    /// Decodes whole lines from <paramref name="data"/>. When <paramref name="atEof"/> is false the span is
    /// only a window: an unterminated tail and a CR at the last byte are left undecoded.
    /// </summary>
    public static List<string> Decode(ReadOnlySpan<byte> data, Encoding enc, int maxLines, bool atEof)
    {
        var lines = new List<string>(Math.Min(maxLines, 256));
        int pos = 0;
        while (lines.Count < maxLines)
        {
            int i = FindTerminator(data, pos);
            if (i < 0)
            {
                if (atEof) lines.Add(enc.GetString(data[pos..]));
                break;
            }
            if (!atEof && data[i] == (byte)'\r' && i + 1 == data.Length) break;
            int end = i;
            if (data[i] == (byte)'\n' && end > pos && data[end - 1] == (byte)'\r') end--;
            lines.Add(enc.GetString(data[pos..end]));
            pos = i + 1;
        }
        return lines;
    }
}
