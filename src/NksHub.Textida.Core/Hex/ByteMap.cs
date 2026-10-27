using System.Numerics;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Hex;

/// <summary>What a map of bytes shows for a stretch of the file.</summary>
public enum ByteMapMode
{
    /// <summary>How unpredictable the bytes are: 0 for a run of one byte, the top for compressed or encrypted data.</summary>
    Entropy,
    /// <summary>The kind of byte that makes up most of the stretch.</summary>
    Class,
}

/// <summary>The kind of a byte for the class map.</summary>
public enum ByteClass : byte
{
    Zero,
    /// <summary>0xFF.</summary>
    Ones,
    /// <summary>Printable ASCII, tab, line feed and carriage return.</summary>
    Text,
    /// <summary>The other bytes below 0x20 and 0x7F.</summary>
    Control,
    /// <summary>0x80 to 0xFE.</summary>
    High,
}

public static class ByteClasses
{
    public static ByteClass Of(byte b) => b switch
    {
        0 => ByteClass.Zero,
        0xFF => ByteClass.Ones,
        >= 0x20 and <= 0x7E or 9 or 10 or 13 => ByteClass.Text,
        < 0x20 or 0x7F => ByteClass.Control,
        _ => ByteClass.High,
    };
}

/// <summary>
/// A picture of a file in <see cref="Buckets"/> stretches of equal length: for each the entropy of the bytes read from it and the class that makes up most of them.
/// A stretch is read in blocks spread over its length, so a map of a huge file after the first pass is a sample; <see cref="IsExact"/> says when every byte was read.
/// </summary>
public sealed class ByteMap
{
    internal ByteMap(long length, int buckets)
    {
        Length = length;
        Buckets = buckets;
        Entropy = new byte[buckets];
        Classes = new ByteClass[buckets];
        Read = new long[buckets];
    }

    public long Length { get; }
    public int Buckets { get; }
    /// <summary>Per stretch: the entropy in bits per byte, 0 to 8, scaled to 0 to 255.</summary>
    public byte[] Entropy { get; }
    public ByteClass[] Classes { get; }
    /// <summary>Per stretch: how many of its bytes were read; 0 until the first pass reaches it.</summary>
    public long[] Read { get; }

    public long BytesRead => Read.Sum();
    public double Coverage => Length == 0 ? 1 : Math.Min(1, (double)BytesRead / Length);
    public bool IsExact => Enumerable.Range(0, Buckets).All(i => Read[i] >= BucketEnd(i) - BucketStart(i));

    public long BucketStart(int bucket) => (long)((UInt128)(ulong)Length * (uint)bucket / (uint)Buckets);
    public long BucketEnd(int bucket) => bucket + 1 >= Buckets ? Length : BucketStart(bucket + 1);

    /// <summary>The stretch a byte is in.</summary>
    public int BucketOf(long offset)
    {
        if (Buckets == 0) return 0;
        int guess = (int)Math.Min(Buckets - 1, (long)((UInt128)(ulong)Math.Max(0, offset) * (uint)Buckets / (ulong)Math.Max(1, Length)));
        while (guess > 0 && BucketStart(guess) > offset) guess--;
        while (guess + 1 < Buckets && BucketStart(guess + 1) <= offset) guess++;
        return guess;
    }

    /// <summary>A copy that a view can keep while the builder goes on.</summary>
    public ByteMap Copy()
    {
        var copy = new ByteMap(Length, Buckets);
        Entropy.CopyTo(copy.Entropy, 0);
        Classes.CopyTo(copy.Classes, 0);
        Read.CopyTo(copy.Read, 0);
        return copy;
    }
}

/// <summary>
/// Builds a <see cref="ByteMap"/> in passes. The first pass reads one block of every stretch, so even a file of tens of GiB gets its picture after a few thousand reads; each
/// later pass reads four times as many blocks, spread evenly, and publishes a better map. Up to <see cref="FullComputeLimit"/> bytes are read in all: a smaller file ends exact,
/// a larger one ends as the best sample that reading limit allows.
/// </summary>
public static class ByteMapBuilder
{
    /// <summary>The most bytes a build reads; a file up to this size is read whole.</summary>
    public const long FullComputeLimit = 1L << 30;

    /// <summary>Size of one read.</summary>
    public const int BlockBytes = 4096;

    private sealed class Stretch
    {
        public readonly long[] Counts = new long[256];
        public int Done;
        public int Sequence;
    }

