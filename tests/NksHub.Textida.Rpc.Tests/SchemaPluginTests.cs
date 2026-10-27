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

        public static async Task<Rig> CreateAsync(string fileName, string content, long schemaMaxBytes = 1 << 16)
        {
            var folder = Path.Combine(Path.GetTempPath(), "textida-schema-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, fileName);
            File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));
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
                MaxDiagnosticsPerDocument = 100, MaxCompletionItems = 100, MaxCompletionEdits = 4, CompletionTimeout = TimeSpan.FromSeconds(5),
            }, new Owner(), send, (plugin, method, payload, timeout, ct) => host!.CallAsync(plugin, method, payload, timeout, ct));
            host = new PluginHost.PluginHost(PluginHostTests.Options(), docs.HandleAsync, docs);
            var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.Plugins.Json.dll");
            Assert.True(File.Exists(dll), dll);
            host.Register(new PluginSpec("json", "JSON", T.DotnetHost,
                [dll, "--max-bytes", "65536", "--indent", "2", "--schema-max-bytes", schemaMaxBytes.ToString(), "--schema-depth", "64", "--schema-regex-ms", "200"], ["onCommand:json"]));
            await host.ActivateAsync("onCommand:json");
            var doc = Document.Open(path, PagedBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Utf8 });
            docs.Expose(doc, null);
            for (int i = 0; i < 500 && !docs.HasCompletionProviders; i++) await Task.Delay(20);
            return new Rig { Host = host, Docs = docs, Doc = doc, Folder = folder };
        }

        public async Task<JsonElement> CheckAsync()
        {
            var json = $"{{\"command\":\"schema.check\",\"document\":\"{Doc.Id}\",\"revision\":{Doc.Revision},\"language\":\"en\"}}";
            return (await Host.CallAsync("json", "command/execute", Encoding.UTF8.GetBytes(json))).Clone();
        }

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
}
