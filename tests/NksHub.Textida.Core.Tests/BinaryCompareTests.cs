using System.Diagnostics;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex;

namespace NksHub.Textida.Core.Tests;

/// <summary>P5.6.5: comparing two files byte by byte and with inserted and deleted bytes.</summary>
public class BinaryCompareTests
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

    private static byte[] Random(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static BinaryCompareResult Aligned(byte[] a, byte[] b) => BinaryCompare.Aligned(new Memory(a), new Memory(b));
    private static BinaryCompareResult Positional(byte[] a, byte[] b) => BinaryCompare.Positional(new Memory(a), new Memory(b));

    /// <summary>The left file with every difference replaced by what the right file has there; it has to be the right file.</summary>
    private static byte[] Rebuild(byte[] a, byte[] b, BinaryCompareResult r)
    {
        var output = new List<byte>(b.Length);
        long pos = 0;
        foreach (var d in r.Differences)
        {
            output.AddRange(a.AsSpan((int)pos, (int)(d.Left.Start - pos)).ToArray());
            output.AddRange(b.AsSpan((int)d.Right.Start, (int)d.Right.Length).ToArray());
            pos = d.Left.End;
        }
        output.AddRange(a.AsSpan((int)pos).ToArray());
        return [.. output];
    }

    [Fact]
    public void Equal_files_and_empty_files_have_no_difference()
    {
        var a = Random(10_000, 1);
        Assert.True(Aligned(a, [.. a]).AreEqual);
        Assert.True(Positional(a, [.. a]).AreEqual);
        Assert.True(Aligned([], []).AreEqual);
        Assert.True(Positional([], []).AreEqual);
    }

    [Fact]
    public void Overwritten_bytes_are_replaced_stretches_with_the_offsets_lengths_and_content_of_the_change()
    {
        var a = Random(100_000, 2);
        var b = (byte[])a.Clone();
        (long At, int Length)[] changes = [(10, 1), (5_000, 17), (70_000, 300), (99_999, 1)];
        foreach (var (at, length) in changes)
            for (int i = 0; i < length; i++) b[at + i] ^= 0xFF;

        foreach (var r in new[] { Positional(a, b), Aligned(a, b) })
        {
            Assert.Equal(changes.Length, r.Differences.Count);
            for (int k = 0; k < changes.Length; k++)
            {
                var d = r.Differences[k];
                Assert.Equal(DiffKind.Replace, d.Kind);
                Assert.Equal(new ByteRange(changes[k].At, changes[k].At + changes[k].Length), d.Left);
                Assert.Equal(d.Left, d.Right);
                Assert.Equal(a.AsSpan((int)d.Left.Start, (int)d.Left.Length).ToArray(), a[(int)d.Left.Start..(int)d.Left.End]);
                Assert.All(Enumerable.Range((int)d.Left.Start, (int)d.Left.Length), i => Assert.NotEqual(a[i], b[i]));
            }
            Assert.Equal(b, Rebuild(a, b, r));
        }
    }

    [Fact]
    public void An_inserted_block_is_an_insertion_when_aligned_and_a_shift_when_compared_byte_by_byte()
    {
        var a = Random(200_000, 3);
        var block = Random(4_000, 4);
        block[0] = (byte)(a[99_999] ^ 0xFF);
        block[^1] = (byte)(a[100_000] ^ 0xFF);
        var b = a.Take(100_000).Concat(block).Concat(a.Skip(100_000)).ToArray();

        var aligned = Aligned(a, b);
        var d = Assert.Single(aligned.Differences);
        Assert.Equal(DiffKind.Insert, d.Kind);
        Assert.Equal(new ByteRange(100_000, 100_000), d.Left);
        Assert.Equal(new ByteRange(100_000, 104_000), d.Right);
        Assert.Equal(b, Rebuild(a, b, aligned));

        // byte by byte, everything behind the insertion is a different byte except where two random bytes happen to be equal
        var positional = Positional(a, b);
        Assert.True(positional.Differences.Count > 100, $"{positional.Differences.Count} differences");
        Assert.True(positional.Differences[0].Left.Start >= 100_000);
        Assert.True(positional.LeftChanged > 0.95 * 100_000, $"{positional.LeftChanged} of 100000 bytes");
        Assert.Equal(b.Length, positional.Differences[^1].Right.End);
        Assert.Equal(b, Rebuild(a, b, positional));
    }

    [Fact]
    public void A_deleted_block_is_a_deletion_and_a_block_of_another_length_a_replacement()
    {
        var a = Random(150_000, 5);
        var deleted = a.Take(40_000).Concat(a.Skip(43_000)).ToArray();
        var d = Assert.Single(Aligned(a, deleted).Differences);
        Assert.Equal((DiffKind.Delete, new ByteRange(40_000, 43_000), new ByteRange(40_000, 40_000)), (d.Kind, d.Left, d.Right));

        var changed = a.Take(60_000).Concat(Random(70, 6)).Concat(a.Skip(60_050)).ToArray();
        var r = Aligned(a, changed);
        var replaced = Assert.Single(r.Differences);
        Assert.Equal((DiffKind.Replace, new ByteRange(60_000, 60_050), new ByteRange(60_000, 60_070)), (replaced.Kind, replaced.Left, replaced.Right));
        Assert.Equal(changed, Rebuild(a, changed, r));
    }

    [Fact]
    public void Appended_and_cut_off_bytes_are_an_insertion_and_a_deletion_at_the_end_in_both_modes()
    {
        var a = Random(5_000, 7);
        var longer = a.Concat(Random(300, 8)).ToArray();
        foreach (var r in new[] { Positional(a, longer), Aligned(a, longer) })
        {
            var d = Assert.Single(r.Differences);
            Assert.Equal((DiffKind.Insert, new ByteRange(5_000, 5_000), new ByteRange(5_000, 5_300)), (d.Kind, d.Left, d.Right));
        }
        foreach (var r in new[] { Positional(longer, a), Aligned(longer, a) })
        {
            var d = Assert.Single(r.Differences);
            Assert.Equal((DiffKind.Delete, new ByteRange(5_000, 5_300), new ByteRange(5_000, 5_000)), (d.Kind, d.Left, d.Right));
        }
        var changedAndLonger = (byte[])longer.Clone();
        for (int i = 4_990; i < 5_000; i++) changedAndLonger[i] ^= 0xFF;
        var merged = Assert.Single(Positional(a, changedAndLonger).Differences);
        Assert.Equal((DiffKind.Replace, new ByteRange(4_990, 5_000), new ByteRange(4_990, 5_300)), (merged.Kind, merged.Left, merged.Right));
    }

    [Fact]
    public void Several_kinds_of_edit_in_one_file_are_all_found_and_rebuild_the_file()
    {
        var a = Random(600_000, 9);
        var b = new List<byte>(a);
        b.RemoveRange(500_000, 2_000);                                                       // delete
        b.InsertRange(300_000, Random(5_000, 10).Select((x, i) => i == 0 ? (byte)(a[299_999] ^ 1) : i == 4_999 ? (byte)(a[300_000] ^ 1) : x)); // insert
        for (int i = 0; i < 40; i++) b[100_000 + i] ^= 0x5A;                                 // overwrite
        var result = Aligned(a, [.. b]);

        Assert.Equal([DiffKind.Replace, DiffKind.Insert, DiffKind.Delete], result.Differences.Select(d => d.Kind));
        Assert.Equal(new ByteRange(100_000, 100_040), result.Differences[0].Left);
        Assert.Equal(new ByteRange(300_000, 300_000), result.Differences[1].Left);
        Assert.Equal(5_000, result.Differences[1].Right.Length);
        Assert.Equal(new ByteRange(500_000, 502_000), result.Differences[2].Left);
        Assert.Equal(b, Rebuild(a, [.. b], result));
    }

    [Fact]
    public void Random_edits_always_give_differences_that_turn_the_left_file_into_the_right_one()
    {
        var random = new Random(77);
        for (int trial = 0; trial < 300; trial++)
        {
            var a = Random(random.Next(0, 4_000), trial);
            var b = new List<byte>(a);
            for (int e = random.Next(0, 6); e > 0; e--)
            {
                int at = b.Count == 0 ? 0 : random.Next(0, b.Count + 1);
                switch (random.Next(3))
                {
                    case 0: b.InsertRange(at, Random(random.Next(1, 200), random.Next())); break;
                    case 1: b.RemoveRange(at, Math.Min(random.Next(1, 200), b.Count - at)); break;
                    default: if (at < b.Count) b[at] ^= 0xFF; break;
                }
            }
            var target = b.ToArray();
            Assert.Equal(target, Rebuild(a, target, Aligned(a, target)));
            Assert.Equal(target, Rebuild(a, target, Positional(a, target)));
        }
    }

    [Fact]
    public void A_place_in_one_file_maps_to_the_place_in_the_other()
    {
        var a = Random(100_000, 11);
        var inserted = Random(1_000, 12);
        inserted[0] = (byte)(a[49_999] ^ 1);
        inserted[^1] = (byte)(a[50_000] ^ 1);
        var b = a.Take(50_000).Concat(inserted).Concat(a.Skip(50_000)).ToArray();
        var r = Aligned(a, b);

        Assert.Equal(10, r.MapLeftToRight(10));
        Assert.Equal(49_999, r.MapLeftToRight(49_999));
        Assert.Equal(51_000, r.MapLeftToRight(50_000));
        Assert.Equal(90_000 + 1_000, r.MapLeftToRight(90_000));
        Assert.Equal(90_000, r.MapRightToLeft(91_000));
        Assert.Equal(50_000, r.MapRightToLeft(50_500)); // inside the insertion: its place in the left file
        Assert.Equal(50_000, r.MapRightToLeft(51_000));
        foreach (long offset in new long[] { 0, 1, 49_998, 50_000, 70_000, 99_999 })
            Assert.Equal(a[offset], b[r.MapLeftToRight(offset)]);
    }

    [Fact]
    public void The_list_stops_at_the_limit_and_says_so()
    {
        var a = new byte[10_000];
        var b = new byte[10_000];
        for (int i = 0; i < b.Length; i += 2) b[i] = 1;
        var limited = BinaryCompare.Positional(new Memory(a), new Memory(b), new() { MaxDifferences = 7 });
        Assert.True(limited.Truncated);
        Assert.Equal(7, limited.Differences.Count);
        Assert.False(Positional(a, b).Truncated);
        Assert.Equal(5_000, Positional(a, b).Differences.Count);
    }

    [Fact]
    public void Files_over_the_align_limit_are_compared_byte_by_byte_and_cancellation_stops_a_comparison()
    {
        var a = Random(50_000, 13);
        var b = a.Take(100).Concat(new byte[] { 1, 2, 3 }).Concat(a.Skip(100)).ToArray();
        Assert.False(BinaryCompare.Compare(new Memory(a), new Memory(b), new() { AlignLimit = 1_000 }).Aligned);
        Assert.True(BinaryCompare.Compare(new Memory(a), new Memory(b)).Aligned);
        Assert.Throws<NotSupportedException>(() => BinaryCompare.Aligned(new Memory(a), new Memory(b), new() { AlignLimit = 1_000 }));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => { BinaryCompare.Positional(new Memory(new byte[5_000_000]), new Memory(new byte[5_000_000]), null, cts.Token); });
    }

    [Fact]
    public void A_file_on_disk_is_read_like_any_other_source()
    {
        using var file = new TempFile(Random(30_000, 14));
        using var disk = new FileBytes(file.Path);
        Assert.Equal(30_000, disk.ByteLength);
        var other = Random(30_000, 14);
        other[12_345] ^= 0xFF;
        var r = BinaryCompare.Compare(disk, new Memory(other));
        Assert.Equal(new ByteRange(12_345, 12_346), Assert.Single(r.Differences).Left);
        var tail = new byte[10];
        Assert.Equal(5, disk.Read(29_995, tail));
        Assert.Equal(0, disk.Read(30_000, tail));
    }

    // ---- big files ----

    private static void MakeSparse(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        var command = new ProcessStartInfo("fsutil") { CreateNoWindow = true, UseShellExecute = false };
        foreach (string arg in new[] { "sparse", "setflag", path }) command.ArgumentList.Add(arg);
        using var process = Process.Start(command)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    [Trait("Category", "Large")]
    public void Two_1_gibibyte_files_with_100_isolated_changes_give_exactly_those_100_ranges()
    {
        const long length = 1L << 30;
        using var left = new TempFile();
        using var right = new TempFile();
        MakeSparse(left.Path);
        MakeSparse(right.Path);
        var planned = new List<(long At, int Length)>();
        var generator = new Random(2026);
        long cursor = 1_000;
        for (int i = 0; i < 100; i++)
        {
            int changeLength = 1 + generator.Next(0, 5_000);
            planned.Add((cursor, changeLength));
            cursor += changeLength + 1 + generator.Next(1_000, 10_000_000); // a gap of at least one equal byte keeps the changes apart
        }
        Assert.True(cursor < length);
        foreach (string path in new[] { left.Path, right.Path })
            using (var s = new FileStream(path, FileMode.Open, FileAccess.Write)) s.SetLength(length);
        // the left file holds a marker byte pattern in each planned range; the right file holds its complement there
        using (var l = new FileStream(left.Path, FileMode.Open, FileAccess.Write))
        using (var r = new FileStream(right.Path, FileMode.Open, FileAccess.Write))
            foreach (var (at, changeLength) in planned)
            {
                var original = new byte[changeLength];
                new Random((int)(at % int.MaxValue)).NextBytes(original);
                var changed = original.Select(x => (byte)~x).ToArray();
                l.Position = at; l.Write(original);
                r.Position = at; r.Write(changed);
            }

        using var a = new FileBytes(left.Path);
        using var b = new FileBytes(right.Path);
        var result = BinaryCompare.Positional(a, b);

        Assert.False(result.Truncated);
        Assert.Equal(100, result.Differences.Count);
        for (int k = 0; k < 100; k++)
        {
            var d = result.Differences[k];
            Assert.Equal(DiffKind.Replace, d.Kind);
            Assert.Equal(new ByteRange(planned[k].At, planned[k].At + planned[k].Length), d.Left);
            Assert.Equal(d.Left, d.Right);
            var original = new byte[planned[k].Length];
            new Random((int)(planned[k].At % int.MaxValue)).NextBytes(original);
            var seenLeft = new byte[d.Left.Length];
            var seenRight = new byte[d.Right.Length];
            Assert.Equal(seenLeft.Length, a.Read(d.Left.Start, seenLeft));
            Assert.Equal(seenRight.Length, b.Read(d.Right.Start, seenRight));
            Assert.Equal(original, seenLeft);
            Assert.Equal(original.Select(x => (byte)~x).ToArray(), seenRight);
        }
    }

    [Fact]
    [Trait("Category", "Large")]
    public void A_100_mebibyte_file_with_inserted_blocks_shows_the_insertions_and_not_a_shift()
    {
        const int length = 100 << 20;
        var a = Random(length, 15);
        var inserts = new (int At, int Size)[] { (1_000_000, 4_096), (20_000_000, 100_000), (20_000_500, 17), (60_000_000, 1_000_000), (99_999_000, 333) };
        var b = new List<byte>(length + 1_200_000);
        int from = 0;
        var generator = new Random(16);
        foreach (var (at, size) in inserts)
        {
            b.AddRange(new ArraySegment<byte>(a, from, at - from));
            var block = new byte[size];
            generator.NextBytes(block);
            block[0] = (byte)(a[at - 1] ^ 0xFF);
            block[^1] = (byte)(a[at] ^ 0xFF);
            b.AddRange(block);
            from = at;
        }
        b.AddRange(new ArraySegment<byte>(a, from, length - from));
        var target = b.ToArray();

        var result = BinaryCompare.Compare(new Memory(a), new Memory(target));

        Assert.True(result.Aligned);
        Assert.All(result.Differences, d => Assert.Equal(DiffKind.Insert, d.Kind));
        Assert.Equal(inserts.Select(i => (long)i.At), result.Differences.Select(d => d.Left.Start));
        Assert.Equal(inserts.Select(i => (long)i.Size), result.Differences.Select(d => d.Right.Length));
        long shift = 0;
        foreach (var (d, insert) in result.Differences.Zip(inserts))
        {
            Assert.Equal(insert.At + shift, d.Right.Start);
            shift += insert.Size;
        }
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void The_aligned_comparison_of_a_256_mebibyte_file_takes_seconds_not_minutes()
    {
        var a = Random(256 << 20, 17);
        var b = new byte[a.Length];
        Array.Copy(a, b, a.Length);
        for (int i = 10_000_000; i < 10_000_064; i++) b[i] ^= 1;
        var clock = Stopwatch.StartNew();
        var result = BinaryCompare.Aligned(new Memory(a), new Memory(b));
        Assert.Single(result.Differences);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"took {clock.Elapsed.TotalSeconds:0.0} s");
    }
}
