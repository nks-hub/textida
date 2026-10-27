using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Compare;

/// <summary>Lines [AStart, AStart + ACount) of the left side became lines [BStart, BStart + BCount) of the right side; either count may be 0.</summary>
public readonly record struct DiffHunk(long AStart, long ACount, long BStart, long BCount);

/// <summary>
/// Bounds of the line diff. The defaults are bounds of the implementation, not product values: <see cref="MaxMiddleLines"/> is the number of lines of
/// each side between the common beginning and end that are kept as 8-byte hashes (16 million lines are 128 MiB per side);
/// <see cref="MaxEditDistance"/> is the largest number of changed lines the search for a minimal diff follows in one piece; <see cref="MaxSteps"/>
/// caps the total work. Where a bound is hit the part is reported as one changed block, so the result is still a correct diff, just not a minimal one.
/// </summary>
public sealed record DiffLimits(long MaxMiddleLines = 16_000_000, int MaxEditDistance = 10_000, long MaxSteps = 2_000_000_000);

/// <summary>The line diff of two sources and the indexes to read their lines.</summary>
public sealed class TextDiff(IReadOnlyList<DiffHunk> hunks, LineIndex a, LineIndex b, bool exact)
{
    public IReadOnlyList<DiffHunk> Hunks => hunks;
    public LineIndex A => a;
    public LineIndex B => b;
    /// <summary>False when a bound of <see cref="DiffLimits"/> made at least one block coarser than a minimal diff.</summary>
    public bool Exact => exact;
    public bool IsIdentical => hunks.Count == 0;
    public DiffLayout Layout => field ??= new DiffLayout(hunks, a.LineCount, b.LineCount);
}

public static class LineDiff
{
    public static TextDiff Compute(DiffSource a, DiffSource b, DiffLimits? limits = null, CancellationToken ct = default)
    {
        limits ??= new DiffLimits();
        var ia = LineIndex.Build(a, ct);
        var ib = LineIndex.Build(b, ct);
        bool bytewise = SameBytes(a.Codec, b.Codec);
        long prefix = 0, suffix = 0;
        if (bytewise && a.Codec.Alignment == 1)
        {
            long common = CommonPrefix(a, b, ct);
            if (common == a.Length - a.Start && common == b.Length - b.Start) return new TextDiff([], ia, ib, true);
            prefix = common == 0 ? 0 : ia.LineOf(a.Start + common - 1);
            long room = Math.Min(a.Length - ia.OffsetOf(prefix), b.Length - ib.OffsetOf(prefix));
            long tail = CommonSuffix(a, b, room, ct);
            // a line start needs the byte before it, which lies inside the common tail only from its second byte on
            suffix = tail == 0 ? 0 : ia.LineCount - Math.Max(prefix, ia.LineAtOrAfter(a.Length - tail + 1));
            if (suffix > 0 && ib.LineCount - suffix < prefix) suffix = 0;
        }
        long na = ia.LineCount - prefix - suffix, nb = ib.LineCount - prefix - suffix;
        if (na < 0 || nb < 0) { prefix = 0; suffix = 0; na = ia.LineCount; nb = ib.LineCount; }

        var hunks = new List<DiffHunk>();
        bool exact = true;
        if (na > limits.MaxMiddleLines || nb > limits.MaxMiddleLines)
        {
            exact = false;
            Emit(hunks, prefix, na, prefix, nb);
        }
        else
        {
            var ha = Hash(ia, prefix, (int)na, bytewise, ct);
            var hb = Hash(ib, prefix, (int)nb, bytewise, ct);
            var myers = new Myers(ha, hb, prefix, hunks, limits, ct);
            myers.Run(0, ha.Length, 0, hb.Length);
            exact = myers.Exact;
        }
        return new TextDiff(hunks, ia, ib, exact);
    }

    private static bool SameBytes(TextCodec x, TextCodec y) => x.SameBytesAs(y);

