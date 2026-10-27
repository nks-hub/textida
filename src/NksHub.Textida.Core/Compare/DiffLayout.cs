using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Compare;

public enum DiffSide { A, B }

public enum DiffRowKind { Equal, Added, Removed, Changed }

/// <summary>One row of the side-by-side view; a side that has no line in the row (filler opposite an inserted or removed block) has line -1.</summary>
public readonly record struct DiffRow(long A, long B, DiffRowKind Kind);

/// <summary>
/// The rows of a diff shown side by side: equal lines are one row, a changed block takes as many rows as its longer side, so both sides stay level.
/// It is also the map between the two sides: <see cref="MapLine"/> says which line of the other side stands next to a line, across inserted and removed blocks.
/// Built from the hunks only; the cost of a lookup is a binary search.
/// </summary>
public sealed class DiffLayout
{
    private readonly IReadOnlyList<DiffHunk> _hunks;
    private readonly long[] _rowStart;
    private readonly long[] _cum; // _cum[h]: rows added by the hunks before h (rows beyond the lines of side A)

    public DiffLayout(IReadOnlyList<DiffHunk> hunks, long linesA, long linesB)
    {
        _hunks = hunks;
        LinesA = linesA;
        LinesB = linesB;
        _rowStart = new long[hunks.Count];
        _cum = new long[hunks.Count + 1];
        for (int h = 0; h < hunks.Count; h++)
        {
            _rowStart[h] = hunks[h].AStart + _cum[h];
            _cum[h + 1] = _cum[h] + Math.Max(0, hunks[h].BCount - hunks[h].ACount);
        }
        RowCount = linesA + _cum[hunks.Count];
    }

    public long LinesA { get; }
    public long LinesB { get; }
    public long RowCount { get; }
    public int HunkCount => _hunks.Count;

    private long Rows(int h) => Math.Max(_hunks[h].ACount, _hunks[h].BCount);

    /// <summary>The hunk whose rows contain the row, or -1.</summary>
    public int HunkAtRow(long row)
    {
        int h = LastBefore(_rowStart, row);
        return h >= 0 && row < _rowStart[h] + Rows(h) ? h : -1;
    }

    /// <summary>First row of a hunk.</summary>
    public long RowOfHunk(int hunk) => _rowStart[hunk];

    public DiffRow Row(long row)
    {
        int h = LastBefore(_rowStart, row);
        if (h >= 0 && row < _rowStart[h] + Rows(h))
        {
            var hunk = _hunks[h];
            long j = row - _rowStart[h];
            var kind = hunk.ACount == 0 ? DiffRowKind.Added : hunk.BCount == 0 ? DiffRowKind.Removed : DiffRowKind.Changed;
            return new DiffRow(j < hunk.ACount ? hunk.AStart + j : -1, j < hunk.BCount ? hunk.BStart + j : -1, kind);
        }
        long a = row - _cum[h + 1];
        long delta = h < 0 ? 0 : _hunks[h].BStart + _hunks[h].BCount - _hunks[h].AStart - _hunks[h].ACount;
        return new DiffRow(a, a + delta, DiffRowKind.Equal);
    }

    public long RowOf(DiffSide side, long line)
    {
        if (side == DiffSide.A)
        {
            int h = LastBefore(_hunks.Count, i => _hunks[i].AStart, line);
            return h >= 0 && line < _hunks[h].AStart + _hunks[h].ACount ? _rowStart[h] + line - _hunks[h].AStart : line + _cum[h + 1];
        }
        else
        {
            int h = LastBefore(_hunks.Count, i => _hunks[i].BStart, line);
            if (h < 0) return line;
            var hunk = _hunks[h];
            return line < hunk.BStart + hunk.BCount ? _rowStart[h] + line - hunk.BStart : _rowStart[h] + Rows(h) + line - (hunk.BStart + hunk.BCount);
        }
    }

    /// <summary>
    /// The line of the other side that stands in the same row as <paramref name="line"/>. Opposite a filler row it is the line where the
    /// block sits on the other side, so a view follows the position inside an inserted or removed block instead of jumping.
    /// </summary>
    public long MapLine(DiffSide side, long line)
    {
        long row = RowOf(side, line);
        var r = Row(row);
        long other = side == DiffSide.A ? r.B : r.A;
        if (other < 0)
        {
            int h = HunkAtRow(row);
            long j = row - _rowStart[h];
            other = side == DiffSide.A ? _hunks[h].BStart + Math.Min(j, _hunks[h].BCount) : _hunks[h].AStart + Math.Min(j, _hunks[h].ACount);
        }
        long lines = side == DiffSide.A ? LinesB : LinesA;
        return Math.Clamp(other, 0, Math.Max(0, lines - 1));
    }

    private static int LastBefore(long[] sorted, long value)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (sorted[mid] <= value) lo = mid + 1; else hi = mid;
        }
        return lo - 1;
    }

    private static int LastBefore(int count, Func<int, long> key, long value)
    {
        int lo = 0, hi = count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (key(mid) <= value) lo = mid + 1; else hi = mid;
        }
        return lo - 1;
    }
}

