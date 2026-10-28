using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex.Ksy;

namespace NksHub.Textida.Core.Tests;

/// <summary>P8.10.4: the expression subset, and templates that use it (switch-on, repeat-until, if, instances) read from bytes, with hostile sizes and unsupported constructs.</summary>
public class KsyExprTests
{
    private sealed class Host(Dictionary<string, object?>? names = null, long budget = long.MaxValue) : IKsyExprHost
    {
        private long _ticks;

        public void Tick()
        {
            if (++_ticks > budget) throw new KsyExprException(KsyErrorKind.LimitWork, "");
        }

        public object? Name(string name) =>
            names is not null && names.TryGetValue(name, out var v) ? v : throw new KsyExprException(KsyErrorKind.Expression, name);

        public long EnumValue(string enumName, string member) =>
            enumName == "k" && member == "two" ? 2 : throw new KsyExprException(KsyErrorKind.Expression, enumName + "::" + member);

        public object? Member(object? target, string name) => throw new KsyExprException(KsyErrorKind.Expression, name);
    }

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

    private static KsyExpr Compile(string text)
    {
        Assert.True(KsyExpr.TryParse(text, out var e, out var error), error?.Reason);
        return e!;
    }

    private static object? Eval(string text, Dictionary<string, object?>? names = null) => Compile(text).Eval(new Host(names));

    private static KsyTemplate Parse(string text)
    {
        Assert.True(KsyParser.TryParse(text, out var t, out var e), e is null ? "" : $"{e.Kind} line {e.Line} {e.Subject}");
        return t!;
    }

    private static IReadOnlyList<KsyNode> Read(string template, byte[] data, KsyLimits? limits = null) =>
        new KsyReader(new ArraySnapshot(data), Parse(template), limits).Root.Children;

    // ---- the expression parser and evaluator ----

    [Theory]
    [InlineData("1 + 2 * 3", 7L)]
    [InlineData("(1 + 2) * 3", 9L)]
    [InlineData("7 - 10", -3L)]
    [InlineData("7 / 2", 3L)]
    [InlineData("7 % 4", 3L)]
    [InlineData("-7 % 4", 1L)]
    [InlineData("1 << 4", 16L)]
    [InlineData("256 >> 4", 16L)]
    [InlineData("0xF0 & 0x3C", 0x30L)]
    [InlineData("0xF0 | 0x0F", 0xFFL)]
    [InlineData("6 ^ 3", 5L)]
    [InlineData("~0", -1L)]
    [InlineData("-(3)", -3L)]
    [InlineData("0b101", 5L)]
    [InlineData("true ? 1 : 2", 1L)]
    [InlineData("1 > 2 ? 10 : 20", 20L)]
    [InlineData("1 | 2 ^ 3 & 4", 3L)]
    [InlineData("'abc'.length", 3L)]
    [InlineData("'42'.to_i", 42L)]
    [InlineData("true.to_i", 1L)]
    [InlineData("k::two + 1", 3L)]
    [InlineData("len * 2", 10L)]
    [InlineData("len.as<u4> + 1", 6L)]
    [InlineData("items.size", 3L)]
    [InlineData("items[1]", 20L)]
    [InlineData("items.last + items.first", 40L)]
    public void An_integer_expression_is_computed(string text, long expected)
    {
        var names = new Dictionary<string, object?> { ["len"] = 5L, ["items"] = new List<object?> { 10L, 20L, 30L } };
        Assert.Equal(expected, Eval(text, names));
    }

    [Theory]
    [InlineData("1 < 2", true)]
    [InlineData("2 <= 2", true)]
    [InlineData("3 > 4", false)]
    [InlineData("3 >= 4", false)]
    [InlineData("1 == 1", true)]
    [InlineData("1 != 1", false)]
    [InlineData("true and false", false)]
    [InlineData("true or false", true)]
    [InlineData("not false", true)]
    [InlineData("'a' == 'a'", true)]
    [InlineData("'a' < 'b'", true)]
    [InlineData("k::two == 2", true)]
    [InlineData("1 + 1 == 2 and 3 > 2", true)]
    [InlineData("false and 1 / 0 == 0", false)]
    [InlineData("true or 1 / 0 == 0", true)]
    public void A_condition_is_computed_and_and_or_stop_early(string text, bool expected) => Assert.Equal(expected, Eval(text));

