using System.Text.Json;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.Core.Tests;

public class SyntaxGrammarFormatTests
{
    private static readonly string[] Expected =
    [
        "plaintext", "ini", "json", "xml", "html", "css", "javascript", "php", "php-code", "csharp", "sql", "markdown", "python", "shell", "yaml", "latte", "neon", "razor", "sln",
    ];

    [Fact]
    public void All_mvp_grammars_are_built_in_and_compile()
    {
        var registry = BuiltinGrammars.Registry;
        Assert.Equal(Expected.Order(), registry.All.Select(g => g.Id).Order());
        registry.Validate();
        foreach (var g in registry.All) Assert.False(string.IsNullOrEmpty(g.Version));
    }

    [Theory]
    [InlineData("a.php", "php")]
    [InlineData("index.html", "html")]
    [InlineData("Program.cs", "csharp")]
    [InlineData("app.config", "xml")]
    [InlineData("settings.conf", "ini")]
    [InlineData("composer.lock", "json")]
    [InlineData("x.CSS", "css")]
    [InlineData("layout.latte", "latte")]
    [InlineData("services.neon", "neon")]
    [InlineData(".bashrc", "shell")]
    [InlineData("notes.txt", "plaintext")]
    [InlineData("Index.cshtml", "razor")]
    [InlineData("Counter.razor", "razor")]
    [InlineData("App.sln", "sln")]
    public void A_file_name_selects_its_grammar(string file, string id) => Assert.Equal(id, BuiltinGrammars.Registry.ForFile(file)?.Id);

    [Fact]
    public void A_first_line_selects_a_grammar_for_a_file_without_extension()
    {
        Assert.Equal("shell", BuiltinGrammars.Registry.ForFirstLine("#!/usr/bin/env bash")?.Id);
        Assert.Equal("python", BuiltinGrammars.Registry.ForFirstLine("#!/usr/bin/python3")?.Id);
        Assert.Equal("xml", BuiltinGrammars.Registry.ForFirstLine("<?xml version=\"1.0\"?>")?.Id);
        Assert.Null(BuiltinGrammars.Registry.ForFirstLine("plain words"));
    }

    [Fact]
    public void The_schema_describes_every_property_the_built_in_files_use()
    {
        using var schema = JsonDocument.Parse(BuiltinGrammars.Schema);
        var top = schema.RootElement.GetProperty("properties");
        var rule = schema.RootElement.GetProperty("$defs").GetProperty("rule").GetProperty("properties");
        var group = schema.RootElement.GetProperty("$defs").GetProperty("keywordGroup").GetProperty("properties");
        Assert.Equal(1, schema.RootElement.GetProperty("properties").GetProperty("formatVersion").GetProperty("const").GetInt32());

        int files = 0;
        var assembly = typeof(Grammar).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Grammars/", StringComparison.Ordinal) && !n.EndsWith("schema.json", StringComparison.Ordinal)))
        {
            files++;
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var doc = JsonDocument.Parse(stream);
            foreach (var p in doc.RootElement.EnumerateObject()) Assert.True(top.TryGetProperty(p.Name, out _), $"{name}: '{p.Name}' is not in the schema");
            Assert.Equal(1, doc.RootElement.GetProperty("formatVersion").GetInt32());
            if (doc.RootElement.TryGetProperty("keywords", out var groups))
                foreach (var g in groups.EnumerateArray())
                    foreach (var p in g.EnumerateObject()) Assert.True(group.TryGetProperty(p.Name, out _), $"{name}: keyword group property '{p.Name}'");
            foreach (var p in AllRules(doc.RootElement).SelectMany(r => r.EnumerateObject()))
                Assert.True(rule.TryGetProperty(p.Name, out _), $"{name}: rule property '{p.Name}' is not in the schema");
        }
        Assert.Equal(Expected.Length, files);
    }

    private static IEnumerable<JsonElement> AllRules(JsonElement container)
    {
        var lists = new List<JsonElement>();
        if (container.TryGetProperty("rules", out var rules)) lists.Add(rules);
        if (container.TryGetProperty("repository", out var repo)) lists.AddRange(repo.EnumerateObject().Select(p => p.Value));
        foreach (var list in lists)
            foreach (var rule in list.EnumerateArray())
            {
                yield return rule;
                if (rule.TryGetProperty("rules", out _))
                    foreach (var inner in AllRules(rule)) yield return inner;
            }
    }

    private static Grammar Load(string json) => Grammar.Parse(json, new GrammarRegistry());