    /// <param name="published">Called after each pass with a copy of the map, on the calling thread.</param>
    /// <param name="maxParallelism">Threads that read at once; null uses every processor. A build that runs beside a window leaves some free.</param>
    public static ByteMap Build(IByteSnapshot snapshot, int buckets, Action<ByteMap>? published = null, CancellationToken cancellationToken = default, int? maxParallelism = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        long length = snapshot.ByteLength;
        buckets = (int)Math.Min(Math.Max(0, buckets), length);
        var map = new ByteMap(length, buckets);
        if (buckets == 0) return map;
        var stretches = new Stretch[buckets];
        for (int i = 0; i < buckets; i++) stretches[i] = new Stretch();
        int[] total = new int[buckets];
        for (int i = 0; i < buckets; i++) total[i] = (int)((map.BucketEnd(i) - map.BucketStart(i) + BlockBytes - 1) / BlockBytes);

        long read = 0;
        long perPass = 1; // blocks per stretch to have read when the pass ends
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long wanted = perPass;
            long cost = 0;
            for (int i = 0; i < buckets; i++) cost += Math.Max(0, Math.Min(wanted, total[i]) - stretches[i].Done) * BlockBytes;
            if (cost == 0) break;
            if (read + cost > FullComputeLimit)
            {
                // the last pass only goes as far as the limit allows
                long affordable = Math.Max(0, FullComputeLimit - read) / ((long)BlockBytes * buckets);
                long next = stretches.Min(s => (long)s.Done) + affordable;
                if (affordable < 1 || next <= stretches.Min(s => (long)s.Done)) break;
                wanted = next;
            }
            var options = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Math.Max(1, maxParallelism ?? Environment.ProcessorCount) };
            long passRead = 0;
            Parallel.For(0, buckets, options, i =>
            {
                var s = stretches[i];
                int target = (int)Math.Min(wanted, total[i]);
                if (s.Done >= target) return;
                var buffer = new byte[BlockBytes];
                long start = map.BucketStart(i), end = map.BucketEnd(i);
                int bits = total[i] <= 1 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)(total[i] - 1));
                long mine = 0;
                while (s.Done < target)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    uint index = Reverse((uint)s.Sequence++, bits);
                    if (index >= total[i]) continue; // the sequence runs over a power of two; the blocks past the end do not exist
                    long at = start + (long)index * BlockBytes;
                    int count = (int)Math.Min(BlockBytes, end - at);
                    for (int filled = 0; filled < count;)
                    {
                        int n = snapshot.Read(at + filled, buffer.AsSpan(filled, count - filled));
                        if (n == 0) throw new EndOfStreamException("The snapshot ended before its declared length.");
                        filled += n;
                    }
                    foreach (byte b in buffer.AsSpan(0, count)) s.Counts[b]++;
                    s.Done++;
                    mine += count;
                }
                Summarise(s.Counts, out byte entropy, out var kind);
                map.Entropy[i] = entropy;
                map.Classes[i] = kind;
                map.Read[i] += mine;
                Interlocked.Add(ref passRead, mine);
            });
            read += passRead;
            published?.Invoke(map.Copy());
            if (wanted >= total.Max()) break;
            perPass = Math.Max(perPass + 1, perPass * 4);
        }
        return map;
    }

    /// <summary>The bits of <paramref name="value"/> in the lowest <paramref name="bits"/> places in reverse order, so 0, 1, 2, 3 visit the start, the middle, the quarters.</summary>
    private static uint Reverse(uint value, int bits)
    {
        uint result = 0;
        for (int i = 0; i < bits; i++)
        {
            result = (result << 1) | (value & 1);
            value >>= 1;
        }
        return result;
    }

    internal static void Summarise(ReadOnlySpan<long> counts, out byte entropy, out ByteClass kind)
    {
        long total = 0;
        var byClass = new long[5];
        for (int b = 0; b < 256; b++)
        {
            total += counts[b];
            byClass[(int)ByteClasses.Of((byte)b)] += counts[b];
        }
        double h = 0;
        if (total > 0)
            for (int b = 0; b < 256; b++)
            {
                if (counts[b] == 0) continue;
                double p = (double)counts[b] / total;
                h -= p * Math.Log2(p);
            }
        entropy = (byte)Math.Clamp((int)Math.Round(h / 8 * 255), 0, 255);
        int best = 0;
        for (int c = 1; c < byClass.Length; c++) if (byClass[c] > byClass[best]) best = c;
        kind = (ByteClass)best;
    }
}

/// <summary>The counts of the 256 byte values in a range, and what can be said from them.</summary>
public sealed class ByteHistogram
{
    private readonly long[] _counts;

    private ByteHistogram(long[] counts) => _counts = counts;

    /// <summary>How often each of the 256 byte values occurs.</summary>
    public IReadOnlyList<long> Counts => _counts;
    public long Total => _counts.Sum();
    public long CountOf(byte value) => _counts[value];
    /// <summary>How many of the 256 values occur at all.</summary>
    public int Distinct => _counts.Count(c => c > 0);

    /// <summary>Shannon entropy in bits per byte, 0 to 8.</summary>
    public double Entropy
    {
        get
        {
            long total = Total;
            if (total == 0) return 0;
            double h = 0;
            foreach (long c in _counts)
            {
                if (c == 0) continue;
                double p = (double)c / total;
                h -= p * Math.Log2(p);
            }
            return h;
        }
    }

    /// <summary>The <paramref name="n"/> most frequent values, the most frequent first, ties by value.</summary>
    public IReadOnlyList<(byte Value, long Count)> Top(int n) =>
        [.. Enumerable.Range(0, 256).Where(v => _counts[v] > 0).OrderByDescending(v => _counts[v]).ThenBy(v => v).Take(n).Select(v => ((byte)v, _counts[v]))];

    public long CountOf(ByteClass kind) => Enumerable.Range(0, 256).Where(v => ByteClasses.Of((byte)v) == kind).Sum(v => _counts[v]);

    /// <summary>Counts the bytes of <paramref name="range"/>; reads in blocks and may run on a worker thread.</summary>
    public static ByteHistogram Compute(IByteSnapshot snapshot, ByteRange range, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (range.End > snapshot.ByteLength) throw new ArgumentOutOfRangeException(nameof(range), "The range exceeds the snapshot.");
        var counts = new long[256];
        var buffer = new byte[1 << 20];
        for (long at = range.Start; at < range.End;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int n = snapshot.Read(at, buffer.AsSpan(0, (int)Math.Min(buffer.Length, range.End - at)));
            if (n == 0) throw new EndOfStreamException("The snapshot ended before its declared length.");
            foreach (byte b in buffer.AsSpan(0, n)) counts[b]++;
            at += n;
        }
        return new ByteHistogram(counts);
    }
}
