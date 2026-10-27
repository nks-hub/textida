using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Core.Tests;

/// <summary>P4.6.3 and P4.7.2 in the core: which dialect a document has, how a dialect colors, and how help for the word under the caret is resolved.</summary>
public class SqlDialectAndLanguageHelpTests
{
    [Fact]
    public void The_first_known_dialect_of_file_folder_and_global_wins_and_nothing_set_is_generic()
    {
        Assert.Equal("generic", SqlDialects.Resolve(null, null, null));
        Assert.Equal("mysql", SqlDialects.Resolve(null, null, "mysql"));
        Assert.Equal("sqlite", SqlDialects.Resolve(null, "sqlite", "mysql"));
        Assert.Equal("tsql", SqlDialects.Resolve("TSQL", "sqlite", "mysql"));
        Assert.Equal("mysql", SqlDialects.Resolve("oracle", "", "mysql")); // an unknown name is passed over, never an error
        Assert.Equal("generic", SqlDialects.Resolve("oracle", "db2", null));
        Assert.Equal(["generic", "mysql", "postgres", "sqlite", "tsql"], SqlDialects.All.Select(d => d.Id));
    }

    private static string[] KindsOf(string dialect, string line)
    {
        var tokenizer = BuiltinGrammars.Tokenizer(SqlDialects.GrammarId(dialect));
        var tokens = new List<Token>();
        tokenizer.TokenizeLine(line, tokenizer.InitialState, tokens);
        return [.. tokens.Select(t => $"{line.Substring(t.Start, t.Length)}:{t.Kind}")];
    }

    [Fact]
    public void A_dialect_colors_its_own_words_and_the_generic_one_does_not()
    {
        Assert.DoesNotContain("auto_increment:keyword", KindsOf("generic", "id int auto_increment"));
        Assert.Contains("auto_increment:keyword", KindsOf("mysql", "id int auto_increment"));
        Assert.Contains("pragma:keyword", KindsOf("sqlite", "pragma foreign_keys"));
        Assert.DoesNotContain("pragma:keyword", KindsOf("postgres", "pragma foreign_keys"));
        Assert.Contains("[a b]:variable", KindsOf("tsql", "select [a b]"));
        Assert.DoesNotContain("[a b]:variable", KindsOf("generic", "select [a b]"));
        Assert.Contains("# note:comment", KindsOf("mysql", "# note"));
        Assert.DoesNotContain("# note:comment", KindsOf("generic", "# note"));
        // what every dialect shares stays
        foreach (var d in SqlDialects.All) Assert.Contains("select:keyword", KindsOf(d.Id, "select 1"));
    }

    [Fact]
    public void Dialect_grammars_do_not_take_over_the_file_extension()
    {
        Assert.Equal("sql", BuiltinGrammars.Registry.ForFile("a.sql")!.Id);
    }

    [Fact]
    public void The_word_at_the_caret_is_found_inside_at_the_end_and_not_in_white_space()
    {
        Assert.Equal("array_map", LanguageHelp.WordAt("$x = array_map($f);", 8));
        Assert.Equal("array_map", LanguageHelp.WordAt("$x = array_map($f);", 5));
        Assert.Equal("array_map", LanguageHelp.WordAt("$x = array_map($f);", 14)); // right after the word
        Assert.Equal("f", LanguageHelp.WordAt("$x = array_map($f);", 16));
        Assert.Null(LanguageHelp.WordAt("a  b", 2));
        Assert.Null(LanguageHelp.WordAt("   ", 1));
        Assert.Null(LanguageHelp.WordAt("", 0));
        Assert.Equal("é1", LanguageHelp.WordAt("é1", 1));
    }

    [Fact]
    public void An_address_is_built_from_the_template_and_only_http_and_https_are_accepted()
    {
        HelpSource Url(string target) => new("a", "A", "php", HelpKind.Url, target);
        Assert.Equal("https://docs.example.org/php/array_map.html", LanguageHelp.UrlFor(Url("https://docs.example.org/${language}/${word}.html"), "array_map")!.AbsoluteUri);
        Assert.Equal("http://example.org/?q=a%26b%20c", LanguageHelp.UrlFor(Url("http://example.org/?q=${word}"), "a&b c")!.AbsoluteUri);
        Assert.Null(LanguageHelp.UrlFor(Url("ftp://example.org/${word}"), "x"));
        Assert.Null(LanguageHelp.UrlFor(Url("javascript:alert(1)"), "x"));
        Assert.Null(LanguageHelp.UrlFor(Url("file:///c:/x/${word}"), "x"));
        Assert.Null(LanguageHelp.UrlFor(Url("not an address"), "x"));
        Assert.Null(LanguageHelp.UrlFor(new HelpSource("f", "F", "php", HelpKind.File, "c:/x.md"), "x"));
    }

    [Fact]
    public void Sources_are_chosen_by_the_language_without_regard_to_case()
    {
        HelpSource S(string id, string language) => new(id, id, language, HelpKind.Url, "https://example.org/");
        var all = new[] { S("a", "php"), S("b", "CSS"), S("c", "PHP") };
        Assert.Equal(["a", "c"], LanguageHelp.For(all, "php").Select(s => s.Id));
        Assert.Equal(["b"], LanguageHelp.For(all, "css").Select(s => s.Id));
        Assert.Empty(LanguageHelp.For(all, "html"));
    }
}
