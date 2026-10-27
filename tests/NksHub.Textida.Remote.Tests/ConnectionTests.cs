using NksHub.Textida.Remote;

namespace NksHub.Textida.Remote.Tests;

public class ConnectionTests
{
    internal static IRemoteClient Create(RemoteConnection connection, IServerTrustPrompt trust, string? password = "p", ProxySettings? proxy = null)
    {
        var creds = new MemoryCredentials();
        if (password is not null) creds.Set(connection.SecretKey, password);
        return RemoteClients.Create(connection, creds, trust);
    }

    [SftpFact]
    public void SftpServerCanRunHere() { }

    [Theory, MemberData(nameof(ServersData))]
    public async Task PinnedIdentityConnectsWithoutAskingAndFilesAreReadable(RemoteProtocol protocol)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("a.txt"), "hello");
        await using var server = await Servers.StartAsync(protocol, dir.Path, dir.Path);
        var ask = new AcceptAll();
        await using var client = Create(server.Connection(trusted: server.Identity), ask);
        await client.ConnectAsync(default);

        Assert.Empty(ask.Asked);
        Assert.Null(client.ConfirmedIdentity);
        var stat = await client.StatAsync("/a.txt", default);
        Assert.Equal(5, stat!.Value.Size);
        Assert.Null(await client.StatAsync("/missing.txt", default));
        var ms = new MemoryStream();
        await client.DownloadAsync("/a.txt", ms, default);
        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task UnknownServerIsRefusedWithoutConfirmationAndAcceptedWithIt(RemoteProtocol protocol)
    {
        using var dir = new TempDir();
        await using var server = await Servers.StartAsync(protocol, dir.Path, dir.Path);

        await using (var refused = Create(server.Connection(), DenyUnknownServers.Instance))
        {
            var e = await Assert.ThrowsAsync<RemoteException>(() => refused.ConnectAsync(default));
            Assert.Equal(RemoteError.UntrustedServer, e.Error);
        }

        var ask = new AcceptAll();
        await using var client = Create(server.Connection(), ask);
        await client.ConnectAsync(default);
        Assert.Equal(server.Identity, client.ConfirmedIdentity);
        Assert.Single(ask.Asked);
        Assert.Equal(protocol == RemoteProtocol.Sftp ? TrustReason.FirstUse : TrustReason.Invalid, ask.Asked[0].Reason);
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task ChangedIdentityDoesNotConnectWithoutConfirmation(RemoteProtocol protocol)
    {
        using var dir = new TempDir();
        using var other = new TempDir();
        await using var original = await Servers.StartAsync(protocol, dir.Path, dir.Path);
        // A second server with its own freshly generated key or certificate plays "the server key changed".
        await using var changed = await Servers.StartAsync(protocol, other.Path, other.Path);
        Assert.NotEqual(original.Identity, changed.Identity);

        await using (var refused = Create(changed.Connection(trusted: original.Identity), DenyUnknownServers.Instance))
        {
            var e = await Assert.ThrowsAsync<RemoteException>(() => refused.ConnectAsync(default));
            Assert.Equal(RemoteError.UntrustedServer, e.Error);
        }

        var ask = new AcceptAll();
        await using var client = Create(changed.Connection(trusted: original.Identity), ask);
        await client.ConnectAsync(default);
        Assert.Equal(TrustReason.Changed, Assert.Single(ask.Asked).Reason);
        Assert.Equal(changed.Identity, client.ConfirmedIdentity);
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task WrongPasswordAndMissingPasswordGiveClearErrors(RemoteProtocol protocol)
    {
        using var dir = new TempDir();
        await using var server = await Servers.StartAsync(protocol, dir.Path, dir.Path);

        await using (var wrong = Create(server.Connection(trusted: server.Identity), DenyUnknownServers.Instance, password: "nope"))
            Assert.Equal(RemoteError.AuthenticationFailed, (await Assert.ThrowsAsync<RemoteException>(() => wrong.ConnectAsync(default))).Error);

        await using var missing = Create(server.Connection(trusted: server.Identity), DenyUnknownServers.Instance, password: null);
        Assert.Equal(RemoteError.CredentialMissing, (await Assert.ThrowsAsync<RemoteException>(() => missing.ConnectAsync(default))).Error);
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task ConnectionDroppedByTheServerIsReportedAsLost(RemoteProtocol protocol)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("a.txt"), "x");
        var server = await Servers.StartAsync(protocol, dir.Path, dir.Path);
        await using var client = Create(server.Connection(trusted: server.Identity), DenyUnknownServers.Instance);
        await client.ConnectAsync(default);
        Assert.NotNull(await client.StatAsync("/a.txt", default));
        await server.DisposeAsync();

        var e = await Assert.ThrowsAsync<RemoteException>(() => client.StatAsync("/a.txt", default));
        Assert.Equal(RemoteError.ConnectionLost, e.Error);
    }

    [Fact]
    public async Task ClosedPortGivesConnectionFailed()
    {
        var c = new RemoteConnection { Protocol = RemoteProtocol.Sftp, Host = "127.0.0.1", Port = FtpsServer.FreePort(), UserName = "u" };
        await using var client = Create(c, DenyUnknownServers.Instance);
        Assert.Equal(RemoteError.ConnectionFailed, (await Assert.ThrowsAsync<RemoteException>(() => client.ConnectAsync(default))).Error);
    }

    [Theory]
    [InlineData(RemoteProtocol.Ftp, false, SftpAuth.Password, FtpsMode.Explicit, "Plain FTP")]
    [InlineData(RemoteProtocol.Ftps, false, SftpAuth.PrivateKey, FtpsMode.Explicit, "Key authentication")]
    [InlineData(RemoteProtocol.Sftp, false, SftpAuth.Password, FtpsMode.Implicit, "implicit TLS")]
    public void UnsupportedCombinationsFailWithAClearError(RemoteProtocol protocol, bool allowPlain, SftpAuth auth, FtpsMode mode, string expected)
    {
        var c = new RemoteConnection { Host = "192.0.2.10", Protocol = protocol, AllowPlainFtp = allowPlain, Auth = auth, FtpsMode = mode };
        var e = Assert.Throws<RemoteException>(c.Validate);
        Assert.Equal(RemoteError.UnsupportedCombination, e.Error);
        Assert.Contains(expected, e.Message);
        Assert.Throws<RemoteException>(() => RemoteClients.Create(c, new MemoryCredentials(), DenyUnknownServers.Instance));
    }

    [Fact]
    public void PlainFtpIsValidOnlyWithTheExplicitChoice()
    {
        new RemoteConnection { Host = "192.0.2.10", Protocol = RemoteProtocol.Ftp, AllowPlainFtp = true }.Validate();
        Assert.Throws<RemoteException>(new RemoteConnection { Host = "192.0.2.10", Protocol = RemoteProtocol.Ftp }.Validate);
    }

    [Fact]
    public async Task PlainFtpConnectsOnlyWhenAllowedAndOnlyToAPlainServer()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("a.txt"), "plain");
        await using var server = await FtpsServer.StartAsync(dir.Path, plain: true);
        var plain = new RemoteConnection { Protocol = RemoteProtocol.Ftp, AllowPlainFtp = true, Host = "127.0.0.1", Port = server.Port, UserName = "u" };
        await using (var client = Create(plain, DenyUnknownServers.Instance))
        {
            await client.ConnectAsync(default);
            Assert.Equal(5, (await client.StatAsync("/a.txt", default))!.Value.Size);
        }

        // FTPS against a server without TLS must fail instead of silently falling back to plain FTP.
        var tls = plain with { Protocol = RemoteProtocol.Ftps, AllowPlainFtp = false };
        await using var strict = Create(tls, DenyUnknownServers.Instance);
        await Assert.ThrowsAsync<RemoteException>(() => strict.ConnectAsync(default));
    }

    [SftpFact]
    public async Task PrivateKeyLoginWorks()
    {
        using var dir = new TempDir();
        await using var server = SftpServer.Start(dir.Path, dir.Combine("host_key"), clientKey: true);
        var c = server.Connection(trusted: server.Identity) with { Auth = SftpAuth.PrivateKey, PrivateKeyPath = server.ClientKeyPath };
        await using var client = Create(c, DenyUnknownServers.Instance, password: null);
        await client.ConnectAsync(default);
        Assert.Null(await client.StatAsync("/none", default));
    }

    public static IEnumerable<object[]> ServersData => Servers.Both;
}
