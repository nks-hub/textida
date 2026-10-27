using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Plugins.Json.Schema;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P8.7.1: the schema engine behind the diagnostics and completion API, with the real JSON plugin process.</summary>
public class SchemaPluginTests
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

    sealed class Rig : IAsyncDisposable
    {
        public required PluginHost.PluginHost Host { get; init; }
        public required DocumentApi Docs { get; init; }
        public required Document Doc { get; init; }
        public required string Folder { get; init; }

        /// <param name="extraArgs">More command line arguments of the plugin, built from the folder of the test.</param>
        /// <param name="companions">More files (name relative to the folder, content) that are written next to the file.</param>
        public static async Task<Rig> CreateAsync(string fileName, string content, long schemaMaxBytes = 1 << 16, Func<string, string[]>? extraArgs = null, IReadOnlyDictionary<string, string>? companions = null)
        {
            var folder = Path.Combine(Path.GetTempPath(), "textida-schema-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));
            foreach (var (name, text) in companions ?? new Dictionary<string, string>())
            {
                var other = Path.Combine(folder, name);
                Directory.CreateDirectory(Path.GetDirectoryName(other)!);
                File.WriteAllText(other, text, new UTF8Encoding(false));
            }
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
                MaxDiagnosticsPerDocument = 100, MaxCompletionItems = 100, MaxCompletionEdits = 4, CompletionTimeout = TimeSpan.FromSeconds(5), MaxHoverChars = 2000,
            }, new Owner(), send, (plugin, method, payload, timeout, ct) => host!.CallAsync(plugin, method, payload, timeout, ct));
            host = new PluginHost.PluginHost(PluginHostTests.Options(), docs.HandleAsync, docs);
            var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.Plugins.Json.dll");
            Assert.True(File.Exists(dll), dll);
            var args = new List<string> { dll, "--max-bytes", "65536", "--indent", "2", "--schema-max-bytes", schemaMaxBytes.ToString(), "--schema-depth", "64", "--schema-regex-ms", "200" };
            args.AddRange(extraArgs?.Invoke(folder) ?? []);
            host.Register(new PluginSpec("json", "JSON", T.DotnetHost, args, ["onCommand:json"]));
            await host.ActivateAsync("onCommand:json");
            var doc = Document.Open(path, PagedBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Utf8 });
            docs.Expose(doc, null);
            for (int i = 0; i < 500 && !docs.HasCompletionProviders; i++) await Task.Delay(20);
            for (int i = 0; i < 500 && !docs.HasHoverProviders; i++) await Task.Delay(20);
            return new Rig { Host = host, Docs = docs, Doc = doc, Folder = folder };
        }

        public async Task<JsonElement> CheckAsync(string language = "en")
        {
            var json = $"{{\"command\":\"schema.check\",\"document\":\"{Doc.Id}\",\"revision\":{Doc.Revision},\"language\":\"{language}\"}}";
            return (await Host.CallAsync("json", "command/execute", Encoding.UTF8.GetBytes(json))).Clone();
        }

        public List<PlacedDiagnostic> Diagnostics() => [.. Docs.DiagnosticsOf(Doc.Id).OrderBy(d => d.Range.Start)];

        public string TextOf(ByteRange range) => Encoding.UTF8.GetString(Doc.GetSnapshot().ReadBytes(range));

        public string Text() => Encoding.UTF8.GetString(Doc.GetSnapshot().ReadBytes(new ByteRange(0, Doc.ByteLength)));

        /// <summary>The byte offset just after the first occurrence of <paramref name="marker"/>.</summary>
        public long After(string marker) => Encoding.UTF8.GetByteCount(Text()[..(Text().IndexOf(marker, StringComparison.Ordinal) + marker.Length)]);

        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            await Docs.DisposeAsync();
            Doc.Dispose();
            Directory.Delete(Folder, true);
        }
    }

    [Fact]
    public async Task An_error_in_package_json_is_published_as_a_diagnostic_on_the_bytes_of_the_value_and_goes_when_it_is_fixed()
    {
        await using var rig = await Rig.CreateAsync("package.json", "{ \"name\": \"demo\", \"description\": \"é\", \"version\": 5 }");

        var r = await rig.CheckAsync();
        Assert.Equal(("ok", "package.json"), (r.GetProperty("status").GetString(), r.GetProperty("schema").GetString()));
        var diagnostic = Assert.Single(rig.Docs.DiagnosticsOf(rig.Doc.Id));
        Assert.Equal(("json", "schema", DiagnosticSeverity.Error, "type"), (diagnostic.PluginId, diagnostic.Source, diagnostic.Severity, diagnostic.Code));
        Assert.Contains("/version", diagnostic.Message);
        Assert.Equal("5", Encoding.UTF8.GetString(rig.Doc.GetSnapshot().ReadBytes(diagnostic.Range)));

        rig.Doc.Replace(diagnostic.Range, Encoding.UTF8.GetBytes("\"1.0.0\""));
        Assert.Empty(rig.Docs.DiagnosticsOf(rig.Doc.Id)); // the edit touched it
        await rig.CheckAsync();
        Assert.Empty(rig.Docs.DiagnosticsOf(rig.Doc.Id));
    }

    [Fact]
    public async Task A_file_without_a_schema_is_reported_as_such_and_a_syntax_error_is_underlined()
    {
        await using var rig = await Rig.CreateAsync("notes.json", "{ \"a\": [1, 2 }");

        var r = await rig.CheckAsync();
        Assert.Equal("ok", r.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, r.GetProperty("schema").ValueKind);
        Assert.Equal("syntax", Assert.Single(rig.Docs.DiagnosticsOf(rig.Doc.Id)).Code);
    }

    [Fact]
    public async Task A_file_over_the_limit_gets_no_diagnostics_and_no_completion()
    {
        await using var rig = await Rig.CreateAsync("package.json", "{ \"version\": 5 }" + new string(' ', 200), schemaMaxBytes: 100);

        Assert.Equal("tooLarge", (await rig.CheckAsync()).GetProperty("status").GetString());
        Assert.Empty(rig.Docs.DiagnosticsOf(rig.Doc.Id));
        Assert.Empty((await rig.Docs.RequestCompletionAsync(rig.Doc.Id, 3)).Items);
    }

    [Fact]
    public async Task Keys_come_from_the_schema_while_one_is_typed_and_the_choice_replaces_the_typed_part_as_one_undo_step()
    {
        await using var rig = await Rig.CreateAsync("package.json", "{ \"name\": \"é\", \"ve\" }");

        var answer = await rig.Docs.RequestCompletionAsync(rig.Doc.Id, rig.After("\"ve"));
        Assert.False(answer.Stale);
        Assert.Contains(answer.Items, i => i.Label == "version");
        Assert.All(answer.Items, i => Assert.StartsWith("ve", i.Label));
        Assert.DoesNotContain(answer.Items, i => i.Label == "name");
        var item = answer.Items.First(i => i.Label == "version");
        Assert.Equal("\"version\": ", item.Text);

        var depth = rig.Doc.UndoDepth;
        rig.Docs.ApplyCompletion(item);
        Assert.Equal("{ \"name\": \"é\", \"version\":  }", rig.Text());
        Assert.Equal(depth + 1, rig.Doc.UndoDepth);
    }

    [Fact]
    public async Task Keys_inside_a_nested_object_and_between_members_are_offered_and_a_value_gets_no_key()
    {
        await using var rig = await Rig.CreateAsync("package.json", "{ \"name\": \"x\", \"author\": { \"na\" }, \"description\": \"de\" }");

        var nested = await rig.Docs.RequestCompletionAsync(rig.Doc.Id, rig.After("\"author\": { \"na"));
        Assert.Contains(nested.Items, i => i.Label == "name");

        var inValue = await rig.Docs.RequestCompletionAsync(rig.Doc.Id, rig.After("\"de"));
        Assert.Empty(inValue.Items);
    }

    [Fact]
    public async Task Keys_of_a_cargo_manifest_are_offered_without_quotes()
    {
        await using var rig = await Rig.CreateAsync("Cargo.toml", "[package]\nname = \"demo\"\nedi\n");

        var answer = await rig.Docs.RequestCompletionAsync(rig.Doc.Id, rig.After("edi"));
        var item = Assert.Single(answer.Items, i => i.Label == "edition");
        Assert.Equal("edition = ", item.Text);
    }

    [Fact]
    public void The_offer_leaves_out_keys_that_are_written_and_needs_a_caret_where_a_key_can_stand()
    {
        var schema = SchemaCatalog.Bundled[0];
        var options = new SchemaOptions(64, TimeSpan.FromMilliseconds(200));
        var offer = KeyCompletion.Offer(schema, DataDialect.Json, "{ \"name\": \"a\", \"\" }", 16, options);
        Assert.NotNull(offer);
        Assert.DoesNotContain("name", offer!.Keys);
        Assert.Contains("version", offer.Keys);
        Assert.Null(KeyCompletion.Offer(schema, DataDialect.Json, "{ \"name\": \"a\", \"version\": 1 }", 12, options)); // inside the value of name
        Assert.Null(KeyCompletion.Offer(schema, DataDialect.Json, "not json at all", 3, options));
        var withValue = KeyCompletion.Offer(schema, DataDialect.Json, "{ \"name\": \"a\", \"ve\": 1 }", 18, options);
        Assert.Equal("\"version\"", withValue!.Insert("version")); // the key already has its value: no colon is added
        const string nestedText = "{ \"name\": \"x\", \"author\": { \"na\" }, \"description\": \"de\" }";
        Assert.Contains("name", KeyCompletion.Offer(schema, DataDialect.Json, nestedText, nestedText.IndexOf("\"na\"", StringComparison.Ordinal) + 3, options)!.Keys);
    }

    // ---- values, descriptions, texts and association (P8.7.1) ----

    [Fact]
    public async Task Hover_describes_the_key_and_the_value_under_the_pointer_in_the_language_asked_for_and_nothing_between_members()
    {
        await using var rig = await Rig.CreateAsync("package.json", "{ \"name\": \"demo\", \"version\": \"1.0.0\", \"type\": \"module\" }");

        var key = await rig.Docs.RequestHoverAsync(rig.Doc.Id, rig.After("\"vers"), "en");
        Assert.False(key.Stale);
        Assert.Equal("json", key.PluginId);
        Assert.Equal("Package version (semver).\n\nType: string", key.Text);
        Assert.Contains("version", rig.TextOf(key.Range!.Value));

        var value = await rig.Docs.RequestHoverAsync(rig.Doc.Id, rig.After("\"version\": \"1.0"), "en");
        Assert.Equal(key.Text, value.Text);
        Assert.Equal("\"1.0.0\"", rig.TextOf(value.Range!.Value));

        Assert.Equal("Package version (semver).\n\nTyp: text", (await rig.Docs.RequestHoverAsync(rig.Doc.Id, rig.After("\"vers"), "cs")).Text);
        Assert.Equal("Module system of .js files.\n\nWerte: commonjs, module", (await rig.Docs.RequestHoverAsync(rig.Doc.Id, rig.After("\"typ"), "de")).Text);
        Assert.Null((await rig.Docs.RequestHoverAsync(rig.Doc.Id, rig.After("\"demo\","), "en")).Text);
    }

    [Fact]
    public async Task Enum_values_and_booleans_are_offered_where_a_value_is_written_and_the_choice_replaces_the_typed_part()
    {
        await using var rig = await Rig.CreateAsync("package.json", "{ \"type\": \"mo\", \"private\": false }");

        var type = await rig.Docs.RequestCompletionAsync(rig.Doc.Id, rig.After("\"mo"));
        var module = Assert.Single(type.Items);
        Assert.Equal(("module", "\"module\"", "value", "package.json"), (module.Label, module.Text, module.Kind, module.Detail));
        rig.Docs.ApplyCompletion(module);
        Assert.Equal("{ \"type\": \"module\", \"private\": false }", rig.Text());

        var flag = await rig.Docs.RequestCompletionAsync(rig.Doc.Id, rig.After("\"private\": "));
        Assert.Equal(["true", "false"], flag.Items.Select(i => i.Label));
        Assert.All((await rig.Docs.RequestCompletionAsync(rig.Doc.Id, rig.After("\"priv"))).Items, i => Assert.Equal("property", i.Kind)); // inside a key only keys are offered
    }

    [Fact]
    public async Task The_messages_are_written_in_the_language_of_the_command()
    {
        await using var rig = await Rig.CreateAsync("package.json", "{ \"name\": 5,\n  \"private\": \"yes\" }");

        await rig.CheckAsync("cs");
        Assert.Equal(["/name: Očekáváno: text.", "/private: Očekáváno: ano/ne."], rig.Diagnostics().Select(d => d.Message));
        await rig.CheckAsync("sk");
        Assert.Equal(["/name: Očakáva sa: text.", "/private: Očakáva sa: áno/nie."], rig.Diagnostics().Select(d => d.Message));
        await rig.CheckAsync("de");
        Assert.Equal(["/name: Erwartet: Text.", "/private: Erwartet: Wahrheitswert."], rig.Diagnostics().Select(d => d.Message));
        await rig.CheckAsync("en");
        Assert.Equal(["/name: Expected string.", "/private: Expected boolean."], rig.Diagnostics().Select(d => d.Message));
    }

    [Fact]
    public async Task A_syntax_error_in_yaml_and_toml_is_underlined_without_a_schema_with_line_and_column_in_the_language_of_the_command()
    {
        await using (var yaml = await Rig.CreateAsync("settings.yaml", "a: 1\na: 2\n"))
        {
            var r = await yaml.CheckAsync();
            Assert.Equal(("ok", JsonValueKind.Null), (r.GetProperty("status").GetString(), r.GetProperty("schema").ValueKind));
            var d = Assert.Single(yaml.Diagnostics());
            Assert.Equal(("syntax", 5L, "Syntax error at line 2, column 1: The key 'a' is defined twice."), (d.Code, d.Range.Start, d.Message));
            await yaml.CheckAsync("cs");
            Assert.Equal("Chyba syntaxe na řádku 2, sloupci 1: The key 'a' is defined twice.", Assert.Single(yaml.Diagnostics()).Message);
        }
        await using (var toml = await Rig.CreateAsync("settings.toml", "a = 1\na = 2\n"))
        {
            await toml.CheckAsync("de");
            var d = Assert.Single(toml.Diagnostics());
            Assert.Equal(("syntax", 6L, "Syntaxfehler in Zeile 2, Spalte 1: The key 'a' is defined twice."), (d.Code, d.Range.Start, d.Message));
        }
    }

    [Fact]
    public async Task A_form_of_yaml_the_reader_does_not_know_is_not_checked_and_is_not_reported_as_an_error()
    {
        await using var rig = await Rig.CreateAsync(".github/workflows/ci.yml", "on: push\njobs:\n  a: &x 1\n  b: *x\n");

        var r = await rig.CheckAsync();
        Assert.Equal(("ok", "unsupported", "GitHub Actions workflow"), (r.GetProperty("status").GetString(), r.GetProperty("state").GetString(), r.GetProperty("name").GetString()));
        Assert.Empty(rig.Diagnostics());
    }

    [Fact]
    public async Task A_yaml_template_is_not_checked_because_it_is_no_yaml_until_it_is_rendered()
    {
        await using var rig = await Rig.CreateAsync("templates/deploy.yaml", "name: {{ .Release.Name }}\nreplicas: {{ .Values.n }}\n");

        var r = await rig.CheckAsync();

        Assert.Equal(("ok", "unsupported"), (r.GetProperty("status").GetString(), r.GetProperty("state").GetString()));
        Assert.Empty(rig.Diagnostics());
    }

    [Fact]
    public async Task A_file_that_names_a_schema_file_is_checked_against_it_without_consent_and_completes_and_describes_from_it()
    {
        const string schema = """{ "type": "object", "properties": { "color": { "enum": ["red", "green"], "description": "Paint." }, "size": { "type": "integer" } } }""";
        await using var rig = await Rig.CreateAsync("conf/thing.json", "{ \"$schema\": \"../shapes/thing.schema.json\", \"size\": \"big\", \"color\": \"re\" }",
            companions: new Dictionary<string, string> { ["shapes/thing.schema.json"] = schema });

        var r = await rig.CheckAsync();
        Assert.Equal(("ok", "ready", "thing.schema.json"), (r.GetProperty("status").GetString(), r.GetProperty("state").GetString(), r.GetProperty("name").GetString()));
        Assert.Equal(["\"big\"", "\"re\""], rig.Diagnostics().Select(d => rig.TextOf(d.Range)));

        Assert.Equal(["red"], (await rig.Docs.RequestCompletionAsync(rig.Doc.Id, rig.After("\"color\": \"re"))).Items.Select(i => i.Label));
        Assert.Equal("Paint.\n\nValues: red, green", (await rig.Docs.RequestHoverAsync(rig.Doc.Id, rig.After("\"col"), "en")).Text);
    }

    [Fact]
    public async Task A_schema_at_an_address_is_downloaded_only_after_the_host_was_allowed_and_a_revoke_ends_its_use()
    {
        using var stub = new PackageRegistryTests.Stub();
        stub.Serve("/schemas/thing.json", """{ "type": "object", "properties": { "n": { "type": "integer" } } }""");
        await using var rig = await Rig.CreateAsync("thing.json", $"{{ \"$schema\": \"{stub.Url}/schemas/thing.json\", \"n\": \"x\" }}", extraArgs: folder =>
        [
            "--registry-config", Path.Combine(folder, "consent.json"), "--registry-timeout-ms", "5000", "--registry-max-bytes", "100000", "--registry-page-size", "20",
            "--registry-max-lookups", "50", "--registry-manifest-max-bytes", "65536",
            "--schema-cache", Path.Combine(folder, "cache"), "--schema-cache-max-bytes", "100000", "--schema-download-max-bytes", "50000",
            "--schema-cache-max-age-seconds", "3600", "--schema-retry-seconds", "60",
        ]);
        string consent = Path.Combine(rig.Folder, "consent.json"), cache = Path.Combine(rig.Folder, "cache");

        var denied = await rig.CheckAsync(); // there is no consent file at all
        Assert.Equal(("noConsent", "127.0.0.1", "thing.json"), (denied.GetProperty("state").GetString(), denied.GetProperty("host").GetString(), denied.GetProperty("name").GetString()));
        Assert.Empty(rig.Diagnostics());
        Assert.Empty((await rig.Docs.RequestCompletionAsync(rig.Doc.Id, rig.After("\"n\": \"x"))).Items);
        Assert.Empty(stub.Requests);

        File.WriteAllText(consent, """{"allowed":["schemas:127.0.0.1"]}""");
        var allowed = await rig.CheckAsync();
        Assert.Equal(("ready", false), (allowed.GetProperty("state").GetString(), allowed.GetProperty("cached").GetBoolean()));
        Assert.Equal("\"x\"", rig.TextOf(Assert.Single(rig.Diagnostics()).Range));
        Assert.Single(stub.Requests);
        Assert.Single(Directory.GetFiles(cache, "127.0.0.1.*.json"));

        Assert.True((await rig.CheckAsync()).GetProperty("cached").GetBoolean());
        Assert.Single(stub.Requests); // the copy is recent: no second request

        File.WriteAllText(consent, """{"allowed":[]}""");
        var revoked = await rig.CheckAsync();
        Assert.Equal("noConsent", revoked.GetProperty("state").GetString());
        Assert.Empty(rig.Diagnostics());
        Assert.Empty(Directory.GetFiles(cache, "*.json")); // the copy of a host that is not allowed any more is deleted
        Assert.Single(stub.Requests);
    }
}
