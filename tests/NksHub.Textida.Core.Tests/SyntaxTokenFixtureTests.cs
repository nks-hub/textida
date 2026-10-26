using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.Core.Tests;

/// <summary>Hand-written expectations for the grammars that P2.2.2 and P2.4.2 need: HTML, PHP, JSON and SQL. Each line states the line start state.</summary>
public class SyntaxTokenFixtureTests
{
    private sealed record Fx(string State, string Line, params string[] Tokens);

    private static void Check(string grammar, params Fx[] lines)
    {
        var tokenizer = BuiltinGrammars.Tokenizer(grammar);
        var state = tokenizer.InitialState;
        foreach (var fx in lines)
        {
            Assert.Equal(fx.State, state.Description);
            var tokens = new List<Token>();
            state = tokenizer.TokenizeLine(fx.Line, state, tokens);
            Assert.Equal(fx.Tokens, tokens.Select(t => t.Kind + "=" + fx.Line.Substring(t.Start, t.Length)).ToArray());
        }
    }

    [Fact]
    public void Html_with_embedded_css_javascript_and_php()
    {
        Check("html",
            new("", "<!DOCTYPE html>", "meta=<!DOCTYPE html>"),
            new("", "<html lang=\"cs\">", "tag=<html", "attribute=lang", "operator==", "string=\"cs\"", "tag=>"),
            new("", "<!-- a comment", "comment=<!-- a comment"),
            new("comment", "  continues -->", "comment=  continues -->"),
            new("", "<head><style type=\"text/css\">", "tag=<head><style type=\"text/css\">"),
            new("style", "a:hover, .x { color: #fff; }", "keyword=:hover", "punctuation=,", "type=.x", "punctuation={", "property=color", "punctuation=:", "constant=#fff", "punctuation=;", "punctuation=}"),
            new("style", "@media (min-width: 600px) {", "keyword=@media", "punctuation=(", "punctuation=:", "number=600px", "punctuation=)", "punctuation={"),
            new("style>block", "  #id > p { color: red }", "constant=#id", "punctuation=>", "punctuation={", "property=color", "punctuation=:", "constant=red", "punctuation=}"),
            new("style>block", "}", "punctuation=}"),
            new("style", "</style>", "tag=</style>"),
            new("", "<script>", "tag=<script>"),
            new("script", "  var a = \"str\\n\" + 12; // line", "keyword=var", "operator==", "string=\"str", "string.escape=\\n", "string=\"", "operator=+", "number=12", "punctuation=;", "comment=// line"),
            new("script", "  /* multi", "comment=/* multi"),
            new("script>block-comment", "  line */ if (a) { }", "comment=  line */", "keyword=if", "punctuation=(", "punctuation=)", "punctuation={", "punctuation=}"),
            new("script", "</script></head>", "tag=</script></head>"),
            new("", "<p>Hello &amp; <?php echo $x->y(\"z\"); /* in", "tag=<p>", "string.escape=&amp;", "meta=<?php", "keyword=echo", "variable=$x", "operator=->", "punctuation=(", "string=\"z\"", "punctuation=);", "comment=/* in"),
            new("php>block-comment", "php */ ?> tail</p>", "comment=php */", "meta=?>", "tag=</p>"),
            new("", "<a href=\"<?= $url ?>\">link</a>", "tag=<a", "attribute=href", "operator==", "string=\"", "meta=<?=", "variable=$url", "meta=?>", "string=\"", "tag=>", "tag=</a>"),
            new("", "<?php", "meta=<?php"),
            new("php", "$s = <<<EOT", "variable=$s", "operator==", "string=<<<EOT"),
            new("php>heredoc", "heredoc $v", "string=heredoc ", "variable=$v"),
            new("php>heredoc", "EOT;", "string=EOT", "punctuation=;"),
            new("php", "if ($a == 1) { echo 'x'; }", "keyword=if", "punctuation=(", "variable=$a", "operator===", "number=1", "punctuation=)", "punctuation={", "keyword=echo", "string='x'", "punctuation=;", "punctuation=}"),
            new("php", "?>", "meta=?>"),
            new("", "</body>", "tag=</body>"));
    }

