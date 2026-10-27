using NksHub.Textida.Core.Data;
using NksHub.Textida.Plugins.Json;
using NksHub.Textida.Plugins.Json.Packages;
using NksHub.Textida.Plugins.Json.Schema;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P8.7.1: association by <c>$schema</c> and by the catalog, values and descriptions from a schema, the texts in four languages, the completion in an empty TOML table.</summary>
public class SchemaFeaturesTests
{
    static readonly SchemaOptions Opt = new(64, TimeSpan.FromSeconds(1));

    static DataNode Parse(string json)
    {
        Assert.True(JsonDataReader.TryParse(json, false, 64, out var node, out var error), error?.Message);
        return node!;
    }

    static DataNode Bundled(string id) => Parse(SchemaCatalog.LoadSchemaText(SchemaCatalog.Bundled.First(b => b.Id == id)));

    const string Custom = """
        { "type": "object", "properties": {
            "mode": { "enum": ["fast", "slow", "a b"], "description": "How it runs." },
            "flag": { "type": "boolean" },
            "list": { "type": "array", "items": { "enum": ["x", "y"] } } } }
        """;

    // ---- association ----

    [Fact]
    public void A_schema_a_file_names_itself_is_found_in_json_yaml_and_toml_even_when_the_rest_does_not_parse()
    {
        Assert.Equal("https://x.test/a.json", SchemaCatalog.DeclaredSchema(DataDialect.Json, "{ \"$schema\": \"https://x.test/a.json\", \"a\": }", 64));
        Assert.Equal("a.json", SchemaCatalog.DeclaredSchema(DataDialect.Jsonc, "{ // note\n /* c */ \"$schema\": \"a.json\" }", 64));
        Assert.Equal("b.json", SchemaCatalog.DeclaredSchema(DataDialect.Json, "{ \"a\": 1, \"$schema\": \"b.json\" }", 64));
        Assert.Null(SchemaCatalog.DeclaredSchema(DataDialect.Json, "{ \"a\": { \"$schema\": \"c.json\" } }", 64));
        Assert.Null(SchemaCatalog.DeclaredSchema(DataDialect.Json, "{ \"a\": 1 }", 64));
        Assert.Equal("https://x.test/w.json", SchemaCatalog.DeclaredSchema(DataDialect.Yaml, "# yaml-language-server: $schema=https://x.test/w.json\nname: x\n", 64));
        Assert.Equal("schema.json", SchemaCatalog.DeclaredSchema(DataDialect.Toml, "#:schema schema.json\n[a]\n", 64));
        Assert.Null(SchemaCatalog.DeclaredSchema(DataDialect.Markdown, "---\n$schema: x\n---\n", 64));
    }

    [Fact]
    public void A_schema_a_file_names_wins_over_the_file_name_and_the_published_address_of_a_carried_schema_gives_the_carried_copy()
    {
        var catalog = new SchemaCatalog([]);
        Assert.Equal("package.json", catalog.Resolve("/w/package.json").Schema!.Id);

        var (named, dialect) = catalog.Resolve("/w/package.json", "https://example.test/other.json");
        Assert.Equal(("url:https://example.test/other.json", "other.json", false, DataDialect.Json), (named!.Id, named.Name, named.IsBundled, dialect));

        Assert.True(catalog.Resolve("/w/x.json", "https://json.schemastore.org/package.json").Schema!.IsBundled);
        Assert.Equal("package.json", catalog.Resolve("/w/x.json", "http://json.schemastore.org/package").Schema!.Id); // either scheme, with or without .json
        Assert.Equal("tsconfig", catalog.Resolve("/w/x.json", "https://json.schemastore.org/tsconfig.json").Schema!.Id);
        Assert.Equal("thing.schema.json", catalog.Resolve("/w/x.json", "..\\shapes\\thing.schema.json").Schema!.Name);
        Assert.Null(catalog.Resolve("/w/x.json", "").Schema); // an empty address names nothing, and x.json has no mask
    }

