using System.Text;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Xunit.Abstractions;

namespace NksHub.Textida.Core.Tests;

public sealed class StructureLegacyEncodingTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> LegacyCodecs => TextCodec.All
        .Where(codec => codec.Kind is CodecKind.SingleByte or CodecKind.MultiByte)
        .Select(codec => new object[] { codec.Name });

    [Theory]
    [MemberData(nameof(LegacyCodecs))]
    public void LegacyScalarMatchesTheExistingCodecUnits(string name)
    {
        var codec = Codec(name);
        Span<byte> bytes = stackalloc byte[4];
        CheckUnit(codec, []);
        for (int first = 0; first < 256; first++)
        {
            bytes[0] = (byte)first;
            CheckUnit(codec, bytes[..1]);
            if (codec.Kind != CodecKind.MultiByte) continue;
            for (int second = 0; second < 256; second++)
            {
                bytes[1] = (byte)second;
                CheckUnit(codec, bytes[..2]);
            }
        }

        byte[][] longerUnits =
        [
            [0x8F, 0xA2, 0xAF], [0x8F, 0xFE, 0xFE],
            [0x81, 0x30, 0x81, 0x30], [0x90, 0x30, 0x81, 0x30],
            [0xE3, 0x32, 0x9A, 0x35], [0xFE, 0x39, 0xFE, 0x39],
            [0x81, 0x30, 0x20, 0x30], [0x81, 0x30, 0x81, 0x20],
        ];
        foreach (byte[] unit in longerUnits)
            for (int length = 1; length <= unit.Length; length++)
                CheckUnit(codec, unit.AsSpan(0, length));
    }

    [Theory]
    [MemberData(nameof(LegacyCodecs))]
    public void LegacyReaderBlocksMatchTheOriginalUnitsAndByteOffsets(string name)
    {
        var codec = Codec(name);
        string sample = codec.CodePage switch
        {
            932 or 51932 => "あ漢ｶx",
            936 => "汉字x",
            54936 => "汉字𠀀𝄞Žx",
            950 => "漢字x",
            51949 => "한글x",
            _ => "x",
        };
        byte[] pattern = codec.Encode(sample);
        byte[] bytes = new byte[64 * 1024 + 37];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = pattern[i % pattern.Length];
        byte[] allBytes = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        allBytes.CopyTo(bytes, 8190);
        byte[] malformed = [0x81, 0x30, 0x20, 0x30, 0xFF, 0x8F, 0xA2];
        malformed.CopyTo(bytes, 64 * 1024 - 3);
        var reader = new StructureCharReader(new ArraySnapshot(bytes), codec, 0, CancellationToken.None);
        int offset = 0;
        while (offset < bytes.Length)
        {
            int expected = OriginalScalar(codec, bytes.AsSpan(offset), out int used);
            Assert.Equal(offset, reader.Position);
            Assert.Equal(expected, reader.Peek());
            Assert.Equal(expected, reader.Read());
            offset += used;
        }
        Assert.Equal(bytes.Length, reader.Position);
        Assert.Equal(-1, reader.Read());
        for (int at = 8188; at < 8208; at++)
        {
            reader.Seek(at);
            int expected = OriginalScalar(codec, bytes.AsSpan(at), out int used);
            Assert.Equal(expected, reader.Read());
            Assert.Equal(at + used, reader.Position);
        }
    }

    [Theory]
    [InlineData("Windows-1250", StructureFormat.Json)]
    [InlineData("GB18030", StructureFormat.Json)]
    [InlineData("Windows-1250", StructureFormat.Xml)]
    [InlineData("GB18030", StructureFormat.Xml)]
    public void LongLegacyTokensDoNotAllocatePerCharacter(string name, StructureFormat format)
    {
        var codec = Codec(name);
        byte[] pattern = codec.Encode(name == "GB18030" ? "汉字𠀀" : "Žluťoučký");
        var small = Source(codec, format, pattern, 64 * 1024);
        var large = Source(codec, format, pattern, 8 * 1024 * 1024);
        Parse(small, codec, format);
        long smallAllocated = Parse(small, codec, format);
        long largeAllocated = Parse(large, codec, format);
        output.WriteLine($"{name} {format}: small-bytes={small.ByteLength} small-allocated={smallAllocated} " +
            $"large-bytes={large.ByteLength} large-allocated={largeAllocated}");
        Assert.True(largeAllocated <= smallAllocated + 256 * 1024,
            $"{name} {format}: 64 KiB allocated {smallAllocated} bytes; 8 MiB allocated {largeAllocated} bytes.");
    }

    [Theory]
    [InlineData("Windows-1250")]
    [InlineData("GB18030")]
    public void LegacyScalarsKeepByteOffsetsAcrossReadWindowsAndSeeks(string name)
    {
        var codec = Codec(name);
        const int padding = 64 * 1024 - 2;
        string tail = name == "GB18030" ? "𠀀汉字Ž" : "Žluťoučký";
        byte[] bytes = codec.Encode(new string('x', padding) + tail);
        var reader = new StructureCharReader(new ArraySnapshot(bytes), codec, 0, CancellationToken.None);
        for (int i = 0; i < padding; i++) Assert.Equal('x', reader.Read());
        long offset = padding;
        Rune[] runes = tail.EnumerateRunes().ToArray();
        for (int i = 0; i < runes.Length; i++)
        {
            var rune = runes[i];
            Assert.Equal(offset, reader.Position);
            Assert.Equal(rune.Value, reader.Peek());
            Assert.Equal(i + 1 < runes.Length ? runes[i + 1].Value : -1, reader.Peek(1));
            Assert.Equal(offset, reader.Position);
            Assert.Equal(rune.Value, reader.Read());
            offset += codec.Encode(rune.ToString()).Length;
            Assert.Equal(offset, reader.Position);
        }
        Assert.Equal(bytes.Length, reader.Position);
        Assert.Equal(-1, reader.Read());
        reader.Seek(padding);
        Assert.Equal(tail.EnumerateRunes().First().Value, reader.Read());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Gb18030ScalarsSurviveOneToThreeByteSourceReads(int readSize)
    {
        var codec = Codec("GB18030");
        const string text = "a𠀀汉字Žz";
        byte[] bytes = codec.Encode(text);
        var source = new ShortSnapshot(bytes, readSize);
        var reader = new StructureCharReader(source, codec, 0, CancellationToken.None);
        long offset = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            Assert.Equal(offset, reader.Position);
            Assert.Equal(rune.Value, reader.Peek());
            Assert.Equal(rune.Value, reader.Read());
            offset += codec.Encode(rune.ToString()).Length;
        }
        Assert.Equal(bytes.Length, reader.Position);
        Assert.Equal(-1, reader.Read());
        Assert.True(source.ReadCalls > 1);
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(2, 6)]
    [InlineData(3, 6)]
    [InlineData(64 * 1024, 64 * 1024 - 2)]
    public void MalformedBlockDoesNotKeepAReplacementForAnIncompleteSourceUnit(int readSize, int splitAt)
    {
        var codec = Codec("GB18030");
        byte[] bytes = new byte[splitAt + 20];
        Array.Fill(bytes, (byte)'a');
        bytes[Math.Max(0, splitAt - 8190)] = 0xFF;
        codec.Encode("𐀀").CopyTo(bytes, splitAt);
        bytes[^1] = 0x81;
        var reader = new StructureCharReader(new ShortSnapshot(bytes, readSize), codec, 0, CancellationToken.None);
        int offset = 0;
        while (offset < bytes.Length)
        {
            int expected = OriginalScalar(codec, bytes.AsSpan(offset), out int used);
            Assert.Equal(expected, reader.Read());
            offset += used;
            Assert.Equal(offset, reader.Position);
        }
        Assert.Equal(-1, reader.Read());
    }

    [Theory]
    [InlineData("Windows-1250", StructureFormat.Json)]
    [InlineData("GB18030", StructureFormat.Json)]
    [InlineData("Windows-1250", StructureFormat.Xml)]
    [InlineData("GB18030", StructureFormat.Xml)]
    public void LegacyTokensStillObserveCancellationBeforeTheirEnd(string name, StructureFormat format)
    {
        var codec = Codec(name);
        using var cancellation = new CancellationTokenSource();
        byte[] pattern = codec.Encode(name == "GB18030" ? "汉𠀀" : "Žluť");
        var source = Source(codec, format, pattern, 8 * 1024 * 1024, cancellation);
        var context = new StructureParseContext(source, codec, 0, new Sink(), cancellation.Token);
        Assert.Throws<OperationCanceledException>(() => Parse(format, context));
        Assert.InRange(source.BytesRead, 128 * 1024, 192 * 1024);
        Assert.True(source.BytesRead < source.ByteLength);
    }

    [Fact]
    public void MalformedGb18030UnitsKeepTheirReplacementAndByteAdvance()
    {
        var codec = Codec("GB18030");
        byte[] bytes = [0x81, 0x30, 0x20, 0x30, 0xFF, 0x81];
        var reader = new StructureCharReader(new ArraySnapshot(bytes), codec, 0, CancellationToken.None);
        int offset = 0;
        while (offset < bytes.Length)
        {
            int length = codec.ReadUnit(bytes.AsSpan(offset), out _);
            string text = codec.GetString(bytes.AsSpan(offset, length));
            Assert.True(Rune.TryGetRuneAt(text, 0, out var expected));
            Assert.Equal(expected.Value, reader.Read());
            offset += length;
            Assert.Equal(offset, reader.Position);
        }
    }

    [Fact]
    public void MalformedLegacyBlockChecksCancellationInsideItsFallback()
    {
        var codec = Codec("GB18030");
        byte[] bytes = new byte[8192];
        Array.Fill(bytes, (byte)0xFF);
        var scalars = new int[8192];
        var byteEnds = new ushort[8192];
        var chars = new char[8192];
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            codec.ReadLegacyBlock(bytes, scalars, byteEnds, chars, cancellation.Token));
    }

    private static TextCodec Codec(string name) => TextCodec.All.Single(codec => codec.Name == name);

    private static void CheckUnit(TextCodec codec, ReadOnlySpan<byte> bytes)
    {
        int expected = OriginalScalar(codec, bytes, out int expectedBytes);
        int actual = codec.ReadLegacyScalar(bytes, out int actualBytes);
        if (expected != actual || expectedBytes != actualBytes)
            Assert.Fail($"{codec.Name} {Convert.ToHexString(bytes)}: expected {expected}/{expectedBytes}, got {actual}/{actualBytes}.");
    }

    private static int OriginalScalar(TextCodec codec, ReadOnlySpan<byte> bytes, out int used)
    {
        used = codec.ReadUnit(bytes, out _);
        string text = codec.GetString(bytes[..used]);
        return text.Length != 0 && Rune.TryGetRuneAt(text, 0, out var scalar) ? scalar.Value : -1;
    }

    private static RepeatedSnapshot Source(TextCodec codec, StructureFormat format, byte[] pattern, long size,
        CancellationTokenSource? cancellation = null) => new(
            codec.Encode(format == StructureFormat.Xml ? "<root>" : "{\"value\":\""), pattern,
            Math.Max(1, size / pattern.Length),
            codec.Encode(format == StructureFormat.Xml ? "</root>" : "\"}"), cancellation);

    private static long Parse(RepeatedSnapshot source, TextCodec codec, StructureFormat format)
    {
        var sink = new Sink();
        var context = new StructureParseContext(source, codec, 0, sink, CancellationToken.None);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Parse(format, context);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(3, sink.Nodes.Count);
        Assert.Equal(source.ByteLength, sink.Nodes[0].End);
        var value = sink.Nodes[^1];
        Assert.Equal(source.PrefixLength - (format == StructureFormat.Json ? 1 : 0), value.ValueStart);
        Assert.Equal(source.ContentEnd + (format == StructureFormat.Json ? 1 : 0), value.ValueEnd);
        return allocated;
    }

    private static void Parse(StructureFormat format, StructureParseContext context)
    {
        if (format == StructureFormat.Xml) XmlStructureReader.Parse(context);
        else JsonStructureReader.Parse(context);
    }

    private sealed class RepeatedSnapshot(byte[] prefix, byte[] pattern, long repeats, byte[] suffix,
        CancellationTokenSource? cancellation) : IByteSnapshot
    {
        public long PrefixLength => prefix.Length;
        public long ContentEnd => prefix.Length + repeats * pattern.Length;
        public long ByteLength => ContentEnd + suffix.Length;
        public long BytesRead { get; private set; }

        public int Read(long offset, Span<byte> destination)
        {
            int count = (int)Math.Min(destination.Length, ByteLength - offset);
            int done = 0;
            while (done < count)
            {
                long at = offset + done;
                ReadOnlySpan<byte> part = at < prefix.Length ? prefix.AsSpan((int)at)
                    : at < ContentEnd ? pattern.AsSpan((int)((at - prefix.Length) % pattern.Length))
                    : suffix.AsSpan((int)(at - ContentEnd));
                int take = Math.Min(part.Length, count - done);
                part[..take].CopyTo(destination[done..]);
                done += take;
            }
            BytesRead += done;
            if (BytesRead >= 128 * 1024) cancellation?.Cancel();
            return done;
        }
    }

    private sealed class ShortSnapshot(byte[] bytes, int readSize) : IByteSnapshot
    {
        public long ByteLength => bytes.Length;
        public int ReadCalls { get; private set; }

        public int Read(long offset, Span<byte> destination)
        {
            int count = (int)Math.Min(Math.Min(destination.Length, readSize), ByteLength - offset);
            bytes.AsSpan((int)offset, count).CopyTo(destination);
            ReadCalls++;
            return count;
        }
    }

    private sealed class Sink : IStructureSink
    {
        public List<StructureNode> Nodes { get; } = [];

        public long AddNode(StructureKind kind, long parentId, long start, long nameStart, long nameEnd,
            long valueStart, long ordinal, int depth)
        {
            long id = Nodes.Count;
            Nodes.Add(new(id, parentId, kind, start, start, nameStart, nameEnd, valueStart, valueStart, ordinal, 0, id, depth));
            return id;
        }

        public void CompleteNode(long id, long valueStart, long valueEnd, long end, long childCount) =>
            Nodes[(int)id] = Nodes[(int)id] with
            {
                ValueStart = valueStart, ValueEnd = valueEnd, End = end,
                ChildCount = childCount, LastDescendantId = Nodes.Count - 1,
            };

        public void SetNamespace(long id, long namespaceId) => Nodes[(int)id] = Nodes[(int)id] with { NamespaceId = namespaceId };
        public void Report(StructureIssue issue) => Assert.Fail(issue.Message);
    }
}
