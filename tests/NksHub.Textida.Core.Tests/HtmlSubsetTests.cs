using System.Diagnostics;
using NksHub.Textida.Core.Markdown;

namespace NksHub.Textida.Core.Tests;

/// <summary>P9.3.1: the HTML subset the preview reads. Everything it does not render stays in the tree as literal text, so nothing is lost and nothing is acted on.</summary>
public class HtmlSubsetTests
{
    private static IReadOnlyList<HtmlNode> Parse(string html, int maxNodes = 1000, int maxDepth = 32) => HtmlSubset.Parse(html, maxNodes, maxDepth);

    /// <summary>text as 'text', literal text as [text], an element as tag(children)</summary>
    private static string Shape(IEnumerable<HtmlNode> nodes) =>
        string.Concat(nodes.Select(n => n.Tag is null ? (n.Literal ? "[" + n.Text + "]" : "'" + n.Text + "'") : n.Tag + "(" + Shape(n.Children) + ")"));

    private static string Everything(IEnumerable<HtmlNode> nodes) => string.Concat(nodes.Select(n => n.TextContent()));

    [Fact]
    public void Headings_paragraphs_emphasis_links_and_lists_become_elements()
    {
        var nodes = Parse("<h1>Title</h1><p>One <b>two</b> <a href=\"https://x.example/a\">three</a></p><ul><li>a<li>b</ul>");
        Assert.Equal("h1('Title')p('One 'b('two')' 'a('three'))ul(li('a')li('b'))", Shape(nodes));
        Assert.Equal("https://x.example/a", nodes[1].Children[3].Attribute("href"));
    }

    [Fact]
    public void A_script_element_is_one_literal_text_and_what_is_inside_it_is_not_read_as_tags()
    {
        var nodes = Parse("<script>if (a<b) alert(\"<b>x</b>\")</script><p>after</p>");
        Assert.Equal("[<script>if (a<b) alert(\"<b>x</b>\")</script>]p('after')", Shape(nodes));
        Assert.Equal("[<style>p { color: red }</style>]", Shape(Parse("<style>p { color: red }</style>")));
        Assert.Equal("[<SCRIPT src=x>alert(1)</SCRIPT>]", Shape(Parse("<SCRIPT src=x>alert(1)</SCRIPT>")));
        Assert.Equal("[<script>never closed <b>x</b>]", Shape(Parse("<script>never closed <b>x</b>")));
    }

    [Fact]
    public void Unknown_tags_stray_end_tags_comments_and_declarations_stay_as_written()
    {
        var nodes = Parse("<!DOCTYPE html><div class=\"a\">text</div><!-- note --></p><iframe src=\"https://x.example\"></iframe>");
        Assert.Equal("[<!DOCTYPE html>][<div class=\"a\">]'text'[</div>][<!-- note -->][</p>][<iframe src=\"https://x.example\">][</iframe>]", Shape(nodes));
    }

    [Fact]
    public void The_implied_end_tags_of_paragraphs_list_items_and_table_cells_are_inferred()
    {
        Assert.Equal("p('one')p('two')table(tr(td('a')td('b'))tr(td('c')))", Shape(Parse("<p>one<p>two<table><tr><td>a<td>b<tr><td>c</table>")));
        Assert.Equal("ul(li('a'ul(li('b')))li('c'))", Shape(Parse("<ul><li>a<ul><li>b</ul><li>c</ul>")));
        Assert.Equal("table(thead(tr(th('h')))tbody(tr(td('d'))))", Shape(Parse("<table><thead><tr><th>h<tbody><tr><td>d</table>")));
    }

