using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Tests;

public sealed class StructureQueryTests
{
    [Fact]
    public void Json_members_indexes_escaped_unicode_and_duplicates_keep_node_identity()
    {
        var index = Parse("""{"a.b":[{"n":1,"s":"x"},{"n":2,"s":"a\u0062"}],"dup":1,"dup":2,"\u679D\uD83D\uDE00":{"v":true}}""");
        Assert.Equal(new[] { "ab" }, Values(index, "$['a.b'][1].s"));
        Assert.Equal(new[] { "true" }, Values(index, "$['枝😀'].v"));
        long[] duplicates = All(index, "$.dup");
        Assert.Equal(2, duplicates.Length);
        Assert.NotEqual(duplicates[0], duplicates[1]);
        Assert.Equal(new[] { "1", "2" }, Values(index, "$..n"));
        Assert.Equal(1L, Assert.Single(All(index, "$")));
        Assert.Empty(All(index, "$.missing"));
    }

    [Fact]
    public void Json_filters_compare_typed_scalars_and_exact_decimal_values()
    {
        var index = Parse("""{"items":[{"n":1.00,"s":"ab","b":true,"z":null},{"n":2e0,"s":"a\u0062","b":false},{"n":-0.0,"s":"1","z":"null"}]}""");
        Assert.Equal(new[] { "ab" }, Values(index, "$.items[?(@.n==1e0)].s"));
        Assert.Equal(new[] { "1.00", "2e0" }, Values(index, "$.items[?(@.s=='ab')].n"));
        Assert.Equal(new[] { "1.00" }, Values(index, "$.items[?(@.b==true)].n"));
        Assert.Equal(new[] { "1.00" }, Values(index, "$.items[?(@.z==null)].n"));
        Assert.Equal(new[] { "1" }, Values(index, "$.items[?(@.n==0)].s"));
        Assert.Empty(All(index, "$.items[?(@.n=='1')].s"));
        var scalars = Parse("[1,1.000,10e-1,0.100e1,2,-0,0e999999]");
        Assert.Equal(4, All(scalars, "$[?(@==1)]").Length);
        Assert.Equal(2, All(scalars, "$[?(@==0)]").Length);
        var precise = Parse("[9007199254740992,9007199254740993,90071992547409930e-1,1e999999999999999999999]");
        Assert.Equal(2, All(precise, "$[?(@==9007199254740993)]").Length);
        Assert.Single(All(precise, "$[?(@==10e999999999999999999998)]"));
    }

    [Fact]
    public void Pages_resume_in_document_order_without_duplicates_or_unbounded_results()
    {
        var index = Parse("""{"a":[{"a":1},{"a":2}],"b":3,"a":4}""");
        var query = StructureQuery.Parse("$..*", StructureQueryLanguage.JsonPath);
        var expected = query.ReadPage(index, 0, 4096, default).NodeIds;
        var combined = new List<long>();
        long next = 0;
        bool complete;
        do
        {
            var page = query.ReadPage(index, next, 2, default);
            Assert.InRange(page.NodeIds.Count, 0, 2);
            Assert.True(page.NextNodeId > next);
            combined.AddRange(page.NodeIds);
            next = page.NextNodeId;
            complete = page.Complete;
        } while (!complete);
        Assert.Equal(expected, combined);
        Assert.Equal(combined.OrderBy(id => id), combined);
        Assert.Equal(combined.Count, combined.Distinct().Count());
        var terminal = query.ReadPage(index, next, 2, default);
        Assert.True(terminal.Complete);
        Assert.Empty(terminal.NodeIds);
    }

    [Fact]
    public void Xml_names_match_namespace_uris_and_default_namespace_does_not_match_unprefixed_xpath()
    {
        var index = Parse("""<r xmlns="urn:d" xmlns:q="urn:p" xml:lang="cs"><q:item id="a&amp;b">one<![CDATA[&two]]></q:item><q:item id="z">three</q:item><plain xmlns="">x</plain></r>""", xml: true);
        var ns = new Dictionary<string, string> { ["d"] = "urn:d", ["p"] = "urn:p" };
        Assert.Empty(All(index, "/r"));
        Assert.Single(All(index, "/d:r", ns));
        Assert.Equal(2, All(index, "//p:item", ns).Length);
        Assert.Equal(new[] { "z" }, Values(index, "//p:item[2]/@id", ns));
        Assert.Equal(new[] { "cs" }, Values(index, "/d:r/@xml:lang", ns));
        Assert.Equal(new[] { "x" }, Values(index, "//plain/text()", ns));
        Assert.Equal(2, All(index, "/d:r/p:*", ns).Length);
        Assert.Equal(4, All(index, "//*", ns).Length);
        Assert.Equal(0L, Assert.Single(All(index, "/", ns)));
        var error = Assert.Throws<StructureQueryException>(() => All(index, "//unknown:item"));
        Assert.Contains("namespace", error.Message);
    }

