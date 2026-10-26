using AvaloniaEdit.Document;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.Document.AvaloniaEdit.Tests;

/// <summary>
/// P2.2.2: smart keys for web files, from the tokens of the shared token base. Each rule has a positive case, negative cases and a case with
/// its setting off. <c>|</c> in an input marks the caret (the caret of a paste, of a typed character or of Enter).
/// </summary>
public class WebSmartKeysTests
{
    private sealed class Probe(TextDocument d, Tokenizer tokenizer) : ITokenProbe
    {
        private List<IReadOnlyList<Token>>? _lines;

        public IReadOnlyList<Token> TokensOfLine(int line)
        {
            if (_lines is null)
            {
                _lines = [];
                var state = tokenizer.InitialState;
                for (int n = 1; n <= d.LineCount; n++)
                {
                    var l = d.GetLineByNumber(n);
                    var tokens = new List<Token>();
                    state = tokenizer.TokenizeLine(d.GetText(l.Offset, l.Length), state, tokens);
                    _lines.Add(tokens);
                }
            }
            return _lines[line - 1];
        }
    }

    private static (SmartKeys Keys, TextDocument Doc, int Caret) Setup(string marked, string language, SmartKeyOptions? options = null)
    {
        int caret = marked.IndexOf('|');
        var d = new TextDocument(marked.Remove(caret, 1));
        var keys = new SmartKeys(d, new Probe(d, BuiltinGrammars.Tokenizer(language)), language, options ?? new SmartKeyOptions());
        return (keys, d, caret);
    }

    private static string Apply(TextDocument d, ViewEdit e)
    {
        d.Replace(e.Start, e.Length, e.Text);
        return d.Text;
    }

    // ---- </ ----

    [Theory]
    [InlineData("<ul><li>a</li><|", "html", "/ul>")]
    [InlineData("<div><p>x</p></div><section><|", "html", "/section>")]
    [InlineData("<br><div><img src=\"a\"><|", "html", "/div>")]
    [InlineData("<div\n  class=\"a\"\n  id=\"b\">\n<|", "html", "/div>")]
    [InlineData("<root><item/><|", "xml", "/root>")]
    [InlineData("<script>var a = 1;</script><div><|", "html", "/div>")]
    public void Typing_a_slash_after_a_bracket_completes_the_innermost_open_tag(string input, string language, string expected)
    {
        var (keys, d, caret) = Setup(input, language);
        Assert.True(keys.TryPlan(d, Sel.At(caret), '/', out var edit, out var result));
        Assert.Equal(new ViewEdit(caret, 0, expected), edit);
        Assert.Equal(Sel.At(caret + expected.Length), result);
    }

    [Theory]
    [InlineData("text <|", "html")]                       // nothing is open
    [InlineData("<div></div><|", "html")]                 // everything is closed
    [InlineData("<a title=\"x<|\">", "html")]             // inside a string
    [InlineData("<!-- <div> < | -->", "html")]            // inside a comment (no bracket before the caret)
    [InlineData("<!-- <div><| -->", "html")]              // inside a comment
    [InlineData("<div><|>", "html")]                      // the bracket is already closed
    [InlineData("<div>a|", "html")]                       // no bracket before the caret
    [InlineData("<div><|", "css")]                        // not a markup language
    public void Typing_a_slash_elsewhere_completes_nothing(string input, string language)
    {
        var (keys, d, caret) = Setup(input, language == "css" ? "css" : language);
        Assert.False(keys.TryPlan(d, Sel.At(caret), '/', out _, out _));
    }

    [Fact]
    public void Typing_a_slash_inside_a_string_of_php_code_and_with_the_setting_off_completes_nothing()
    {
        var (keys, d, caret) = Setup("<div><?php echo \"<|\"; ?>", "php");
        Assert.False(keys.TryPlan(d, Sel.At(caret), '/', out _, out _));
        var (off, d2, caret2) = Setup("<div><|", "html", new SmartKeyOptions(CloseTag: false));
        Assert.False(off.TryPlan(d2, Sel.At(caret2), '/', out _, out _));
    }

    [Fact]
    public void A_selection_is_replaced_by_the_slash_as_usual()
    {
        var (keys, d, caret) = Setup("<div><|", "html");
        Assert.False(keys.TryPlan(d, new Sel(caret - 1, caret), '/', out _, out _));
    }

    // ---- renaming the partner ----

    [Fact]
    public void Renaming_div_to_section_changes_the_closing_tag_too()
    {
        var (keys, d, _) = Setup("<div class=\"a\">x</div>|", "html");
        Assert.True(keys.TryPlanRename(new ViewEdit(1, 3, "section"), out var partner));
        Assert.Equal("<div class=\"a\">x</section>", Apply(d, partner));
    }

