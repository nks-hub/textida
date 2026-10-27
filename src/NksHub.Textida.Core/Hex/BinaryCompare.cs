using System.Buffers;
using Microsoft.Win32.SafeHandles;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Hex;

/// <summary>How a stretch of the left file corresponds to a stretch of the right one.</summary>
public enum DiffKind
{
    /// <summary>Both sides have bytes here and they differ (the lengths may differ too).</summary>
    Replace,
    /// <summary>The right side has bytes that the left one lacks; <see cref="BinaryDifference.Left"/> is empty, at the place where they go.</summary>
    Insert,
    /// <summary>The left side has bytes that the right one lacks; <see cref="BinaryDifference.Right"/> is empty.</summary>
    Delete,
}

/// <summary>One difference: the bytes it covers in the left file and in the right file. Between two differences both files hold the same bytes.</summary>
public readonly record struct BinaryDifference(DiffKind Kind, ByteRange Left, ByteRange Right);

public sealed record BinaryCompareOptions
{
    /// <summary>Stops after this many differences and reports <see cref="BinaryCompareResult.Truncated"/>; a list of millions of differences between two unrelated files is no use and would fill the memory.</summary>
    public int MaxDifferences { get; init; } = 1_000_000;

    /// <summary>The aligned comparison reads both files into memory; a file over this size is compared byte by byte.</summary>
    public long AlignLimit { get; init; } = BinaryCompare.DefaultAlignLimit;

    /// <summary>Called with the number of bytes of each file handled so far, from the thread that compares.</summary>
    public Action<long>? Progress { get; init; }
}

/// <summary>The differences of two files and the arithmetic to find the place in the other file for a place in one of them.</summary>
public sealed class BinaryCompareResult
{
    internal BinaryCompareResult(long leftLength, long rightLength, bool aligned, bool truncated, IReadOnlyList<BinaryDifference> differences)
    {
        LeftLength = leftLength;
        RightLength = rightLength;
        Aligned = aligned;
        Truncated = truncated;
        Differences = differences;
    }

    public long LeftLength { get; }
    public long RightLength { get; }
    /// <summary>True when the comparison looked for inserted and deleted bytes; false when it compared the byte at every offset with the byte at the same offset.</summary>
    public bool Aligned { get; }
    /// <summary>The list stops at <see cref="BinaryCompareOptions.MaxDifferences"/>; the files may differ further on.</summary>
    public bool Truncated { get; }
    public IReadOnlyList<BinaryDifference> Differences { get; }
    public bool AreEqual => Differences.Count == 0;

    /// <summary>Bytes of the left file that differ.</summary>
    public long LeftChanged => Differences.Sum(d => d.Left.Length);
    public long RightChanged => Differences.Sum(d => d.Right.Length);

    /// <summary>The offset in the right file that holds what is at <paramref name="offset"/> in the left one; inside a difference, the same distance from its start, at most its end.</summary>
    public long MapLeftToRight(long offset) => Map(offset, left: true);

    public long MapRightToLeft(long offset) => Map(offset, left: false);

    private long Map(long offset, bool left)
    {
        var list = Differences;
        int lo = 0, hi = list.Count; // first difference that ends after the offset
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            var side = left ? list[mid].Left : list[mid].Right;
            if (side.End <= offset) lo = mid + 1; else hi = mid;
        }
        if (lo < list.Count)
        {
            var d = list[lo];
            var from = left ? d.Left : d.Right;
            var to = left ? d.Right : d.Left;
            if (offset >= from.Start) return to.Start + Math.Min(offset - from.Start, to.Length);
        }
        long shift = lo == 0 ? 0 : (left ? list[lo - 1].Right.End - list[lo - 1].Left.End : list[lo - 1].Left.End - list[lo - 1].Right.End);
        return offset + shift;
    }
}

/// <summary>A file read through the offsets of a snapshot, for comparing a file on disk without opening a document on it.</summary>
public sealed class FileBytes : IByteSnapshot, IDisposable
{
    private readonly SafeFileHandle _handle;

    public FileBytes(string path)
    {
        _handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
        ByteLength = RandomAccess.GetLength(_handle);
    }

    public long ByteLength { get; }

    public int Read(long offset, Span<byte> destination) =>
        offset >= ByteLength ? 0 : RandomAccess.Read(_handle, destination[..(int)Math.Min(destination.Length, ByteLength - offset)], offset);

    public void Dispose() => _handle.Dispose();
}

