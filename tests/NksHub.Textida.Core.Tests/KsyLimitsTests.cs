using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex.Ksy;

namespace NksHub.Textida.Core.Tests;

/// <summary>P8.10.3: arrays are read lazily and shown a page at a time, elements of one size are found by arithmetic, and a hostile or slow source ends in a clear limit.</summary>
public class KsyLimitsTests
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

    /// <summary>A source that takes its time over every read, like a file on a slow share.</summary>
    private sealed class Slow(IByteSnapshot inner, int milliseconds) : IByteSnapshot
    {
        public long ByteLength => inner.ByteLength;

        public int Read(long offset, Span<byte> destination)
        {
            Thread.Sleep(milliseconds);
            return inner.Read(offset, destination);
        }
    }

    private static KsyTemplate Parse(string text)
    {
        Assert.True(KsyParser.TryParse(text, out var t, out var e), e is null ? "" : $"{e.Kind} line {e.Line} {e.Subject}");
        return t!;
    }

    private const string Untilled = "meta:\n  id: p\nseq:\n  - id: items\n    type: u1\n    repeat: until\n    repeat-until: _ == 0\n";

    // ---- pages ----

    [Fact]
    public void A_long_array_is_shown_in_ranges_of_a_page_and_each_range_holds_the_bytes_of_its_elements()
    {
        byte[] data = [1, 1, 1, 1, 1, 1, 1, 1, 1, 0];
        var items = new KsyReader(new ArraySnapshot(data), Parse(Untilled), new KsyLimits(PageSize: 4)).Root.Children.Single();
        Assert.Equal("u1[10]", items.TypeName);

        var groups = items.Children;
        Assert.Equal(["items[0..3]", "items[4..7]", "items[8..9]"], groups.Select(g => g.Name));
        Assert.Equal([(0L, 4L), (4L, 4L), (8L, 2L)], groups.Select(g => (g.Start, g.Length)));
        Assert.Equal(["items[4]", "items[5]", "items[6]", "items[7]"], groups[1].Children.Select(c => c.Name));
        Assert.Equal(["items[8]", "items[9]"], groups[2].Children.Select(c => c.Name));
        Assert.Equal(["items", "items[4..7]", "items[6]"], items.PathTo(6).Select(n => n.Name));
    }

    [Fact]
    public void An_array_up_to_a_page_is_not_split()
    {
        byte[] data = [1, 1, 1, 0];
        var items = new KsyReader(new ArraySnapshot(data), Parse(Untilled), new KsyLimits(PageSize: 4)).Root.Children.Single();
        Assert.Equal(["items[0]", "items[1]", "items[2]", "items[3]"], items.Children.Select(c => c.Name));
    }

    [Fact]
    public void A_path_to_a_byte_names_the_deepest_field_and_is_empty_outside_the_node()
    {
        var png = KsySamples.Read("png_chunks", KsySamples.Png());
        Assert.Equal(["png_chunks", "chunks", "chunks[2]", "body", "text"], png.PathTo(63).Select(n => n.Name));
        Assert.Equal(["png_chunks", "chunks", "chunks[2]", "body", "keyword"], png.PathTo(57).Select(n => n.Name));
        Assert.Equal(["png_chunks", "chunks", "chunks[4]", "crc"], png.PathTo(98).Select(n => n.Name));
        Assert.Empty(png.PathTo(99));
        Assert.Empty(png.PathTo(-1));
    }

    // ---- elements of one size ----

    private const string Table = """
        meta:
          id: big
          endian: le
        seq:
          - id: items
            type: u2
            repeat: expr
            repeat-expr: 1000000
        instances:
          count:
            value: items.size
          last:
            value: items.last
        """;

    [Fact]
    public void An_array_of_elements_of_one_size_is_not_walked_and_opens_down_to_its_last_element_at_once()
    {
        var nodes = new KsyReader(new Zeros(2_000_000), Parse(Table)).Root.Children;
        var items = nodes[0];
        Assert.Equal(("u2[1000000]", 0L, 2_000_000L), (items.TypeName, items.Start, items.Length));

        var groups = items.Children;
        Assert.Equal(16, groups.Count);
        Assert.Equal("items[0..65535]", groups[0].Name);
        Assert.Equal("items[983040..999999]", groups[^1].Name);
        var last = groups[^1].Children[^1].Children[^1];
        Assert.Equal(("items[999999]", 1_999_998L, 2L), (last.Name, last.Start, last.Length));
        Assert.Equal(["1000000", "0"], nodes.Skip(1).Select(n => n.Value));
    }

    [Fact]
    public void A_count_that_does_not_fit_the_file_is_an_error_over_the_bytes_that_are_there()
    {
        var items = new KsyReader(new Zeros(1_000), Parse(Table)).Root.Children[0];
        Assert.Equal(KsyErrorKind.OutOfFile, items.Error!.Kind);
        Assert.Equal((0, 1_000), (items.Start, items.Length));
    }

    [Fact]
    public void A_binary_mesh_of_five_million_triangles_opens_at_once_and_a_byte_is_found_through_four_levels()
    {
        var stl = new KsySamples.BigStl(5_000_000);
        var root = new KsyReader(stl, KsyCatalog.All.Single(p => p.Id == "stl_binary").Template).Root;
        var triangles = root.Children[2];
        Assert.Equal(("triangle[5000000]", 84L, 250_000_000L), (triangles.TypeName, triangles.Start, triangles.Length));

        long inside = 84 + 50L * 3_999_999 + 20;
        Assert.Equal(["triangles", "triangles[3997696..4063231]", "triangles[3999744..3999999]", "triangles[3999999]", "vertices", "vertices[0]", "z"], triangles.PathTo(inside).Select(n => n.Name));
    }

    [Fact]
    public void A_count_over_the_limit_of_elements_that_differ_in_size_is_a_limit_error_and_never_a_walk_to_the_end()
    {
        var t = Parse("meta:\n  id: v\n  endian: be\nseq:\n  - id: n\n    type: u4\n  - id: items\n    type: item\n    repeat: expr\n    repeat-expr: n\ntypes:\n  item:\n    seq:\n      - id: len\n        type: u1\n      - id: body\n        size: len\n");
        byte[] data = [0, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0];
        Assert.Equal(KsyErrorKind.LimitRepeat, new KsyReader(new ArraySnapshot(data), t).Root.Children[1].Error!.Kind);
    }

    [Fact]
    public void The_size_of_an_element_follows_from_the_template_only_when_the_template_fixes_it()
    {
        var fixedSize = Parse("meta:\n  id: f\n  endian: le\nseq:\n  - id: a\n    type: pair\n    repeat: expr\n    repeat-expr: 3\n  - id: b\n    type: other\n    repeat: eos\ntypes:\n  pair:\n    seq:\n      - id: x\n        type: u2\n      - id: y\n        size: 6\n  other:\n    seq:\n      - id: len\n        type: u1\n      - id: body\n        size: len\n");
        Assert.Equal(8, fixedSize.ElementSize(fixedSize.Root.Seq[0]));
        Assert.Null(fixedSize.ElementSize(fixedSize.Root.Seq[1]));

        var conditional = Parse("meta:\n  id: c\nseq:\n  - id: a\n    type: item\n    repeat: eos\ntypes:\n  item:\n    seq:\n      - id: n\n        type: u1\n      - id: m\n        type: u1\n        if: n == 1\n");
        Assert.Null(conditional.ElementSize(conditional.Root.Seq[0]));

        var recursive = Parse("meta:\n  id: r\nseq:\n  - id: a\n    type: node\n    repeat: eos\ntypes:\n  node:\n    seq:\n      - id: next\n        type: node\n");
        Assert.Null(recursive.ElementSize(recursive.Root.Seq[0]));
    }

    // ---- time ----

    [Fact]
    public void A_slow_source_ends_in_the_time_limit_long_before_the_repeat_limit()
    {
        var t = Parse("meta:\n  id: s\nseq:\n  - id: items\n    type: u1\n    repeat: until\n    repeat-until: false\n");
        var items = new KsyReader(new Slow(new Zeros(1 << 20), 5), t, new KsyLimits(MaxMilliseconds: 60)).Root.Children.Single();
        Assert.Equal(KsyErrorKind.LimitTime, items.Error!.Kind);
    }

    [Fact]
    public void Without_a_time_limit_the_same_source_runs_into_the_repeat_limit()
    {
        var t = Parse("meta:\n  id: s\nseq:\n  - id: items\n    type: u1\n    repeat: until\n    repeat-until: false\n");
        var items = new KsyReader(new Zeros(1 << 20), t, new KsyLimits(MaxMilliseconds: 0, MaxRepeat: 100)).Root.Children.Single();
        Assert.Equal(KsyErrorKind.LimitRepeat, items.Error!.Kind);
    }
}