    [Theory]
    [InlineData("<div>x</div>", 4, 0, "s", "<div>x</divs>")]                       // typing at the end of the name
    [InlineData("<div>x</div>", 1, 1, "", "<div>x</iv>")]                          // deleting the first letter
    [InlineData("<div>x</div>", 11, 0, "s", "<divs>x</div>")]                      // editing the closing tag changes the opening one
    [InlineData("<div><div>a</div>b</div>", 9, 0, "x", "<div><div>a</divx>b</div>")] // nested tags of one name: the inner pair
    [InlineData("<p>a<br>b</p>", 1, 1, "q", "<p>a<br>b</q>")]                      // a void tag between them is not a pair
    [InlineData("<a href=\"x\"\n   target=\"y\">t</a>", 1, 1, "b", "<a href=\"x\"\n   target=\"y\">t</b>")] // an opening tag over several lines
    public void The_partner_follows_an_edit_of_a_tag_name(string text, int start, int length, string insert, string expected)
    {
        var d = new TextDocument(text);
        var keys = new SmartKeys(d, new Probe(d, BuiltinGrammars.Tokenizer("html")), "html", new SmartKeyOptions());
        Assert.True(keys.TryPlanRename(new ViewEdit(start, length, insert), out var partner));
        Assert.Equal(expected, Apply(d, partner));
    }

    [Theory]
    [InlineData("<div>x</div>", 5, 0, "a")]            // text between the tags
    [InlineData("<div class=\"a\">x</div>", 8, 0, "z")] // an attribute name
    [InlineData("<div>x</div>", 4, 0, " ")]            // not a name character
    [InlineData("<div>x", 1, 1, "s")]                  // no partner
    [InlineData("<div>x</span>", 1, 1, "s")]           // the names do not pair
    [InlineData("<div>x</div>", 1, 3, "")]             // the whole name removed: the partner keeps its name
    [InlineData("<br>x", 1, 1, "s")]                   // a void tag has none
    public void An_edit_that_is_not_a_tag_name_edit_renames_nothing(string text, int start, int length, string insert)
    {
        var d = new TextDocument(text);
        var keys = new SmartKeys(d, new Probe(d, BuiltinGrammars.Tokenizer("html")), "html", new SmartKeyOptions());
        Assert.False(keys.TryPlanRename(new ViewEdit(start, length, insert), out _));
    }

    [Fact]
    public void Renaming_works_in_xml_and_can_be_switched_off()
    {
        var d = new TextDocument("<a><b/></a>");
        var xml = new SmartKeys(d, new Probe(d, BuiltinGrammars.Tokenizer("xml")), "xml", new SmartKeyOptions());
        Assert.True(xml.TryPlanRename(new ViewEdit(1, 1, "root"), out var partner));
        Assert.Equal("<a><b/></root>", Apply(d, partner));
        var d2 = new TextDocument("<div>x</div>");
        var off = new SmartKeys(d2, new Probe(d2, BuiltinGrammars.Tokenizer("html")), "html", new SmartKeyOptions(RenameTag: false));
        Assert.False(off.TryPlanRename(new ViewEdit(1, 3, "section"), out _));
    }

    // ---- escaping pasted text ----

    [Theory]
    [InlineData("var s = \"|\";", "javascript", "say \"hi\"", "say \\\"hi\\\"")]
    [InlineData("var s = 'it|';", "javascript", "it's", "it\\'s")]
    [InlineData("var s = \"|\";", "javascript", "a\\b", "a\\\\b")]
    [InlineData("var s = \"|\";", "javascript", "one\r\ntwo\nthree", "one\\ntwo\\nthree")]
    [InlineData("{ \"k\": \"|\" }", "json", "x\"y", "x\\\"y")]
    [InlineData("<?php $a = \"|\";", "php", "\"q\"", "\\\"q\\\"")]
    [InlineData("x = 'abc|def'", "python", "'", "\\'")]
    [InlineData("<a title=\"|\">", "html", "a\"b", "a&quot;b")]
    public void Pasting_into_a_string_escapes_what_the_string_cannot_hold(string input, string language, string pasted, string expected)
    {
        var (keys, _, caret) = Setup(input, language);
        Assert.Equal(expected, keys.EscapeForPaste(caret, pasted));
    }

    [Theory]
    [InlineData("var s = |;", "javascript")]               // not in a string
    [InlineData("var s = |\"a\";", "javascript")]          // before the opening quote
    [InlineData("var s = \"a\"|;", "javascript")]          // after the closing quote
    [InlineData("var s = `a|`;", "javascript")]            // a template literal has its own rules
    [InlineData("// \"|\"", "javascript")]                 // a comment
    [InlineData("<p>|</p>", "html")]                       // text between tags
    [InlineData("a = 1|", "csharp")]
    public void Pasting_outside_a_string_inserts_the_text_as_it_is(string input, string language)
    {
        var (keys, _, caret) = Setup(input, language);
        Assert.Equal("say \"hi\"\\", keys.EscapeForPaste(caret, "say \"hi\"\\"));
    }

