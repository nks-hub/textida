using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.Core.Tests;

public class LanguageEditsTests
{
    private static string[] Apply(string[] lines, IReadOnlyList<LineReplacement> edits)
    {
        var copy = (string[])lines.Clone();
        foreach (var e in edits) copy[e.Line] = e.Text;
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
