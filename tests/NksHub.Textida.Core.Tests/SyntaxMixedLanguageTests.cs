using System.Text;
using System.Text.RegularExpressions;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Syntax;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>P4.7.1: one PHP page coloured by HTML, CSS, JavaScript and PHP, and the same tokens after a jump and an edit far from the viewport.</summary>
public class SyntaxMixedLanguageTests
{
    private const string Page = """
        <!DOCTYPE html>
        <html>
        <head><style>
        a:hover { color: #fff; }
        </style></head>
        <body>
        <script>
        var total = 12; // js
        </script>
        <?php
        $name = 'x';
        echo $name;
        ?>
        <p>done</p>
        </body>
        </html>
        """;

    private static List<string> Kinds(Tokenizer tokenizer, IReadOnlyList<string> lines, string line, string text)
    {
        var all = SyntaxHelpers.TokenizeAll(tokenizer, lines);
        var row = all[lines.ToList().IndexOf(line)];
        return [.. row.Tokens.Select(t => t.Kind + "=" + line.Substring(t.Start, t.Length)).Where(t => t.EndsWith(text, StringComparison.Ordinal))];
    }

    [Fact]
    public void A_php_page_is_coloured_by_html_css_javascript_and_php()
    {
        var lines = Page.ReplaceLineEndings("\n").Split('\n');
        var tokenizer = BuiltinGrammars.Tokenizer("php");
        Assert.Contains("tag=<html>", Kinds(tokenizer, lines, "<html>", "<html>"));
        Assert.Contains("property=color", Kinds(tokenizer, lines, "a:hover { color: #fff; }", "color"));
        Assert.Contains("keyword=var", Kinds(tokenizer, lines, "var total = 12; // js", "var"));
        Assert.Contains("comment=// js", Kinds(tokenizer, lines, "var total = 12; // js", "// js"));
        Assert.Contains("variable=$name", Kinds(tokenizer, lines, "echo $name;", "$name"));
        Assert.Contains("keyword=echo", Kinds(tokenizer, lines, "echo $name;", "echo"));
    }

    [Fact]
    public void After_a_jump_and_an_edit_of_the_start_of_a_long_block_the_tokens_equal_a_full_pass()
    {
        // a long JavaScript block inside the page: the viewport sits behind it, the edit is at its start
        string block = string.Concat(Enumerable.Repeat("var v = 1; /* c */\n", 20_000));
        string text = "<p>x</p>\n<script>\n" + block + "</script>\n<?php echo $a; ?>\n<style>b { color: red; }</style>\n";
        var tokenizer = BuiltinGrammars.Tokenizer("php");
        using var document = Doc.FromBytes(Encoding.UTF8.GetBytes(text), PagedBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Utf8 });
        using var session = new CheckpointSyntaxSession(document, tokenizer);
        int last = Regex.Split(text, "\r\n|\r|\n").Length - 4;
        AssertEqualToFullPass(text, tokenizer, Complete(session, last, 4), last, 4);

        // "<script>" becomes "<scriptx>": the rest of the page is no longer JavaScript until the closing tag
        int at = text.IndexOf("<script>", StringComparison.Ordinal) + "<script".Length;
        Assert.True(document.Replace(new ByteRange(at, at), "x"u8.ToArray()).Applied);
        text = text.Insert(at, "x");
        AssertEqualToFullPass(text, tokenizer, Complete(session, last, 4), last, 4);
    }

    private static SyntaxViewport Complete(CheckpointSyntaxSession session, long first, int count)
    {
        for (int attempt = 0; attempt < 10000; attempt++)
        {
            var result = session.TokenizeViewport(first, count, TimeSpan.FromMilliseconds(2));
            if (result.IsComplete) return result;
        }
        throw new InvalidOperationException("Syntax catch-up did not finish.");
    }

    private static void AssertEqualToFullPass(string text, Tokenizer tokenizer, SyntaxViewport actual, int first, int count)
    {
        var expected = SyntaxHelpers.TokenizeAll(tokenizer, Regex.Split(text, "\r\n|\r|\n"));
        Assert.Equal(count, actual.Lines.Count);
        foreach (var line in actual.Lines)
        {
            Assert.Same(expected[(int)line.Line].EndState, line.EndState);
            Assert.Equal(expected[(int)line.Line].Tokens, line.Tokens);
        }
    }
}
