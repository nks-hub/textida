using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using Xunit;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>
/// P5.4.4: <c>net/request</c>, the network requests of plugins through the editor. The handler the editor builds points at an HTTP proxy in the test that answers every
/// request itself, so an answer can only come through the proxy; the addresses are documentation ranges nobody listens on.
/// </summary>
public sealed class PluginNetworkTests : IDisposable
{
    private const string Plugin = "test.plugin";

    /// <summary>An HTTP proxy that answers itself and keeps the request line, the headers and the body of what it was sent.</summary>
    private sealed class AnsweringProxy : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly List<string> _requests = [];
        public string Answer = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 8\r\nConnection: close\r\n\r\nproxied!";
        public bool Hang;
        public int Port { get; }

        public AnsweringProxy()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(LoopAsync);
        }

        public IReadOnlyList<string> Requests { get { lock (_requests) return [.. _requests]; } }

        public HttpClientHandler Handler() => new() { Proxy = new WebProxy(new Uri($"http://127.0.0.1:{Port}"), false), UseProxy = true };

        private async Task LoopAsync()
        {
            try
            {
                while (true)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    var s = client.GetStream();
                    var head = new StringBuilder();
                    var one = new byte[1];
                    while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                    {
                        if (await s.ReadAsync(one, _stop.Token) == 0) break;
                        head.Append((char)one[0]);
                    }
                    var length = head.ToString().Split("\r\n").FirstOrDefault(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) is { } line ? int.Parse(line[15..].Trim()) : 0;
                    var body = new byte[length];
                    await s.ReadExactlyAsync(body, _stop.Token);
                    lock (_requests) _requests.Add(head + Encoding.UTF8.GetString(body));
                    if (Hang) await Task.Delay(Timeout.Infinite, _stop.Token);
                    await s.WriteAsync(Encoding.ASCII.GetBytes(Answer), _stop.Token);
                }
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException or IOException) { }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }
    }

    private readonly AnsweringProxy _proxy = new();
    private readonly PluginPermissions _permissions = new();
    private readonly PluginCallLog _log = new(100);

    public void Dispose() => _proxy.Dispose();

    private PluginNetwork Network(PluginNetworkOptions? options = null, Func<HttpClientHandler>? handler = null) =>
        new(_permissions, handler ?? _proxy.Handler, options ?? new PluginNetworkOptions(TimeSpan.FromSeconds(10), 1 << 20), _log);

    private static async Task<JsonElement> CallAsync(PluginNetwork network, string json)
    {
        using var p = JsonDocument.Parse(json);
        var answer = await network.HandleAsync(Plugin, PluginNetwork.Request, p.RootElement, default);
        return JsonDocument.Parse(answer!).RootElement.Clone();
    }

    [Fact]
    public async Task A_plugin_without_the_network_permission_gets_an_error_and_nothing_is_sent()
    {
        var e = await Assert.ThrowsAsync<RpcException>(() => CallAsync(Network(), """{"url":"http://192.0.2.10/x"}"""));

        Assert.Equal(DocumentErrorCodes.PermissionDenied, e.Code);
        Assert.Empty(_proxy.Requests);
        Assert.Equal(CallOutcome.Denied, Assert.Single(_log.Entries()).Outcome);
        Assert.Contains(PluginPermissionNames.Network, PluginPermissionNames.Declarable); // a manifest may ask for it
    }

    [Fact]
    public async Task The_request_goes_out_through_the_handler_of_the_editor_with_its_method_headers_and_body()
    {
        _permissions.Grant(Plugin, PluginPermissionNames.Network, GrantScope.All);
        var body = Convert.ToBase64String("{\"a\":1}"u8.ToArray());

        var answer = await CallAsync(Network(), $$"""{"method":"POST","url":"http://192.0.2.10/api/x?q=1","headers":{"X-Api-Key":"k-1","Content-Type":"application/json"},"body":"{{body}}"}""");

        Assert.Equal(200, answer.GetProperty("status").GetInt32());
        Assert.Equal("proxied!", Encoding.UTF8.GetString(answer.GetProperty("body").GetBytesFromBase64()));
        Assert.Equal("text/plain", answer.GetProperty("contentType").GetString());
        var sent = Assert.Single(_proxy.Requests);
        Assert.StartsWith("POST http://192.0.2.10/api/x?q=1 HTTP/1.1\r\n", sent); // the absolute form only a proxy gets
        Assert.Contains("X-Api-Key: k-1\r\n", sent);
        Assert.Contains("Content-Type: application/json\r\n", sent);
        Assert.EndsWith("{\"a\":1}", sent);
        var entry = Assert.Single(_log.Entries());
        Assert.Equal((PluginNetwork.Request, CallOutcome.Ok), (entry.Method, entry.Outcome)); // no address, no header in the log by construction
    }

    [Fact]
    public async Task A_redirect_comes_back_to_the_plugin_and_is_not_followed_so_its_headers_stay_with_the_first_host()
    {
        _permissions.Grant(Plugin, PluginPermissionNames.Network, GrantScope.All);
        _proxy.Answer = "HTTP/1.1 302 Found\r\nLocation: http://203.0.113.5/other\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";

        var answer = await CallAsync(Network(), """{"url":"http://192.0.2.10/x","headers":{"X-Api-Key":"k-1"}}""");

        Assert.Equal(302, answer.GetProperty("status").GetInt32());
        Assert.Equal("http://203.0.113.5/other", answer.GetProperty("location").GetString());
        Assert.Single(_proxy.Requests);
    }

    [Fact]
    public async Task An_answer_over_the_limit_and_a_proxy_that_does_not_answer_are_network_errors()
    {
        _permissions.Grant(Plugin, PluginPermissionNames.Network, GrantScope.All);

        var large = await Assert.ThrowsAsync<RpcException>(() => CallAsync(Network(new PluginNetworkOptions(TimeSpan.FromSeconds(10), 4)), """{"url":"http://192.0.2.10/x"}"""));
        Assert.Equal(DocumentErrorCodes.NetworkUnavailable, large.Code);
        Assert.Contains("more than 4 bytes", large.Message);

        _proxy.Hang = true;
        var slow = await Assert.ThrowsAsync<RpcException>(() => CallAsync(Network(new PluginNetworkOptions(TimeSpan.FromMilliseconds(300), 1 << 20)), """{"url":"http://192.0.2.10/y"}"""));
        Assert.Equal(DocumentErrorCodes.NetworkUnavailable, slow.Code);
        Assert.Contains("did not answer", slow.Message);
    }

    [Fact]
    public async Task A_network_policy_the_editor_cannot_read_stops_the_request_instead_of_going_around_the_proxy()
    {
        _permissions.Grant(Plugin, PluginPermissionNames.Network, GrantScope.All);
        bool built = false;

        var e = await Assert.ThrowsAsync<RpcException>(() => CallAsync(Network(handler: () => { built = true; throw new InvalidOperationException("bad proxy file"); }), """{"url":"http://192.0.2.10/x"}"""));

        Assert.True(built);
        Assert.Equal(DocumentErrorCodes.NetworkUnavailable, e.Code);
        Assert.Contains("bad proxy file", e.Message);
        Assert.Empty(_proxy.Requests);
    }

    [Theory]
    [InlineData("""{"url":"file:///C:/x.txt"}""")]
    [InlineData("""{"url":"ftp://192.0.2.10/x"}""")]
    [InlineData("""{"url":"http://user:pass@192.0.2.10/x"}""")]
    [InlineData("""{"url":"/relative"}""")]
    [InlineData("""{"url":"http://192.0.2.10/x","method":"CONNECT"}""")]
    [InlineData("""{"url":"http://192.0.2.10/x","body":"not base64!"}""")]
    [InlineData("""{"url":"http://192.0.2.10/x","headers":{"Bad Name":"v"}}""")]
    public async Task Only_http_requests_the_api_describes_are_sent(string json)
    {
        _permissions.Grant(Plugin, PluginPermissionNames.Network, GrantScope.All);

        var e = await Assert.ThrowsAsync<RpcException>(() => CallAsync(Network(), json));

        Assert.Equal(RpcErrorCodes.InvalidParams, e.Code);
        Assert.Empty(_proxy.Requests);
    }

    [Fact]
    public async Task Revoking_the_permission_cancels_a_request_in_flight()
    {
        _permissions.Grant(Plugin, PluginPermissionNames.Network, GrantScope.All);
        _proxy.Hang = true;
        var call = CallAsync(Network(), """{"url":"http://192.0.2.10/x"}""");
        for (int i = 0; i < 500 && _proxy.Requests.Count == 0; i++) await Task.Delay(10);
        Assert.Single(_proxy.Requests);

        _permissions.Revoke(Plugin, PluginPermissionNames.Network, GrantScope.All);

        var e = await Assert.ThrowsAsync<RpcException>(() => call);
        Assert.Equal(DocumentErrorCodes.PermissionDenied, e.Code);
        Assert.Contains("Revoked", e.DataJson);
    }
}