    [Fact]
    public void Void_tags_have_no_children_and_a_start_tag_with_a_slash_is_still_a_start_tag()
    {
        Assert.Equal("p('a'br()'b'img())hr()", Shape(Parse("<p>a<br/>b<img src=\"x.png\" alt=\"x\" /></p><hr>")));
        Assert.Equal("p('a')hr()[</p>]", Shape(Parse("<p>a<hr></p>"))); // a rule ends the paragraph, so the end tag that follows has nothing to close
        Assert.Equal("x.png", Parse("<img src=\"x.png\">")[0].Attribute("src"));
    }

    [Fact]
    public void Attributes_are_read_with_either_quote_or_none_and_found_without_regard_to_case()
    {
        var a = Parse("<a href=\"x y\" title='t' data-n=5 disabled HREF2=z>link</a>")[0];
        Assert.Equal("x y", a.Attribute("href"));
        Assert.Equal("x y", a.Attribute("HREF"));
        Assert.Equal("t", a.Attribute("title"));
        Assert.Equal("5", a.Attribute("data-n"));
        Assert.Equal("", a.Attribute("disabled"));
        Assert.Equal("z", a.Attribute("href2"));
        Assert.Equal("", a.Attribute("missing"));
    }

    [Fact]
    public void Entities_are_decoded_once_in_text_and_attributes_but_not_in_literal_text()
    {
        var p = Parse("<p title=\"a &amp; b\">x &lt; y &amp;amp; z</p>")[0];
        Assert.Equal("a & b", p.Attribute("title"));
        Assert.Equal("x < y &amp; z", p.TextContent());
        Assert.Equal("[<div title=\"&amp;\">]", Shape(Parse("<div title=\"&amp;\">")));
    }

    [Fact]
    public void A_lone_angle_bracket_is_text_and_a_tag_that_never_ends_is_literal()
    {
        Assert.Equal("'a < b'", Shape(Parse("a < b")));
        Assert.Equal("'text '[<b onclick=\"x]", Shape(Parse("text <b onclick=\"x")));
        Assert.Equal("'1 <2 and 3> 2'", Shape(Parse("1 <2 and 3> 2")));
    }

    [Fact]
    public void Nodes_know_the_line_where_they_start()
    {
        var nodes = Parse("<p>a</p>\n<h2>b</h2>\n\n<ul><li>c</li></ul>");
        Assert.Equal([0, 1, 3], nodes.Where(n => n.Tag is not null).Select(n => n.Line));
    }

    [Fact]
    public void A_tag_nested_deeper_than_the_limit_stays_text_and_nothing_is_lost()
    {
        var nodes = Parse("<ul><ul><ul><li>x</li></ul></ul></ul>", 100, maxDepth: 2);
        Assert.Equal("ul(ul([<ul>][<li>]'x'[</li>]))[</ul>]", Shape(nodes));
        Assert.Equal("<ul><li>x</li></ul>", Everything(nodes));
    }

    [Fact]
    public void After_the_node_limit_the_rest_of_the_text_is_kept_as_written()
    {
        var nodes = Parse("<p>a</p><p>b</p><p>c</p>", maxNodes: 3);
        Assert.Equal("ab</p><p>c</p>", Everything(nodes));
        Assert.True(nodes.Count <= 3);
    }

    [Fact]
    public void Many_lone_angle_brackets_are_read_in_linear_time()
    {
        // every lone '<' joins the text before it; copying that text again for each one made a 480 KB page take many seconds on the UI thread
        string html = string.Concat(Enumerable.Repeat("a < ", 120_000));
        var timer = Stopwatch.StartNew();
        var nodes = Parse(html, 20_000);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(3), "Reading took " + timer.Elapsed);
        Assert.Equal(html, Everything(nodes));
        Assert.Single(nodes);
    }

    [Fact]
    public void An_attribute_that_would_run_or_fetch_is_only_data_in_the_node()
    {
        var img = Parse("<img src=\"https://x.example/a.png\" onerror=\"alert(1)\">")[0];
        Assert.Equal("img", img.Tag);
        Assert.Empty(img.Children);
        Assert.Equal("alert(1)", img.Attribute("onerror"));
    }
}