    [Fact]
    public void A_format_version_this_build_does_not_read_is_rejected()
    {
        var e = Assert.Throws<GrammarException>(() => Load("""{ "formatVersion": 2, "id": "x" }"""));
        Assert.Contains("not supported", e.Message);
        Assert.Throws<GrammarException>(() => Load("""{ "id": "x" }"""));
        Assert.Throws<GrammarException>(() => Load("""{ "formatVersion": "1", "id": "x" }"""));
    }

    [Theory]
    [InlineData("""{ "formatVersion": 1, "id": "x", "colour": 1 }""")]
    [InlineData("""{ "formatVersion": 1, "id": "X Y" }""")]
    [InlineData("""{ "formatVersion": 1, "id": "x", "rules": [ { "match": "(", "token": "a" } ] }""")]
    [InlineData("""{ "formatVersion": 1, "id": "x", "rules": [ { "match": "a", "token": "Bad Kind" } ] }""")]
    [InlineData("""{ "formatVersion": 1, "id": "x", "rules": [ { "match": "a", "words": true } ] }""")]
    [InlineData("""{ "formatVersion": 1, "id": "x", "rules": [ { "begin": "a" } ] }""")]
    [InlineData("""{ "formatVersion": 1, "id": "x", "rules": [ { "match": "a", "typo": 1 } ] }""")]
    [InlineData("""{ "formatVersion": 1, "id": "x", "keywords": [ { "name": "k" } ] }""")]
    [InlineData("""{ "formatVersion": 1, "id": "x", "rules": "no" }""")]
    [InlineData("not json")]
    public void A_malformed_file_is_rejected_with_a_message(string json)
    {
        var e = Assert.Throws<GrammarException>(() => Load(json));
        Assert.False(string.IsNullOrWhiteSpace(e.Message));
    }

    [Fact]
    public void A_reference_to_a_missing_grammar_or_entry_is_found_by_validation()
    {
        Assert.Throws<GrammarException>(() => Load("""{ "formatVersion": 1, "id": "x", "rules": [ { "include": "#nope" } ] }""").Validate());
        Assert.Throws<GrammarException>(() => Load("""{ "formatVersion": 1, "id": "x", "rules": [ { "include": "other" } ] }""").Validate());
        Assert.Throws<GrammarException>(() => Load("""{ "formatVersion": 1, "id": "x", "rules": [ { "begin": "<", "end": ">", "embed": "other" } ] }""").Validate());
    }

    private const string Small = """
        {
          "formatVersion": 1, "id": "small", "version": "2.0.0", "caseSensitive": false,
          "keywords": [
            { "name": "control", "token": "keyword.control", "words": ["if", "ELSE"] },
            { "name": "exact", "token": "constant", "words": ["Nil"], "caseSensitive": true }
          ],
          "rules": [
            { "match": "//.*", "token": "comment" },
            { "match": "'.*", "token": "comment.rem" },
            { "begin": "\\(\\*", "end": "\\*\\)", "token": "comment", "multiline": true, "name": "paren-comment" },
            { "begin": "\\{", "end": "\\}", "token": "comment", "multiline": true, "name": "brace-comment" },
            { "begin": "\"", "end": "\"", "token": "string", "name": "string", "rules": [ { "match": "\\\\.", "token": "string.escape" } ] },
            { "match": "\\b\\d+\\b", "token": "number" },
            { "match": "/[^/\\r\\n]+/", "token": "regex" },
            { "words": true },
            { "operators": [":=", "<>", ":", "="] }
          ]
        }
        """;

    private static string Dump(Tokenizer tz, ref LineState state, string line)
    {
        var tokens = new List<Token>();
        state = tz.TokenizeLine(line, state, tokens);
        return SyntaxHelpers.Render(line, tokens);
    }

    [Fact]
    public void A_user_grammar_expresses_comment_kinds_keyword_groups_strings_numbers_regex_and_operators()
    {
        var g = Load(Small);
        Assert.Equal("2.0.0", g.Version);
        var tz = new Tokenizer(g);
        var s = tz.InitialState;
        Assert.Equal("keyword.control=IF | operator=:= | number=12", Dump(tz, ref s, "IF x:=12"));
        Assert.Equal("constant=Nil", Dump(tz, ref s, "Nil nil"));
        Assert.Equal("keyword.control=else | comment=// c", Dump(tz, ref s, "else // c"));
        Assert.Equal("comment.rem=' basic", Dump(tz, ref s, "' basic"));
        Assert.Equal("string=\"a | string.escape=\\\" | string=b\" | regex=/re/", Dump(tz, ref s, "\"a\\\"b\" /re/"));
        Assert.Equal("comment=(* open", Dump(tz, ref s, "(* open"));
        Assert.Equal("paren-comment", s.Description);
        Assert.Equal("comment={ not nested } *)", Dump(tz, ref s, "{ not nested } *) "));
        Assert.True(s.IsInitial);
        Assert.Equal("comment={ brace", Dump(tz, ref s, "{ brace"));
        Assert.Equal("brace-comment", s.Description);
    }