    [Fact]
    public void Xml_predicates_decode_entities_include_cdata_and_apply_positions_after_prior_predicates()
    {
        var index = Parse("""<r><item id="a&amp;b">one<![CDATA[&two]]></item><item id="z">three</item><item id="z">four</item><!--note--><?p data?></r>""", xml: true);
        Assert.Equal(new[] { "one", "&two" }, Values(index, "//item[@id='a&b']/text()"));
        Assert.Single(All(index, "//item[.='one&two']"));
        Assert.Equal(new[] { "three" }, Values(index, "//item[@id='z'][1]/text()"));
        Assert.Empty(All(index, "//item[1][@id='z']"));
        Assert.Equal(new[] { "four" }, Values(index, "//item[text()='four']/text()"));
        Assert.Equal(5, All(index, "/r/node()").Length);
        Assert.Single(All(index, "/r/comment()"));
        var nested = Parse("<r><item><name>A&amp;B</name></item><item><name>C</name></item></r>", xml: true);
        Assert.Single(All(nested, "//item[name='A&B']"));
    }

    [Fact]
    public void Utf16_queries_and_previews_decode_surrogates_escapes_and_xml_attribute_whitespace()
    {
        var json = Parse("""{"\u679D":"A\uD83D\uDE00\nB"}""", codec: TextCodec.Utf16BEBom);
        long id = Assert.Single(All(json, "$['枝']"));
        var node = json.ReadNode(id);
        Assert.Equal(new StructurePreview("枝", false), StructureQuery.ReadPreview(json, node, name: true));
        Assert.Equal(new StructurePreview("A😀", true), StructureQuery.ReadPreview(json, node, maxChars: 3));
        Assert.Equal(new StructurePreview("A", true), StructureQuery.ReadPreview(json, node, maxChars: 2));
        Assert.Equal(new StructurePreview("A😀\nB", false), StructureQuery.ReadPreview(json, node, maxChars: 5));
        var xml = Parse("<r a='A\tB&#xA;😀'>&#x1F600;&amp;</r>", xml: true, codec: TextCodec.Utf16LEBom);
        Assert.Equal(new[] { "A B\n😀" }, Values(xml, "/r/@a"));
        Assert.Equal(new[] { "😀&" }, Values(xml, "/r/text()"));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("$.x[", false)]
    [InlineData("$[-1]", false)]
    [InlineData("$[0,1]", false)]
    [InlineData("$[1:3]", false)]
    [InlineData("$[?(@.a!=1)]", false)]
    [InlineData("$[?(@.a==01)]", false)]
    [InlineData("$['\\uD800']", false)]
    [InlineData("", true)]
    [InlineData("//", true)]
    [InlineData("/r/", true)]
    [InlineData("/r[0]", true)]
    [InlineData("/r | /x", true)]
    [InlineData("/r/ancestor::x", true)]
    [InlineData("/r[contains(@x,'a')]", true)]
    public void Malformed_and_unsupported_expressions_report_a_position(string expression, bool xml)
    {
        var error = Assert.Throws<StructureQueryException>(() => StructureQuery.Parse(expression,
            xml ? StructureQueryLanguage.XPath : StructureQueryLanguage.JsonPath));
        Assert.InRange(error.Position, 0, expression.Length);
    }