/// <summary>The one edit that makes a block of one side equal to the other.</summary>
/// <param name="Lossy">The target encoding cannot hold some characters of the other side; the edit would replace them.</param>
public readonly record struct TransferEdit(ByteRange Range, byte[] Bytes, bool Lossy);

public static class DiffTransfer
{
    private const long MaxBlockBytes = 1L << 30;

    /// <summary>
    /// Plans the edit that replaces the lines of one hunk in <paramref name="target"/> by the lines of the other side. Applying it as one edit
    /// to the document of the target is one undo step. Terminators at the end of the file are kept right, so the lines match after the edit.
    /// </summary>
    public static TransferEdit Plan(TextDiff diff, int hunk, DiffSide target)
    {
        var h = diff.Hunks[hunk];
        var t = target == DiffSide.A ? diff.A : diff.B;
        var s = target == DiffSide.A ? diff.B : diff.A;
        long tFirst = target == DiffSide.A ? h.AStart : h.BStart, tCount = target == DiffSide.A ? h.ACount : h.BCount;
        long sFirst = target == DiffSide.A ? h.BStart : h.AStart, sCount = target == DiffSide.A ? h.BCount : h.ACount;
        long tStart = t.OffsetOf(tFirst), tEnd = t.OffsetOf(tFirst + tCount);
        long sStart = s.OffsetOf(sFirst), sEnd = s.OffsetOf(sFirst + sCount);
        if (sEnd - sStart > MaxBlockBytes) throw new NotSupportedException("The block is too large for one edit.");

        var bytes = new byte[sEnd - sStart];
        for (int done = 0; done < bytes.Length;)
        {
            int n = s.Source.Snapshot.Read(sStart + done, bytes.AsSpan(done));
            if (n <= 0) throw new IOException("The source changed while the block was read.");
            done += n;
        }
        bool lossy = false;
        var tc = t.Source.Codec;
        if (!tc.SameBytesAs(s.Source.Codec))
        {
            string text = s.Source.Codec.GetString(bytes);
            lossy = tc.IsLossy && tc.LossyByteCount(text) > 0;
            bytes = lossy ? tc.EncodeLossy(text) : tc.Encode(text);
        }

        byte[] eol = t.Eol ?? (s.Eol is { } se && !tc.SameBytesAs(s.Source.Codec) ? tc.Encode(s.Source.Codec.GetString(se)) : s.Eol) ?? tc.Encode("\n");
        // a block inserted behind the last line needs the terminator that line lacks; a block removed from the end takes the terminator before it
        if (tCount == 0 && tFirst == t.LineCount && sCount > 0) bytes = [.. eol, .. bytes];
        else if (sCount == 0 && tFirst + tCount == t.LineCount && tFirst > 0) tStart -= TerminatorBefore(t.Source, tStart);
        (tStart, bytes) = KeepTerminatorsApart(t.Source, tStart, tEnd, bytes);
        return new TransferEdit(new ByteRange(tStart, tEnd), bytes, lossy);
    }

    /// <summary>
    /// A CR directly before an LF is one terminator, so a block that begins or ends next to the wrong neighbour would join two lines.
    /// Lone CRs that end the block, with an LF behind the edit, become LFs; lone CRs right before an edit that begins with an LF become LFs too
    /// (the edit then starts earlier). Only the spelling of those terminators changes, never the lines.
    /// </summary>
    private static (long Start, byte[] Bytes) KeepTerminatorsApart(DiffSource t, long start, long end, byte[] bytes)
    {
        var c = t.Codec;
        int w = c.Alignment;
        byte[] lf = c.Encode("\n");
        bool nextLf = end + w <= t.Length && UnitAt(t, end) == 10;
        if (nextLf)
            for (int i = bytes.Length - w; i >= 0 && LineScanner.Unit(bytes, i, c) == 13; i -= w) lf.CopyTo(bytes, i);
        uint first = bytes.Length >= w ? LineScanner.Unit(bytes, 0, c) : nextLf ? 10u : 0u;
        if (first == 10 && start - w >= t.Start && UnitAt(t, start - w) == 13)
        {
            long from = start;
            while (from - w >= t.Start && UnitAt(t, from - w) == 13) from -= w;
            bytes = [.. Enumerable.Range(0, (int)((start - from) / w)).SelectMany(_ => lf), .. bytes];
            start = from;
        }
        return (start, bytes);
    }

    private static uint UnitAt(DiffSource s, long offset)
    {
        var unit = new byte[s.Codec.Alignment];
        s.Snapshot.Read(offset, unit);
        return LineScanner.Unit(unit, 0, s.Codec);
    }

    /// <summary>Size of the terminator that ends right before the offset (CR LF is one).</summary>
    private static int TerminatorBefore(DiffSource s, long offset)
    {
        int w = s.Codec.Alignment;
        var two = new byte[2 * w];
        long from = Math.Max(s.Start, offset - 2 * w);
        int n = s.Snapshot.Read(from, two.AsSpan(0, (int)(offset - from)));
        if (n >= 2 * w && LineScanner.Unit(two, 0, s.Codec) == 13 && LineScanner.Unit(two, w, s.Codec) == 10) return 2 * w;
        return w;
    }
}
