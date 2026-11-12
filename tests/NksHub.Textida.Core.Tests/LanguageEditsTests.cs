using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.Core.Tests;

public class LanguageEditsTests
{
    private static string[] Apply(string[] lines, IReadOnlyList<LineEdit> edits)
    {
        var copy = (string[])lines.Clone();
        foreach (var e in edits.Reverse()) copy[e.Line] = copy[e.Line].Remove(e.Column, e.Length).Insert(e.Column, e.Text);
        return copy;
    }

    private static string[] Toggle(string grammar, string[] lines, params (int, int)[] ranges) =>
        Apply(lines, CommentToggle.Toggle(lines, BuiltinGrammars.Tokenizer(grammar), ranges));

    [Fact]
    public void Several_selections_in_html_use_the_marker_of_html_javascript_and_css_and_toggle_back()
    {
        string[] lines = ["<p>a</p>", "<script>", "  var x = 1;", "</script>", "<style>", "a { color: red; }", "</style>"];
        var commented = Toggle("html", lines, (0, 0), (2, 2), (5, 5));
        Assert.Equal(["<!-- <p>a</p> -->", "<script>", "  // var x = 1;", "</script>", "<style>", "/* a { color: red; } */", "</style>"], commented);
        Assert.Equal(lines, Toggle("html", commented, (0, 0), (2, 2), (5, 5)));
    }

    [Fact]
    public void Php_code_gets_slashes_and_the_html_around_it_html_comments()
    {
        string[] lines = ["<div>", "<?php", "echo 1;", "?>"];
        Assert.Equal(["<!-- <div> -->", "<?php", "// echo 1;", "?>"], Toggle("php", lines, (0, 0), (2, 2)));
    }

    [Fact]
    public void Css_and_json_like_languages_use_their_markers()
    {
        Assert.Equal(["/* a {} */"], Toggle("css", ["a {}"], (0, 0)));
        Assert.Equal(["# a: 1"], Toggle("neon", ["a: 1"], (0, 0)));
        Assert.Equal(["{", "  // \"a\": 1", "}"], Toggle("json", ["{", "  \"a\": 1", "}"], (1, 1)));
    }

    [Fact]
    public void A_range_with_an_uncommented_line_is_commented_at_the_smallest_indent_and_blank_lines_stay()
    {
        string[] lines = ["    // a", "", "  b"];
        Assert.Equal(["  //   // a", "", "  // b"], Toggle("javascript", lines, (0, 2)));
        Assert.Equal(["    a", "", "  b"], Toggle("javascript", ["    // a", "", "  // b"], (0, 2)));
    }

    [Fact]
    public void Without_a_grammar_nothing_changes()
    {
        Assert.Empty(CommentToggle.Toggle(["x"], null, [(0, 0)]));
        Assert.Empty(CommentToggle.Toggle(["x"], BuiltinGrammars.Tokenizer("plaintext"), [(0, 0)]));
    }

    [Theory]
    [InlineData("plaintext", false)]
    [InlineData("csharp", true)]
    [InlineData("json", true)]
    [InlineData("xml", true)]
    [InlineData("html", true)]
    [InlineData("markdown", true)]
    public void Availability_uses_grammar_comment_markers(string id, bool expected) =>
        Assert.Equal(expected, CommentToggle.Supports(BuiltinGrammars.Registry.Find(id)));

    [Fact]
    public void A_markerless_host_can_comment_an_embedded_language_reached_through_cyclic_includes()
    {
        var registry = new GrammarRegistry();
        registry.Add("""{ "formatVersion": 1, "id": "guest", "comments": { "line": "//" }, "rules": [] }""");
        var host = registry.Add("""
            { "formatVersion": 1, "id": "host", "rules": [ { "include": "#body" } ],
              "repository": { "body": [ { "include": "#body" },
                { "begin": "<g>", "end": "</g>", "multiline": true, "embed": "guest" } ] } }
            """);
        registry.Validate();
        Assert.True(CommentToggle.Supports(host));
        string[] lines = ["outside", "<g>", "  value", "</g>"];
        Assert.Equal(["outside", "<g>", "  // value", "</g>"], Apply(lines, CommentToggle.Toggle(lines, new Tokenizer(host), [(2, 2)])));
    }

