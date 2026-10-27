namespace NksHub.Textida.Plugins.Git;

/// <summary>
/// One conflict block of a file with merge markers, as byte offsets: <c>Start..End</c> is the whole block, <c>OursStart..OursEnd</c> the lines of our side (without the marker lines
/// and without a <c>|||||||</c> base section), <c>TheirsStart..TheirsEnd</c> the lines of their side.
/// </summary>
public sealed record ConflictBlock(long Start, long OursStart, long OursEnd, long TheirsStart, long TheirsEnd, long End);

public static class Conflicts
{
    private static bool Marker(ReadOnlySpan<byte> line, byte c) =>
        line.Length >= 7 && line[..7].IndexOfAnyExcept(c) < 0 && (line.Length == 7 || line[7] is (byte)' ' or (byte)'\r' or (byte)'\n');

    /// <summary>All complete blocks of an ASCII compatible text; a block that is not closed is left out.</summary>
    public static List<ConflictBlock> Find(ReadOnlySpan<byte> text)
    {
        var blocks = new List<ConflictBlock>();
        long start = 0, oursStart = 0, oursEnd = -1, theirsStart = 0;
        int state = 0; // 0 outside, 1 ours, 2 base, 3 theirs
        for (int at = 0; at < text.Length;)
        {
            int length = text[at..].IndexOf((byte)'\n') is var nl and >= 0 ? nl + 1 : text.Length - at;
            var line = text.Slice(at, length);
            long next = at + length;
            if (Marker(line, (byte)'<'))
            {
                (state, start, oursStart, oursEnd) = (1, at, next, -1);
            }
            else if (state == 1 && Marker(line, (byte)'|'))
            {
                (state, oursEnd) = (2, at);
            }
            else if (state is 1 or 2 && Marker(line, (byte)'=') && line.TrimEnd((byte)'\n').TrimEnd((byte)'\r').Length == 7)
            {
                if (oursEnd < 0) oursEnd = at;
                (state, theirsStart) = (3, next);
            }
            else if (state == 3 && Marker(line, (byte)'>'))
            {
                blocks.Add(new ConflictBlock(start, oursStart, oursEnd, theirsStart, at, next));
                state = 0;
            }
            at += length;
        }
        return blocks;
    }

    /// <summary>The block the caret stands in (any byte of its marker and content lines); null when it is in none.</summary>
    public static ConflictBlock? At(IEnumerable<ConflictBlock> blocks, long caret) => blocks.FirstOrDefault(b => caret >= b.Start && caret < b.End);
}
