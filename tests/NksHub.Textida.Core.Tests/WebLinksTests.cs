using NksHub.Textida.Core.Http;

namespace NksHub.Textida.Core.Tests;

/// <summary>PZ.2.3 and PZ.2.4: the search address from a template and the address of a file from path rules.</summary>
public class WebLinksTests
{
    [Fact]
    public void The_text_is_percent_encoded_into_the_template_and_a_bad_template_or_text_gives_nothing()
    {
        Assert.Equal("https://find.test/?q=a%20%26%20b%3Dc%C4%8D", WebLinks.SearchUri("https://find.test/?q={query}", " a & b=cč ")!.AbsoluteUri);
        Assert.Null(WebLinks.SearchUri("", "x"));
        Assert.Null(WebLinks.SearchUri("https://find.test/?q=", "x")); // no placeholder
        Assert.Null(WebLinks.SearchUri("ftp://find.test/{query}", "x"));
        Assert.Null(WebLinks.SearchUri("javascript:alert({query})", "x"));
        Assert.Null(WebLinks.SearchUri("https://find.test/?q={query}", "   "));
        Assert.Null(WebLinks.SearchUri("https://find.test/?q={query}", new string('a', WebLinks.MaxQueryChars + 1)));
    }

    [Fact]
    public void Rules_are_read_from_semicolons_and_lines_and_a_broken_rule_is_left_out()
    {
        var rules = WebLinks.ParseRules("C:\\work\\site => http://site.loc ; D:/x=>https://x.test/app/\nbroken ; E:\\y=>file:///etc;=>http://z.test");

        Assert.Equal([("C:\\work\\site", "http://site.loc/"), ("D:/x", "https://x.test/app/")], rules.Select(r => (r.Folder, r.Address)));
    }

    [Fact]
    public void The_longest_folder_wins_segments_are_encoded_and_a_file_outside_or_a_dotdot_gives_nothing()
    {
        var rules = WebLinks.ParseRules("C:\\work\\htdocs=>http://all.loc/;C:\\work\\htdocs\\shop=>http://shop.loc/");

        Assert.Equal("http://shop.loc/css/main%20x.css", WebLinks.MapPath(rules, "C:\\work\\htdocs\\shop\\css\\main x.css")!.AbsoluteUri);
        Assert.Equal("http://all.loc/blog/index.php", WebLinks.MapPath(rules, "C:\\work\\htdocs\\blog\\index.php")!.AbsoluteUri);
        Assert.Null(WebLinks.MapPath(rules, "C:\\work\\htdocs2\\a.php")); // a folder that only starts the same
        Assert.Null(WebLinks.MapPath(rules, "C:\\other\\a.php"));
        Assert.Null(WebLinks.MapPath(rules, "C:\\work\\htdocs\\..\\secret.php"));
        Assert.Null(WebLinks.MapPath(rules, "C:\\work\\htdocs"));
    }
}
