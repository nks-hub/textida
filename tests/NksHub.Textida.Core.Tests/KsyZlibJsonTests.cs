using System.IO.Compression;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex.Ksy;

namespace NksHub.Textida.Core.Tests;

/// <summary>PZ.9.1 (part): <c>process: zlib</c> in a template and the export of the tree to JSON.</summary>
public sealed class KsyZlibJsonTests
{
    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data);
        return output.ToArray();
    }

    private static KsyNode Root(string template, byte[] data)
    {
        Assert.True(KsyParser.TryParse(template, out var t, out var e), e is null ? "" : $"{e.Kind} {e.Subject}");
        return new KsyReader(new ArraySnapshot(data), t!).Root;
    }

    private const string Head = "meta:\n  id: z\n  encoding: UTF-8\nseq:\n  - id: len\n    type: u1\n";

    [Fact]
    public void A_string_is_decoded_after_inflating_and_its_node_covers_the_packed_bytes_of_the_file()
    {
        var packed = Deflate("héllo zlib"u8.ToArray());
        byte[] file = [(byte)packed.Length, .. packed, 7];
        var nodes = Root(Head + "  - id: text\n    type: str\n    size: len\n    process: zlib\n  - id: after\n    type: u1\n", file).Children;

        Assert.Equal([("text", "\"héllo zlib\"", 1L, (long)packed.Length), ("after", "7", 1L + packed.Length, 1L)], nodes.Skip(1).Select(n => (n.Name, n.Value, n.Start, n.Length)));
    }

    [Fact]
    public void Raw_bytes_show_the_first_inflated_bytes_and_a_damaged_stream_is_an_error_of_the_field()
    {
        var packed = Deflate(Enumerable.Range(0, 100).Select(i => (byte)i).ToArray());
        var good = Root(Head + "  - id: body\n    size: len\n    process: zlib\n", [(byte)packed.Length, .. packed]).Children;
        Assert.Equal("000102030405060708090A0B0C0D0E0F …", good[1].Value);

        byte[] damaged = [(byte)packed.Length, .. packed.Select(b => (byte)(b ^ 0x5A))];
        var bad = Root(Head + "  - id: body\n    size: len\n    process: zlib\n", damaged).Children[1];
        Assert.Equal((KsyErrorKind.Invalid, "process zlib"), (bad.Error!.Kind, bad.Error.Subject));
    }

    [Fact]
    public void A_string_that_inflates_past_the_limit_is_refused()
    {
        var packed = Deflate(new byte[200_000].Select(_ => (byte)'a').ToArray()); // a few hundred bytes of file, 200 KB of text
        var node = Root("meta:\n  id: z\n  encoding: ASCII\nseq:\n  - id: text\n    type: str\n    size-eos: true\n    process: zlib\n", packed).Children.Single();
        Assert.Equal((KsyErrorKind.LimitSize, "process zlib"), (node.Error!.Kind, node.Error.Subject));
    }

    [Fact]
    public void The_tree_is_written_as_JSON_with_places_values_errors_and_children()
    {
        var packed = Deflate("abc"u8.ToArray());
        const string template = "meta:\n  id: z\n  encoding: ASCII\nseq:\n  - id: len\n    type: u1\n  - id: inner\n    type: part\n  - id: packed\n    size: len\n    process: zlib\n  - id: missing\n    size: 100\ntypes:\n  part:\n    seq:\n      - id: a\n        type: u1\n";
        var root = Root(template, [(byte)packed.Length, 5, .. packed]);

        string json = KsyJson.Write(root, out bool truncated);

        Assert.False(truncated);
        using var document = JsonDocument.Parse(json);
        var top = document.RootElement;
        Assert.Equal("z", top.GetProperty("name").GetString());
        Assert.Equal(packed.Length + 2, top.GetProperty("length").GetInt32());
        var children = top.GetProperty("children").EnumerateArray().ToList();
        Assert.Equal(["len", "inner", "packed", "missing"], children.Select(c => c.GetProperty("name").GetString()));
        Assert.Equal("5", children[1].GetProperty("children")[0].GetProperty("value").GetString());
        Assert.Equal(1, children[2].GetProperty("start").GetInt32() - 1); // after len and the one byte of inner
        Assert.Equal("OutOfFile", children[3].GetProperty("error").GetProperty("kind").GetString());
    }

    [Fact]
    public void A_tree_with_too_many_nodes_is_cut_and_says_so_and_is_still_valid_JSON()
    {
        const string template = "meta:\n  id: big\nseq:\n  - id: items\n    type: rec\n    repeat: expr\n    repeat-expr: 10000\ntypes:\n  rec:\n    seq:\n      - id: a\n        type: u1\n      - id: b\n        type: u1\n      - id: c\n        type: u1\n      - id: d\n        type: u1\n      - id: e\n        type: u1\n      - id: f\n        type: u1\n";
        var root = Root(template, new byte[60_000]);

        string json = KsyJson.Write(root, out bool truncated);

        Assert.True(truncated);
        Assert.Contains("\"truncated\": true", json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("big", document.RootElement.GetProperty("name").GetString());
    }
}