    [Fact]
    public void Pasting_into_a_string_leaves_the_text_alone_with_the_setting_off()
    {
        var (keys, _, caret) = Setup("var s = \"|\";", "javascript", new SmartKeyOptions(EscapeInString: false));
        Assert.Equal("a\"b", keys.EscapeForPaste(caret, "a\"b"));
    }

    // ---- JSON comma on Enter ----

    [Theory]
    [InlineData("{\n  \"a\": 1|\n  \"b\": 2\n}", "{\n  \"a\": 1,\n  |\n  \"b\": 2\n}")]
    [InlineData("{\n  \"a\": \"x\"|\n  \"b\": 2\n}", "{\n  \"a\": \"x\",\n  |\n  \"b\": 2\n}")]
    [InlineData("{\n  \"a\": true|\n  \"b\": 2\n}", "{\n  \"a\": true,\n  |\n  \"b\": 2\n}")]
    [InlineData("{\n  \"a\": null  |\n  \"b\": 2\n}", "{\n  \"a\": null,\n  |\n  \"b\": 2\n}")]
    [InlineData("{\n  \"a\": {\n    \"x\": 1\n  }|\n  \"b\": 2\n}", "{\n  \"a\": {\n    \"x\": 1\n  },\n  |\n  \"b\": 2\n}")]
    [InlineData("[\n  1|\n  2\n]", "[\n  1,\n  |\n  2\n]")]
    public void Enter_at_the_end_of_a_json_value_followed_by_another_adds_the_comma(string input, string expected)
    {
        var (keys, d, caret) = Setup(input, "json");
        Assert.True(keys.TryPlanLineBreak(Sel.At(caret), "\n", out var edit, out var result));
        string text = Apply(d, edit);
        Assert.Equal(expected, text.Insert(result.Caret, "|"));
    }

    [Theory]
    [InlineData("{\n  \"a\": 1|\n}")]                       // the last value
    [InlineData("{\n  \"a\": [\n    1|\n  ]\n}")]           // the last item
    [InlineData("{\n  \"a\": 1,|\n  \"b\": 2\n}")]          // the comma is there
    [InlineData("{\n  \"a\":|\n  \"b\": 2\n}")]             // no value yet
    [InlineData("{\n  \"a\": [|\n  1\n  ]\n}")]             // an opening bracket
    [InlineData("{\n  \"a\": 1 // x|\n  \"b\": 2\n}")]      // a comment ends the line
    [InlineData("{\n  \"a\": 1|, \"b\": 2\n}")]             // not at the end of the line
    [InlineData("{\n  \"a\": 1|")]                          // nothing follows
    public void Enter_elsewhere_in_json_adds_no_comma(string input)
    {
        var (keys, d, caret) = Setup(input, "json");
        Assert.False(keys.TryPlanLineBreak(Sel.At(caret), "\n", out _, out _));
    }

    [Fact]
    public void Enter_adds_no_json_comma_with_the_setting_off_or_in_another_language_or_over_a_selection()
    {
        var (off, d, caret) = Setup("{\n  \"a\": 1|\n  \"b\": 2\n}", "json", new SmartKeyOptions(JsonComma: false));
        Assert.False(off.TryPlanLineBreak(Sel.At(caret), "\n", out _, out _));
        var (js, d2, caret2) = Setup("{\n  a: 1|\n  b: 2\n}", "javascript");
        Assert.False(js.TryPlanLineBreak(Sel.At(caret2), "\n", out _, out _));
        var (on, d3, caret3) = Setup("{\n  \"a\": 1|\n  \"b\": 2\n}", "json");
        Assert.False(on.TryPlanLineBreak(new Sel(caret3 - 1, caret3), "\n", out _, out _));
    }

    // ---- -> after a PHP variable ----

    [Theory]
    [InlineData("<?php $obj|", "->")]
    [InlineData("<?php $this|;", "->")]
    [InlineData("<?php echo $a_b2|", "->")]
    public void Typing_a_minus_right_after_a_php_variable_adds_the_arrow(string input, string expected)
    {
        var (keys, d, caret) = Setup(input, "php");
        Assert.True(keys.TryPlan(d, Sel.At(caret), '-', out var edit, out var result));
        Assert.Equal(new ViewEdit(caret, 0, expected), edit);
        Assert.Equal(Sel.At(caret + 2), result);
    }

