using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Remote;
using NksHub.Textida.Remote.Tests;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P5.4.4: the proxy of the network policy (File, Remote Files, Network Proxy) is the one the update check and the requests plugins send through <c>net/request</c> use,
/// with its password from the credential store, and a policy the editor cannot read stops them instead of letting them go around the proxy.
/// </summary>
[Collection("LocState")]
public class NetworkPolicyAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly MemoryCredentials _secrets = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public NetworkPolicyAppTests()
    {
        _app = TestApp.Prepare(_temp, new ScriptedDialogs());
        _app.Remote = new RemoteServices(_app.ProfileDirectory, _secrets);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    /// <summary>A web server on loopback that answers <c>ok</c> to each request, or a redirect to the address <paramref name="redirectTo"/> gives for the request (counted from 0).</summary>
    private static (int Port, Task Serving) Serve(TcpListener web, int requests, Func<int, string?>? redirectTo = null) => (((IPEndPoint)web.LocalEndpoint).Port, Task.Run(async () =>
    {
        for (int i = 0; i < requests; i++)
        {
            using var tcp = await web.AcceptTcpClientAsync();
            var s = tcp.GetStream();
            var head = new StringBuilder();
            var one = new byte[1];
            while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal) && await s.ReadAsync(one) > 0) head.Append((char)one[0]);
            string? location = redirectTo?.Invoke(i);
            await s.WriteAsync(location is null
                ? "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray()
                : Encoding.ASCII.GetBytes($"HTTP/1.1 302 Found\r\nLocation: {location}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
        }
    }));

    private async Task<JsonElement> PluginGetAsync(int port)
    {
        using var p = JsonDocument.Parse($$"""{"url":"http://127.0.0.1:{{port}}/feed"}""");
        var answer = await _app.Plugins.Network.HandleAsync(PluginServices.WikiPluginId, PluginNetwork.Request, p.RootElement, default);
        return JsonDocument.Parse(answer!).RootElement.Clone();
    }

    [AvaloniaFact]
    public async Task A_plugin_request_through_the_editor_uses_the_proxy_saved_in_the_policy_from_the_next_request_on_with_its_password()
    {
        var web = new TcpListener(IPAddress.Loopback, 0);
        web.Start();
        var (port, serving) = Serve(web, 2);
        using var proxy = new TestProxy(ProxyType.Socks5, "pu", "proxy-secret-4410");

        var direct = await PluginGetAsync(port);
        Assert.Equal(200, direct.GetProperty("status").GetInt32());
        Assert.Equal(0, proxy.Tunnels);

        NetworkPolicy.Save(_app.Remote.NetworkPolicyPath, proxy.Settings, "proxy-secret-4410", _app.Remote.Secrets);
        var proxied = await PluginGetAsync(port);

        Assert.Equal(200, proxied.GetProperty("status").GetInt32());
        Assert.Equal("ok", Encoding.UTF8.GetString(proxied.GetProperty("body").GetBytesFromBase64()));
        Assert.Equal(1, proxy.Tunnels); // the SOCKS5 login took the password from the credential store
        Assert.DoesNotContain("proxy-secret-4410", File.ReadAllText(_app.Remote.NetworkPolicyPath));
        web.Stop();
        await serving;
    }

    [AvaloniaFact]
    public async Task The_client_of_the_update_check_goes_through_the_proxy_saved_in_the_policy_with_its_password()
    {
        var web = new TcpListener(IPAddress.Loopback, 0);
        web.Start();
        var (port, serving) = Serve(web, 2);
        using var proxy = new TestProxy(ProxyType.Socks5, "pu", "proxy-secret-4411");

        using (var direct = _w.CreateUpdateHttpClient()) Assert.Equal("ok", await direct.GetStringAsync($"http://127.0.0.1:{port}/feed"));
        Assert.Equal(0, proxy.Tunnels);

        NetworkPolicy.Save(_app.Remote.NetworkPolicyPath, proxy.Settings, "proxy-secret-4411", _app.Remote.Secrets);
        using (var proxied = _w.CreateUpdateHttpClient()) Assert.Equal("ok", await proxied.GetStringAsync($"http://127.0.0.1:{port}/feed"));

        Assert.Equal(1, proxy.Tunnels);
        web.Stop();
        await serving;
    }

    [AvaloniaFact]
    public async Task The_client_of_the_update_check_follows_a_redirect_to_a_page_of_the_same_host()
    {
        var web = new TcpListener(IPAddress.Loopback, 0);
        web.Start();
        var (port, serving) = Serve(web, 2, i => i == 0 ? $"http://127.0.0.1:{((IPEndPoint)web.LocalEndpoint).Port}/moved" : null);

        using (var http = _w.CreateUpdateHttpClient()) Assert.Equal("ok", await http.GetStringAsync($"http://127.0.0.1:{port}/feed"));

        await serving;
        web.Stop();
    }

    [AvaloniaFact]
    public async Task The_client_of_the_update_check_does_not_follow_a_redirect_to_another_host()
    {
        var web = new TcpListener(IPAddress.Loopback, 0);
        web.Start();
        var (port, serving) = Serve(web, 1, _ => $"http://localhost:{((IPEndPoint)web.LocalEndpoint).Port}/elsewhere");

        using (var http = _w.CreateUpdateHttpClient())
        {
            using var answer = await http.GetAsync($"http://127.0.0.1:{port}/feed");
            Assert.Equal(HttpStatusCode.Found, answer.StatusCode); // handed back, not followed
        }

        await serving;
        web.Stop();
    }

    [AvaloniaFact]
    public async Task Remote_files_of_the_editor_connect_through_the_proxy_saved_in_the_policy()
    {
        string root = Directory.CreateDirectory(Path.Combine(_temp.Path, "server")).FullName;
        File.WriteAllText(Path.Combine(root, "a.txt"), "x");
        await using var server = await FtpsServer.StartAsync(root);
        using var proxy = new TestProxy(ProxyType.Socks5, "pu", "proxy-secret-4412");
        NetworkPolicy.Save(_app.Remote.NetworkPolicyPath, proxy.Settings, "proxy-secret-4412", _app.Remote.Secrets);
        var connection = server.Connection(trusted: server.Identity);
        _secrets.Set(connection.SecretKey, server.Password);

        await using var client = _app.Remote.CreateClient(connection, DenyUnknownServers.Instance);
        await client.ConnectAsync(default);
        var downloaded = new MemoryStream();
        await client.DownloadAsync("/a.txt", downloaded, default);

        Assert.Equal("x", Encoding.UTF8.GetString(downloaded.ToArray()));
        Assert.True(proxy.Tunnels >= 2, $"only {proxy.Tunnels} connection(s) of the FTPS session used the proxy"); // the control connection and the one of the transfer
    }

    [AvaloniaFact]
    public async Task A_policy_file_the_editor_cannot_read_stops_plugin_requests_and_the_update_check()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_app.Remote.NetworkPolicyPath)!);
        File.WriteAllText(_app.Remote.NetworkPolicyPath, """{ "type": "socks5h", "host": "198.51.100.1", "port": 1080 }""");

        var e = await Assert.ThrowsAsync<NksHub.Textida.Rpc.RpcException>(() => PluginGetAsync(1));
        Assert.Equal(NksHub.Textida.PluginHost.Documents.DocumentErrorCodes.NetworkUnavailable, e.Code);
        Assert.Contains("socks4", e.Message); // the message names the supported types

        _w.CheckForUpdates();
        await _w.UpdateTask;
        Assert.StartsWith(Loc.Get(K.UpdateFailed, ("reason", "")).TrimEnd(), _w.StatusMessageText);
        Assert.Contains("socks4", _w.StatusMessageText);
    }
}
