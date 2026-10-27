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

    // ---- editable virtual documents: their own address, edits that reach the plugin, a save that goes to the plugin, closing ----

    static DocumentApiOptions WithVirtualSave(DocumentApiOptions o) => o with { VirtualSaveTimeout = TimeSpan.FromSeconds(10) };

    static string OpenEditableParams(string uri, string name, string text, string? key = null) =>
        $"{{\"uri\":\"{uri}\",\"name\":\"{name}\",\"editable\":true,{(key is null ? "" : $"\"key\":\"{key}\",")}\"text\":{JsonSerializer.Serialize(text)}}}";

    static async Task<Guid> OpenEditableAsync(Rig rig, string uri, string name, string text, string? key = null, string plugin = "p")
    {
        var r = await rig.CallAsync(DocumentMethods.VirtualOpen, OpenEditableParams(uri, name, text, key), plugin);
        Assert.True(r.TryGetProperty("document", out _), r.GetRawText());
        return r.GetProperty("document").GetGuid();
    }

    static async Task<List<JsonElement>> VirtualSavesAsync(Rig rig) =>
        [.. (await rig.Host.CallAsync("p", "virtualSaves")).EnumerateArray().Select(e => e.Clone())];

    [Fact]
    public async Task AnEditableVirtualDocumentHasItsOwnAddressTakesEditsWithUndoAndTheProviderHearsThemWithoutSubscribing()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"), tune: WithVirtualSave);
        rig.Permissions.Grant("p", DocumentPermissions.Virtual, GrantScope.All);

        var r = await rig.CallAsync(DocumentMethods.VirtualOpen, OpenEditableParams("agent://session/7", "notes.md", "one\n"));
        var id = r.GetProperty("document").GetGuid();
        Assert.True(r.GetProperty("editable").GetBoolean());
        Assert.Equal("agent://session/7", r.GetProperty("uri").GetString());
        Assert.Equal("agent://session/7", r.GetProperty("key").GetString()); // the address is the key when the plugin names none
        Assert.Equal(new VirtualInfo("agent://session/7", "agent://session/7", true), rig.Tools.VirtualInfos[id]);
        var doc = rig.Tools.Virtuals.Single().Document;
        Assert.True(doc.VirtualEditable);
        Assert.False(doc.IsModified);

        // the plugin (with its edit grant) and the user both edit it like an ordinary document
        var revision = r.GetProperty("revision").GetInt64();
        var apply = await rig.CallAsync(DocumentMethods.EditApply, $"{{\"document\":\"{id}\",\"revision\":{revision},\"batchId\":\"b1\",\"edits\":[{{\"start\":0,\"end\":3,\"text\":\"ONE\"}}]}}");
        Assert.True(apply.GetProperty("applied").GetBoolean());
        rig.Owner.Run(() => { doc.Insert(doc.ByteLength, Utf8("two\n")); return 0; });
        Assert.True(doc.IsModified);
        Assert.Equal("ONE\ntwo\n", VirtualText(rig, id));

        rig.Owner.Run(() => doc.Undo());
        Assert.DoesNotContain("two", VirtualText(rig, id));
        rig.Owner.Run(() => doc.Redo());
        Assert.Equal("ONE\ntwo\n", VirtualText(rig, id));

        // the provider never subscribed, and still hears the user's edits (source null) and its own
        await rig.WaitEventsAsync(e => e.Count(x => x.GetProperty("name").GetString() == DocumentEvents.Changed) >= 2);
        var changed = (await rig.EventsAsync()).Where(x => x.GetProperty("name").GetString() == DocumentEvents.Changed).Select(x => x.GetProperty("payload")).ToList();
        Assert.Contains(changed, c => c.GetProperty("source").ValueKind == JsonValueKind.Null);
        Assert.Contains(changed, c => c.GetProperty("source").ValueKind == JsonValueKind.String);

        // the provider replaces the text: unsaved edits go with it, and a base revision that is behind stops it
        await rig.ErrorAsync(DocumentMethods.VirtualUpdate, $"{{\"document\":\"{id}\",\"baseRevision\":{revision},\"text\":\"lost\"}}", DocumentErrorCodes.RevisionConflict);
        Assert.Equal("ONE\ntwo\n", VirtualText(rig, id));
        await rig.CallAsync(DocumentMethods.VirtualUpdate, $"{{\"document\":\"{id}\",\"text\":\"fresh\\n\"}}");
        Assert.Equal("fresh\n", VirtualText(rig, id));
        Assert.False(doc.IsModified);
        Assert.False(doc.CanUndo);
    }

    [Fact]
    public async Task AVirtualAddressIsASchemeThatIsNotFileAndAnEditableDocumentNeedsTheSaveTimeout()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"), tune: WithVirtualSave);
        rig.Permissions.Grant("p", DocumentPermissions.Virtual, GrantScope.All);
        await rig.ErrorAsync(DocumentMethods.VirtualOpen, "{\"uri\":\"file:///etc/hosts\",\"name\":\"a.txt\"}", RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(DocumentMethods.VirtualOpen, "{\"uri\":\"not an address\",\"name\":\"a.txt\"}", RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(DocumentMethods.VirtualOpen, "{\"name\":\"a.txt\"}", RpcErrorCodes.InvalidParams);
        Assert.Empty(rig.Tools.Virtuals);

        var first = await OpenEditableAsync(rig, "list://remote/a", "a.txt", "x");
        var again = await rig.CallAsync(DocumentMethods.VirtualOpen, OpenEditableParams("list://remote/a", "other.txt", "ignored"));
        Assert.False(again.GetProperty("created").GetBoolean());
        Assert.Equal(first, again.GetProperty("document").GetGuid());
        var keyed = await rig.CallAsync(DocumentMethods.VirtualOpen, $"{{\"key\":\"k\",\"uri\":\"list://remote/a\",\"name\":\"b.txt\",\"text\":\"y\"}}");
        Assert.True(keyed.GetProperty("created").GetBoolean()); // a key of its own is another identity, read only unless it says otherwise
        Assert.False(keyed.GetProperty("editable").GetBoolean());

        await using var plain = await Rig.CreateAsync(Utf8("x"));
        plain.Permissions.Grant("p", DocumentPermissions.Virtual, GrantScope.All);
        await plain.ErrorAsync(DocumentMethods.VirtualOpen, OpenEditableParams("list://remote/a", "a.txt", "x"), RpcErrorCodes.MethodNotFound);
        Assert.Empty(plain.Tools.Virtuals);
    }

    [Fact]
    public async Task ASaveIsSentToTheProviderWithTheBytesAndNothingElseAndEveryRefusalIsNamed()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"), tune: o => WithVirtualSave(o) with { MaxVirtualBytes = 64 });
        rig.Permissions.Grant("p", DocumentPermissions.Virtual, GrantScope.All);
        var id = await OpenEditableAsync(rig, "agent://session/7", "notes.md", "one\n", key: "k");

        await rig.Api.VirtualSaveAsync(id, Utf8("saved\n"), revision: 3);
        var save = Assert.Single(await VirtualSavesAsync(rig));
        Assert.Equal(id, save.GetProperty("document").GetGuid());
        Assert.Equal(("k", "agent://session/7", 3), (save.GetProperty("key").GetString(), save.GetProperty("uri").GetString(), save.GetProperty("revision").GetInt32()));
        Assert.Equal("saved\n", Encoding.UTF8.GetString(Convert.FromBase64String(save.GetProperty("bytes").GetString()!)));

        // a plugin that refuses leaves the failure to the caller, with the plugin's own words
        await rig.Host.CallAsync("p", "virtualMode", T.Json("{\"fail\":true}"));
        var refused = await Assert.ThrowsAsync<SourceException>(() => rig.Api.VirtualSaveAsync(id, Utf8("again\n"), 4));
        Assert.Equal(SourceFailure.Failed, refused.Kind);
        Assert.Contains("cannot keep it", refused.Message);
        await rig.Host.CallAsync("p", "virtualMode", T.Json("{\"fail\":false}"));

        var (readOnly, _) = await OpenVirtualAsync(rig, "ro", "r.txt", "x");
        Assert.Equal(SourceFailure.ReadOnly, (await Assert.ThrowsAsync<SourceException>(() => rig.Api.VirtualSaveAsync(readOnly, Utf8("x"), 1))).Kind);
        Assert.Equal(SourceFailure.NoSource, (await Assert.ThrowsAsync<SourceException>(() => rig.Api.VirtualSaveAsync(rig.Doc.Id, Utf8("x"), 1))).Kind);
        Assert.Equal(SourceFailure.TooLarge, (await Assert.ThrowsAsync<SourceException>(() => rig.Api.VirtualSaveAsync(id, Utf8(new string('y', 65)), 1))).Kind);
        Assert.Equal(2, (await VirtualSavesAsync(rig)).Count); // the first save and the refused one reached the plugin, the other attempts never did

        rig.Permissions.Revoke("p", DocumentPermissions.Virtual, GrantScope.All);
        Assert.Equal(SourceFailure.Unavailable, (await Assert.ThrowsAsync<SourceException>(() => rig.Api.VirtualSaveAsync(id, Utf8("x"), 1))).Kind);
        Assert.Equal(2, (await VirtualSavesAsync(rig)).Count);
    }

    [Fact]
    public async Task TheProviderClosesItsDocumentOnlyAndTheKeyIsFreeAgain()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"), plugins: ["p", "q"], tune: WithVirtualSave);
        rig.Permissions.Grant("p", DocumentPermissions.Virtual, GrantScope.All);
        rig.Permissions.Grant("q", DocumentPermissions.Virtual, GrantScope.All);
        var (id, _) = await OpenVirtualAsync(rig, "k", "t.txt", "one\n");

        await rig.ErrorAsync(DocumentMethods.VirtualClose, $"{{\"document\":\"{id}\"}}", DocumentErrorCodes.NotProvider, plugin: "q");
        await rig.ErrorAsync(DocumentMethods.VirtualClose, $"{{\"document\":\"{rig.Doc.Id}\"}}", DocumentErrorCodes.NotProvider);
        Assert.Empty(rig.Tools.VirtualClosed);

        var closed = await rig.CallAsync(DocumentMethods.VirtualClose, $"{{\"document\":\"{id}\"}}");
        Assert.True(closed.GetProperty("closed").GetBoolean());
        Assert.Equal([id], rig.Tools.VirtualClosed);
        await rig.ErrorAsync(DocumentMethods.Info, $"{{\"document\":\"{id}\"}}", DocumentErrorCodes.UnknownDocument); // withdrawn
        var (second, _) = await OpenVirtualAsync(rig, "k", "t.txt", "two\n");
        Assert.NotEqual(id, second);

        rig.Permissions.Revoke("p", DocumentPermissions.Virtual, GrantScope.All);
        await rig.ErrorAsync(DocumentMethods.VirtualClose, $"{{\"document\":\"{second}\"}}", DocumentErrorCodes.PermissionDenied);
        Assert.Equal([id], rig.Tools.VirtualClosed);
    }

    [Fact]
    public async Task WhenAPluginEndsItsVirtualDocumentsCloseAndThoseOfOtherPluginsStay()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"), plugins: ["p", "q"], tune: WithVirtualSave);
        rig.Permissions.Grant("p", DocumentPermissions.Virtual, GrantScope.All);
        rig.Permissions.Grant("q", DocumentPermissions.Virtual, GrantScope.All);
        var (readOnly, _) = await OpenVirtualAsync(rig, "a", "a.txt", "x");
        var editable = await OpenEditableAsync(rig, "agent://session/7", "b.md", "y");
        var (others, _) = await OpenVirtualAsync(rig, "c", "c.txt", "z", plugin: "q");

        rig.Owner.Run(() => { rig.Api.PluginEnded("p"); return 0; });

        Assert.Equal(new[] { readOnly, editable }.Order(), rig.Tools.VirtualClosed.Order());
        await rig.ErrorAsync(DocumentMethods.Info, $"{{\"document\":\"{editable}\"}}", DocumentErrorCodes.UnknownDocument);
        Assert.Equal(others.ToString(), (await rig.CallAsync(DocumentMethods.Info, $"{{\"document\":\"{others}\"}}", plugin: "q")).GetProperty("id").GetString());
        await Assert.ThrowsAsync<SourceException>(() => rig.Api.VirtualSaveAsync(editable, Utf8("late"), 1)); // nobody is left to take a save
        rig.Owner.Run(() => { rig.Api.PluginEnded("p"); return 0; }); // a second report changes nothing
        Assert.Equal(2, rig.Tools.VirtualClosed.Count);
    }
}