    [Fact]
    public void The_catalog_subset_maps_masks_to_addresses_and_hides_no_carried_schema()
    {
        var catalog = new SchemaCatalog([]);
        foreach (var (path, id, dialect) in new[]
        {
            ("/w/tsconfig.json", "tsconfig", DataDialect.Jsonc), ("/w/tsconfig.build.json", "tsconfig", DataDialect.Jsonc), ("/w/composer.json", "composer", DataDialect.Json),
            ("/w/pubspec.yaml", "pubspec", DataDialect.Yaml), ("/w/pyproject.toml", "pyproject", DataDialect.Toml), ("/w/.github/dependabot.yml", "dependabot", DataDialect.Yaml),
            ("/w/appsettings.Development.json", "appsettings", DataDialect.Jsonc), ("C:\\w\\.gitlab-ci.yml", "gitlab-ci", DataDialect.Yaml),
        })
        {
            var (entry, found) = catalog.Resolve(path);
            Assert.Equal((id, dialect, false), (entry?.Id, found, entry?.IsBundled));
        }
        Assert.All(SchemaCatalog.Store, e =>
        {
            Assert.StartsWith("https://", e.Url);
            Assert.Equal("", e.Resource);
            Assert.NotEmpty(e.FileMatch);
        });
        Assert.Equal(SchemaCatalog.Store.Count, SchemaCatalog.Store.Select(e => e.Id).Distinct().Count());
        Assert.Empty(SchemaCatalog.Store.Select(e => e.Id).Intersect(SchemaCatalog.Bundled.Select(b => b.Id)));
        // package.json, Cargo.toml and workflows are carried, so the first mask that fits is the carried one
        Assert.True(catalog.Resolve("/w/package.json").Schema!.IsBundled);
        // a schema that has not been loaded checks the syntax only
        var r = catalog.Check("/w/tsconfig.json", "{ \"a\": 1 }", Opt);
        Assert.Equal(("tsconfig", 0), (r.Schema?.Id, r.Issues.Count));
        Assert.NotNull(catalog.Check("/w/tsconfig.json", "{ \"a\": }", Opt).Syntax);
    }

    [Fact]
    public void A_user_mapping_may_name_an_address_a_file_or_a_known_id()
    {
        var byAddress = new SchemaCatalog([new SchemaMapping("conf/*.json", "https://example.test/c.json")]).Resolve("/p/conf/a.json");
        Assert.Equal("c.json", byAddress.Schema!.Name);
        var byId = new SchemaCatalog([new SchemaMapping("conf/*.json", "tsconfig")]).Resolve("/p/conf/a.json");
        Assert.Equal(("tsconfig", DataDialect.Jsonc), (byId.Schema!.Id, byId.Dialect));
        Assert.Null(new SchemaCatalog([new SchemaMapping("conf/*.json", "nothing-by-this-name")]).Resolve("/p/conf/a.json").Schema);
    }

    [Fact]
    public void A_form_of_yaml_the_reader_does_not_implement_is_told_from_a_mistake_in_the_text()
    {
        var catalog = new SchemaCatalog([]);
        Assert.True(catalog.Check("c.yml", "a: &x 1\n", Opt).Syntax!.Unsupported);
        Assert.True(catalog.Check("c.yml", "a: !tag 1\n", Opt).Syntax!.Unsupported);
        Assert.True(catalog.Check("c.yml", "? a\n: 1\n", Opt).Syntax!.Unsupported);
        Assert.False(catalog.Check("c.yml", "a: 1\na: 2\n", Opt).Syntax!.Unsupported);
        Assert.False(catalog.Check("c.toml", "a = 1\na = 2\n", Opt).Syntax!.Unsupported);
    }

    // ---- values ----

    static ValueOffer? Values(DataNode schema, DataDialect dialect, string text, string marker) =>
        ValueCompletion.Offer(schema, dialect, text, text.IndexOf(marker, StringComparison.Ordinal) + marker.Length, Opt);

    [Fact]
    public void Enum_values_are_offered_in_json_for_a_string_being_typed_and_for_an_empty_value()
    {
        var schema = Bundled("package.json");

        const string typed = "{ \"type\": \"mo\" }";
        var offer = Values(schema, DataDialect.Json, typed, "\"mo")!;
        Assert.Equal([("module", "\"module\"")], offer.Items);
        Assert.Equal("\"mo\"", typed[offer.Start..offer.End]);

        const string empty = "{ \"type\": }";
        var all = Values(schema, DataDialect.Json, empty, ": ")!;
        Assert.Equal(["commonjs", "module"], all.Items.Select(i => i.Label));
        Assert.Equal((all.Start, all.End), (empty.IndexOf('}'), empty.IndexOf('}')));

        Assert.Equal(["true", "false"], Values(schema, DataDialect.Json, "{ \"private\": }", ": ")!.Items.Select(i => i.Label));
        Assert.Null(Values(schema, DataDialect.Json, "{ \"type\": \"module\" }", "\"module")); // the value is complete
    }

