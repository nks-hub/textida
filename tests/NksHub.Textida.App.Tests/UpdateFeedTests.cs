using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using NksHub.Textida.App.Services;
using NksHub.Textida.Remote;

namespace NksHub.Textida.App.Tests;

public sealed class UpdateFeedTests : IDisposable
{
    private readonly TempDir _temp = new();
    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task The_release_signing_tool_produces_a_feed_that_the_updater_accepts()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var package = Encoding.UTF8.GetBytes("package bytes for the signed digest");
        WritePackages(package);
        using var server = new FeedServer();
        var (exitCode, output) = await SignAsync(key.ExportPkcs8PrivateKeyPem(), server.Base.TrimEnd('/'), Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        Assert.True(exitCode == 0, output);
        server.Routes["/feed.json"] = File.ReadAllBytes(Path.Combine(_temp.Path, "feed.json"));
        var updater = new AppUpdater(new HttpClient(), server.Base + "feed.json", Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), "unused");

        foreach (var rid in new[] { "win-x64", "osx-arm64", "osx-x64", "linux-x64" })
        {
            var release = await updater.CheckAsync("1.0.0", rid, CancellationToken.None);
            Assert.NotNull(release);
            Assert.Equal(rid, release.Rid);
            Assert.Equal(package.Length, release.Size);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(package)), release.Sha256);
            Assert.StartsWith(server.Base, release.Url);
        }
    }

    [Fact]
    public async Task A_release_without_the_signing_key_fails_without_writing_a_feed()
    {
        var (exitCode, _) = await SignAsync("", "https://example.test/release");
        Assert.NotEqual(0, exitCode);
        Assert.False(File.Exists(Path.Combine(_temp.Path, "feed.json")));
    }

    [Fact]
    public async Task A_release_signed_with_another_key_fails_without_writing_a_feed()
    {
        WritePackages(Encoding.UTF8.GetBytes("package"));
        using var wrongKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (exitCode, output) = await SignAsync(wrongKey.ExportPkcs8PrivateKeyPem(), "https://example.test/release");
        Assert.NotEqual(0, exitCode);
        Assert.Contains("does not match the public key", output);
        Assert.False(File.Exists(Path.Combine(_temp.Path, "feed.json")));
    }

    private void WritePackages(byte[] bytes)
    {
        foreach (var rid in new[] { "win-x64", "osx-arm64", "osx-x64", "linux-x64" })
            File.WriteAllBytes(Path.Combine(_temp.Path, $"Textida-1.1.0-{rid}" + (rid.StartsWith("linux-") ? ".tar.gz" : ".zip")), bytes);
    }

    private async Task<(int ExitCode, string Output)> SignAsync(string privateKey, string releaseUrl, string? publicKey = null)
    {
        var start = new ProcessStartInfo("dotnet", ["run", Path.Combine(Repo.Root, "tools", "SignUpdateFeed.cs"), "--", _temp.Path, "1.1.0", releaseUrl])
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Repo.Root
        };
        if (publicKey is not null) start.ArgumentList.Add(publicKey);
        start.Environment["FEED_SIGNING_KEY"] = privateKey;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await output + await errors);
    }

    [Theory]
    [InlineData("https://release-assets.githubusercontent.com/asset", true)]
    [InlineData("https://objects.githubusercontent.com/asset", true)]
    [InlineData("https://elsewhere.example/asset", false)]
    [InlineData("http://release-assets.githubusercontent.com/asset", false)]
    public async Task Github_asset_redirects_allow_only_the_release_hosts_over_https(string location, bool accepted)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var feed = AppUpdater.CreateFeed(key, new UpdateRelease("1.1.0", "win-x64", "package.zip", new string('0', 64), 1));
        var transport = new RedirectTransport(location, feed);
        using var http = new HttpClient(new SameHostRedirectHandler(transport));
        var updater = new AppUpdater(http, "https://github.com/nks-hub/textida/releases/latest/download/feed.json", Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), "unused");
        if (accepted)
        {
            Assert.NotNull(await updater.CheckAsync("1.0.0", "win-x64", CancellationToken.None));
            Assert.Equal(2, transport.Requests);
        }
        else
        {
            await Assert.ThrowsAsync<UpdateException>(() => updater.CheckAsync("1.0.0", "win-x64", CancellationToken.None));
            Assert.Equal(1, transport.Requests);
        }
    }

    [Fact]
    public void A_windows_switch_interrupted_after_install_is_completed_once_at_startup()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows installer recovery");
        var dir = Path.Combine(_temp.Path, "Textida");
        foreach (var (suffix, version) in new[] { ("", "1.1.0"), (".aside", "1.0.0"), (".previous", "0.9.0") })
        {
            Directory.CreateDirectory(dir + suffix);
            File.WriteAllText(Path.Combine(dir + suffix, "Textida.exe"), version);
        }
        AppUpdater.RecoverAtStartup(dir);
        AppUpdater.RecoverAtStartup(dir);
        Assert.Equal("1.1.0", File.ReadAllText(Path.Combine(dir, "Textida.exe")));
        Assert.Equal("1.0.0", File.ReadAllText(Path.Combine(dir + ".previous", "Textida.exe")));
        Assert.False(Directory.Exists(dir + ".aside"));
    }

    private sealed class RedirectTransport(string location, string feed) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var response = Requests == 1 ? new HttpResponseMessage(HttpStatusCode.Found)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(feed) };
            if (Requests == 1) response.Headers.Location = new Uri(location);
            return Task.FromResult(response);
        }
    }
}
