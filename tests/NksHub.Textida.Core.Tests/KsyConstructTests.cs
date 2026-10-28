using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex.Ksy;

namespace NksHub.Textida.Core.Tests;

/// <summary>P8.10.2: every construct of the subset has a template with the tree it reads into, and what is not in the subset is refused with its name and line.</summary>
public class KsyConstructTests
{
    private static string Lines(string text) => text.ReplaceLineEndings("\n").TrimEnd('\n');

    private static KsyError Refuse(string text)
    {
        Assert.False(KsyParser.TryParse(text, out _, out var e));
        return e!;
    }

    private static KsyTemplate Parse(string text)
    {
        Assert.True(KsyParser.TryParse(text, out var t, out var e), e is null ? "" : $"{e.Kind} line {e.Line} {e.Subject}");
        return t!;
    }

    private static IReadOnlyList<KsyNode> Read(string template, byte[] data) => new KsyReader(new ArraySnapshot(data), Parse(template)).Root.Children;

    public static IEnumerable<object[]> SampleNames() => KsyConstructSamples.All.Select(s => new object[] { s.Name });

    [Theory, MemberData(nameof(SampleNames))]
    public void A_sample_template_reads_into_the_expected_tree(string name)
    {
        var sample = KsyConstructSamples.All.Single(s => s.Name == name);
        Assert.Equal(Lines(KsyConstructSamples.Expected[name]), Lines(KsyConstructSamples.Describe(sample)));
    }

    [Fact]
    public void Every_sample_has_an_expected_tree_and_every_expected_tree_a_sample()
    {
        Assert.Equal(KsyConstructSamples.All.Select(s => s.Name).Order(), KsyConstructSamples.Expected.Keys.Order());
        Assert.Equal(KsyConstructSamples.All.Count, KsyConstructSamples.All.Select(s => s.Name).Distinct().Count());
    }

    [Fact]
    public void Every_construct_of_the_subset_is_used_by_a_sample_and_no_sample_uses_another()
    {
        var used = KsyConstructSamples.All.SelectMany(s => Parse(s.Ksy).Constructs).ToHashSet();
        var unused = KsyParser.Constructs.Where(c => !used.Contains(c)).ToList();
        Assert.True(unused.Count == 0, "no sample uses: " + string.Join(", ", unused));
        var unknown = used.Where(c => !KsyParser.Constructs.Contains(c)).ToList();
        Assert.True(unknown.Count == 0, "not in the list of constructs: " + string.Join(", ", unknown));
    }

    // ---- bit-sized fields ----

    [Theory]
    [InlineData("type: b4\n    repeat: expr\n    repeat-expr: 2", KsyErrorKind.Unsupported, "repeat of a bit-sized type")]
    [InlineData("type: b0", KsyErrorKind.UnknownType, "b0")]
    [InlineData("type: b65", KsyErrorKind.UnknownType, "b65")]
    [InlineData("type: b4\n    size: 1", KsyErrorKind.Invalid, "size")]
    [InlineData("type: b1\n    enum: kinds", KsyErrorKind.Invalid, "enum")]
    [InlineData("type:\n      switch-on: len\n      cases:\n        1: b4", KsyErrorKind.Unsupported, "switch-on/bit-sized type")]
    public void A_bit_sized_field_is_refused_where_the_subset_has_no_meaning_for_it(string field, KsyErrorKind kind, string subject)
    {
        var e = Refuse($"meta:\n  id: x\nseq:\n  - id: len\n    type: u1\n  - id: a\n    {field}\n");
        Assert.Equal((kind, subject), (e.Kind, e.Subject));
        Assert.True(e.Line >= 6, e.Line.ToString());
    }

    [Fact]
    public void A_bit_field_past_the_end_is_an_error_node_over_the_bytes_that_are_there()
    {
        var nodes = Read("meta:\n  id: x\nseq:\n  - id: a\n    type: b12\n", [0xFF]);
        Assert.Equal(KsyErrorKind.OutOfFile, nodes.Single().Error!.Kind);
        Assert.Equal((0, 1), (nodes[0].Start, nodes[0].Length));
    }

    [Fact]
    public void A_bit_field_with_its_own_order_and_an_enum_is_read()
    {
        var nodes = Read("meta:\n  id: x\n  bit-endian: le\nseq:\n  - id: a\n    type: b4be\n    enum: kinds\nenums:\n  kinds:\n    10: ten\n", [0xA5]);
        Assert.Equal("ten (10)", nodes.Single().Value);
    }

    // ---- enums inside types ----

    private const string Nested = """
        meta:
          id: ne
        seq:
          - id: item
            type: inner
          - id: other
            type: u1
            enum: inner::kinds
        types:
          inner:
            enums:
              kinds:
                1: one
                2: two
            seq:
              - id: kind
                type: u1
                enum: kinds
              - id: mark
                type: u1
                if: kind == kinds::two
              - id: outer_mark
                type: u1
                if: kind == inner::kinds::one
        """;