    [Fact]
    public void No_value_is_offered_at_a_key_in_a_comment_in_a_file_that_does_not_read_or_where_the_schema_has_none()
    {
        var schema = Bundled("package.json");
        Assert.Null(Values(schema, DataDialect.Json, "{ \"ty\": \"module\" }", "\"ty")); // a key
        Assert.Null(Values(schema, DataDialect.Json, "{ \"name\": \"x\", \"ty\" }", "\"ty")); // a key still being typed
        Assert.Null(Values(schema, DataDialect.Json, "{ \"type\": \"mo\", \"x\": }", "\"mo")); // the file does not read
        Assert.Null(Values(schema, DataDialect.Json, "{ \"name\": \"x\" }", "\"name\": \"x")); // a string without a list of values
        Assert.Null(Values(Parse(Custom), DataDialect.Yaml, "mode: fast # f\n", "# f")); // in a comment
        Assert.Null(Values(Parse(Custom), DataDialect.Yaml, "mo\n", "mo")); // a lone word is no value of a property
    }

    [Fact]
    public void Enum_values_are_offered_in_yaml_and_toml_in_the_form_the_format_wants()
    {
        var schema = Parse(Custom);

        Assert.Equal([("fast", "fast")], Values(schema, DataDialect.Yaml, "mode: f\n", "mode: f")!.Items);
        var yaml = Values(schema, DataDialect.Yaml, "mode: \nflag: true\n", "mode: ")!;
        Assert.Equal([("fast", "fast"), ("slow", "slow"), ("a b", "\"a b\"")], yaml.Items); // a value with a space is written in quotes
        Assert.Equal(["true", "false"], Values(schema, DataDialect.Yaml, "flag: \n", "flag: ")!.Items.Select(i => i.Label));
        Assert.Equal(["x", "y"], Values(schema, DataDialect.Yaml, "list:\n  - \n", "- ")!.Items.Select(i => i.Label));

        Assert.Equal([("fast", "\"fast\"")], Values(schema, DataDialect.Toml, "mode = f\n", "mode = f")!.Items);
        const string quoted = "mode = \"s\"\n";
        var toml = Values(schema, DataDialect.Toml, quoted, "\"s")!;
        Assert.Equal([("slow", "\"slow\"")], toml.Items);
        Assert.Equal("\"s\"", quoted[toml.Start..toml.End]);
        Assert.Null(Values(schema, DataDialect.Toml, "mo\n", "mo")); // a key
    }

    [Fact]
    public void Values_of_cargo_toml_come_from_the_schema_of_the_table_the_caret_is_in()
    {
        var offer = Values(Bundled("cargo"), DataDialect.Toml, "[package]\nname = \"demo\"\nedition = \n", "edition = ")!;
        Assert.Equal(["2015", "2018", "2021", "2024"], offer.Items.Select(i => i.Label));
        Assert.Equal("\"2021\"", offer.Items[2].Insert);
    }

    // ---- keys in an empty TOML table ----

    [Fact]
    public void Keys_are_offered_in_a_toml_table_that_has_nothing_in_it_yet()
    {
        var cargo = Bundled("cargo");

        const string table = "[package]\n\n";
        var offer = KeyCompletion.Offer(cargo, DataDialect.Toml, table, table.Length, Opt);
        Assert.Contains("name", offer!.Keys);
        Assert.Equal("edition = ", offer.Insert("edition"));

        const string nested = "[dependencies.serde]\n";
        Assert.Contains("features", KeyCompletion.Offer(cargo, DataDialect.Toml, nested, nested.Length, Opt)!.Keys);

        const string partial = "[package]\nna";
        Assert.Equal(["name"], KeyCompletion.Offer(cargo, DataDialect.Toml, partial, partial.Length, Opt)!.Keys);

        const string array = "[[bin]]\n";
        Assert.Null(KeyCompletion.Offer(cargo, DataDialect.Toml, array, array.Length, Opt)); // the schema names no keys of a binary
    }

    // ---- descriptions ----

    static HoverInfo? Hover(DataNode schema, DataDialect dialect, string text, string marker, string language = "en") =>
        SchemaHover.Describe(schema, dialect, text, text.IndexOf(marker, StringComparison.Ordinal) + marker.Length, Opt, SchemaTexts.For(language));

