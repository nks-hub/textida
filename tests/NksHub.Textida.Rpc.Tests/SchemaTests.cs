using NksHub.Textida.Core.Data;
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
        Assert.False(TomlDataReader.TryParse("a = \"\\uD800\"\n", 8, out _, out var surrogate)); // not a scalar value: a syntax error, never an exception
        Assert.Contains("\\u", surrogate!.Message);
        Assert.False(TomlDataReader.TryParse("a = \"\\U00110000\"\n", 8, out _, out _));
        Assert.True(TomlDataReader.TryParse("a = \"\\U0001F600\"\n", 8, out var emoji, out _));
        Assert.Equal("\U0001F600", emoji!.Get("a")!.Text);
        Assert.True(TomlDataReader.TryParse("a = \"\"\"\nx\"\"\"\"\"\nb = '''y'''''\n", 8, out var quotes, out var quoteError), quoteError?.Message); // one or two quotes may touch the closing three
        Assert.Equal(("x\"\"", "y''"), (quotes!.Get("a")!.Text, quotes.Get("b")!.Text));
        Assert.False(TomlDataReader.TryParse("a = \"\"\"x\"\"\"\"\"\"\n", 8, out _, out _)); // six quotes are too many
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

    // ---- YAML ----

    static string Dump(DataNode n) => n.Kind switch
    {
        DataKind.Object => "{" + string.Join(",", n.Members.Select(m => m.Name + ":" + Dump(m.Value))) + "}",
        DataKind.Array => "[" + string.Join(",", n.Items.Select(Dump)) + "]",
        DataKind.String => "'" + n.Text.Replace("\n", "\\n", StringComparison.Ordinal) + "'",
        DataKind.Number => n.Text,
        DataKind.Boolean => n.Bool ? "true" : "false",
        _ => "null",
    };

    static string Yaml(string text)
    {
        Assert.True(YamlDataReader.TryParse(text, 16, out var n, out var e), e?.Message);
        return Dump(n!);
    }

    static DataSyntaxException YamlError(string text)
    {
        Assert.False(YamlDataReader.TryParse(text, 16, out _, out var e), "the text should not be read");
        return e!;
    }

    const string Workflow = "name: CI\non:\n  push:\n    branches: [main]\n  pull_request:\njobs:\n  build:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@v4\n      - name: Test\n        run: |\n          dotnet test\n          echo \"done: yes\"\n        env:\n          A: 1 # comment\n";

    [Fact]
    public void Yaml_workflow_valid_syntax_error_and_schema_error()
    {
        var good = Catalog().Check("/r/.github/workflows/ci.yml", Workflow, Opt);
        Assert.Equal("github-workflow", good.Schema?.Id);
        Assert.Equal(DataDialect.Yaml, good.Dialect);
        Assert.Null(good.Syntax);
        Assert.Empty(good.Issues);

        var broken = Workflow.Replace("branches: [main]", "branches: [main", StringComparison.Ordinal);
        var bad = Catalog().Check("/r/.github/workflows/ci.yml", broken, Opt);
        Assert.NotNull(bad.Syntax);
        Assert.True(bad.Syntax!.Start > broken.IndexOf("[main", StringComparison.Ordinal)); // the unclosed '[' swallows the next lines, so the error is after it

        const string wrong = "on: push\nextra: 1\njobs:\n  build:\n    steps:\n      - run: 5\n";
        var issues = Catalog().Check(".github/workflows/x.yaml", wrong, Opt).Issues;
        Assert.Contains(issues, i => i.Pointer == "/extra" && i.Code == "additionalProperties" && wrong[i.Start..i.End] == "extra");
        Assert.Contains(issues, i => i.Pointer == "/jobs/build" && i.Code == "anyOf");
        Assert.Contains(issues, i => i.Pointer == "/jobs/build/steps/0/run" && wrong[i.Start..i.End] == "5");
    }

    [Fact]
    public void Yaml_without_a_schema_is_parsed_but_a_file_outside_the_subset_is_an_error_with_a_range()
    {
        Assert.Null(Catalog().Check("compose.yml", "a: 1\n", Opt).Schema);
        var anchored = Catalog().Check("compose.yml", "a: &x 1\n", Opt);
        Assert.Contains("Anchors", anchored.Syntax!.Message);
        Assert.Equal("&x", "a: &x 1\n"[anchored.Syntax.Start..anchored.Syntax.End]);
    }

    [Theory]
    [InlineData("a:\n- x\n- y\nb: 1\n", "{a:['x','y'],b:1}")] // a sequence may stand at the indentation of its key
    [InlineData("a:\n  - x\nb: 1\n", "{a:['x'],b:1}")]
    [InlineData("k: v # c\nj: v#c\n", "{k:'v',j:'v#c'}")] // ' #' starts a comment, '#' inside a word does not
    [InlineData("\"a: b\": 1\n'it''s': 2\n", "{a: b:1,it's:2}")] // a quoted key may hold ': '
    [InlineData("a: yes\nb: 1.0.0\nc: ~\nd: True\ne: 0x1F\nf: 0o17\ng: .5\nh: 1e3\ni: -.inf\nj: 'null'\nk:\nl: 12:30\n", "{a:'yes',b:'1.0.0',c:null,d:true,e:31,f:15,g:.5,h:1e3,i:-Infinity,j:'null',k:null,l:'12:30'}")] // core schema: yes and on are strings
    [InlineData("a: |\n  l1\n  l2\n\nb: >-\n  x\n  y\n\n  z\nc: |+\n  k\n\nd: >\n  p\n   more\n  q\n", "{a:'l1\\nl2\\n',b:'x y\\nz',c:'k\\n\\n',d:'p\\n more\\nq\\n'}")] // chomping and folding
    [InlineData("a: 1\n---\n- b\n...\n---\nc\n", "[{a:1},['b'],'c']")] // several documents give an array
    [InlineData("m: {a: [1, 2], \"b\": x, c}\n", "{m:{a:[1,2],b:'x',c:null}}")]
    [InlineData("steps:\n  - name: a\n    run: b\n  - uses: c\n", "{steps:[{name:'a',run:'b'},{uses:'c'}]}")] // a mapping that starts on the line of its dash
    [InlineData("b: 'x\n  y\n\n  z'\nc: plain\n  more\n", "{b:'x y\\nz',c:'plain more'}")] // line folding in quoted and plain scalars
    [InlineData("a: \"t\\tA\\u0041\\x41\"\n", "{a:'t\tAAA'}")]
    [InlineData("url: http://h.example:80/p?a=b\nwin: C:\\dir\n", "{url:'http://h.example:80/p?a=b',win:'C:\\dir'}")] // ':' not followed by a blank is part of the scalar
    [InlineData("\uFEFFa: 1\r\nb:\r\n  - 2\r\n", "{a:1,b:[2]}")]
    [InlineData("", "null")]
    public void Yaml_reader_reads(string text, string expected) => Assert.Equal(expected, Yaml(text));

    [Fact]
    public void Yaml_reader_keeps_the_offsets_of_keys_and_values()
    {
        const string t = "key: value\nlist:\n  - 12\n";
        Assert.True(YamlDataReader.TryParse(t, 16, out var n, out _));
        Assert.Equal("key", t[n!.Members[0].NameStart..n.Members[0].NameEnd]);
        Assert.Equal("value", t[n.Get("key")!.Start..n.Get("key")!.End]);
        Assert.Equal("12", t[n.Get("list")!.Items[0].Start..n.Get("list")!.Items[0].End]);
    }

    [Theory]
    [InlineData("a: 1\nb: 2\na: 3\n", "defined twice", "a: 3")]
    [InlineData("a:\n\tb: 1\n", "tab", "\tb")]
    [InlineData("a: !!str 1\n", "Tags", "!!str")]
    [InlineData("a: &x 1\nb: *x\n", "Anchors", "&x")]
    [InlineData("a:\n  b: 1\n c: 2\n", "indentation", "c: 2")]
    [InlineData("a: b: c\n", "mapping value", ": c")]
    [InlineData("a: 1\nb\n", "key followed", "b\n")]
    [InlineData("a: \"abc\n", "not closed", "\"abc")]
    [InlineData("a: {x: 1, x: 2}\n", "defined twice", "x: 2")]
    [InlineData("? a\n: b\n", "Complex", "? a")]
    [InlineData("%YAML 1.2\n---\na: 1\n", "directives", "%YAML 1.2")]
    [InlineData("a: [1, 2\n", "']'", "")]
    [InlineData("a: \"\\q\"\n", "escape", "\\q")]
    [InlineData("a: - b\n", "block sequence", "- b")]
    public void Yaml_reader_rejects(string text, string message, string at)
    {
        var e = YamlError(text);
        Assert.Contains(message, e.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(text.IndexOf(at, StringComparison.Ordinal) is var i && at.Length > 0 ? i : text.Length, e.Start);
    }

    [Fact]
    public void Yaml_reader_limits_the_depth_and_refuses_lone_cr()
    {
        Assert.False(YamlDataReader.TryParse("a:\n b:\n  c:\n   d: 1\n", 3, out _, out var deep));
        Assert.Contains("deep", deep!.Message);
        Assert.Contains("CR", YamlError("a: 1\rb: 2").Message);
    }

    [Fact]
    public void Yaml_key_completion_offers_the_keys_of_the_mapping_around_the_caret()
    {
        var wf = SchemaCatalog.Bundled.First(b => b.Id == "github-workflow");
        const string t = "name: CI\non: push\njobs:\n  build:\n    ru\n";
        var offer = KeyCompletion.Offer(wf, DataDialect.Yaml, t, t.IndexOf("ru", StringComparison.Ordinal) + 2, Opt);
        Assert.NotNull(offer);
        Assert.Contains("runs-on", offer!.Keys);
        Assert.DoesNotContain("name", offer.Keys); // top-level keys are not offered inside a job
        Assert.Equal("runs-on: ", offer.Insert("runs-on"));
        Assert.Equal("ru", t[offer.Start..offer.End]);
        var top = KeyCompletion.Offer(wf, DataDialect.Yaml, "name: CI\nj", 10, Opt);
        Assert.Equal(["jobs"], top!.Keys);
        Assert.Null(KeyCompletion.Offer(wf, DataDialect.Yaml, "name: C", 7, Opt)); // a value, not a key
    }

    // ---- agent files ----

    [Theory]
    [InlineData("/w/.claude/settings.json", "claude-settings", DataDialect.Json)]
    [InlineData("C:\\w\\.claude\\settings.local.json", "claude-settings", DataDialect.Json)]
    [InlineData("/w/.mcp.json", "mcp", DataDialect.Json)]
    [InlineData("/home/u/.codex/config.toml", "codex-config", DataDialect.Toml)]
    [InlineData("/w/opencode.json", "opencode", DataDialect.Jsonc)]
    [InlineData("/w/opencode.jsonc", "opencode", DataDialect.Jsonc)]
    [InlineData("/w/.claude/skills/deploy/SKILL.md", "skill-claude", DataDialect.Markdown)]
    [InlineData("/w/skills/deploy/SKILL.md", "skill-agentskills", DataDialect.Markdown)]
    [InlineData("/w/.claude/agents/reviewer.md", "agent-claude", DataDialect.Markdown)]
    [InlineData("/w/.claude/rules/api.md", "rule-claude", DataDialect.Markdown)]
    [InlineData("/w/.claude/rules/sub/api.md", "rule-claude", DataDialect.Markdown)]
    [InlineData("/w/.cursor/rules/api.mdc", "rule-cursor", DataDialect.Markdown)]
    [InlineData("/w/README.md", null, DataDialect.Markdown)]
    [InlineData("/w/.claude/other.json", null, DataDialect.Json)]
    public void Agent_files_map_path_to_schema_and_dialect(string path, string? schema, DataDialect dialect)
    {
        var (entry, found) = Catalog().Resolve(path);
        Assert.Equal(schema, entry?.Id);
        Assert.Equal(dialect, found);
    }

    [Fact]
    public void User_mapping_switches_the_frontmatter_profile()
    {
        var r = Catalog(new SchemaMapping("**/skills/**/SKILL.md", "skill-claude")).Check("/w/skills/a/SKILL.md", "---\nname: Other_Name\n---\n", Opt);
        Assert.Equal("skill-claude", r.Schema?.Id);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Claude_settings_comment_is_a_syntax_error_and_opencode_accepts_it()
    {
        const string text = "{\n  // allow npm\n  \"permissions\": { \"allow\": [\"Bash(npm run *)\"] }\n}";
        var r = Catalog().Check("/w/.claude/settings.json", text, Opt);
        Assert.Equal("claude-settings", r.Schema?.Id);
        Assert.Equal("/", text[r.Syntax!.Start..(r.Syntax.Start + 1)]);
        Assert.Equal(text.IndexOf("//", StringComparison.Ordinal), r.Syntax.Start);
        Assert.Null(Catalog().Check("/w/opencode.json", text, Opt).Syntax);

        const string wrong = "{ \"permissions\": { \"allow\": [5] }, \"cleanupPeriodDays\": 0, \"model\": \"claude-sonnet-5\" }";
        var issues = Catalog().Check("/w/.claude/settings.local.json", wrong, Opt).Issues;
        Assert.Contains(issues, i => i.Pointer == "/permissions/allow/0" && wrong[i.Start..i.End] == "5");
        Assert.Contains(issues, i => i.Pointer == "/cleanupPeriodDays");
        Assert.Equal(2, issues.Count);
    }

    [Fact]
    public void Mcp_json_checks_the_server_entries()
    {
        const string ok = """{ "mcpServers": { "a": { "command": "npx", "args": ["x"] }, "b": { "type": "http", "url": "https://h.example/mcp" }, "c": { "type": "stdio", "command": "x" } } }""";
        Assert.Empty(Catalog().Check("/w/.mcp.json", ok, Opt).Issues);
        const string bad = """{ "mcpServers": { "a": { "url": "https://h.example" }, "b": {}, "c": { "type": "sse" }, "d": { "type": "x", "command": "y" } } }""";
        var issues = Catalog().Check("/w/.mcp.json", bad, Opt).Issues;
        Assert.Contains(issues, i => i.Pointer == "/mcpServers/a" && i.Code == "required"); // a url without a type is read as stdio
        Assert.Contains(issues, i => i.Pointer == "/mcpServers/b");
        Assert.Contains(issues, i => i.Pointer == "/mcpServers/c");
        Assert.Contains(issues, i => i.Pointer == "/mcpServers/d/type" && i.Code == "enum");
        Assert.Contains(Catalog().Check("/w/.mcp.json", "{}", Opt).Issues, i => i.Code == "required");
    }

    [Fact]
    public void Codex_config_toml_is_checked()
    {
        Assert.Empty(Catalog().Check("/h/.codex/config.toml", "model = \"gpt\"\n[mcp_servers.x]\ncommand = \"y\"\n", Opt).Issues);
        Assert.Contains(Catalog().Check("/h/.codex/config.toml", "model = 5\n", Opt).Issues, i => i.Pointer == "/model" && i.Code == "type");
    }

    const string GoodSkill = "---\nname: pdf-processing\ndescription: Extract PDF text. Use when handling PDFs.\nlicense: Apache-2.0\nmetadata:\n  author: example-org\n  version: \"1.0\"\n---\n\n# Body\n";

    [Fact]
    public void Agent_skills_profile_valid_file_has_no_issues()
    {
        var r = Catalog().Check("/w/skills/pdf-processing/SKILL.md", GoodSkill, Opt);
        Assert.Equal("skill-agentskills", r.Schema?.Id);
        Assert.Null(r.Syntax);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Agent_skills_profile_underlines_name_that_differs_from_the_folder()
    {
        const string text = "---\nname: other-name\ndescription: x\n---\n";
        var issue = Assert.Single(Catalog().Check("C:\\w\\skills\\pdf-processing\\SKILL.md", text, Opt).Issues);
        Assert.Equal("folder", issue.Code);
        Assert.Equal("other-name", text[issue.Start..issue.End]);
    }

    [Fact]
    public void Agent_skills_profile_rejects_what_the_specification_forbids()
    {
        const string text = "---\nname: PDF--Processing\ndescription: \"\"\nargument-hint: x\nmetadata:\n  version: 1.0\n---\n";
        var issues = Catalog().Check("/w/skills/pdf/SKILL.md", text, Opt).Issues;
        Assert.Contains(issues, i => i.Pointer == "/name" && i.Code == "pattern" && text[i.Start..i.End] == "PDF--Processing");
        Assert.Contains(issues, i => i.Pointer == "/name" && i.Code == "folder");
        Assert.Contains(issues, i => i.Pointer == "/description" && i.Code == "minLength");
        Assert.Contains(issues, i => i.Pointer == "/argument-hint" && i.Code == "additionalProperties");
        Assert.Contains(issues, i => i.Pointer == "/metadata/version" && i.Code == "type");
        var noFrontmatter = Catalog().Check("/w/skills/pdf/SKILL.md", "# Just text\n", Opt).Issues;
        Assert.Equal(2, noFrontmatter.Count(i => i.Code == "required"));
        Assert.Contains("not closed", Catalog().Check("/w/skills/pdf/SKILL.md", "---\nname: pdf\n", Opt).Syntax!.Message);
        var longName = Catalog().Check("/w/skills/" + new string('a', 65) + "/SKILL.md", "---\nname: " + new string('a', 65) + "\ndescription: x\n---\n", Opt).Issues;
        Assert.Contains(longName, i => i.Code == "maxLength");
    }

    [Fact]
    public void Claude_code_profile_accepts_a_name_that_differs_from_the_folder_and_unknown_fields()
    {
        const string text = "---\nname: deploy-staging\ndescription: Deploy.\ndisable-model-invocation: yes\nallowed-tools: Read Grep\nargument-hint: [env]\npaths:\n  - \"src/**\"\neffort: high\nfuture-field: 1\n---\n";
        var r = Catalog().Check("/w/.claude/skills/deploy/SKILL.md", text, Opt);
        Assert.Equal("skill-claude", r.Schema?.Id);
        Assert.Empty(r.Issues);
        Assert.Empty(Catalog().Check("/w/.claude/skills/deploy/SKILL.md", "# Just text\n", Opt).Issues);
    }

    [Fact]
    public void Claude_code_profile_rejects_wrong_values()
    {
        const string text = "---\neffort: huge\ncontext: join\ndisable-model-invocation: maybe\nshell: zsh\npaths: 5\n---\n";
        var issues = Catalog().Check("/w/.claude/skills/deploy/SKILL.md", text, Opt).Issues;
        foreach (var p in new[] { "/effort", "/context", "/disable-model-invocation", "/shell", "/paths" })
            Assert.Contains(issues, i => i.Pointer == p);
    }

    [Fact]
    public void Claude_agent_profile_valid_and_invalid()
    {
        const string ok = "---\nname: code-reviewer\ndescription: Reviews code.\ntools: Read, Grep\npermissionMode: acceptEdits\ncolor: blue\nmemory: project\n---\nYou review.\n";
        Assert.Empty(Catalog().Check("/w/.claude/agents/reviewer.md", ok, Opt).Issues);
        const string bad = "---\nname: a:b\npermissionMode: bogus\ncolor: teal\nmaxTurns: many\n---\n";
        var issues = Catalog().Check("/w/.claude/agents/reviewer.md", bad, Opt).Issues;
        Assert.Contains(issues, i => i.Pointer == "/name" && i.Code == "pattern");
        Assert.Contains(issues, i => i.Pointer == "/permissionMode" && i.Code == "enum");
        Assert.Contains(issues, i => i.Pointer == "/color");
        Assert.Contains(issues, i => i.Pointer == "/maxTurns" && i.Code == "type");
        Assert.Contains(issues, i => i.Code == "required" && i.Detail.Contains("'description'", StringComparison.Ordinal));
    }

    [Fact]
    public void Rule_profiles_valid_and_invalid()
    {
        Assert.Empty(Catalog().Check("/w/.claude/rules/api.md", "---\npaths:\n  - \"src/api/**/*.ts\"\n---\nRules\n", Opt).Issues);
        Assert.Empty(Catalog().Check("/w/.claude/rules/api.md", "No frontmatter at all\n", Opt).Issues);
        Assert.Contains(Catalog().Check("/w/.claude/rules/api.md", "---\npaths: 5\n---\n", Opt).Issues, i => i.Pointer == "/paths");
        Assert.Empty(Catalog().Check("/w/.cursor/rules/api.mdc", "---\ndescription: API\nglobs: src/**/*.ts\nalwaysApply: false\n---\n", Opt).Issues);
        const string bad = "---\nalwaysApply: sometimes\n---\n";
        var issue = Assert.Single(Catalog().Check("/w/.cursor/rules/api.mdc", bad, Opt).Issues);
        Assert.Equal("sometimes", bad[issue.Start..issue.End]);
    }

    [Fact]
    public void Frontmatter_syntax_error_has_the_offset_in_the_whole_file_and_plain_markdown_is_not_checked()
    {
        const string text = "---\nname: pdf\ndescription: a\ndescription: b\n---\nBody: not yaml\n";
        var r = Catalog().Check("/w/skills/pdf/SKILL.md", text, Opt);
        Assert.Equal(text.IndexOf("description: b", StringComparison.Ordinal), r.Syntax!.Start);
        Assert.Contains("twice", r.Syntax.Message);
        var plain = Catalog().Check("/w/README.md", "---\nnot: [closed\n", Opt);
        Assert.Null(plain.Schema);
        Assert.Empty(plain.Issues);
    }

    [Fact]
    public void Frontmatter_key_completion_works_only_inside_the_frontmatter()
    {
        var skill = SchemaCatalog.Bundled.First(b => b.Id == "skill-agentskills");
        const string t = "---\nname: x\nde\n---\nBody de\n";
        var offer = KeyCompletion.Offer(skill, DataDialect.Markdown, t, t.IndexOf("de\n", StringComparison.Ordinal) + 2, Opt);
        Assert.Equal(["description"], offer!.Keys);
        Assert.Null(KeyCompletion.Offer(skill, DataDialect.Markdown, t, t.Length - 1, Opt));
    }
}
