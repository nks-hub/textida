using NksHub.Textida.Plugins.Json.Schema;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>Schema mapping, syntax and schema checks for JSON, JSONC and TOML (the engine of the built-in schema support).</summary>
public class SchemaTests
{
    static readonly SchemaOptions Opt = new(64, TimeSpan.FromSeconds(1));

    static SchemaCatalog Catalog(params SchemaMapping[] rules) => new(rules);

    [Fact]
    public void Package_json_valid_file_has_no_issues()
    {
        var r = Catalog().Check(@"C:\app\package.json", """{ "name": "demo", "version": "1.0.0", "scripts": { "build": "tsc" }, "private": true }""", Opt);
        Assert.Equal("package.json", r.Schema?.Id);
        Assert.Null(r.Syntax);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Package_json_error_points_at_the_value()
    {
        const string text = """{ "name": "demo", "scripts": { "build": 5 }, "private": "yes" }""";
        var r = Catalog().Check("/work/package.json", text, Opt);
        Assert.Equal(2, r.Issues.Count);
        var scripts = Assert.Single(r.Issues, i => i.Pointer == "/scripts/build");
        Assert.Equal("5", text[scripts.Start..scripts.End]);
        var priv = Assert.Single(r.Issues, i => i.Pointer == "/private");
        Assert.Equal("\"yes\"", text[priv.Start..priv.End]);
        Assert.Equal("type", priv.Code);
    }

    [Fact]
    public void Json_syntax_error_has_range_and_trailing_comma_is_only_for_jsonc()
    {
        var strict = Catalog().Check("package.json", "{ \"a\": 1, }", Opt);
        Assert.NotNull(strict.Syntax);
        Assert.Equal(10, strict.Syntax!.Start);
        var jsonc = Catalog(new SchemaMapping("*.json", "package.json", DataDialect.Jsonc)).Check("x/package.json", "{ // c\n \"name\": \"a\", /* d */ }", Opt);
        Assert.Null(jsonc.Syntax);
        Assert.Equal(DataDialect.Jsonc, jsonc.Dialect);
        Assert.NotNull(Catalog().Check("package.json", "{ // c\n }", Opt).Syntax);
    }

    [Fact]
    public void Jsonc_file_is_checked_against_the_mapped_schema()
    {
        var r = Catalog(new SchemaMapping("conf/*.jsonc", "package.json")).Check("/p/conf/app.jsonc", "{ /* note */ \"name\": 3, }", Opt);
        Assert.Equal(DataDialect.Jsonc, r.Dialect);
        Assert.Null(r.Syntax);
        Assert.Equal("type", Assert.Single(r.Issues).Code);
    }

    [Fact]
    public void File_without_schema_has_no_schema_and_is_still_parsed()
    {
        var r = Catalog().Check("notes.json", "{ \"a\": [1, 2] }", Opt);
        Assert.Null(r.Schema);
        Assert.Empty(r.Issues);
        Assert.NotNull(Catalog().Check("notes.json", "{ \"a\": [1, 2 }", Opt).Syntax);
    }

    [Fact]
    public void Toml_valid_syntax_error_and_schema_error()
    {
        const string ok = "[package]\nname = \"demo\"\nedition = \"2021\"\n\n[dependencies]\nserde = { version = \"1\", features = [\"derive\"] }\nrand = \"0.8\"\n";
        var good = Catalog().Check("Cargo.toml", ok, Opt);
        Assert.Equal("cargo", good.Schema?.Id);
        Assert.Null(good.Syntax);
        Assert.Empty(good.Issues);

        var bad = Catalog().Check("Cargo.toml", "[package]\nname = \"demo\"\nname = \"x\"\n", Opt);
        Assert.NotNull(bad.Syntax);
        Assert.Equal(3, "[package]\nname = \"demo\"\nname = \"x\"\n"[..bad.Syntax!.Start].Count(c => c == '\n') + 1);

        const string wrong = "[package]\nedition = \"2020\"\n\n[dependencies]\nrand = 5\n";
        var issues = Catalog().Check("Cargo.toml", wrong, Opt).Issues;
        Assert.Contains(issues, i => i.Code == "required" && i.Pointer == "/package");
        Assert.Contains(issues, i => i.Code == "enum" && wrong[i.Start..i.End] == "\"2020\"");
        Assert.Contains(issues, i => i.Pointer == "/dependencies/rand");
    }

    [Fact]
    public void Toml_reader_handles_arrays_of_tables_dotted_keys_numbers_and_dates()
    {
        const string t = "a.b = 0x10\nd = 1979-05-27T07:32:00Z\nf = 1_000.5\n[[bin]]\nname = 'x'\n[[bin]]\nname = '''\nl1\r\nl2'''\n[t.u]\nv = [1, [2], {k = true}]\n";
        Assert.True(TomlDataReader.TryParse(t, 8, out var n, out var e), e?.Message);
        Assert.Equal("16", n!.Get("a")!.Get("b")!.Text);
        Assert.Equal(DataKind.String, n.Get("d")!.Kind);
        Assert.Equal(1000.5, n.Get("f")!.Number);
        var bin = n.Get("bin")!;
        Assert.Equal(2, bin.Items.Count);
        Assert.Equal("l1\nl2", bin.Items[1].Get("name")!.Text);
        Assert.True(n.Get("t")!.Get("u")!.Get("v")!.Items[2].Get("k")!.Bool);
        Assert.False(TomlDataReader.TryParse("a = {b = 1}\n[a.c]\n", 8, out _, out _));
        Assert.False(TomlDataReader.TryParse("a = [[[[1]]]]", 3, out _, out _));
    }

    [Fact]
    public void Validator_keywords()
    {
        static List<SchemaIssue> Run(string schema, string data)
        {
            Assert.True(JsonDataReader.TryParse(schema, false, 64, out var s, out _));
            Assert.True(JsonDataReader.TryParse(data, false, 64, out var d, out _));
            return new SchemaValidator(s!, Opt).Validate(d!);
        }
        Assert.Single(Run("""{"type":"integer"}""", "1.5"));
        Assert.Empty(Run("""{"type":"integer"}""", "2.0"));
        Assert.Single(Run("""{"required":["a"],"additionalProperties":false,"properties":{"a":{}}}""", """{"b":1}"""), i => i.Code == "additionalProperties");
        Assert.Single(Run("""{"items":{"type":"string"},"uniqueItems":true}""", """["a","a"]"""));
        Assert.Single(Run("""{"oneOf":[{"type":"string"},{"minLength":1}]}""", "\"x\""));
        Assert.Empty(Run("""{"$ref":"#/$defs/p","$defs":{"p":{"pattern":"^a+$"}}}""", "\"aaa\""));
        Assert.Single(Run("""{"$ref":"#/$defs/p","$defs":{"p":{"pattern":"^a+$"}}}""", "\"b\""));
        Assert.Single(Run("""{"minimum":3,"multipleOf":2}""", "4.5"));
        Assert.Single(Run("""{"if":{"properties":{"k":{"const":1}}},"then":{"required":["z"]}}""", """{"k":1}"""));
        Assert.Single(Run("""{"prefixItems":[{"type":"string"}],"items":false}""", """["a", 1]"""));
        Assert.Single(Run("""{"properties":{"r":{"$ref":"#"}},"type":"object"}""", """{"r":{"r":3}}"""));
    }

    [Fact]
    public void Key_completion_lists_missing_keys_of_the_object_around_the_caret()
    {
        const string text = """{ "name": "a", "scripts": { "x": "y",  }, "repository": { "type": "git" } }""";
        Assert.True(JsonDataReader.TryParse(text, true, 64, out var data, out _));
        Assert.True(JsonDataReader.TryParse(SchemaCatalog.LoadSchemaText(SchemaCatalog.Bundled[0]), false, 64, out var schema, out _));
        var top = SchemaCatalog.CompleteKeys(schema!, data!, text.IndexOf("\"scripts\"", StringComparison.Ordinal) - 1, Opt);
        Assert.Contains("version", top);
        Assert.DoesNotContain("name", top);
        Assert.Contains("dependencies", top);
        Assert.Empty(SchemaCatalog.CompleteKeys(schema!, data!, text.IndexOf("  }", StringComparison.Ordinal), Opt)); // scripts has no named keys
    }

    [Theory]
    [InlineData("package.json", "C:\\a\\b\\package.json", true)]
    [InlineData("package.json", "package.json.bak", false)]
    [InlineData("**/.claude/settings*.json", "C:\\w\\.claude\\settings.local.json", true)]
    [InlineData(".claude/settings.json", "/w/x/.claude/settings.json", true)]
    [InlineData("*.jsonc", "/w/a.jsonc", true)]
    [InlineData("conf/?.toml", "/w/conf/ab.toml", false)]
    public void Glob_masks(string glob, string path, bool expected) => Assert.Equal(expected, Glob.IsMatch(glob, path));

    [Fact]
    public void Bundled_schemas_are_valid_json()
    {
        foreach (var e in SchemaCatalog.Bundled)
            Assert.True(JsonDataReader.TryParse(SchemaCatalog.LoadSchemaText(e), false, 64, out _, out var err), $"{e.Id}: {err?.Message}");
    }
}
