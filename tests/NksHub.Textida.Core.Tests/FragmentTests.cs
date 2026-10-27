using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Fragments;
using NksHub.Textida.Core.Syntax;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class FragmentTests
{
    private static Doc Make(string text)
    {
        byte[] bytes = [.. TextCodec.Utf8.Bom, .. TextCodec.Utf8.Encode(text)];
        return Doc.FromBytes(bytes, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Utf8 });
    }

    private static string Text(Doc d) => d.Codec.GetString(d.ReadBytes(new ByteRange(d.BomLength, d.ByteLength)));

    private static FragmentOpened Open(Doc doc, string marker, bool php = true)
    {
        long caret = doc.BomLength + Encoding.UTF8.GetByteCount(Text(doc)[..(Text(doc).IndexOf(marker, StringComparison.Ordinal) + 1)]);
        return FragmentLink.Open(doc, BuiltinGrammars.Tokenizer(php ? "php" : "javascript"), php, caret);
    }

    private const string Json = "{\"name\":\"it's\",\"path\":\"C:\\\\x\",\"price\":\"$5\",\"t\":\"{$a}\"}";

    [Fact]
    public void Json_with_quotes_returns_into_a_php_single_quoted_string_escaped()
    {
        using var doc = Make("<?php\n$j = '{}';\necho $j;\n");
        var opened = Open(doc, "{}");
        Assert.Equal("{}", opened.Text);
        Assert.Equal(FragmentProblem.None, opened.Link!.WriteBack(Json));
        Assert.Equal("<?php\n$j = '{\"name\":\"it\\'s\",\"path\":\"C:\\\\\\\\x\",\"price\":\"$5\",\"t\":\"{$a}\"}';\necho $j;\n", Text(doc));
        Assert.Equal(Json, Open(doc, "name").Text);
    }

    [Fact]
    public void Json_with_quotes_returns_into_a_php_double_quoted_string_escaped()
    {
        using var doc = Make("<?php\n$j = \"{}\";\n");
        var opened = Open(doc, "{}");
        Assert.Equal(FragmentProblem.None, opened.Link!.WriteBack(Json));
        Assert.Equal("<?php\n$j = \"{\\\"name\\\":\\\"it's\\\",\\\"path\\\":\\\"C:\\\\\\\\x\\\",\\\"price\\\":\\\"\\$5\\\",\\\"t\\\":\\\"{\\$a}\\\"}\";\n", Text(doc));
        Assert.Equal(Json, Open(doc, "name").Text);
    }

    [Theory]
    [InlineData("<?php\n$a = 'x';\n", "x", true)]
    [InlineData("<?php\n$a = \"x\";\n", "x", true)]
    [InlineData("<?php\n$a = <<<EOT\n  x\n  EOT;\n", "x", true)]
    [InlineData("<?php\n$a = <<<'EOT'\n  x\n  EOT;\n", "x", true)]
    [InlineData("<?php\n$a = <<<\"EOT\"\r\nx\r\nEOT;\r\n", "x", true)]
    [InlineData("var a = 'x';\n", "x", false)]
    [InlineData("var a = \"x\";\n", "x", false)]
    [InlineData("var a = `x`;\n", "x", false)]
    public void Every_kind_round_trips(string source, string marker, bool php)
    {
        const string Tricky = "line one\n\tq\"uote' back\\slash \u00e9 $x {y}\nline $ two";
        using var doc = Make(source);
        var link = Open(doc, marker, php).Link!;
        Assert.Equal(FragmentProblem.None, link.WriteBack(Tricky));
        var again = Open(doc, "line one", php);
        Assert.Equal(Tricky, again.Text);
        Assert.Equal(FragmentProblem.None, again.Link!.WriteBack(again.Text));
        Assert.Equal(again.Text, Open(doc, "line one", php).Text);
    }

    [Fact]
    public void Nowdoc_text_is_kept_raw_and_a_closing_line_is_refused()
    {
        using var doc = Make("<?php\n$a = <<<'EOT'\nraw \\n $x\nEOT;\n");
        var opened = Open(doc, "raw");
        Assert.Equal("raw \\n $x", opened.Text);
        Assert.Equal(FragmentProblem.ClosingMarker, opened.Link!.WriteBack("a\n  EOT\nb"));
        Assert.Contains("raw", Text(doc));
    }

    [Fact]
    public void Heredoc_keeps_the_indent_and_writes_into_an_empty_body()
    {
        using var doc = Make("<?php\n$a = <<<EOT\n    EOT;\n");
        var link = Open(doc, "EOT").Link!;
        Assert.Equal(FragmentProblem.None, link.WriteBack("a\nb"));
        Assert.Equal("<?php\n$a = <<<EOT\n    a\n    b\n    EOT;\n", Text(doc));
    }

    [Fact]
    public void Interpolation_and_template_expressions_are_refused()
    {
        using var php = Make("<?php\n$a = \"hello $name\";\n$b = \"{$x['k']}\";\n");
        Assert.Equal(FragmentProblem.Interpolation, Open(php, "hello").Problem);
        Assert.Equal(FragmentProblem.Interpolation, Open(php, "{$x").Problem);
        using var js = Make("var a = `hi ${name}`;\n");
        var opened = Open(js, "hi", php: false);
        Assert.Equal(FragmentProblem.TemplateExpression, opened.Problem);
        Assert.Null(opened.Link);
    }

    [Fact]
    public void Template_without_expression_escapes_backtick_and_dollar_brace_on_write()
    {
        using var doc = Make("var a = `x`;\n");
        Assert.Equal(FragmentProblem.None, Open(doc, "x", php: false).Link!.WriteBack("a`b${c}"));
        Assert.Equal("var a = `a\\`b\\${c}`;\n", Text(doc));
    }

    [Fact]
    public void No_literal_at_the_caret_in_a_comment()
    {
        using var doc = Make("<?php\n// 'not a string'\n$a = 1;\n");
        Assert.Equal(FragmentProblem.NoLiteral, Open(doc, "not").Problem);
    }

    [Fact]
    public void A_change_of_the_parent_inside_the_string_is_a_conflict()
    {
        using var doc = Make("<?php\n$j = 'abc';\n");
        var link = Open(doc, "abc").Link!;
        doc.Insert(Encoding.UTF8.GetByteCount("<?php\n$j = 'a") + doc.BomLength, "Z"u8.ToArray());
        string before = Text(doc);
        Assert.Equal(FragmentProblem.Conflict, link.WriteBack("new"));
        Assert.Equal(before, Text(doc));
        Assert.Equal(FragmentProblem.None, link.WriteBack("new", overwrite: true));
        Assert.Equal("<?php\n$j = 'new';\n", Text(doc));
    }

    [Fact]
    public void A_change_of_the_parent_outside_the_string_moves_the_anchors()
    {
        using var doc = Make("<?php\n$j = 'abc';\necho 1;\n");
        var link = Open(doc, "abc").Link!;
        doc.Insert(doc.BomLength, "<!-- head -->\n"u8.ToArray());
        doc.Replace(new ByteRange(doc.ByteLength - 3, doc.ByteLength - 2), "22"u8.ToArray());
        Assert.False(link.ParentChangedInside());
        Assert.Equal(FragmentProblem.None, link.WriteBack("x'y"));
        Assert.Equal("<!-- head -->\n<?php\n$j = 'x\\'y';\necho 22;\n", Text(doc));
        Assert.Equal(FragmentProblem.None, link.WriteBack("again"));
        Assert.Contains("$j = 'again';", Text(doc));
    }

    [Fact]
    public void Deleting_the_string_loses_the_range_and_a_detached_link_writes_nothing()
    {
        using var doc = Make("<?php\n$j = 'abc';\n");
        var link = Open(doc, "abc").Link!;
        doc.Delete(new ByteRange(doc.BomLength + 6, doc.ByteLength));
        Assert.Equal(FragmentProblem.RangeLost, link.WriteBack("x", overwrite: true));
        using var other = Make("<?php\n$j = 'abc';\n");
        var detached = Open(other, "abc").Link!;
        detached.Detach();
        Assert.Equal(FragmentProblem.Detached, detached.WriteBack("x"));
        Assert.Equal("<?php\n$j = 'abc';\n", Text(other));
    }
}
