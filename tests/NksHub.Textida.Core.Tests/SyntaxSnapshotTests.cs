using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// Token fixtures of the remaining built-in grammars: a few lines each, checked by eye when written (the tokenizer printed them, a person read every token),
/// and frozen here so a change of a grammar shows up as a diff. Each line also fixes the state it starts in.
/// </summary>
public class SyntaxSnapshotTests
{
    internal sealed record Line(string State, string Text, params string[] Tokens);

    public static TheoryData<string> Grammars()
    {
        var data = new TheoryData<string>();
        foreach (var (grammar, _) in Snapshots.All) data.Add(grammar);
        return data;
    }

    [Theory]
    [MemberData(nameof(Grammars))]
    public void Tokens_and_line_states_match_the_fixture(string grammar)
    {
        var lines = Snapshots.All.Single(s => s.Grammar == grammar).Lines;
        var tokenizer = BuiltinGrammars.Tokenizer(grammar);
        var state = tokenizer.InitialState;
        for (int i = 0; i < lines.Length; i++)
        {
            var fx = lines[i];
            Assert.True(fx.State == state.Description, $"{grammar} line {i + 1} starts in '{state.Description}', expected '{fx.State}'");
            var tokens = new List<Token>();
            state = tokenizer.TokenizeLine(fx.Text, state, tokens);
            Assert.Equal(fx.Tokens, tokens.Select(t => t.Kind + "=" + fx.Text.Substring(t.Start, t.Length)).ToArray());
        }
    }

    [Fact]
    public void Every_builtin_grammar_has_a_fixture_or_a_hand_written_test()
    {
        string[] handWritten = ["html", "php", "json", "sql", "php-code"];
        var covered = Snapshots.All.Select(s => s.Grammar).Concat(handWritten).ToHashSet();
        Assert.All(BuiltinGrammars.Registry.All.Select(g => g.Id), id => Assert.Contains(id, covered));
    }

