using NksHub.Textida.Core.Snippets;
using NksHub.Textida.Plugins.Emmet;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P4.5.2: the abbreviation engine of the Emmet plugin (HTML and CSS), the search for the abbreviation in front of the caret and the wrapping of text.</summary>
public class EmmetExpandTests
{
    private static ExpandOptions Options(string baseIndent = "", int maxChars = 10_000, int maxCarets = 64) => new("\t", baseIndent, maxChars, maxCarets);

    private static Expansion Html(string abbreviation, string baseIndent = "") => HtmlExpander.Expand(abbreviation, Options(baseIndent)) ?? throw new Xunit.Sdk.XunitException($"'{abbreviation}' is not an abbreviation");

    private static string Marked(Expansion e) => string.Concat(e.Carets.Reverse().Aggregate(e.Text, (text, at) => text.Insert(at, "|")));

    [Fact]
    public void A_list_with_three_items_has_a_caret_inside_every_item()
    {
        var e = Html("ul>li*3");
        Assert.Equal("<ul>\n\t<li></li>\n\t<li></li>\n\t<li></li>\n</ul>", e.Text);
        Assert.Equal("<ul>\n\t<li>|</li>\n\t<li>|</li>\n\t<li>|</li>\n</ul>", Marked(e));
    }

    [Fact]
    public void Ids_classes_attributes_text_and_numbering_follow_the_syntax()
    {
        Assert.Equal("<div id=\"page\" class=\"wrap big\">\n\t<p class=\"a1\"></p>\n\t<p class=\"a2\"></p>\n\t<a href=\"x y\" title=\"t\">Go</a>\n</div>",
            Html("div#page.wrap.big>p.a$*2+a[href=\"x y\" title=t]{Go}").Text);
        Assert.Equal("<ul>\n\t<li class=\"n03\"></li>\n\t<li class=\"n02\"></li>\n\t<li class=\"n01\"></li>\n</ul>", Html("ul>li.n$$@-*3").Text);
        Assert.Equal("<h1></h1>\n<h2></h2>", Html("h$*2").Text);
    }

    [Fact]
    public void Inline_children_stay_on_the_line_of_their_parent_and_block_children_do_not()
    {
        Assert.Equal("<p><a href=\"\"></a></p>", Html("p>a").Text);
        Assert.Equal("<p><a href=\"\"></a><b></b></p>", Html("p>a+b").Text);
        Assert.Equal("<ul>\n\t<li><a href=\"\"></a></li>\n</ul>", Html("ul>li>a").Text);
        Assert.Equal("<div>\n\t<p></p>\n\t<a href=\"\"></a>\n</div>", Html("div>p+a").Text);
        Assert.Equal("<p><a href=\"\">|</a></p>", Marked(Html("p>a")));
    }

    [Fact]
    public void Implied_names_groups_and_climbing_work()
    {
        Assert.Equal("<div class=\"box\"></div>", Html(".box").Text);
        Assert.Equal("<ul>\n\t<li class=\"x\"></li>\n</ul>", Html("ul>.x").Text);
        Assert.Equal("<table>\n\t<tr class=\"row\">\n\t\t<td class=\"col\"></td>\n\t</tr>\n</table>", Html("table>.row>.col").Text);
        Assert.Equal("<em><span class=\"s\"></span></em>", Html("em>.s").Text);
        Assert.Equal("<h1></h1>\n<p></p>\n<h1></h1>\n<p></p>", Html("(h1+p)*2").Text);
        Assert.Equal("<div>\n\t<p><span></span></p>\n\t<h1></h1>\n</div>", Html("div>p>span^h1").Text);
    }

    [Fact]
    public void Known_aliases_give_their_default_attributes_and_void_elements_have_no_closing_tag()
    {
        Assert.Equal("<img src=\"\" alt=\"\">", Html("img").Text);
        Assert.Equal("<img src=\"\" alt=\"\">", Html("img").Text);
        Assert.Equal("<input type=\"checkbox\" name=\"\" id=\"\">", Html("input:checkbox").Text);
        Assert.Equal("<input type=\"checkbox\" name=\"|\" id=\"\">", Marked(Html("input:checkbox")));
        Assert.Equal("<a href=\"x\"></a>", Html("a[href=x]").Text);
        Assert.Equal("<blockquote></blockquote>", Html("bq").Text);
        Assert.Equal("<link rel=\"stylesheet\" href=\"style.css\">", Html("link:css").Text);
        Assert.Equal("<br>", Html("br").Text);
    }

    [Fact]
    public void Later_lines_start_with_the_indentation_of_the_line_and_the_carets_follow()
    {
        var e = Html("ul>li*2", baseIndent: "  ");
        Assert.Equal("<ul>\n  \t<li></li>\n  \t<li></li>\n  </ul>", e.Text);
        Assert.All(e.Carets, at => Assert.True(e.Text[..at].EndsWith("<li>") && e.Text[at..].StartsWith("</li>")));
    }

