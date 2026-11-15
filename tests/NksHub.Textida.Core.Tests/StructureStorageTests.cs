using System.Text;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Tests;

public sealed class StructureStorageTests
{
    private static DocumentSnapshot Snapshot(string text) => new(Guid.NewGuid(), 3, new ArraySnapshot(Encoding.UTF8.GetBytes(text)), TextCodec.Utf8);

    [Fact]
    public void Indexed_children_and_source_containment_survive_cache_eviction()
    {
        const int values = 50_000;
        var source = Snapshot("[" + string.Join(',', Enumerable.Repeat("0", values)) + "]");
        using var index = new StructureIndex(source, 0, StructureFormat.Json);
        JsonStructureReader.Parse(new(source, source.Codec, 0, index, CancellationToken.None));

        Assert.Equal(values + 2, index.Count);
        Assert.Equal(values, index.CountOf(StructureKind.JsonNumber));
        Assert.InRange(index.CachedBytes, 1, 4 * 1024 * 1024);
        var first = index.ReadChildren(1, -1, 3, CancellationToken.None);
        Assert.Equal([2L, 3L, 4L], first.Select(node => node.Id));
        var next = index.ReadChildren(1, first[^1].Id, 3, CancellationToken.None);
        Assert.Equal([3L, 4L, 5L], next.Select(node => node.Ordinal));
        var final = index.ReadNode(values + 1);
        Assert.Equal(values - 1, final.Ordinal);
        Assert.Equal(1 + 2L * (values - 1), final.Start);
        Assert.Equal(final.Id, index.FindContaining(final.Start, CancellationToken.None)!.Value.Id);
        Assert.Equal(1, index.FindContaining(source.ByteLength - 1, CancellationToken.None)!.Value.Id);
        Assert.Null(index.FindContaining(source.ByteLength, CancellationToken.None));
        Assert.Equal(source.ByteLength, index.ReadNode(0).End);
    }

    [Fact]
    public void Namespace_links_and_element_ranges_round_trip_through_the_file_index()
    {
        const string xml = "<r xmlns='urn:demo'><a id='x'/></r>";
        var source = Snapshot(xml);
        using var index = new StructureIndex(source, 0, StructureFormat.Xml);
        XmlStructureReader.Parse(new(source, source.Codec, 0, index, CancellationToken.None));

        var root = Assert.Single(index.ReadChildren(0, -1, 10, CancellationToken.None));
        Assert.Equal(StructureKind.XmlElement, root.Kind);
        Assert.Equal(xml.Length, root.End);
        var children = index.ReadChildren(root.Id, -1, 10, CancellationToken.None);
        var declaration = Assert.Single(children.Where(node => node.Kind == StructureKind.XmlNamespace));
        var element = Assert.Single(children.Where(node => node.Kind == StructureKind.XmlElement));
        Assert.Equal(declaration.Id, root.NamespaceId);
        Assert.Equal(declaration.Id, element.NamespaceId);
        var attribute = Assert.Single(index.ReadChildren(element.Id, -1, 10, CancellationToken.None));
        Assert.Equal(-1, attribute.NamespaceId);
        Assert.Equal("x", source.ReadText(attribute.ValueRange));
        Assert.Equal(element.Id, index.FindContaining(element.Start, CancellationToken.None)!.Value.Id);
    }

    [Fact]
    public void Closing_an_index_rejects_its_old_node_handles_without_changing_the_source()
    {
        var source = Snapshot("{\"a\":1}");
        var index = new StructureIndex(source, 0, StructureFormat.Json);
        JsonStructureReader.Parse(new(source, source.Codec, 0, index, CancellationToken.None));
        index.Dispose();

        Assert.Throws<ObjectDisposedException>(() => index.ReadNode(0));
        Assert.Equal("{\"a\":1}", source.ReadText(new ByteRange(0, source.ByteLength)));
    }
}