    [Fact]
    public void Php_file_starts_in_markup_and_switches_to_code()
    {
        Check("php",
            new("", "<?php", "meta=<?php"),
            new("php", "/** doc", "comment.doc=/** doc"),
            new("php>doc-comment", " */ function foo(int $a = 0x1F): string {", "comment.doc= */", "keyword=function", "function=foo", "punctuation=(", "type=int", "variable=$a", "operator==", "number=0x1F", "punctuation=)", "operator=:", "type=string", "punctuation={"),
            new("php", "    $s = \"a {$b} \\\" $c\"; // done ?> <b>", "variable=$s", "operator==", "string=\"a {", "variable=$b", "string=} ", "string.escape=\\\"", "string= ", "variable=$c", "string=\"", "punctuation=;", "comment=// done ", "meta=?>", "tag=<b>"),
            new("", "text", Array.Empty<string>()),
            new("", "<?php /* open", "meta=<?php", "comment=/* open"),
            new("php>block-comment", "still comment", "comment=still comment"),
            new("php>block-comment", "*/ echo 'it\\'s' ?>", "comment=*/", "keyword=echo", "string='it", "string.escape=\\'", "string=s'", "meta=?>"),
            new("", "", Array.Empty<string>()));
    }

    [Fact]
    public void Json_strings_keys_numbers_and_comments()
    {
        Check("json",
            new("", "{", "punctuation={"),
            new("", "  \"name\": \"x\\\"y\\n\", // note", "property=\"name\"", "punctuation=:", "string=\"x", "string.escape=\\\"", "string=y", "string.escape=\\n", "string=\"", "punctuation=,", "comment=// note"),
            new("", "  /* block", "comment=/* block"),
            new("block-comment", "  end */ \"n\": -1.5e+3, \"ok\": true, \"l\": [1, null, \"s\"],", "comment=  end */", "property=\"n\"", "punctuation=:", "number=-1.5e+3", "punctuation=,", "property=\"ok\"", "punctuation=:", "constant=true", "punctuation=,", "property=\"l\"", "punctuation=:", "punctuation=[", "number=1", "punctuation=,", "constant=null", "punctuation=,", "string=\"s\"", "punctuation=],"),
            new("", "  \"bad\": \"unterminated", "property=\"bad\"", "punctuation=:", "string=\"unterminated"),
            new("", "}", "punctuation=}"));
    }

    [Fact]
    public void Sql_multiline_strings_and_comments_keep_state()
    {
        Check("sql",
            new("", "-- comment", "comment=-- comment"),
            new("", "SELECT u.id, COUNT(*) AS n, 'it''s' || \"Col\", `x`, @v, 0x1F, 3.14e2", "keyword=SELECT", "punctuation=.", "punctuation=,", "function=COUNT", "punctuation=(", "operator=*", "punctuation=)", "keyword=AS", "punctuation=,", "string='it''s'", "operator=||", "variable=\"Col\"", "punctuation=,", "variable=`x`", "punctuation=,", "variable=@v", "punctuation=,", "number=0x1F", "punctuation=,", "number=3.14e2"),
            new("", "  FROM users u /* inline */ WHERE u.name LIKE 'multi", "keyword=FROM", "comment=/* inline */", "keyword=WHERE", "punctuation=.", "keyword=LIKE", "string='multi"),
            new("string", "line' AND u.x IS NOT NULL /* block", "string=line'", "keyword=AND", "punctuation=.", "keyword=IS", "keyword=NOT", "constant=NULL", "comment=/* block"),
            new("block-comment", "comment */ ORDER BY 1 DESC;", "comment=comment */", "keyword=ORDER", "keyword=BY", "number=1", "keyword=DESC", "punctuation=;"),
            new("", "CREATE TABLE t (id INT PRIMARY KEY, name VARCHAR(20) DEFAULT 'a');", "keyword=CREATE", "keyword=TABLE", "punctuation=(", "type=INT", "keyword=PRIMARY", "keyword=KEY", "punctuation=,", "type=VARCHAR", "punctuation=(", "number=20", "punctuation=)", "keyword=DEFAULT", "string='a'", "punctuation=);"));
    }

    [Fact]
    public void Html_state_after_an_unclosed_region_is_the_region()
    {
        var tokenizer = BuiltinGrammars.Tokenizer("html");
        var tokens = new List<Token>();
        Assert.Equal("script", tokenizer.TokenizeLine("<script>", tokenizer.InitialState, tokens).Description);
        Assert.Equal("script>template>template-expr", tokenizer.TokenizeLine("var a = `x ${", tokenizer.TokenizeLine("<script>", tokenizer.InitialState, []), []).Description);
        Assert.True(tokenizer.TokenizeLine("<p>text</p>", tokenizer.InitialState, []).IsInitial);
    }
}
