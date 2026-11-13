using System.Text;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Tests;

public sealed class StructureJsonTests
{
    private sealed class Sink : IStructureSink
    {
        public List<StructureNode> Nodes { get; } = [];

        public long AddNode(StructureKind kind, long parentId, long start, long nameStart, long nameEnd, long valueStart, long ordinal, int depth)
        {
            long id = Nodes.Count;
            Nodes.Add(new(id, parentId, kind, start, -1, nameStart, nameEnd, valueStart, -1, ordinal, -1, -1, depth));
            return id;
        }

        public void CompleteNode(long id, long valueStart, long valueEnd, long end, long childCount)
        {
            Nodes[checked((int)id)] = Nodes[checked((int)id)] with
            {
                ValueStart = valueStart, ValueEnd = valueEnd, End = end, ChildCount = childCount, LastDescendantId = Nodes.Count - 1,
            };
        }

        public void SetNamespace(long id, long namespaceId) => throw new InvalidOperationException("JSON has no namespace bindings.");
        public void Report(StructureIssue issue) => throw new InvalidOperationException("JSON syntax failures must be thrown.");
    }

    private static TextCodec Codec(string name) => name switch
    {
        "utf8" => TextCodec.Utf8,
        "utf8-bom" => TextCodec.Utf8Bom,
        "utf16-le" => TextCodec.Utf16LEBom,
        "utf16-be" => TextCodec.Utf16BEBom,
        "utf32-le" => TextCodec.Utf32LEBom,
        "utf32-be" => TextCodec.Utf32BEBom,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static (Sink Sink, byte[] Bytes) Parse(string text, TextCodec? codec = null, bool jsonc = false)
    {
        codec ??= TextCodec.Utf8;
        byte[] bytes = [.. codec.Bom, .. codec.Encode(text)];
        var sink = new Sink();
        JsonStructureReader.Parse(new(new ArraySnapshot(bytes), codec, codec.Bom.Length, sink, CancellationToken.None), jsonc);
        return (sink, bytes);
    }

    private static string Slice(byte[] bytes, TextCodec codec, long start, long end) => codec.GetString(bytes.AsSpan(checked((int)start), checked((int)(end - start))));

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8-bom")]
    [InlineData("utf16-le")]
    [InlineData("utf16-be")]
    [InlineData("utf32-le")]
    [InlineData("utf32-be")]
    public void UnicodeMembersKeepAbsoluteByteSpans(string encoding)
    {
        const string text = " {\"město\":\"A😀\\n\",\"list\":[true,null,-12.5e+3]} \n";
        var codec = Codec(encoding);
        var (sink, bytes) = Parse(text, codec);
        long At(int character) => codec.Bom.Length + codec.Encode(text[..character]).Length;
        var document = sink.Nodes[0];
        var root = sink.Nodes[1];
        var member = sink.Nodes[2];
        var array = sink.Nodes[3];

        Assert.Equal(7, sink.Nodes.Count);
        Assert.Equal(StructureKind.Document, document.Kind);
        Assert.Equal(-1L, document.ParentId);
        Assert.Equal(0L, document.Start);
        Assert.Equal(bytes.LongLength, document.End);
        Assert.Equal(1L, document.ChildCount);
        Assert.Equal(0, document.Depth);
        Assert.Equal(StructureKind.JsonObject, root.Kind);
        Assert.Equal(At(text.IndexOf('{')), root.Start);
        Assert.Equal(At(text.LastIndexOf('}') + 1), root.End);
        Assert.Equal(2L, root.ChildCount);
        Assert.Equal(document.Id, root.ParentId);
        Assert.Equal(1, root.Depth);

        Assert.Equal(At(text.IndexOf("\"město\"", StringComparison.Ordinal)), member.Start);
        Assert.Equal(member.NameStart, member.Start);
        Assert.Equal("\"město\"", Slice(bytes, codec, member.NameStart, member.NameEnd));
        Assert.Equal("\"A😀\\n\"", Slice(bytes, codec, member.ValueStart, member.ValueEnd));
        Assert.Equal(member.ValueEnd, member.End);
        Assert.Equal(root.Id, member.ParentId);
        Assert.Equal(0L, member.Ordinal);
        Assert.Equal(2, member.Depth);

        Assert.Equal(StructureKind.JsonArray, array.Kind);
        Assert.Equal("\"list\"", Slice(bytes, codec, array.NameStart, array.NameEnd));
        Assert.Equal("[true,null,-12.5e+3]", Slice(bytes, codec, array.ValueStart, array.ValueEnd));
        Assert.Equal(1L, array.Ordinal);
        Assert.Equal(3L, array.ChildCount);
        Assert.Equal(new[] { StructureKind.JsonBoolean, StructureKind.JsonNull, StructureKind.JsonNumber }, sink.Nodes.Skip(4).Select(n => n.Kind));
        Assert.Equal(new long[] { 0, 1, 2 }, sink.Nodes.Skip(4).Select(n => n.Ordinal));
        Assert.All(sink.Nodes.Skip(4), n => { Assert.Equal(array.Id, n.ParentId); Assert.Equal(-1L, n.NameStart); Assert.Equal(-1L, n.NameEnd); });
    }

    [Theory]
    [InlineData("{}", StructureKind.JsonObject)]
    [InlineData("[]", StructureKind.JsonArray)]
    [InlineData("false", StructureKind.JsonBoolean)]
    [InlineData("null", StructureKind.JsonNull)]
    [InlineData("-0", StructureKind.JsonNumber)]
    [InlineData("0.125e-12", StructureKind.JsonNumber)]
    [InlineData("\"\\uD83D\\uDE00\\u0000\\b\\f\\n\\r\\t\\/\\\\\\\"\"", StructureKind.JsonString)]
    public void ADocumentHasExactlyOneRootValue(string text, StructureKind kind)
    {
        var (sink, bytes) = Parse(text);
        Assert.Equal(2, sink.Nodes.Count);
        Assert.Equal(kind, sink.Nodes[1].Kind);
        Assert.Equal(1L, sink.Nodes[0].ChildCount);
        Assert.Equal(0L, sink.Nodes[1].ParentId);
        Assert.Equal(0L, sink.Nodes[1].Ordinal);
        Assert.Equal(bytes.LongLength, sink.Nodes[1].ValueEnd);
    }

    [Fact]
    public void DuplicateAndEscapedNamesRemainSeparateMembers()
    {
        var (sink, bytes) = Parse("{\"a\":1,\"\\u0061\":2,\"a\":3}");
        var members = sink.Nodes.Skip(2).ToArray();
        Assert.Equal(3L, sink.Nodes[1].ChildCount);
        Assert.Equal(new[] { "\"a\"", "\"\\u0061\"", "\"a\"" }, members.Select(n => Slice(bytes, TextCodec.Utf8, n.NameStart, n.NameEnd)));
        Assert.Equal(new[] { "1", "2", "3" }, members.Select(n => Slice(bytes, TextCodec.Utf8, n.ValueStart, n.ValueEnd)));
        Assert.Equal(new long[] { 0, 1, 2 }, members.Select(n => n.Ordinal));
        Assert.Equal(3, members.Select(n => n.Id).Distinct().Count());
    }

    [Fact]
    public void JsoncCommentsAndTrailingCommasKeepLiteralSpans()
    {
        const string text = "/* before */{\"a\"/* key */:/* value */[1,// item\r2,],/* last */} // after";
        var (sink, bytes) = Parse(text, jsonc: true);
        Assert.Equal(5, sink.Nodes.Count);
        Assert.Equal(1L, sink.Nodes[1].ChildCount);
        Assert.Equal(2L, sink.Nodes[2].ChildCount);
        Assert.Equal("[1,// item\r2,]", Slice(bytes, TextCodec.Utf8, sink.Nodes[2].ValueStart, sink.Nodes[2].ValueEnd));
        Assert.Equal("\"a\"", Slice(bytes, TextCodec.Utf8, sink.Nodes[2].NameStart, sink.Nodes[2].NameEnd));
        Assert.Equal(new[] { "1", "2" }, sink.Nodes.Skip(3).Select(n => Slice(bytes, TextCodec.Utf8, n.ValueStart, n.ValueEnd)));
        Assert.Equal(bytes.LongLength, sink.Nodes[0].End);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("{} []", false)]
    [InlineData("{\"a\" 1}", false)]
    [InlineData("{\"a\":}", false)]
    [InlineData("{\"a\":1,}", false)]
    [InlineData("[1,]", false)]
    [InlineData("[1,,2]", true)]
    [InlineData("{,}", true)]
    [InlineData("/* never", true)]
    [InlineData("/* comment */0", false)]
    [InlineData("[1 2]", true)]
    [InlineData("\"\\q\"", false)]
    [InlineData("\"\\u12xz\"", false)]
    [InlineData("\"\\uD800\"", false)]
    [InlineData("\"\\uDC00\"", false)]
    [InlineData("\"\\uD800\\u0041\"", false)]
    [InlineData("\"a\nb\"", false)]
    [InlineData("\"unterminated", false)]
    [InlineData("+1", false)]
    [InlineData("01", false)]
    [InlineData("1.", false)]
    [InlineData("1e+", false)]
    [InlineData("NaN", false)]
    [InlineData("true false", false)]
    public void InvalidGrammarThrowsAnIssueInsideSourceBounds(string text, bool jsonc)
    {
        var exception = Assert.Throws<StructureSyntaxException>(() => Parse(text, jsonc: jsonc));
        Assert.Equal(StructureIssueKind.Syntax, exception.Issue.Kind);
        Assert.InRange(exception.Issue.Start, 0, Encoding.UTF8.GetByteCount(text));
        Assert.InRange(exception.Issue.End, exception.Issue.Start, Encoding.UTF8.GetByteCount(text));
    }

    [Fact]
    public void InvalidEncodedStringIsNotSilentlyReplaced()
    {
        byte[] bytes = [(byte)'"', 0xF0, 0x28, 0x8C, 0x28, (byte)'"'];
        var original = bytes.ToArray();
        var exception = Assert.Throws<StructureSyntaxException>(() => JsonStructureReader.Parse(new(new ArraySnapshot(bytes), TextCodec.Utf8, 0, new Sink(), CancellationToken.None)));
        Assert.Equal(StructureIssueKind.Encoding, exception.Issue.Kind);
        Assert.Equal(1L, exception.Issue.Start);
        Assert.Equal(original, bytes);
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16-le")]
    public void AUnicodeStringCanCrossTheReadWindow(string encoding)
    {
        string text = "\"" + new string('a', 65_533) + "😀end\"";
        var codec = Codec(encoding);
        var (sink, bytes) = Parse(text, codec);
        var value = sink.Nodes[1];
        Assert.Equal(text, Slice(bytes, codec, value.ValueStart, value.ValueEnd));
        Assert.Equal(bytes.LongLength, value.End);
    }

    [Fact]
    public void DeepContainersUseABoundedStack()
    {
        const int depth = 2048;
        var (sink, _) = Parse(new string('[', depth) + "0" + new string(']', depth));
        Assert.Equal(depth + 2, sink.Nodes.Count);
        Assert.Equal(depth + 1, sink.Nodes[^1].Depth);
        Assert.All(sink.Nodes.Skip(1).Take(depth), n => Assert.Equal(1L, n.ChildCount));
        var exception = Assert.Throws<StructureSyntaxException>(() => Parse(new string('[', JsonStructureReader.MaxDepth + 1)));
        Assert.Equal(StructureIssueKind.ResourceLimit, exception.Issue.Kind);
    }

    private sealed class RepeatedToken(string prefix, byte fill, long bodyBytes, string suffix, CancellationTokenSource? cancellation = null) : IByteSnapshot
    {
        private readonly byte[] before = Encoding.ASCII.GetBytes(prefix);
        private readonly byte[] after = Encoding.ASCII.GetBytes(suffix);
        public long ByteLength => before.Length + bodyBytes + after.Length;
        public long BytesRead { get; private set; }

        public int Read(long offset, Span<byte> destination)
        {
            int count = (int)Math.Min(destination.Length, ByteLength - offset);
            for (int i = 0; i < count; i++)
            {
                long at = offset + i;
                destination[i] = at < before.Length ? before[(int)at] : at >= before.Length + bodyBytes ? after[(int)(at - before.Length - bodyBytes)] : fill;
            }
            BytesRead += count;
            if (BytesRead >= 128 * 1024) cancellation?.Cancel();
            return count;
        }
    }

    [Theory]
    [InlineData("\"", 'a', "\"", false)]
    [InlineData("", '1', "", false)]
    [InlineData("", ' ', "0", false)]
    [InlineData("/*", 'a', "*/0", true)]
    public void CancellationIsObservedInsideALongToken(string prefix, char fill, string suffix, bool jsonc)
    {
        using var cancellation = new CancellationTokenSource();
        var source = new RepeatedToken(prefix, (byte)fill, 1L << 30, suffix, cancellation);
        var context = new StructureParseContext(source, TextCodec.Utf8, 0, new Sink(), cancellation.Token);
        Assert.Throws<OperationCanceledException>(() => JsonStructureReader.Parse(context, jsonc));
        Assert.InRange(source.BytesRead, 1, 1 << 20);
    }

    [Fact]
    public void ALongStringIsScannedWithoutKeepingItsText()
    {
        var source = new RepeatedToken("\"", (byte)'a', 8L << 20, "\"");
        var sink = new Sink();
        var context = new StructureParseContext(source, TextCodec.Utf8, 0, sink, CancellationToken.None);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        JsonStructureReader.Parse(context);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.Equal(2, sink.Nodes.Count);
        Assert.Equal(source.ByteLength, sink.Nodes[1].ValueEnd);
        Assert.True(allocated < 2L << 20, $"The parser allocated {allocated} bytes for an 8 MiB string.");
    }
}