    private static long CommonPrefix(DiffSource a, DiffSource b, CancellationToken ct)
    {
        long max = Math.Min(a.Length - a.Start, b.Length - b.Start), done = 0;
        var bufA = new byte[1 << 16];
        var bufB = new byte[1 << 16];
        while (done < max)
        {
            ct.ThrowIfCancellationRequested();
            int n = (int)Math.Min(bufA.Length, max - done);
            if (Fill(a.Snapshot, a.Start + done, bufA.AsSpan(0, n)) < n || Fill(b.Snapshot, b.Start + done, bufB.AsSpan(0, n)) < n) break;
            int same = bufA.AsSpan(0, n).CommonPrefixLength(bufB.AsSpan(0, n));
            done += same;
            if (same < n) break;
        }
        return done;
    }

    private static long CommonSuffix(DiffSource a, DiffSource b, long max, CancellationToken ct)
    {
        long done = 0;
        var bufA = new byte[1 << 16];
        var bufB = new byte[1 << 16];
        while (done < max)
        {
            ct.ThrowIfCancellationRequested();
            int n = (int)Math.Min(bufA.Length, max - done);
            if (Fill(a.Snapshot, a.Length - done - n, bufA.AsSpan(0, n)) < n || Fill(b.Snapshot, b.Length - done - n, bufB.AsSpan(0, n)) < n) break;
            int i = n;
            while (i > 0 && bufA[i - 1] == bufB[i - 1]) i--;
            done += n - i;
            if (i > 0) break;
        }
        return done;
    }

    private static int Fill(Documents.IByteSnapshot s, long offset, Span<byte> destination)
    {
        int done = 0;
        while (done < destination.Length)
        {
            int n = s.Read(offset + done, destination[done..]);
            if (n <= 0) break;
            done += n;
        }
        return done;
    }

    private static long[] Hash(LineIndex index, long firstLine, int count, bool bytewise, CancellationToken ct)
    {
        var hashes = new long[count];
        if (count == 0) return hashes;
        int i = 0;
        var codec = index.Source.Codec;
        LineScan.Run(index.Source, index.OffsetOf(firstLine), (_, content, _) =>
        {
            hashes[i++] = bytewise ? Hash64(content) : Hash64(MemoryMarshal.AsBytes(codec.GetString(content).AsSpan()));
            return i < count;
        }, ct: ct);
        return hashes;
    }

    internal static long Hash64(ReadOnlySpan<byte> data)
    {
        ulong h = 0x9E3779B97F4A7C15UL ^ (ulong)data.Length;
        var words = MemoryMarshal.Cast<byte, ulong>(data[..(data.Length & ~7)]);
        foreach (ulong w in words) h = BitOperations.RotateLeft((h ^ w) * 0xFF51AFD7ED558CCDUL, 29) * 0xC4CEB9FE1A85EC53UL;
        for (int k = data.Length & ~7; k < data.Length; k++) h = (h ^ data[k]) * 0x100000001B3UL;
        h ^= h >> 32;
        h *= 0xFF51AFD7ED558CCDUL;
        return (long)(h ^ (h >> 29));
    }

    internal static void Emit(List<DiffHunk> hunks, long aStart, long aCount, long bStart, long bCount)
    {
        if (hunks.Count > 0 && hunks[^1] is var last && last.AStart + last.ACount == aStart && last.BStart + last.BCount == bStart)
            hunks[^1] = new DiffHunk(last.AStart, last.ACount + aCount, last.BStart, last.BCount + bCount);
        else hunks.Add(new DiffHunk(aStart, aCount, bStart, bCount));
    }

    /// <summary>
    /// The linear-space variant of Myers' O(ND) algorithm (middle snake, divide and conquer) over line hashes: memory is O(lines + D),
    /// time O((lines) * D) in the worst case and close to O(lines + D^2) for ordinary edits.
    /// </summary>
    private sealed class Myers(long[] a, long[] b, long origin, List<DiffHunk> hunks, DiffLimits limits, CancellationToken ct)
    {
        private long _budget = limits.MaxSteps;
        public bool Exact { get; private set; } = true;

