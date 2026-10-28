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

    /// <summary>The whole contract of a store on a machine that has the system store: absent, set, replaced, deleted, deleted again.</summary>
    private static void KeepsAndDeletesASecret(ICredentialStore store)
    {
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
    public void WindowsCredentialManagerKeepsAndDeletesASecret()
    {
        if (!OperatingSystem.IsWindows()) return;
        KeepsAndDeletesASecret(CredentialStores.ForCurrentPlatform());
    }

    // Written without a Mac and without a Linux desktop at hand: these two run only there, and the first run on those systems is the first real check.
    [Fact]
    public void MacKeychainKeepsAndDeletesASecret()
    {
        if (!OperatingSystem.IsMacOS()) return;
        KeepsAndDeletesASecret(CredentialStores.ForCurrentPlatform());
    }

    [Fact]
    public void LinuxSecretServiceKeepsAndDeletesASecretWhenThereIsOne()
    {
        if (!OperatingSystem.IsLinux()) return;
        var store = CredentialStores.ForCurrentPlatform();
        try { store.Get("textida:test:probe"); }
        catch (CredentialStoreUnavailableException) { return; } // no libsecret or no keyring in this session: the store says so, which UnavailableStoreThrowsInsteadOfStoringElsewhere covers
        KeepsAndDeletesASecret(store);
    }

    [Theory]
    [InlineData("keychain", "Keychain")]
    [InlineData("secret", "libsecret")]
    public void AMissingSystemLibraryGivesAStoreThatSaysSoAndKeepsNothing(string kind, string mentions)
    {
        var store = kind == "keychain" ? MacKeychainCredentialStore.CreateOrUnavailable(_ => false) : LinuxSecretCredentialStore.CreateOrUnavailable(_ => false);

        Assert.IsType<UnavailableCredentialStore>(store);
        foreach (var call in new Action[] { () => store.Get("k"), () => store.Set("k", "v"), () => store.Delete("k") })
            Assert.Contains(mentions, Assert.Throws<CredentialStoreUnavailableException>(call).Message);
    }

    [Fact]
    public void ALoadableSystemLibraryGivesTheRealStore()
    {
        Assert.IsType<MacKeychainCredentialStore>(MacKeychainCredentialStore.CreateOrUnavailable(_ => true));
        if (IntPtr.Size == 8) Assert.IsType<LinuxSecretCredentialStore>(LinuxSecretCredentialStore.CreateOrUnavailable(_ => true));
    }

    [Fact]
    public void LibrariesOfTheSystemStoresAreNotLoadableOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.False(CredentialStores.CanLoad("libsecret-1.so.0"));
        Assert.False(CredentialStores.CanLoad("/System/Library/Frameworks/Security.framework/Security"));
        Assert.True(CredentialStores.CanLoad("advapi32.dll"));
    }

    [Fact]
    public void UnavailableStoreThrowsInsteadOfStoringElsewhere()
    {
        var store = new UnavailableCredentialStore();
        Assert.Throws<CredentialStoreUnavailableException>(() => store.Set("k", "v"));
        Assert.Throws<CredentialStoreUnavailableException>(() => store.Get("k"));
    }
}