    [Fact]
    public void Cancellation_interrupts_a_gigabyte_numeric_comparison_without_reading_the_whole_value()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new LongNumberSource(cancellation);
        var index = new MemoryIndex(source, TextCodec.Utf8, StructureFormat.Json);
        long end = source.ByteLength;
        index.Nodes.Add(new(0, -1, StructureKind.Document, 0, end, -1, -1, 0, end, 0, 1, 2, 0));
        index.Nodes.Add(new(1, 0, StructureKind.JsonArray, 0, end, -1, -1, 0, end, 0, 1, 2, 1));
        index.Nodes.Add(new(2, 1, StructureKind.JsonNumber, 1, end - 1, -1, -1, 1, end - 1, 0, 0, 2, 2));
        var query = StructureQuery.Parse("$[?(@==1)]", StructureQueryLanguage.JsonPath);
        Assert.Throws<OperationCanceledException>(() => query.ReadPage(index, 0, 8, cancellation.Token));
        Assert.InRange(source.BytesRead, 1, 1 << 20);
    }

    [Fact]
    public void Query_format_and_page_guards_are_explicit_and_cancellation_is_checked_before_scanning()
    {
        var index = Parse("{}");
        var query = StructureQuery.Parse("$", StructureQueryLanguage.JsonPath);
        Assert.Throws<ArgumentOutOfRangeException>(() => query.ReadPage(index, 0, 0, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => query.ReadPage(index, -1, 1, default));
        Assert.Throws<ArgumentException>(() => StructureQuery.Parse("/", StructureQueryLanguage.XPath).ReadPage(index, 0, 1, default));
        Assert.Throws<OperationCanceledException>(() => query.ReadPage(index, 0, 1, new CancellationToken(true)));
    }

    private static MemoryIndex Parse(string text, bool xml = false, TextCodec? codec = null)
    {
        codec ??= TextCodec.Utf8;
        var index = new MemoryIndex(new ArraySnapshot([.. codec.Bom, .. codec.Encode(text)]), codec,
            xml ? StructureFormat.Xml : StructureFormat.Json);
        var context = new StructureParseContext(index.Source, codec, codec.Bom.Length, index, default);
        if (xml) XmlStructureReader.Parse(context);
        else JsonStructureReader.Parse(context);
        return index;
    }

    private static long[] All(MemoryIndex index, string expression, IReadOnlyDictionary<string, string>? ns = null)
    {
        var query = StructureQuery.Parse(expression, index.Format == StructureFormat.Xml ? StructureQueryLanguage.XPath : StructureQueryLanguage.JsonPath);
        var ids = new List<long>();
        long next = 0;
        while (true)
        {
            var page = query.ReadPage(index, next, 2, default, ns);
            ids.AddRange(page.NodeIds);
            if (page.Complete) return ids.ToArray();
            next = page.NextNodeId;
        }
    }

    private static string[] Values(MemoryIndex index, string expression, IReadOnlyDictionary<string, string>? ns = null) =>
        All(index, expression, ns).Select(id => StructureQuery.ReadPreview(index, index.ReadNode(id)).Text).ToArray();

    private sealed class MemoryIndex(IByteSnapshot source, TextCodec codec, StructureFormat format) : IStructureIndex, IStructureSink
    {
        public List<StructureNode> Nodes { get; } = [];
        public long Count => Nodes.Count;
        public IByteSnapshot Source => source;
        public TextCodec Codec => codec;
        public int BomLength => codec.Bom.Length;
        public StructureFormat Format => format;
        public Guid DocumentId { get; } = Guid.NewGuid();
        public long Revision => 1;
        public StructureNode ReadNode(long id) => Nodes[checked((int)id)];
        public long AddNode(StructureKind kind, long parentId, long start, long nameStart, long nameEnd, long valueStart, long ordinal, int depth)
        {
            long id = Nodes.Count;
            Nodes.Add(new(id, parentId, kind, start, -1, nameStart, nameEnd, valueStart, -1, ordinal, 0, id, depth));
            return id;
        }
        public void CompleteNode(long id, long valueStart, long valueEnd, long end, long childCount) => Nodes[(int)id] = Nodes[(int)id] with
        { ValueStart = valueStart, ValueEnd = valueEnd, End = end, ChildCount = childCount, LastDescendantId = Nodes.Count - 1 };
        public void SetNamespace(long id, long namespaceId) => Nodes[(int)id] = Nodes[(int)id] with { NamespaceId = namespaceId };
        public void Report(StructureIssue issue) => throw new InvalidOperationException(issue.Message);
    }

    private sealed class LongNumberSource(CancellationTokenSource cancellation) : IByteSnapshot
    {
        public long ByteLength => (1L << 30) + 5;
        public long BytesRead { get; private set; }
        public int Read(long offset, Span<byte> destination)
        {
            int count = (int)Math.Min(destination.Length, ByteLength - offset);
            destination[..count].Fill((byte)'0');
            for (int i = 0; i < count; i++)
            {
                long at = offset + i;
                if (at == 0) destination[i] = (byte)'[';
                else if (at == 1) destination[i] = (byte)'1';
                else if (at == 2) destination[i] = (byte)'.';
                else if (at == ByteLength - 1) destination[i] = (byte)']';
            }
            BytesRead += count;
            if (BytesRead >= 128 * 1024) cancellation.Cancel();
            return count;
        }
    }
}