    [Theory]
    [InlineData("<?php $obj |")]            // a blank between
    [InlineData("<?php 5|")]                // a number
    [InlineData("<?php foo|")]              // not a variable
    [InlineData("<?php echo \"$name|\";")]  // interpolated in a string
    [InlineData("<?php $x|>")]              // an arrow follows
    [InlineData("<?php $x|-")]              // a decrement follows
    [InlineData("<?php $x|= 1;")]           // an assignment follows
    [InlineData("<p>$obj|</p>")]            // not in PHP code
    public void Typing_a_minus_elsewhere_adds_no_arrow(string input)
    {
        var (keys, d, caret) = Setup(input, "php");
        Assert.False(keys.TryPlan(d, Sel.At(caret), '-', out _, out _));
    }

    [Fact]
    public void The_arrow_needs_php_and_its_setting()
    {
        var (js, d, caret) = Setup("var $obj|", "javascript");
        Assert.False(js.TryPlan(d, Sel.At(caret), '-', out _, out _));
        var (off, d2, caret2) = Setup("<?php $obj|", "php", new SmartKeyOptions(PhpArrow: false));
        Assert.False(off.TryPlan(d2, Sel.At(caret2), '-', out _, out _));
    }

    // ---- selecting a PHP variable ----

    [Theory]
    [InlineData("<?php echo $userName;", 14, "userName")]   // inside the name
    [InlineData("<?php echo $userName;", 11, "userName")]   // on the dollar sign
    [InlineData("<?php echo $$dyn;", 12, "dyn")]            // variable variable
    public void A_php_variable_is_selected_without_its_dollar_sign(string text, int offset, string expected)
    {
        var d = new TextDocument(text);
        var keys = new SmartKeys(d, new Probe(d, BuiltinGrammars.Tokenizer("php")), "php", new SmartKeyOptions());
        Assert.True(keys.TrySelectVariable(offset, out int start, out int end));
        Assert.Equal(expected, d.GetText(start, end - start));
    }

    [Theory]
    [InlineData("<?php echo $userName;", 8)]      // a keyword
    [InlineData("<?php echo $userName;", 20)]     // the semicolon
    [InlineData("<p>user</p>", 4)]
    public void Other_words_are_left_to_the_ordinary_double_click(string text, int offset)
    {
        var d = new TextDocument(text);
        var keys = new SmartKeys(d, new Probe(d, BuiltinGrammars.Tokenizer("php")), "php", new SmartKeyOptions());
        Assert.False(keys.TrySelectVariable(offset, out _, out _));
    }

    [Fact]
    public void Variable_selection_needs_php_and_its_setting()
    {
        var d = new TextDocument("<?php echo $userName;");
        Assert.False(new SmartKeys(d, new Probe(d, BuiltinGrammars.Tokenizer("php")), "php", new SmartKeyOptions(SelectVariable: false)).TrySelectVariable(14, out _, out _));
        Assert.False(new SmartKeys(d, new Probe(d, BuiltinGrammars.Tokenizer("javascript")), "javascript", new SmartKeyOptions()).TrySelectVariable(14, out _, out _));
    }

    // ---- BEGIN -> END in SQL ----

    [Theory]
    [InlineData("BEGIN|", "BEGIN\n    |\nEND")]
    [InlineData("  begin|", "  begin\n      |\n  end")]
    [InlineData("CREATE PROCEDURE p()\nBEGIN  |\nSELECT 1;", "CREATE PROCEDURE p()\nBEGIN\n    |\nEND\nSELECT 1;")]
    [InlineData("BEGIN|\n  SELECT 1;\n", "BEGIN\n    |\nEND\n  SELECT 1;\n")]
    public void Enter_after_begin_adds_an_end_line(string input, string expected)
    {
        var (keys, d, caret) = Setup(input, "sql");
        Assert.True(keys.TryPlanLineBreak(Sel.At(caret), "\n", out var edit, out var result));
        Assert.Equal(expected, Apply(d, edit).Insert(result.Caret, "|"));
    }

    [Theory]
    [InlineData("BEGIN|\n  SELECT 1;\nEND")]       // already closed
    [InlineData("BEGIN|\nend;")]
    [InlineData("SELECT 'BEGIN|'")]               // in a string
    [InlineData("-- begin|")]                     // in a comment
    [InlineData("BEGINNING|")]                    // another word
    [InlineData("BEGIN |x")]                      // not at the end of the line
    [InlineData("SELECT 1|")]
    public void Enter_elsewhere_in_sql_adds_nothing(string input)
    {
        var (keys, d, caret) = Setup(input, "sql");
        Assert.False(keys.TryPlanLineBreak(Sel.At(caret), "\n", out _, out _));
    }

    [Fact]
    public void The_sql_end_needs_its_setting()
    {
        var (off, d, caret) = Setup("BEGIN|", "sql", new SmartKeyOptions(SqlEnd: false));
        Assert.False(off.TryPlanLineBreak(Sel.At(caret), "\n", out _, out _));
    }
}