    [Fact]
    public void The_html5_skeleton_has_its_caret_in_the_body()
    {
        var e = Html("!");
        Assert.StartsWith("<!DOCTYPE html>\n<html lang=\"\">\n<head>", e.Text);
        Assert.Contains("<body>\n\t\n</body>", e.Text);
        Assert.EndsWith("<body>\n\t", e.Text[..e.Carets.Single()]);
        Assert.Equal(Html("!").Text, Html("html:5").Text);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("10")]
    [InlineData("ul>")]
    [InlineData("a>>b")]
    [InlineData("li*0")]
    [InlineData("div[")]
    [InlineData("(a+b)>c")]
    [InlineData("a^b")]
    [InlineData("li*")]
    [InlineData("br>a")]
    [InlineData("a b")]
    public void Text_that_is_not_an_abbreviation_is_left_alone(string text) => Assert.Null(HtmlExpander.Expand(text, Options()));

    [Fact]
    public void A_plain_word_is_an_element_only_when_it_is_a_known_name()
    {
        Assert.NotNull(HtmlExpander.Expand("section", Options()));
        Assert.Null(HtmlExpander.Expand("section2", Options()));
        Assert.NotNull(HtmlExpander.Expand("my-element.a", Options()));
    }

    [Fact]
    public void An_expansion_longer_than_the_limit_is_refused_before_it_is_built()
    {
        Assert.Throws<ExpansionTooLargeException>(() => HtmlExpander.Expand("div*100000", Options(maxChars: 1000)));
        Assert.Throws<ExpansionTooLargeException>(() => HtmlExpander.Expand("div*1000>div*1000>div*1000", Options(maxChars: 1000)));
    }

    [Fact]
    public void Empty_attribute_values_without_a_caret_are_the_later_tab_stops_in_text_order()
    {
        var link = Html("a");
        Assert.Equal("<a href=\"\">|</a>", Marked(link));
        Assert.Equal(["<a href=\"".Length], link.Stops!);

        // the caret takes the first empty value, the others follow
        var image = Html("img");
        Assert.Equal("<img src=\"|\" alt=\"\">", Marked(image));
        Assert.Equal(["<img src=\"\" alt=\"".Length], image.Stops!);
        Assert.Empty(Html("ul>li*3").Stops!);
        Assert.Equal(["<a href=\"".Length], HtmlExpander.Wrap("a", "x", Options())!.Stops!);

        var page = Html("!");
        Assert.EndsWith("<title>", page.Text[..page.Stops!.Single()]);
    }

    [Fact]
    public void The_carets_are_cut_to_the_limit()
    {
        var e = HtmlExpander.Expand("ul>li*5", Options(maxCarets: 2))!;
        Assert.Equal(2, e.Carets.Count);
        Assert.Equal(2, e.Places(64).Count);
        Assert.Single(e.Places(1));
    }

    [Fact]
    public void The_places_are_the_carets_then_the_stops_and_the_end_of_the_text_is_no_place()
    {
        var link = Html("a");
        Assert.Equal(["<a href=\"\">".Length, "<a href=\"".Length], link.Places(64));
        Assert.Empty(Html("br").Places(64));
        Assert.Empty(HtmlExpander.Wrap("p", "x", Options())!.Places(64));
        Assert.Empty(CssExpander.Expand("m10", Options())!.Places(64));
        Assert.Equal(2, CssExpander.Expand("c+m", Options())!.Places(64).Count);
    }

    [Fact]
    public void The_template_numbers_the_places_in_the_order_Tab_visits_them()
    {
        Assert.Equal("<ul>\n\t<li>${1}</li>\n\t<li>${2}</li>\n\t<li>${3}</li>\n</ul>", Html("ul>li*3").Template(64, 3));
        Assert.Equal("<a href=\"${2}\">${1}</a>", Html("a").Template(64, 2));
        Assert.Equal("margin: ${1};", CssExpander.Expand("m", Options())!.Template(64, 1));
    }

    [Fact]
    public void An_expansion_without_a_place_has_no_stop_and_one_with_fewer_places_than_the_others_is_padded_at_its_end()
    {
        Assert.Equal("<br>", Html("br").Template(64, 3));
        Assert.Equal("<p>${1}</p>${2}${3}", Html("p").Template(64, 3));
    }

    [Fact]
    public void A_dollar_sign_and_a_backslash_of_the_text_survive_the_trip_through_the_snippet_syntax()
    {
        var wrapped = HtmlExpander.Wrap("a", "cost $5 and ${name} in C:\\dir\\$1 $$", Options())!;
        var template = wrapped.Template(64, 1);
        Assert.Contains("\\$5", template);
        var parsed = SnippetParser.Parse(template);
        Assert.Empty(parsed.Missing);
        Assert.Equal(wrapped.Text, parsed.Text);
        Assert.Equal(wrapped.Places(64), parsed.Stops.Select(s => Assert.Single(s.Spans).Start));
        Assert.Equal(parsed.Text.Length, parsed.FinalOffset);
    }

