using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.Core.Tests;

/// <summary>PZ.5.3: TextMate grammars become grammars of the editor; what cannot be carried over is named.</summary>
public sealed class TextMateImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tm-" + Guid.NewGuid().ToString("N")[..8]);

    public TextMateImportTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private const string Toy = """
        {
          "name": "Toy",
          "scopeName": "source.toy",
          "fileTypes": ["toy", "lua"],
          "firstLineMatch": "^#!toy",
          "patterns": [
            { "include": "#comments" },
            { "match": "\\b(if|else)\\b", "name": "keyword.control.toy" },
            { "match": "\\b(def)\\s+([[:alpha:]_]\\w*)", "captures": { "1": { "name": "keyword.control.toy" }, "2": { "name": "entity.name.function.toy" } } },
            { "match": "\\b\\d++\\b", "name": "constant.numeric.toy" },
            { "begin": "\"", "end": "\"", "name": "string.quoted.double.toy", "patterns": [ { "match": "\\\\.", "name": "constant.character.escape.toy" } ] },
            { "begin": "<<(\\w+)", "end": "^\\1$", "name": "string.unquoted.heredoc.toy" },
            { "begin": "^```", "while": "^>", "name": "markup.raw.toy" },
            { "match": "\\Gfoo", "name": "keyword.toy" },
            { "include": "source.other#thing" }
          ],
          "repository": {
            "comments": { "patterns": [ { "match": "#.*$", "name": "comment.line.number-sign.toy" } ] }
          }
        }
        """;

    private static Tokenizer TokenizerOf(TextMateImportResult result, GrammarRegistry registry) => new(registry.Add(result.Json));

    private static List<string> Tokens(Tokenizer tokenizer, string line, ref LineState state)
    {
        var tokens = new List<Token>();
        state = tokenizer.TokenizeLine(line, state, tokens);
        return [.. tokens.Select(t => t.Kind + "=" + line.Substring(t.Start, t.Length))];
    }

    [Fact]
    public void A_json_grammar_is_converted_and_tokenized_by_the_own_engine_and_what_was_left_out_is_named()
    {
        string file = Path.Combine(_root, "toy.tmLanguage.json");
        File.WriteAllText(file, Toy);
        var registry = new GrammarRegistry(BuiltinGrammars.Registry);

        var result = Assert.Single(TextMateImport.FromPath(file, BuiltinGrammars.Registry));

        Assert.Equal(("toy", "Toy"), (result.Id, result.Name));
        Assert.Contains(result.Warnings, w => w.Contains(".lua") && w.Contains("built-in")); // the built-in Lua keeps its extension
        Assert.Contains(result.Warnings, w => w.Contains("while"));
        Assert.Contains(result.Warnings, w => w.Contains("source.other") && w.Contains("include"));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("\\G") || w.Contains("ossessive") || w.Contains("refers back"));

        var tokenizer = TokenizerOf(result, registry);
        Assert.Equal([".toy"], tokenizer.Grammar.Extensions);
        var state = tokenizer.InitialState;
        Assert.Equal(["comment=# note"], Tokens(tokenizer, "# note", ref state));
        Assert.Equal(["keyword=if", "number=12"], Tokens(tokenizer, "if 12", ref state));
        Assert.Equal(["keyword=def", "function=run"], Tokens(tokenizer, "def run", ref state)); // one rule per captured group
        Assert.Equal(["string=\"a", "string.escape=\\n", "string=b\""], Tokens(tokenizer, "\"a\\nb\"", ref state));

        Assert.Equal(["string=<<END"], Tokens(tokenizer, "<<END", ref state)); // a region goes on over the lines
        Assert.False(state.IsInitial);
        Assert.Equal(["string=END"], Tokens(tokenizer, "END", ref state)); // the end pattern \u0001 is read as a word
        Assert.True(state.IsInitial);
    }

    private static (Tokenizer Tokenizer, TextMateImportResult Result) Convert(string rules)
    {
        var result = Assert.Single(TextMateImport.Convert([("t.json", JsonNode.Parse($$"""{ "scopeName": "source.t", "patterns": [ {{rules}} ] }""")!.AsObject())], BuiltinGrammars.Registry));
        return (TokenizerOf(result, new GrammarRegistry(BuiltinGrammars.Registry)), result);
    }

    private static string Quote(string pattern) => JsonValue.Create(pattern)!.ToJsonString();

    private static string Match(string pattern) => $$"""{ "match": {{Quote(pattern)}}, "name": "keyword.t" }""";

    private static List<string> Run(string rules, params string[] lines)
    {
        var (tokenizer, _) = Convert(rules);
        var state = tokenizer.InitialState;
        var all = new List<string>();
        foreach (var line in lines) all.Add(string.Join(" ", Tokens(tokenizer, line, ref state)));
        return all;
    }

    [Fact]
    public void Groups_of_the_begin_and_end_patterns_are_coloured_like_the_groups_of_a_match()
    {
        string rule = """{ "begin": "(\\w+)(=)", "end": "(;)", "name": "string.quoted.t", "beginCaptures": { "1": { "name": "variable.other.t" }, "2": { "name": "keyword.operator.t" } }, "endCaptures": { "1": { "name": "punctuation.t" } } }""";
        Assert.Equal(["variable=x operator== string=abc punctuation=;"], Run(rule, "x=abc;"));
        string both = """{ "begin": "<(a)", "end": "(a)>", "name": "string.t", "captures": { "1": { "name": "keyword.t" } } }""";
        Assert.Equal(["string=< keyword=a string=b keyword=a string=>"], Run(both, "<aba>"));
        Assert.Empty(Convert(rule).Result.Warnings);
    }

    [Fact]
    public void A_possessive_quantifier_does_not_give_characters_back_and_an_interval_with_a_plus_repeats_the_interval()
    {
        Assert.Equal([""], Run(Match("^a*+a"), "aaa")); // greedy would match by giving one a back
        Assert.Equal(["keyword=ab"], Run(Match("^(?:a|b)++"), "ab"));
        Assert.Equal([""], Run(Match("^a++a"), "aaa"));
        Assert.Equal([""], Run(Match("^a?+a"), "a"));
        Assert.Equal([""], Run(Match(@"^\w*+\d"), "ab1"));
        Assert.Equal(["keyword=aaa"], Run(Match("^a{1,2}+"), "aaa")); // (?:a{1,2})+ in Oniguruma
        Assert.Equal(["keyword=\\++"], Run(Match(@"\\\++"), "\\++")); // an escaped plus before a quantifier is not possessive
    }

    [Fact]
    public void A_G_anchor_holds_where_the_scan_continues_after_the_last_token()
    {
        Assert.Equal(["keyword=xx"], Run(Match(@"\Gx"), "xxax"));
        Assert.Equal([""], Run(Match(@"\Gx"), "ax"));
        Assert.DoesNotContain(Convert(Match(@"\Gx")).Result.Warnings, w => w.Contains("\\G"));
    }

    [Fact]
    public void A_line_break_escape_is_atomic_and_knows_every_line_break_character()
    {
        foreach (var brk in new[] { "\u000B", "\u000C", "\u0085", "\u2028", "\u2029", "\r\n", "\r", "\n" })
            Assert.Equal(["keyword=a" + brk + "b"], Run(Match(@"^a\Rb$"), "a" + brk + "b"));
        Assert.Equal([""], Run(Match(@"^a\R\n"), "a\r\n")); // atomic: \R keeps the \r\n pair
        Assert.Equal(["keyword=a\r\n"], Run(Match(@"^a\R"), "a\r\n"));
    }

    [Theory]
    [InlineData(@"^\p{Word}+$", "a_b1", true)]
    [InlineData(@"^\P{Word}$", "_", false)]
    [InlineData(@"^[[:word:]]+$", "a_1", true)]
    [InlineData(@"^[[:alpha:]]+$", "éx", true)]
    [InlineData(@"^[[:alpha:]]$", "1", false)]
    [InlineData(@"^[[:punct:]]+$", "$+<=>^`|~!", true)]
    [InlineData(@"^[[:punct:]]$", "a", false)]
    [InlineData(@"^\p{Punct}$", "$", true)]
    [InlineData(@"^[[:space:]]$", "\u0085", true)]
    [InlineData(@"^[[:space:]]$", "\u00A0", true)]
    [InlineData(@"^[[:blank:]]$", "\n", false)]
    [InlineData(@"^[[:cntrl:]]$", "\u0001", true)]
    [InlineData(@"^[[:graph:]]+$", "ab!", true)]
    [InlineData(@"^[[:graph:]]$", " ", false)]
    [InlineData(@"^[[:print:]]$", " ", true)]
    [InlineData(@"^[[:print:]]$", "\u0001", false)]
    [InlineData(@"^[[:^alpha:]]$", "1", true)]
    [InlineData(@"^[[:^alpha:]]$", "a", false)]
    [InlineData(@"^[^[:alpha:]]$", "a", false)]
    [InlineData(@"^[^[:graph:]]$", " ", true)]
    [InlineData(@"^[[:alpha:][:digit:]_]+$", "a1_", true)]
    [InlineData(@"^[[:xdigit:]]+$", "09afAF", true)]
    [InlineData(@"^[[:xdigit:]]$", "g", false)]
    [InlineData(@"^[[:upper:]]$", "a", false)]
    [InlineData(@"^[\p{Alpha}\d]+$", "a1", true)]
    public void The_posix_classes_and_properties_follow_the_oniguruma_definitions(string pattern, string text, bool matches)
    {
        var (tokenizer, _) = Convert(Match(pattern));
        var tokens = new List<Token>();
        tokenizer.TokenizeLine(text, tokenizer.InitialState, tokens);
        Assert.Equal(matches, tokens.Count > 0);
    }

    [Fact]
    public void A_back_reference_in_the_end_pattern_ends_the_region_only_at_the_text_the_begin_pattern_matched()
    {
        string heredoc = """{ "begin": "<<(\\S+)", "end": "^\\1$", "name": "string.t" }""";
        Assert.Equal(["string=<<a.b", "string=aXb", "string=a.b"], Run(heredoc, "<<a.b", "aXb", "a.b")); // the dot is escaped
        var (tokenizer, _) = Convert(heredoc);
        var state = tokenizer.InitialState;
        Tokens(tokenizer, "<<END", ref state);
        Tokens(tokenizer, "END2", ref state);
        Assert.False(state.IsInitial);
        Tokens(tokenizer, "END", ref state);
        Assert.True(state.IsInitial);
        var first = tokenizer.InitialState;
        var second = tokenizer.InitialState;
        var third = tokenizer.InitialState;
        Tokens(tokenizer, "<<ONE", ref first);
        Tokens(tokenizer, "<<ONE", ref second);
        Tokens(tokenizer, "<<TWO", ref third);
        Assert.Same(first, second);
        Assert.NotSame(first, third);

        string named = """{ "begin": "(?<q>['\"])", "end": "\\k<q>", "name": "string.t" }""";
        Assert.Equal(["string=\"a'b\""], Run(named, "\"a'b\""));
        Assert.Equal(["string='a\"b'"], Run(named, "'a\"b'"));
    }

    [Fact]
    public void A_property_list_and_a_zipped_bundle_with_two_grammars_that_include_each_other_are_read()
    {
        string plist = """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0"><dict>
              <key>name</key><string>Outer</string>
              <key>scopeName</key><string>source.outer</string>
              <key>fileTypes</key><array><string>outer</string></array>
              <key>patterns</key><array>
                <dict><key>match</key><string>\bKEY\b</string><key>name</key><string>keyword.other.outer</string></dict>
                <dict><key>include</key><string>source.inner</string></dict>
              </array>
            </dict></plist>
            """;
        string inner = """{ "scopeName": "source.inner", "name": "Inner", "fileTypes": ["inner"], "patterns": [ { "match": "\\bword\\b", "name": "variable.other.inner" } ] }""";
        string bundle = Path.Combine(_root, "demo.tmbundle");
        using (var zip = ZipFile.Open(bundle, ZipArchiveMode.Create))
        {
            foreach (var (name, text) in new[] { ("Demo.tmbundle/Syntaxes/Outer.tmLanguage", plist), ("Demo.tmbundle/Syntaxes/Inner.tmLanguage.json", inner) })
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(text);
            }
        }
        var registry = new GrammarRegistry(BuiltinGrammars.Registry);

        var results = TextMateImport.FromPath(bundle, BuiltinGrammars.Registry);

        Assert.Equal(["inner", "outer"], results.Select(r => r.Id).Order());
        foreach (var r in results) registry.Add(r.Json);
        var outer = new Tokenizer(registry.Find("outer")!);
        var state = outer.InitialState;
        Assert.Equal(["keyword=KEY", "variable=word"], Tokens(outer, "KEY word", ref state)); // the include of source.inner reached the other grammar
        Assert.Empty(results.Single(r => r.Id == "outer").Warnings);
    }

    [Fact]
    public void A_folder_bundle_works_and_a_file_that_is_not_a_grammar_is_refused_with_the_reason()
    {
        Directory.CreateDirectory(Path.Combine(_root, "b.tmbundle", "Syntaxes"));
        File.WriteAllText(Path.Combine(_root, "b.tmbundle", "Syntaxes", "x.tmLanguage.json"), """{ "scopeName": "source.xlang", "patterns": [ { "match": "x+", "name": "string.xlang" } ] }""");
        Assert.Equal("xlang", Assert.Single(TextMateImport.FromPath(Path.Combine(_root, "b.tmbundle"), BuiltinGrammars.Registry)).Id);

        string bad = Path.Combine(_root, "bad.tmLanguage.json");
        File.WriteAllText(bad, """{ "name": "no scope" }""");
        Assert.Contains("scopeName", Assert.Throws<TextMateImportException>(() => TextMateImport.FromPath(bad, BuiltinGrammars.Registry)).Message);
        File.WriteAllText(bad, "{ not json");
        Assert.Throws<TextMateImportException>(() => TextMateImport.FromPath(bad, BuiltinGrammars.Registry));
        Assert.Throws<TextMateImportException>(() => TextMateImport.FromPath(_root, BuiltinGrammars.Registry)); // a folder with nothing in it
    }

    [Theory]
    [InlineData("keyword.control.flow.js", "keyword")]
    [InlineData("keyword.operator.assignment", "operator")]
    [InlineData("constant.numeric.hex", "number")]
    [InlineData("constant.character.escape.c", "string.escape")]
    [InlineData("string.quoted.double", "string")]
    [InlineData("entity.name.function.python", "function")]
    [InlineData("storage.type.class", "type")]
    [InlineData("comment.line.double-slash", "comment")]
    [InlineData("meta.function.body", null)]
    [InlineData("source.toy", null)]
    public void Scopes_become_token_kinds_by_their_prefix(string scope, string? kind) => Assert.Equal(kind, TextMateImport.KindOf(scope));

    /// <summary>With TEXTIDA_TEST_TM_DIR set to a folder with real grammars (Perl, R, SCSS of a public editor), they are converted and tokenized, and typing stays fast.</summary>
    [Fact]
    public void Real_grammars_convert_without_failing_and_tokenize_a_line_in_well_under_a_millisecond()
    {
        if (Environment.GetEnvironmentVariable("TEXTIDA_TEST_TM_DIR") is not { } dir || !Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            var result = Assert.Single(TextMateImport.FromPath(file, BuiltinGrammars.Registry));
            var registry = new GrammarRegistry(BuiltinGrammars.Registry);
            var tokenizer = TokenizerOf(result, registry);
            Assert.True(JsonNode.Parse(result.Json) is JsonObject);
            string line = "my $name = shift @ARGV; print \"Hello, $name\\n\" if $x > 10; # done  .a { color: red; margin: 0 auto; }";
            var state = tokenizer.InitialState;
            Tokens(tokenizer, line, ref state); // warm up
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 2000; i++)
            {
                state = tokenizer.InitialState;
                tokenizer.TokenizeLine(line, state, []);
            }
            Assert.True(sw.Elapsed.TotalMilliseconds / 2000 < 5, $"{result.Id}: {sw.Elapsed.TotalMilliseconds / 2000:0.00} ms per line");
            Console.WriteLine($"{result.Id}: {sw.Elapsed.TotalMilliseconds / 2000:0.000} ms per line, {result.Warnings.Count} warnings");
        }
    }
}
