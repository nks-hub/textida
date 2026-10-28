using System.Text.Json;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugin.Tests.Contracts;

/// <summary>
/// P8b.2.2, interface <b>documents</b> (list, info, read, selections, positions): the SDK client and the raw protocol against the real document API.
/// Each interface has three scenarios: it works, a stale revision is a revision conflict, and a plugin without the permission is refused.
/// </summary>
public class DocumentsContractTests
{
    [Fact]
    public async Task Positive_a_plugin_with_the_read_permission_lists_describes_and_reads_a_document()
    {
        await using var editor = await T.RawEditor(DocumentPermissions.Read);
        var document = editor.OpenDocument("a.txt", "héllo\nworld\n");
        var client = new DocumentClient(editor.Raw!.Connection, document.Id, document.Revision);

        var list = await editor.Raw.RequestAsync(DocumentMethods.List);
        Assert.Equal([document.Id], list.EnumerateArray().Select(d => d.GetProperty("id").GetString()));

        var info = await client.InfoAsync();
        Assert.Equal((13L, "UTF-8", 0, null), (info.ByteLength, info.Codec, info.BomLength, info.Path));

        // the é takes two bytes: offsets are bytes, and the text stops at character boundaries
        Assert.Equal(new TextRead("hél", false), await client.ReadTextAsync(0, 4));
        var selections = await client.SelectionsAsync();
        Assert.Equal([new ByteSelection(0, 0)], selections);

        var position = await client.PositionAsync(9);
        Assert.Equal((1L, 2L), position);
        Assert.Equal("\n", await client.DominantLineEndingAsync());
    }

    [Fact]
    public async Task Positive_a_subscription_delivers_ranges_and_revisions_of_a_change_and_never_text()
    {
        await using var editor = await T.RawEditor(DocumentPermissions.Read);
        var document = editor.OpenDocument("a.txt", "abc");
        await editor.Raw!.RequestAsync(DocumentMethods.Subscribe, $"{{\"document\":\"{document.Id}\"}}");
        var before = document.Revision;

        document.Type(1, "XYZ");

        var changed = await editor.Raw.WaitForEventAsync(DocumentEvents.Changed);
        using var payload = JsonDocument.Parse(changed.PayloadJson);
        Assert.Equal(before + 1, payload.RootElement.GetProperty("revision").GetInt64());
        var edit = payload.RootElement.GetProperty("edits")[0];
        Assert.Equal((1L, 0L, 3L), (edit.GetProperty("start").GetInt64(), edit.GetProperty("deleted").GetInt64(), edit.GetProperty("inserted").GetInt64()));
        Assert.DoesNotContain("XYZ", changed.PayloadJson);
    }

    [Fact]
    public async Task Revision_conflict_a_read_of_a_revision_that_is_not_the_current_one_is_refused_with_the_current_revision()
    {
        await using var editor = await T.RawEditor(DocumentPermissions.Read);
        var document = editor.OpenDocument("a.txt", "abc");
        var stale = new DocumentClient(editor.Raw!.Connection, document.Id, document.Revision);
        var past = document.Revision;
        document.Type(0, "x");

        var read = await T.ErrorOf(() => stale.ReadTextAsync(0, 2));
        Assert.Equal(DocumentErrorCodes.RevisionConflict, read.Code);
        Assert.Equal(("Stale", (past + 1).ToString()), (T.Data(read, "reason"), T.Data(read, "currentRevision")));

        // the client checks the revision it is bound to before it hands out facts of a newer one
        Assert.Equal(DocumentErrorCodes.RevisionConflict, (await T.ErrorOf(() => stale.InfoAsync())).Code);

        var future = await T.ErrorOf(() => editor.Raw.RequestAsync(DocumentMethods.ReadBytes, $"{{\"document\":\"{document.Id}\",\"revision\":{past + 9},\"start\":0,\"end\":1}}"));
        Assert.Equal(("FutureRevision", DocumentErrorCodes.RevisionConflict), (T.Data(future, "reason"), future.Code));

        // a snapshot pins a revision: it can be read after the document moved on
        var fresh = await editor.Raw.RequestAsync(DocumentMethods.SnapshotCreate, $"{{\"document\":\"{document.Id}\"}}");
        var snapshot = fresh.GetProperty("snapshot").GetString();
        document.Type(0, "y");
        var pinned = await editor.Raw.RequestAsync(DocumentMethods.ReadText, $"{{\"snapshot\":\"{snapshot}\",\"start\":0,\"end\":4}}");
        Assert.Equal("xabc", pinned.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Permission_a_plugin_without_the_read_permission_gets_an_error_and_never_an_empty_answer()
    {
        await using var editor = await T.RawEditor();
        var document = editor.OpenDocument("a.txt", "abc");

        var list = await T.ErrorOf(() => editor.Raw!.RequestAsync(DocumentMethods.List));
        Assert.Equal((DocumentErrorCodes.PermissionDenied, DocumentPermissions.Read, "NotGranted"), (list.Code, T.Data(list, "permission"), T.Data(list, "reason")));
        var read = await T.ErrorOf(() => editor.Raw!.RequestAsync(DocumentMethods.ReadBytes, T.Doc(document, "\"start\":0,\"end\":1")));
        Assert.Equal(DocumentErrorCodes.PermissionDenied, read.Code);
        Assert.All(editor.Calls, c => Assert.Equal(DocumentErrorCodes.PermissionDenied, c.ErrorCode));

        // a grant for one document does not reach another one
        editor.Permissions.Grant(editor.PluginId, DocumentPermissions.Read, NksHub.Textida.PluginHost.Permissions.GrantScope.OfDocument(Guid.Parse(document.Id)));
        var second = editor.OpenDocument("b.txt", "second");
        Assert.Equal("abc", (await editor.Raw!.RequestAsync(DocumentMethods.ReadText, T.Doc(document, "\"start\":0,\"end\":3"))).GetProperty("text").GetString());
        Assert.Equal(DocumentErrorCodes.PermissionDenied, (await T.ErrorOf(() => editor.Raw.RequestAsync(DocumentMethods.ReadText, T.Doc(second, "\"start\":0,\"end\":3")))).Code);
    }
}
