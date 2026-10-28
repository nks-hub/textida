using NksHub.Textida.Plugin.Testing;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.Plugin.Tests.Contracts;

/// <summary>P8b.2.2, interface <b>diff</b> (diff/compute, diff/open, diff/stage): sides are bound to a revision, the content of a plugin needs the virtual grant to be shown, and a stage is refused when its target moved on.</summary>
public class DiffContractTests
{
    private static string Side(string text) => $"{{\"text\":\"{text}\"}}";

    [Fact]
    public async Task Positive_a_diff_of_a_document_and_a_text_of_the_plugin_lists_the_hunks_in_lines_and_bytes()
    {
        await using var editor = await T.RawEditor(DocumentPermissions.Read);
        var document = editor.OpenDocument("a.txt", "one\ntwo\nthree\n");

        var diff = await editor.Raw!.RequestAsync(DocumentMethods.DiffCompute, $"{{\"left\":{{\"document\":\"{document.Id}\",\"revision\":{document.Revision}}},\"right\":{Side(@"one\nTWO\nthree\n")}}}");

        Assert.False(diff.GetProperty("identical").GetBoolean());
        Assert.Equal(1, diff.GetProperty("hunkCount").GetInt32());
        var hunk = diff.GetProperty("hunks")[0];
        Assert.Equal((1L, 1L, 4L, 8L), (hunk.GetProperty("left").GetProperty("startLine").GetInt64(), hunk.GetProperty("left").GetProperty("lines").GetInt64(),
            hunk.GetProperty("left").GetProperty("start").GetInt64(), hunk.GetProperty("left").GetProperty("end").GetInt64()));
        Assert.Equal(document.Id, diff.GetProperty("left").GetProperty("document").GetString());
        Assert.True(diff.GetProperty("right").GetProperty("detached").GetBoolean());
    }