    [Fact]
    public void Strings_are_joined_and_unescaped()
    {
        Assert.Equal("abcd", Eval("'ab' + \"cd\""));
        Assert.Equal("a\nb", Eval("'a\\nb'"));
    }

    [Theory]
    [InlineData("1 / 0")]
    [InlineData("5 % 0")]
    [InlineData("1 << 64")]
    [InlineData("9223372036854775807 + 1")]
    [InlineData("-9223372036854775807 - 2")]
    [InlineData("1 + true")]
    [InlineData("not 1")]
    [InlineData("'a' + 1")]
    [InlineData("'a' < 1")]
    [InlineData("items[9]")]
    [InlineData("len[0]")]
    [InlineData("len.nothing")]
    [InlineData("'x'.to_i")]
    public void A_division_by_zero_and_a_wrong_value_are_a_field_error_not_a_crash(string text)
    {
        var names = new Dictionary<string, object?> { ["len"] = 5L, ["items"] = new List<object?> { 1L } };
        var e = Assert.Throws<KsyExprException>(() => Eval(text, names));
        Assert.Equal(KsyErrorKind.Expression, e.Kind);
    }

    [Fact]
    public void A_missing_field_names_the_field_and_a_division_names_the_expression()
    {
        Assert.Equal("missing", Assert.Throws<KsyExprException>(() => Eval("missing + 1")).Subject);
        Assert.Equal("10 / 0", Assert.Throws<KsyExprException>(() => Eval("10 / 0")).Subject);
    }

    [Theory]
    [InlineData("1 +", false)]
    [InlineData("(1", false)]
    [InlineData("1 2", false)]
    [InlineData("a $ b", false)]
    [InlineData("'abc", false)]
    [InlineData("99999999999999999999", false)]
    [InlineData("0x", false)]
    [InlineData("1.5e", false)]
    [InlineData("1.5e+", false)]
    [InlineData("1.5x", false)]
    [InlineData("and", false)]
    [InlineData("a ? b", false)]
    [InlineData("[1, 2]", true)]
    [InlineData("x.reverse()", true)]
    public void A_malformed_or_unsupported_expression_is_refused_with_its_kind(string text, bool unsupported)
    {
        Assert.False(KsyExpr.TryParse(text, out _, out var error));
        Assert.Equal(unsupported, error!.Unsupported);
        Assert.NotEmpty(error.Reason);
    }

    [Fact]
    public void Nesting_and_length_are_bounded_and_the_work_is_counted()
    {
        Assert.False(KsyExpr.TryParse(new string('(', 100) + "1" + new string(')', 100), out _, out var deep));
        Assert.Equal("nested too deeply", deep!.Reason);
        Assert.False(KsyExpr.TryParse(string.Join("+", Enumerable.Repeat("1", 400)), out _, out var tooLong));
        Assert.Equal("too long", tooLong!.Reason);

        var many = Compile(string.Join("+", Enumerable.Repeat("1", 50)));
        Assert.Equal(KsyErrorKind.LimitWork, Assert.Throws<KsyExprException>(() => many.Eval(new Host(budget: 20))).Kind);
    }

    [Fact]
    public void The_names_enums_and_casts_of_an_expression_are_listed()
    {
        var e = Compile("a.b + c.as<u4> + k::two + _parent.d + _io.pos");
        Assert.Equal(["a", "c"], e.Names);
        Assert.Equal([("k", "two")], e.EnumRefs);
        Assert.Equal(["u4"], e.Casts);
        Assert.True(e.UsesContext);
        Assert.False(Compile("1 + k::two").UsesContext);
    }

    // ---- floats ----

    private static readonly Dictionary<string, object?> FloatNames = new() { ["scale"] = 2.5 };

