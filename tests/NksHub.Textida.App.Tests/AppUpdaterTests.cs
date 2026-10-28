using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Microsoft.Win32;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.App.Tests;

/// <summary>A local HTTP server that serves fixed bodies and counts every request.</summary>
internal sealed class FeedServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Task _loop;
    public Dictionary<string, byte[]> Routes { get; } = [];
    public List<string> Requests { get; } = [];
    public string Base { get; }

    public FeedServer()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Base = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(Base);
        _listener.Start();
        _loop = Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }
                try
                {
                    lock (Requests) Requests.Add(context.Request.Url!.AbsolutePath);
                    if (Routes.TryGetValue(context.Request.Url!.AbsolutePath, out var body)) await context.Response.OutputStream.WriteAsync(body);
                    else context.Response.StatusCode = 404;
                    context.Response.Close();
                }
                catch (Exception e) when (e is HttpListenerException or IOException or ObjectDisposedException) { } // the client hung up: an updater cuts off a download that passes its signed size
            }
        });
    }

    public int Count { get { lock (Requests) return Requests.Count; } }

    public void Dispose()
    {
        _listener.Close();
        _loop.Wait(2000);
    }
}

/// <summary>
/// P3.7.2 on a fake package (whoami.exe stands in for Textida.exe) and a local HTTP server. The install folder, the Apps &amp; features key and the feed are all redirected: the real
/// <c>%LOCALAPPDATA%\Programs\Textida</c> is never touched, and the helper does not wait for the test process.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class AppUpdaterTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly FeedServer _server = new();
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _keyPath = @"Software\Textida.Tests\" + Guid.NewGuid().ToString("N");
    private readonly string _dir;

    public AppUpdaterTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the per-user install is Windows only");
        _dir = Path.Combine(_temp.Path, "Textida");
        WriteVersion(_dir, "1.0.0");
        Directory.CreateDirectory(Path.Combine(_dir, "profile"));
        File.WriteAllText(Path.Combine(_dir, "profile", "settings.json"), "mine");
        using var entry = Registry.CurrentUser.CreateSubKey(_keyPath + @"\Uninstall");
        entry.SetValue("DisplayVersion", "1.0.0");
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
        _server.Dispose();
        _key.Dispose();
        _temp.Dispose();
    }

    private static void WriteVersion(string dir, string version)
    {
        Directory.CreateDirectory(dir);
        File.Copy(Path.Combine(Environment.SystemDirectory, "whoami.exe"), Path.Combine(dir, "Textida.exe"), overwrite: true);
        File.Copy(Path.Combine(Repo.Root, "tools", "windows-installer", "Setup.ps1"), Path.Combine(dir, "Setup.ps1"), overwrite: true);
        File.WriteAllText(Path.Combine(dir, "version.txt"), version);
        File.WriteAllText(Path.Combine(dir, "marker.txt"), "v" + version);
    }

    private string PublicKey => Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());

    private AppUpdater Updater(string? publicKey = null, string? feed = null) =>
        new(new HttpClient(), feed ?? _server.Base + "feed.json", publicKey ?? PublicKey, _dir) { UninstallKey = @"HKCU:\" + _keyPath + @"\Uninstall", WaitPid = 0 };

    /// <summary>Builds a package of <paramref name="version"/>, publishes it with a feed signed by <paramref name="signer"/>, and returns the release as the feed names it.</summary>
    private UpdateRelease Publish(string version, ECDsa? signer = null)
    {
        var folder = Path.Combine(_temp.Path, "pack-" + version);
        WriteVersion(folder, version);
        File.WriteAllText(Path.Combine(folder, "Install.cmd"), "@echo off");
        var zip = Path.Combine(_temp.Path, $"Textida-{version}.zip");
        File.Delete(zip);
        ZipFile.CreateFromDirectory(folder, zip);
        var bytes = File.ReadAllBytes(zip);
        var release = new UpdateRelease(version, AppUpdater.CurrentRid, $"Textida-{version}.zip", Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
        _server.Routes["/feed.json"] = Encoding.UTF8.GetBytes(AppUpdater.CreateFeed(signer ?? _key, release));
        _server.Routes["/" + release.Url] = bytes;
        return release with { Url = _server.Base + release.Url };
    }

    private static int Run(Process process)
    {
        Assert.True(process.WaitForExit(120_000), "Setup.ps1 did not finish");
        return process.ExitCode;
    }

    private static string Marker(string dir) => File.ReadAllText(Path.Combine(dir, "marker.txt"));

    private async Task<string> Stage(AppUpdater updater, UpdateRelease release)
    {
        var zip = await updater.DownloadAsync(release, Path.Combine(_temp.Path, "download"), CancellationToken.None);
        return updater.Stage(zip, release);
    }

    [Fact]
    public async Task A_valid_update_is_unpacked_next_to_the_old_version_and_switched_only_by_the_helper()
    {
        Publish("1.1.0");
        var updater = Updater();

        var release = await updater.CheckAsync("1.0.0", AppUpdater.CurrentRid, CancellationToken.None);
        Assert.NotNull(release);
        var staged = await Stage(updater, release);

        Assert.Equal(updater.StagingDir, staged);
        Assert.Equal("v1.1.0", Marker(staged));
        Assert.Equal("v1.0.0", Marker(_dir));
        Assert.False(File.Exists(Path.Combine(staged, "Install.cmd")));
        Assert.Equal(["/feed.json", "/Textida-1.1.0.zip"], _server.Requests);

        Assert.Equal(0, Run(updater.Switch(staged, restart: false)));

        Assert.Equal("v1.1.0", Marker(_dir));
        Assert.Equal("v1.0.0", Marker(updater.PreviousDir));
        Assert.False(Directory.Exists(updater.StagingDir));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_dir, "profile", "settings.json")));
        using var entry = Registry.CurrentUser.OpenSubKey(_keyPath + @"\Uninstall")!;
        Assert.Equal("1.1.0", entry.GetValue("DisplayVersion"));
    }

    [Fact]
    public async Task Return_swaps_back_to_the_previous_version_and_keeps_the_newer_one_for_a_second_return()
    {
        Publish("1.1.0");
        var updater = Updater();
        Assert.Equal(0, Run(updater.Switch(await Stage(updater, (await updater.CheckAsync("1.0.0", AppUpdater.CurrentRid, CancellationToken.None))!), restart: false)));
        File.WriteAllText(Path.Combine(_dir, "profile", "settings.json"), "changed in 1.1.0");

        Assert.Equal(0, Run(updater.Rollback(restart: false)));

        Assert.Equal("v1.0.0", Marker(_dir));
        Assert.Equal("v1.1.0", Marker(updater.PreviousDir));
        Assert.Equal("changed in 1.1.0", File.ReadAllText(Path.Combine(_dir, "profile", "settings.json")));
        using (var entry = Registry.CurrentUser.OpenSubKey(_keyPath + @"\Uninstall")!) Assert.Equal("1.0.0", entry.GetValue("DisplayVersion"));

        Assert.Equal(0, Run(updater.Rollback(restart: false)));
        Assert.Equal("v1.1.0", Marker(_dir));
    }

    /// <summary>whoami.exe stands in for TextidaSetup.exe: it starts, ends at once and takes any arguments, so the real setup never runs here.</summary>
    private void AddSetupProgram() => File.Copy(Path.Combine(Environment.SystemDirectory, "whoami.exe"), Path.Combine(_dir, "TextidaSetup.exe"), overwrite: true);

    [Fact]
    public void An_install_made_by_the_setup_program_updates_through_a_temporary_copy_of_it()
    {
        AddSetupProgram();
        var updater = Updater();
        var staged = _dir + ".new";
        Directory.CreateDirectory(staged);

        using var helper = updater.Switch(staged, restart: true);
        helper.WaitForExit(30_000);

        var copy = helper.StartInfo.FileName;
        Assert.Equal("TextidaSetup.exe", Path.GetFileName(copy));
        Assert.StartsWith(Path.Combine(Path.GetTempPath(), "textida-update-"), copy);
        Assert.NotEqual(Path.Combine(_dir, "TextidaSetup.exe"), copy);
        Assert.Equal(["--apply-update", staged, "--dir", _dir, "--uninstall-key", @"HKCU:\" + _keyPath + @"\Uninstall", "--restart", "--temp-copy"], helper.StartInfo.ArgumentList);
    }

    [Fact]
    public void The_return_and_the_wait_for_the_editor_are_passed_to_the_setup_program()
    {
        AddSetupProgram();
        Directory.CreateDirectory(Path.Combine(_dir + ".previous"));
        File.Copy(Path.Combine(_dir, "Textida.exe"), Path.Combine(_dir + ".previous", "Textida.exe"));
        var updater = new AppUpdater(new HttpClient(), _server.Base + "feed.json", PublicKey, _dir) { WaitPid = 4321 };

        using var helper = updater.Rollback(restart: false);
        helper.WaitForExit(30_000);

        Assert.Equal(["--rollback", "--dir", _dir, "--wait-pid", "4321", "--temp-copy"], helper.StartInfo.ArgumentList);
    }

    [Fact]
    public void The_setup_program_alone_is_enough_for_the_update_and_without_either_there_is_none()
    {
        File.Delete(Path.Combine(_dir, "Setup.ps1"));
        Assert.False(Updater().IsInstalled);

        AddSetupProgram();
        Assert.True(Updater().IsInstalled);
    }

    [Fact]
    public async Task An_install_without_the_setup_program_still_uses_the_script()
    {
        Publish("1.1.0");
        var updater = Updater();
        var staged = await Stage(updater, (await updater.CheckAsync("1.0.0", AppUpdater.CurrentRid, CancellationToken.None))!);

        using var helper = updater.Switch(staged, restart: false);
        Assert.EndsWith("powershell.exe", helper.StartInfo.FileName);
        Assert.Equal(0, Run(helper));
        Assert.Equal("v1.1.0", Marker(_dir));
    }

    [Fact]
    public void Return_without_a_previous_version_is_refused_before_anything_starts()
    {
        var error = Assert.Throws<UpdateException>(() => Updater().Rollback(restart: false));
        Assert.Equal(UpdateError.NoPrevious, error.Error);
        Assert.Equal("v1.0.0", Marker(_dir));
    }

    [Fact]
    public async Task A_tampered_package_is_refused_and_leaves_nothing_behind()
    {
        var release = Publish("1.1.0");
        var tampered = (byte[])_server.Routes["/Textida-1.1.0.zip"].Clone();
        tampered[tampered.Length / 2] ^= 0xFF;
        _server.Routes["/Textida-1.1.0.zip"] = tampered;
        var updater = Updater();

        var error = await Assert.ThrowsAsync<UpdateException>(() => updater.DownloadAsync(release, Path.Combine(_temp.Path, "download"), CancellationToken.None));

        Assert.Equal(UpdateError.BadPackage, error.Error);
        Assert.Empty(Directory.GetFiles(Path.Combine(_temp.Path, "download")));
        Assert.False(Directory.Exists(updater.StagingDir));
        Assert.Equal("v1.0.0", Marker(_dir));
    }

    [Fact]
    public async Task A_package_longer_than_the_signed_size_is_cut_off()
    {
        var release = Publish("1.1.0");
        _server.Routes["/Textida-1.1.0.zip"] = [.. _server.Routes["/Textida-1.1.0.zip"], .. new byte[1000]];
        var error = await Assert.ThrowsAsync<UpdateException>(() => Updater().DownloadAsync(release, Path.Combine(_temp.Path, "download"), CancellationToken.None));
        Assert.Equal(UpdateError.BadPackage, error.Error);
    }

    [Fact]
    public async Task A_feed_signed_by_another_key_or_changed_after_signing_is_refused_before_any_download()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Publish("1.1.0", signer: other);
        var error = await Assert.ThrowsAsync<UpdateException>(() => Updater().CheckAsync("1.0.0", AppUpdater.CurrentRid, CancellationToken.None));
        Assert.Equal(UpdateError.BadSignature, error.Error);

        Publish("1.1.0");
        var feed = JsonNode.Parse(Encoding.UTF8.GetString(_server.Routes["/feed.json"]))!.AsObject();
        feed["payload"] = feed["payload"]!.GetValue<string>().Replace("1.1.0", "9.9.9");
        _server.Routes["/feed.json"] = Encoding.UTF8.GetBytes(feed.ToJsonString());
        error = await Assert.ThrowsAsync<UpdateException>(() => Updater().CheckAsync("1.0.0", AppUpdater.CurrentRid, CancellationToken.None));
        Assert.Equal(UpdateError.BadSignature, error.Error);
        Assert.DoesNotContain(_server.Requests, r => r.EndsWith(".zip"));
    }

    [Fact]
    public async Task A_feed_with_the_same_or_an_older_version_is_ignored()
    {
        Publish("1.0.0");
        Assert.Null(await Updater().CheckAsync("1.0.0", AppUpdater.CurrentRid, CancellationToken.None));
        Publish("0.9.0");
        Assert.Null(await Updater().CheckAsync("1.0.0", AppUpdater.CurrentRid, CancellationToken.None));
        Assert.Null(await Updater().CheckAsync("1.0.0", AppUpdater.CurrentRid, CancellationToken.None));
        Assert.All(_server.Requests, r => Assert.Equal("/feed.json", r));
    }

    [Fact]
    public async Task A_build_without_address_or_key_makes_no_request()
    {
        Publish("1.1.0");
        foreach (var updater in new[] { Updater(publicKey: ""), Updater(feed: "") })
        {
            var error = await Assert.ThrowsAsync<UpdateException>(() => updater.CheckAsync("1.0.0", AppUpdater.CurrentRid, CancellationToken.None));
            Assert.Equal(UpdateError.NotConfigured, error.Error);
        }
        Assert.Equal(0, _server.Count);
        Assert.False(new AppUpdater(new HttpClient(), AppUpdater.DefaultFeedUrl, AppUpdater.DefaultPublicKey, _dir).IsConfigured);
    }

    [Fact]
    public async Task A_package_that_is_not_the_announced_version_or_is_incomplete_is_not_staged()
    {
        var release = Publish("1.1.0");
        var updater = Updater();
        var zip = await updater.DownloadAsync(release, Path.Combine(_temp.Path, "download"), CancellationToken.None);

        var error = Assert.Throws<UpdateException>(() => updater.Stage(zip, release with { Version = "1.2.0" }));
        Assert.Equal(UpdateError.BadPackage, error.Error);
        Assert.False(Directory.Exists(updater.StagingDir));

        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update)) archive.GetEntry("Textida.exe")!.Delete();
        error = Assert.Throws<UpdateException>(() => updater.Stage(zip, release));
        Assert.Equal(UpdateError.BadPackage, error.Error);
        Assert.False(Directory.Exists(updater.StagingDir));
        Assert.Equal("v1.0.0", Marker(_dir));
    }

    [Fact]
    public void A_switch_to_a_folder_that_is_not_a_package_changes_nothing()
    {
        var updater = Updater();
        Directory.CreateDirectory(updater.StagingDir);
        File.WriteAllText(Path.Combine(updater.StagingDir, "junk.txt"), "x");

        Assert.Equal(1, Run(updater.Switch(updater.StagingDir, restart: false)));

        Assert.Equal("v1.0.0", Marker(_dir));
        Assert.False(Directory.Exists(updater.PreviousDir));
        using var entry = Registry.CurrentUser.OpenSubKey(_keyPath + @"\Uninstall")!;
        Assert.Equal("1.0.0", entry.GetValue("DisplayVersion"));
    }

    [Fact]
    public void An_interrupted_switch_is_finished_by_the_next_helper_run()
    {
        Directory.Move(_dir, _dir + ".aside");
        var script = Path.Combine(_dir + ".aside", "Setup.ps1");
        var args = new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Dir", _dir, "-Rollback", "-UninstallKey", @"HKCU:\" + _keyPath + @"\Uninstall" };
        using var process = Process.Start(new ProcessStartInfo("powershell.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        process.StandardOutput.ReadToEnd();

        Assert.Equal(1, Run(process));
        Assert.Equal("v1.0.0", Marker(_dir));
    }

    [Theory]
    [InlineData("1.1.0", "1.0.0", 1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("1.0.0", "1.0.0-rc1", 1)]
    [InlineData("0.0.0-abc", "1.0.0", -1)]
    [InlineData("1.10.0", "1.9.9", 1)]
    public void Versions_compare_by_number_and_a_suffix_makes_a_version_older(string a, string b, int expected) =>
        Assert.Equal(expected, Math.Sign(AppUpdater.Compare(a, b)));

    [Fact]
    public async Task The_feed_must_come_over_https_unless_it_is_on_this_computer()
    {
        var error = await Assert.ThrowsAsync<UpdateException>(() => Updater(feed: "http://updates.example.invalid/feed.json").CheckAsync("1.0.0", AppUpdater.CurrentRid, CancellationToken.None));
        Assert.Equal(UpdateError.BadFeed, error.Error);
    }
}

[Collection("LocState")]
public class AppUpdaterWindowTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly FeedServer _server = new();

    public void Dispose()
    {
        if (Avalonia.Application.Current is App app && app.Journal is not null) TestApp.Cleanup(app);
        _server.Dispose();
        _temp.Dispose();
    }

    private AppUpdater Updater()
    {
        var dir = Path.Combine(_temp.Path, "Textida");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Textida.exe"), "");
        File.WriteAllText(Path.Combine(dir, "Setup.ps1"), "");
        File.WriteAllText(Path.Combine(dir, "version.txt"), "1.0.0");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _server.Routes["/feed.json"] = Encoding.UTF8.GetBytes(AppUpdater.CreateFeed(key, new UpdateRelease("1.1.0", AppUpdater.CurrentRid, "p.zip", new string('0', 64), 10)));
        return new AppUpdater(new HttpClient(), _server.Base + "feed.json", Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), dir);
    }

    [AvaloniaFact]
    public async Task With_the_check_off_nothing_is_requested_and_with_it_on_one_request_shows_a_bar()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the per-user install is Windows only");
        var app = TestApp.Prepare(_temp);
        var w = app.CreateWindow(null);
        w.UpdaterOverride = Updater();
        Assert.False(app.Workbench.Settings.GetBool("updates.checkOnStart"));

        w.CheckUpdatesOnStart();
        await w.UpdateTask;
        Assert.Equal(0, _server.Count);

        app.Workbench.Settings.SetLayer(SettingSource.User, new JsonObject { ["updates.checkOnStart"] = true });
        w.CheckUpdatesOnStart();
        await w.UpdateTask;
        Assert.Equal(["/feed.json"], _server.Requests);
        w.Close();
    }

    [AvaloniaFact]
    public async Task An_unconfigured_build_says_so_and_makes_no_request()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the per-user install is Windows only");
        var app = TestApp.Prepare(_temp);
        var w = app.CreateWindow(null);
        var updater = Updater();
        w.UpdaterOverride = new AppUpdater(new HttpClient(), "", "", updater.InstallDir);

        w.CheckForUpdates();
        await w.UpdateTask;

        Assert.Equal(Loc.Get(K.UpdateNotConfigured), w.StatusMessageText);
        Assert.Equal(0, _server.Count);
        w.Close();
    }
}