/// <summary>
/// Binary comparison (F244). <see cref="Positional"/> compares the byte at every offset with the byte at the same offset, in blocks, for files of any size; an
/// inserted byte makes everything after it differ. <see cref="Aligned"/> reads both files, keeps what they share at the start and at the end, and looks in between for
/// stretches that one file has in a different place of the other (anchors of 32 bytes found by a rolling hash and extended both ways), so an inserted block shows as an
/// insertion and not as a shift of everything behind it.
/// </summary>
public static class BinaryCompare
{
    /// <summary>The default size up to which the aligned comparison is used, per file.</summary>
    public const long DefaultAlignLimit = 256L << 20;

    private const int Block = 1 << 20;
    private const int Anchor = 32;
    private const int MaxChain = 8;

    /// <summary>Aligned when both files are within the limit, byte by byte otherwise.</summary>
    public static BinaryCompareResult Compare(IByteSnapshot left, IByteSnapshot right, BinaryCompareOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        return left.ByteLength <= options.AlignLimit && right.ByteLength <= options.AlignLimit
            ? Aligned(left, right, options, cancellationToken)
            : Positional(left, right, options, cancellationToken);
    }

    // ---- byte by byte ----

    private static void ReadExactly(IByteSnapshot source, long offset, Span<byte> destination)
    {
        for (int filled = 0; filled < destination.Length;)
        {
            int n = source.Read(offset + filled, destination[filled..]);
            if (n <= 0) throw new EndOfStreamException("The source ended before its declared length.");
            filled += n;
        }
    }

    public static BinaryCompareResult Positional(IByteSnapshot left, IByteSnapshot right, BinaryCompareOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        options ??= new();
        long la = left.ByteLength, lb = right.ByteLength, common = Math.Min(la, lb);
        var diffs = new List<BinaryDifference>();
        var a = ArrayPool<byte>.Shared.Rent(Block);
        var b = ArrayPool<byte>.Shared.Rent(Block);
        try
        {
            long open = -1;
            for (long pos = 0; pos < common;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int n = (int)Math.Min(Block, common - pos);
                ReadExactly(left, pos, a.AsSpan(0, n));
                ReadExactly(right, pos, b.AsSpan(0, n));
                var x = a.AsSpan(0, n);
                var y = b.AsSpan(0, n);
                int i = 0;
                if (open < 0 && x.SequenceEqual(y))
                {
                    pos += n;
                    options.Progress?.Invoke(pos);
                    continue;
                }
                while (i < n)
                {
                    if (open < 0)
                    {
                        i += x[i..].CommonPrefixLength(y[i..]);
                        if (i >= n) break;
                        open = pos + i;
                    }
                    int j = i;
                    while (j < n && x[j] != y[j]) j++;
                    if (j < n)
                    {
                        diffs.Add(new(DiffKind.Replace, new ByteRange(open, pos + j), new ByteRange(open, pos + j)));
                        open = -1;
                        if (diffs.Count >= options.MaxDifferences) return new(la, lb, aligned: false, truncated: true, diffs);
                    }
                    i = j;
                }
                pos += n;
                options.Progress?.Invoke(pos);
            }
            if (la != lb)
            {
                if (open >= 0)
                {
                    diffs.Add(new(DiffKind.Replace, new ByteRange(open, la), new ByteRange(open, lb)));
                    open = -1;
                }
                else if (la > lb) diffs.Add(new(DiffKind.Delete, new ByteRange(common, la), new ByteRange(lb, lb)));
                else diffs.Add(new(DiffKind.Insert, new ByteRange(la, la), new ByteRange(common, lb)));
            }
            else if (open >= 0) diffs.Add(new(DiffKind.Replace, new ByteRange(open, common), new ByteRange(open, common)));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(a);
            ArrayPool<byte>.Shared.Return(b);
        }
        return new(la, lb, aligned: false, truncated: false, diffs);
    }

    // ---- with inserted and deleted bytes ----

    private readonly record struct Run(long Left, long Right, long Length);

    private static byte[] ReadAll(IByteSnapshot source, CancellationToken ct, Action<long>? progress)
    {
        var bytes = new byte[source.ByteLength];
        for (long at = 0; at < bytes.Length;)
        {
            ct.ThrowIfCancellationRequested();
            int n = source.Read(at, bytes.AsSpan((int)at, (int)Math.Min(Block, bytes.Length - at)));
            if (n <= 0) throw new EndOfStreamException("The source ended before its declared length.");
            at += n;
            progress?.Invoke(at);
        }
        return bytes;
    }

