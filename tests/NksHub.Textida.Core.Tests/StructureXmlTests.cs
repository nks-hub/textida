using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Tests;

public class StructureXmlTests
{
    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("utf32le")]
    [InlineData("utf32be")]
    public void Unicode_names_values_and_delimiters_keep_their_exact_source_byte_spans(string encoding)
    {
        var codec = Codec(encoding);
        const string source = "<?xml version=\"1.0\"?>\r\n<r xmlns=\"urn:é\" xmlns:p=\"urn:p\" p:名=\"&#x1F600; &amp;\">a😀<![CDATA[<é>&]]><p:𐐀/></r><!--tail-->";
        var parsed = Parse(source, codec, shortReads: true);
        var document = Assert.Single(parsed.Sink.Nodes, n => n.Kind == StructureKind.Document);
        var root = Assert.Single(parsed.Sink.Nodes, n => n.Kind == StructureKind.XmlElement && parsed.Name(n) == "r");
        var child = Assert.Single(parsed.Sink.Nodes, n => n.Kind == StructureKind.XmlElement && parsed.Name(n) == "p:𐐀");
        var attribute = Assert.Single(parsed.Sink.Nodes, n => n.Kind == StructureKind.XmlAttribute);
        var text = Assert.Single(parsed.Sink.Nodes, n => n.Kind == StructureKind.XmlText && n.ParentId == root.Id);
        var cdata = Assert.Single(parsed.Sink.Nodes, n => n.Kind == StructureKind.XmlCData);
        Assert.Equal((0L, (long)parsed.Bytes.Length, -1L, 0), (document.Start, document.End, document.ParentId, document.Depth));
        Assert.Equal(4, document.ChildCount);
        Assert.Equal(6, root.ChildCount);
        Assert.Equal(source[source.IndexOf("<r ", StringComparison.Ordinal)..source.IndexOf("<!--tail", StringComparison.Ordinal)], parsed.Raw(root));
        Assert.Equal("a😀<![CDATA[<é>&]]><p:𐐀/>", parsed.Value(root));
        Assert.Equal("p:名", parsed.Name(attribute));
        Assert.Equal("p:名=\"&#x1F600; &amp;\"", parsed.Raw(attribute));
        Assert.Equal("&#x1F600; &amp;", parsed.Value(attribute));
        Assert.Equal("a😀", parsed.Raw(text));
        Assert.Equal("<![CDATA[<é>&]]>", parsed.Raw(cdata));
        Assert.Equal("<é>&", parsed.Value(cdata));
        Assert.Equal("<p:𐐀/>", parsed.Raw(child));
        Assert.Equal(child.End, child.ValueStart);
        Assert.Equal(child.ValueStart, child.ValueEnd);
        Assert.Equal(root.Id, child.ParentId);
        Assert.Equal(2, child.Depth);
        Assert.Equal(0, child.ChildCount);
        foreach (var group in parsed.Sink.Nodes.Where(n => n.ParentId >= 0).GroupBy(n => n.ParentId))
            Assert.Equal(Enumerable.Range(0, group.Count()).Select(i => (long)i), group.Select(n => n.Ordinal));
        Assert.Equal(2, parsed.Sink.Nodes.Count(n => n.Kind == StructureKind.XmlNamespace));
        Assert.Equal(StructureKind.XmlNamespace, parsed.Sink.Nodes[(int)attribute.NamespaceId].Kind);
    }

    [Fact]
    public void Namespace_scopes_apply_after_the_whole_start_tag_and_restore_after_empty_children()
    {
        var parsed = Parse("<r p:a='1' a='2' xml:lang='en' xmlns='urn:a' xmlns:p='urn:b'><p:x p:a='3' xmlns:p='urn:c'/><x xmlns=''><y/></x><p:z/></r>");
        var nodes = parsed.Sink.Nodes;
        var root = Assert.Single(nodes, n => n.Kind == StructureKind.XmlElement && parsed.Name(n) == "r");
        var rootDefault = Assert.Single(nodes, n => n.Kind == StructureKind.XmlNamespace && n.ParentId == root.Id && parsed.Name(n) == "xmlns");
        var rootPrefix = Assert.Single(nodes, n => n.Kind == StructureKind.XmlNamespace && n.ParentId == root.Id && parsed.Name(n) == "xmlns:p");
        var inner = Assert.Single(nodes, n => n.Kind == StructureKind.XmlElement && parsed.Name(n) == "p:x");
        var innerPrefix = Assert.Single(nodes, n => n.Kind == StructureKind.XmlNamespace && n.ParentId == inner.Id);
        Assert.Equal(rootDefault.Id, root.NamespaceId);
        Assert.Equal(rootPrefix.Id, Assert.Single(nodes, n => n.Kind == StructureKind.XmlAttribute && n.ParentId == root.Id && parsed.Name(n) == "p:a").NamespaceId);
        Assert.Equal(-1, Assert.Single(nodes, n => n.Kind == StructureKind.XmlAttribute && parsed.Name(n) == "a").NamespaceId);
        Assert.Equal(-2, Assert.Single(nodes, n => n.Kind == StructureKind.XmlAttribute && parsed.Name(n) == "xml:lang").NamespaceId);
        Assert.Equal(innerPrefix.Id, inner.NamespaceId);
        Assert.Equal(innerPrefix.Id, Assert.Single(nodes, n => n.Kind == StructureKind.XmlAttribute && n.ParentId == inner.Id).NamespaceId);
        Assert.Equal(rootPrefix.Id, Assert.Single(nodes, n => n.Kind == StructureKind.XmlElement && parsed.Name(n) == "p:z").NamespaceId);
        Assert.All(nodes.Where(n => n.Kind == StructureKind.XmlElement && parsed.Name(n) is "x" or "y"), n => Assert.Equal(-1, n.NamespaceId));
    }

    [Fact]
    public void Explicit_xml_binding_is_preserved_and_predefined_entities_do_not_change_raw_spans()
    {
        var parsed = Parse("<xml:r xml:lang='en' xmlns:xml='http://www.w3.org/XML/1998/namespace'>&lt;&gt;&quot;&apos;&amp;&#65;&#x1F600;</xml:r>");
        var binding = Assert.Single(parsed.Sink.Nodes, n => n.Kind == StructureKind.XmlNamespace);
        Assert.All(parsed.Sink.Nodes.Where(n => n.Kind is StructureKind.XmlElement or StructureKind.XmlAttribute), n => Assert.Equal(binding.Id, n.NamespaceId));
        Assert.Equal("&lt;&gt;&quot;&apos;&amp;&#65;&#x1F600;", parsed.Raw(Assert.Single(parsed.Sink.Nodes, n => n.Kind == StructureKind.XmlText)));
    }

    [Theory]
    [InlineData("<!DOCTYPE r><r/>")]
    [InlineData("<!DOCTYPE r [<!ENTITY x 'expanded'>]><r>&x;</r>")]
    [InlineData("<!DOCTYPE r SYSTEM 'http://127.0.0.1:9/never.dtd'><r/>")]
    [InlineData("<!DOCTYPE r [<!ENTITY x SYSTEM 'file:///not-a-real-fixture'>]><r>&x;</r>")]
    [InlineData("<!DOCTYPE r [<!ENTITY % p SYSTEM 'http://127.0.0.1:9/never.dtd'>%p;]><r/>")]
    [InlineData("<r><!ENTITY x 'value'></r>")]
    public void Dtd_and_entity_declarations_are_rejected_without_resolving_entities(string source)
    {
        var failure = Assert.Throws<StructureSyntaxException>(() => Parse(source));
        Assert.Equal(StructureIssueKind.ForbiddenDtd, failure.Issue.Kind);
        Assert.Equal(source.IndexOf("<!", StringComparison.Ordinal), failure.Issue.Start);
    }

    [Theory]
    [InlineData("<r>&unknown;</r>")]
    [InlineData("<r a='&unknown;'/>")]
    [InlineData("<r xmlns:p='&unknown;'/>")]
    public void Undeclared_entities_are_not_silently_accepted_or_expanded(string source)
    {
        var failure = Assert.Throws<StructureSyntaxException>(() => Parse(source));
        Assert.Equal(StructureIssueKind.UnknownEntity, failure.Issue.Kind);
        Assert.Equal(source.IndexOf('&'), failure.Issue.Start);
    }

    [Theory]
    [InlineData("<r></s>")]
    [InlineData("<r/><s/>")]
    [InlineData("<r>")]
    [InlineData("<a:b:c/>")]
    [InlineData("<r a='1' a='2'/>")]
    [InlineData("<r xmlns:p='urn:x' xmlns:q='urn:x' p:a='1' q:a='2'/>")]
    [InlineData("<r xmlns:p='urn:&#x78;' xmlns:q='urn:x' p:a='1' q:a='2'/>")]
    [InlineData("<r xmlns:p='urn:x' xmlns:p='urn:y'/>")]
    [InlineData("<r xmlns='http://www.w3.org/XML/1998/namespace'/>")]
    [InlineData("<r xmlns:p='http://www.w3.org/2000/xmlns/'/>")]
    [InlineData("<r xmlns:xml='urn:wrong'/>")]
    [InlineData("<r xmlns:xmlns='urn:wrong'/>")]
    [InlineData("<r xmlns:p=''/>")]
    [InlineData("<r><p:x/></r>")]
    [InlineData("<r p:a='x'/>")]
    [InlineData("<r>]]></r>")]
    [InlineData("<r><!--a--b--></r>")]
    [InlineData("<r><![CDATA[unclosed</r>")]
    [InlineData("<r a='<raw'/>")]
    [InlineData("<r>&#0;</r>")]
    [InlineData("<r>&#xD800;</r>")]
    [InlineData("<r>&#x110000;</r>")]
    [InlineData("<r>&#;</r>")]
    [InlineData("<r>&amp</r>")]
    [InlineData("<r>\u0001</r>")]
    [InlineData(" <?xml version='1.0'?><r/>")]
    [InlineData("<r><?xml version='1.0'?></r>")]
    [InlineData("<?XmL?><r/>")]
    [InlineData("<?xml encoding='utf-8' version='1.0'?><r/>")]
    [InlineData("<?xml version='1.0' standalone='yes' encoding='utf-8'?><r/>")]
    public void Malformed_xml_reports_an_explicit_source_issue(string source)
    {
        var failure = Assert.Throws<StructureSyntaxException>(() => Parse(source));
        Assert.Equal(StructureIssueKind.Syntax, failure.Issue.Kind);
        Assert.InRange(failure.Issue.Start, 0, TextCodec.Utf8.Encode(source).LongLength);
        Assert.InRange(failure.Issue.End, failure.Issue.Start, TextCodec.Utf8.Encode(source).LongLength);
    }

    [Fact]
    public void Xml_11_is_reported_as_an_unsupported_version()
    {
        var failure = Assert.Throws<StructureSyntaxException>(() => Parse("<?xml version='1.1'?><r/>"));
        Assert.Contains("Unsupported XML version", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Processing_instructions_comments_and_declarations_keep_source_ranges()
    {
        var parsed = Parse("<?xml version='1.0' encoding='UTF-8' standalone='yes'?><?before x?><r><!--x--><?inside y?></r><?after?>");
        var instructions = parsed.Sink.Nodes.Where(n => n.Kind == StructureKind.XmlProcessingInstruction).ToArray();
        Assert.Equal(["xml", "before", "inside", "after"], instructions.Select(parsed.Name));
        Assert.Equal(["version='1.0' encoding='UTF-8' standalone='yes'", "x", "y", ""], instructions.Select(parsed.Value));
        Assert.Equal("<!--x-->", parsed.Raw(Assert.Single(parsed.Sink.Nodes, n => n.Kind == StructureKind.XmlComment)));
        Assert.Equal("x", parsed.Value(Assert.Single(parsed.Sink.Nodes, n => n.Kind == StructureKind.XmlComment)));
    }

    [Fact]
    public void Deep_input_stops_with_a_resource_issue_instead_of_recursing_on_the_call_stack()
    {
        var failure = Assert.Throws<StructureSyntaxException>(() => Parse(string.Concat(Enumerable.Repeat("<r>", 20_000))));
        Assert.Equal(StructureIssueKind.ResourceLimit, failure.Issue.Kind);
        Assert.Contains("nesting", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<r><![CDATA[", "]]></r>")]
    [InlineData("<r value='", "'/>")]
    public void Cancellation_interrupts_one_huge_cdata_or_attribute_without_materializing_it(string prefix, string suffix)
    {
        using var cancellation = new CancellationTokenSource();
        var source = new RepeatedSnapshot(prefix, suffix, cancellation);
        var sink = new TestSink();
        var context = new StructureParseContext(source, TextCodec.Utf8, 0, sink, cancellation.Token);
        Assert.Throws<OperationCanceledException>(() => XmlStructureReader.Parse(context));
        Assert.True(cancellation.IsCancellationRequested);
        Assert.InRange(source.LargestReadOffset, 128 * 1024, 192 * 1024);
        Assert.InRange(source.BytesRead, 1, 512 * 1024);
        Assert.Equal(2, sink.Nodes.Count);
        Assert.Equal(1L << 30, source.RepeatedLength);
    }

    private static TextCodec Codec(string name) => name switch
    {
        "utf8" => TextCodec.Utf8Bom,
        "utf16le" => TextCodec.Utf16LEBom,
        "utf16be" => TextCodec.Utf16BEBom,
        "utf32le" => TextCodec.Utf32LEBom,
        "utf32be" => TextCodec.Utf32BEBom,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static Parsed Parse(string source, TextCodec? codec = null, bool shortReads = false)
    {
        codec ??= TextCodec.Utf8;
        byte[] bytes = [.. codec.Bom, .. codec.Encode(source)];
        var sink = new TestSink();
        IByteSnapshot snapshot = shortReads ? new ShortSnapshot(bytes) : new ArraySnapshot(bytes);
        XmlStructureReader.Parse(new(snapshot, codec, codec.Bom.Length, sink, CancellationToken.None));
        return new(bytes, codec, sink);
    }

    private sealed record Parsed(byte[] Bytes, TextCodec Codec, TestSink Sink)
    {
        public string Raw(StructureNode node) => Slice(node.Start, node.End);
        public string Value(StructureNode node) => Slice(node.ValueStart, node.ValueEnd);
        public string Name(StructureNode node) => node.HasName ? Slice(node.NameStart, node.NameEnd) : "";
        private string Slice(long start, long end) => Codec.GetString(Bytes.AsSpan(checked((int)start), checked((int)(end - start))));
    }

    private sealed class TestSink : IStructureSink
    {
        public List<StructureNode> Nodes { get; } = [];
        public long AddNode(StructureKind kind, long parentId, long start, long nameStart, long nameEnd, long valueStart, long ordinal, int depth)
        {
            long id = Nodes.Count;
            Nodes.Add(new(id, parentId, kind, start, -1, nameStart, nameEnd, valueStart, -1, ordinal, -1, id, depth));
            return id;
        }
        public void CompleteNode(long id, long valueStart, long valueEnd, long end, long childCount) =>
            Nodes[(int)id] = Nodes[(int)id] with { ValueStart = valueStart, ValueEnd = valueEnd, End = end, ChildCount = childCount, LastDescendantId = Nodes.Count - 1 };
        public void SetNamespace(long id, long namespaceId) => Nodes[(int)id] = Nodes[(int)id] with { NamespaceId = namespaceId };
        public void Report(StructureIssue issue) => throw new InvalidOperationException($"Unexpected issue: {issue}");
    }

    private sealed class ShortSnapshot(byte[] bytes) : IByteSnapshot
    {
        public long ByteLength => bytes.LongLength;
        public int Read(long offset, Span<byte> destination)
        {
            int count = (int)Math.Min(Math.Min(destination.Length, 3), bytes.LongLength - offset);
            bytes.AsSpan((int)offset, count).CopyTo(destination);
            return count;
        }
    }

    private sealed class RepeatedSnapshot(string prefix, string suffix, CancellationTokenSource cancellation) : IByteSnapshot
    {
        private readonly byte[] prefix = TextCodec.Utf8.Encode(prefix);
        private readonly byte[] suffix = TextCodec.Utf8.Encode(suffix);
        public long RepeatedLength => 1L << 30;
        public long ByteLength => prefix.Length + RepeatedLength + suffix.Length;
        public long RetainedBytes => prefix.Length + suffix.Length;
        public long LargestReadOffset { get; private set; }
        public long BytesRead { get; private set; }

        public int Read(long offset, Span<byte> destination)
        {
            int count = (int)Math.Min(destination.Length, ByteLength - offset);
            destination = destination[..count];
            destination.Fill((byte)'a');
            CopyOverlap(prefix, 0, offset, destination);
            CopyOverlap(suffix, prefix.Length + RepeatedLength, offset, destination);
            BytesRead += count;
            LargestReadOffset = Math.Max(LargestReadOffset, offset);
            if (offset >= 128 * 1024) cancellation.Cancel();
            return count;
        }

        private static void CopyOverlap(byte[] bytes, long start, long offset, Span<byte> destination)
        {
            long left = Math.Max(start, offset), right = Math.Min(start + bytes.Length, offset + destination.Length);
            if (left < right) bytes.AsSpan((int)(left - start), (int)(right - left)).CopyTo(destination[(int)(left - offset)..]);
        }
    }
}
