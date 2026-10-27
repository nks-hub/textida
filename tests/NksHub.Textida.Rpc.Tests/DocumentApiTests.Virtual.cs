using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P6.2.6: a document a plugin supplies as a view of its data: opened by key, read only for everyone, searchable and readable, with hidden values kept hidden.</summary>
public partial class DocumentApiTests
{
    static string OpenVirtualParams(string key, string name, string text) =>
        $"{{\"key\":\"{key}\",\"name\":\"{name}\",\"title\":\"Transcript\",\"text\":{JsonSerializer.Serialize(text)}}}";

    static async Task<(Guid Id, long Revision)> OpenVirtualAsync(Rig rig, string key, string name, string text, string plugin = "p")
    {
        var r = await rig.CallAsync(DocumentMethods.VirtualOpen, OpenVirtualParams(key, name, text), plugin);
        Assert.True(r.TryGetProperty("document", out _), r.GetRawText());
        return (r.GetProperty("document").GetGuid(), r.GetProperty("revision").GetInt64());
    }

    static string VirtualText(Rig rig, Guid id)
    {
        var doc = rig.Tools.Virtuals.Single(v => v.Document.Id == id).Document;
        return Encoding.UTF8.GetString(rig.Owner.Run(() => doc.ReadBytes(new ByteRange(0, doc.ByteLength))));
    }

    [Fact]
    public async Task APluginOpensAVirtualDocumentByKeyAndReadsItWithoutAnyOtherGrant()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"));
        rig.Permissions.Revoke("p", DocumentPermissions.Read, GrantScope.All);
        rig.Permissions.Grant("p", DocumentPermissions.Virtual, GrantScope.All);

        var r = await rig.CallAsync(DocumentMethods.VirtualOpen, OpenVirtualParams("agent:7", "transcript.md", "# Turn 1\nhello\n"));
        Assert.True(r.GetProperty("created").GetBoolean());
        var id = r.GetProperty("document").GetGuid();
        var (plugin, name, title, doc) = Assert.Single(rig.Tools.Virtuals);
        Assert.Equal(("p", "transcript.md", "Transcript", id), (plugin, name, title, doc.Id));
        Assert.True(doc.IsVirtual);
        Assert.False(doc.IsModified);

        var revision = r.GetProperty("revision").GetInt64();
        var text = await rig.CallAsync(DocumentMethods.ReadText, $"{{\"document\":\"{id}\",\"revision\":{revision},\"start\":0,\"end\":{doc.ByteLength}}}");
        Assert.Equal("# Turn 1\nhello\n", text.GetProperty("text").GetString());
        var found = await rig.CallAsync(DocumentMethods.Find, $"{{\"document\":\"{id}\",\"revision\":{revision},\"text\":\"hello\"}}");
        Assert.Equal(9, found.GetProperty("matches")[0].GetProperty("start").GetInt64());
        // the plugin sees the documents it provided and nothing else
        var list = await rig.CallAsync(DocumentMethods.List, "{}");
        Assert.Equal([id.ToString()], list.EnumerateArray().Select(d => d.GetProperty("id").GetString()));