    [Theory]
    [InlineData("1.5 + 1", 2.5)]
    [InlineData("7 / 2.0", 3.5)]
    [InlineData("0.5 * 4", 2.0)]
    [InlineData("10 - 0.25", 9.75)]
    [InlineData("1.5 * 1.5", 2.25)]
    [InlineData("2.5e1", 25.0)]
    [InlineData("2.5E-1", 0.25)]
    [InlineData("-1.5", -1.5)]
    [InlineData("scale * 2", 5.0)]
    [InlineData("true ? 1.5 : 2.5", 1.5)]
    public void A_float_expression_is_computed_in_floats(string text, double expected) =>
        Assert.Equal(expected, Assert.IsType<double>(Eval(text, FloatNames)));

    [Theory]
    [InlineData("1.5 < 2", true)]
    [InlineData("2 > 1.5", true)]
    [InlineData("1.0 == 1", true)]
    [InlineData("1.5 != 1.5", false)]
    [InlineData("2.5 >= 2.5", true)]
    [InlineData("scale == 2.5", true)]
    public void Floats_and_integers_compare_by_value(string text, bool expected) => Assert.Equal(expected, Eval(text, FloatNames));

    [Theory]
    [InlineData("7.9.to_i", 7L)]
    [InlineData("(0.0 - 7.9).to_i", -7L)]
    [InlineData("scale.to_i", 2L)]
    public void A_float_becomes_an_integer_by_to_i(string text, long expected) => Assert.Equal(expected, Eval(text, FloatNames));

    [Theory]
    [InlineData("1.5 % 2")]
    [InlineData("2 % 1.5")]
    [InlineData("1.5 & 1")]
    [InlineData("1.5 << 1")]
    [InlineData("~1.5")]
    [InlineData("not 1.5")]
    [InlineData("1.5 / 0")]
    [InlineData("1 / 0.0")]
    [InlineData("1.0e308 * 10")]
    [InlineData("1.0e19.to_i")]
    [InlineData("1.5 + 'a'")]
    [InlineData("1.5 < 'a'")]
    public void A_float_that_cannot_be_computed_is_a_field_error_not_a_crash(string text) =>
        Assert.Equal(KsyErrorKind.Expression, Assert.Throws<KsyExprException>(() => Eval(text, FloatNames)).Kind);

    [Fact]
    public void The_float_flag_the_constant_and_the_long_enum_name_of_an_expression_are_known()
    {
        Assert.True(Compile("1.5 + a").UsesFloat);
        Assert.False(Compile("1 + a").UsesFloat);
        Assert.True(Compile("2 * 8").TryConstant(out long constant));
        Assert.Equal(16, constant);
        Assert.False(Compile("n + 1").TryConstant(out _));
        Assert.False(Compile("1 / 0").TryConstant(out _));
        Assert.False(Compile("1.5").TryConstant(out _));
        Assert.False(Compile("'a'").TryConstant(out _));
        Assert.Equal([("t::k", "two")], Compile("t::k::two + 1").EnumRefs);
    }

    // ---- templates ----

    private const string Records = """
        meta:
          id: rec
          endian: be
          encoding: ASCII
        seq:
          - id: version
            type: u1
          - id: records
            type: record
            repeat: until
            repeat-until: _.kind == kinds::end
          - id: footer
            type: u2
            if: version >= 2
        types:
          record:
            seq:
              - id: kind
                type: u1
                enum: kinds
              - id: len
                type: u1
                if: kind != kinds::end
              - id: body
                size: len
                if: kind != kinds::end
                type:
                  switch-on: kind
                  cases:
                    kinds::num: num_body
                    kinds::text: text_body
          num_body:
            seq:
              - id: value
                type: u2
          text_body:
            seq:
              - id: text
                type: str
                size-eos: true
        enums:
          kinds:
            0: end
            1: num
            2: text
        """;