    [Fact]
    public void The_description_type_values_and_default_of_a_property_are_shown_for_its_key_and_its_value()
    {
        var schema = Parse("""
            { "type": "object", "properties": {
                "mode": { "title": "Mode", "description": "How it runs.", "enum": ["fast", "slow"], "default": "fast" },
                "old": { "type": ["string", "null"], "deprecated": true, "deprecationMessage": "Use mode." },
                "list": { "type": "array", "items": { "type": "integer", "description": "One number." } },
                "tree": { "type": "object", "patternProperties": { "^x": { "type": "boolean", "description": "Starts with x." } } } } }
            """);

        const string json = "{ \"mode\": \"fast\", \"old\": null, \"list\": [1, 2], \"tree\": { \"xa\": true } }";
        var key = Hover(schema, DataDialect.Json, json, "\"mo")!;
        Assert.Equal("Mode\nHow it runs.\n\nValues: fast, slow\nDefault: fast", key.Text);
        Assert.Equal("\"mode\"", json[key.Start..key.End]);
        var value = Hover(schema, DataDialect.Json, json, "\"mode\": \"fa")!;
        Assert.Equal((key.Text, "\"fast\""), (value.Text, json[value.Start..value.End]));
        Assert.Equal("Deprecated: Use mode.\n\nType: string, null", Hover(schema, DataDialect.Json, json, "\"ol")!.Text);
        Assert.Equal("One number.\n\nType: integer", Hover(schema, DataDialect.Json, json, "[1, ")!.Text);
        Assert.Equal("Starts with x.\n\nType: boolean", Hover(schema, DataDialect.Json, json, "\"xa")!.Text);

        Assert.Equal("Mode\nHow it runs.\n\nHodnoty: fast, slow\nVýchozí: fast", Hover(schema, DataDialect.Json, json, "\"mo", "cs")!.Text);
        Assert.Equal("Zastaralé: Use mode.\n\nTyp: text, null", Hover(schema, DataDialect.Json, json, "\"ol", "cs")!.Text);
        Assert.Equal("Veraltet: Use mode.\n\nTyp: Text, null", Hover(schema, DataDialect.Json, json, "\"ol", "de")!.Text);
    }

    [Fact]
    public void The_description_is_found_in_yaml_toml_and_a_file_that_does_not_read_gets_none()
    {
        var schema = Parse(Custom);
        Assert.Equal("How it runs.\n\nValues: fast, slow, a b", Hover(schema, DataDialect.Yaml, "mode: fast\nflag: true\n", "mo")!.Text);
        Assert.Equal("Type: boolean", Hover(schema, DataDialect.Yaml, "mode: fast\nflag: true\n", "flag: tr")!.Text);
        Assert.Equal("How it runs.\n\nValues: fast, slow, a b", Hover(schema, DataDialect.Toml, "mode = \"fast\"\n", "mo")!.Text);
        Assert.Equal("Package version (semver).\n\nType: string", Hover(Bundled("package.json"), DataDialect.Json, "{ \"version\": \"1\" }", "\"vers")!.Text);
        Assert.Null(Hover(schema, DataDialect.Yaml, "mode:   fast\nflag: true\n", "mode:")); // in the blanks between a key and its value
        Assert.Null(Hover(Bundled("package.json"), DataDialect.Json, "{ \"version\": }", "\"vers"));
        Assert.Null(Hover(schema, DataDialect.Yaml, "other: 1\n", "oth")); // a key the schema does not know
    }

    // ---- texts ----

    const string Kitchen = """
        { "type": "object",
          "properties": {
            "t": { "type": "string" }, "e": { "enum": ["a"] }, "c": { "const": 1 },
            "any": { "anyOf": [ { "type": "string" }, { "type": "integer" } ] },
            "one0": { "oneOf": [ { "type": "string" }, { "type": "integer" } ] },
            "oneMany": { "oneOf": [ { "type": "integer" }, { "minimum": 0 } ] },
            "not": { "not": { "type": "string" } },
            "minP": { "type": "object", "minProperties": 1 }, "maxP": { "type": "object", "maxProperties": 0 },
            "names": { "type": "object", "propertyNames": { "pattern": "^a" } },
            "minI": { "type": "array", "minItems": 1 }, "maxI": { "type": "array", "maxItems": 0 },
            "uniq": { "type": "array", "uniqueItems": true }, "cont": { "type": "array", "contains": { "type": "string" } },
            "minL": { "type": "string", "minLength": 3 }, "maxL": { "type": "string", "maxLength": 1 }, "pat": { "type": "string", "pattern": "^x" },
            "min": { "type": "number", "minimum": 5 }, "max": { "type": "number", "maximum": 5 },
            "xmin": { "type": "number", "exclusiveMinimum": 5 }, "xmax": { "type": "number", "exclusiveMaximum": 5 }, "mul": { "type": "number", "multipleOf": 5 },
            "no": false },
          "required": ["missing"], "additionalProperties": false }
        """;

