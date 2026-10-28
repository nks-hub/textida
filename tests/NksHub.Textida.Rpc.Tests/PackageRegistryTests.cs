using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Plugins.Json.Packages;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>
/// P8.7.3 and P8.7.5: the package registry client and its seven adapters, with the real JSON plugin process. A registry is a listener of this test on the loopback
/// address that serves a small recorded answer in the shape the registry's public API documents; no real registry is ever contacted.
/// </summary>
public class PackageRegistryTests
{
    sealed class Owner : IDocumentOwnerThread
    {
        readonly object _gate = new();

        public Task<T> InvokeAsync<T>(Func<T> work)
        {
            try { lock (_gate) return Task.FromResult(work()); }
            catch (Exception e) { return Task.FromException<T>(e); }
        }
    }

    sealed class NoProposals : IEditProposals
    {
        public ValueTask<ProposalDecision> SubmitAsync(EditProposal proposal, CancellationToken cancellation) => throw new NotSupportedException();
    }

    /// <summary>A registry for one test: answers by path, counts what it was asked.</summary>
    public sealed class Stub : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly ConcurrentDictionary<string, (int Status, string Body)> _routes = new();
        public ConcurrentQueue<string> Requests { get; } = new();
        public string Url { get; }

        public Stub()
        {
            int port;
            using (var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0))
            {
                probe.Start();
                port = ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            Url = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(Url + "/");
            _listener.Start();
            _ = Task.Run(Serve);
        }

        public void Serve(string path, string body, int status = 200) => _routes[path] = (status, body);

        public int Count(string pathPrefix) => Requests.Count(r => r.StartsWith(pathPrefix, StringComparison.Ordinal));

