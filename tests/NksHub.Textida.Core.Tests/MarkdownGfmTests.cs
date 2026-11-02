using Markdig.Extensions.Alerts;
using Markdig.Extensions.Emoji;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using NksHub.Textida.Core.Markdown;

namespace NksHub.Textida.Core.Tests;

public class MarkdownGfmTests
{
    private static IEnumerable<T> Nodes<T>(string source) where T : MarkdownObject =>
        MarkdownSyntax.Walk(MarkdownSyntax.Parse(source)).Select(x => x.Node).OfType<T>();

    [Fact]
    public void Strikethrough_is_an_emphasis_of_double_tildes_and_single_tildes_stay_text()
    {
        var span = Assert.Single(Nodes<EmphasisInline>("a ~~gone~~ and ~also~ b"));
        Assert.Equal(('~', 2), (span.DelimiterChar, span.DelimiterCount));
    }

    [Fact]
    public void Bare_urls_and_www_addresses_become_links_and_www_gets_https()
    {
        var links = Nodes<LinkInline>("see https://example.com/a and www.example.org now").Select(l => l.Url).ToList();
        Assert.Equal(["https://example.com/a", "https://www.example.org"], links);
    }

    [Fact]
    public void A_footnote_has_a_reference_a_definition_in_the_group_and_a_back_link()
    {
        var document = MarkdownSyntax.Parse("Text[^1] here.\n\n[^1]: The note.\n");
        var nodes = MarkdownSyntax.Walk(document).Select(x => x.Node).ToList();
        var note = Assert.Single(nodes.OfType<Footnote>());
        Assert.Single(nodes.OfType<FootnoteGroup>());
        Assert.Equal("^1", note.Label);
        var links = nodes.OfType<FootnoteLink>().ToList();
        Assert.Equal([false, true], links.Select(l => l.IsBackLink));
        Assert.Equal(0, links[0].Line);
    }

    [Theory]
    [InlineData("NOTE"), InlineData("TIP"), InlineData("IMPORTANT"), InlineData("WARNING"), InlineData("CAUTION")]
    public void An_alert_block_has_its_kind_and_keeps_its_text(string kind)
    {
        var alert = Assert.Single(Nodes<AlertBlock>($"> [!{kind}]\n> Be careful.\n"));
        Assert.Equal(kind, alert.Kind.ToString());
        Assert.Contains("Be careful.", MarkdownSyntax.Text(Assert.IsType<ParagraphBlock>(Assert.Single(alert)).Inline));
    }

    [Fact]
    public void An_ordinary_quote_is_not_an_alert()
    {
        Assert.Empty(Nodes<AlertBlock>("> plain\n"));
    }

    [Fact]
    public void Emoji_shortcodes_become_the_character_and_smileys_stay_text()
    {
        var emoji = Assert.Single(Nodes<EmojiInline>("hi :smile: :) ok"));
        Assert.Equal("\U0001F604", emoji.Content.ToString());
        Assert.Equal("hi \U0001F604 :) ok", MarkdownSyntax.Text(Assert.IsType<ParagraphBlock>(Assert.Single(MarkdownSyntax.Parse("hi :smile: :) ok"))).Inline));
    }

    [Fact]
    public void Headings_with_the_new_inlines_keep_their_anchor_and_the_table_commands_still_work()
    {
        var document = MarkdownSyntax.Parse("# A ~~b~~ :smile:\n\n## Next\n\n| a | b |\n|---|---|\n| ~~1~~ | 2 |\n");
        Assert.Equal(["A b \U0001F604", "Next"], MarkdownSyntax.Headings(document).Select(h => h.Title));
        Assert.Contains("next", MarkdownSyntax.HeadingTargets(document).Keys);
        Assert.Single(MarkdownSyntax.Walk(document).Select(x => x.Node).OfType<Table>());
    }
}
