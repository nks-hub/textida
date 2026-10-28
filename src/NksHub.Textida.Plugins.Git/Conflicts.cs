using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Plugins.Git;

/// <summary>
/// One conflict block of a file with merge markers, as byte offsets: <c>Start..End</c> is the whole block, <c>OursStart..OursEnd</c> the lines of our side (without the marker lines
/// and without a <c>|||||||</c> base section), <c>TheirsStart..TheirsEnd</c> the lines of their side.
/// </summary>
public sealed record ConflictBlock(long Start, long OursStart, long OursEnd, long TheirsStart, long TheirsEnd, long End);

public static class Conflicts
{
    /// <summary>The blocks of a text in the given encoding; see <see cref="Find(ReadOnlySpan{byte}, int, bool)"/>.</summary>
    public static List<ConflictBlock> Find(ReadOnlySpan<byte> text, TextCodec codec) =>
        Find(text, codec.Kind switch { CodecKind.Utf16 => 2, CodecKind.Utf32 => 4, _ => 1 }, codec.BigEndian);

    /// <summary>
    /// All complete blocks of a text whose characters are <paramref name="width"/> bytes wide (1 for UTF-8 and the single-byte pages, 2 for UTF-16, 4 for UTF-32) in the given byte order;
    /// a block that is not closed is left out. Offsets are bytes of the text, the byte order mark included.
    /// </summary>
    public static List<ConflictBlock> Find(ReadOnlySpan<byte> text, int width = 1, bool bigEndian = false)
    {
        var blocks = new List<ConflictBlock>();
        long start = 0, oursStart = 0, oursEnd = -1, theirsStart = 0;
        int state = 0; // 0 outside, 1 ours, 2 base, 3 theirs
        for (int at = 0; at + width <= text.Length;)
        {
            int next = LineEnd(text, at, width, bigEndian);
            if (Marker(text, at, next, width, bigEndian, '<'))
            {
                (state, start, oursStart, oursEnd) = (1, at, next, -1);
            }
            else if (state == 1 && Marker(text, at, next, width, bigEndian, '|'))
            {
                (state, oursEnd) = (2, at);
            }
            else if (state is 1 or 2 && Marker(text, at, next, width, bigEndian, '=') && Units(text, at, next, width, bigEndian) == 7)
            {
                if (oursEnd < 0) oursEnd = at;
                (state, theirsStart) = (3, next);
            }
            else if (state == 3 && Marker(text, at, next, width, bigEndian, '>'))
            {
                blocks.Add(new ConflictBlock(start, oursStart, oursEnd, theirsStart, at, next));
                state = 0;
            }
            at = next;
        }
        return blocks;
    }

    private static uint Unit(ReadOnlySpan<byte> text, int at, int width, bool bigEndian)
    {
        uint value = 0;
        for (int k = 0; k < width; k++) value = bigEndian ? value << 8 | text[at + k] : value | (uint)text[at + k] << 8 * k;
        return value;
    }

    /// <summary>The offset after the line that starts at <paramref name="at"/>, its line feed included; the end of the text for a last line without one.</summary>
    private static int LineEnd(ReadOnlySpan<byte> text, int at, int width, bool bigEndian)
    {
        for (int i = at; i + width <= text.Length; i += width)
            if (Unit(text, i, width, bigEndian) == '\n') return i + width;
        return text.Length;
    }

    /// <summary>The characters of a line without its line feed and the carriage return before it.</summary>
    private static int Units(ReadOnlySpan<byte> text, int at, int end, int width, bool bigEndian)
    {
        int count = (end - at) / width;
        if (count > 0 && Unit(text, at + (count - 1) * width, width, bigEndian) == '\n') count--;
        if (count > 0 && Unit(text, at + (count - 1) * width, width, bigEndian) == '\r') count--;
        return count;
    }

    private static bool Marker(ReadOnlySpan<byte> text, int at, int end, int width, bool bigEndian, char c)
    {
        int count = (end - at) / width;
        if (count < 7) return false;
        for (int k = 0; k < 7; k++)
            if (Unit(text, at + k * width, width, bigEndian) != c) return false;
        return count == 7 || Unit(text, at + 7 * width, width, bigEndian) is ' ' or '\r' or '\n';
    }

    /// <summary>The block the caret stands in (any byte of its marker and content lines); null when it is in none.</summary>
    public static ConflictBlock? At(IEnumerable<ConflictBlock> blocks, long caret) => blocks.FirstOrDefault(b => caret >= b.Start && caret < b.End);
}
