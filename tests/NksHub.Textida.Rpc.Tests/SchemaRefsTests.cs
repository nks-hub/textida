using System.Text.Json;
using NksHub.Textida.Core.Data;
using NksHub.Textida.Plugins.Json;
using NksHub.Textida.Plugins.Json.Packages;
using NksHub.Textida.Plugins.Json.Schema;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>
/// P8.7.1: <c>$ref</c> to other documents. A file is followed from the folder of the schema, a cycle ends, the chain and the number of documents are limited, an address is followed only through the
/// consent of its host, and a document from the network never reads a file. The server is a listener of the test on the loopback address.
/// </summary>
public sealed class SchemaRefsTests : IDisposable
{
    private static readonly SchemaOptions Options = new(64, TimeSpan.FromSeconds(1));

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "textida-refs-" + Guid.NewGuid().ToString("N"));
    private readonly PackageRegistryTests.Stub _stub = new();
    private readonly List<RegistryClient> _clients = [];

    public SchemaRefsTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        foreach (var client in _clients) client.Dispose();
        _stub.Dispose();
        Directory.Delete(_folder, true);
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private string Consent => Path.Combine(_folder, "consent.json");

    private SchemaLoader Loader(bool downloads = false)
    {
        var settings = new SchemaSettings(65_536, 64, 200);
        if (!downloads) return new SchemaLoader(null, null, settings);
        var client = new RegistryClient(new RegistrySettings(Consent, 5000, 100_000, 20, 50, 65_536));
        _clients.Add(client);
        return new SchemaLoader(client, new SchemaCacheSettings(Path.Combine(_folder, "cache"), 100_000, 50_000, TimeSpan.FromSeconds(3600), TimeSpan.FromSeconds(60)), settings);
    }

    private async Task<(SchemaLinks Links, DataNode Root, SchemaEntry Entry)> LinkAsync(string schema, int depth = 4, int documents = 8, SchemaLoader? loader = null, bool network = true)
    {
        var path = Path.Combine(_folder, schema);
        var entry = SchemaCatalog.ByAddress(path)!;
        Assert.True(SchemaCatalog.TryParse(File.ReadAllText(path), DataDialect.Json, 64, out var root, out _));
        var links = await SchemaRefs.LinkAsync(root!, entry, Path.Combine(_folder, "doc.json"), loader ?? Loader(), new SchemaRefLimits(depth, documents), Options, network);
        return (links, root!, entry);
    }

    private static List<SchemaIssue> Validate(SchemaLinks links, DataNode root, string data)
    {
        Assert.True(SchemaCatalog.TryParse(data, DataDialect.Json, 64, out var node, out _));
        return new SchemaValidator(root, Options with { Links = links.Map }).Validate(node!);
    }

    [Fact]
    public async Task A_reference_to_another_file_is_followed_from_the_folder_of_the_schema_and_the_fragment_picks_a_part_of_it()
    {
        Write("schemas/main.json", """{ "type": "object", "properties": { "size": { "$ref": "defs.json#/definitions/size" }, "any": { "$ref": "free.json" } } }""");
        Write("schemas/defs.json", """{ "definitions": { "size": { "type": "integer", "minimum": 1 } } }""");
        Write("schemas/free.json", """{ "type": "string" }""");
        Write("defs.json", """{ "definitions": { "size": { "type": "string" } } }"""); // next to the document, not to the schema: it must not be used

        var (links, root, _) = await LinkAsync("schemas/main.json");
        Assert.Equal((2, 0), (links.Documents, links.Unresolved));
        Assert.Empty(Validate(links, root, """{ "size": 3, "any": "x" }"""));
        Assert.Equal("minimum", Assert.Single(Validate(links, root, """{ "size": 0 }""")).Code);
        var wrong = Assert.Single(Validate(links, root, """{ "size": "x" }"""));
        Assert.Equal(("type", "/size"), (wrong.Code, wrong.Pointer));
        Assert.Equal("/any", Assert.Single(Validate(links, root, """{ "any": 5 }""")).Pointer);
    }

    [Fact]
    public async Task Without_links_a_reference_to_another_file_asks_nothing_and_with_a_depth_of_zero_it_is_not_followed()
    {
        Write("main.json", """{ "properties": { "size": { "$ref": "defs.json" } } }""");
        Write("defs.json", """{ "type": "integer" }""");
        Assert.True(SchemaCatalog.TryParse(File.ReadAllText(Path.Combine(_folder, "main.json")), DataDialect.Json, 64, out var root, out _));
        Assert.True(SchemaCatalog.TryParse("""{ "size": "x" }""", DataDialect.Json, 64, out var data, out _));
        Assert.Empty(new SchemaValidator(root!, Options).Validate(data!));

        var (links, linked, _) = await LinkAsync("main.json", depth: 0);
        Assert.Equal((0, 1), (links.Documents, links.Unresolved));
        Assert.Empty(Validate(links, linked, """{ "size": "x" }"""));
    }

    [Fact]
    public async Task References_that_lead_in_a_circle_between_files_end_and_each_document_is_read_once()
    {
        Write("a.json", """{ "definitions": { "y": { "type": "object", "properties": { "back": { "$ref": "b.json#/definitions/x" } } } }, "type": "object", "properties": { "next": { "$ref": "b.json#/definitions/x" } } }""");
        Write("b.json", """{ "definitions": { "x": { "type": "object", "properties": { "next": { "$ref": "a.json#/definitions/y" }, "n": { "type": "integer" } } } } }""");

        var (links, root, _) = await LinkAsync("a.json");
        Assert.Equal((1, 0), (links.Documents, links.Unresolved)); // b.json is read; a.json is the schema itself
        var issue = Assert.Single(Validate(links, root, """{ "next": { "next": { "back": { "n": "bad" } } } }"""));
        Assert.Equal("/next/next/back/n", issue.Pointer);
    }

    [Fact]
    public async Task A_reference_that_leads_back_to_itself_ends_even_when_it_branches_and_still_offers_the_keys()
    {
        Write("s.json", """{ "type": "object", "properties": { "k": {} }, "$ref": "#/definitions/a", "definitions": { "a": { "allOf": [ { "$ref": "#/definitions/a" }, { "$ref": "#/definitions/a" }, { "$ref": "#" } ] } } }""");
        var (links, root, _) = await LinkAsync("s.json");

        var done = await Task.Run(() =>
        {
            var issues = Validate(links, root, "{}");
            Assert.True(SchemaCatalog.TryParse("{ }", DataDialect.Json, 64, out var data, out _));
            return (issues, keys: SchemaCatalog.CompleteKeys(root, data!, 2, Options with { Links = links.Map }));
        }).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Empty(done.issues);
        Assert.Equal(["k"], done.keys);
    }

    [Fact]
    public async Task The_length_of_the_chain_of_documents_and_their_number_are_limited()
    {
        Write("c1.json", """{ "properties": { "x": { "$ref": "c2.json" } } }""");
        Write("c2.json", """{ "properties": { "x": { "$ref": "c3.json" } } }""");
        Write("c3.json", """{ "properties": { "x": { "$ref": "c4.json" } } }""");
        Write("c4.json", """{ "type": "integer" }""");

        Assert.Equal((0, 1), Counts(await LinkAsync("c1.json", depth: 0)));
        Assert.Equal((2, 1), Counts(await LinkAsync("c1.json", depth: 2))); // c2 and c3 are read; the reference of c3 to c4 is one step too far
        Assert.Equal((3, 0), Counts(await LinkAsync("c1.json", depth: 3)));

        foreach (var name in new[] { "p1.json", "p2.json", "p3.json" }) Write(name, """{ "type": "integer" }""");
        Write("many.json", """{ "properties": { "a": { "$ref": "p1.json" }, "b": { "$ref": "p2.json" }, "c": { "$ref": "p3.json" } } }""");
        Assert.Equal((2, 1), Counts(await LinkAsync("many.json", documents: 2))); // three different documents are named; two are read, the third is cut off by the number
    }

    private static (int Documents, int Unresolved) Counts((SchemaLinks Links, DataNode Root, SchemaEntry Entry) linked) => (linked.Links.Documents, linked.Links.Unresolved);

    [Fact]
    public async Task A_reference_to_a_file_on_another_computer_to_another_scheme_or_inside_data_is_not_followed_and_never_throws()
    {
        Write("main.json", """
            { "properties": { "a": { "$ref": "//server/share/x.json" }, "b": { "$ref": "\\\\server\\share\\x.json" }, "c": { "$ref": "ftp://host/x.json" },
              "d": { "$ref": "file://host/share/x.json" }, "e": { "$ref": "missing.json#/nothing" } },
              "default": { "$ref": "in-data.json" }, "examples": [ { "$ref": "in-data.json" } ] }
            """);
        var (links, root, _) = await LinkAsync("main.json");
        Assert.Equal((0, 5), (links.Documents, links.Unresolved)); // the two inside default and examples are data and are not counted
        Assert.Null(links.NoConsentHost);
        Assert.Empty(Validate(links, root, """{ "a": 1, "e": 2 }"""));
    }

    [Fact]
    public async Task A_reference_to_an_address_is_downloaded_only_after_its_host_was_allowed_and_a_copy_serves_the_completion()
    {
        _stub.Serve("/defs.json", """{ "definitions": { "size": { "type": "integer" }, "other": { "$ref": "more.json" } } }""");
        _stub.Serve("/more.json", """{ "type": "string" }""");
        Write("main.json", $$"""{ "properties": { "size": { "$ref": "{{_stub.Url}}/defs.json#/definitions/size" }, "other": { "$ref": "{{_stub.Url}}/defs.json#/definitions/other" } } }""");
        var loader = Loader(downloads: true);

        var denied = await LinkAsync("main.json", loader: loader);
        Assert.Equal(("127.0.0.1", 2, 0), (denied.Links.NoConsentHost, denied.Links.Unresolved, denied.Links.Documents));
        Assert.Empty(_stub.Requests);
        Assert.Empty(Validate(denied.Links, denied.Root, """{ "size": "x" }"""));

        File.WriteAllText(Consent, """{"allowed":["schemas:127.0.0.1"]}""");
        var allowed = await LinkAsync("main.json", loader: loader);
        Assert.Equal((null, 0, 2), (allowed.Links.NoConsentHost, allowed.Links.Unresolved, allowed.Links.Documents)); // /defs.json and the /more.json it names relative to its address
        Assert.Equal(["/defs.json", "/more.json"], _stub.Requests.Order().ToArray());
        Assert.Equal("/size", Assert.Single(Validate(allowed.Links, allowed.Root, """{ "size": "x" }""")).Pointer);
        Assert.Equal("/other", Assert.Single(Validate(allowed.Links, allowed.Root, """{ "other": 5 }""")).Pointer);

        var offline = await LinkAsync("main.json", loader: loader, network: false); // completion and hover never wait for the network
        Assert.Equal(2, offline.Links.Documents);
        Assert.Equal(2, _stub.Requests.Count);
    }

    [Fact]
    public async Task A_document_from_the_network_never_reads_a_file_whatever_it_names()
    {
        var secret = Write("secret.json", """{ "type": "string" }""");
        _stub.Serve("/evil.json", $$"""{ "properties": { "f": { "$ref": "{{new Uri(secret).AbsoluteUri}}" }, "g": { "$ref": {{JsonSerializer.Serialize(secret)}} }, "h": { "$ref": "../secret.json" } } }""");
        Write("main.json", $$"""{ "$ref": "{{_stub.Url}}/evil.json" }""");
        File.WriteAllText(Consent, """{"allowed":["schemas:127.0.0.1"]}""");

        var (links, root, _) = await LinkAsync("main.json", loader: Loader(downloads: true));
        Assert.Equal(1, links.Documents);
        Assert.Equal(3, links.Unresolved);
        Assert.Empty(Validate(links, root, """{ "f": 1, "g": 1, "h": 1 }"""));
    }

    [Fact]
    public async Task A_reference_to_a_carried_schema_by_its_published_address_needs_no_request()
    {
        Write("main.json", """{ "properties": { "p": { "$ref": "https://json.schemastore.org/package.json#/properties/private" } } }""");
        var (links, root, _) = await LinkAsync("main.json", loader: Loader(downloads: true));
        Assert.Equal((1, 0), (links.Documents, links.Unresolved));
        Assert.Equal("/p", Assert.Single(Validate(links, root, """{ "p": "yes" }""")).Pointer);
        Assert.Empty(_stub.Requests);
    }

    [Fact]
    public void The_mapping_text_is_split_by_semicolons_and_line_breaks_with_an_optional_dialect_and_rejects_what_is_not_a_rule()
    {
        var rules = SchemaMappingText.Parse("""
            # my rules
            *.cfg = package.json ; config/** = https://example.test/a.json?v=1 as YAML
            app.conf=C:\my schemas as json\app.json
            broken
            =nothing
            x.y=
            """, out var rejected);
        Assert.Equal([("*.cfg", "package.json", null), ("config/**", "https://example.test/a.json?v=1", "yaml"), ("app.conf", @"C:\my schemas as json\app.json", null)],
            rules.Select(r => (r.Glob, r.Schema, r.Dialect)));
        Assert.Equal(["broken", "=nothing", "x.y="], rejected);
        Assert.Empty(SchemaMappingText.Parse(null, out var none));
        Assert.Empty(none);
    }

    [Fact]
    public void The_file_of_the_mappings_is_read_again_when_it_changes_and_a_missing_file_means_no_rules()
    {
        var catalog = new SchemaCatalog([]);
        var file = new SchemaMappingFile(Path.Combine(_folder, "map.txt"), 65_536);
        file.Refresh(catalog);
        Assert.Null(catalog.Resolve("/x/a.cfg").Schema);

        File.WriteAllText(Path.Combine(_folder, "map.txt"), "*.cfg=package.json as jsonc");
        file.Refresh(catalog);
        var (schema, dialect) = catalog.Resolve("/x/a.cfg");
        Assert.Equal(("package.json", DataDialect.Jsonc), (schema?.Id, dialect));

        File.WriteAllText(Path.Combine(_folder, "map.txt"), "*.cfg=cargo");
        file.Refresh(catalog);
        Assert.Equal("cargo", catalog.Resolve("/x/a.cfg").Schema?.Id);

        File.Delete(Path.Combine(_folder, "map.txt"));
        file.Refresh(catalog);
        Assert.Null(catalog.Resolve("/x/a.cfg").Schema);
    }

    [Fact]
    public void A_rule_of_the_user_wins_over_the_masks_of_the_catalog_and_a_schema_the_file_names_wins_over_the_rule()
    {
        var catalog = new SchemaCatalog(SchemaMappingFile.Convert(SchemaMappingText.Parse("package.json=cargo; configs/**=tsconfig as jsonc", out _)));
        Assert.Equal("cargo", catalog.Resolve("/p/package.json").Schema?.Id);
        var (tsconfig, dialect) = catalog.Resolve("/p/configs/deep/any.txt");
        Assert.Equal(("tsconfig", DataDialect.Jsonc), (tsconfig?.Id, dialect));
        Assert.Equal("composer", catalog.Resolve("/p/package.json", "https://json.schemastore.org/composer.json").Schema?.Id);
    }
}
