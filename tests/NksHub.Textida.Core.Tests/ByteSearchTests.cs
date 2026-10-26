using System.Diagnostics;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Hex;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class ByteSearchTests
{
    private static Doc Create(byte[] bytes) => Doc.FromBytes(bytes, new PagedBackendFactory(startIndexing: false),
        new DocumentOptions { Format = TextCodec.Cp1252 });

    [Theory]
    [InlineData("504b??04", "00504B0304504B090400", new long[] { 1, 5 })]
    [InlineData("4? ?b", "4A3B4FFF403B", new long[] { 0, 4 })]
    [InlineData("0A 00 0D", "0A000D000A000D", new long[] { 0, 4 })]
    [InlineData("AA AA", "AAAAAA", new long[] { 0, 1 })]
    [InlineData("?? ??", "000102", new long[] { 0, 1 })]
    public void Masks_and_arbitrary_bytes_match_across_every_block_size(string hex, string data, long[] expected)
    {
        using var doc = Create(Convert.FromHexString(data));
        var pattern = BytePattern.ParseHex(hex);
        for (int block = 1; block <= doc.ByteLength + 1; block++)
        {
            var result = ByteSearch.Find(doc.GetSnapshot(), pattern, new() { ChunkBytes = block });
            Assert.Equal(expected, result.Matches.Select(m => m.Start));
            Assert.All(result.Matches, m => Assert.Equal(pattern.Length, m.Length));
            Assert.False(result.LimitReached);
        }
    }

    [Fact]
    public void Modified_piece_boundaries_and_old_snapshots_keep_their_own_revision()
    {
        using var file = new TempFile([0x50, 0x4B, 0x11, 0x04, 0, 0x50, 0x04]);
        using var doc = Doc.Open(file.Path, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252 });
        var before = doc.GetSnapshot();
        Assert.True(doc.Insert(6, new byte[] { 0x4B, 0x22 }).Applied);
        Assert.True(doc.Replace(new ByteRange(0, 1), new byte[] { 0 }).Applied);
        var after = doc.GetSnapshot();
        var pattern = BytePattern.ParseHex("50 4B ?? 04");
        var oldResult = ByteSearch.Find(before, pattern, new() { ChunkBytes = 2 });
        var newResult = ByteSearch.Find(after, pattern, new() { ChunkBytes = 2 });
        Assert.Equal(new long[] { 0 }, oldResult.Matches.Select(m => m.Start));
        Assert.Equal(new long[] { 5 }, newResult.Matches.Select(m => m.Start));
        Assert.Equal(doc.Id, newResult.DocumentId);
        Assert.Equal(before.Revision, oldResult.Revision);
        Assert.Equal(after.Revision, newResult.Revision);
        Assert.NotEqual(oldResult.Revision, newResult.Revision);
        Assert.Equal(new byte[] { 0x50, 0x4B, 0x11, 0x04, 0, 0x50, 0x04 }, File.ReadAllBytes(file.Path));
    }

    [Fact]
    public void Random_masked_queries_match_an_independent_naive_scan_after_edits()
    {
        var random = new Random(7817);
        var model = Enumerable.Range(0, 3000).Select(_ => (byte)random.Next(0x40, 0x50)).ToList();
        using var doc = Create(model.ToArray());
        for (int step = 0; step < 60; step++)
        {
            int start = random.Next(model.Count), remove = random.Next(0, Math.Min(10, model.Count - start) + 1);
            byte[] insert = Enumerable.Range(0, random.Next(10)).Select(_ => (byte)random.Next(0x40, 0x50)).ToArray();
            Assert.True(doc.Replace(new ByteRange(start, start + remove), insert).Applied);
            model.RemoveRange(start, remove);
            model.InsertRange(start, insert);
            int length = random.Next(1, 14), source = random.Next(model.Count - length);
            byte[] masks = Enumerable.Range(0, length).Select(_ => new byte[] { 0, 15, 240, 255 }[random.Next(4)]).ToArray();
            byte[] values = Enumerable.Range(0, length).Select(i => (byte)(model[source + i] & masks[i])).ToArray();
            string hex = string.Concat(values.Select((v, i) =>
                (masks[i] >= 240 ? (v >> 4).ToString("X") : "?") + ((masks[i] & 15) != 0 ? (v & 15).ToString("X") : "?")));
            var expected = new List<long>();
            for (int at = 0; at <= model.Count - length; at++)
            {
                bool match = true;
                for (int i = 0; i < length; i++)
                    if ((model[at + i] & masks[i]) != values[i]) { match = false; break; }
                if (match) expected.Add(at);
            }
            var result = ByteSearch.Find(doc.GetSnapshot(), BytePattern.ParseHex(hex), new() { ChunkBytes = random.Next(1, 100) });
            Assert.Equal(expected, result.Matches.Select(m => m.Start));
        }
    }

    [Fact]
    public void Range_includes_only_complete_matches_and_limit_is_explicit()
    {
        using var doc = Create([1, 1, 1, 1, 1, 1]);
        var pattern = BytePattern.FromBytes([1, 1]);
        var result = ByteSearch.Find(doc.GetSnapshot(), pattern, new() { Range = new(1, 5), MaxResults = 2, ChunkBytes = 1 });
        Assert.Equal(new long[] { 1, 2 }, result.Matches.Select(m => m.Start));
        Assert.True(result.LimitReached);
        result = ByteSearch.Find(doc.GetSnapshot(), pattern, new() { Range = new(1, 5), MaxResults = 4 });
        Assert.Equal(new long[] { 1, 2, 3 }, result.Matches.Select(m => m.Start));
        Assert.False(result.LimitReached);
        Assert.Empty(ByteSearch.Find(doc.GetSnapshot(), pattern, new() { Range = new(2, 3) }).Matches);
        Assert.Empty(ByteSearch.Find(doc.GetSnapshot(), pattern, new() { Range = new(6, 6) }).Matches);
    }

    [Fact]
    public void Empty_input_and_invalid_options_are_handled_before_reading()
    {
        using var doc = Create([]);
        var pattern = BytePattern.ParseHex("00");
        Assert.Empty(ByteSearch.Find(doc.GetSnapshot(), pattern).Matches);
        Assert.Throws<ArgumentOutOfRangeException>(() => ByteSearch.Find(doc.GetSnapshot(), pattern, new() { MaxResults = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ByteSearch.Find(doc.GetSnapshot(), pattern, new() { ChunkBytes = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ByteSearch.Find(doc.GetSnapshot(), pattern, new() { Range = new(0, 1) }));
        Assert.Throws<OperationCanceledException>(() => ByteSearch.Find(doc.GetSnapshot(), pattern, cancellationToken: new(true)));
    }

    [Theory]
    [InlineData("??")]
    [InlineData("00")]
    public void Cancellation_after_read_interrupts_both_mask_and_anchor_scans(string pattern)
    {
        using var cancel = new CancellationTokenSource();
        var source = new ShortReads(new byte[100], 100, cancel.Cancel);
        var snapshot = new DocumentSnapshot(Guid.NewGuid(), 0, source, TextCodec.Cp1252);
        Assert.Throws<OperationCanceledException>(() => ByteSearch.Find(snapshot, BytePattern.ParseHex(pattern), cancellationToken: cancel.Token));
    }

    [Fact]
    public void Partial_reads_do_not_skip_bytes_or_cross_the_requested_range()
    {
        var source = new ShortReads([0, 1, 2, 3, 1, 2, 3, 0], 2);
        var snapshot = new DocumentSnapshot(Guid.NewGuid(), 42, source, TextCodec.Cp1252);
        var result = ByteSearch.Find(snapshot, BytePattern.ParseHex("01 02 03"), new() { ChunkBytes = 4, Range = new(1, 7) });
        Assert.Equal(new long[] { 1, 4 }, result.Matches.Select(m => m.Start));
        Assert.Equal(7, source.FurthestRead);
    }

    private sealed class ShortReads(byte[] bytes, int maxRead, Action? onRead = null) : IByteSnapshot
    {
        public long ByteLength => bytes.Length;
        public long FurthestRead { get; private set; }
        public int Read(long offset, Span<byte> destination)
        {
            int count = (int)Math.Min(Math.Min(maxRead, destination.Length), bytes.Length - offset);
            bytes.AsSpan((int)offset, count).CopyTo(destination);
            FurthestRead = Math.Max(FurthestRead, offset + count);
            onRead?.Invoke();
            return count;
        }
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void Ten_gibibyte_sparse_file_matches_a_full_naive_scan_before_and_after_editing()
    {
        const long length = 10L << 30;
        using var file = new TempFile();
        if (OperatingSystem.IsWindows())
        {
            var command = new ProcessStartInfo("fsutil") { CreateNoWindow = true, UseShellExecute = false };
            foreach (string arg in new[] { "sparse", "setflag", file.Path }) command.ArgumentList.Add(arg);
            using var process = Process.Start(command)!;
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }
        long[] positions = [0, (1 << 20) - 2, (4L << 30) + 1, length - 4];
        using (var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Write))
        {
            stream.SetLength(length);
            foreach (long offset in positions)
            {
                stream.Position = offset;
                stream.Write([0x50, 0x4B, 0x33, 0x04]);
            }
        }
        using var doc = Doc.Open(file.Path, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252 });
        var pattern = BytePattern.ParseHex("50 4B ?? 04");
        for (int pass = 0; pass < 2; pass++)
        {
            var snapshot = doc.GetSnapshot();
            Assert.Equal(length + (pass == 0 ? 0 : 4), snapshot.ByteLength);
            var expected = NaiveZipScan(snapshot);
            var actual = ByteSearch.Find(snapshot, pattern);
            Assert.Equal(expected, actual.Matches.Select(m => m.Start));
            Assert.False(actual.LimitReached);
            Assert.Equal(pass == 0 ? 4 : 5, expected.Count);
            if (pass == 0) Assert.True(doc.Insert((6L << 30) - 1, new byte[] { 0x50, 0x4B, 0x55, 0x04 }).Applied);
        }
    }

    private static List<long> NaiveZipScan(DocumentSnapshot snapshot)
    {
        var offsets = new List<long>();
        var buffer = new byte[1 << 20];
        uint window = 0;
        for (long offset = 0; offset < snapshot.ByteLength;)
        {
            int count = snapshot.Read(offset, buffer);
            Assert.True(count > 0);
            for (int i = 0; i < count; i++)
            {
                window = (window << 8) | buffer[i];
                if (offset + i >= 3 && (window & 0xFFFF00FF) == 0x504B0004) offsets.Add(offset + i - 3);
            }
            offset += count;
        }
        return offsets;
    }
}