    [Fact]
    public void A_region_that_is_not_multiline_ends_with_the_line()
    {
        var tz = new Tokenizer(Load(Small));
        var s = tz.InitialState;
        Dump(tz, ref s, "\"never closed");
        Assert.True(s.IsInitial);
    }

    [Fact]
    public void An_embedded_grammar_runs_until_the_end_pattern_of_the_host_and_the_host_resumes()
    {
        var registry = new GrammarRegistry();
        registry.Add("""
            { "formatVersion": 1, "id": "guest", "rules": [ { "match": "\\d+", "token": "number" }, { "begin": "\"", "end": "\"", "token": "string", "name": "gs" } ] }
            """);
        registry.Add("""
            {
              "formatVersion": 1, "id": "host",
              "rules": [
                { "begin": "<g>", "end": "(?=</g>)", "name": "guest-block", "multiline": true, "beginToken": "tag", "embed": "guest", "rules": [ { "match": "#.*", "token": "comment" } ] },
                { "match": "</g>", "token": "tag" },
                { "match": "\\d+", "token": "number.host" }
              ]
            }
            """);
        registry.Validate();
        var tz = new Tokenizer(registry.Find("host")!);
        var s = tz.InitialState;
        Assert.Equal("number.host=1 | tag=<g>", Dump(tz, ref s, "1 <g>"));
        Assert.Equal("number=2 | comment=# note", Dump(tz, ref s, "2 # note"));
        Assert.Equal("guest-block", s.Description);
        Assert.Equal("string=\"s\" | number=3 | tag=</g> | number.host=4", Dump(tz, ref s, "\"s\" 3 </g> 4"));
        Assert.True(s.IsInitial);
    }

    [Fact]
    public void Include_splices_rules_of_a_repository_entry_and_of_another_grammar()
    {
        var registry = new GrammarRegistry();
        registry.Add("""{ "formatVersion": 1, "id": "base", "repository": { "num": [ { "match": "\\d+", "token": "number" } ] }, "rules": [ { "match": "!", "token": "bang" } ] }""");
        registry.Add("""{ "formatVersion": 1, "id": "top", "repository": { "own": [ { "match": "x", "token": "ex" } ] }, "rules": [ { "include": "#own" }, { "include": "base" }, { "include": "base#num" } ] }""");
        registry.Validate();
        var tz = new Tokenizer(registry.Find("top")!);
        var s = tz.InitialState;
        Assert.Equal("ex=x | number=12 | bang=!", Dump(tz, ref s, "x12!"));
    }

    [Fact]
    public void A_pattern_that_matches_nothing_or_everything_cannot_hang_the_tokenizer()
    {
        var g = Load("""{ "formatVersion": 1, "id": "odd", "rules": [ { "match": "x*", "token": "x" }, { "begin": "\\b", "end": "$", "token": "r" } ] }""");
        var tz = new Tokenizer(g);
        var tokens = new List<Token>();
        tz.TokenizeLine("axxb", tz.InitialState, tokens);
        Assert.Equal("x=xx", SyntaxHelpers.Render("axxb", tokens));
    }

    [Fact]
    public void A_nesting_of_regions_deeper_than_the_limit_is_cut_not_followed()
    {
        var g = Load("""{ "formatVersion": 1, "id": "deep", "repository": { "r": [ { "begin": "\\(", "end": "\\)", "token": "p", "multiline": true, "rules": [ { "include": "#r" } ] } ] }, "rules": [ { "include": "#r" } ] }""");
        var tz = new Tokenizer(g);
        var s = tz.TokenizeLine(new string('(', 500), tz.InitialState, []);
        Assert.Equal(LineState.MaxDepth, s.Description.Split('>').Length);
        s = tz.TokenizeLine(new string(')', 500), s, []);
        Assert.True(s.IsInitial);
    }
}