    const string KitchenData = """
        { "t": 1, "e": "b", "c": 2, "any": true, "one0": true, "oneMany": 1, "not": "s", "minP": {}, "maxP": {"k": 1}, "names": {"b": 1},
          "minI": [], "maxI": [1], "uniq": [1, 1], "cont": [1], "minL": "a", "maxL": "abc", "pat": "y",
          "min": 1, "max": 9, "xmin": 5, "xmax": 5, "mul": 7, "no": 1, "extra": 1 }
        """;

    [Fact]
    public void Every_finding_of_the_validator_has_a_text_in_czech_slovak_and_german_and_english_keeps_the_original()
    {
        var issues = new SchemaValidator(Parse(Kitchen), Opt).Validate(Parse(KitchenData));
        Assert.Equal(25, issues.Select(i => i.Code + (i.Args is ["many"] ? "*" : "")).Distinct().Count());

        foreach (var language in new[] { "cs", "sk", "de" })
        {
            var texts = SchemaTexts.For(language);
            foreach (var issue in issues)
            {
                string text = texts.Issue(issue);
                Assert.True(!text.Contains(issue.Detail, StringComparison.Ordinal), $"{language}: {issue.Code} is not translated: {text}");
                Assert.StartsWith(issue.Pointer, text, StringComparison.Ordinal);
            }
        }
        static string English(SchemaIssue i) => (i.Pointer.Length == 0 ? "" : i.Pointer + ": ") + i.Detail;
        Assert.Equal(issues.Select(English), issues.Select(i => SchemaTexts.For("en").Issue(i)));
        Assert.Equal(issues.Select(English), issues.Select(i => SchemaTexts.For("fr").Issue(i))); // an unknown language is English
    }

    [Fact]
    public void The_values_of_a_finding_go_into_its_text()
    {
        var issues = new SchemaValidator(Parse("""{ "type": "object", "required": ["a"], "properties": { "b": { "type": "integer", "minimum": 3 } }, "additionalProperties": false }"""), Opt)
            .Validate(Parse("""{ "b": 1, "c": 2 }"""));
        var cs = SchemaTexts.For("cs");
        var texts = issues.ToDictionary(i => i.Code, i => cs.Issue(i));
        Assert.Equal("Vlastnost „a“ je povinná.", texts["required"]);
        Assert.Equal("/b: Hodnota musí být alespoň 3.", texts["minimum"]);
        Assert.Equal("/c: Vlastnost „c“ tu není povolena.", texts["additionalProperties"]);
        Assert.Equal("Syntaxfehler in Zeile 3, Spalte 7: x", SchemaTexts.For("de").Syntax(3, 7, "x"));
        Assert.Equal("Chyba syntaxe na riadku 3, v stĺpci 7: x", SchemaTexts.For("sk").Syntax(3, 7, "x"));
        Assert.Equal("Syntax error at line 3, column 7: x", SchemaTexts.For("en").Syntax(3, 7, "x"));
    }

    // ---- package manifests ----

    [Fact]
    public void A_pubspec_gives_its_dependencies_with_the_version_and_only_an_exact_one_is_pinned()
    {
        const string text = "name: demo\nenvironment:\n  sdk: '>=3.0.0 <4.0.0'\ndependencies:\n  flutter:\n    sdk: flutter\n  http: ^1.2.0\n  path: 1.8.3 # exact\n  collection: \"1.17.0\"\n  htt\n  any_pkg: any\ndev_dependencies:\n  lints: '>=2.0.0 <4.0.0'\n  test: 1.24.0\n";
        var found = Manifests.ReadPubspec(text);
        Assert.Equal(["flutter", "http", "path", "collection", "htt", "any_pkg", "lints", "test"], found.Select(d => d.Name));
        Assert.Equal(["path", "collection", "test"], found.Where(d => d.Pinned).Select(d => d.Name));
        var http = found.Single(d => d.Name == "http");
        Assert.Equal(("http", "1.2.0"), (text[http.NameStart..http.NameEnd], text[http.VersionStart..http.VersionEnd]));
        var collection = found.Single(d => d.Name == "collection");
        Assert.Equal("1.17.0", text[collection.VersionStart..collection.VersionEnd]); // the quotes are not part of it
        Assert.Equal(-1, found.Single(d => d.Name == "flutter").VersionStart);
        Assert.Equal("htt", text[found.Single(d => d.Name == "htt").NameStart..found.Single(d => d.Name == "htt").NameEnd]);
    }
}
