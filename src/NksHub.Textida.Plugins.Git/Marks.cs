using NksHub.Textida.Core.Compare;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Plugins.Git;

/// <summary>
/// A change shown in the gutter of the working tree document: <paramref name="Lines"/> lines from <paramref name="Line"/> (0 based) that differ from the index, or, with
/// <paramref name="Staged"/>, from <c>HEAD</c> but not from the index. A mark without lines stands where lines were removed. <paramref name="Kind"/> is <c>added</c>, <c>modified</c> or <c>removed</c>.
/// </summary>
public sealed record GutterMark(long Line, long Lines, string Kind, bool Staged);

public static class GutterMarks
{
    /// <summary>
    /// The marks for a file: its working tree text against the index, and the index against <c>HEAD</c> with the lines moved to where they stand in the working tree.
    /// All three are in the encoding <paramref name="codec"/>. The marks are in file order; where a staged and an unstaged change meet, the unstaged one comes last.
    /// </summary>
    public static List<GutterMark> Compute(byte[] tree, byte[] index, byte[] head, TextCodec codec, CancellationToken ct = default)
    {
        var unstaged = LineDiff.Compute(Source(index, codec), Source(tree, codec), null, ct).Hunks;
        var staged = LineDiff.Compute(Source(head, codec), Source(index, codec), null, ct).Hunks;
        var marks = new List<GutterMark>(unstaged.Count + staged.Count);
        foreach (var h in staged)
        {
            long first = Start(unstaged, h.BStart);
            marks.Add(new GutterMark(first, Math.Max(first, End(unstaged, h.BStart + h.BCount)) - first, KindOf(h), true));
        }
        foreach (var h in unstaged) marks.Add(new GutterMark(h.BStart, h.BCount, KindOf(h), false));
        return [.. marks.OrderBy(m => m.Line).ThenBy(m => m.Staged ? 0 : 1)];
    }

    private static DiffSource Source(byte[] bytes, TextCodec codec) => new(new ArraySnapshot(bytes), codec, codec.BomLengthIn(bytes));

    private static string KindOf(DiffHunk h) => h.ACount == 0 ? "added" : h.BCount == 0 ? "removed" : "modified";

    /// <summary>The line of the working tree that an index line stands at; a line inside a changed block maps to the start of the block.</summary>
    private static long Start(IReadOnlyList<DiffHunk> hunks, long line)
    {
        long delta = 0;
        foreach (var h in hunks)
        {
            if (line < h.AStart) break;
            if (line < h.AStart + h.ACount) return h.BStart;
            delta += h.BCount - h.ACount;
        }
        return line + delta;
    }

    /// <summary>The same for the end of a range of index lines (the first line after it); inside a changed block it maps to the end of the block.</summary>
    private static long End(IReadOnlyList<DiffHunk> hunks, long line)
    {
        long delta = 0;
        foreach (var h in hunks)
        {
            if (line <= h.AStart) break;
            if (line < h.AStart + h.ACount) return h.BStart + h.BCount;
            delta += h.BCount - h.ACount;
        }
        return line + delta;
    }
}
