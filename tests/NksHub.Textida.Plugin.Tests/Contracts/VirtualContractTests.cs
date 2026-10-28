using System.Text;
using NksHub.Textida.Plugin.Testing;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.Plugin.Tests.Contracts;

/// <summary>P8b.2.2, interface <b>virtual</b> (virtual/open, virtual/update, virtual/close, and the save of an editable tab): a document the plugin supplies, bound to the revision the plugin last saw.</summary>
public class VirtualContractTests
{
    [Fact]
    public async Task Positive_a_plugin_opens_updates_and_closes_a_virtual_document_and_reads_what_it_provided()
    {
        await using var editor = await T.RawEditor(DocumentPermissions.Virtual);
        var plugin = editor.Raw!;

        var opened = await plugin.RequestAsync(DocumentMethods.VirtualOpen, """{"key":"log","name":"build.log","title":"Build","text":"line 1\n"}""");
        var id = opened.GetProperty("document").GetString()!;
        Assert.Equal((true, false, "log"), (opened.GetProperty("created").GetBoolean(), opened.GetProperty("editable").GetBoolean(), opened.GetProperty("key").GetString()));
        Assert.Equal(("build.log", "Build"), (editor.Tools.Virtuals.Single().Name, editor.Tools.Virtuals.Single().Title));

        // the same key is the same document, and the content of the second request is ignored
        var again = await plugin.RequestAsync(DocumentMethods.VirtualOpen, """{"key":"log","name":"build.log","text":"ignored"}""");
        Assert.Equal((id, false), (again.GetProperty("document").GetString(), again.GetProperty("created").GetBoolean()));

        var revision = opened.GetProperty("revision").GetInt64();
        var updated = await plugin.RequestAsync(DocumentMethods.VirtualUpdate, $"{{\"document\":\"{id}\",\"baseRevision\":{revision},\"text\":\"line 1\\nline 2\\n\"}}");
        Assert.Equal((revision + 1, 14L), (updated.GetProperty("revision").GetInt64(), updated.GetProperty("byteLength").GetInt64()));

        // reading what the plugin provided needs no further grant
        var read = await plugin.RequestAsync(DocumentMethods.ReadText, $"{{\"document\":\"{id}\",\"revision\":{revision + 1},\"start\":0,\"end\":14}}");
        Assert.Equal("line 1\nline 2\n", read.GetProperty("text").GetString());

        Assert.True((await plugin.RequestAsync(DocumentMethods.VirtualClose, $"{{\"document\":\"{id}\"}}")).GetProperty("closed").GetBoolean());
        Assert.Equal([Guid.Parse(id)], editor.Tools.VirtualClosed);
        var reopened = await plugin.RequestAsync(DocumentMethods.VirtualOpen, """{"key":"log","name":"build.log","text":"fresh"}""");
        Assert.True(reopened.GetProperty("created").GetBoolean()); // the key is free again
    }

    [Fact]
    public async Task Positive_the_save_of_an_editable_tab_goes_to_the_plugin_and_nothing_is_written_to_a_file()
    {
        await using var editor = await T.RawEditor(DocumentPermissions.Virtual);
        var plugin = editor.Raw!;
        string? saved = null;
        plugin.Answer = (method, parameters) =>
        {
            if (method != DocumentRequests.VirtualSave) return null;
            saved = Encoding.UTF8.GetString(parameters.GetProperty("bytes").GetBytesFromBase64());
            return "true"u8.ToArray();
        };
        var opened = await plugin.RequestAsync(DocumentMethods.VirtualOpen, """{"key":"note","name":"note.txt","text":"draft","editable":true}""");
        var id = Guid.Parse(opened.GetProperty("document").GetString()!);

        await editor.Documents.VirtualSaveAsync(id, "final"u8.ToArray(), 7);

        Assert.Equal("final", saved);
    }

    [Fact]
    public async Task Revision_conflict_an_update_that_names_a_revision_other_than_the_current_one_changes_nothing()
    {
        await using var editor = await T.RawEditor(DocumentPermissions.Virtual);
        var plugin = editor.Raw!;
        var opened = await plugin.RequestAsync(DocumentMethods.VirtualOpen, """{"key":"log","name":"build.log","text":"one"}""");
        var id = opened.GetProperty("document").GetString()!;
        var seen = opened.GetProperty("revision").GetInt64();
        await plugin.RequestAsync(DocumentMethods.VirtualUpdate, $"{{\"document\":\"{id}\",\"text\":\"two\"}}"); // without baseRevision it always goes through

        var stale = await T.ErrorOf(() => plugin.RequestAsync(DocumentMethods.VirtualUpdate, $"{{\"document\":\"{id}\",\"baseRevision\":{seen},\"text\":\"three\"}}"));

        Assert.Equal((DocumentErrorCodes.RevisionConflict, "Stale", (seen + 1).ToString()), (stale.Code, T.Data(stale, "reason"), T.Data(stale, "currentRevision")));
        var read = await plugin.RequestAsync(DocumentMethods.ReadText, $"{{\"document\":\"{id}\",\"revision\":{seen + 1},\"start\":0,\"end\":3}}");
        Assert.Equal("two", read.GetProperty("text").GetString());
    }

    [Fact]
    public async Task A_plugin_cannot_update_or_close_a_document_it_did_not_provide()
    {
        await using var editor = await T.RawEditor(DocumentPermissions.Virtual, DocumentPermissions.Read);
        var own = editor.OpenDocument("a.txt", "abc");

        var update = await T.ErrorOf(() => editor.Raw!.RequestAsync(DocumentMethods.VirtualUpdate, $"{{\"document\":\"{own.Id}\",\"text\":\"x\"}}"));
        var close = await T.ErrorOf(() => editor.Raw!.RequestAsync(DocumentMethods.VirtualClose, $"{{\"document\":\"{own.Id}\"}}"));

        Assert.Equal((DocumentErrorCodes.NotProvider, DocumentErrorCodes.NotProvider), (update.Code, close.Code));
        Assert.Equal("abc", own.Text);
    }

    [Fact]
    public async Task Permission_a_plugin_without_the_virtual_permission_opens_nothing_and_a_plugin_at_its_quota_is_told_so()
    {
        await using (var without = await T.RawEditor(DocumentPermissions.Read))
        {
            var refused = await T.ErrorOf(() => without.Raw!.RequestAsync(DocumentMethods.VirtualOpen, """{"key":"k","name":"n.txt","text":"x"}"""));
            Assert.Equal((DocumentErrorCodes.PermissionDenied, DocumentPermissions.Virtual, "NotGranted"), (refused.Code, T.Data(refused, "permission"), T.Data(refused, "reason")));
            Assert.Empty(without.Tools.Virtuals);
        }

        await using var limited = await T.RawEditor(new TestEditorOptions { Grants = [DocumentPermissions.Virtual], TuneDocuments = o => o with { MaxVirtualDocumentsPerPlugin = 1 } });
        await limited.Raw!.RequestAsync(DocumentMethods.VirtualOpen, """{"key":"one","name":"one.txt","text":"x"}""");
        var quota = await T.ErrorOf(() => limited.Raw.RequestAsync(DocumentMethods.VirtualOpen, """{"key":"two","name":"two.txt","text":"x"}"""));
        Assert.Equal(DocumentErrorCodes.VirtualQuotaExceeded, quota.Code);
    }
}
