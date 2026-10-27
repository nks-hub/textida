using System.IO.Compression;
using System.Text.Json;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Manager;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.Rpc.Tests;

public sealed class PluginManagerTests : IAsyncLifetime
{
    private const string Payload = "NksHub.Textida.PluginHost.TestPlugin.dll";
    private static readonly PluginInstallLimits Limits = new(256L << 20, 500);

    private readonly string _root = T.TempDir();
    private readonly string _zips = T.TempDir();
    private readonly PluginPermissions _permissions = new();
    private readonly PluginHost.PluginHost _host = new(PluginHostTests.Options());
    private readonly PluginManager _manager;

    public PluginManagerTests() =>
        _manager = new PluginManager(Path.Combine(_root, "installed"), 1, 1 << 20, Limits, new HashSet<string> { "textida.json" }, _permissions, _host);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        foreach (var folder in new[] { _root, _zips })
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
    }

    /// <summary>A plugin package whose process is the test plugin: its files from the test output folder, started by the dotnet host.</summary>
    private string Zip(string id, string version, string[] permissions, Action<ZipArchive>? change = null, bool withPayload = true, string? manifest = null)
    {
        var path = Path.Combine(_zips, Guid.NewGuid().ToString("N") + ".zip");
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        manifest ??= "{\"schemaVersion\":1,\"id\":\"" + id + "\",\"name\":\"Test " + id + "\",\"version\":\"" + version + "\",\"api\":{\"min\":1,\"max\":1}," +
            "\"permissions\":" + JsonSerializer.Serialize(permissions) + "," +
            "\"main\":{\"executable\":" + JsonSerializer.Serialize(T.DotnetFullPath) + ",\"args\":[\"" + Payload + "\"]}," +
            "\"contributes\":{\"commands\":[{\"id\":\"" + id + ".run\",\"title\":\"Run\"}]}}";
        Add(zip, "nkspad-plugin.json", manifest);
        if (withPayload)
            foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "NksHub.*").Where(f => f.EndsWith(".dll") || f.EndsWith(".runtimeconfig.json") || f.EndsWith(".deps.json")))
                zip.CreateEntryFromFile(file, Path.GetFileName(file));
        change?.Invoke(zip);
        return path;
    }

    private static void Add(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(text);
    }

    private static PluginInstallException Refused(Func<PluginPackage> prepare) => Assert.Throws<PluginInstallException>(prepare);

    private async Task Install(string zip, params string[] approved) => await _manager.CommitAsync(_manager.Prepare(zip), approved);

    [Fact]
    public async Task AZipInstallsAfterConsentThenStartsAndAnswersInitialize()
    {
        var package = _manager.Prepare(Zip("acme.one", "1.0.0", [DocumentPermissions.Read, DocumentPermissions.WorkspaceFiles]));
        Assert.Equal([DocumentPermissions.Read, DocumentPermissions.WorkspaceFiles], package.NewPermissions);
        Assert.False(package.FromInternet);

        var refused = await Assert.ThrowsAsync<PluginInstallException>(() => _manager.CommitAsync(package, [DocumentPermissions.Read]));
        Assert.Equal(PluginInstallError.ConsentRequired, refused.Error);
        Assert.Equal([DocumentPermissions.WorkspaceFiles], refused.Details);
        Assert.Empty(_manager.Installed());
        Assert.False(_permissions.IsGranted("acme.one", DocumentPermissions.Read, GrantScope.All));

        await _manager.CommitAsync(package, package.NewPermissions);
        var row = Assert.Single(_manager.Installed());
        Assert.Equal(("acme.one", "1.0.0", true), (row.Id, row.Version, row.Enabled));
        Assert.True(_permissions.IsGranted("acme.one", DocumentPermissions.WorkspaceFiles, GrantScope.All));
        Assert.True(File.Exists(Path.Combine(_manager.Root, "acme.one", "1.0.0", Payload)));

        Assert.Equal(PluginState.Inactive, _host.GetState("acme.one"));
        Assert.Equal(["acme.one"], await _host.ActivateAsync("onCommand:acme.one.run"));
        Assert.Equal(PluginState.Running, _host.GetState("acme.one"));
        Assert.Equal("{\"x\":1}", (await _host.CallAsync("acme.one", "echo", T.Json("{\"x\":1}"))).GetRawText());
    }

    [Fact]
    public async Task AGrantIsStoredAndRevocable()
    {
        await Install(Zip("acme.one", "1.0.0", [DocumentPermissions.Read]), DocumentPermissions.Read);
        var again = new PluginPermissions();
        var restarted = new PluginManager(_manager.Root, 1, 1 << 20, Limits, new HashSet<string>(), again);
        await restarted.StartAsync();
        Assert.True(again.IsGranted("acme.one", DocumentPermissions.Read, GrantScope.Any));

        restarted.SetPermission("acme.one", DocumentPermissions.Read, false);
        Assert.False(again.IsGranted("acme.one", DocumentPermissions.Read, GrantScope.Any));
        Assert.Empty(Assert.Single(restarted.Installed()).Granted);
        Assert.Equal(DocumentErrorCodes.PermissionDenied, Assert.IsType<RpcException>(Record.Exception(() => again.Enter("acme.one", DocumentPermissions.Read, GrantScope.All, default))).Code);
    }

    [Fact]
    public void AZipSlipEntryIsRefusedAndNothingIsLeftBehind()
    {
        var e = Refused(() => _manager.Prepare(Zip("acme.one", "1.0.0", [], zip => Add(zip, "../evil.txt", "x"), withPayload: false)));
        Assert.Equal(PluginInstallError.UnsafePath, e.Error);
        Assert.False(File.Exists(Path.Combine(_manager.Root, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(_manager.Root, "..", "evil.txt")));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(_manager.Root, ".staging")));
        Assert.Equal(PluginInstallError.UnsafePath, Refused(() => _manager.Prepare(Zip("acme.one", "1.0.0", [], zip => Add(zip, "sub/../../evil.txt", "x"), withPayload: false))).Error);
    }

    [Fact]
    public void ALinkAQuotaAndADuplicateEachHaveTheirOwnError()
    {
        var link = Zip("acme.one", "1.0.0", [], zip => zip.CreateEntry("outside").ExternalAttributes = (0xA000 | 0x1FF) << 16, withPayload: false);
        Assert.Equal(PluginInstallError.SymbolicLink, Refused(() => _manager.Prepare(link)).Error);

        var small = new PluginManager(Path.Combine(_root, "small"), 1, 1 << 20, new PluginInstallLimits(100, 10), new HashSet<string>(), _permissions);
        Assert.Equal(PluginInstallError.TooLarge, Refused(() => small.Prepare(Zip("acme.one", "1.0.0", [], zip => Add(zip, "big.bin", new string('x', 500)), withPayload: false))).Error);
        var few = new PluginManager(Path.Combine(_root, "few"), 1, 1 << 20, new PluginInstallLimits(1 << 20, 1), new HashSet<string>(), _permissions);
        Assert.Equal(PluginInstallError.TooManyEntries, Refused(() => few.Prepare(Zip("acme.one", "1.0.0", [], zip => Add(zip, "a.txt", "a"), withPayload: false))).Error);

        Assert.Equal(PluginInstallError.DuplicateEntry, Refused(() => _manager.Prepare(Zip("acme.one", "1.0.0", [], zip => { Add(zip, "a.txt", "a"); Add(zip, "a.txt", "b"); }, withPayload: false))).Error);
        File.WriteAllText(Path.Combine(_zips, "no.zip"), "not a zip");
        Assert.Equal(PluginInstallError.NotAZip, Refused(() => _manager.Prepare(Path.Combine(_zips, "no.zip"))).Error);
    }

    [Fact]
    public async Task ABuiltInIdAndAnInstalledOneThatIsNotNewerCollide()
    {
        Assert.Equal(PluginInstallError.IdCollision, Refused(() => _manager.Prepare(Zip("textida.json", "1.0.0", [], withPayload: false, manifest: DataManifest("textida.json", "1.0.0")))).Error);
        await Install(Zip("acme.data", "1.2.0", [], withPayload: false, manifest: DataManifest("acme.data", "1.2.0")));
        Assert.Equal(PluginInstallError.NotNewer, Refused(() => _manager.Prepare(Zip("acme.data", "1.2.0", [], withPayload: false, manifest: DataManifest("acme.data", "1.2.0")))).Error);
        Assert.Equal(PluginInstallError.NotNewer, Refused(() => _manager.Prepare(Zip("acme.data", "1.10.0", [], withPayload: false, manifest: DataManifest("acme.data", "1.1.9")))).Error);
        Assert.Equal("1.10.0", _manager.Prepare(Zip("acme.data", "1.10.0", [], withPayload: false, manifest: DataManifest("acme.data", "1.10.0"))).Version);
    }

    private static string DataManifest(string id, string version, string permissions = "") =>
        "{\"schemaVersion\":1,\"id\":\"" + id + "\",\"name\":\"Data\",\"version\":\"" + version + "\",\"api\":{\"min\":1,\"max\":1}" + permissions +
        ",\"contributes\":{\"commands\":[{\"id\":\"" + id + ".hi\",\"title\":\"Hi\"}],\"clips\":[{\"id\":\"hi\",\"title\":\"Hi\",\"text\":\"hi\",\"command\":\"" + id + ".hi\"}]}}";

    [Theory]
    [InlineData("\"made.up\"")]
    [InlineData("\"document.secrets.reveal\"")]
    [InlineData("\"document.read\",\"document.read\"")]
    public void AManifestWithAnUnknownOrRepeatedPermissionIsRefused(string list)
    {
        var e = Refused(() => _manager.Prepare(Zip("acme.data", "1.0.0", [], withPayload: false, manifest: DataManifest("acme.data", "1.0.0", ",\"permissions\":[" + list + "]"))));
        Assert.Equal(PluginInstallError.InvalidManifest, e.Error);
        Assert.Contains(e.Details, d => d.Contains("ermission"));
    }

    [Fact]
    public async Task AnUpdateAndARollbackKeepConsentForUnchangedPermissionsAndAskAgainForNewOnes()
    {
        await Install(Zip("acme.one", "1.0.0", [DocumentPermissions.Read, DocumentPermissions.EditDirect]), DocumentPermissions.Read, DocumentPermissions.EditDirect);
        _manager.SetPermission("acme.one", DocumentPermissions.EditDirect, false);

        var update = _manager.Prepare(Zip("acme.one", "1.1.0", [DocumentPermissions.Read, DocumentPermissions.EditDirect, DocumentPermissions.WorkspaceFiles]));
        Assert.Equal([DocumentPermissions.WorkspaceFiles], update.NewPermissions);
        Assert.Equal("1.0.0", update.InstalledVersion);
        var e = await Assert.ThrowsAsync<PluginInstallException>(() => _manager.CommitAsync(update, []));
        Assert.Equal(PluginInstallError.ConsentRequired, e.Error);
        Assert.Equal("1.0.0", Assert.Single(_manager.Installed()).Version);

        await _manager.CommitAsync(update, [DocumentPermissions.WorkspaceFiles]);
        var row = Assert.Single(_manager.Installed());
        Assert.Equal(("1.1.0", "1.0.0"), (row.Version, row.PreviousVersion));
        Assert.Equal([DocumentPermissions.Read, DocumentPermissions.WorkspaceFiles], row.Granted);
        Assert.False(_permissions.IsGranted("acme.one", DocumentPermissions.EditDirect, GrantScope.All));

        await _manager.RollBackAsync("acme.one", []);
        row = Assert.Single(_manager.Installed());
        Assert.Equal(("1.0.0", "1.1.0"), (row.Version, row.PreviousVersion));
        Assert.Equal([DocumentPermissions.Read], row.Granted);
        Assert.False(_permissions.IsGranted("acme.one", DocumentPermissions.WorkspaceFiles, GrantScope.All));
        Assert.True(File.Exists(Path.Combine(_manager.Root, "acme.one", "1.1.0", Payload)));

        Assert.Equal(["acme.one"], await _host.ActivateAsync("onCommand:acme.one.run"));
    }

    [Fact]
    public async Task ABrokenUpdateKeepsTheOldVersionAndARollbackNeedsAPreviousOne()
    {
        await Install(Zip("acme.one", "1.0.0", [DocumentPermissions.Read]), DocumentPermissions.Read);
        var broken = Zip("acme.one", "2.0.0", [], withPayload: false, manifest: DataManifest("acme.one", "2.0.0", ",\"permissions\":[\"made.up\"]"));
        Assert.Equal(PluginInstallError.InvalidManifest, Refused(() => _manager.Prepare(broken)).Error);
        Assert.Equal("1.0.0", Assert.Single(_manager.Installed()).Version);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(_manager.Root, ".staging")));
        Assert.Equal(PluginInstallError.NothingToRollBack, (await Assert.ThrowsAsync<PluginInstallException>(() => _manager.RollBackAsync("acme.one", []))).Error);

        await Install(Zip("acme.one", "1.5.0", [DocumentPermissions.Read, DocumentPermissions.Virtual]), DocumentPermissions.Virtual);
        Assert.Empty(_manager.RollBackPermissions("acme.one"));
        await _manager.RollBackAsync("acme.one", []);
        Assert.Equal("1.0.0", Assert.Single(_manager.Installed()).Version);
        var again = _manager.Prepare(Zip("acme.one", "1.6.0", [DocumentPermissions.Read]));
        Assert.Empty(again.NewPermissions);
    }

    [Fact]
    public async Task ADisabledPluginDoesNotStart()
    {
        await Install(Zip("acme.one", "1.0.0", [DocumentPermissions.Read]), DocumentPermissions.Read);
        await _manager.SetEnabledAsync("acme.one", false);
        Assert.False(Assert.Single(_manager.Installed()).Enabled);
        Assert.Empty(await _host.ActivateAsync("onCommand:acme.one.run"));
        Assert.False(_host.IsRegistered("acme.one"));
        Assert.False(_permissions.IsGranted("acme.one", DocumentPermissions.Read, GrantScope.All));

        var restarted = new PluginManager(_manager.Root, 1, 1 << 20, Limits, new HashSet<string>(), new PluginPermissions(), new PluginHost.PluginHost(PluginHostTests.Options()));
        Assert.Empty((await restarted.StartAsync()).Plugins);

        await _manager.SetEnabledAsync("acme.one", true);
        Assert.Equal(["acme.one"], await _host.ActivateAsync("onCommand:acme.one.run"));
        Assert.True(_permissions.IsGranted("acme.one", DocumentPermissions.Read, GrantScope.All));
    }

    [Fact]
    public async Task UninstallStopsTheProcessAndRemovesOnlyItsOwnFiles()
    {
        await Install(Zip("acme.one", "1.0.0", [DocumentPermissions.Read]), DocumentPermissions.Read);
        await Install(Zip("acme.two", "1.0.0", []));
        await _host.ActivateAsync("onCommand:acme.one.run");
        var pid = (await _host.CallAsync("acme.one", "pid")).GetInt32();

        await _manager.UninstallAsync("acme.one");
        Assert.False(Directory.Exists(Path.Combine(_manager.Root, "acme.one")));
        Assert.True(File.Exists(Path.Combine(_manager.Root, "acme.two", "1.0.0", Payload)));
        Assert.Equal(["acme.two"], _manager.Installed().Select(p => p.Id));
        Assert.False(_permissions.IsGranted("acme.one", DocumentPermissions.Read, GrantScope.All));
        Assert.False(_host.IsRegistered("acme.one"));
        await T.Until(() => { try { return System.Diagnostics.Process.GetProcessById(pid).HasExited; } catch (ArgumentException) { return true; } });
        Assert.Equal(PluginInstallError.UnknownPlugin, (await Assert.ThrowsAsync<PluginInstallException>(() => _manager.UninstallAsync("acme.one"))).Error);
    }
}
