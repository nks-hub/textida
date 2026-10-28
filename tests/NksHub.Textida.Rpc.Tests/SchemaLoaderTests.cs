using NksHub.Textida.Plugins.Json;
using NksHub.Textida.Plugins.Json.Packages;
using NksHub.Textida.Plugins.Json.Schema;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>
/// P8.7.1 and P8.7.3: a schema is downloaded only after consent, kept on disk with a size limit and used offline, and the answers of the package registry client are kept in a bounded cache that expires.
/// The servers are listeners of the test on the loopback address; no real host is ever contacted.
/// </summary>
public sealed class SchemaLoaderTests : IDisposable
{
    const string Schema = """{ "type": "object", "properties": { "n": { "type": "integer" } } }""";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "textida-loader-" + Guid.NewGuid().ToString("N"));
    private readonly PackageRegistryTests.Stub _stub = new();
    private readonly List<RegistryClient> _clients = [];

    public SchemaLoaderTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        foreach (var client in _clients) client.Dispose();
        _stub.Dispose();
        Directory.Delete(_folder, true);
    }

    private string Consent => Path.Combine(_folder, "consent.json");

    private string Cache => Path.Combine(_folder, "cache");

    private void Allow(params string[] ids) => File.WriteAllText(Consent, System.Text.Json.JsonSerializer.Serialize(new { allowed = ids }));

    private SchemaLoader Loader(long cacheBytes = 100_000, long schemaBytes = 50_000, int ageSeconds = 3600, int retrySeconds = 60, long fileBytes = 65_536)
    {
        var client = new RegistryClient(new RegistrySettings(Consent, 5000, 100_000, 20, 50, 65_536));
        _clients.Add(client);
        return new SchemaLoader(client, new SchemaCacheSettings(Cache, cacheBytes, schemaBytes, TimeSpan.FromSeconds(ageSeconds), TimeSpan.FromSeconds(retrySeconds)), new SchemaSettings(fileBytes, 64, 200));
    }

    private SchemaEntry Remote(string path) => SchemaCatalog.ByAddress(_stub.Url + path)!;

    private string[] Copies => Directory.Exists(Cache) ? Directory.GetFiles(Cache, "*.json") : [];

    // ---- local files ----

    [Fact]
    public async Task A_schema_file_is_read_from_the_folder_of_the_document_with_no_consent_and_a_bad_one_is_told_from_a_missing_one()
    {
        File.WriteAllText(Path.Combine(_folder, "s.json"), Schema);
        File.WriteAllText(Path.Combine(_folder, "list.json"), "[1, 2]");
        File.WriteAllText(Path.Combine(_folder, "big.json"), new string(' ', 200) + Schema);
        var loader = new SchemaLoader(null, null, new SchemaSettings(100, 64, 200));
        string document = Path.Combine(_folder, "doc.json");

        var ok = await loader.LoadAsync(SchemaCatalog.ByAddress("s.json")!, document, network: true);
        Assert.Equal((SchemaState.Ready, Schema), (ok.State, ok.Text));
        Assert.Equal(SchemaState.Ready, (await loader.LoadAsync(SchemaCatalog.ByAddress(Path.Combine(_folder, "s.json"))!, null, network: false)).State); // an absolute path needs no folder
        Assert.Equal(SchemaState.Invalid, (await loader.LoadAsync(SchemaCatalog.ByAddress("list.json")!, document, network: true)).State);
        Assert.Equal(SchemaState.TooLarge, (await loader.LoadAsync(SchemaCatalog.ByAddress("big.json")!, document, network: true)).State);
        Assert.Equal(SchemaState.Unavailable, (await loader.LoadAsync(SchemaCatalog.ByAddress("nothing.json")!, document, network: true)).State);
        Assert.Equal(SchemaState.Unavailable, (await loader.LoadAsync(SchemaCatalog.ByAddress("s.json")!, null, network: true)).State); // a relative path needs the folder of a file
    }

    [Fact]
    public async Task A_schema_path_on_another_computer_is_not_read_because_a_file_may_not_reach_out_without_the_consent_of_a_download()
    {
        var loader = new SchemaLoader(null, null, new SchemaSettings(65_536, 64, 200));
        foreach (var address in new[] { "//127.0.0.1/share/s.json", @"\\127.0.0.1\share\s.json", "file://127.0.0.1/share/s.json" })
        {
            var load = await loader.LoadAsync(SchemaCatalog.ByAddress(address)!, Path.Combine(_folder, "doc.json"), network: true);
            Assert.Equal(SchemaState.Unavailable, load.State);
            Assert.Contains("another computer", load.Reason);
        }
    }

    [Fact]
    public async Task A_carried_schema_is_returned_as_it_is_and_an_address_without_the_download_options_is_not_allowed()
    {
        var loader = new SchemaLoader(null, null, new SchemaSettings(65_536, 64, 200));
        var carried = await loader.LoadAsync(SchemaCatalog.Bundled[0], null, network: false);
        Assert.Equal(SchemaState.Ready, carried.State);
        Assert.Contains("\"properties\"", carried.Text);

        var remote = await loader.LoadAsync(Remote("/x.json"), null, network: true);
        Assert.Equal((SchemaState.NoConsent, "127.0.0.1"), (remote.State, remote.Host));
        Assert.Empty(_stub.Requests);
    }

    // ---- the network ----

    [Fact]
    public async Task Without_consent_nothing_is_sent_nothing_is_kept_and_the_host_is_named()
    {
        _stub.Serve("/s.json", Schema);
        var loader = Loader();

        var none = await loader.LoadAsync(Remote("/s.json"), null, network: true); // there is no consent file
        Assert.Equal((SchemaState.NoConsent, "127.0.0.1", null), (none.State, none.Host, none.Text));
        Allow("npm", "schemas:example.test"); // other ids and other hosts do not allow this one
        Assert.Equal(SchemaState.NoConsent, (await loader.LoadAsync(Remote("/s.json"), null, network: true)).State);
        Assert.Equal(SchemaState.NoConsent, (await loader.LoadAsync(Remote("/s.json"), null, network: false)).State);
        Assert.Empty(_stub.Requests);
        Assert.Empty(Copies);
    }

    [Fact]
    public async Task After_consent_the_schema_is_downloaded_once_kept_on_disk_and_used_again_while_it_is_recent()
    {
        _stub.Serve("/s.json", Schema);
        Allow("schemas:127.0.0.1");
        var loader = Loader();

        var first = await loader.LoadAsync(Remote("/s.json"), null, network: true);
        Assert.Equal((SchemaState.Ready, Schema, false, false), (first.State, first.Text, first.FromCache, first.Offline));
        Assert.Single(Copies);
        Assert.True(loader.CachedBytes >= Schema.Length);

        var second = await loader.LoadAsync(Remote("/s.json"), null, network: true);
        Assert.Equal((Schema, true, false), (second.Text, second.FromCache, second.Offline));
        Assert.Single(_stub.Requests);
    }

    [Fact]
    public async Task When_the_host_does_not_answer_the_saved_copy_is_used_and_the_host_is_left_alone_for_a_while()
    {
        _stub.Serve("/s.json", Schema);
        Allow("schemas:127.0.0.1");
        var loader = Loader(ageSeconds: 0); // a copy is never recent, so the host is asked each time

        Assert.Equal(Schema, (await loader.LoadAsync(Remote("/s.json"), null, network: true)).Text);
        _stub.Serve("/s.json", "overloaded", 503);

        var offline = await loader.LoadAsync(Remote("/s.json"), null, network: true);
        Assert.Equal((SchemaState.Ready, Schema, true, true), (offline.State, offline.Text, offline.FromCache, offline.Offline));
        Assert.Contains("503", offline.Reason);
        Assert.Equal(2, _stub.Requests.Count);

        var quiet = await loader.LoadAsync(Remote("/s.json"), null, network: true);
        Assert.True(quiet.Offline);
        Assert.Equal(2, _stub.Requests.Count); // asked again only after the retry time

        var again = await Loader(ageSeconds: 0, retrySeconds: 0).LoadAsync(Remote("/s.json"), null, network: true); // a new process asks at once
        Assert.True(again.Offline);
        Assert.Equal(3, _stub.Requests.Count);
    }

    [Fact]
    public async Task A_host_that_did_not_answer_and_has_no_copy_gives_no_schema_and_a_schema_that_is_not_one_is_not_kept()
    {
        _stub.Serve("/down.json", "oops", 500);
        _stub.Serve("/page.json", "<html>not a schema</html>");
        Allow("schemas:127.0.0.1");
        var loader = Loader();

        var down = await loader.LoadAsync(Remote("/down.json"), null, network: true);
        Assert.Equal((SchemaState.Unavailable, null), (down.State, down.Text));
        Assert.Contains("500", down.Reason);

        var page = await loader.LoadAsync(Remote("/page.json"), null, network: true);
        Assert.Equal((SchemaState.Unavailable, null), (page.State, page.Text));
        Assert.Empty(Copies);

        var cachedOnly = await loader.LoadAsync(Remote("/down.json"), null, network: false); // completion and hover never wait for the network
        Assert.Equal(SchemaState.Unavailable, cachedOnly.State);
        Assert.Equal(2, _stub.Requests.Count);
    }

    [Fact]
    public async Task A_schema_over_the_size_limit_is_refused_and_not_kept()
    {
        _stub.Serve("/big.json", "{ \"description\": \"" + new string('x', 2000) + "\" }");
        Allow("schemas:127.0.0.1");

        var big = await Loader(schemaBytes: 1000).LoadAsync(Remote("/big.json"), null, network: true);

        Assert.Equal((SchemaState.TooLarge, null), (big.State, big.Text));
        Assert.Empty(Copies);
    }

    [Fact]
    public async Task The_copies_on_disk_stay_within_the_size_limit_and_the_oldest_goes_first()
    {
        string Body(char c) => "{ \"description\": \"" + new string(c, 500) + "\" }";
        _stub.Serve("/a.json", Body('a'));
        _stub.Serve("/b.json", Body('b'));
        Allow("schemas:127.0.0.1");
        var loader = Loader(cacheBytes: 700);

        Assert.Equal(SchemaState.Ready, (await loader.LoadAsync(Remote("/a.json"), null, network: true)).State);
        await Task.Delay(100); // the files differ in their time
        Assert.Equal(SchemaState.Ready, (await loader.LoadAsync(Remote("/b.json"), null, network: true)).State);

        Assert.Single(Copies);
        Assert.True(loader.CachedBytes <= 700);
        Assert.Equal(SchemaState.Ready, (await loader.LoadAsync(Remote("/b.json"), null, network: false)).State);
        Assert.Equal(SchemaState.Unavailable, (await loader.LoadAsync(Remote("/a.json"), null, network: false)).State);
    }

    [Fact]
    public async Task A_revoke_ends_the_use_of_the_copies_and_deletes_them()
    {
        _stub.Serve("/s.json", Schema);
        Allow("schemas:127.0.0.1");
        var loader = Loader();
        await loader.LoadAsync(Remote("/s.json"), null, network: true);
        Assert.Single(Copies);

        Allow();

        Assert.Equal(SchemaState.NoConsent, (await loader.LoadAsync(Remote("/s.json"), null, network: false)).State);
        Assert.Empty(Copies);
        Assert.Single(_stub.Requests);
    }

    // ---- the cache of the registry client (P8.7.3) ----

    private RegistryClient Registry(long cacheBytes, int lifetimeSeconds, out NpmAdapter npm)
    {
        npm = new NpmAdapter();
        npm.Override(_stub.Url);
        Allow("npm");
        var client = new RegistryClient(new RegistrySettings(Consent, 5000, 100_000, 20, 50, 65_536, cacheBytes, lifetimeSeconds));
        _clients.Add(client);
        return client;
    }

    [Fact]
    public async Task The_cache_of_registry_answers_holds_no_more_than_its_limit_and_drops_the_oldest_first()
    {
        foreach (var name in new[] { "a", "b", "c" }) _stub.Serve("/" + name, new string(name[0], 100));
        var client = Registry(250, 0, out var npm);
        Uri Url(string name) => npm.VersionsUrl(name);

        await client.GetAsync(npm, Url("a"));
        await client.GetAsync(npm, Url("b"));
        Assert.Equal(200, client.CachedBytes);
        await client.GetAsync(npm, Url("c")); // 300 bytes would not fit: a goes
        Assert.Equal(200, client.CachedBytes);
        Assert.True((await client.GetAsync(npm, Url("b"))).Cached);
        Assert.Equal(3, client.Requests);

        Assert.False((await client.GetAsync(npm, Url("a"))).Cached); // a was dropped, so it is asked for again
        Assert.Equal(4, client.Requests);
        Assert.True(client.CachedBytes <= 250);
    }

    [Fact]
    public async Task An_answer_larger_than_the_whole_cache_is_not_kept()
    {
        _stub.Serve("/big", new string('x', 400));
        var client = Registry(250, 0, out var npm);

        Assert.False((await client.GetAsync(npm, npm.VersionsUrl("big"))).Cached);
        Assert.False((await client.GetAsync(npm, npm.VersionsUrl("big"))).Cached);

        Assert.Equal((0, 2), (client.CachedBytes, client.Requests));
    }

    [Fact]
    public async Task A_cached_answer_expires_after_its_lifetime_and_refresh_drops_it_at_any_time()
    {
        _stub.Serve("/a", "answer");
        var client = Registry(0, 1, out var npm);
        var url = npm.VersionsUrl("a");

        Assert.False((await client.GetAsync(npm, url)).Cached);
        Assert.True((await client.GetAsync(npm, url)).Cached);
        await Task.Delay(1200);
        Assert.False((await client.GetAsync(npm, url)).Cached); // older than the lifetime
        Assert.Equal(2, client.Requests);

        client.Refresh("npm");
        Assert.Equal(0, client.CachedBytes);
        Assert.False((await client.GetAsync(npm, url)).Cached);
        Assert.Equal(3, client.Requests);
    }
}