    /// <exception cref="NotSupportedException">A file is over <see cref="BinaryCompareOptions.AlignLimit"/> (or 2 GiB).</exception>
    public static BinaryCompareResult Aligned(IByteSnapshot left, IByteSnapshot right, BinaryCompareOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        options ??= new();
        if (Math.Max(left.ByteLength, right.ByteLength) > Math.Min(options.AlignLimit, Array.MaxLength))
            throw new NotSupportedException("A file is too large for the aligned comparison; compare it byte by byte.");
        var a = ReadAll(left, cancellationToken, null);
        var b = ReadAll(right, cancellationToken, options.Progress);
        var runs = new List<Run>();

        int prefix = a.AsSpan().CommonPrefixLength(b);
        int suffix = 0;
        var restA = a.AsSpan(prefix);
        var restB = b.AsSpan(prefix);
        int max = Math.Min(restA.Length, restB.Length);
        while (suffix < max && restA[^(suffix + 1)] == restB[^(suffix + 1)]) suffix++;
        if (prefix > 0) runs.Add(new Run(0, 0, prefix));
        FindRuns(a.AsSpan(prefix, a.Length - prefix - suffix), b.AsSpan(prefix, b.Length - prefix - suffix), prefix, prefix, runs, cancellationToken);
        if (suffix > 0) runs.Add(new Run(a.Length - suffix, b.Length - suffix, suffix));

        var diffs = new List<BinaryDifference>();
        long endA = 0, endB = 0;
        bool truncated = false;
        void Gap(long toA, long toB)
        {
            if (toA == endA && toB == endB) return;
            var kind = toA == endA ? DiffKind.Insert : toB == endB ? DiffKind.Delete : DiffKind.Replace;
            diffs.Add(new(kind, new ByteRange(endA, toA), new ByteRange(endB, toB)));
        }
        foreach (var run in runs)
        {
            Gap(run.Left, run.Right);
            endA = run.Left + run.Length;
            endB = run.Right + run.Length;
            if (diffs.Count >= options.MaxDifferences) { truncated = true; break; }
        }
        if (!truncated) Gap(a.Length, b.Length);
        return new(a.Length, b.Length, aligned: true, truncated, diffs);
    }

    /// <summary>Appends the stretches that <paramref name="a"/> and <paramref name="b"/> share, in ascending order of both offsets (offsets are relative to the two bases).</summary>
    private static void FindRuns(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, long baseA, long baseB, List<Run> runs, CancellationToken ct)
    {
        if (a.Length < Anchor || b.Length < Anchor) return;
        int blocks = a.Length / Anchor;
        int size = 1;
        while (size < blocks * 2) size <<= 1;
        var heads = new int[size];
        Array.Fill(heads, -1);
        var next = new int[blocks];
        int mask = size - 1;
        for (int k = 0; k < blocks; k++)
        {
            uint h = HashOf(a.Slice(k * Anchor, Anchor));
            int slot = Slot(h, mask);
            next[k] = heads[slot];
            heads[slot] = k;
        }

        uint top = 1; // P^(Anchor-1)
        for (int i = 1; i < Anchor; i++) top *= Prime;
        long lastA = 0, lastB = 0, diagonal = 0;
        int pos = 0;
        uint hash = HashOf(b[..Anchor]);
        while (pos + Anchor <= b.Length)
        {
            if ((pos & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            int slot = Slot(hash, mask);
            int best = -1;
            long bestDistance = long.MaxValue;
            int walked = 0;
            for (int k = heads[slot]; k >= 0 && walked < MaxChain; k = next[k], walked++)
            {
                long at = (long)k * Anchor;
                if (at < lastA) continue;
                long distance = Math.Abs(at - pos - diagonal);
                if (distance >= bestDistance) continue;
                if (!a.Slice((int)at, Anchor).SequenceEqual(b.Slice(pos, Anchor))) continue;
                best = k;
                bestDistance = distance;
            }
            if (best < 0)
            {
                if (pos + Anchor >= b.Length) break;
                hash = (hash - b[pos] * top) * Prime + b[pos + Anchor];
                pos++;
                continue;
            }
            int ia = best * Anchor;
            int length = Anchor + a[(ia + Anchor)..].CommonPrefixLength(b[(pos + Anchor)..]);
            int back = 0;
            while (ia - back - 1 >= lastA && pos - back - 1 >= lastB && a[ia - back - 1] == b[pos - back - 1]) back++;
            runs.Add(new Run(baseA + ia - back, baseB + pos - back, length + back));
            lastA = ia + length;
            lastB = pos + length;
            diagonal = ia - pos;
            pos += length;
            if (pos + Anchor > b.Length) break;
            hash = HashOf(b.Slice(pos, Anchor));
        }
    }

    private const uint Prime = 0x01000193;

    private static uint HashOf(ReadOnlySpan<byte> window)
    {
        uint h = 0;
        foreach (byte x in window) h = h * Prime + x;
        return h;
    }

    private static int Slot(uint hash, int mask) => (int)((hash * 0x9E3779B1u) >> 7) & mask;
}