    [Fact]
    public void Switch_on_repeat_until_and_if_give_the_tree_of_the_bytes()
    {
        byte[] data = [2, 1, 2, 1, 2, 2, 3, 0x61, 0x62, 0x63, 3, 1, 0xFF, 0, 0xBE, 0xEF];
        var top = Read(Records, data);
        Assert.Equal(["version", "records", "footer"], top.Select(n => n.Name));
        Assert.Equal("record[4]", top[1].TypeName);
        Assert.Equal((1, 13), (top[1].Start, top[1].Length));
        Assert.Equal("48879 (0xBEEF)", top[2].Value);

        var records = top[1].Children;
        Assert.Equal(4, records.Count);
        var first = records[0].Children;
        Assert.Equal(["kind", "len", "body"], first.Select(n => n.Name));
        Assert.Equal("num (1)", first[0].Value);
        Assert.Equal("num_body", first[2].TypeName);
        Assert.Equal((3, 2), (first[2].Start, first[2].Length));
        Assert.Equal("258 (0x0102)", first[2].Children.Single().Value);

        var text = records[1].Children[2];
        Assert.Equal("text_body", text.TypeName);
        Assert.Equal("\"abc\"", text.Children.Single().Value);

        var unknown = records[2].Children;
        Assert.Equal("3", unknown[0].Value);
        Assert.Equal(("bytes", "FF"), (unknown[2].TypeName, unknown[2].Value));

        var end = records[3].Children;
        Assert.Equal(["kind"], end.Select(n => n.Name));
        Assert.Equal("end (0)", end[0].Value);
    }

    [Fact]
    public void A_field_with_a_false_condition_is_not_in_the_tree()
    {
        byte[] data = [1, 0];
        var top = Read(Records, data);
        Assert.Equal(["version", "records"], top.Select(n => n.Name));
        Assert.Equal(2, top[1].End);
    }

    [Fact]
    public void An_index_and_the_parent_are_usable_in_expressions()
    {
        const string template = """
            meta:
              id: idx
            seq:
              - id: base
                type: u1
              - id: chunks
                size: _index + 1
                repeat: expr
                repeat-expr: 3
              - id: pair
                type: item
            types:
              item:
                seq:
                  - id: v
                    type: u1
                  - id: flag
                    type: u1
                    if: v > _parent.base
            """;
        var top = Read(template, [5, 0xAA, 0xBB, 0xBB, 0xCC, 0xCC, 0xCC, 9, 7]);
        Assert.Equal([1L, 2L, 3L], top[1].Children.Select(c => c.Length));
        Assert.Equal(["v", "flag"], top[2].Children.Select(c => c.Name));
        Assert.Equal(2, top[2].Length);

        var small = Read(template, [5, 0xAA, 0xBB, 0xBB, 0xCC, 0xCC, 0xCC, 1]);
        Assert.Equal(["v"], small[2].Children.Select(c => c.Name));
        Assert.Equal(1, small[2].Length);
    }

    [Fact]
    public void A_float_field_is_a_value_in_expressions_and_never_a_size()
    {
        const string template = """
            meta:
              id: fl
              endian: be
            seq:
              - id: ratio
                type: f4
              - id: scaled
                size: (ratio * 2).to_i
              - id: bad
                size: ratio
            """;
        var nodes = Read(template, [0x3F, 0xC0, 0, 0, 1, 2, 3, 9]);
        Assert.Equal(["1.5", "010203"], nodes.Take(2).Select(n => n.Value));
        Assert.Equal((KsyErrorKind.Expression, "ratio"), (nodes[2].Error!.Kind, nodes[2].Error!.Subject));
    }

    private const string Instances = """
        meta:
          id: inst
          endian: le
        seq:
          - id: table_pos
            type: u1
          - id: count
            type: u1
        instances:
          total:
            value: count * 2 + 1
          table:
            pos: table_pos
            type: u1
            repeat: expr
            repeat-expr: count
          last:
            pos: _io.size - 1
            type: u1
        """;

    [Fact]
    public void Instances_follow_the_fields_and_read_at_their_position()
    {
        var nodes = Read(Instances, [4, 2, 0, 0, 10, 20, 99]);
        Assert.Equal(["table_pos", "count", "total", "table", "last"], nodes.Select(n => n.Name));
        Assert.Equal("5", nodes[2].Value);
        Assert.Equal(("u1[2]", 4L, 2L), (nodes[3].TypeName, nodes[3].Start, nodes[3].Length));
        Assert.Equal(["10", "20"], nodes[3].Children.Select(c => c.Value));
        Assert.Equal(("99", 6L), (nodes[4].Value, nodes[4].Start));
    }

