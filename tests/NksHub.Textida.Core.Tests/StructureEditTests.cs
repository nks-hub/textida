using System.Text.Json;
using System.Xml.Linq;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class StructureEditTests
{
    [Theory]
    [InlineData("utf8", false)]
    [InlineData("utf16le", false)]
    [InlineData("utf16be", false)]
    [InlineData("utf8", true)]
    [InlineData("utf16le", true)]
    public void Json_string_edits_preserve_keys_comments_other_bytes_and_one_undo(string encoding, bool jsonc)
    {
        string source = jsonc
            ? "{\r\n/* keep */ \"na\\u006de\" : \"old\", \"other\" : \"\\u0061\", // keep too\r\n}\n"
            : "{\r\n \"na\\u006de\" : \"old\", \"other\" : \"\\u0061\"\r\n}\n";
        using var fixture = new Fixture(source, jsonc ? StructureFormat.Jsonc : StructureFormat.Json, Codec(encoding));
        var node = fixture.First(StructureKind.JsonString);
        const string input = " \r\n\t\"\\ & < é😀 ";
        fixture.ApplyAndUndo(node, input, node.ValueRange, raw =>
        {
            using var parsed = JsonDocument.Parse(raw, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = jsonc });
            Assert.Equal(input, parsed.RootElement.GetProperty("name").GetString());
            Assert.Equal("a", parsed.RootElement.GetProperty("other").GetString());
            Assert.Contains("\"na\\u006de\" : ", raw, StringComparison.Ordinal);
            Assert.Contains("\"other\" : \"\\u0061\"", raw, StringComparison.Ordinal);
            if (jsonc) Assert.Contains("/* keep */", raw, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("0", "-0")]
    [InlineData("0", "-12.50E+03")]
    [InlineData("0", "1e100000")]
    [InlineData("true", "false")]
    [InlineData("false", "true")]
    [InlineData("null", "null")]
    public void Json_literals_are_validated_by_grammar_without_rounding_or_reformatting(string oldValue, string input)
    {
        using var fixture = new Fixture("{\"value\" : " + oldValue + ", \"keep\" : 4}", StructureFormat.Json, TextCodec.Utf8);
        var node = fixture.Index.ReadNode(2);
        var edit = StructureEdits.CreateEdit(fixture.Index, node, input, CancellationToken.None);
        Assert.Equal(node.ValueRange, edit.Range);
        Assert.Equal(input, TextCodec.Utf8.GetString(edit.Insert.Span));
    }

    [Theory]
    [InlineData("0", " 2")]
    [InlineData("0", "2 ")]
    [InlineData("0", "+1")]
    [InlineData("0", "01")]
    [InlineData("0", "-01")]
    [InlineData("0", ".5")]
    [InlineData("0", "1.")]
    [InlineData("0", "1e")]
    [InlineData("0", "1e+")]
    [InlineData("0", "NaN")]
    [InlineData("0", "Infinity")]
    [InlineData("0", "١")]
    [InlineData("0", "1,2")]
    [InlineData("0", "")]
    [InlineData("true", "TRUE")]
    [InlineData("true", " false ")]
    [InlineData("true", "null")]
    [InlineData("null", "NULL")]
    [InlineData("null", " null")]
    public void Invalid_json_editor_input_leaves_the_document_and_undo_untouched(string oldValue, string input)
    {
        using var fixture = new Fixture("[" + oldValue + "]", StructureFormat.Json, TextCodec.Utf8);
        var node = fixture.Index.ReadNode(2);
        var failure = Assert.Throws<StructureSyntaxException>(() => StructureEdits.CreateEdit(fixture.Index, node, input, CancellationToken.None));
        Assert.Equal(StructureIssueKind.Syntax, failure.Issue.Kind);
        fixture.AssertUnchanged();
    }

    [Theory]
    [InlineData("utf8", "'")]
    [InlineData("utf8", "\"")]
    [InlineData("utf16le", "'")]
    [InlineData("utf16be", "\"")]
    public void Xml_attributes_keep_the_original_quote_and_preserve_literal_entities_and_whitespace(string encoding, string quote)
    {
        using var fixture = new Fixture("<r xmlns='urn:a' target = " + quote + "old &amp; value" + quote + " other='unchanged'/>", StructureFormat.Xml, Codec(encoding));
        var node = fixture.First(StructureKind.XmlAttribute);
        const string input = "  &entity; < > ' \"\r\n\té😀  ";
        fixture.ApplyAndUndo(node, input, node.ValueRange, raw =>
        {
            var parsed = XDocument.Parse(raw, LoadOptions.PreserveWhitespace);
            Assert.Equal(input, parsed.Root!.Attribute("target")!.Value);
            Assert.Equal("unchanged", parsed.Root.Attribute("other")!.Value);
            Assert.Equal("urn:a", parsed.Root.Name.NamespaceName);
            Assert.Contains("&amp;entity;", raw, StringComparison.Ordinal);
            Assert.Contains("&#xD;&#xA;&#x9;", raw, StringComparison.Ordinal);
            Assert.Contains(quote == "'" ? "&apos;" : "&quot;", raw, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    public void Xml_element_edit_selects_its_single_text_child_without_touching_attributes_or_names(string encoding)
    {
        using var fixture = new Fixture("<p:r xmlns:p='urn:p' a='unchanged'>old &amp; value</p:r>", StructureFormat.Xml, Codec(encoding));
        var element = fixture.First(StructureKind.XmlElement);
        var text = fixture.First(StructureKind.XmlText);
        const string input = " \t & < > \r\né😀 </p:r><injected/> ";
        fixture.ApplyAndUndo(element, input, text.ValueRange, raw =>
        {
            var parsed = XDocument.Parse(raw, LoadOptions.PreserveWhitespace);
            Assert.Equal(input, parsed.Root!.Value);
            Assert.Empty(parsed.Root.Elements());
            Assert.Equal("unchanged", parsed.Root.Attribute("a")!.Value);
            Assert.Equal(XName.Get("r", "urn:p"), parsed.Root.Name);
            Assert.Contains("&lt;injected/&gt;", raw, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16le")]
    public void Cdata_closing_sequences_are_split_inside_the_old_body_as_one_undo(string encoding)
    {
        using var fixture = new Fixture("<r a='keep'><!--keep--><![CDATA[old]]></r>", StructureFormat.Xml, Codec(encoding));
        var element = fixture.First(StructureKind.XmlElement);
        var body = fixture.First(StructureKind.XmlCData);
        const string input = "  a]]>b & < é😀 ]]> \r\r\n <&>  ";
        fixture.ApplyAndUndo(element, input, body.ValueRange, raw =>
        {
            var parsed = XDocument.Parse(raw, LoadOptions.PreserveWhitespace);
            Assert.Equal(input, parsed.Root!.Value);
            Assert.Contains("a]]]]><![CDATA[>b", raw, StringComparison.Ordinal);
            Assert.Contains("&#xD;&#xD;", raw, StringComparison.Ordinal);
            Assert.Contains("<!--keep-->", raw, StringComparison.Ordinal);
            Assert.Equal("keep", parsed.Root.Attribute("a")!.Value);
        });
    }

    [Theory]
    [InlineData("[]", false, StructureKind.JsonArray)]
    [InlineData("{}", false, StructureKind.JsonObject)]
    [InlineData("<r/>", true, StructureKind.XmlElement)]
    [InlineData("<r><x/></r>", true, StructureKind.XmlElement)]
    [InlineData("<r>one<![CDATA[two]]></r>", true, StructureKind.XmlElement)]
    [InlineData("<r xmlns='urn:a'>value</r>", true, StructureKind.XmlNamespace)]
    [InlineData("<r><!--keep--></r>", true, StructureKind.XmlComment)]
    [InlineData("<r/><?keep value?>", true, StructureKind.XmlProcessingInstruction)]
    public void Containers_mixed_values_namespaces_and_markup_are_not_primitive_edits(string source, bool xml, StructureKind kind)
    {
        using var fixture = new Fixture(source, xml ? StructureFormat.Xml : StructureFormat.Json, TextCodec.Utf8);
        var failure = Assert.Throws<StructureSyntaxException>(() => StructureEdits.CreateEdit(fixture.Index, fixture.First(kind), "new", CancellationToken.None));
        Assert.Equal(StructureIssueKind.UnsupportedSyntax, failure.Issue.Kind);
        fixture.AssertUnchanged();
    }

    [Fact]
    public void Invalid_unicode_xml_characters_and_unrepresentable_input_fail_before_document_mutation()
    {
        using var xml = new Fixture("<r>old</r>", StructureFormat.Xml, TextCodec.Cp1250);
        var node = xml.First(StructureKind.XmlText);
        Assert.Throws<StructureSyntaxException>(() => StructureEdits.CreateEdit(xml.Index, node, "bad\u0001", CancellationToken.None));
        Assert.Throws<StructureSyntaxException>(() => StructureEdits.CreateEdit(xml.Index, node, "bad\uD800", CancellationToken.None));
        Assert.Throws<UnrepresentableCharactersException>(() => StructureEdits.CreateEdit(xml.Index, node, "😀", CancellationToken.None));
        xml.AssertUnchanged();
        using var json = new Fixture("[\"old\"]", StructureFormat.Json, TextCodec.Utf8);
        Assert.Throws<StructureSyntaxException>(() => StructureEdits.CreateEdit(json.Index, json.First(StructureKind.JsonString), "bad\uD800", CancellationToken.None));
        json.AssertUnchanged();
    }

    [Fact]
    public void Input_limit_and_cancellation_reject_the_plan_without_an_undo_step()
    {
        using var fixture = new Fixture("<r>old</r>", StructureFormat.Xml, TextCodec.Utf8);
        var node = fixture.First(StructureKind.XmlText);
        Assert.Throws<ArgumentException>(() => StructureEdits.CreateEdit(fixture.Index, node, new string('a', StructureEdits.MaxInputChars + 1), CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => StructureEdits.CreateEdit(fixture.Index, node, "new", cancellation.Token));
        Assert.Throws<ArgumentException>(() => StructureEdits.CreateEdit(fixture.Index, node with { ValueEnd = node.ValueEnd - 1 }, "new", CancellationToken.None));
        fixture.AssertUnchanged();
    }

    [Fact]
    public void The_largest_new_json_escape_value_fits_the_utf32_output_budget()
    {
        using var fixture = new Fixture("\"old\"", StructureFormat.Json, TextCodec.Utf32LEBom);
        var edit = StructureEdits.CreateEdit(fixture.Index, fixture.First(StructureKind.JsonString), new string('\u0001', StructureEdits.MaxInputChars), CancellationToken.None);
        Assert.Equal((StructureEdits.MaxInputChars * 6 + 2) * 4, edit.Insert.Length);
        Assert.InRange(edit.Insert.Length, 1, 32 * 1024 * 1024);
        Assert.Equal("\"\\u0001", TextCodec.Utf32LE.GetString(edit.Insert.Span[..28]));
        fixture.AssertUnchanged();
    }

    [Fact]
    public void Planning_a_small_replacement_does_not_read_or_materialize_the_old_one_gibibyte_value()
    {
        var node = new StructureNode(0, -1, StructureKind.JsonString, 0, 1L << 30, -1, -1, 0, 1L << 30, 0, 0, 0, 0);
        var index = new UnreadableIndex(node);
        var edit = StructureEdits.CreateEdit(index, node, "new", CancellationToken.None);
        Assert.Equal(node.ValueRange, edit.Range);
        Assert.Equal("\"new\"", TextCodec.Utf8.GetString(edit.Insert.Span));
    }

    private static TextCodec Codec(string encoding) => encoding switch
    {
        "utf8" => TextCodec.Utf8Bom,
        "utf16le" => TextCodec.Utf16LEBom,
        "utf16be" => TextCodec.Utf16BEBom,
        _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
    };

    private sealed class Fixture : IDisposable
    {
        private readonly byte[] original;
        public Doc Document { get; }
        public StructureIndex Index { get; }

        public Fixture(string source, StructureFormat format, TextCodec codec)
        {
            original = [.. codec.Bom, .. codec.Encode(source)];
            Document = Doc.FromBytes(original, ReferenceBackendFactory.Instance, new DocumentOptions { Format = codec });
            var snapshot = Document.GetSnapshot();
            Index = new(snapshot, Document.BomLength, format);
            var context = new StructureParseContext(snapshot, codec, Document.BomLength, Index, CancellationToken.None);
            if (format == StructureFormat.Xml) XmlStructureReader.Parse(context);
            else JsonStructureReader.Parse(context, format == StructureFormat.Jsonc);
        }

        public StructureNode First(StructureKind kind) => Enumerable.Range(0, (int)Index.Count).Select(i => Index.ReadNode(i)).First(node => node.Kind == kind);

        public void ApplyAndUndo(StructureNode node, string input, ByteRange expectedRange, Action<string> check)
        {
            var edit = StructureEdits.CreateEdit(Index, node, input, CancellationToken.None);
            Assert.Equal(expectedRange, edit.Range);
            Assert.Equal(Document.Id, Index.DocumentId);
            Assert.Equal(Document.Revision, Index.Revision);
            Assert.Same(Document.Codec, Index.Codec);
            Assert.True(Document.Apply([edit], new EditOptions { BaseRevision = Index.Revision }).Applied);
            Assert.Equal(1, Document.UndoDepth);
            byte[] after = Document.ReadBytes(new(0, Document.ByteLength));
            Assert.Equal(original.AsSpan(0, (int)edit.Range.Start).ToArray(), after.AsSpan(0, (int)edit.Range.Start).ToArray());
            Assert.Equal(original.AsSpan((int)edit.Range.End).ToArray(), after.AsSpan((int)edit.Range.Start + edit.Insert.Length).ToArray());
            check(Document.Codec.GetString(after.AsSpan(Document.BomLength)));
            var oldBytes = new byte[original.Length];
            Assert.Equal(oldBytes.Length, Index.Source.Read(0, oldBytes));
            Assert.Equal(original, oldBytes);
            Assert.NotNull(Document.Undo());
            Assert.False(Document.CanUndo);
            Assert.Equal(original, Document.ReadBytes(new(0, Document.ByteLength)));
        }

        public void AssertUnchanged()
        {
            Assert.Equal(Index.Revision, Document.Revision);
            Assert.False(Document.CanUndo);
            Assert.Equal(original, Document.ReadBytes(new(0, Document.ByteLength)));
        }

        public void Dispose() { Index.Dispose(); Document.Dispose(); }
    }

    private sealed class UnreadableIndex(StructureNode node) : IStructureIndex, IByteSnapshot
    {
        public long Count => 1;
        public IByteSnapshot Source => this;
        public TextCodec Codec => TextCodec.Utf8;
        public int BomLength => 0;
        public StructureFormat Format => StructureFormat.Json;
        public Guid DocumentId => Guid.Empty;
        public long Revision => 0;
        public long ByteLength => node.End;
        public StructureNode ReadNode(long id) => id == node.Id ? node : throw new ArgumentOutOfRangeException(nameof(id));
        public int Read(long offset, Span<byte> destination) => throw new InvalidOperationException("Planning this edit must not read the old value.");
    }
}
