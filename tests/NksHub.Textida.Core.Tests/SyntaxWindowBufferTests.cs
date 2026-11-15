using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Document.Contract;
using Xunit.Abstractions;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public sealed class SyntaxWindowBufferTests(ITestOutputHelper output)
{
    private const int FillBytes = SyntaxChunkReader.WindowBytes + 8;

    [Fact]
    public void Scanning_16_mib_of_valid_utf8_allocates_at_most_2_mib_after_warmup()
    {
        var bytes = new byte[16 << 20];
        long expected = 0;
        for (int i = 0; i < bytes.Length; i++) expected += bytes[i] = (byte)('a' + i % 23);
        using var document = Create(bytes, TextCodec.Utf8);
        var snapshot = document.GetSnapshot();
        var warm = new StreamingLineSource(snapshot, 0);
        foreach (var _ in warm.Ensure(FillBytes * 5L)) { }
        Scan(warm, 0, FillBytes);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var source = new StreamingLineSource(snapshot, 0);
        long actual = Scan(source, 0, bytes.Length);
        foreach (var _ in source.Ensure(bytes.Length)) { }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine($"utf8-bytes={bytes.Length}; allocated-bytes={allocated}; limit-bytes={2 << 20}");
        Assert.Equal(expected, actual);
        Assert.Equal((long)bytes.Length, source.Length);
        Assert.Equal(bytes.Length, source.NextByteOffset);
        Assert.True(source.IsDocumentEnd);
        Assert.InRange(allocated, 0, 2L << 20);
    }

    [Fact]
    public void Abandoning_a_loader_after_one_step_reuses_its_byte_buffer()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(new string('x', 8 << 20));
        using var document = Create(bytes, TextCodec.Utf8);
        var source = new StreamingLineSource(document.GetSnapshot(), 0);
        foreach (var _ in source.Ensure(FillBytes * 5L)) { }
        long target = bytes.Length - 1;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            using var pending = source.Ensure(target).GetEnumerator();
            if (!pending.MoveNext()) throw new InvalidOperationException("The distant window unexpectedly became cached.");
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine($"abandoned-loads=100; allocated-bytes={allocated}; limit-bytes={512 << 10}");
        Assert.InRange(allocated, 0, 512L << 10);
        foreach (var _ in source.Ensure(target)) { }
        Assert.Equal('x', source.At(target));
    }

    [Fact]
    public void Each_progress_step_keeps_the_original_logical_byte_window_size()
    {
        using var document = Create(Encoding.UTF8.GetBytes(new string('x', 4 * FillBytes)), TextCodec.Utf8);
        var source = new StreamingLineSource(document.GetSnapshot(), 0);
        using var pending = source.Ensure(3L * FillBytes).GetEnumerator();
        Assert.True(pending.MoveNext());
        Assert.Equal(FillBytes, source.BytesRead);
        Assert.Equal(FillBytes, source.WindowEnd(0));
        Assert.Equal('x', source.At(FillBytes - 1));
        Assert.False(source.TryReadCached(FillBytes, out _));
    }

    [Theory]
    [InlineData("UTF-8")]
    [InlineData("UTF-8 with BOM")]
    [InlineData("UTF-16 LE with BOM")]
    [InlineData("UTF-16 BE with BOM")]
    [InlineData("UTF-32 LE with BOM")]
    [InlineData("UTF-32 BE with BOM")]
    [InlineData("Windows-1250")]
    [InlineData("Shift-JIS")]
    public void Unicode_boundaries_line_endings_bom_and_eof_keep_text_and_byte_offsets(string codecName)
    {
        var codec = TextCodec.ByName(codecName)!;
        string symbol = codec.Kind == CodecKind.MultiByte ? "日" : codec.IsLossy ? "ž" : "😀ž";
        int width = codec.Alignment;
        string first = new string('a', FillBytes / width - 1) + symbol + new string('b', 5 * FillBytes);
        string second = new string('c', FillBytes / width - 1);
        string[] lines = [first, second, "", "tail", ""];
        string[] endings = ["\r\n", "\r\n", "\r", "\n", ""];
        byte[] body = codec.Encode(string.Concat(lines.Zip(endings, (line, ending) => line + ending)));
        byte[] bytes = [.. codec.Bom, .. body];
        using var document = Create(bytes, codec);
        Assert.Equal(codec.Bom.Length, document.BomLength);
        var snapshot = document.GetSnapshot();
        var legacy = new SyntaxChunkReader(snapshot, document.BomLength);
        long offset = document.BomLength;

        for (int line = 0; line < lines.Length; line++)
        {
            var source = new StreamingLineSource(snapshot, offset);
            Assert.Equal(lines[line], ReadLine(source));
            var legacyText = new StringBuilder();
            SyntaxChunk chunk;
            do { chunk = legacy.Read(); legacyText.Append(chunk.Text); } while (!chunk.LineEnd);
            Assert.Equal(lines[line], legacyText.ToString());
            offset += codec.Encode(lines[line] + endings[line]).Length;
            Assert.Equal(offset, source.NextByteOffset);
            Assert.Equal(offset, legacy.Offset);
            Assert.Equal(line == lines.Length - 1, source.IsDocumentEnd);
            Assert.Equal(source.IsDocumentEnd, chunk.DocumentEnd);
            Assert.Equal(-1, source.At(lines[line].Length));

            if (line == 0)
            {
                Assert.False(source.TryReadCached(0, out _));
                foreach (long at in new long[] { first.Length - 1, FillBytes * 3L, 1, FillBytes - 2L, 0 })
                {
                    foreach (var _ in source.Ensure(at)) { }
                    Assert.Equal(first[(int)at], source.At(at));
                    int count = (int)Math.Min(7, source.WindowEnd(at) - at);
                    Assert.Equal(first.Substring((int)at, count), source.SmallSlice(at, count));
                }
            }
        }
        Assert.Equal(bytes.Length, offset);
    }

    public static IEnumerable<object[]> InvalidCases()
    {
        yield return ["UTF-8", new byte[] { 0xF0, 0x28, 0x8C, 0x28, 0xFF }, new byte[] { 0xE2, 0x82 }];
        yield return ["UTF-16 LE", new byte[] { 0x00, 0xD8, 0x61, 0x00 }, new byte[] { 0x00, 0xDC, 0x61 }];
        yield return ["UTF-16 BE", new byte[] { 0xD8, 0x00, 0x00, 0x61 }, new byte[] { 0xDC, 0x00, 0x61 }];
        yield return ["UTF-32 LE", new byte[] { 0x00, 0x00, 0x11, 0x00 }, new byte[] { 0x00, 0xD8, 0x00, 0x00, 0x61 }];
        yield return ["UTF-32 BE", new byte[] { 0x00, 0x11, 0x00, 0x00 }, new byte[] { 0x00, 0x00, 0xD8, 0x00, 0x61 }];
        yield return ["Shift-JIS", new byte[] { 0x82, 0x20, 0xFF }, new byte[] { 0x82 }];
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Invalid_and_incomplete_units_keep_the_original_codec_fallback(string codecName, byte[] invalid, byte[] incomplete)
    {
        var codec = TextCodec.ByName(codecName)!;
        byte[] prefix = codec.Encode(new string('a', FillBytes / codec.Alignment - 1));
        byte[] bytes = [.. prefix, .. invalid, .. codec.Encode(new string('b', 5 * FillBytes)), .. incomplete];
        string expected = codec.GetString(bytes);
        using var document = Create(bytes, codec);
        var source = new StreamingLineSource(document.GetSnapshot(), 0);
        Assert.Equal(expected, ReadLine(source));
        Assert.Equal(bytes.Length, source.NextByteOffset);
        Assert.True(source.IsDocumentEnd);
        foreach (long at in new long[] { expected.Length - 1, FillBytes * 2L, 0 })
        {
            foreach (var _ in source.Ensure(at)) { }
            Assert.Equal(expected[(int)at], source.At(at));
        }
    }

    [Fact]
    public void Interleaved_cold_loaders_keep_their_own_offsets_across_yields_and_eviction()
    {
        string text = string.Concat(Enumerable.Range(0, 12).Select(i => new string((char)('a' + i), FillBytes)));
        using var document = Create(Encoding.UTF8.GetBytes(text), TextCodec.Utf8);
        var source = new StreamingLineSource(document.GetSnapshot(), 0);
        using var first = source.Ensure(4L * FillBytes + 17).GetEnumerator();
        Assert.True(first.MoveNext());
        foreach (var _ in source.Ensure(9L * FillBytes + 19)) { }
        Assert.Equal('j', source.At(9L * FillBytes + 19));
        while (first.MoveNext()) { }
        Assert.Equal('e', source.At(4L * FillBytes + 17));
        foreach (long at in new long[] { 11L * FillBytes, 0, 6L * FillBytes, FillBytes - 1L })
        {
            foreach (var _ in source.Ensure(at)) { }
            Assert.Equal(text[(int)at], source.At(at));
        }
    }

    private static long Scan(StreamingLineSource source, long from, long end)
    {
        long checksum = 0;
        while (from < end)
        {
            foreach (var _ in source.Ensure(from)) { }
            long next = Math.Min(end, source.WindowEnd(from));
            if (next <= from) throw new InvalidOperationException("The syntax window did not advance.");
            for (; from < next; from++) checksum += source.At(from);
        }
        return checksum;
    }

    private static string ReadLine(StreamingLineSource source)
    {
        var text = new StringBuilder();
        long at = 0;
        while (source.Length is null)
        {
            foreach (var _ in source.Ensure(at)) { }
            long end = source.WindowEnd(at);
            for (; at < end; at++) text.Append((char)source.At(at));
        }
        return text.ToString();
    }

    private static Doc Create(byte[] bytes, TextCodec codec) => Doc.FromBytes(bytes,
        codec.Kind == CodecKind.Utf8 ? new PagedBackendFactory(startIndexing: false) : ReferenceBackendFactory.Instance,
        new DocumentOptions { Format = codec });
}