    [Fact]
    public void Included_matches_and_unused_repository_contexts_do_not_switch_the_language()
    {
        var registry = new GrammarRegistry();
        registry.Add("""{ "formatVersion": 1, "id": "guest", "comments": { "line": "//" }, "rules": [ { "match": "word", "token": "keyword" } ] }""");
        var host = registry.Add("""
            { "formatVersion": 1, "id": "host", "rules": [ { "include": "#loop" } ],
              "repository": { "loop": [ { "include": "#loop" }, { "include": "guest" } ],
                "unused": [ { "begin": "<g>", "end": "</g>", "embed": "guest" } ] } }
            """);
        registry.Validate();
        Assert.False(CommentToggle.Supports(host));
        registry.Add("""{ "formatVersion": 1, "id": "guest", "comments": { "line": "//" }, "rules": [ { "begin": "<q>", "end": "</q>" } ] }""");
        Assert.True(CommentToggle.Supports(host));
    }

    [Theory]
    [InlineData("""{ "include": "missing" }""")]
    [InlineData("""{ "include": "#missing" }""")]
    [InlineData("""{ "begin": "<g>", "end": "</g>", "embed": "missing" }""")]
    public void Unresolved_dependencies_do_not_throw_from_the_availability_query(string rule)
    {
        var grammar = new GrammarRegistry().Add("""{ "formatVersion": 1, "id": "host", "rules": [""" + rule + "] }");
        Assert.False(CommentToggle.Supports(grammar));
        Assert.Throws<GrammarException>(grammar.Validate);
        Assert.False(CommentToggle.Supports(null));
    }

    [Fact]
    public void Availability_follows_added_and_replaced_embedded_grammars()
    {
        var registry = new GrammarRegistry();
        var host = registry.Add("""{ "formatVersion": 1, "id": "host", "rules": [ { "begin": "<g>", "end": "</g>", "embed": "guest" } ] }""");
        Assert.False(CommentToggle.Supports(host));
        registry.Add("""{ "formatVersion": 1, "id": "guest", "comments": { "line": "//" }, "rules": [] }""");
        Assert.True(CommentToggle.Supports(host));
        registry.Add("""{ "formatVersion": 1, "id": "guest", "rules": [] }""");
        Assert.False(CommentToggle.Supports(host));
    }

    private static (TextPosition, TextPosition) P(int l1, int c1, int l2, int c2) => (new TextPosition(l1, c1), new TextPosition(l2, c2));

    [Fact]
    public void Expanding_inside_brackets_takes_the_word_the_content_the_brackets_and_then_the_next_pair_out()
    {
        string[] lines = ["f(a, [b + c]);"];
        var tokenizer = BuiltinGrammars.Tokenizer("javascript");
        var steps = new List<(TextPosition, TextPosition)>();
        var sel = P(0, 6, 0, 6);
        for (int i = 0; i < 7; i++)
        {
            sel = SelectionGrowth.Expand(lines, tokenizer, sel.Item1, sel.Item2);
            steps.Add(sel);
        }
        Assert.Equal([P(0, 6, 0, 7), P(0, 6, 0, 11), P(0, 5, 0, 12), P(0, 2, 0, 12), P(0, 1, 0, 13), P(0, 0, 0, 14), P(0, 0, 0, 14)], steps);
    }

    [Fact]
    public void A_bracket_in_a_string_does_not_count_and_pairs_span_lines()
    {
        var js = BuiltinGrammars.Tokenizer("javascript");
        Assert.Equal(P(0, 2, 0, 8), SelectionGrowth.Expand(["f(\"(\", x)"], js, new(0, 7), new(0, 8)));
        Assert.Equal(P(0, 1, 2, 0), SelectionGrowth.Expand(["{", "  a", "}"], js, new(1, 2), new(1, 3)));
        Assert.Equal(P(0, 0, 2, 1), SelectionGrowth.Expand(["{", "  a", "}"], js, new(0, 1), new(2, 0)));
    }
}
