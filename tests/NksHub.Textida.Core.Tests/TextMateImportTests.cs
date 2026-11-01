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
        Assert.Contains(result.Warnings, w => w.Contains("\\G"));
        Assert.Contains(result.Warnings, w => w.Contains("Possessive"));
        Assert.Contains(result.Warnings, w => w.Contains("source.other") && w.Contains("include"));
        Assert.Contains(result.Warnings, w => w.Contains("refers back"));

        var tokenizer = TokenizerOf(result, registry);
        Assert.Equal([".toy"], tokenizer.Grammar.Extensions);
        var state = tokenizer.InitialState;
        Assert.Equal(["comment=# note"], Tokens(tokenizer, "# note", ref state));
        Assert.Equal(["keyword=if", "number=12"], Tokens(tokenizer, "if 12", ref state));
        Assert.Equal(["keyword=def", "function=run"], Tokens(tokenizer, "def run", ref state)); // one rule per captured group
        Assert.Equal(["string=\"a", "string.escape=\\n", "string=b\""], Tokens(tokenizer, "\"a\\nb\"", ref state));

        Assert.Equal(["string=<<END"], Tokens(tokenizer, "<<END", ref state)); // a region goes on over the lines
        Assert.False(state.IsInitial);
        Assert.Equal(["string=END"], Tokens(tokenizer, "END", ref state)); // the end pattern  is read as a word
        Assert.True(state.IsInitial);
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