    [Fact]
    public void An_enum_of_a_type_is_found_by_its_short_name_inside_the_type_and_by_the_long_name_outside()
    {
        var t = Parse(Nested);
        Assert.Equal(["inner::kinds"], t.Enums.Keys);
        var top = new KsyReader(new ArraySnapshot([2, 9, 1]), t).Root.Children;
        Assert.Equal("two (2)", top[0].Children[0].Value);
        Assert.Equal(["kind", "mark"], top[0].Children.Select(c => c.Name));
        Assert.Equal("one (1)", top[1].Value);
    }

    [Theory]
    [InlineData("enum: nothing")]
    [InlineData("enum: inner::nothing")]
    public void An_enum_that_is_not_there_is_refused_by_name(string field)
    {
        var e = Refuse($"meta:\n  id: x\nseq:\n  - id: a\n    type: u1\n    {field}\ntypes:\n  inner:\n    enums:\n      kinds:\n        1: one\n    seq:\n      - id: b\n        type: u1\n");
        Assert.Equal((KsyErrorKind.UnknownEnum, 6), (e.Kind, e.Line));
    }

    // ---- endian by the data ----

    [Theory]
    [InlineData("    cases:\n      1: middle", KsyErrorKind.Invalid, "endian")]
    [InlineData("    cases:\n      1: le\n    terminator: 0", KsyErrorKind.Unsupported, "endian/terminator")]
    [InlineData("    cases:\n      sel: le", KsyErrorKind.Invalid, "cases")]
    public void An_endian_switch_with_a_wrong_case_is_refused(string cases, KsyErrorKind kind, string subject)
    {
        var e = Refuse($"meta:\n  id: x\n  endian:\n    switch-on: sel\n{cases}\nseq:\n  - id: sel\n    type: u1\n  - id: n\n    type: u2\n");
        Assert.Equal((kind, subject), (e.Kind, e.Subject));
    }

    [Fact]
    public void A_value_that_no_case_names_is_an_error_on_the_first_number_it_decides()
    {
        var nodes = Read("meta:\n  id: x\n  endian:\n    switch-on: sel\n    cases:\n      1: le\nseq:\n  - id: sel\n    type: u1\n  - id: n\n    type: u2\n", [9, 1, 2]);
        Assert.Equal(["sel", "n"], nodes.Select(n => n.Name));
        Assert.Equal((KsyErrorKind.Expression, "sel"), (nodes[1].Error!.Kind, nodes[1].Error!.Subject));
    }

    // ---- a switch with no default branch ----

    [Fact]
    public void A_switch_with_no_branch_for_the_value_is_raw_bytes_with_a_size_and_an_error_without()
    {
        const string head = "meta:\n  id: x\nseq:\n  - id: kind\n    type: u1\n  - id: body\n";
        const string cases = "    type:\n      switch-on: kind\n      cases:\n        1: u1\n";
        var sized = Read(head + cases + "    size: 2\n", [7, 8, 9]);
        Assert.Equal(("bytes", "0809"), (sized[1].TypeName, sized[1].Value));
        var unsized = Read(head + cases, [7, 8, 9]);
        Assert.Equal((KsyErrorKind.Expression, "kind"), (unsized[1].Error!.Kind, unsized[1].Error!.Subject));
    }

    // ---- what the subset does not have ----

    [Theory]
    [InlineData("size: 4\n    process: xor(0x55)", "process")]
    [InlineData("size: 4\n    process: zlib", "process")]
    [InlineData("size: 4\n    terminator: 0", "terminator")]
    [InlineData("type: u1\n    valid: 1", "valid")]
    public void A_construct_outside_the_subset_is_refused_by_its_name_and_line(string field, string subject)
    {
        var e = Refuse($"meta:\n  id: x\nseq:\n  - id: a\n    {field}\n");
        Assert.Equal((KsyErrorKind.Unsupported, subject), (e.Kind, e.Subject));
        Assert.True(e.Line >= 5, e.Line.ToString());
    }

    [Fact]
    public void Process_names_its_line_exactly()
    {
        var e = Refuse("meta:\n  id: x\nseq:\n  - id: a\n    size: 4\n    process: xor(0x55)\n");
        Assert.Equal((KsyErrorKind.Unsupported, "process", 6), (e.Kind, e.Subject, e.Line));
    }

    [Fact]
    public void A_type_inside_a_type_is_refused_by_name()
    {
        var nested = Refuse("meta:\n  id: x\nseq:\n  - id: a\n    type: inner\ntypes:\n  inner:\n    seq:\n      - id: b\n        type: u1\n    types:\n      deeper:\n        seq: []\n");
        Assert.Equal((KsyErrorKind.Unsupported, "types", 11), (nested.Kind, nested.Subject, nested.Line));
    }

    [Fact]
    public void A_control_character_in_a_string_is_written_as_an_escape()
    {
        var nodes = Read("meta:\n  id: x\n  encoding: ASCII\nseq:\n  - id: s\n    type: str\n    size: 4\n", [(byte)'a', 0, (byte)'\n', 0x7F]);
        Assert.Equal("\"a\\0\\n\\x7F\"", nodes.Single().Value);
    }

    [Fact]
    public void An_instance_with_io_needs_a_pos()
    {
        var e = Refuse("meta:\n  id: x\nseq:\n  - id: a\n    type: u1\ninstances:\n  at:\n    io: _root._io\n    value: 1\n");
        Assert.Equal(KsyErrorKind.Invalid, e.Kind);
    }
}
