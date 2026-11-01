using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Headless.XUnit;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Manager;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.8.1 in the app: a catalog in a real Git repository, the check of hash and signature, the trust in a publisher key and the new consent for an update that asks for more.</summary>
[Collection("LocState")]
public class PluginCatalogAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _repo;
    private readonly List<(string Id, string Version, string Package, string Sha, string[] Permissions, string? Signature)> _entries = [];

    public PluginCatalogAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _repo = Path.Combine(_temp.Path, "catalog-repo");
        Directory.CreateDirectory(Path.Combine(_repo, "packages"));
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private string PublicKey => Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());

    private byte[] ZipBytes(string version, string[] permissions)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry("nkspad-plugin.json").Open()))
            writer.Write("{\"schemaVersion\":1,\"id\":\"acme.clips\",\"name\":\"Acme clips\",\"version\":\"" + version + "\",\"api\":{\"min\":1,\"max\":1}," +
                "\"permissions\":[" + string.Join(",", permissions.Select(p => "\"" + p + "\"")) + "]," +
                "\"contributes\":{\"commands\":[{\"id\":\"acme.clips.hi\",\"title\":\"Hi\"}],\"clips\":[{\"id\":\"hi\",\"title\":\"Hi\",\"text\":\"hi\",\"command\":\"acme.clips.hi\"}]}}");
        return stream.ToArray();
    }

    /// <summary>Adds a release to the index; <paramref name="listed"/> are the permissions the signed entry states (the ZIP may ask for others).</summary>
    private void Release(string version, string[] zipPermissions, string[]? listed = null, bool tamperHash = false, bool tamperSignature = false)
    {
        listed ??= zipPermissions;
        var bytes = ZipBytes(version, zipPermissions);
        string package = $"packages/clips-{version}.zip";
        File.WriteAllBytes(Path.Combine(_repo, package.Replace('/', Path.DirectorySeparatorChar)), bytes);
        string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string signature = PluginCatalog.Sign(_key, "acme.clips", version, sha, listed);
        if (tamperSignature) signature = PluginCatalog.Sign(_key, "acme.clips", version + "x", sha, listed);
        if (tamperHash) File.AppendAllText(Path.Combine(_repo, package.Replace('/', Path.DirectorySeparatorChar)), "tampered");
        _entries.Add(("acme.clips", version, package, sha, listed, signature));
    }

    private void WriteIndex(string? publisherKey = null)
    {
        var entries = _entries.Select(e => "{\"id\":\"" + e.Id + "\",\"name\":\"Acme clips\",\"version\":\"" + e.Version + "\",\"description\":\"Clips\",\"publisher\":\"acme\",\"package\":\"" + e.Package + "\",\"sha256\":\"" + e.Sha +
            "\",\"permissions\":[" + string.Join(",", e.Permissions.Select(p => "\"" + p + "\"")) + "],\"signature\":\"" + e.Signature + "\"}");
        File.WriteAllText(Path.Combine(_repo, "index.json"), "{\"schemaVersion\":1,\"publishers\":[{\"id\":\"acme\",\"name\":\"Acme\",\"publicKey\":\"" + (publisherKey ?? PublicKey) + "\"}],\"plugins\":[" + string.Join(",", entries) + "]}");
    }

    private static void Git(string folder, params string[] args)
    {
        var info = new ProcessStartInfo(ToolLocator.Find("git")!) { WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in new[] { "-c", "user.name=catalog-test", "-c", "user.email=catalog@example.test", "-c", "commit.gpgsign=false" }.Concat(args)) info.ArgumentList.Add(a);
        using var process = Process.Start(info)!;
        string errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, errors);
    }

    private void Commit()
    {
        if (!Directory.Exists(Path.Combine(_repo, ".git"))) Git(_repo, "init", "--quiet");
        Git(_repo, "add", "-A");
        Git(_repo, "commit", "--quiet", "-m", "catalog");
    }

    private static bool HaveGit => ToolLocator.Find("git") is not null;

    private PluginCatalogIndex Open()
    {
        _app.Workbench.Set("plugins.catalogUrl", _repo);
        return PluginCatalogSource.ReadIndex(PluginCatalogSource.CacheFolder(_app.ProfileDirectory, _repo))!;
    }

    private static void Finish(Task task) => ToolTestKit.WaitFor(() => task.IsCompleted, "the task did not finish");

    [AvaloniaFact]
    public void A_catalog_from_a_git_repository_lists_the_plugins_and_installs_one_after_the_publisher_is_trusted()
    {
        if (!HaveGit) return;
        Release("1.0.0", [DocumentPermissions.Read]);
        WriteIndex();
        Commit();
        _app.Workbench.Set("plugins.catalogUrl", _repo);

        _dialogs.Answers.Enqueue(0); // run git
        Finish(_w.ShowPluginCatalogAsync(true));
        Assert.True(_w.IsPaletteOpen, _w.StatusMessageText);
        var row = Assert.Single(_w.QuickResults);
        Assert.Equal("Acme clips 1.0.0", row.Title);
        Assert.Contains(Loc.Get(K.CatalogStateNew), row.Detail);
        Assert.Contains("Acme", row.Detail);
        Assert.Contains("clone", _dialogs.Asked[0].Details![0]);

        _dialogs.Asked.Clear();
        _dialogs.Answers.Enqueue(0); // trust the publisher
        _dialogs.Answers.Enqueue(0); // allow the install
        row.Run();
        ToolTestKit.WaitFor(() => _app.Plugins.Manager.Installed().Count == 1, "the plugin was not installed");
        Assert.Equal(Loc.Get(K.CatalogTrustTitle), _dialogs.Asked[0].Title);
        Assert.Contains(Assert.Single(PluginCatalogSource.ReadIndex(PluginCatalogSource.CacheFolder(_app.ProfileDirectory, _repo))!.Publishers).Fingerprint, _dialogs.Asked[0].Details![0]);
        Assert.Equal("1.0.0", _app.Plugins.Manager.Installed().Single().Version);
        Assert.Equal(PublisherTrust.Trusted, new CatalogTrust(Path.Combine(_app.ProfileDirectory, "plugin-publishers.json")).Of(PluginCatalogSource.ReadIndex(PluginCatalogSource.CacheFolder(_app.ProfileDirectory, _repo))!.Publishers[0]));
    }

    [AvaloniaFact]
    public void An_update_that_asks_for_more_shows_the_new_permission_and_needs_a_new_consent()
    {
        if (!HaveGit) return;
        Release("1.0.0", [DocumentPermissions.Read]);
        WriteIndex();
        Commit();
        _app.Workbench.Set("plugins.catalogUrl", _repo);
        _dialogs.Answers.Enqueue(0);
        Finish(_w.ShowPluginCatalogAsync(true));
        _dialogs.Answers.Enqueue(0);
        _dialogs.Answers.Enqueue(0);
        _w.QuickResults.Single().Run();
        ToolTestKit.WaitFor(() => _app.Plugins.Manager.Installed().Count == 1, "not installed");

        Release("1.1.0", [DocumentPermissions.Read, DocumentPermissions.EditDirect]);
        WriteIndex();
        Commit();
        _dialogs.Asked.Clear();
        Finish(_w.ShowPluginCatalogAsync(true));
        var update = _w.QuickResults.Single(r => r.Title.EndsWith("1.1.0"));
        Assert.Contains(Loc.Get(K.CatalogStateUpdate, ("installed", "1.0.0")), update.Detail);

        _dialogs.Asked.Clear();
        _dialogs.Answers.Enqueue(1); // the user declines the wider permissions
        update.Run();
        ToolTestKit.WaitFor(() => _dialogs.Asked.Count >= 1, "no consent asked");
        var consent = _dialogs.Asked.Single();
        Assert.Equal(Loc.Get(K.PluginsConsentTitleUpdate), consent.Title);
        Assert.Contains(consent.Details!, d => d.StartsWith(DocumentPermissions.EditDirect, StringComparison.Ordinal));
        ToolTestKit.WaitFor(() => _dialogs.Answers.Count == 0, "the answer was not taken");
        Assert.Equal("1.0.0", _app.Plugins.Manager.Installed().Single().Version);
        Assert.False(_app.Plugins.Permissions.IsGranted("acme.clips", DocumentPermissions.EditDirect, GrantScope.All));
    }

    private async Task<(bool Installed, ChoiceRequest? Message)> TryInstall(CatalogEntry entry, PluginCatalogIndex index)
    {
        _dialogs.Asked.Clear();
        bool done = await _w.InstallFromCatalogAsync(PluginCatalogSource.CacheFolder(_app.ProfileDirectory, _repo), index, entry);
        return (done, _dialogs.Asked.FirstOrDefault(a => a.Title == Loc.Get(K.CatalogTitle)));
    }

    [AvaloniaFact]
    public async Task A_changed_zip_a_bad_signature_a_swapped_key_and_a_zip_that_asks_for_more_are_all_refused()
    {
        if (!HaveGit) return;
        Release("1.0.0", [DocumentPermissions.Read], tamperHash: true);
        Release("1.1.0", [DocumentPermissions.Read], tamperSignature: true);
        Release("1.2.0", [DocumentPermissions.Read, DocumentPermissions.EditDirect], listed: [DocumentPermissions.Read]);
        WriteIndex();
        Commit();
        var index = Open();
        _dialogs.Answers.Enqueue(0);
        await _w.ShowPluginCatalogAsync(true);
        index = PluginCatalogSource.ReadIndex(PluginCatalogSource.CacheFolder(_app.ProfileDirectory, _repo))!;
        new CatalogTrust(Path.Combine(_app.ProfileDirectory, "plugin-publishers.json")).Trust(index.Publishers[0]);
        for (int i = 0; i < 8; i++) _dialogs.Answers.Enqueue(0);

        var (hashOk, hashMessage) = await TryInstall(index.Entries.Single(e => e.Version == "1.0.0"), index);
        Assert.False(hashOk);
        Assert.Contains("SHA-256", hashMessage!.Message);

        var (sigOk, sigMessage) = await TryInstall(index.Entries.Single(e => e.Version == "1.1.0"), index);
        Assert.False(sigOk);
        Assert.Equal(Loc.Get(K.CatalogBadSignature, ("publisher", "acme"), ("name", "Acme clips")), sigMessage!.Message);

        var (moreOk, moreMessage) = await TryInstall(index.Entries.Single(e => e.Version == "1.2.0"), index);
        Assert.False(moreOk);
        Assert.Equal(Loc.Get(K.CatalogMismatch, ("name", "Acme clips")), moreMessage!.Message);
        Assert.Empty(_app.Plugins.Manager.Installed());

        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var swapped = new PluginCatalogIndex([index.Publishers[0] with { PublicKey = Convert.ToBase64String(other.ExportSubjectPublicKeyInfo()) }], index.Entries, []);
        var forged = index.Entries.Single(e => e.Version == "1.2.0") with { Permissions = [DocumentPermissions.Read], Signature = PluginCatalog.Sign(other, "acme.clips", "1.2.0", index.Entries.Single(e => e.Version == "1.2.0").Sha256, [DocumentPermissions.Read]) };
        var (keyOk, keyMessage) = await TryInstall(forged, swapped);
        Assert.False(keyOk);
        Assert.Equal(Loc.Get(K.CatalogKeyChanged, ("publisher", "Acme")), keyMessage!.Message);
        Assert.Empty(_app.Plugins.Manager.Installed());
    }

    [AvaloniaFact]
    public void Without_a_source_a_bad_source_or_a_repository_without_an_index_the_catalog_says_so_and_runs_nothing()
    {
        Finish(_w.ShowPluginCatalogAsync(false));
        Assert.Equal(Loc.Get(K.CatalogNoSource), _w.StatusMessageText);
        _app.Workbench.Set("plugins.catalogUrl", "http://insecure.test/x.git");
        Finish(_w.ShowPluginCatalogAsync(false));
        Assert.Contains("http://insecure.test", _w.StatusMessageText);
        Assert.Empty(_dialogs.Asked);

        if (!HaveGit) return;
        File.WriteAllText(Path.Combine(_repo, "readme.txt"), "no index");
        Commit();
        _app.Workbench.Set("plugins.catalogUrl", _repo);
        _dialogs.Answers.Enqueue(0);
        Finish(_w.ShowPluginCatalogAsync(true));
        Assert.Contains("index.json", _w.StatusMessageText);
    }

}

