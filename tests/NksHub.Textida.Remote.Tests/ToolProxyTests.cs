using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NksHub.Textida.Remote;

namespace NksHub.Textida.Remote.Tests;

public class ToolProxyTests
{
    private static async Task<string> RequestAsync(ToolProxy proxy, string head)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        var buffer = new byte[512];
        using var timeout = new CancellationTokenSource(5000);
        int read = await stream.ReadAsync(buffer, timeout.Token);
        return Encoding.ASCII.GetString(buffer, 0, read);
    }

    [Fact]
    public async Task A_host_outside_the_list_gets_403_and_is_logged_for_CONNECT_and_plain_requests()
    {
        using var proxy = new ToolProxy();
        Assert.StartsWith("HTTP/1.1 403", await RequestAsync(proxy, "CONNECT telemetry.example.test:443 HTTP/1.1\r\nHost: telemetry.example.test:443\r\n\r\n"));
        Assert.StartsWith("HTTP/1.1 403", await RequestAsync(proxy, "GET http://plain.example.test/x HTTP/1.1\r\nHost: plain.example.test\r\n\r\n"));
        Assert.Equal(["telemetry.example.test:443", "plain.example.test:80"], proxy.Refused);
    }

    [Fact]
    public async Task An_allowed_host_passes_only_while_the_policy_permits_it()
    {
        var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        try
        {
            int port = ((IPEndPoint)upstream.LocalEndpoint).Port;
            var echo = Task.Run(async () =>
            {
                using var peer = await upstream.AcceptTcpClientAsync();
                var buffer = new byte[16];
                int read = await peer.GetStream().ReadAsync(buffer);
                await peer.GetStream().WriteAsync(buffer.AsMemory(0, read));
            });
            using var proxy = new ToolProxy(["127.0.0.1"]);
            string connect = $"CONNECT 127.0.0.1:{port} HTTP/1.1\r\n\r\n";

            Assert.StartsWith("HTTP/1.1 403", await RequestAsync(proxy, connect)); // listed, but the policy and the consent say no
            Assert.Equal([$"127.0.0.1:{port}"], proxy.Refused);

            proxy.Permit = _ => true;
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(connect));
            var buffer = new byte[64];
            using var timeout = new CancellationTokenSource(5000);
            int read = await stream.ReadAsync(buffer, timeout.Token);
            Assert.StartsWith("HTTP/1.1 200", Encoding.ASCII.GetString(buffer, 0, read));
            await stream.WriteAsync("ping"u8.ToArray());
            read = await stream.ReadAsync(buffer, timeout.Token);
            Assert.Equal("ping", Encoding.ASCII.GetString(buffer, 0, read));
            await echo;
        }
        finally { upstream.Stop(); }
    }

    [Fact]
    public void The_children_get_the_proxy_in_every_variable_and_only_loopback_is_exempt()
    {
        using var proxy = new ToolProxy();
        var info = new ProcessStartInfo("program");
        info.Environment["HTTPS_PROXY"] = "http://elsewhere.example.test:3128";
        proxy.Apply(info);
        foreach (var name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy" })
            Assert.Equal(proxy.Url, info.Environment[name]);
        Assert.Equal("localhost,127.0.0.1,::1", info.Environment["NO_PROXY"]);
        Assert.Equal("localhost,127.0.0.1,::1", info.Environment["no_proxy"]);
    }
}
