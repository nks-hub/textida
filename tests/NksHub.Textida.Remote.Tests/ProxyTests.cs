using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using NksHub.Textida.Remote;

namespace NksHub.Textida.Remote.Tests;

public class ProxyTests
{
    public static IEnumerable<object[]> Matrix()
    {
        foreach (var protocol in Servers.Both.Select(o => (RemoteProtocol)o[0]))
            foreach (var kind in new[] { ProxyType.Http, ProxyType.Socks4, ProxyType.Socks5 })
                if (!(protocol == RemoteProtocol.Ftps && kind == ProxyType.Socks4)) // refused with a clear error, see Socks4IsRefusedForFtp
                    yield return [protocol, kind];
    }

    [Theory, MemberData(nameof(Matrix))]
    public async Task TrafficGoesThroughTheProxyForEveryTypeAndProtocol(RemoteProtocol protocol, ProxyType kind)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("a.txt"), "through the proxy");
        await using var server = await Servers.StartAsync(protocol, dir.Path, dir.Path);
        using var proxy = new TestProxy(kind);

        await using var client = ConnectionTests.Create(server.Connection(trusted: server.Identity, proxy: proxy.Settings), DenyUnknownServers.Instance);
        await client.ConnectAsync(default);
        var ms = new MemoryStream();
        await client.DownloadAsync("/a.txt", ms, default);

        Assert.Equal("through the proxy", Encoding.UTF8.GetString(ms.ToArray()));
        Assert.True(proxy.Tunnels >= 1, "no connection came through the proxy");
        // An FTP session has a control connection and a data connection per transfer; both must pass the proxy.
        if (protocol == RemoteProtocol.Ftps) Assert.True(proxy.Tunnels >= 2, $"only {proxy.Tunnels} connection(s) used the proxy");
    }

    [Theory, MemberData(nameof(Matrix))]
    public async Task TheProxyOfTheNetworkPolicyCarriesEveryTypeAndProtocolForAConnectionWithoutItsOwn(RemoteProtocol protocol, ProxyType kind)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("a.txt"), "by policy");
        await using var server = await Servers.StartAsync(protocol, dir.Path, dir.Path);
        using var proxy = new TestProxy(kind);
        var connection = server.Connection(trusted: server.Identity);
        var secrets = new MemoryCredentials();
        secrets.Set(connection.SecretKey, "p");

        await using var client = RemoteClients.Create(connection, secrets, DenyUnknownServers.Instance, new NetworkPolicy(proxy.Settings, null));
        await client.ConnectAsync(default);
        var ms = new MemoryStream();
        await client.DownloadAsync("/a.txt", ms, default);

        Assert.Equal("by policy", Encoding.UTF8.GetString(ms.ToArray()));
        Assert.True(proxy.Tunnels >= 1, "no connection came through the proxy of the policy");
        if (protocol == RemoteProtocol.Ftps) Assert.True(proxy.Tunnels >= 2, $"only {proxy.Tunnels} connection(s) used the proxy of the policy");
    }

    [Fact]
    public async Task ASocks4ProxyOfThePolicyIsRefusedForFtpsWithAClearErrorAndAcceptedForSftp()
    {
        var policy = new NetworkPolicy(new ProxySettings(ProxyType.Socks4, "198.51.100.1", 1080), null);
        var ftps = new RemoteConnection { Name = "f", Host = "192.0.2.1", Protocol = RemoteProtocol.Ftps };
        var e = Assert.Throws<RemoteException>(() => RemoteClients.Create(ftps, new MemoryCredentials(), DenyUnknownServers.Instance, policy));
        Assert.Equal(RemoteError.UnsupportedCombination, e.Error);
        Assert.Contains("SOCKS4", e.Message);

        var sftp = ftps with { Protocol = RemoteProtocol.Sftp };
        await using var client = RemoteClients.Create(sftp, new MemoryCredentials(), DenyUnknownServers.Instance, policy);
        Assert.NotNull(client);
    }

    [Fact]
    public void AnUnsupportedProxyTypeInThePolicyStopsTheClientBeforeAnyConnection()
    {
        var policy = new NetworkPolicy(new ProxySettings((ProxyType)99, "198.51.100.1", 8080), null);
        var connection = new RemoteConnection { Name = "f", Host = "192.0.2.1", Protocol = RemoteProtocol.Sftp };
        var e = Assert.Throws<RemoteException>(() => RemoteClients.Create(connection, new MemoryCredentials(), DenyUnknownServers.Instance, policy));
        Assert.Equal(RemoteError.UnsupportedCombination, e.Error);
        Assert.Contains("http, socks4, socks5", e.Message);
    }

    [Theory]
    [InlineData(RemoteProtocol.Ftps, ProxyType.Http)]
    [InlineData(RemoteProtocol.Ftps, ProxyType.Socks5)]
    [InlineData(RemoteProtocol.Sftp, ProxyType.Http)]
    [InlineData(RemoteProtocol.Sftp, ProxyType.Socks5)]
    public async Task ProxyCredentialsComeFromTheCredentialStoreAndAWrongOneFails(RemoteProtocol protocol, ProxyType kind)
    {
        if (protocol == RemoteProtocol.Sftp && !SftpServer.Available) return; // reported by ConnectionTests.SftpServerCanRunHere
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("a.txt"), "x");
        await using var server = await Servers.StartAsync(protocol, dir.Path, dir.Path);
        using var proxy = new TestProxy(kind, "pu", "proxy-secret");
        var connection = server.Connection(trusted: server.Identity, proxy: proxy.Settings);

        var good = new MemoryCredentials();
        good.Set(connection.SecretKey, "p");
        good.Set(connection.ProxySecretKey, "proxy-secret");
        await using (var client = RemoteClients.Create(connection, good, DenyUnknownServers.Instance))
        {
            await client.ConnectAsync(default);
            Assert.NotNull(await client.StatAsync("/a.txt", default));
        }

        var bad = new MemoryCredentials();
        bad.Set(connection.SecretKey, "p");
        bad.Set(connection.ProxySecretKey, "wrong");
        await using var refused = RemoteClients.Create(connection, bad, DenyUnknownServers.Instance);
        var e = await Assert.ThrowsAsync<RemoteException>(() => refused.ConnectAsync(default));
        Assert.Equal(RemoteError.ConnectionFailed, e.Error);
        Assert.True(proxy.Refused >= 1);
    }

    [Theory]
    [InlineData(RemoteProtocol.Ftps)]
    [InlineData(RemoteProtocol.Sftp)]
    public async Task ADeadProxyIsAConnectionError(RemoteProtocol protocol)
    {
        if (protocol == RemoteProtocol.Sftp && !SftpServer.Available) return;
        using var dir = new TempDir();
        await using var server = await Servers.StartAsync(protocol, dir.Path, dir.Path);
        var dead = new ProxySettings(ProxyType.Socks5, "127.0.0.1", FtpsServer.FreePort());
        await using var client = ConnectionTests.Create(server.Connection(trusted: server.Identity, proxy: dead), DenyUnknownServers.Instance);
        Assert.Equal(RemoteError.ConnectionFailed, (await Assert.ThrowsAsync<RemoteException>(() => client.ConnectAsync(default))).Error);
    }

    [Fact]
    public void Socks4IsRefusedForFtp()
    {
        // FluentFTP sends the data connection of a SOCKS4 session to the control port, so no transfer would work; better an error than a hang.
        var c = new RemoteConnection { Host = "192.0.2.1", Protocol = RemoteProtocol.Ftps, Proxy = new(ProxyType.Socks4, "198.51.100.1", 1080) };
        var e = Assert.Throws<RemoteException>(c.Validate);
        Assert.Equal(RemoteError.UnsupportedCombination, e.Error);
        Assert.Contains("SOCKS4", e.Message);
        new RemoteConnection { Host = "192.0.2.1", Protocol = RemoteProtocol.Sftp, Proxy = new(ProxyType.Socks4, "198.51.100.1", 1080) }.Validate();
    }

    [Fact]
    public void UnsupportedProxyTypesAndIncompleteProxiesFailWithAClearError()
    {
        var c = new RemoteConnection { Host = "192.0.2.1", Proxy = new((ProxyType)99, "198.51.100.1", 8080) };
        Assert.Equal(RemoteError.UnsupportedCombination, Assert.Throws<RemoteException>(c.Validate).Error);
        Assert.Throws<RemoteException>(() => NetworkPolicy.Validate(new ProxySettings(ProxyType.Socks5, "", 0)));

        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("n.json"), """{ "type": "socks5h", "host": "198.51.100.1", "port": 1080 }""");
        var e = Assert.Throws<RemoteException>(() => NetworkPolicy.Load(dir.Combine("n.json"), new MemoryCredentials()));
        Assert.Equal(RemoteError.UnsupportedCombination, e.Error);
        Assert.Contains("socks4", e.Message);
    }

    [Fact]
    public async Task GlobalPolicyAppliesToConnectionsWithoutTheirOwnProxyAndTheOwnOneWins()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("a.txt"), "x");
        await using var server = await FtpsServer.StartAsync(dir.Path);
        using var global = new TestProxy(ProxyType.Socks5, "gu", "gp");
        using var own = new TestProxy(ProxyType.Http);
        var secrets = new MemoryCredentials();
        NetworkPolicy.Save(dir.Combine("network.json"), global.Settings, "gp", secrets);
        Assert.DoesNotContain("gp", File.ReadAllText(dir.Combine("network.json")));
        var policy = NetworkPolicy.Load(dir.Combine("network.json"), secrets);

        var plain = server.Connection(trusted: server.Identity);
        secrets.Set(plain.SecretKey, "p");
        await using (var c = RemoteClients.Create(plain, secrets, DenyUnknownServers.Instance, policy))
        {
            await c.ConnectAsync(default);
            await c.StatAsync("/a.txt", default);
        }
        Assert.True(global.Tunnels >= 1);
        Assert.Equal(0, own.Tunnels);

        var specific = server.Connection(trusted: server.Identity, proxy: own.Settings);
        secrets.Set(specific.SecretKey, "p");
        await using (var c = RemoteClients.Create(specific, secrets, DenyUnknownServers.Instance, policy))
        {
            await c.ConnectAsync(default);
            await c.StatAsync("/a.txt", default);
        }
        Assert.True(own.Tunnels >= 1);
    }

    [Theory]
    [InlineData(ProxyType.Http)]
    [InlineData(ProxyType.Socks4)]
    [InlineData(ProxyType.Socks5)]
    public async Task HttpHandlerOfThePolicyRoutesRequestsThroughTheProxy(ProxyType kind)
    {
        using var cert = FtpsServer.NewCertificate();
        var web = new TcpListener(IPAddress.Loopback, 0);
        web.Start();
        int webPort = ((IPEndPoint)web.LocalEndpoint).Port;
        var serving = Task.Run(async () =>
        {
            using var tcp = await web.AcceptTcpClientAsync();
            await using var tls = new SslStream(tcp.GetStream());
            await tls.AuthenticateAsServerAsync(cert);
            var buf = new byte[4096];
            _ = await tls.ReadAsync(buf);
            await tls.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray());
            await tls.FlushAsync();
        });
        using var proxy = new TestProxy(kind);
        var policy = new NetworkPolicy(proxy.Settings, null);
        using var handler = policy.CreateHttpHandler();
        handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        using var http = new HttpClient(handler);

        Assert.Equal("ok", await http.GetStringAsync($"https://127.0.0.1:{webPort}/"));
        Assert.Equal(1, proxy.Tunnels);
        web.Stop();
        await serving;
    }
}