        private async Task Serve()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }
                Requests.Enqueue(context.Request.Url!.PathAndQuery);
                var (status, body) = _routes.TryGetValue(context.Request.Url.AbsolutePath, out var route) ? route : (404, "{}");
                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.StatusCode = status;
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }

        public void Dispose() => _listener.Close();
    }

    sealed class Rig : IAsyncDisposable
    {
        public required PluginHost.PluginHost Host { get; init; }
        public required DocumentApi Docs { get; init; }
        public required Document Doc { get; init; }
        public required string Folder { get; init; }
        public required string ConfigPath { get; init; }
        public required Stub Registry { get; init; }

        public static async Task<Rig> CreateAsync(string fileName, string content, string[] allowed, Action<Stub>? routes = null)
        {
            var folder = Path.Combine(Path.GetTempPath(), "textida-packages-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, fileName);
            File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));
            var config = Path.Combine(folder, "package-registries.json");
            File.WriteAllText(config, ConfigJson(allowed));
            var stub = new Stub();
            routes?.Invoke(stub);
            PluginHost.PluginHost? host = null;
            PluginEventSender send = (plugin, name, payload) => host!.SendEventAsync(plugin, name, payload);
            var permissions = new PluginPermissions();
            foreach (var permission in new[] { DocumentPermissions.Read, DocumentPermissions.EditDirect })
                permissions.Grant("json", permission, GrantScope.All);
            var docs = new DocumentApi(new DocumentApiOptions
            {
                MaxReadBytes = 1 << 16, MaxFindMatches = 10, MaxSnapshotsPerPlugin = 2, SnapshotLifetime = TimeSpan.FromSeconds(10),
                EventQueueCapacity = 64, MaxRememberedBatches = 16, Permissions = permissions, CallLog = new PluginCallLog(100), Proposals = new NoProposals(),
                Tools = new DocumentApiTests.RecordingTools(), MaxSymbolsPerDocument = 10, MaxSymbolTextLength = 400, MaxSnippetLength = 100, MaxSnippetCarets = 2, TabTimeout = TimeSpan.FromSeconds(1),
                MaxSecretScanBytes = 1 << 20, MaxClipboardChars = 1000, MaxScanBytes = 1 << 22, MaxVirtualDocumentsPerPlugin = 4, MaxVirtualBytes = 1 << 20,
                MaxDiagnosticsPerDocument = 100, MaxCompletionItems = 100, MaxCompletionEdits = 4, CompletionTimeout = TimeSpan.FromSeconds(10),
            }, new Owner(), send, (plugin, method, payload, timeout, ct) => host!.CallAsync(plugin, method, payload, timeout, ct));
            host = new PluginHost.PluginHost(PluginHostTests.Options(), docs.HandleAsync, docs);
            var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.Plugins.Json.dll");
            Assert.True(File.Exists(dll), dll);
            var args = new List<string>
            {
                dll, "--max-bytes", "65536", "--indent", "2", "--max-depth", "64", "--schema-max-bytes", "65536", "--schema-depth", "64", "--schema-regex-ms", "200",
                "--registry-config", config, "--registry-timeout-ms", "5000", "--registry-max-bytes", "100000", "--registry-page-size", "20", "--registry-max-lookups", "50",
                "--registry-manifest-max-bytes", "65536",
            };
            foreach (var adapter in RegistryAdapter.CreateAll()) args.AddRange(["--registry-url", adapter.Id + "=" + stub.Url]);
            host.Register(new PluginSpec("json", "JSON", T.DotnetHost, args, ["onCommand:json"]));
            await host.ActivateAsync("onCommand:json");
            var doc = Document.Open(path, PagedBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Utf8 });
            docs.Expose(doc, null);
            for (int i = 0; i < 500 && !docs.HasCompletionProviders; i++) await Task.Delay(20);
            return new Rig { Host = host, Docs = docs, Doc = doc, Folder = folder, ConfigPath = config, Registry = stub };
        }

        public static string ConfigJson(string[] allowed) => JsonSerializer.Serialize(new { allowed });

        public void Allow(params string[] allowed) => File.WriteAllText(ConfigPath, ConfigJson(allowed));

        public async Task<JsonElement> CheckAsync(bool refresh = false, string language = "en")
        {
            var json = $"{{\"command\":\"packages.check\",\"document\":\"{Doc.Id}\",\"revision\":{Doc.Revision},\"language\":\"{language}\",\"refresh\":{(refresh ? "true" : "false")}}}";
            return (await Host.CallAsync("json", "command/execute", Encoding.UTF8.GetBytes(json))).Clone();
        }

        public string Text() => Encoding.UTF8.GetString(Doc.GetSnapshot().ReadBytes(new ByteRange(0, Doc.ByteLength)));

        /// <summary>The byte offset just after the first occurrence of <paramref name="marker"/>.</summary>
        public long After(string marker) => Encoding.UTF8.GetByteCount(Text()[..(Text().IndexOf(marker, StringComparison.Ordinal) + marker.Length)]);

        public async Task<IReadOnlyList<CompletionItem>> OffersAfterAsync(string marker) => (await Docs.RequestCompletionAsync(Doc.Id, After(marker))).Items;

        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            await Docs.DisposeAsync();
            Doc.Dispose();
            Registry.Dispose();
            Directory.Delete(Folder, true);
        }
    }

    /// <summary>One registry: a small manifest, where the caret is for a name and for a version, the recorded answers and what they must lead to.</summary>
    public sealed record Case(string Id, string File, string Manifest, string NameCaret, string SearchPath, string SearchBody, string[] Names,
        string VersionCaret, string VersionsPath, string VersionsBody, string[] Versions, string Pinned, string Latest, params (string Path, string Body)[] OtherRoutes)
    {
        public override string ToString() => Id;

        public void Serve(Stub s)
        {
            s.Serve(SearchPath, SearchBody);
            s.Serve(VersionsPath, VersionsBody);
            foreach (var (path, body) in OtherRoutes) s.Serve(path, body);
        }
    }

    public static IEnumerable<object[]> Cases()
    {
        yield return [new Case("npm", "package.json",
            "{\n  \"dependencies\": {\n    \"expr\": \"^4.0.0\",\n    \"lodash\": \"4.17.0\"\n  }\n}\n", "\"expr",
            "/-/v1/search", """{"objects":[{"package":{"name":"express","version":"4.18.2","description":"Fast web framework"},"score":{"final":0.9}},{"package":{"name":"express-session","version":"1.17.3"}}],"total":2,"time":"2024-01-01T00:00:00.000Z"}""", ["express", "express-session"],
            "\"lodash\": \"4.1", "/lodash", """{"name":"lodash","dist-tags":{"latest":"4.17.21"},"versions":{"4.16.0":{"name":"lodash"},"4.17.0":{"name":"lodash"},"4.17.21":{"name":"lodash"},"5.0.0-beta.1":{"name":"lodash"}}}""", ["4.17.21", "4.17.0", "4.16.0"],
            "4.17.0", "4.17.21")];
        yield return [new Case("packagist", "composer.json",
            "{\n  \"require\": {\n    \"php\": \"8.1.0\",\n    \"mono\": \"*\",\n    \"psr/log\": \"1.1.0\"\n  }\n}\n", "\"mono",
            "/search.json", """{"results":[{"name":"monolog/monolog","description":"Sends your logs to files, sockets, inboxes, databases and various web services","url":"https://packagist.org/packages/monolog/monolog","repository":"https://github.com/Seldaek/monolog","downloads":1000,"favers":100}],"total":1}""", ["monolog/monolog"],
            "\"psr/log\": \"1.", "/p2/psr/log.json", """{"minified":"composer/2.0","packages":{"psr/log":[{"name":"psr/log","version":"3.0.0","version_normalized":"3.0.0.0"},{"version":"2.0.0","version_normalized":"2.0.0.0"},{"version":"1.1.0","version_normalized":"1.1.0.0"},{"version":"dev-master","version_normalized":"9999999-dev"}]}}""", ["1.1.0"],
            "1.1.0", "3.0.0")];
        yield return [new Case("pypi", "requirements.txt",
            "requests\nflask==1.0.0\n", "requests",
            "/pypi/requests/json", """{"info":{"name":"requests","version":"2.31.0","summary":"Python HTTP for Humans."},"releases":{"2.30.0":[{"filename":"requests-2.30.0.tar.gz","yanked":false}],"2.31.0":[{"filename":"requests-2.31.0.tar.gz","yanked":false}]}}""", ["requests"],
            "flask==1.", "/pypi/flask/json", """{"info":{"name":"Flask","version":"2.0.0"},"releases":{"0.9":[{"yanked":true}],"1.0.0":[{"yanked":false}],"1.1.0":[{"yanked":false}],"2.0.0":[{"yanked":false}],"2.1.0rc1":[{"yanked":false}]}}""", ["1.1.0", "1.0.0"],
            "1.0.0", "2.0.0")];
        yield return [new Case("nuget", "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <PackageReference Include=\"Newt\" />\n    <PackageReference Include=\"Serilog\" Version=\"2.0.0\" />\n  </ItemGroup>\n</Project>\n", "Include=\"Newt",
            "/autocomplete", """{"@context":{"@vocab":"http://schema.nuget.org/schema#"},"totalHits":2,"data":["Newtonsoft.Json","Newtonsoft.Json.Bson"]}""", ["Newtonsoft.Json", "Newtonsoft.Json.Bson"],
            "Version=\"2.", "/v3-flatcontainer/serilog/index.json", """{"versions":["2.0.0","2.1.0","2.10.0","3.0.0-beta.1","3.1.1"]}""", ["2.10.0", "2.1.0", "2.0.0"],
            "2.0.0", "3.1.1")];
        yield return [new Case("go", "go.mod",
            "module example.com/app\n\ngo 1.21\n\nrequire (\n\tgithub.com/spf13/cobra v1.0.0\n\tgithub.com/stretchr/testify v1.8.0\n\tgithub.com/gin-gonic/gin\n)\n", "github.com/gin-gonic/gin",
            "/github.com/gin-gonic/gin/@latest", """{"Version":"v1.9.1","Time":"2023-06-01T00:00:00Z"}""", ["github.com/gin-gonic/gin"],
            "cobra v1.", "/github.com/spf13/cobra/@v/list", "v1.0.0\nv1.1.0\nv1.8.0\n", ["v1.8.0", "v1.1.0", "v1.0.0"],
            "v1.0.0", "v1.8.0", ("/github.com/stretchr/testify/@v/list", "v1.7.0\nv1.8.0\n"))];
        yield return [new Case("crates", "Cargo.toml",
            "[package]\nname = \"demo\"\nversion = \"0.1.0\"\n\n[dependencies]\nser\nrand = \"0.7.0\"\n", "\nser",
            "/api/v1/crates", """{"crates":[{"id":"serde","name":"serde","max_version":"1.0.197","newest_version":"1.0.197","description":"A generic serialization framework"},{"id":"serde_json","name":"serde_json","max_version":"1.0.114"}],"meta":{"total":2}}""", ["serde", "serde_json"],
            "rand = \"0.7", "/api/v1/crates/rand", """{"crate":{"id":"rand","max_version":"0.8.5"},"versions":[{"num":"0.8.5","yanked":false},{"num":"0.8.4","yanked":false},{"num":"0.7.3","yanked":false},{"num":"0.7.0","yanked":false},{"num":"0.9.0-alpha.1","yanked":false},{"num":"0.8.6","yanked":true}]}""", ["0.7.3", "0.7.0"],
            "0.7.0", "0.8.5")];
        yield return [new Case("rubygems", "Gemfile",
            "source 'https://rubygems.org'\ngem 'rai'\ngem 'nokogiri', '1.10.0'\n", "gem 'rai",
            "/api/v1/search.json", """[{"name":"rails","version":"7.1.3","downloads":500000000,"info":"Ruby on Rails is a full-stack web framework."},{"name":"railties","version":"7.1.3","downloads":400000000}]""", ["rails", "railties"],
            "'1.10", "/api/v1/versions/nokogiri.json", """[{"number":"1.16.2","prerelease":false,"platform":"ruby"},{"number":"1.16.0.rc1","prerelease":true,"platform":"ruby"},{"number":"1.10.10","prerelease":false,"platform":"ruby"},{"number":"1.10.0","prerelease":false,"platform":"ruby"}]""", ["1.10.10", "1.10.0"],
            "1.10.0", "1.16.2")];
        yield return [new Case("pub", "pubspec.yaml",
            "name: demo\ndependencies:\n  flutter:\n    sdk: flutter\n  htt\n  collection: 1.17.0\n", "\n  htt",
            "/api/search", """{"packages":[{"package":"http"},{"package":"http_parser"}],"next":"https://pub.dev/api/search?q=htt&page=2"}""", ["http", "http_parser"],
            "collection: 1.1", "/api/packages/collection", """{"name":"collection","latest":{"version":"1.18.0","pubspec":{"name":"collection","version":"1.18.0"}},"versions":[{"version":"1.15.0"},{"version":"1.16.0"},{"version":"1.17.0"},{"version":"1.18.0"},{"version":"1.19.0","retracted":true}]}""", ["1.18.0", "1.17.0", "1.16.0", "1.15.0"],
            "1.17.0", "1.18.0")];
        yield return [new Case("maven", "build.gradle",
            "dependencies {\n    implementation 'org.spr'\n    implementation 'org.apache.commons:commons-lang3:3.1'\n}\n", "'org.spr",
            "/solrsearch/select", """{"responseHeader":{"status":0,"QTime":2,"params":{"q":"org.spr","rows":"20","wt":"json"}},"response":{"numFound":2,"start":0,"docs":[{"id":"org.springframework:spring-core","g":"org.springframework","a":"spring-core","latestVersion":"6.1.4","repositoryId":"central","p":"jar","timestamp":1708000000000,"versionCount":300,"text":["org.springframework","spring-core","-sources.jar"],"ec":["-sources.jar",".jar",".pom"]},{"id":"org.springframework:spring-web","g":"org.springframework","a":"spring-web","latestVersion":"6.1.4","repositoryId":"central","p":"jar","versionCount":200}]}}""", ["org.springframework:spring-core", "org.springframework:spring-web"],
            "commons-lang3:3.1", "/org/apache/commons/commons-lang3/maven-metadata.xml", """<?xml version="1.0" encoding="UTF-8"?><metadata><groupId>org.apache.commons</groupId><artifactId>commons-lang3</artifactId><versioning><latest>3.14.0</latest><release>3.14.0</release><versions><version>3.0</version><version>3.1</version><version>3.11</version><version>3.12.0</version><version>3.13.0</version><version>3.14.0</version></versions><lastUpdated>20231215000000</lastUpdated></versioning></metadata>""", ["3.14.0", "3.13.0", "3.12.0", "3.11", "3.1"],
            "3.1", "3.14.0")];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Names_and_versions_come_from_the_registry_a_pinned_old_version_is_marked_and_a_repeated_answer_is_cached_and_can_be_refreshed(Case c)
    {
        await using var rig = await Rig.CreateAsync(c.File, c.Manifest, [c.Id], c.Serve);

        var names = await rig.OffersAfterAsync(c.NameCaret);
        Assert.Equal(c.Names, names.Select(i => i.Label));
        Assert.All(names, i => Assert.Equal(c.Id, i.Detail));
        Assert.Equal(1, rig.Registry.Count(c.SearchPath));

        var versions = await rig.OffersAfterAsync(c.VersionCaret);
        Assert.Equal(c.Versions, versions.Select(i => i.Label));
        var chosen = versions[0];
        rig.Docs.ApplyCompletion(chosen); // the choice replaces the version number only
        Assert.Contains(chosen.Label, rig.Text());
        Assert.Equal(c.Manifest.Length - c.Pinned.Length + chosen.Label.Length, rig.Text().Length);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_pinned_version_with_a_newer_release_gets_an_information_mark_and_the_second_answer_comes_from_the_cache(Case c)
    {
        await using var rig = await Rig.CreateAsync(c.File, c.Manifest, [c.Id], c.Serve);

        var first = await rig.CheckAsync();
        Assert.Equal(("ok", c.Id, 1, false), (first.GetProperty("status").GetString(), first.GetProperty("registry").GetString(), first.GetProperty("issues").GetInt32(), first.GetProperty("cached").GetBoolean()));
        var mark = Assert.Single(rig.Docs.DiagnosticsOf(rig.Doc.Id));
        Assert.Equal(("json", "packages", DiagnosticSeverity.Information, "newer-version"), (mark.PluginId, mark.Source, mark.Severity, mark.Code));
        Assert.Equal(c.Pinned, Encoding.UTF8.GetString(rig.Doc.GetSnapshot().ReadBytes(mark.Range)));
        Assert.Contains(c.Latest, mark.Message);
        Assert.DoesNotContain("cache", mark.Message);
        int asked = rig.Registry.Requests.Count;

        var second = await rig.CheckAsync();
        Assert.True(second.GetProperty("cached").GetBoolean());
        Assert.Contains("cache", Assert.Single(rig.Docs.DiagnosticsOf(rig.Doc.Id)).Message);
        Assert.Equal(asked, rig.Registry.Requests.Count); // nothing was asked again

        var refreshed = await rig.CheckAsync(refresh: true);
        Assert.False(refreshed.GetProperty("cached").GetBoolean());
        Assert.DoesNotContain("cache", Assert.Single(rig.Docs.DiagnosticsOf(rig.Doc.Id)).Message);
        Assert.Equal(asked * 2, rig.Registry.Requests.Count);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_list_of_offers_that_came_from_the_cache_says_so(Case c)
    {
        await using var rig = await Rig.CreateAsync(c.File, c.Manifest, [c.Id], c.Serve);

        var live = await rig.OffersAfterAsync(c.VersionCaret);
        Assert.All(live, i => Assert.DoesNotContain("cache", i.Detail));
        var cached = await rig.OffersAfterAsync(c.VersionCaret);
        Assert.Equal(live.Select(i => i.Label), cached.Select(i => i.Label));
        Assert.All(cached, i => Assert.EndsWith("cache", i.Detail));
        Assert.Single(rig.Registry.Requests);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_registry_that_fails_gives_no_offers_and_no_marks_and_the_check_names_the_error(Case c)
    {
        await using var rig = await Rig.CreateAsync(c.File, c.Manifest, [c.Id], s =>
        {
            c.Serve(s);
            s.Serve(c.SearchPath, "oops", 500);
            s.Serve(c.VersionsPath, "oops", 500);
            foreach (var (path, _) in c.OtherRoutes) s.Serve(path, "oops", 500);
        });

        Assert.Empty(await rig.OffersAfterAsync(c.NameCaret));
        Assert.Empty(await rig.OffersAfterAsync(c.VersionCaret));
        var r = await rig.CheckAsync();
        Assert.Equal("error", r.GetProperty("status").GetString());
        Assert.Contains("500", r.GetProperty("message").GetString());
        Assert.Empty(rig.Docs.DiagnosticsOf(rig.Doc.Id));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Without_consent_no_request_is_made_and_a_revoke_ends_the_answers_of_the_cache(Case c)
    {
        await using var rig = await Rig.CreateAsync(c.File, c.Manifest, [], c.Serve);

        Assert.Empty(await rig.OffersAfterAsync(c.NameCaret));
        Assert.Empty(await rig.OffersAfterAsync(c.VersionCaret));
        Assert.Equal("noConsent", (await rig.CheckAsync()).GetProperty("status").GetString());
        Assert.Empty(rig.Docs.DiagnosticsOf(rig.Doc.Id));
        Assert.Empty(rig.Registry.Requests);

        rig.Allow("someOtherRegistry"); // another registry does not allow this one
        Assert.Empty(await rig.OffersAfterAsync(c.VersionCaret));
        Assert.Empty(rig.Registry.Requests);

        rig.Allow(c.Id);
        Assert.NotEmpty(await rig.OffersAfterAsync(c.VersionCaret));
        int asked = rig.Registry.Requests.Count;
        rig.Allow();
        Assert.Empty(await rig.OffersAfterAsync(c.VersionCaret)); // the cache does not outlive the consent
        Assert.Equal(asked, rig.Registry.Requests.Count);
    }

    [Fact]
    public void Versions_are_ordered_by_number_and_a_pre_release_is_older_than_its_release()
    {
        Assert.True(Versions.Compare("4.17.0", "4.17.21") < 0);
        Assert.True(Versions.Compare("1.10.0", "1.9.0") > 0);
        Assert.True(Versions.Compare("v1.2.3", "1.2.3") == 0);
        Assert.True(Versions.Compare("1.0.0-rc.1", "1.0.0") < 0);
        Assert.True(Versions.Compare("1.0.0-rc.2", "1.0.0-rc.10") < 0);
        Assert.True(Versions.Compare("2.0.0+incompatible", "2.0.0") == 0);
        Assert.Equal("4.17.21", Versions.Latest(["4.17.0", "5.0.0-beta.1", "4.17.21"]));
        Assert.Equal("5.0.0-beta.1", Versions.Latest(["5.0.0-alpha", "5.0.0-beta.1"]));
        Assert.Null(Versions.Latest([]));
    }

    [Fact]
    public void Only_an_exact_version_counts_as_pinned_and_a_range_does_not()
    {
        var npm = Manifests.ReadNpm("{ \"dependencies\": { \"a\": \"1.0.0\", \"b\": \"^1.0.0\", \"c\": \"~1.0.0\", \"d\": \"1.x\", \"e\": \">=1.0.0 <2\", \"f\": \"file:../f\", \"g\": \"=1.2.3\" }, \"scripts\": { \"h\": \"1.0.0\" } }");
        Assert.Equal(["a", "g"], npm.Where(d => d.Pinned).Select(d => d.Name));
        Assert.DoesNotContain(npm, d => d.Name == "h");
        var pip = Manifests.ReadRequirements("a==1.0\nb>=1.0\nc~=1.0\n# d==1\n-r other.txt\ne==1.*\nf[extra]==2.0 ; python_version > '3'\n");
        Assert.Equal(["a", "f"], pip.Where(d => d.Pinned).Select(d => d.Name));
        var gems = Manifests.ReadGems("gem 'a', '1.0.0'\ngem 'b', '~> 1.0'\ngem \"c\", \">= 1.0\", \"< 2\"\ngem 'd', require: false\n");
        Assert.Equal(["a"], gems.Where(d => d.Pinned).Select(d => d.Name));
        Assert.Equal(4, gems.Count);
    }

    [Fact]
    public void A_name_still_being_typed_is_found_in_a_file_that_does_not_parse()
    {
        var text = "{\n  \"require\": {\n    \"mono\n    \"psr/log\": \"1.1.0\"\n  }\n}";
        var found = Manifests.ReadComposer(text);
        Assert.Equal(["mono", "psr/log"], found.Select(d => d.Name));
        Assert.Equal("mono", text[found[0].NameStart..found[0].NameEnd]);
        Assert.Equal("1.1.0", text[found[1].VersionStart..found[1].VersionEnd]);
    }

    // ---- Maven Central: pom.xml and Gradle ----

    private const string Pom = """
        <?xml version="1.0"?>
        <project>
          <dependencyManagement>
            <dependencies>
              <dependency>
                <groupId>org.apache.commons</groupId>
                <artifactId>commons-lang3</artifactId>
                <version>3.1</version>
                <exclusions>
                  <exclusion><groupId>x.y</groupId><artifactId>skip-me</artifactId></exclusion>
                </exclusions>
              </dependency>
            </dependencies>
          </dependencyManagement>
          <dependencies>
            <!-- <dependency><groupId>a.b</groupId><artifactId>commented</artifactId><version>1.0</version></dependency> -->
            <dependency>
              <groupId>${project.groupId}</groupId>
              <artifactId>sibling</artifactId>
              <version>${project.version}</version>
            </dependency>
            <dependency>
              <groupId>junit</groupId>
              <artifactId>junit</artifactId>
              <version>[4.0,5.0)</version>
            </dependency>
            <dependency>
              <groupId>org.slf4j</groupId>
              <artifactId>slf4j-api</artifactId>
            </dependency>
          </dependencies>
        </project>
        """;

    [Fact]
    public void A_pom_gives_group_and_artifact_as_the_name_the_version_text_and_only_an_exact_version_is_pinned()
    {
        var found = Manifests.ReadMaven(Pom);
        Assert.Equal(["org.apache.commons:commons-lang3", "${project.groupId}:sibling", "junit:junit", "org.slf4j:slf4j-api"], found.Select(d => d.Name)); // not the exclusion, not the comment
        Assert.Equal("3.1", Pom[found[0].VersionStart..found[0].VersionEnd]);
        Assert.Equal(["org.apache.commons:commons-lang3"], found.Where(d => d.Pinned).Select(d => d.Name));
        Assert.Equal("[4.0,5.0)", Pom[found[2].VersionStart..found[2].VersionEnd]);
        Assert.Equal(-1, found[3].VersionStart);
        Assert.All(found, d => Assert.Equal((-1, -1), (d.NameStart, d.NameEnd))); // two elements: nothing in one piece to complete
        var adapter = new MavenAdapter();
        Assert.Equal(["org.apache.commons:commons-lang3", "junit:junit", "org.slf4j:slf4j-api"], found.Where(d => adapter.IsPackage(d.Name)).Select(d => d.Name));
    }

    [Fact]
    public void A_gradle_file_gives_the_coordinate_after_a_configuration_in_both_notations_and_skips_comments_and_other_forms()
    {
        const string text = """
            plugins { id 'java' }
            dependencies {
                implementation("com.google.guava:guava:31.0")
                testImplementation 'junit:junit:4.+'
                api "org.slf4j:slf4j-api"
                // implementation 'old:gone:1.0'
                /* runtimeOnly 'also:gone:1.0' */
                implementation platform('org.springframework.boot:spring-boot-dependencies:3.2.0')
                implementation project(':lib')
                implementation group: 'a', name: 'b', version: '1'
                implementation 'x.y:z:'
            }
            """;
        var found = Manifests.ReadMaven(text);
        Assert.Equal(["com.google.guava:guava", "junit:junit", "org.slf4j:slf4j-api", "x.y:z"], found.Select(d => d.Name));
        Assert.Equal("com.google.guava:guava", text[found[0].NameStart..found[0].NameEnd]);
        Assert.Equal("31.0", text[found[0].VersionStart..found[0].VersionEnd]);
        Assert.Equal(["com.google.guava:guava"], found.Where(d => d.Pinned).Select(d => d.Name)); // 4.+ is not one version
        Assert.Equal(-1, found[2].VersionStart);
        Assert.True(found[3].VersionStart >= 0 && found[3].VersionStart == found[3].VersionEnd); // a version still being typed is an empty span
    }

    [Fact]
    public async Task A_pom_offers_the_versions_of_an_artifact_and_marks_the_newer_one_but_offers_no_names_and_looks_up_no_variable()
    {
        const string versions = """<metadata><groupId>org.apache.commons</groupId><artifactId>commons-lang3</artifactId><versioning><versions><version>3.1</version><version>3.14.0</version><version>3.15.0-M1</version></versions></versioning></metadata>""";
        await using var rig = await Rig.CreateAsync("pom.xml", Pom, ["maven"], s => s.Serve("/org/apache/commons/commons-lang3/maven-metadata.xml", versions));

        Assert.Equal(["3.15.0-M1", "3.14.0", "3.1"], (await rig.OffersAfterAsync("<version>3.1")).Select(i => i.Label)); // a pre-release is offered, marked as one
        Assert.Empty(await rig.OffersAfterAsync("<artifactId>comm"));
        Assert.Equal(0, rig.Registry.Count("/solrsearch"));

        var check = await rig.CheckAsync();
        Assert.Equal(("ok", "maven", 1), (check.GetProperty("status").GetString(), check.GetProperty("registry").GetString(), check.GetProperty("issues").GetInt32()));
        var mark = Assert.Single(rig.Docs.DiagnosticsOf(rig.Doc.Id));
        Assert.Equal("3.1", Encoding.UTF8.GetString(rig.Doc.GetSnapshot().ReadBytes(mark.Range)));
        Assert.Contains("3.14.0", mark.Message);
        Assert.All(rig.Registry.Requests, r => Assert.StartsWith("/org/apache/commons/commons-lang3/", r)); // a range, a variable and a missing version are not asked about
    }

    [Fact]
    public void The_answer_of_the_maven_registry_that_is_not_what_its_api_documents_is_a_registry_error()
    {
        var adapter = new MavenAdapter();
        Assert.Throws<RegistryException>(() => adapter.Names("""{"unexpected":true}""", "x"));
        Assert.Throws<RegistryException>(() => adapter.Names("<html/>", "x"));
        Assert.Throws<RegistryException>(() => adapter.VersionList("<metadata><versioning/></metadata>", "g:a"));
        Assert.Throws<RegistryException>(() => adapter.VersionList("not xml", "g:a"));
        Assert.Throws<RegistryException>(() => adapter.VersionList("<!DOCTYPE metadata [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><metadata><versioning><versions><version>&x;</version></versions></versioning></metadata>", "g:a")); // no entities, no files
        Assert.Equal(["1.0", "1.1"], adapter.VersionList("<metadata><versioning><versions><version>1.0</version><version> </version><version>1.1</version></versions></versioning></metadata>", "g:a"));
        Assert.Equal(["1.0", "2.0"], adapter.VersionList("<metadata><versioning><versions><version>1.0</version><version>2.0</version></versions></versioning></metadata>", "g:a")); // elements with no white space between them
    }

    [Theory]
    [InlineData("spring", "q=spring")]
    [InlineData("org.springframework:", "q=g%3A%22org.springframework%22")]
    [InlineData("org.springframework:spring-c", "q=g%3A%22org.springframework%22%20AND%20a%3Aspring-c%2A")]
    public void A_typed_name_becomes_a_search_of_the_group_and_the_artifact_and_text_that_is_no_name_is_not_searched(string prefix, string expectedQuery)
    {
        var adapter = new MavenAdapter();
        Assert.Contains(expectedQuery, adapter.NamesUrl(prefix, 20)!.OriginalString);
        Assert.Null(adapter.NamesUrl("x\" OR g:*", 20));
        Assert.Null(adapter.NamesUrl("a b", 20));
        Assert.Equal("https://repo1.maven.org/maven2/org/apache/commons/commons-lang3/maven-metadata.xml", adapter.VersionsUrl("org.apache.commons:commons-lang3").OriginalString);
    }
}
