using NksHub.Textida.Core.Compare;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Plugins.Git;

/// <summary>
/// A change shown in the gutter of the working tree document: <paramref name="Lines"/> lines from <paramref name="Line"/> (0 based) that differ from the index, or, with
/// <paramref name="Staged"/>, from <c>HEAD</c> but not from the index. A mark without lines stands where lines were removed. <paramref name="Kind"/> is <c>added</c>, <c>modified</c> or <c>removed</c>.
/// <paramref name="OldLines"/> lines from <paramref name="OldStart"/> of <paramref name="Base"/> (<c>index</c> or <c>head</c>) are what the change replaced.
/// </summary>
public sealed record GutterMark(long Line, long Lines, string Kind, bool Staged, long OldStart = 0, long OldLines = 0, string Base = GutterMarks.IndexBase);

/// <summary>The lines a change replaced, as the peek in the editor shows them and the revert of the change puts them back.</summary>
/// <param name="Lines">The decoded old lines, without their line breaks; a very long line is cut.</param>
/// <param name="EndsWithNewline">The last old line ends with a line break in the file it comes from.</param>
/// <param name="Truncated">There were more old lines than the limit; only the first <paramref name="Lines"/> are here, and the change cannot be reverted from them.</param>
public sealed record OldText(IReadOnlyList<string> Lines, bool EndsWithNewline, bool Truncated);

public static class GutterMarks
{
    public const string IndexBase = "index";
    public const string HeadBase = "head";

    /// <summary>
    /// The marks for a file: its working tree text against the index, and the index against <c>HEAD</c> with the lines moved to where they stand in the working tree.
    /// All three are in the encoding <paramref name="codec"/>. The marks are in file order; where a staged and an unstaged change meet, the unstaged one comes last.
    /// With <paramref name="againstHead"/> the working tree is compared with <c>HEAD</c> alone: every difference is one mark, drawn as staged when the index has no
    /// difference from the working tree where it lies.
    /// </summary>
    public static List<GutterMark> Compute(byte[] tree, byte[] index, byte[] head, TextCodec codec, CancellationToken ct = default, bool againstHead = false)
    {
        var unstaged = LineDiff.Compute(Source(index, codec), Source(tree, codec), null, ct).Hunks;
        if (againstHead)
        {
            var all = LineDiff.Compute(Source(head, codec), Source(tree, codec), null, ct).Hunks;
            return [.. all.Select(h => new GutterMark(h.BStart, h.BCount, KindOf(h), !Touches(unstaged, h), h.AStart, h.ACount, HeadBase))];
        }
        var staged = LineDiff.Compute(Source(head, codec), Source(index, codec), null, ct).Hunks;
        var marks = new List<GutterMark>(unstaged.Count + staged.Count);
        foreach (var h in staged)
        {
            long first = Start(unstaged, h.BStart);
            marks.Add(new GutterMark(first, Math.Max(first, End(unstaged, h.BStart + h.BCount)) - first, KindOf(h), true, h.AStart, h.ACount, HeadBase));
        }
        foreach (var h in unstaged) marks.Add(new GutterMark(h.BStart, h.BCount, KindOf(h), false, h.AStart, h.ACount, IndexBase));
        return [.. marks.OrderBy(m => m.Line).ThenBy(m => m.Staged ? 0 : 1)];
    }

    /// <summary>The old lines of a mark, read from the bytes of the file its <see cref="GutterMark.Base"/> names. At most <paramref name="maxLines"/> lines are read.</summary>
    public static OldText OldLinesOf(GutterMark mark, byte[] baseBytes, TextCodec codec, int maxLines, CancellationToken ct = default)
    {
        if (mark.OldLines == 0) return new OldText([], true, false);
        var lines = LineIndex.Build(Source(baseBytes, codec), ct);
        int take = (int)Math.Min(mark.OldLines, Math.Max(0, maxLines));
        return new OldText(lines.ReadLines(mark.OldStart, take), mark.OldStart + mark.OldLines < lines.LineCount, take < mark.OldLines);
    }

    private static DiffSource Source(byte[] bytes, TextCodec codec) => new(new ArraySnapshot(bytes), codec, codec.BomLengthIn(bytes));

    private static string KindOf(DiffHunk h) => h.ACount == 0 ? "added" : h.BCount == 0 ? "removed" : "modified";

    /// <summary>Whether a difference of the index from the working tree lies where (or next to) the hunk of <c>HEAD</c> does: then the change is not (all) in the index.</summary>
    private static bool Touches(IReadOnlyList<DiffHunk> unstaged, DiffHunk h)
    {
        long from = h.BStart, to = h.BStart + h.BCount;
        return unstaged.Any(u => u.BStart <= to && u.BStart + u.BCount >= from);
    }

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
