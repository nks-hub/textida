using System.Buffers.Binary;
using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex.Ksy;

namespace NksHub.Textida.Core.Tests;

/// <summary>P8.10.1 to P8.10.3: the .ksy subset, the tree read lazily from bytes, and the limits against hostile input.</summary>
public class KsyTests
{
    private sealed class Zeros(long length) : IByteSnapshot
    {
        public long ByteLength => length;
        public int Read(long offset, Span<byte> destination)
        {
            int n = (int)Math.Min(destination.Length, Math.Max(0, length - offset));
            destination[..n].Clear();
            return n;
        }
    }

    private static KsyTemplate Parse(string text)
    {
        Assert.True(KsyParser.TryParse(text, out var t, out var e), e is null ? "" : $"{e.Kind} line {e.Line} {e.Subject}");
        return t!;
    }

    private static KsyError Refuse(string text)
    {
        Assert.False(KsyParser.TryParse(text, out _, out var e));
        return e!;
    }

    private static byte[] Chunk(string type, byte[] body)
    {
        var bytes = new byte[12 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)body.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
        body.CopyTo(bytes, 8);
        return bytes;
    }

    private static byte[] Png(params byte[][] chunks) => [.. new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, .. chunks.SelectMany(c => c)];

    // ---- parser and validation ----

    [Fact]
    public void A_template_with_every_supported_construct_parses()
    {
        var t = Parse("""
            meta:
              id: demo
              endian: le
              encoding: UTF-8
              file-extension: [dm, demo]
            seq:
              - id: magic
                contents: [0x44, M]
              - id: count
                type: u2be
              - id: items
                type: item
                repeat: expr
                repeat-expr: count
              - id: tail
                size-eos: true
            types:
              item:
                seq:
                  - id: kind
                    type: u1
                    enum: kinds
                  - id: name
                    type: strz
            enums:
              kinds:
                1: first
                2: second
            """);
        Assert.Equal("demo", t.Id);
        Assert.Equal(["dm", "demo"], t.Extensions);
        Assert.Equal([0x44, (byte)'M'], t.Magic);
        Assert.Equal(KsyRepeat.Expr, t.Root.Seq[2].Repeat);
        Assert.Equal("count", t.Root.Seq[2].RepeatCount!.Source);
        Assert.Equal("second", t.Enums["kinds"][2]);
    }

    [Theory]
    [InlineData("process: xor(1)", "process")]
    [InlineData("size: len * 1.5", "expression float literal")]
    [InlineData("size: '[1, 2].size'", "expression array literal")]
    [InlineData("size: len.substring(0, 1)", "expression call .substring(...)")]
    [InlineData("type:\n      switch-on: len\n      cases:\n        1: u1\n      terminator: 0", "type/terminator")]
    public void An_unsupported_construct_is_refused_by_name_and_line(string field, string subject)
    {
        var e = Refuse($"meta:\n  id: x\nseq:\n  - id: len\n    type: u1\n  - id: a\n    {field}\n");
        Assert.Equal(KsyErrorKind.Unsupported, e.Kind);
        Assert.Equal(subject, e.Subject);
        Assert.True(e.Line >= 6, e.Line.ToString());
    }

    [Fact]
    public void Top_level_params_are_refused_by_name()
    {
        var e = Refuse("meta:\n  id: x\nseq:\n  - id: a\n    type: u1\nparams:\n  - id: p\n    type: u1\n");
        Assert.Equal((KsyErrorKind.Unsupported, "params", 6), (e.Kind, e.Subject, e.Line));
    }

