using System.Text;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Tests;

public sealed class StructureNavigationTests
{
    private static StructureAnalysis Parse(string text, StructureFormat format = StructureFormat.Json)
    {
        var snapshot = new DocumentSnapshot(Guid.NewGuid(), 7, new ArraySnapshot(Encoding.UTF8.GetBytes(text)), TextCodec.Utf8);
        return StructureAnalysis.Parse(snapshot, 0, format, CancellationToken.None);
    }

    [Fact]
    public void Copied_json_paths_escape_names_and_reveal_the_same_nodes()
    {
        using var analysis = Parse("{\"a\\\"\\\\\\n😀\":[null,{\"žluť\":\"kůň\"}]}");
        var index = analysis.Index;
        for (long id = 1; id < index.Count; id++)
        {
            var node = index.ReadNode(id);
            string path = StructurePaths.Create(index, node);
            var page = StructureQuery.Parse(path, StructureQueryLanguage.JsonPath).ReadPage(index, 0, 10, CancellationToken.None);
            Assert.Equal([id], page.NodeIds);
        }
    }

    [Fact]
    public void Positional_xml_paths_distinguish_namespaces_attributes_cdata_and_comments()
    {
        using var analysis = Parse("<r xmlns='urn:r' a='one'><!--x--><a/>text<![CDATA[é😀]]><a b='two'/></r>", StructureFormat.Xml);
        var index = analysis.Index;
        for (long id = 1; id < index.Count; id++)
        {
            var node = index.ReadNode(id);
            if (node.Kind == StructureKind.XmlNamespace) continue;
            string path = StructurePaths.Create(index, node);
            var page = StructureQuery.Parse(path, StructureQueryLanguage.XPath).ReadPage(index, 0, 10, CancellationToken.None);
            Assert.Equal([id], page.NodeIds);
        }
    }

    [Fact]
    public void Search_decodes_names_values_and_paths_and_resumes_without_losing_matches()
    {
        using var analysis = Parse("{\"\\u017Eluť\":[\"KŮŇ😀\",\"kůň😀\"],\"other\":false}");
        var index = analysis.Index;
        var names = StructureSearch.ReadPage(index, "ŽLUŤ", StructureSearchScope.Name, 0, 10);
        Assert.Equal([2L], names.NodeIds);
        var first = StructureSearch.ReadPage(index, "kůň😀", StructureSearchScope.Value, 0, 1);
        Assert.Equal([3L], first.NodeIds);
        var second = StructureSearch.ReadPage(index, "kůň😀", StructureSearchScope.Value, first.NextNodeId, 1);
        Assert.Equal([4L], second.NodeIds);
        var paths = StructureSearch.ReadPage(index, "ŽLUŤ", StructureSearchScope.Path, 0, 10);
        Assert.Equal([2L, 3L, 4L], paths.NodeIds);
        Assert.True(paths.Complete);
    }

    [Fact]
    public void Text_preview_distinguishes_decoded_value_from_source_and_never_splits_a_surrogate()
    {
        using var analysis = Parse("{\"x\":\"a😀\\n\"}");
        var node = analysis.Index.ReadNode(2);
        Assert.Equal(new StructurePreview("a", true), StructureText.ReadValue(analysis.Index, node, 2));
        Assert.Equal(new StructurePreview("a😀\n", false), StructureText.ReadValue(analysis.Index, node, 20));
        Assert.Equal(new StructurePreview("\"x\":\"a😀\\n\"", false), StructureText.ReadSource(analysis.Index, node, 30));
        Assert.Throws<InvalidOperationException>(() => StructurePaths.Create(analysis.Index, node, 2));
    }

    [Fact]
    public void Invalid_analysis_preserves_the_source_and_does_not_present_a_valid_partial_tree()
    {
        using var analysis = Parse("{\"x\":[0,]}");
        Assert.False(analysis.IsValid);
        Assert.Single(analysis.Index.Issues);
        Assert.Equal(StructureIssueKind.Syntax, analysis.Index.Issues[0].Kind);
        Assert.Equal("{\"x\":[0,]}", analysis.Index.Source.ReadText(new ByteRange(0, analysis.Index.Source.ByteLength)));
    }
}
