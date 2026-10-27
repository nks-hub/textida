using NksHub.Textida.Core.Secrets;
using NksHub.Textida.Remote;

namespace NksHub.Textida.Remote.Tests;

public class SecretsTests
{
    private const string Password = "S3cret-pa55w0rd-ünïcode";

    [Fact]
    public void ConnectionFileAndBackupNeverContainThePassword()
    {
        using var dir = new TempDir();
        var secrets = new MemoryCredentials();
        var store = new ConnectionStore(dir.Combine("connections.json"));
        var c = new RemoteConnection { Name = "A", Host = "192.0.2.1", UserName = "u", Proxy = new(ProxyType.Socks5, "198.51.100.1", 1080, "pu") };
        store.Add(c, Password, secrets);
        secrets.Set(c.ProxySecretKey, "proxy-" + Password);

        Assert.Equal(Password, secrets.Get(c.SecretKey));
        string file = File.ReadAllText(dir.Combine("connections.json"));
        string backup = ConnectionStore.Export(store.Load());
        foreach (var text in new[] { file, backup })
        {
            Assert.DoesNotContain(Password, text);
            Assert.DoesNotContain("proxy-", text);
        }
        Assert.Equal(c, store.Load().Single());
        Assert.Equal(c, ConnectionStore.Import(backup).Single());
    }

    [Fact]
    public void RemovingAConnectionRemovesItsSecrets()
    {
        using var dir = new TempDir();
        var secrets = new MemoryCredentials();
        var store = new ConnectionStore(dir.Combine("c.json"));
        var c = new RemoteConnection { Host = "192.0.2.1" };
        store.Add(c, Password, secrets);
        store.Remove(c, secrets);
        Assert.Empty(store.Load());
        Assert.Null(secrets.Get(c.SecretKey));
    }

    [Fact]
    public void WindowsCredentialManagerKeepsAndDeletesASecret()
    {
        if (!OperatingSystem.IsWindows()) return; // the macOS Keychain and Linux secret service are not implemented, see UnavailableStoreThrowsInsteadOfStoringElsewhere
        var store = CredentialStores.ForCurrentPlatform();
        string key = "textida:test:" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.Null(store.Get(key));
            store.Set(key, Password);
            Assert.Equal(Password, store.Get(key));
            store.Set(key, "second");
            Assert.Equal("second", store.Get(key));
            Assert.True(store.Delete(key));
            Assert.Null(store.Get(key));
            Assert.False(store.Delete(key));
        }
        finally { store.Delete(key); }
    }

    [Fact]
    public void UnavailableStoreThrowsInsteadOfStoringElsewhere()
    {
        var store = new UnavailableCredentialStore();
        Assert.Throws<CredentialStoreUnavailableException>(() => store.Set("k", "v"));
        Assert.Throws<CredentialStoreUnavailableException>(() => store.Get("k"));
    }
}