    [Theory]
    [InlineData("meta:\n  id: x\nseq:\n  - id: a\n    type: u2\n", KsyErrorKind.Invalid, "endian", 5)]
    [InlineData("meta:\n  id: x\nseq:\n  - id: a\n    type: nothing\n", KsyErrorKind.UnknownType, "nothing", 5)]
    [InlineData("meta:\n  id: x\nseq:\n  - id: a\n    type: u1\n  - id: a\n    type: u1\n", KsyErrorKind.Duplicate, "a", 6)]
    [InlineData("meta:\n  id: x\nseq:\n  - id: a\n    size: nope\n", KsyErrorKind.UnknownField, "nope", 5)]
    [InlineData("meta:\n  id: x\nseq:\n  - id: a\n    type: u1\n    enum: nope\n", KsyErrorKind.UnknownEnum, "nope", 6)]
    [InlineData("meta:\n  id: x\nseq:\n  - id: a\n    type: str\n    size: 2\n", KsyErrorKind.Invalid, "encoding", 5)]
    [InlineData("meta:\n  id: x\nseq:\n  - id: a\n    type: u1\n    repeat: expr\n", KsyErrorKind.Invalid, "repeat-expr", 6)]
    [InlineData("meta:\n  id: x\nseq: []\n", KsyErrorKind.Invalid, "seq", 1)]
    [InlineData("seq:\n  - id: a\n    type: u1\n", KsyErrorKind.Invalid, "meta", 1)]
    [InlineData("meta:\n  id: x\nseq:\n  - id: a\n    type: u1\n  - id: b\n   type: u1\n", KsyErrorKind.Syntax, "", 7)]
    public void An_invalid_template_is_refused_with_its_line_and_reason(string text, KsyErrorKind kind, string subject, int line)
    {
        var e = Refuse(text);
        Assert.Equal(kind, e.Kind);
        if (subject != "") Assert.Equal(subject, e.Subject);
        Assert.Equal(line, e.Line);
    }

    [Fact]
    public void A_template_over_the_size_limit_is_refused()
    {
        var e = Refuse(new string(' ', KsyParser.MaxTemplateChars + 1));
        Assert.Equal(KsyErrorKind.LimitSize, e.Kind);
    }

    // ---- the tree ----

    [Fact]
    public void A_png_is_read_into_chunks_with_exact_byte_ranges()
    {
        var png = KsyCatalog.All.Single(p => p.Id == "png_chunks").Template;
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, 640);
        var file = Png(Chunk("IHDR", ihdr), Chunk("IDAT", [1, 2, 3]), Chunk("IEND", []));
        var root = new KsyReader(new ArraySnapshot(file), png).Root;

        var top = root.Children;
        Assert.Equal(["signature", "chunks"], top.Select(n => n.Name));
        Assert.Equal((0, 8), (top[0].Start, top[0].Length));
        Assert.Equal((8, file.Length - 8), (top[1].Start, top[1].Length));
        Assert.Equal("chunk[3]", top[1].TypeName);

