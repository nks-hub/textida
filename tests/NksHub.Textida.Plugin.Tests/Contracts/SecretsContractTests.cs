using NksHub.Textida.Plugin.Testing;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugin.Tests.Contracts;

/// <summary>
/// P8b.2.2, interface <b>secrets</b>: the secret store of the plugin (secret/get, secret/set, secret/delete) and the values of secrets files (<c>.env</c>) in documents, which a plugin reads
/// as stars until the user allows it to see them. The store has no revision and needs no permission (a plugin reaches only its own secrets); the revision and the permission scenarios are those of the files.
/// </summary>
public class SecretsContractTests
{
    private const string Env = "# keys\nAPI_KEY=hunter2\nEMPTY=\nHOST=example\n";

    [Fact]
    public async Task Positive_a_plugin_stores_reads_and_deletes_its_own_secret_and_nobody_else_sees_it()
    {
        await using var editor = await T.RawEditor();
        var plugin = editor.Raw!;

        Assert.True((await plugin.RequestAsync(PluginServiceMethods.SecretSet, """{"name":"token","value":"s3cret"}""")).GetProperty("stored").GetBoolean());
        Assert.Equal("s3cret", (await plugin.RequestAsync(PluginServiceMethods.SecretGet, """{"name":"token"}""")).GetProperty("value").GetString());
        Assert.Null(editor.Secrets.Get("another.plugin", "token"));
        Assert.True((await plugin.RequestAsync(PluginServiceMethods.SecretDelete, """{"name":"token"}""")).GetProperty("deleted").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await plugin.RequestAsync(PluginServiceMethods.SecretGet, """{"name":"token"}""")).GetProperty("value").ValueKind);
        // the log of calls holds the method and the outcome, never the value
        Assert.DoesNotContain("s3cret", string.Join("\n", editor.CallLog.Entries().Select(e => e.ToString())));
    }

    [Fact]
    public async Task Positive_a_secrets_file_reads_as_stars_for_a_plugin_that_may_not_see_the_values()
    {
        await using var editor = await T.RawEditor(DocumentPermissions.Read);
        var env = editor.OpenFile(".env", Env);

        var text = await editor.Raw!.RequestAsync(DocumentMethods.ReadText, T.Doc(env, $"\"start\":0,\"end\":{Env.Length}"));
        var search = await editor.Raw.RequestAsync(DocumentMethods.Find, T.Doc(env, "\"text\":\"hunter2\""));

        Assert.Equal("# keys\nAPI_KEY=*******\nEMPTY=\nHOST=*******\n", text.GetProperty("text").GetString());
        Assert.Equal(0, search.GetProperty("matches").GetArrayLength()); // a search cannot probe a hidden value
        Assert.Equal(Env, env.Text); // the file itself is untouched
    }

    [Fact]
    public async Task Revision_conflict_a_secrets_file_is_read_at_a_revision_like_any_document_and_a_stale_read_is_refused()
    {
        await using var editor = await T.RawEditor(DocumentPermissions.Read);
        var env = editor.OpenFile(".env", Env);
        var past = env.Revision;
        env.Type(0, "# changed\n");

        var stale = await T.ErrorOf(() => editor.Raw!.RequestAsync(DocumentMethods.ReadText, $"{{\"document\":\"{env.Id}\",\"revision\":{past},\"start\":0,\"end\":6}}"));

        Assert.Equal((DocumentErrorCodes.RevisionConflict, "Stale"), (stale.Code, T.Data(stale, "reason")));
    }

    [Fact]
    public async Task Permission_the_values_show_after_the_user_allows_one_document_and_hide_again_when_the_grant_is_revoked()
    {
        await using var editor = await T.RawEditor(DocumentPermissions.Read);
        var env = editor.OpenFile("prod.env", "K=secret\n");
        var other = editor.OpenFile("other.env", "K=secret\n");
        async Task<string?> ReadAsync(TestDocument document) => (await editor.Raw!.RequestAsync(DocumentMethods.ReadText, T.Doc(document, "\"start\":0,\"end\":9"))).GetProperty("text").GetString();

        Assert.Equal("K=******\n", await ReadAsync(env));
        editor.Permissions.Grant(editor.PluginId, DocumentPermissions.SecretsReveal, GrantScope.OfDocument(Guid.Parse(env.Id)));
        Assert.Equal("K=secret\n", await ReadAsync(env));
        Assert.Equal("K=******\n", await ReadAsync(other)); // the grant belongs to one document
        editor.Permissions.Revoke(editor.PluginId, DocumentPermissions.SecretsReveal, GrantScope.OfDocument(Guid.Parse(env.Id)));
        Assert.Equal("K=******\n", await ReadAsync(env));
    }

    [Fact]
    public async Task A_secret_without_a_name_is_refused()
    {
        await using var editor = await T.RawEditor();

        var error = await T.ErrorOf(() => editor.Raw!.RequestAsync(PluginServiceMethods.SecretGet, """{"name":""}"""));

        Assert.Equal(RpcErrorCodes.InvalidParams, error.Code);
    }
}
