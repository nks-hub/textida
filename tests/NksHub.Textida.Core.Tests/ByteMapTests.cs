using System.Diagnostics;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex;

namespace NksHub.Textida.Core.Tests;

/// <summary>P5.6.4: the map of a file by entropy or byte class, sampled for a huge file and refined, and the histogram of a selection.</summary>
public class ByteMapTests
{
    private sealed class Memory(byte[] bytes) : IByteSnapshot
    {
        public long ByteLength => bytes.Length;
        public int Read(long offset, Span<byte> destination)
        {
            int n = (int)Math.Min(destination.Length, bytes.Length - offset);
            bytes.AsSpan((int)offset, n).CopyTo(destination);
            return n;
        }
    }

    /// <summary>A file of any length made on demand: zeros, except stretches of text, noise and 0xFF at chosen places; counts the bytes read.</summary>
    private sealed class Synthetic(long length, params (long Start, long End, char Kind)[] parts) : IByteSnapshot
    {
        private long _read;
        public long BytesRead => Interlocked.Read(ref _read);
        public long ByteLength => length;

        public int Read(long offset, Span<byte> destination)
        {
            int n = (int)Math.Min(destination.Length, length - offset);
            Interlocked.Add(ref _read, n);
            for (int i = 0; i < n; i++) destination[i] = At(offset + i);
            return n;
        }

        private byte At(long position)
        {
            foreach (var (start, end, kind) in parts)
            {
                if (position < start || position >= end) continue;
                return kind switch
                {
                    't' => (byte)"the quick brown fox jumps over the lazy dog\n"[(int)(position % 44)],
                    'n' => (byte)(unchecked((ulong)position * 0x9E3779B97F4A7C15UL) >> 56),
                    'f' => 0xFF,
                    _ => 0,
                };
            }
            return 0;
        }
    }

    [Fact]
    public void A_small_file_is_mapped_exactly_by_entropy_and_by_class()
    {
        var bytes = new byte[40_000];
        Array.Fill(bytes, (byte)0xFF, 10_000, 10_000);                                    // second quarter: 0xFF
        var text = "the quick brown fox jumps over the lazy dog\n"u8;
        for (int i = 20_000; i < 30_000; i++) bytes[i] = text[i % text.Length];            // third quarter: text
        new Random(3).NextBytes(bytes.AsSpan(30_000));                                     // last quarter: noise
        var map = ByteMapBuilder.Build(new Memory(bytes), 4);

        Assert.True(map.IsExact);
        Assert.Equal(1.0, map.Coverage);
        Assert.Equal([ByteClass.Zero, ByteClass.Ones, ByteClass.Text, ByteClass.High], map.Classes);
        Assert.Equal(0, map.Entropy[0]);
        Assert.Equal(0, map.Entropy[1]);
        Assert.InRange(map.Entropy[2], 120, 170); // text: about 4.7 bits of 8
        Assert.True(map.Entropy[3] > 245, $"noise entropy {map.Entropy[3]}");
        Assert.Equal([0L, 10_000L, 20_000L, 30_000L], Enumerable.Range(0, 4).Select(map.BucketStart));
        Assert.Equal(40_000, map.BucketEnd(3));
    }

    [Fact]
    public void Offsets_belong_to_the_stretch_that_contains_them()
    {
        var map = ByteMapBuilder.Build(new Memory(new byte[1000]), 7);
        for (long offset = 0; offset < 1000; offset += 13)
        {
            int bucket = map.BucketOf(offset);
            Assert.InRange(offset, map.BucketStart(bucket), map.BucketEnd(bucket) - 1);
        }
        Assert.Equal(6, map.BucketOf(999));
        Assert.Equal(0, map.BucketOf(0));
    }

    [Fact]
    public void Fewer_bytes_than_stretches_and_an_empty_file_are_handled()
    {
        Assert.Equal(3, ByteMapBuilder.Build(new Memory([1, 2, 3]), 100).Buckets);
        var empty = ByteMapBuilder.Build(new Memory([]), 50);
        Assert.Equal(0, empty.Buckets);
        Assert.True(empty.IsExact);
    }

