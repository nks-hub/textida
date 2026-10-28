using System.Net;
using System.Text;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugin.Tests.Contracts;

/// <summary>
/// P8b.2.2, interface <b>net</b> (net/request). A request carries no document, so it has no revision to be stale; the failure that belongs to this interface is a network that does not answer
/// (<see cref="DocumentErrorCodes.NetworkUnavailable"/>), and the scenario for it is below.
/// </summary>
public class NetContractTests
{
    [Fact]
    public async Task Positive_a_request_goes_through_the_network_of_the_editor_with_its_method_headers_and_body()
    {
        await using var editor = await T.RawEditor(PluginPermissionNames.Network);
        editor.Network.Respond = request => new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("made", Encoding.UTF8, "text/plain") };
        var body = Convert.ToBase64String("{\"a\":1}"u8.ToArray());

        var answer = await editor.Raw!.RequestAsync(PluginServiceMethods.NetRequest, $$"""{"method":"POST","url":"https://example.test/api?q=1","headers":{"X-Key":"k"},"body":"{{body}}"}""");

        Assert.Equal((201, "made", "text/plain; charset=utf-8"), (answer.GetProperty("status").GetInt32(), Encoding.UTF8.GetString(answer.GetProperty("body").GetBytesFromBase64()), answer.GetProperty("contentType").GetString()));
        Assert.Equal(["POST https://example.test/api?q=1 {\"a\":1}"], editor.Network.Requests);
    }

    [Fact]
    public async Task Positive_an_http_client_built_on_the_editor_handler_reaches_the_same_network()
    {
        await using var editor = await T.RawEditor(PluginPermissionNames.Network);
        editor.Network.Respond = request => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("pong " + request.RequestUri!.AbsolutePath) };
        using var http = new HttpClient(new EditorHttpHandler(editor.Raw!.Connection));

        Assert.Equal("pong /ping", await http.GetStringAsync("https://example.test/ping"));
    }

    [Fact]
    public async Task Revision_conflict_has_no_counterpart_here_a_network_that_does_not_answer_is_network_unavailable()
    {
        await using var editor = await T.RawEditor(PluginPermissionNames.Network);
        editor.Network.Unreachable = true;

        var down = await T.ErrorOf(() => editor.Raw!.RequestAsync(PluginServiceMethods.NetRequest, """{"url":"https://example.test/x"}"""));

        Assert.Equal(DocumentErrorCodes.NetworkUnavailable, down.Code);
        // the request and the address stay out of the call log; only the method and the outcome are kept
        var entry = editor.CallLog.Entries().Single();
        Assert.Equal((PluginServiceMethods.NetRequest, CallOutcome.Failed), (entry.Method, entry.Outcome));
        Assert.DoesNotContain("example.test", entry.ToString());
    }

    [Fact]
    public async Task Permission_a_plugin_without_the_network_permission_is_refused_and_nothing_is_sent()
    {
        await using var editor = await T.RawEditor(DocumentPermissions.Read);

        var refused = await T.ErrorOf(() => editor.Raw!.RequestAsync(PluginServiceMethods.NetRequest, """{"url":"https://example.test/x"}"""));
        var viaClient = await Assert.ThrowsAsync<HttpRequestException>(() => new HttpClient(new EditorHttpHandler(editor.Raw!.Connection)).GetStringAsync("https://example.test/y"));

        Assert.Equal((DocumentErrorCodes.PermissionDenied, PluginPermissionNames.Network, "NotGranted"), (refused.Code, T.Data(refused, "permission"), T.Data(refused, "reason")));
        Assert.Contains("network", viaClient.Message);
        Assert.Empty(editor.Network.Requests);
    }

    [Fact]
    public async Task Parameters_that_are_not_a_plain_http_address_are_refused_before_anything_is_sent()
    {
        await using var editor = await T.RawEditor(PluginPermissionNames.Network);

        foreach (var parameters in new[] { """{"url":"file:///etc/passwd"}""", """{"url":"https://user:pw@example.test/"}""", """{"url":"https://example.test/","method":"TRACE"}""", """{"method":"GET"}""" })
            Assert.Equal(RpcErrorCodes.InvalidParams, (await T.ErrorOf(() => editor.Raw!.RequestAsync(PluginServiceMethods.NetRequest, parameters))).Code);

        Assert.Empty(editor.Network.Requests);
    }
}
