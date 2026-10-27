using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.Core.Tests;

/// <summary>P4.6.5: the HTML tag tools work on offsets of the text.</summary>
public class HtmlTagsTests
{
    private const string Page = "<div><!-- <b> --><p>a<br>b</p><script>if (a<b) {}</script></div>";

    [Fact]
    public void Tags_are_paired_by_nesting_and_the_tools_find_the_open_tag_the_partner_and_the_element()
    {
        Assert.Equal("p", HtmlTags.UnclosedBefore("<div><p>a<br>b", 14));
        Assert.Null(HtmlTags.UnclosedBefore(Page, Page.Length));

        int open = Page.IndexOf("<p>", StringComparison.Ordinal), close = Page.IndexOf("</p>", StringComparison.Ordinal);
        Assert.True(HtmlTags.TryMatch(Page, open + 1, out int partner));
        Assert.Equal(close, partner);
        Assert.True(HtmlTags.TryMatch(Page, close, out partner));
        Assert.Equal(open, partner);

        Assert.True(HtmlTags.TryElement(Page, open + 4, open + 5, out int start, out int end));
        Assert.Equal("<p>a<br>b</p>", Page[start..end]);
        Assert.True(HtmlTags.TryElement(Page, start, end, out start, out end));
        Assert.Equal(Page, Page[start..end]);

        Assert.Equal(["</p>", "<p>"], HtmlTags.TagsToRemove(Page, open + 3).Select(t => Page[t.Start..t.End]));
        Assert.Equal(["<br>"], HtmlTags.TagsToRemove(Page, open + 5).Select(t => Page[t.Start..t.End]));
    }

    [Fact]
    public void Strip_removes_tags_and_comments_and_keeps_text_and_lone_angle_brackets()
    {
        Assert.Equal("a b 1 < 2", HtmlTags.Strip("<p class=\"x>y\">a</p><!-- c --> b <br/>1 < 2"));
        Assert.True(HtmlTags.IsName("h1") && !HtmlTags.IsName("1h") && !HtmlTags.IsName("a b"));
    }
}