    private static class Snapshots
    {
    public static readonly (string Grammar, Line[] Lines)[] All =
    [
        ("plaintext", [
            new("", "Just text, see https://example.com/a?b=1 for more.", "link=https://example.com/a?b=1"),
            new("", "Nothing \"else\" /* here */ 123 if"),
            new("", "")]),
        ("ini", [
            new("", "; comment", "comment=; comment"),
            new("", "[Section One]", "heading=[Section One]"),
            new("", "key = value", "property=key", "operator=="),
            new("", "  other key= \"quoted\" ; trailing", "property=other key", "operator==", "string=\"quoted\"", "comment=; trailing"),
            new("", "flag=true", "property=flag", "operator==", "constant=true"),
            new("", "n = 12.5", "property=n", "operator==", "number=12.5"),
            new("", "path=C:\\dir\\it's", "property=path", "operator=="),
            new("", "# hash", "comment=# hash"),
            new("", "url = https://x.y/z", "property=url", "operator=="),
            new("", "")]),
        ("xml", [
            new("", "<?xml version=\"1.0\" encoding=\"utf-8\"?>", "meta=<?xml version=\"1.0\" encoding=\"utf-8\"?>"),
            new("", "<!DOCTYPE a>", "meta=<!DOCTYPE a>"),
            new("", "<root xmlns:x=\"u\" a='1'>", "tag=<root", "attribute=xmlns:x", "operator==", "string=\"u\"", "attribute=a", "operator==", "string='1'", "tag=>"),
            new("", "  <!-- c -->", "comment=<!-- c -->"),
            new("", "  <x:item id=\"1\" flag>text &amp; more</x:item>", "tag=<x:item", "attribute=id", "operator==", "string=\"1\"", "attribute=flag", "tag=>", "string.escape=&amp;", "tag=</x:item>"),
            new("", "  <![CDATA[ raw <b> ]]>", "string=<![CDATA[ raw <b> ]]>"),
            new("", "  <empty/>", "tag=<empty/>"),
            new("", "</root>", "tag=</root>"),
            new("", "")]),
        ("css", [
            new("", "/* header", "comment=/* header"),
            new("comment", "   comment */", "comment=   comment */"),
            new("", "@import url(\"a.css\");", "keyword=@import", "function=url", "punctuation=(", "string=\"a.css\"", "punctuation=);"),
            new("", "a:hover, div.cls > .x::before { color: #FFF; margin: -2px .5em 0 1.5rem; background: url(x.png) no-repeat !important }", "keyword=:hover", "punctuation=,", "type=.cls", "punctuation=>", "type=.x", "keyword=::before", "punctuation={", "property=color", "punctuation=:", "constant=#FFF", "punctuation=;", "property=margin", "punctuation=:", "number=-2px", "number=.5em", "number=0", "number=1.5rem", "punctuation=;", "property=background", "punctuation=:", "function=url", "punctuation=(", "punctuation=)", "constant=no-repeat", "keyword=!important", "punctuation=}"),
            new("", "@media (max-width: 600px) {", "keyword=@media", "punctuation=(", "punctuation=:", "number=600px", "punctuation=)", "punctuation={"),
            new("block", "  .a { width: calc(100% - 2px); content: \"a\\\"b\"; }", "type=.a", "punctuation={", "property=width", "punctuation=:", "function=calc", "punctuation=(", "number=100%", "number=2px", "punctuation=);", "property=content", "punctuation=:", "string=\"a", "string.escape=\\\"", "string=b\"", "punctuation=;", "punctuation=}"),
            new("block", "}", "punctuation=}"),
            new("", "")]),
        ("javascript", [
            new("", "const a = `x ${b + `in ${c}`} y", "keyword=const", "operator==", "string=`x ", "punctuation=${", "operator=+", "string=`in ", "punctuation=${", "punctuation=}", "string=`", "punctuation=}", "string= y"),
            new("template", "  more` + 1.5e3; // end", "string=  more`", "operator=+", "number=1.5e3", "punctuation=;", "comment=// end"),
            new("", "function f(x) { return /re[/]g+/i.test(x) ? x / 2 / 3 : 'it\\'s'; }", "keyword=function", "punctuation=(", "punctuation=)", "punctuation={", "keyword=return", "regex=/re[/]g+/i", "punctuation=.", "punctuation=(", "punctuation=)", "operator=?", "operator=/", "number=2", "operator=/", "number=3", "operator=:", "string='it", "string.escape=\\'", "string=s'", "punctuation=;", "punctuation=}"),
            new("", "let o = { k: 1n, \"q\": 0xFF, ...r }, t = x => x?.y ?? null;", "keyword=let", "operator==", "punctuation={", "operator=:", "number=1n", "punctuation=,", "string=\"q\"", "operator=:", "number=0xFF", "punctuation=,", "operator=...", "punctuation=},", "operator==", "operator==>", "operator=?.", "operator=??", "constant=null", "punctuation=;"),
            new("", "")]),
        ("csharp", [
            new("", "using System;", "keyword=using", "punctuation=;"),
            new("", "#region Foo", "preprocessor=#region Foo"),
            new("", "namespace A.B;", "keyword=namespace", "punctuation=.", "punctuation=;"),
            new("", "/// <summary>doc</summary>", "comment.doc=/// <summary>doc</summary>"),
            new("", "public sealed class C : Base // c", "keyword=public", "keyword=sealed", "keyword=class", "operator=:", "comment=// c"),
            new("", "{", "punctuation={"),
            new("", "    private readonly int _x = 0x1F + 12_000;", "keyword=private", "keyword=readonly", "type=int", "operator==", "number=0x1F", "operator=+", "number=12_000", "punctuation=;"),
            new("", "    string s = \"a\\\"b\", v = @\"verb \"\"q\"\"", "type=string", "operator==", "string=\"a", "string.escape=\\\"", "string=b\"", "punctuation=,", "operator==", "string=@\"verb ", "string.escape=\"\"", "string=q", "string.escape=\"\""),
            new("verbatim-string", "more\", i = $\"v={x + 1} {{lit}} {Foo(\"n\")}\", r = \"\"\"", "string=more\"", "punctuation=,", "operator==", "string=$\"v=", "punctuation={", "operator=+", "number=1", "punctuation=}", "string= ", "string.escape={{", "string=lit", "string.escape=}}", "string= ", "punctuation={", "punctuation=(", "string=\"n\"", "punctuation=)}", "string=\"", "punctuation=,", "operator==", "string=\"\"\""),
            new("raw-string", "   raw \"text\"", "string=   raw \"text\""),
            new("raw-string", "   \"\"\";", "string=   \"\"\"", "punctuation=;"),
            new("", "    char c = '\\n'; /* multi", "type=char", "operator==", "string='\\n'", "punctuation=;", "comment=/* multi"),
            new("block-comment", "    line */ var y = await Task.Run(() => null);", "comment=    line */", "keyword=var", "operator==", "keyword=await", "type=Task", "punctuation=.", "punctuation=(()", "operator==>", "constant=null", "punctuation=);"),
            new("", "}", "punctuation=}"),
            new("", "")]),
        ("python", [
            new("", "#!/usr/bin/env python", "comment=#!/usr/bin/env python"),
            new("", "@decorator.x(1)", "meta=@decorator.x", "punctuation=(", "number=1", "punctuation=)"),
            new("", "def foo(self, x=0x1F, *a):  # c", "keyword=def", "function=foo", "punctuation=(", "constant=self", "punctuation=,", "operator==", "number=0x1F", "punctuation=,", "operator=*", "punctuation=):", "comment=# c"),
            new("", "    \"\"\"doc", "string=\"\"\"doc"),
            new("triple-dq-string", "    string\"\"\" + r'raw\\d' + f\"v{x}\" 'it\\'s'", "string=    string\"\"\"", "operator=+", "string=r'raw", "string.escape=\\d", "string='", "operator=+", "string=f\"v{x}\"", "string='it", "string.escape=\\'", "string=s'"),
            new("", "    return None if x != 3.5e2 else True", "keyword=return", "constant=None", "keyword=if", "operator=!=", "number=3.5e2", "keyword=else", "constant=True"),
            new("", "class Bar(Base): pass", "keyword=class", "type=Bar", "punctuation=(", "punctuation=):", "keyword=pass"),
            new("", "")]),
        ("shell", [
            new("", "#!/bin/bash", "meta=#!/bin/bash"),
            new("", "# comment", "comment=# comment"),
            new("", "export FOO=\"bar $BAR ${BAZ:-x} \\\"q\\\"", "function=export", "variable=FOO", "operator==", "string=\"bar ", "variable=$BAR", "string= ", "variable=${BAZ:-x}", "string= ", "string.escape=\\\"", "string=q", "string.escape=\\\""),
            new("dq-string", "second line\" ; echo 'single $no' && [ -f /tmp/x ] || exit 1", "string=second line\"", "punctuation=;", "function=echo", "string='single $no'", "operator=&&", "punctuation=[", "attribute=-f", "punctuation=]", "operator=||", "function=exit", "number=1"),
            new("", "for f in *.txt; do cat \"$f\" | grep -i --color foo > out.log; done", "keyword=for", "keyword=in", "punctuation=;", "keyword=do", "string=\"", "variable=$f", "string=\"", "operator=|", "attribute=-i", "attribute=--color", "operator=>", "punctuation=;", "keyword=done"),
            new("", "x=$(date +%s) y=`uname` n=42 #trailing", "variable=x", "operator==", "punctuation=(", "punctuation=)", "operator==", "string=`uname`", "operator==", "number=42", "comment=#trailing"),
            new("", "")]),
        ("yaml", [
            new("", "---", "meta=---"),
            new("", "# comment", "comment=# comment"),
            new("", "name: \"quoted \\\"x\\\"\"   # trailing", "property=name", "string=\"quoted ", "string.escape=\\\"", "string=x", "string.escape=\\\"", "string=\"", "comment=# trailing"),
            new("", "version: 1.2", "property=version", "number=1.2"),
            new("", "items:", "property=items"),
            new("", "  - key: value with colon: inside", "punctuation=-", "property=key"),
            new("", "  - &anchor {a: 1, b: [x, y]}", "punctuation=-", "variable=&anchor", "punctuation={", "property=a", "number=1", "punctuation=,", "property=b", "punctuation=[", "punctuation=,", "punctuation=]}"),
            new("", "  - *anchor", "punctuation=-", "variable=*anchor"),
            new("", "  - 'it''s': yes", "punctuation=-", "string='it''s'", "constant=yes"),
            new("", "text: |", "property=text", "operator=|"),
            new("", "  block text"),
            new("", "empty: ~", "property=empty", "constant=~"),
            new("", "url: http://example.com/a:b", "property=url"),
            new("", "")]),
        ("neon", [
            new("", "# comment", "comment=# comment"),
            new("", "parameters:", "property=parameters", "operator=:"),
            new("", "\tname: 'it''s'", "property=name", "operator=:", "string='it''s'"),
            new("", "\tdebug: %debugMode%", "property=debug", "operator=:", "variable=%debugMode%"),
            new("", "services:", "property=services", "operator=:"),
            new("", "\t- App\\Model\\Foo(@db, 'x', debug: true)", "punctuation=-", "type=App\\Model\\Foo", "punctuation=(", "variable=@db", "punctuation=,", "string='x'", "punctuation=,", "property=debug", "operator=:", "constant=true", "punctuation=)"),
            new("", "\trouter: App\\Router(::method)", "property=router", "operator=:", "type=App\\Router", "punctuation=(", "operator=::", "punctuation=)"),
            new("", "\tmulti: '''", "property=multi", "operator=:", "string='''"),
            new("multiline-sq-string", "\t\ttext", "string=\t\ttext"),
            new("multiline-sq-string", "\t'''", "string=\t'''"),
            new("", "\tother: [1, 2.5, \"s\"]", "property=other", "operator=:", "punctuation=[", "number=1", "punctuation=,", "number=2.5", "punctuation=,", "string=\"s\"", "punctuation=]"),
            new("", "")]),
        ("latte", [
            new("", "{* comment", "comment={* comment"),
            new("latte-comment", " more *}", "comment= more *}"),
            new("", "{layout 'layout.latte'}", "meta={", "keyword=layout", "string='layout.latte'", "meta=}"),
            new("", "<div n:if=\"$user\" class=\"x {$cls}\">", "tag=<div", "attribute=n:if", "operator==", "string=\"$user\"", "attribute=class", "operator==", "string=\"x {$cls}\"", "tag=>"),
            new("", "{foreach $items as $item}", "meta={", "keyword=foreach", "variable=$items", "keyword=as", "variable=$item", "meta=}"),
            new("", "  <p>{$item->name|upper} {if $a > 1}big{else}small{/if}</p>", "tag=<p>", "meta={", "variable=$item", "operator=->", "function=|upper", "meta=}", "meta={", "keyword=if", "variable=$a", "operator=>", "number=1", "meta=}", "meta={", "keyword=else", "meta=}", "meta={", "keyword=/if", "meta=}", "tag=</p>"),
            new("", "{/foreach}", "meta={", "keyword=/foreach", "meta=}"),
            new("", "{var $x = 'a'}", "meta={", "keyword=var", "variable=$x", "operator==", "string='a'", "meta=}"),
            new("", "</div>", "tag=</div>"),
            new("", "<style>.a { color: red }</style>", "tag=<style>", "type=.a", "punctuation={", "property=color", "punctuation=:", "constant=red", "punctuation=}", "tag=</style>"),
            new("", "")]),
        ("markdown", [
            new("", "# Title", "heading=# Title"),
            new("", "Some *emph* and **strong** and `code` and [link](http://x.y) text_with_underscores.", "emphasis=*emph*", "strong=**strong**", "code=`code`", "link=[link](http://x.y)"),
            new("", "> quote", "punctuation=>"),
            new("", "- item one", "punctuation=-"),
            new("", "1. numbered", "punctuation=1."),
            new("", "```js", "code=```js"),
            new("fenced-code", "# not a heading", "code=# not a heading"),
            new("fenced-code", "```", "code=```"),
            new("", "---", "punctuation=---"),
            new("", "<div class=\"x\">html</div> <!-- c", "tag=<div class=\"x\">", "tag=</div>", "comment=<!-- c"),
            new("comment", "omment -->", "comment=omment -->"),
            new("", "")]),
    ];
    }
}