    [Fact]
    public async Task Positive_a_comparison_tab_opens_and_a_hunk_is_staged_into_a_virtual_document_of_the_plugin()
    {
        await using var editor = await T.RawEditor(DocumentPermissions.Read, DocumentPermissions.Virtual);
        var plugin = editor.Raw!;

        var opened = await plugin.RequestAsync(DocumentMethods.DiffOpen, $"{{\"left\":{Side(@"a\nb\n")},\"right\":{Side(@"a\nc\n")},\"title\":\"index vs file\",\"key\":\"k1\"}}");
        Assert.True(opened.GetProperty("opened").GetBoolean());
        Assert.Equal(("index vs file", "k1"), (editor.Tools.Diffs.Single().Title, editor.Tools.Diffs.Single().Key));

        var index = await plugin.RequestAsync(DocumentMethods.VirtualOpen, """{"key":"index","name":"index.txt","text":"a\nb\n"}""");
        var id = index.GetProperty("document").GetString()!;
        var staged = await plugin.RequestAsync(DocumentMethods.DiffStage, $"{{\"base\":{{\"document\":\"{id}\",\"revision\":{index.GetProperty("revision").GetInt64()}}},\"source\":{Side(@"a\nc\n")},\"hunk\":0}}");

        Assert.Equal(index.GetProperty("revision").GetInt64() + 1, staged.GetProperty("revision").GetInt64());
        var text = await plugin.RequestAsync(DocumentMethods.ReadText, $"{{\"document\":\"{id}\",\"revision\":{staged.GetProperty("revision").GetInt64()},\"start\":0,\"end\":4}}");
        Assert.Equal("a\nc\n", text.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Revision_conflict_a_side_at_an_old_revision_is_refused_and_so_is_a_stage_into_a_target_that_moved_on()
    {
        await using var editor = await T.RawEditor(DocumentPermissions.Read, DocumentPermissions.Virtual);
        var plugin = editor.Raw!;
        var document = editor.OpenDocument("a.txt", "one\n");
        var past = document.Revision;
        document.Type(0, "x");

        var compute = await T.ErrorOf(() => plugin.RequestAsync(DocumentMethods.DiffCompute, $"{{\"left\":{{\"document\":\"{document.Id}\",\"revision\":{past}}},\"right\":{Side(@"one\n")}}}"));
        Assert.Equal((DocumentErrorCodes.RevisionConflict, "Stale"), (compute.Code, T.Data(compute, "reason")));

        var index = await plugin.RequestAsync(DocumentMethods.VirtualOpen, """{"key":"index","name":"index.txt","text":"a\nb\n"}""");
        var id = index.GetProperty("document").GetString()!;
        var seen = index.GetProperty("revision").GetInt64();
        await plugin.RequestAsync(DocumentMethods.VirtualUpdate, $"{{\"document\":\"{id}\",\"text\":\"a\\nb\\nc\\n\"}}");

        var stage = await T.ErrorOf(() => plugin.RequestAsync(DocumentMethods.DiffStage, $"{{\"base\":{{\"document\":\"{id}\",\"revision\":{seen}}},\"source\":{Side(@"a\nx\n")},\"hunk\":0}}"));
        Assert.Equal(DocumentErrorCodes.RevisionConflict, stage.Code);
        var current = await plugin.RequestAsync(DocumentMethods.ReadText, $"{{\"document\":\"{id}\",\"revision\":{seen + 1},\"start\":0,\"end\":6}}");
        Assert.Equal("a\nb\nc\n", current.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Permission_a_diff_needs_the_read_permission_and_showing_content_of_the_plugin_needs_the_virtual_one()
    {
        await using var editor = await T.RawEditor();
        var plugin = editor.Raw!;
        var document = editor.OpenDocument("a.txt", "one\n");

        var none = await T.ErrorOf(() => plugin.RequestAsync(DocumentMethods.DiffCompute, $"{{\"left\":{Side(@"a\n")},\"right\":{Side(@"b\n")}}}"));
        Assert.Equal((DocumentErrorCodes.PermissionDenied, DocumentPermissions.Read), (none.Code, T.Data(none, "permission")));

        editor.Permissions.Grant(editor.PluginId, DocumentPermissions.Read, GrantScope.OfDocument(Guid.Parse(document.Id)));
        var elsewhere = editor.OpenDocument("b.txt", "other\n");
        var other = await T.ErrorOf(() => plugin.RequestAsync(DocumentMethods.DiffCompute, $"{{\"left\":{{\"document\":\"{elsewhere.Id}\",\"revision\":{elsewhere.Revision}}},\"right\":{Side(@"b\n")}}}"));
        Assert.Equal(DocumentErrorCodes.PermissionDenied, other.Code);

        editor.Permissions.Grant(editor.PluginId, DocumentPermissions.Read, GrantScope.All);
        var open = await T.ErrorOf(() => plugin.RequestAsync(DocumentMethods.DiffOpen, $"{{\"left\":{Side(@"a\n")},\"right\":{Side(@"b\n")}}}"));
        Assert.Equal((DocumentErrorCodes.PermissionDenied, DocumentPermissions.Virtual, "NotGranted"), (open.Code, T.Data(open, "permission"), T.Data(open, "reason")));
        Assert.Empty(editor.Tools.Diffs);
    }

    [Fact]
    public async Task A_side_over_the_limit_of_the_editor_is_refused_with_both_sizes()
    {
        await using var editor = await T.RawEditor(new TestEditorOptions { Grants = [DocumentPermissions.Read], TuneDocuments = o => o with { MaxDiffBytes = 8 } });

        var big = await T.ErrorOf(() => editor.Raw!.RequestAsync(DocumentMethods.DiffCompute, $"{{\"left\":{Side("0123456789")},\"right\":{Side("x")}}}"));

        Assert.Equal((DocumentErrorCodes.DiffTooLarge, "8", "10"), (big.Code, T.Data(big, "limitBytes"), T.Data(big, "bytes")));
    }
}