        var again = await rig.CallAsync(DocumentMethods.VirtualOpen, OpenVirtualParams("agent:7", "other.md", "ignored"));
        Assert.False(again.GetProperty("created").GetBoolean());
        Assert.Equal(id, again.GetProperty("document").GetGuid());
        Assert.Single(rig.Tools.Virtuals);
    }

    [Fact]
    public async Task AVirtualDocumentTakesNoEditFromAPluginOrAnAssistantAndNothingChanges()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"));
        rig.Permissions.Grant("p", DocumentPermissions.Virtual, GrantScope.All);
        var (id, revision) = await OpenVirtualAsync(rig, "k", "t.txt", "one\n");
        var edit = $"{{\"document\":\"{id}\",\"revision\":{revision},\"batchId\":\"b1\",\"edits\":[{{\"start\":0,\"end\":3,\"text\":\"two\"}}]}}";

        var apply = await rig.ErrorAsync(DocumentMethods.EditApply, edit, DocumentErrorCodes.ReadOnlyDocument);
        Assert.Equal("Virtual", apply.GetProperty("data").GetProperty("reason").GetString());
        await rig.ErrorAsync(DocumentMethods.EditPropose, edit, DocumentErrorCodes.ReadOnlyDocument);
        await rig.ErrorAsync(DocumentMethods.SnippetInsert, $"{{\"document\":\"{id}\",\"revision\":{revision},\"text\":\"zz\"}}", DocumentErrorCodes.ReadOnlyDocument);

        Assert.Equal("one\n", VirtualText(rig, id));
        Assert.Empty(rig.Proposals.Items);
        Assert.Equal(revision, rig.Tools.Virtuals[0].Document.Revision);
    }

    [Fact]
    public async Task TheProviderUpdatesItsDocumentAndAnOutdatedBaseRevisionIsAConflict()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"), plugins: ["p", "q"]);
        rig.Permissions.Grant("p", DocumentPermissions.Virtual, GrantScope.All);
        rig.Permissions.Grant("q", DocumentPermissions.Virtual, GrantScope.All);
        var (id, revision) = await OpenVirtualAsync(rig, "k", "t.txt", "one\n");
        await rig.CallAsync(DocumentMethods.Subscribe, $"{{\"document\":\"{id}\"}}");

        var updated = await rig.CallAsync(DocumentMethods.VirtualUpdate, $"{{\"document\":\"{id}\",\"baseRevision\":{revision},\"text\":\"one\\ntwo\\n\"}}");
        Assert.Equal(revision + 1, updated.GetProperty("revision").GetInt64());
        Assert.Equal("one\ntwo\n", VirtualText(rig, id));
        var doc = rig.Tools.Virtuals[0].Document;
        Assert.False(doc.IsModified);
        Assert.False(doc.CanUndo);

        var stale = await rig.ErrorAsync(DocumentMethods.VirtualUpdate, $"{{\"document\":\"{id}\",\"baseRevision\":{revision},\"text\":\"lost\"}}", DocumentErrorCodes.RevisionConflict);
        Assert.Equal(revision + 1, stale.GetProperty("data").GetProperty("currentRevision").GetInt64());
        Assert.Equal("one\ntwo\n", VirtualText(rig, id));

        // only the provider updates a document, and only a virtual one
        await rig.ErrorAsync(DocumentMethods.VirtualUpdate, $"{{\"document\":\"{id}\",\"text\":\"mine\"}}", DocumentErrorCodes.NotProvider, plugin: "q");
        await rig.ErrorAsync(DocumentMethods.VirtualUpdate, $"{{\"document\":\"{rig.Doc.Id}\",\"text\":\"mine\"}}", DocumentErrorCodes.NotProvider);
        Assert.Equal("one\ntwo\n", VirtualText(rig, id));

        await rig.WaitEventsAsync(e => e.Any(x => x.GetProperty("payload").GetProperty("source").GetString() == "plugin:p"));
    }

    [Fact]
    public async Task AVirtualDocumentNeedsTheVirtualPermissionAndIsLimitedInCountAndSize()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"), tune: o => o with { MaxVirtualDocumentsPerPlugin = 2, MaxVirtualBytes = 16 });
        var denied = await rig.ErrorAsync(DocumentMethods.VirtualOpen, OpenVirtualParams("a", "a.txt", "x"), DocumentErrorCodes.PermissionDenied);
        Assert.Equal(DocumentPermissions.Virtual, denied.GetProperty("data").GetProperty("permission").GetString());
        Assert.Empty(rig.Tools.Virtuals);

        rig.Permissions.Grant("p", DocumentPermissions.Virtual, GrantScope.All);
        await OpenVirtualAsync(rig, "a", "a.txt", "x");
        await OpenVirtualAsync(rig, "b", "b.txt", "x");
        await rig.ErrorAsync(DocumentMethods.VirtualOpen, OpenVirtualParams("c", "c.txt", "x"), DocumentErrorCodes.VirtualQuotaExceeded);
        await rig.ErrorAsync(DocumentMethods.VirtualOpen, OpenVirtualParams("a", "a.txt", new string('y', 17)), RpcErrorCodes.MessageTooLarge);
        var (id, _) = await OpenVirtualAsync(rig, "a", "a.txt", "ignored");
        await rig.ErrorAsync(DocumentMethods.VirtualUpdate, $"{{\"document\":\"{id}\",\"text\":\"{new string('y', 17)}\"}}", RpcErrorCodes.MessageTooLarge);
        Assert.Equal(2, rig.Tools.Virtuals.Count);
    }

    [Fact]
    public async Task HiddenValuesOfAVirtualEnvDocumentAreStarsInEveryReadAndMissingFromSearch()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"));
        rig.Permissions.Grant("p", DocumentPermissions.Virtual, GrantScope.All);
        const string text = "# keys\nAPI_KEY=hunter2\nname=shown-name\n";
        var (id, revision) = await OpenVirtualAsync(rig, "env", ".env", text);
        var doc = rig.Tools.Virtuals[0].Document;
        Assert.Equal(".env", doc.SecretsPath);
        var length = Utf8(text).Length;

        var read = await rig.CallAsync(DocumentMethods.ReadText, $"{{\"document\":\"{id}\",\"revision\":{revision},\"start\":0,\"end\":{length}}}");
        Assert.Equal("# keys\nAPI_KEY=*******\nname=**********\n", read.GetProperty("text").GetString());
        var bytes = await rig.CallAsync(DocumentMethods.ReadBytes, $"{{\"document\":\"{id}\",\"revision\":{revision},\"start\":0,\"end\":{length}}}");
        Assert.DoesNotContain("hunter2", Encoding.UTF8.GetString(FromB64(bytes)));
        var hidden = await rig.CallAsync(DocumentMethods.Find, $"{{\"document\":\"{id}\",\"revision\":{revision},\"text\":\"hunter2\"}}");
        Assert.Equal(0, hidden.GetProperty("matches").GetArrayLength());
        var shownKey = await rig.CallAsync(DocumentMethods.Find, $"{{\"document\":\"{id}\",\"revision\":{revision},\"text\":\"API_KEY\"}}");
        Assert.Equal(1, shownKey.GetProperty("matches").GetArrayLength());

        // the plugin may see the values only when the user granted it for this document
        rig.Permissions.Grant("p", DocumentPermissions.SecretsReveal, GrantScope.OfDocument(id));
        var revealed = await rig.CallAsync(DocumentMethods.ReadText, $"{{\"document\":\"{id}\",\"revision\":{revision},\"start\":0,\"end\":{length}}}");
        Assert.Equal(text, revealed.GetProperty("text").GetString());
    }

    [Fact]
    public async Task ClosingTheDocumentFreesTheKeyAndTakesTheProvidersAccessAway()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"));
        rig.Permissions.Revoke("p", DocumentPermissions.Read, GrantScope.All);
        rig.Permissions.Grant("p", DocumentPermissions.Virtual, GrantScope.All);
        var (id, revision) = await OpenVirtualAsync(rig, "k", "t.txt", "one\n");
        var doc = rig.Tools.Virtuals[0].Document;

        rig.Owner.Run(() => { rig.Api.Withdraw(doc); return 0; });
        await rig.ErrorAsync(DocumentMethods.ReadBytes, $"{{\"document\":\"{id}\",\"revision\":{revision},\"start\":0,\"end\":1}}", DocumentErrorCodes.PermissionDenied);
        var (second, _) = await OpenVirtualAsync(rig, "k", "t.txt", "two\n");
        Assert.NotEqual(id, second);
        Assert.Equal(2, rig.Tools.Virtuals.Count);
    }
}