    [Fact]
    public void An_instance_past_the_end_is_an_error_on_that_instance_only()
    {
        var nodes = Read(Instances, [200, 2, 1, 1]);
        Assert.Equal(KsyErrorKind.OutOfFile, nodes.Single(n => n.Name == "table").Error!.Kind);
        Assert.Null(nodes.Single(n => n.Name == "last").Error);
    }

    [Fact]
    public void An_instance_that_refers_to_itself_is_an_error_and_a_long_chain_ends_in_the_depth_limit()
    {
        var cycle = Read("meta:\n  id: c\nseq:\n  - id: a\n    type: u1\ninstances:\n  x:\n    value: y + 1\n  y:\n    value: x + 1\n", [1]);
        Assert.Equal([KsyErrorKind.Expression, KsyErrorKind.Expression], cycle.Skip(1).Select(n => n.Error!.Kind));

        var text = new StringBuilder("meta:\n  id: chain\nseq:\n  - id: a\n    type: u1\ninstances:\n");
        for (int i = 100; i >= 1; i--) text.Append($"  i{i}:\n    value: i{i - 1} + 1\n");
        text.Append("  i0:\n    value: 1\n");
        var chain = Read(text.ToString(), [1]);
        Assert.Equal(KsyErrorKind.LimitDepth, chain.Single(n => n.Name == "i100").Error!.Kind);
        Assert.Equal("1", chain.Single(n => n.Name == "i0").Value);
    }

    // ---- hostile data and unsupported constructs ----

    [Fact]
    public void A_hostile_size_is_an_error_node_and_never_a_huge_read_or_a_crash()
    {
        const string size = "meta:\n  id: h\n  endian: be\nseq:\n  - id: len\n    type: u4\n  - id: body\n    size: len * len\n";
        Assert.Equal(KsyErrorKind.Expression, Read(size, [0xFF, 0xFF, 0xFF, 0xFF])[1].Error!.Kind);
        Assert.Equal(KsyErrorKind.OutOfFile, Read(size, [0, 1, 0, 0])[1].Error!.Kind);

        const string count = "meta:\n  id: h\n  endian: be\nseq:\n  - id: len\n    type: u4\n  - id: items\n    type: u1\n    repeat: expr\n    repeat-expr: len * 1000\n";
        Assert.Equal(KsyErrorKind.OutOfFile, Read(count, [0, 0, 0, 100])[1].Error!.Kind);

        const string divide = "meta:\n  id: h\n  endian: be\nseq:\n  - id: len\n    type: u4\n  - id: body\n    size: 10 / len\n";
        var error = Read(divide, [0, 0, 0, 0])[1].Error!;
        Assert.Equal((KsyErrorKind.Expression, "10 / len", 7), (error.Kind, error.Subject, error.Line));

        var root = Read("meta:\n  id: h\nseq:\n  - id: body\n    size: _parent.x\n", [1, 2]);
        Assert.Equal(KsyErrorKind.Expression, root.Single().Error!.Kind);

        var huge = new KsyReader(new Zeros(10L << 30), Parse("meta:\n  id: h\nseq:\n  - id: body\n    size: 1 << 40\n")).Root.Children;
        Assert.Equal(KsyErrorKind.OutOfFile, huge.Single().Error!.Kind);
    }

    [Fact]
    public void A_repeat_until_that_never_ends_stops_at_the_repeat_limit()
    {
        var t = Parse("meta:\n  id: u\nseq:\n  - id: items\n    type: u1\n    repeat: until\n    repeat-until: false\n");
        var node = new KsyReader(new Zeros(1 << 20), t).Root.Children.Single();
        Assert.Equal(KsyErrorKind.LimitRepeat, node.Error!.Kind);
    }

    [Fact]
    public void An_unsupported_expression_is_refused_with_the_line_of_the_template()
    {
        Assert.False(KsyParser.TryParse("meta:\n  id: x\nseq:\n  - id: a\n    type: u1\n  - id: b\n    size: a.to_s(16)\n", out _, out var error));
        Assert.Equal((KsyErrorKind.Unsupported, "expression call .to_s(...)", 7), (error!.Kind, error.Subject, error.Line));
    }
}
