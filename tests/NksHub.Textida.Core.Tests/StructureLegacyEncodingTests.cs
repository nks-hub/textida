using System.Text;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Tests;

public sealed class StructureLegacyEncodingTests
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
        foreach (var rune in tail.EnumerateRunes())
        {
            Assert.Equal(offset, reader.Position);
            Assert.Equal(rune.Value, reader.Peek());
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

    private static TextCodec Codec(string name) => TextCodec.All.Single(codec => codec.Name == name);

    private static void CheckUnit(TextCodec codec, ReadOnlySpan<byte> bytes)
    {
        int expectedBytes = codec.ReadUnit(bytes, out _);
        string text = codec.GetString(bytes[..expectedBytes]);
        int expected = Rune.TryGetRuneAt(text, 0, out var scalar) ? scalar.Value : -1;
        int actual = codec.ReadLegacyScalar(bytes, out int actualBytes);
        if (expected != actual || expectedBytes != actualBytes)
            Assert.Fail($"{codec.Name} {Convert.ToHexString(bytes)}: expected {expected}/{expectedBytes}, got {actual}/{actualBytes}.");
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
