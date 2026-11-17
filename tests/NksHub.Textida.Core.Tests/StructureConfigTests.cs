using System.Text;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public sealed class StructureConfigTests
{
    [Theory]
    [InlineData(StructureFormat.Yaml)]
    [InlineData(StructureFormat.Neon)]
    public void Nested_configuration_keeps_typed_values_paths_and_source_bytes(StructureFormat format)
    {
        const string text = "server:\n  host: \"město😀\" # keep\n  flags: [true, null, 12.5e2]\n  routes:\n    - name: first\n      enabled: false\n    - name: second\n      enabled: true\n";
        var index = Parse(text, format);
        Assert.Equal("město😀", Value(index, "$.server.host"));
        Assert.Equal("false", Value(index, "$.server.routes[0].enabled"));
        Assert.Equal("second", Value(index, "$.server.routes[?(@.enabled==true)].name"));
        var host = Node(index, "$.server.host");
        Assert.Equal("host: \"město😀\"", StructureText.ReadSource(index, host).Text);
        Assert.Equal("$[\"server\"][\"host\"]", StructurePaths.Create(index, host));
        Assert.Equal(StructureKind.JsonNumber, Node(index, "$.server.flags[2]").Kind);
        Assert.Equal(Encoding.UTF8.GetByteCount(text[..text.IndexOf("\"město", StringComparison.Ordinal)]), host.ValueStart);
        Assert.Equal(index.Count - 1, index.Nodes[0].LastDescendantId);
    }

    [Theory]
    [InlineData("v: 'it''s a scalar'\n", "it's a scalar")]
    [InlineData("v: \"line\\n\\uD83D\\uDE00\"\n", "line\n😀")]
    [InlineData("v: \"first\n  second\"\n", "first second")]
    [InlineData("v: \"first  \n  second\"\n", "first second")]
    [InlineData("v: first\n  second\n", "first second")]
    [InlineData("v: |\n  first\n  second\nn: 1\n", "first\nsecond\n")]
    [InlineData("v: >-\n  first\n  second\n", "first second")]
    [InlineData("v: |+\n  first\n\n", "first\n\n")]
    [InlineData("v: |2-\n    more\n  base\n", "  more\nbase")]
    public void Yaml_scalars_decode_quotes_folding_indentation_and_chomping(string text, string expected)
    {
        Assert.Equal(expected, Value(Parse(text, StructureFormat.Yaml), "$.v"));
    }

    [Fact]
    public void Neon_services_retain_entities_named_arguments_and_symbolic_references()
    {
        const string text = "parameters:\n\tdebug = yes\n\troot: %appDir%/var\nservices:\n\tmailer: App\\Mailer(@transport, debug: %debug%)\n\t- @mailer\n\t- App\\Job(\n\t\tname: 'daily'\n\t\tretry: 3\n\t)\n";
        var index = Parse(text, StructureFormat.Neon);
        Assert.Equal("true", Value(index, "$.parameters.debug"));
        Assert.Equal(StructureKind.ConfigReference, Node(index, "$.parameters.root").Kind);
        var entity = Node(index, "$.services.mailer");
        Assert.Equal(StructureKind.ConfigEntity, entity.Kind);
        Assert.Equal(2, entity.ChildCount);
        Assert.Equal("%debug%", Value(index, "$.services.mailer.debug"));
        Assert.Equal("@transport", Value(index, "$.services.mailer[0]"));
        Assert.Contains("App\\Mailer(@transport", StructureText.ReadSource(index, entity).Text);
        Assert.Equal(StructureKind.JsonObject, Node(index, "$.services").Kind);
    }

    [Theory]
    [InlineData("v: '''\n  first\n    second\n  third\n  '''\n", "first\n  second\nthird")]
    [InlineData("v: \"\"\"\n  first\\nsecond\n  \"\"\"\n", "first\nsecond")]
    [InlineData("v: 'it''s not escaped: \\n'\n", "it's not escaped: \\n")]
    public void Neon_multiline_and_single_quotes_keep_their_own_escape_rules(string text, string expected)
    {
        Assert.Equal(expected, Value(Parse(text, StructureFormat.Neon), "$.v"));
    }

    [Fact]
    public void Neon_chains_and_mixed_inline_collections_preserve_all_items()
    {
        var index = Parse("v: Column(type: int) Field(id: 1)\nitems: {first: 1, 'other', enabled: yes}\n", StructureFormat.Neon);
        var entity = Node(index, "$.v");
        Assert.Equal(StructureKind.ConfigEntity, entity.Kind);
        Assert.Contains("Field(id: 1)", StructureText.ReadSource(index, entity).Text);
        Assert.Equal("other", Value(index, "$.items[0]"));
        Assert.Equal(1, Node(index, "$.items[0]").Ordinal);
        Assert.Equal("true", Value(index, "$.items.enabled"));
    }

    [Fact]
    public void Quotes_inside_neon_sequence_comments_are_not_tokens()
    {
        var index = Parse("items:\n  - first # an unmatched ' comment\n  - second\n", StructureFormat.Neon);
        Assert.Equal(StructureKind.JsonArray, Node(index, "$.items").Kind);
        Assert.Equal("second", Value(index, "$.items[1]"));
    }

    [Theory]
    [InlineData(StructureFormat.Yaml)]
    [InlineData(StructureFormat.Neon)]
    public void Decimal_and_radix_filters_and_decoded_search_share_the_index(StructureFormat format)
    {
        var index = Parse("items: [{n: +1.5, s: \"Brn\\u006f\"}, {n: 0x10, s: other}]\n", format);
        Assert.Equal("Brno", Value(index, "$.items[?(@.n==1.5)].s"));
        Assert.Equal("other", Value(index, "$.items[?(@.n==16)].s"));
        var matches = StructureSearch.ReadPage(index, "brno", StructureSearchScope.Value, 0, 64);
        Assert.Equal(Node(index, "$.items[0].s").Id, Assert.Single(matches.NodeIds));
    }

    [Fact]
    public void Yaml_tags_anchors_and_aliases_are_symbolic_without_expansion()
    {
        var index = Parse("defaults: &base\n  enabled: true\ncopy: *base\ntext: !!str 12\ncycle: &loop {self: *loop}\n", StructureFormat.Yaml);
        Assert.Equal(StructureKind.ConfigTaggedValue, Node(index, "$.defaults").Kind);
        Assert.Equal(StructureKind.ConfigReference, Node(index, "$.copy").Kind);
        Assert.Equal("*base", Value(index, "$.copy"));
        Assert.Equal("!!str 12", StructureText.ReadSource(index, Node(index, "$.text")).Text.Split(": ", 2)[1]);
        Assert.InRange(index.Count, 10, 20);
    }

    [Theory]
    [InlineData(StructureFormat.Yaml, "v: yes\n", StructureKind.JsonString)]
    [InlineData(StructureFormat.Yaml, "v: 1.0.0\n", StructureKind.JsonString)]
    [InlineData(StructureFormat.Yaml, "v: 0x7A\n", StructureKind.JsonNumber)]
    [InlineData(StructureFormat.Yaml, "v: .inf\n", StructureKind.JsonNumber)]
    [InlineData(StructureFormat.Neon, "v: yes\n", StructureKind.JsonBoolean)]
    [InlineData(StructureFormat.Neon, "v: on\n", StructureKind.JsonString)]
    [InlineData(StructureFormat.Neon, "v: 0b110\n", StructureKind.JsonNumber)]
    [InlineData(StructureFormat.Neon, "v: 1_000\n", StructureKind.JsonString)]
    public void Scalar_types_follow_the_selected_configuration_format(StructureFormat format, string text, StructureKind kind)
    {
        Assert.Equal(kind, Node(Parse(text, format), "$.v").Kind);
    }

    [Theory]
    [InlineData(StructureFormat.Yaml, "v: [1, 2\n")]
    [InlineData(StructureFormat.Yaml, "v: \"\\uD800\"\n")]
    [InlineData(StructureFormat.Yaml, "v: |0\n  invalid\n")]
    [InlineData(StructureFormat.Yaml, "v:\n\tbad: true\n")]
    [InlineData(StructureFormat.Yaml, "v: *\n")]
    [InlineData(StructureFormat.Neon, "v: Service(1,2\n")]
    [InlineData(StructureFormat.Neon, "v: \"\\q\"\n")]
    [InlineData(StructureFormat.Neon, "v: '''\n  unclosed\n")]
    public void Malformed_configuration_reports_a_source_range(StructureFormat format, string text)
    {
        var error = Assert.Throws<StructureSyntaxException>(() => Parse(text, format));
        Assert.InRange(error.Issue.Start, 0, Encoding.UTF8.GetByteCount(text));
        Assert.InRange(error.Issue.End, error.Issue.Start, Encoding.UTF8.GetByteCount(text));
        Assert.NotEmpty(error.Message);
    }

    [Theory]
    [InlineData(StructureFormat.Yaml)]
    [InlineData(StructureFormat.Neon)]
    public void Duplicate_keys_use_the_bounded_disk_analysis(StructureFormat format)
    {
        using var doc = Doc.FromBytes(Encoding.UTF8.GetBytes("key: 1\n'key': 2\n"), ReferenceBackendFactory.Instance,
            new DocumentOptions { Format = TextCodec.Utf8 });
        using var analysis = StructureAnalysis.Parse(doc.GetSnapshot(), doc.BomLength, format, default);
        Assert.False(analysis.IsValid);
        Assert.Equal(1, analysis.DuplicateKeys);
        Assert.Contains(analysis.Index.Issues, x => x.Kind == StructureIssueKind.DuplicateKey);
    }

    [Fact]
    public void Neon_mixed_collections_update_disk_index_type_counts_without_a_prescan()
    {
        using var doc = Doc.FromBytes(Encoding.UTF8.GetBytes("- first\nname: second\n- third\n"), ReferenceBackendFactory.Instance,
            new DocumentOptions { Format = TextCodec.Utf8 });
        using var analysis = StructureAnalysis.Parse(doc.GetSnapshot(), doc.BomLength, StructureFormat.Neon, default);
        Assert.True(analysis.IsValid);
        Assert.Equal(StructureKind.JsonObject, analysis.Index.ReadNode(1).Kind);
        Assert.Equal(1, analysis.Index.CountOf(StructureKind.JsonObject));
        Assert.Equal(0, analysis.Index.CountOf(StructureKind.JsonArray));
        Assert.Equal(3, analysis.Index.ReadNode(1).ChildCount);
    }

    [Theory]
    [InlineData("{5: Cat, Dog}", 6)]
    [InlineData("{name: first, second}", 0)]
    [InlineData("{-5: Cat, Dog}", -4)]
    [InlineData("{'5': Cat, Dog}", 6)]
    [InlineData("{'05': Cat, Dog}", 0)]
    [InlineData("{'+5': Cat, Dog}", 0)]
    [InlineData("{'-0': Cat, Dog}", 0)]
    [InlineData("{+005: Cat, Dog}", 6)]
    [InlineData("{-0: Cat, Dog}", 1)]
    [InlineData("{0x10: Cat, Dog}", 17)]
    [InlineData("{1e1: Cat, Dog}", 11)]
    [InlineData("{1.5: Cat, Dog}", 0)]
    [InlineData("{-0.0: Cat, Dog}", 0)]
    [InlineData("{9223372036854775808: Cat, Dog}", 0)]
    [InlineData("{-9223372036854775808: Cat, Dog}", -9223372036854775807)]
    public void Neon_implicit_keys_follow_the_greatest_integer_key_not_the_row_number(string body, long key)
    {
        var index = Parse("items: " + body, StructureFormat.Neon);
        var child = index.Nodes.Single(n => n.ParentId == Node(index, "$.items").Id && n.Ordinal == 1);
        Assert.Equal(key, child.IntegerKey);
        Assert.Equal(1, child.Ordinal);
        string path = "$.items[" + (key < 0 ? "\"" + key + "\"" : key.ToString(System.Globalization.CultureInfo.InvariantCulture)) + "]";
        Assert.Equal(child.Id, Node(index, path).Id);
        Assert.Equal(child.Id, Node(index, StructurePaths.Create(index, child)).Id);
        Assert.Equal(key.ToString(System.Globalization.CultureInfo.InvariantCulture), StructureText.ReadName(index, child).Text);
    }

    [Fact]
    public void Neon_integer_keys_round_trip_through_disk_pages_and_keep_physical_ordinals()
    {
        const string text = "- first\n5: named\n- last\n";
        using var doc = Doc.FromBytes(Encoding.UTF8.GetBytes(text), ReferenceBackendFactory.Instance,
            new DocumentOptions { Format = TextCodec.Utf8 });
        using var analysis = StructureAnalysis.Parse(doc.GetSnapshot(), doc.BomLength, StructureFormat.Neon, default);
        Assert.True(analysis.IsValid);
        var children = analysis.Index.ReadChildren(1, -1, 64, default);
        Assert.Equal(new long?[] { 0, 5, 6 }, children.Select(n => n.IntegerKey));
        Assert.Equal(new long[] { 0, 1, 2 }, children.Select(n => n.Ordinal));
        Assert.All(children, n => Assert.Equal(-1, n.NamespaceId));
        Assert.Equal("$[6]", StructurePaths.Create(analysis.Index, children[2]));
        Assert.Equal("last", StructureText.ReadSource(analysis.Index, children[2]).Text);
        Assert.InRange(analysis.Index.CachedBytes, 1, 4 * 1024 * 1024);
    }

    [Theory]
    [InlineData("{5: one, -10: two, three}", -10, 6)]
    [InlineData("{-5: one, -10: two, three}", -10, -4)]
    public void Negative_explicit_keys_do_not_replace_a_greater_existing_key(string body, long explicitKey, long implicitKey)
    {
        var index = Parse("items: " + body, StructureFormat.Neon);
        var parent = Node(index, "$.items");
        Assert.Equal(explicitKey, index.Nodes.Single(n => n.ParentId == parent.Id && n.Ordinal == 1).IntegerKey);
        Assert.Equal(implicitKey, index.Nodes.Single(n => n.ParentId == parent.Id && n.Ordinal == 2).IntegerKey);
    }

    [Fact]
    public void Neon_explicit_quoted_integer_keys_are_semantic_but_leading_zero_strings_stay_strings()
    {
        var index = Parse("items: {'5': first, '05': second, '+5': third}\n", StructureFormat.Neon);
        Assert.Equal("first", Value(index, "$.items[5]"));
        Assert.Equal("second", Value(index, "$.items['05']"));
        Assert.Equal("third", Value(index, "$.items['+5']"));
        Assert.Equal(5, Node(index, "$.items[5]").IntegerKey);
        Assert.Null(Node(index, "$.items['05']").IntegerKey);
        Assert.Equal("'5': first", StructureText.ReadSource(index, Node(index, "$.items[5]")).Text);
    }

    [Fact]
    public void Neon_implicit_key_overflow_is_a_syntax_error()
    {
        Assert.Throws<StructureSyntaxException>(() => Parse("items: {9223372036854775807: first, next}", StructureFormat.Neon));
    }

    [Theory]
    [InlineData("v: *missing\n")]
    [InlineData("v: *later\na: &later 1\n")]
    [InlineData("v: '*missing'\na: *missing\n")]
    [InlineData("# &missing\nv: *missing\n")]
    public void Undefined_and_forward_yaml_aliases_are_invalid(string text)
    {
        using var doc = Doc.FromBytes(Encoding.UTF8.GetBytes(text), ReferenceBackendFactory.Instance,
            new DocumentOptions { Format = TextCodec.Utf8 });
        using var analysis = StructureAnalysis.Parse(doc.GetSnapshot(), doc.BomLength, StructureFormat.Yaml, default);
        Assert.False(analysis.IsValid);
        Assert.Contains(analysis.Index.Issues, i => i.Kind == StructureIssueKind.Syntax && i.Message.Contains("preceding anchor"));
    }

    [Theory]
    [InlineData("a: &base 1\nb: *base\nc: *base\n")]
    [InlineData("a: &loop {self: *loop}\n")]
    [InlineData("a: &base 1\nb: &base 2\nc: *base\n")]
    [InlineData("a: &větev 1\nb: *větev\n")]
    public void Prior_yaml_anchors_allow_reuse_shadowing_and_symbolic_cycles(string text)
    {
        using var doc = Doc.FromBytes(Encoding.UTF8.GetBytes(text), ReferenceBackendFactory.Instance,
            new DocumentOptions { Format = TextCodec.Utf8 });
        using var analysis = StructureAnalysis.Parse(doc.GetSnapshot(), doc.BomLength, StructureFormat.Yaml, default);
        Assert.True(analysis.IsValid);
    }

    [Fact]
    public void Yaml_anchor_registry_keeps_fixed_memory_as_definitions_grow_on_disk()
    {
        const int count = 50_000, width = 8;
        string text = string.Concat(Enumerable.Range(0, count).Select(i => i.ToString("D8", System.Globalization.CultureInfo.InvariantCulture))) + "missing!";
        var source = new ArraySnapshot(Encoding.UTF8.GetBytes(text));
        long before = GC.GetAllocatedBytesForCurrentThread();
        using var anchors = new YamlAnchors(source, default);
        long retained = anchors.RetainedBytes;
        for (int i = 0; i < count; i++) anchors.Define((long)i * width, (long)(i + 1) * width);
        Assert.True(anchors.Contains(0, width));
        Assert.True(anchors.Contains((count - 1L) * width, (long)count * width));
        Assert.False(anchors.Contains((long)count * width, (long)(count + 1) * width));
        Assert.Equal(retained, anchors.RetainedBytes);
        Assert.InRange(anchors.RetainedBytes, 1, 1 << 20);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 2L << 20, $"The anchor registry allocated {allocated} bytes for {count} definitions.");
    }

    [Fact]
    public void Cancellation_interrupts_hashing_a_large_yaml_anchor_name()
    {
        using var cancel = new CancellationTokenSource();
        var source = new LongScalar("", 1L << 30, "", cancel);
        using var anchors = new YamlAnchors(source, cancel.Token);
        Assert.Throws<OperationCanceledException>(() => anchors.Define(0, source.ByteLength));
        Assert.InRange(source.BytesRead, 1, 1 << 20);
    }

    [Theory]
    [InlineData(StructureFormat.Yaml, "v: old # keep\nother: 1\n")]
    [InlineData(StructureFormat.Yaml, "v: |\n  old\nother: 1\n")]
    [InlineData(StructureFormat.Neon, "v: 'old' # keep\nother: 1\n")]
    public void Scalar_edits_round_trip_and_preserve_other_source_bytes(StructureFormat format, string source)
    {
        var index = Parse(source, format);
        var node = Node(index, "$.v");
        var edit = StructureEdits.CreateEdit(index, node, "new\n😀", default);
        var bytes = Encoding.UTF8.GetBytes(source);
        byte[] changed = [.. bytes.AsSpan(0, (int)edit.Range.Start), .. edit.Insert.Span, .. bytes.AsSpan((int)edit.Range.End)];
        var parsed = Parse(Encoding.UTF8.GetString(changed), format);
        Assert.Equal("new\n😀", Value(parsed, "$.v"));
        Assert.Equal("1", Value(parsed, "$.other"));
        Assert.Equal("other: 1\n", Encoding.UTF8.GetString(changed)[^9..]);
    }

    [Theory]
    [InlineData(StructureFormat.Yaml)]
    [InlineData(StructureFormat.Neon)]
    public void Empty_values_can_be_edited_without_joining_the_mapping_key(StructureFormat format)
    {
        var index = Parse("v:\nother: 1\n", format);
        var edit = StructureEdits.CreateEdit(index, Node(index, "$.v"), "null", default);
        Assert.Equal(" null", TextCodec.Utf8.GetString(edit.Insert.Span));
    }

    [Theory]
    [InlineData(StructureFormat.Yaml, false)]
    [InlineData(StructureFormat.Yaml, true)]
    [InlineData(StructureFormat.Neon, false)]
    public void Unicode_offsets_work_in_utf16_and_utf32(StructureFormat format, bool utf32)
    {
        var codec = utf32 ? TextCodec.Utf32BEBom : TextCodec.Utf16LEBom;
        const string text = "město: \"Brno😀\"\n";
        var index = Parse(text, format, codec);
        var node = Node(index, "$['město']");
        Assert.Equal("Brno😀", StructureText.ReadValue(index, node).Text);
        Assert.Equal(codec.Bom.Length + codec.Encode("město: ").Length, node.ValueStart);
        Assert.Equal("město: \"Brno😀\"", StructureText.ReadSource(index, node).Text);
    }

    [Fact]
    public void An_explicit_block_indent_inside_a_sequence_uses_the_mapping_key_column()
    {
        var index = Parse("- value: |2-\n      extra\n    base\n", StructureFormat.Yaml);
        Assert.Equal("  extra\nbase", Value(index, "$[0].value"));
    }

    [Theory]
    [InlineData(StructureFormat.Yaml, "v: ", "\n")]
    [InlineData(StructureFormat.Yaml, "v: |\n  ", "\n")]
    [InlineData(StructureFormat.Neon, "v: \"", "\"\n")]
    public void Long_scalars_do_not_allocate_their_source_text(StructureFormat format, string prefix, string suffix)
    {
        var source = new LongScalar(prefix, 8L << 20, suffix);
        var index = new MemoryIndex(source, TextCodec.Utf8, format);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Parse(index, default);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(3, index.Count);
        Assert.True(allocated < 2L << 20, $"The parser allocated {allocated} bytes for an 8 MiB scalar.");
    }

    [Theory]
    [InlineData(StructureFormat.Yaml)]
    [InlineData(StructureFormat.Neon)]
    public void Cancellation_interrupts_a_one_gibibyte_token(StructureFormat format)
    {
        using var cancel = new CancellationTokenSource();
        var source = new LongScalar("v: ", 1L << 30, "\n", cancel);
        var index = new MemoryIndex(source, TextCodec.Utf8, format);
        Assert.Throws<OperationCanceledException>(() => Parse(index, cancel.Token));
        Assert.InRange(source.BytesRead, 1, 1 << 20);
    }

    private static MemoryIndex Parse(string text, StructureFormat format, TextCodec? codec = null)
    {
        codec ??= TextCodec.Utf8;
        var index = new MemoryIndex(new ArraySnapshot([.. codec.Bom, .. codec.Encode(text)]), codec, format);
        Parse(index, default);
        return index;
    }

    private static void Parse(MemoryIndex index, CancellationToken ct)
    {
        var context = new StructureParseContext(index.Source, index.Codec, index.BomLength, index, ct);
        if (index.Format == StructureFormat.Neon) NeonStructureReader.Parse(context);
        else YamlStructureReader.Parse(context);
    }

    private static StructureNode Node(MemoryIndex index, string expression)
    {
        var page = StructureQuery.Parse(expression, StructureQueryLanguage.JsonPath).ReadPage(index, 0, 128, default);
        return index.ReadNode(Assert.Single(page.NodeIds));
    }

    private static string Value(MemoryIndex index, string expression) => StructureText.ReadValue(index, Node(index, expression), 4096).Text;

    private sealed class MemoryIndex(IByteSnapshot source, TextCodec codec, StructureFormat format) : IStructureIndex, IStructureSink
    {
        public List<StructureNode> Nodes { get; } = [];
        public long Count => Nodes.Count;
        public IByteSnapshot Source => source;
        public TextCodec Codec => codec;
        public int BomLength => codec.Bom.Length;
        public StructureFormat Format => format;
        public Guid DocumentId => Guid.Empty;
        public long Revision => 0;
        public StructureNode ReadNode(long id) => Nodes[(int)id];
        public long AddNode(StructureKind kind, long parentId, long start, long nameStart, long nameEnd, long valueStart, long ordinal, int depth)
        {
            long id = Nodes.Count;
            Nodes.Add(new(id, parentId, kind, start, start, nameStart, nameEnd, valueStart, valueStart, ordinal, 0, id, depth));
            return id;
        }
        public void CompleteNode(long id, long valueStart, long valueEnd, long end, long childCount) => Nodes[(int)id] = Nodes[(int)id] with
        { ValueStart = valueStart, ValueEnd = valueEnd, End = end, ChildCount = childCount, LastDescendantId = Nodes.Count - 1 };
        public void SetNamespace(long id, long namespaceId) => throw new InvalidOperationException();
        public void SetIntegerKey(long id, long key) => Nodes[(int)id] = Nodes[(int)id] with { IntegerKey = key };
        public void SetKind(long id, StructureKind kind) => Nodes[(int)id] = Nodes[(int)id] with { Kind = kind };
        public void Report(StructureIssue issue) => throw new InvalidOperationException(issue.Message);
    }

    private sealed class LongScalar(string prefix, long fill, string suffix, CancellationTokenSource? cancel = null) : IByteSnapshot
    {
        private readonly byte[] before = Encoding.UTF8.GetBytes(prefix), after = Encoding.UTF8.GetBytes(suffix);
        public long ByteLength => before.Length + fill + after.Length;
        public long BytesRead { get; private set; }
        public int Read(long offset, Span<byte> destination)
        {
            int count = (int)Math.Min(destination.Length, ByteLength - offset);
            destination[..count].Fill((byte)'x');
            for (int i = 0; i < count; i++)
            {
                long at = offset + i;
                if (at < before.Length) destination[i] = before[(int)at];
                else if (at >= before.Length + fill) destination[i] = after[(int)(at - before.Length - fill)];
            }
            BytesRead += count;
            if (BytesRead >= 128 * 1024) cancel?.Cancel();
            return count;
        }
    }
}