    [Fact]
    public void Wrapping_puts_the_text_into_the_innermost_last_element_and_a_bare_star_repeats_it_per_line()
    {
        Assert.Equal("<div class=\"box\">x</div>", HtmlExpander.Wrap("div.box", "x", Options())!.Text);
        Assert.Equal("<div class=\"box\">\n\tone\n\ttwo\n</div>", HtmlExpander.Wrap("div.box", "one\ntwo", Options())!.Text);
        Assert.Equal("<ul>\n\t<li>a</li>\n\t<li>b</li>\n</ul>", HtmlExpander.Wrap("ul>li*", "a\n\n  b  \n", Options())!.Text);
        Assert.Equal("<div><span>t</span></div>", HtmlExpander.Wrap("div>span", "t", Options())!.Text);
        var wrapped = HtmlExpander.Wrap("p", "x", Options())!;
        Assert.Equal([wrapped.Text.Length], wrapped.Carets);
    }

    [Fact]
    public void Wrapping_never_loses_text()
    {
        Assert.Null(HtmlExpander.Wrap("br", "x", Options()));
        Assert.Null(HtmlExpander.Wrap("div", "  \n ", Options()));
        Assert.Equal("<ul>\n\t<li>x*</li>\n</ul>", HtmlExpander.Wrap("ul>li", "x*", Options())!.Text);
    }

    private static string Css(string abbreviation) => CssExpander.Expand(abbreviation, Options())?.Text ?? "(none)";

    [Theory]
    [InlineData("m10", "margin: 10px;")]
    [InlineData("m10-20", "margin: 10px 20px;")]
    [InlineData("m-10", "margin: -10px;")]
    [InlineData("m10--20", "margin: 10px -20px;")]
    [InlineData("p0", "padding: 0;")]
    [InlineData("w100p", "width: 100%;")]
    [InlineData("w50e", "width: 50em;")]
    [InlineData("fz1.5", "font-size: 1.5em;")]
    [InlineData("lh1.5", "line-height: 1.5;")]
    [InlineData("z10", "z-index: 10;")]
    [InlineData("h20vh", "height: 20vh;")]
    [InlineData("bgc#f00", "background-color: #f00;")]
    [InlineData("c#1", "color: #111111;")]
    [InlineData("c#12", "color: #121212;")]
    [InlineData("dn", "display: none;")]
    [InlineData("dib", "display: inline-block;")]
    [InlineData("pos:a", "position: absolute;")]
    [InlineData("posr", "position: relative;")]
    [InlineData("tac", "text-align: center;")]
    [InlineData("d:none", "display: none;")]
    [InlineData("jc:sb", "justify-content: space-between;")]
    [InlineData("m10!", "margin: 10px !important;")]
    [InlineData("bdrs4", "border-radius: 4px;")]
    [InlineData("m", "margin: ;")]
    [InlineData("zzz", "(none)")]
    [InlineData("m10x#", "(none)")]
    [InlineData("c#12345", "(none)")]
    [InlineData("", "(none)")]
    [InlineData("m10+", "(none)")]
    public void Css_abbreviations_become_declarations(string abbreviation, string expected) => Assert.Equal(expected, Css(abbreviation));

    [Fact]
    public void Several_css_declarations_go_on_lines_of_their_own_and_empty_values_get_a_caret()
    {
        var e = CssExpander.Expand("fz14+c+m", Options("  "))!;
        Assert.Equal("font-size: 14px;\n  color: ;\n  margin: ;", e.Text);
        Assert.Equal("font-size: 14px;\n  color: |;\n  margin: |;", Marked(e));
        var full = CssExpander.Expand("m10", Options())!;
        Assert.Equal([full.Text.Length], full.Carets);
    }

    [Theory]
    [InlineData("a { color: red; ", true)]
    [InlineData("a { }\n", false)]
    [InlineData("", false)]
    [InlineData("/* { */ a", false)]
    [InlineData("a { content: '}'; ", true)]
    [InlineData("@media x { a { ", true)]
    [InlineData("@media x { a { } ", true)]
    public void A_declaration_belongs_in_a_block(string before, bool inside) => Assert.Equal(inside, CssExpander.InsideBlock(before));

    [Theory]
    [InlineData("html", "<p>ul>li*3", 3)]
    [InlineData("html", "    ul>li*3", 4)]
    [InlineData("html", "text div.a[b=c d]{x y}", 5)]
    [InlineData("html", "<div cla", -1)]
    [InlineData("html", "<a href=\"x\">b", 12)]
    [InlineData("html", "", -1)]
    [InlineData("html", "  ", -1)]
    [InlineData("html", "ul>li*3 ", -1)]
    [InlineData("html", ">ul", 1)]
    [InlineData("css", "\tm10", 1)]
    [InlineData("css", "a { m10", 4)]
    [InlineData("css", "color: c", -1)]
    [InlineData("css", "a { color: red; d:n", 16)]
    [InlineData("css", "", -1)]
    public void The_abbreviation_is_found_in_front_of_the_caret(string syntax, string line, int start) => Assert.Equal(start, AbbreviationText.Find(syntax, line));
}