        public void Run(int a0, int a1, int b0, int b1)
        {
            while (a0 < a1 && b0 < b1 && a[a0] == b[b0]) { a0++; b0++; }
            while (a0 < a1 && b0 < b1 && a[a1 - 1] == b[b1 - 1]) { a1--; b1--; }
            if (a0 == a1 || b0 == b1)
            {
                if (a0 < a1 || b0 < b1) Emit(hunks, origin + a0, a1 - a0, origin + b0, b1 - b0);
                return;
            }
            if (!MiddleSnake(a0, a1, b0, b1, out int x, out int y, out int u, out int v))
            {
                Exact = false;
                Emit(hunks, origin + a0, a1 - a0, origin + b0, b1 - b0);
                return;
            }
            Run(a0, a0 + x, b0, b0 + y);
            Run(a0 + u, a1, b0 + v, b1);
        }

        private bool MiddleSnake(int a0, int a1, int b0, int b1, out int sx, out int sy, out int ex, out int ey)
        {
            int n = a1 - a0, m = b1 - b0, delta = n - m;
            bool odd = (delta & 1) != 0;
            int max = Math.Min((n + m + 1) / 2, limits.MaxEditDistance), off = max + 1;
            var vf = ArrayPool<int>.Shared.Rent(2 * max + 3);
            var vb = ArrayPool<int>.Shared.Rent(2 * max + 3);
            sx = sy = ex = ey = 0;
            try
            {
                vf[off + 1] = 0;
                vb[off + 1] = 0;
                for (int d = 0; d <= max; d++)
                {
                    ct.ThrowIfCancellationRequested();
                    for (int k = -d; k <= d; k += 2)
                    {
                        int x = k == -d || (k != d && vf[off + k - 1] < vf[off + k + 1]) ? vf[off + k + 1] : vf[off + k - 1] + 1;
                        int xs = x, ys = x - k, y = ys;
                        while (x < n && y < m && a[a0 + x] == b[b0 + y]) { x++; y++; }
                        _budget -= 1 + (x - xs);
                        vf[off + k] = x;
                        if (odd && k >= delta - (d - 1) && k <= delta + (d - 1) && x + vb[off + delta - k] >= n)
                            return Found(n, m, out sx, out sy, out ex, out ey, xs, ys, x, y);
                    }
                    for (int k = -d; k <= d; k += 2)
                    {
                        int x = k == -d || (k != d && vb[off + k - 1] < vb[off + k + 1]) ? vb[off + k + 1] : vb[off + k - 1] + 1;
                        int xs = x, ys = x - k, y = ys;
                        while (x < n && y < m && a[a1 - 1 - x] == b[b1 - 1 - y]) { x++; y++; }
                        _budget -= 1 + (x - xs);
                        vb[off + k] = x;
                        if (!odd && k >= delta - d && k <= delta + d && x + vf[off + delta - k] >= n)
                            return Found(n, m, out sx, out sy, out ex, out ey, n - x, m - y, n - xs, m - ys);
                    }
                    if (_budget < 0) return false;
                }
                return false;
            }
            finally
            {
                ArrayPool<int>.Shared.Return(vf);
                ArrayPool<int>.Shared.Return(vb);
            }
        }

        // a snake that is the whole problem would not shrink it; this cannot happen for a problem with a difference at both ends
        private static bool Found(int n, int m, out int sx, out int sy, out int ex, out int ey, int x0, int y0, int x1, int y1)
        {
            (sx, sy, ex, ey) = (x0, y0, x1, y1);
            return !(x0 == n && y0 == m) && !(x1 == 0 && y1 == 0);
        }
    }
}
