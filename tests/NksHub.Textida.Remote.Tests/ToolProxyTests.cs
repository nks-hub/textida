using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NksHub.Textida.Remote;

namespace NksHub.Textida.Remote.Tests;

public sealed class ToolProxyTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "textida-toolproxy-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly ToolProxy _proxy = new();
    private readonly ToolNetworkConsent _consent;

    public ToolProxyTests()
    {
        Directory.CreateDirectory(_folder);
        _consent = new ToolNetworkConsent(Path.Combine(_folder, "tool-network.json"));
        _proxy.Consent = _consent;
    }

    public void Dispose()
    {
        _proxy.Dispose();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    /// <summary>Sends a request to the port of <paramref name="tool"/> (no login: a client like dotnet sends none) and returns what the proxy answers first.</summary>
    private async Task<string> ExchangeAsync(string tool, string head)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _proxy.PortOf(tool));
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        var buffer = new byte[1024];
        using var timeout = new CancellationTokenSource(5000);
        int read = await stream.ReadAsync(buffer, timeout.Token);
        return Encoding.ASCII.GetString(buffer, 0, read);
    }

    private const string Crlf = "\r\n";

    /// <summary>A server that answers the first connection with the first line it got, then closes.</summary>
    private static (TcpListener Listener, int Port, Task<string> Heard) Server(string answer)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var heard = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync();
            var stream = peer.GetStream();
            var buffer = new byte[2048];
            int read = await stream.ReadAsync(buffer);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(answer));
            return Encoding.ASCII.GetString(buffer, 0, read);
        });
        return (listener, ((IPEndPoint)listener.LocalEndpoint).Port, heard);
    }

    [Fact]
    public async Task A_host_that_is_not_on_the_list_of_the_tool_gets_403_and_is_logged_for_CONNECT_and_plain_requests()
    {
        _proxy.Register("t", ["api.nuget.org"]);
        _consent.Allow("t", ["api.nuget.org", "telemetry.example.test"]);
        Assert.StartsWith("HTTP/1.1 403", await ExchangeAsync("t", "CONNECT telemetry.example.test:443 HTTP/1.1" + Crlf + Crlf));
        Assert.StartsWith("HTTP/1.1 403", await ExchangeAsync("t", "GET http://plain.example.test/x HTTP/1.1" + Crlf + "Host: plain.example.test" + Crlf + Crlf));
        Assert.StartsWith("HTTP/1.1 403", await ExchangeAsync("other", "CONNECT api.nuget.org:443 HTTP/1.1" + Crlf + Crlf)); // the port of a tool that has no list
        Assert.Equal(["telemetry.example.test:443", "plain.example.test:80", "api.nuget.org:443"], _proxy.Refused);
    }

    [Fact]
    public async Task A_NuGet_host_passes_through_the_proxy_of_the_policy_after_consent_and_is_refused_without_it()
    {
        var upstream = Server("HTTP/1.1 200 OK" + Crlf + Crlf);
        try
        {
            _consent.SetProxy($"http://127.0.0.1:{upstream.Port}", blocked: false);
            _proxy.Register("roslyn", ["api.nuget.org", "globalcdn.nuget.org"]);
            string connect = "CONNECT api.nuget.org:443 HTTP/1.1" + Crlf + Crlf;

            Assert.StartsWith("HTTP/1.1 403", await ExchangeAsync("roslyn", connect)); // listed, but the user has not agreed
            Assert.Equal(["api.nuget.org:443"], _proxy.Refused);

            _consent.Allow("roslyn", ["api.nuget.org"]);
            Assert.StartsWith("HTTP/1.1 200", await ExchangeAsync("roslyn", connect));
            Assert.StartsWith("CONNECT api.nuget.org:443 HTTP/1.1", await upstream.Heard); // the proxy of the policy got the request

            Assert.StartsWith("HTTP/1.1 403", await ExchangeAsync("roslyn", "CONNECT globalcdn.nuget.org:443 HTTP/1.1" + Crlf + Crlf)); // another host needs its own consent
            _consent.RevokeAll();
            Assert.StartsWith("HTTP/1.1 403", await ExchangeAsync("roslyn", connect));
        }
        finally { upstream.Listener.Stop(); }
    }

    [Fact]
    public async Task A_consent_of_one_tool_does_not_open_the_port_of_another()
    {
        _proxy.Register("roslyn", ["api.nuget.org"]);
        _proxy.Register("csharp-ls", ["api.nuget.org"]);
        _consent.Allow("roslyn", ["api.nuget.org"]);
        Assert.StartsWith("HTTP/1.1 403", await ExchangeAsync("csharp-ls", "CONNECT api.nuget.org:443 HTTP/1.1" + Crlf + Crlf));
        Assert.True(_proxy.Permits("roslyn", "api.nuget.org"));
        Assert.False(_proxy.Permits("csharp-ls", "api.nuget.org"));
    }

    [Fact]
    public async Task Telemetry_stays_refused_with_a_consent_for_every_host_and_when_the_policy_cannot_be_honoured()
    {
        _proxy.Register("roslyn", ["*", "telemetry.visualstudio.microsoft.com", "api.nuget.org"]);
        _consent.Allow("roslyn", ["*", "telemetry.visualstudio.microsoft.com"]);
        Assert.StartsWith("HTTP/1.1 403", await ExchangeAsync("roslyn", "CONNECT telemetry.visualstudio.microsoft.com:443 HTTP/1.1" + Crlf + Crlf));
        Assert.StartsWith("HTTP/1.1 403", await ExchangeAsync("roslyn", "CONNECT x.events.data.microsoft.com:443 HTTP/1.1" + Crlf + Crlf));
        Assert.True(ToolProxy.IsTelemetry("DC.services.visualstudio.com"));
        Assert.False(_proxy.Permits("roslyn", "dc.services.visualstudio.com"));
        Assert.True(_proxy.Permits("roslyn", "api.nuget.org"));

        _consent.SetProxy(null, blocked: true); // a SOCKS proxy or a login in the policy
        Assert.False(_proxy.Permits("roslyn", "api.nuget.org"));
    }

    [Fact]
    public async Task Two_plain_requests_for_different_hosts_are_each_routed_by_their_own_target()
    {
        var first = Server("HTTP/1.1 200 OK" + Crlf + "Content-Length: 1" + Crlf + "Connection: close" + Crlf + Crlf + "A");
        var second = Server("HTTP/1.1 200 OK" + Crlf + "Content-Length: 1" + Crlf + "Connection: close" + Crlf + Crlf + "B");
        try
        {
            _proxy.Register("t", ["127.0.0.1"]);
            _consent.Allow("t", ["127.0.0.1"]);

            string a = await ExchangeAsync("t", $"GET /a HTTP/1.1{Crlf}Host: 127.0.0.1:{first.Port}{Crlf}Connection: keep-alive{Crlf}{Crlf}");
            string b = await ExchangeAsync("t", $"GET http://127.0.0.1:{second.Port}/b HTTP/1.1{Crlf}Host: ignored.example.test{Crlf}{Crlf}");

            Assert.EndsWith("A", a);
            Assert.EndsWith("B", b);
            string heard = await first.Heard;
            Assert.StartsWith("GET /a HTTP/1.1", heard);
            Assert.Contains("Connection: close", heard);
            Assert.DoesNotContain("Proxy-Authorization", heard);
            Assert.DoesNotContain("keep-alive", heard);
            Assert.StartsWith("GET http://127.0.0.1:", await second.Heard);
        }
        finally { first.Listener.Stop(); second.Listener.Stop(); }
    }

    [Fact]
    public void The_children_get_the_port_of_their_tool_in_every_variable_without_a_login_and_only_loopback_is_exempt()
    {
        var info = new ProcessStartInfo("program");
        info.Environment["HTTPS_PROXY"] = "http://elsewhere.example.test:3128";
        _proxy.Apply(info, "Microsoft.CodeAnalysis.LanguageServer");
        foreach (var name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy" })
            Assert.Equal($"http://127.0.0.1:{_proxy.PortOf("Microsoft.CodeAnalysis.LanguageServer")}", info.Environment[name]);
        Assert.NotEqual(_proxy.PortOf("Microsoft.CodeAnalysis.LanguageServer"), _proxy.PortOf("csharp-ls")); // one port per tool
        Assert.Equal("localhost,127.0.0.1,::1", info.Environment["NO_PROXY"]);
        Assert.Equal("localhost,127.0.0.1,::1", info.Environment["no_proxy"]);
    }
}