        var chunks = top[1].Children;
        Assert.Equal(3, chunks.Count);
        Assert.Equal((8, 25), (chunks[0].Start, chunks[0].Length));
        var fields = chunks[0].Children;
        Assert.Equal(["length", "type", "body", "crc"], fields.Select(f => f.Name));
        Assert.Equal("13 (0x0000000D)", fields[0].Value);
        Assert.Equal("\"IHDR\"", fields[1].Value);
        Assert.Equal((16, 13), (fields[2].Start, fields[2].Length));
        Assert.Equal("\"IDAT\"", chunks[1].Children[1].Value);
        Assert.Equal(file.Length, chunks[2].End);
    }

    [Fact]
    public void A_bmp_header_shows_numbers_and_enum_names()
    {
        var bmp = KsyCatalog.All.Single(p => p.Id == "bmp_header").Template;
        var file = new byte[54];
        file[0] = (byte)'B'; file[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(2), 54);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(10), 54);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(18), -2);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(28), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(30), 3);
        var root = new KsyReader(new ArraySnapshot(file), bmp).Root;

        var info = root.Children.Single(n => n.Name == "info");
        Assert.Equal((14, 20), (info.Start, info.Length));
        var f = info.Children.ToDictionary(n => n.Name);
        Assert.Equal("-2", f["width"].Value);
        Assert.Equal("24 (0x0018)", f["bits_per_pixel"].Value);
        Assert.Equal("bitfields (3)", f["compression"].Value);
        Assert.Equal((30, 4), (f["compression"].Start, f["compression"].Length));
    }

    [Fact]
    public void Strings_floats_and_fixed_sizes_are_read()
    {
        var t = Parse("meta:\n  id: m\n  endian: be\n  encoding: ASCII\nseq:\n  - id: s\n    type: str\n    size: 3\n  - id: z\n    type: strz\n  - id: f\n    type: f4\n  - id: sub\n    type: pair\n    size: 4\ntypes:\n  pair:\n    seq:\n      - id: a\n        type: u1\n      - id: rest\n        size-eos: true\n");
        var data = new byte[] { (byte)'a', (byte)'b', (byte)'c', (byte)'h', (byte)'i', 0, 0x3F, 0x80, 0, 0, 7, 8, 9, 10 };
        var nodes = new KsyReader(new ArraySnapshot(data), t).Root.Children;
        Assert.Equal("\"abc\"", nodes[0].Value);
        Assert.Equal(("\"hi\"", 3L), (nodes[1].Value, nodes[1].Length));
        Assert.Equal("1", nodes[2].Value);
        Assert.Equal((10, 4), (nodes[3].Start, nodes[3].Length));
        Assert.Equal(["7", "08090A"], nodes[3].Children.Select(c => c.Value));
    }

    [Fact]
    public void A_wrong_magic_or_a_field_past_the_end_is_an_error_node_and_stops_the_reading()
    {
        var png = KsyCatalog.All.Single(p => p.Id == "png_chunks").Template;
        var bad = new KsyReader(new ArraySnapshot([1, 2, 3, 4, 5, 6, 7, 8, 9]), png).Root.Children;
        Assert.Single(bad);
        Assert.Equal(KsyErrorKind.Contents, bad[0].Error!.Kind);
        Assert.Equal((0, 8), (bad[0].Start, bad[0].Length));

        var cut = Png(Chunk("IDAT", [1, 2, 3, 4, 5]))[..^7];
        var chunks = new KsyReader(new ArraySnapshot(cut), png).Root.Children[1];
        Assert.Equal(KsyErrorKind.OutOfFile, chunks.Error!.Kind);
        Assert.Equal(cut.Length, chunks.End);
    }

    // ---- limits ----

    [Fact]
    public void A_structure_that_contains_itself_ends_in_the_depth_limit()
    {
        var t = Parse("meta:\n  id: loop\nseq:\n  - id: next\n    type: node\ntypes:\n  node:\n    seq:\n      - id: next\n        type: node\n");
        var limits = new KsyLimits(MaxDepth: 8);
        var node = new KsyReader(new Zeros(1 << 20), t, limits).Root;
        var error = node.Children.Single().Error;
        Assert.Equal(KsyErrorKind.LimitDepth, error!.Kind);
    }

    [Fact]
    public void A_huge_repeat_count_is_refused_before_anything_is_read()
    {
        var t = Parse("meta:\n  id: r\n  endian: be\nseq:\n  - id: n\n    type: u4\n  - id: items\n    type: u4\n    repeat: expr\n    repeat-expr: n\n");
        var data = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(data, 0xFFFFFFFF);
        var nodes = new KsyReader(new ArraySnapshot(data), t).Root.Children;
        Assert.Equal(KsyErrorKind.LimitRepeat, nodes[1].Error!.Kind);
    }

    [Fact]
    public void Repeating_to_the_end_of_a_ten_gigabyte_file_stops_at_the_repeat_limit_and_a_length_past_the_end_is_an_error()
    {
        long size = 10L << 30;
        var chunks = Parse("meta:\n  id: c\n  endian: be\nseq:\n  - id: chunks\n    type: chunk\n    repeat: eos\ntypes:\n  chunk:\n    seq:\n      - id: length\n        type: u4\n      - id: body\n        size: length\n");
        var all = new KsyReader(new Zeros(size), chunks).Root.Children;
        Assert.Equal(KsyErrorKind.LimitRepeat, all.Single().Error!.Kind);

        var huge = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(huge, 0xFFFFFFFF);
        var past = new KsyReader(new ArraySnapshot(huge), chunks).Root.Children.Single();
        Assert.Equal(KsyErrorKind.OutOfFile, past.Error!.Kind);
    }

    [Fact]
    public void A_string_over_the_size_limit_and_a_missing_terminator_are_errors()
    {
        var t = Parse("meta:\n  id: s\n  encoding: ASCII\nseq:\n  - id: z\n    type: strz\n");
        var limits = new KsyLimits(MaxStringBytes: 16);
        var node = new KsyReader(new Zeros(0), t, limits).Root.Children.Single();
        Assert.Equal(KsyErrorKind.OutOfFile, node.Error!.Kind);
        var long1 = new byte[100];
        Array.Fill(long1, (byte)'a');
        Assert.Equal(KsyErrorKind.LimitSize, new KsyReader(new ArraySnapshot(long1), t, limits).Root.Children.Single().Error!.Kind);
    }

    [Fact]
    public void The_catalog_finds_a_template_by_magic_then_by_extension()
    {
        Assert.Equal("png_chunks", KsyCatalog.Find("x.bin", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0])?.Id);
        Assert.Equal("bmp_header", KsyCatalog.Find("a.BMP", [])?.Id);
        Assert.Null(KsyCatalog.Find("a.txt", [1, 2, 3]));
    }
}