    [Fact]
    public void Each_pass_publishes_a_better_map_and_the_last_one_is_exact()
    {
        var bytes = new byte[2_000_000];
        new Random(1).NextBytes(bytes);
        var seen = new List<double>();
        var map = ByteMapBuilder.Build(new Memory(bytes), 50, m => seen.Add(m.Coverage));

        Assert.True(seen.Count >= 3);
        Assert.Equal(seen.Order(), seen);
        Assert.True(seen[0] < 0.2, $"the first pass already read {seen[0]:P0}");
        Assert.Equal(1.0, seen[^1]);
        Assert.True(map.IsExact);
    }

    [Fact]
    public void Cancellation_stops_the_build()
    {
        using var cts = new CancellationTokenSource();
        var bytes = new byte[5_000_000];
        Assert.Throws<OperationCanceledException>(() => ByteMapBuilder.Build(new Memory(bytes), 50, _ => cts.Cancel(), cts.Token));
    }

    [Fact]
    public void The_histogram_counts_the_bytes_of_a_range_and_tells_its_entropy_and_top_values()
    {
        var bytes = new byte[1000];
        for (int i = 0; i < 1000; i++) bytes[i] = (byte)(i % 4 == 3 ? 0x41 : i % 2);
        var h = ByteHistogram.Compute(new Memory(bytes), new ByteRange(100, 900));

        Assert.Equal(800, h.Total);
        Assert.Equal(3, h.Distinct);
        Assert.Equal(200, h.CountOf(0x41));
        Assert.Equal(new[] { ((byte)0, 400L), ((byte)1, 200L), ((byte)0x41, 200L) }, h.Top(3));
        Assert.Equal(1.5, h.Entropy, 6);
        Assert.Equal(400, h.CountOf(ByteClass.Zero));
        Assert.Equal(200, h.CountOf(ByteClass.Text));
        Assert.Equal(200, h.CountOf(ByteClass.Control));
        Assert.Equal(0, ByteHistogram.Compute(new Memory(bytes), new ByteRange(5, 5)).Total);
        Assert.Throws<ArgumentOutOfRangeException>(() => ByteHistogram.Compute(new Memory(bytes), new ByteRange(0, 1001)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => ByteHistogram.Compute(new Memory(bytes), new ByteRange(0, 10), cts.Token));
    }

    [Fact]
    [Trait("Category", "Large")]
    public void A_fifty_gibibyte_file_is_sampled_in_the_first_pass_and_refined_within_the_reading_limit()
    {
        const long length = 50L << 30;
        long textAt = 20L << 30;
        var file = new Synthetic(length, (textAt, textAt + (1L << 30), 't'), (30L << 30, (30L << 30) + (512L << 20), 'n'), (length - (256L << 20), length, 'f'));
        const int buckets = 500;
        var published = new List<(double Coverage, long Read)>();
        var map = ByteMapBuilder.Build(file, buckets, m => published.Add((m.Coverage, file.BytesRead)));

        Assert.True(published[0].Read <= (long)buckets * ByteMapBuilder.BlockBytes, $"the first pass read {published[0].Read} bytes");
        Assert.True(published[0].Coverage < 1e-4);
        Assert.True(published.Count >= 3);
        Assert.True(file.BytesRead <= ByteMapBuilder.FullComputeLimit, $"read {file.BytesRead} bytes");
        Assert.False(map.IsExact);
        Assert.Equal(ByteClass.Text, map.Classes[map.BucketOf(textAt + (100L << 20))]);
        Assert.Equal(ByteClass.High, map.Classes[map.BucketOf((30L << 30) + (100L << 20))]);
        Assert.Equal(ByteClass.Ones, map.Classes[map.BucketOf(length - 100)]);
        Assert.Equal(ByteClass.Zero, map.Classes[map.BucketOf(1L << 30)]);
        Assert.True(map.Entropy[map.BucketOf((30L << 30) + (100L << 20))] > 245);
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void The_first_pass_of_a_fifty_gibibyte_file_is_ready_within_a_second()
    {
        const long length = 50L << 30;
        var file = new Synthetic(length);
        var clock = Stopwatch.StartNew();
        ByteMap? first = null;
        using var cts = new CancellationTokenSource();
        try { ByteMapBuilder.Build(file, 1000, m => { first = m; cts.Cancel(); }, cts.Token); }
        catch (OperationCanceledException) { }
        Assert.NotNull(first);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), $"the first pass took {clock.Elapsed.TotalSeconds:0.00} s");
    }
}
