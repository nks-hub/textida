using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;
using NksHub.Textida.Remote;

namespace NksHub.Textida.Remote.Tests;

/// <summary>
/// The SHA-1 signature of RSA keys (ssh-rsa) is weak and stays off unless a connection allows it. The "old" servers are paramiko with the SHA-1 signature put back
/// (a current paramiko has dropped it); a modern server has only the SHA-2 signatures.
/// </summary>
public class SshRsaTests
{
    private static IEnumerable<string> Names(IEnumerable<Renci.SshNet.Security.HostAlgorithm> algorithms) => algorithms.Select(a => a.Name);

    private static PrivateKeyFile NewKey(string kind)
    {
        string pem;
        if (kind == "rsa") { using var rsa = RSA.Create(2048); pem = rsa.ExportRSAPrivateKeyPem(); }
        else { using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256); pem = ec.ExportECPrivateKeyPem(); }
        return new PrivateKeyFile(new MemoryStream(Encoding.ASCII.GetBytes(pem)));
    }

    [Fact]
    public void AnRsaKeyOffersTheSha2SignaturesFirstAndSha1OnlyWhenAllowed()
    {
        using var rsa = NewKey("rsa");

        var strict = Names(SftpRemoteClient.SignatureAlgorithms(rsa.HostKeyAlgorithms, allowSha1: false)).ToList();
        var allowed = Names(SftpRemoteClient.SignatureAlgorithms(rsa.HostKeyAlgorithms, allowSha1: true)).ToList();

        Assert.Equal(["rsa-sha2-512", "rsa-sha2-256"], strict);
        Assert.Equal(["rsa-sha2-512", "rsa-sha2-256", "ssh-rsa"], allowed);
    }

    [Fact]
    public void AKeyOfAnotherKindIsUntouchedByTheSwitch()
    {
        using var ec = NewKey("ec");

        var off = Names(SftpRemoteClient.SignatureAlgorithms(ec.HostKeyAlgorithms, allowSha1: false)).ToList();
        var on = Names(SftpRemoteClient.SignatureAlgorithms(ec.HostKeyAlgorithms, allowSha1: true)).ToList();

        Assert.NotEmpty(off);
        Assert.Equal(off, on);
        Assert.DoesNotContain(off, n => n.StartsWith("ssh-rsa", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSwitchIsOffByDefaultOnlyForSftpAndSurvivesTheSavedList()
    {
        Assert.False(new RemoteConnection().AllowSshRsaSha1);

        var ftps = new RemoteConnection { Host = "192.0.2.10", Protocol = RemoteProtocol.Ftps, AllowSshRsaSha1 = true };
        var e = Assert.Throws<RemoteException>(ftps.Validate);
        Assert.Equal(RemoteError.UnsupportedCombination, e.Error);
        Assert.Contains("SFTP only", e.Message);

        var sftp = new RemoteConnection { Host = "192.0.2.10", Protocol = RemoteProtocol.Sftp, AllowSshRsaSha1 = true };
        sftp.Validate();
        var back = Assert.Single(ConnectionStore.Import(ConnectionStore.Export([sftp])));
        Assert.True(back.AllowSshRsaSha1);
        Assert.False(Assert.Single(ConnectionStore.Import(ConnectionStore.Export([sftp with { AllowSshRsaSha1 = false }]))).AllowSshRsaSha1);
    }

    [SftpFact]
    public async Task AModernServerTakesAnRsaKeyWithoutTheSwitch()
    {
        using var dir = new TempDir();
        await using var server = SftpServer.Start(dir.Path, dir.Combine("host_key"), clientKey: true, clientKeyType: "rsa");
        var c = server.Connection(trusted: server.Identity) with { Auth = SftpAuth.PrivateKey, PrivateKeyPath = server.ClientKeyPath };

        await using var client = ConnectionTests.Create(c, DenyUnknownServers.Instance, password: null);
        await client.ConnectAsync(default);

        Assert.Null(await client.StatAsync("/none", default));
    }

    [SftpFact]
    public async Task AnOldServerThatSignsLoginsWithSha1OnlyRefusesUntilTheConnectionAllowsIt()
    {
        using var dir = new TempDir();
        await using var server = SftpServer.Start(dir.Path, dir.Combine("host_key"), clientKey: true, clientKeyType: "rsa", legacyRsa: "userauth");
        var c = server.Connection(trusted: server.Identity) with { Auth = SftpAuth.PrivateKey, PrivateKeyPath = server.ClientKeyPath };

        await using (var refused = ConnectionTests.Create(c, DenyUnknownServers.Instance, password: null))
            Assert.Equal(RemoteError.AuthenticationFailed, (await Assert.ThrowsAsync<RemoteException>(() => refused.ConnectAsync(default))).Error);

        await using var client = ConnectionTests.Create(c with { AllowSshRsaSha1 = true }, DenyUnknownServers.Instance, password: null);
        await client.ConnectAsync(default);
        Assert.Null(await client.StatAsync("/none", default));
    }

    [SftpFact]
    public async Task AnOldServerWithASha1HostKeyShareNoAlgorithmUntilTheConnectionAllowsIt()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("a.txt"), "old");
        await using var server = SftpServer.Start(dir.Path, dir.Combine("host_key"), legacyRsa: "hostkey");

        await using (var refused = ConnectionTests.Create(server.Connection(trusted: server.Identity), DenyUnknownServers.Instance))
            Assert.Equal(RemoteError.NoCommonAlgorithm, (await Assert.ThrowsAsync<RemoteException>(() => refused.ConnectAsync(default))).Error);

        await using var client = ConnectionTests.Create(server.Connection(trusted: server.Identity) with { AllowSshRsaSha1 = true }, DenyUnknownServers.Instance);
        await client.ConnectAsync(default);
        Assert.Equal(3, (await client.StatAsync("/a.txt", default))!.Value.Size);
    }
}
