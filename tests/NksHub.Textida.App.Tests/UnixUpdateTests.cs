using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using NksHub.Textida.App.Services;

namespace NksHub.Textida.App.Tests;

public sealed class UnixUpdateTests : IDisposable
{
    private readonly TempDir _temp = new();
    public void Dispose() => _temp.Dispose();

    private string Install => Path.Combine(_temp.Path, "Textida");

    private static void WriteVersion(string folder, string version, UpdatePlatform platform = UpdatePlatform.Linux)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppUpdater.Executable(folder, platform))!);
        File.WriteAllText(AppUpdater.Executable(folder, platform), version);
        var file = platform == UpdatePlatform.MacOS ? Path.Combine(folder, "Contents", "Resources", "version.txt") : Path.Combine(folder, "version.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, version);
    }

    private static void ExchangeForWindowsTest(string left, string right)
    {
        Directory.Move(left, left + ".test-aside");
        Directory.Move(right, left);
        Directory.Move(left + ".test-aside", right);
    }

    [Theory]
    [InlineData("before-exchange", "1.0.0")]
    [InlineData("after-exchange", "1.1.0")]
    [InlineData("before-previous", "1.1.0")]
    public void An_interrupted_update_recovers_without_removing_the_runnable_installation(string checkpoint, string expected)
    {
        WriteVersion(Install, "1.0.0");
        WriteVersion(Install + ".new", "1.1.0");
        WriteVersion(Install + ".previous", "0.9.0");
        Directory.CreateDirectory(Path.Combine(Install, "profile"));
        var journal = Encoding.UTF8.GetBytes("unconfirmed edit\n");
        File.WriteAllBytes(Path.Combine(Install, "profile", "journal"), journal);
        File.WriteAllText(Path.Combine(Install, "profile", "settings.json"), "{\"schemaVersion\":1,\"unknown\":true}");
        var swap = new UpdateSwap(Install, ExchangeForWindowsTest) { Checkpoint = point => { if (point == checkpoint) throw new IOException("injected interruption"); } };

        Assert.Throws<IOException>(() => swap.Apply(rollback: false));
        Assert.True(File.Exists(Path.Combine(Install, "Textida")));
        new UpdateSwap(Install, ExchangeForWindowsTest).Recover();
        new UpdateSwap(Install, ExchangeForWindowsTest).Recover();

        Assert.Equal(expected, AppUpdater.InstalledVersion(Install));
        Assert.Equal(SHA256.HashData(journal), SHA256.HashData(File.ReadAllBytes(Path.Combine(Install, "profile", "journal"))));
        Assert.Contains("\"unknown\":true", File.ReadAllText(Path.Combine(Install, "profile", "settings.json")));
        Assert.False(File.Exists(Install + ".update"));
        if (expected == "1.1.0") Assert.Equal("1.0.0", AppUpdater.InstalledVersion(Install + ".previous"));
    }

    [Fact]
    public void Rollback_exchanges_the_versions_and_carries_the_current_profile_back()
    {
        WriteVersion(Install, "1.1.0");
        WriteVersion(Install + ".previous", "1.0.0");
        Directory.CreateDirectory(Path.Combine(Install, "profile"));
        File.WriteAllText(Path.Combine(Install, "profile", "settings.json"), "latest settings");
        var swap = new UpdateSwap(Install, ExchangeForWindowsTest);

        swap.Apply(rollback: true);

        Assert.Equal("1.0.0", AppUpdater.InstalledVersion(Install));
        Assert.Equal("1.1.0", AppUpdater.InstalledVersion(Install + ".previous"));
        Assert.Equal("latest settings", File.ReadAllText(Path.Combine(Install, "profile", "settings.json")));
        swap.Apply(rollback: true);
        Assert.Equal("1.1.0", AppUpdater.InstalledVersion(Install));
    }

    [Fact]
    public void The_real_unix_exchange_updates_and_rolls_back_the_same_installation()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "renameat2/renamex_np requires a Unix system");
        WriteVersion(Install, "1.0.0");
        WriteVersion(Install + ".new", "1.1.0");
        var swap = new UpdateSwap(Install);
        swap.Apply(rollback: false);
        Assert.Equal("1.1.0", AppUpdater.InstalledVersion(Install));
        Assert.Equal("1.0.0", AppUpdater.InstalledVersion(Install + ".previous"));
        swap.Apply(rollback: true);
        Assert.Equal("1.0.0", AppUpdater.InstalledVersion(Install));
        Assert.Equal("1.1.0", AppUpdater.InstalledVersion(Install + ".previous"));
    }

    [Fact]
    public void A_failed_exchange_preserves_the_old_install_and_can_be_recovered()
    {
        WriteVersion(Install, "1.0.0");
        WriteVersion(Install + ".new", "1.1.0");
        var swap = new UpdateSwap(Install, (_, _) => throw new IOException("filesystem has no atomic exchange"));
        Assert.Throws<IOException>(() => swap.Apply(rollback: false));
        swap.Recover();
        Assert.Equal("1.0.0", AppUpdater.InstalledVersion(Install));
        Assert.Equal("1.1.0", AppUpdater.InstalledVersion(Install + ".new"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_unix_package_is_staged_in_the_layout_the_packager_actually_writes(int platformId)
    {
        var platform = (UpdatePlatform)platformId;
        var updater = new AppUpdater(new HttpClient(), "", "", Install) { Platform = platform, MacSignatureCheck = _ => { } };
        var rid = platform == UpdatePlatform.MacOS ? "osx-arm64" : "linux-x64";
        var source = Path.Combine(_temp.Path, "package");
        var root = Path.Combine(source, platform == UpdatePlatform.MacOS ? "Textida.app" : "Textida-1.1.0-" + rid);
        WriteVersion(root, "1.1.0", platform);
        var package = Path.Combine(_temp.Path, platform == UpdatePlatform.MacOS ? "p.zip" : "p.tar.gz");
        if (platform == UpdatePlatform.MacOS) ZipFile.CreateFromDirectory(source, package);
        else
        {
            using var gzip = new GZipStream(File.Create(package), CompressionMode.Compress);
            TarFile.CreateFromDirectory(source, gzip, includeBaseDirectory: false);
        }
        var release = new UpdateRelease("1.1.0", rid, "", "", 0);

        Assert.Equal(updater.StagingDir, updater.Stage(package, release));
        Assert.Equal("1.1.0", AppUpdater.InstalledVersion(updater.StagingDir));
        Assert.True(File.Exists(AppUpdater.Executable(updater.StagingDir, platform)));
        Assert.False(Directory.Exists(Install));
    }

    [Fact]
    public void A_mac_package_refused_by_signature_verification_is_never_staged()
    {
        WriteVersion(Install, "1.0.0", UpdatePlatform.MacOS);
        var packageRoot = Path.Combine(_temp.Path, "package");
        WriteVersion(Path.Combine(packageRoot, "Textida.app"), "1.1.0", UpdatePlatform.MacOS);
        var zip = Path.Combine(_temp.Path, "p.zip");
        ZipFile.CreateFromDirectory(packageRoot, zip);
        var updater = new AppUpdater(new HttpClient(), "", "", Install)
        {
            Platform = UpdatePlatform.MacOS,
            MacSignatureCheck = _ => throw new UpdateException(UpdateError.BadSignature, "invalid signature")
        };
        Assert.Equal(UpdateError.BadSignature, Assert.Throws<UpdateException>(() => updater.Stage(zip, new("1.1.0", "osx-arm64", "", "", 0))).Error);
        Assert.Equal("1.0.0", AppUpdater.InstalledVersion(Install));
        Assert.False(Directory.Exists(updater.StagingDir));
    }

    [Fact]
    public void A_deb_install_is_delegated_to_apt_before_any_download_or_swap()
    {
        WriteVersion(Install, "1.0.0");
        File.WriteAllText(Path.Combine(Install, ".deb-managed"), "apt");
        var updater = new AppUpdater(new HttpClient(), "", "", Install) { Platform = UpdatePlatform.Linux };
        Assert.True(updater.IsInstalled);
        Assert.True(updater.UsesSystemPackageManager);
        Assert.Equal(UpdateError.WrongSystem, Assert.Throws<UpdateException>(() => updater.Stage("absent.tar.gz", new("1.1.0", "linux-x64", "", "", 0))).Error);
        Assert.Equal("1.0.0", AppUpdater.InstalledVersion(Install));
    }

    [Fact]
    public void A_portable_mac_profile_inside_the_signed_bundle_is_kept_and_the_update_is_refused()
    {
        WriteVersion(Install, "1.0.0", UpdatePlatform.MacOS);
        WriteVersion(Install + ".previous", "0.9.0", UpdatePlatform.MacOS);
        var folder = Path.Combine(Install, "Contents", "MacOS");
        File.WriteAllText(Path.Combine(folder, "textida.portable"), "");
        Directory.CreateDirectory(Path.Combine(folder, "profile"));
        var journal = Encoding.UTF8.GetBytes("unsaved portable document");
        File.WriteAllBytes(Path.Combine(folder, "profile", "journal"), journal);
        var updater = new AppUpdater(new HttpClient(), "", "", Install) { Platform = UpdatePlatform.MacOS };

        Assert.Throws<UpdateException>(() => updater.Stage("absent.zip", new("1.1.0", "osx-arm64", "", "", 0)));
        Assert.Throws<UpdateException>(() => updater.Rollback(restart: false));
        Assert.Equal(SHA256.HashData(journal), SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, "profile", "journal"))));
        Assert.Equal("1.0.0", AppUpdater.InstalledVersion(Install));
    }

    [Fact]
    public async Task One_signed_feed_selects_the_exact_platform_and_refuses_ambiguous_packages()
    {
        using var server = new FeedServer();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var releases = new[] { new UpdateRelease("1.1.0", "win-x64", "w.zip", new string('0', 64), 1), new UpdateRelease("1.1.0", "osx-arm64", "m.zip", new string('1', 64), 2) };
        server.Routes["/feed.json"] = Encoding.UTF8.GetBytes(AppUpdater.CreateFeed(key, releases));
        var updater = new AppUpdater(new HttpClient(), server.Base + "feed.json", Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), Install);
        var release = await updater.CheckAsync("1.0.0", "osx-arm64", CancellationToken.None);
        Assert.Equal(server.Base + "m.zip", release!.Url);
        Assert.Equal(UpdateError.WrongSystem, (await Assert.ThrowsAsync<UpdateException>(() => updater.CheckAsync("1.0.0", "linux-x64", CancellationToken.None))).Error);
        server.Routes["/feed.json"] = Encoding.UTF8.GetBytes(AppUpdater.CreateFeed(key, [releases[0], releases[0]]));
        Assert.Equal(UpdateError.WrongSystem, (await Assert.ThrowsAsync<UpdateException>(() => updater.CheckAsync("1.0.0", "win-x64", CancellationToken.None))).Error);
    }
}