public class CatalogTrustTests
{
    [Fact]
    public void A_publisher_is_trusted_only_while_its_key_is_the_confirmed_one_and_the_source_check_refuses_odd_addresses()
    {
        string file = Path.Combine(Path.GetTempPath(), "trust-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        try
        {
            var trust = new CatalogTrust(file);
            var publisher = new CatalogPublisher("acme", "Acme", "AAAA");
            Assert.Equal(PublisherTrust.New, trust.Of(publisher));
            trust.Trust(publisher);
            Assert.Equal(PublisherTrust.Trusted, trust.Of(publisher));
            Assert.Equal(PublisherTrust.Changed, trust.Of(publisher with { PublicKey = "BBBB" }));
            Assert.Equal(PublisherTrust.New, trust.Of(publisher with { Id = "other" }));
        }
        finally { File.Delete(file); }
        Assert.True(PluginCatalogSource.IsSource("https://example.test/catalog.git"));
        Assert.False(PluginCatalogSource.IsSource("http://example.test/catalog.git"));
        Assert.False(PluginCatalogSource.IsSource("ext::sh -c id"));
        Assert.False(PluginCatalogSource.IsSource("ssh://example.test/catalog.git"));
        Assert.False(PluginCatalogSource.IsSource(Path.Combine(Path.GetTempPath(), "no-such-folder-" + Guid.NewGuid().ToString("N"))));
        Assert.Null(PluginCatalogSource.LocalPackage(Path.GetTempPath(), "../x.zip"));
    }
}
