using NksHub.Textida.Plugins.CodeExplorer;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P4.4.1: the symbols of HTML, PHP, CSS, C# and Markdown text from the rules of each language, with the place of every name.</summary>
public class CodeExplorerOutlineTests
{
    /// <summary>Kind, name and the text the range selects, nested by two spaces per level.</summary>
    private static string[] Tree(string language, string text)
    {
        var symbols = Outlines.Extract(language, text);
        string Depth(int parent) => parent < 0 ? "" : "  " + Depth(symbols[parent].Parent);
        return [.. symbols.Select(s => $"{Depth(s.Parent)}{s.Kind} {s.Name} [{(language == "html" ? "" : text[s.Start..s.End])}]")];
    }

    [Fact]
    public void Markdown_headings_nest_by_level_and_fenced_code_and_front_matter_are_not_read()
    {
        const string md = "---\ntitle: x\n# not a heading\n---\n# One\n\ntext\n## Two ##\n```\n# in code\n```\n### Three\n# Four\r\n";
        Assert.Equal(["h1 One [One]", "  h2 Two [Two]", "    h3 Three [Three]", "h1 Four [Four]"], Tree("markdown", md));
    }

    [Fact]
    public void Css_rules_nest_in_at_rules_and_comments_strings_and_declarations_are_not_rules()
    {
        const string css = "/* a { } */\n@media (min-width: 600px) {\n  .a, .b > c { color: red; background: url(\"x{.png\"); }\n  .d:hover { margin: 0 }\n}\n:root { --x: 1 }\n@import url(a.css);\n";
        Assert.Equal(["at-rule @media (min-width: 600px) [@media (min-width: 600px)]", "  rule .a, .b > c [.a, .b > c]", "  rule .d:hover [.d:hover]", "rule :root [:root]"], Tree("css", css));
    }

    [Fact]
    public void Html_gives_headings_landmarks_and_elements_with_an_id_nested_by_the_open_elements()
    {
        const string html = "<!-- <h1>no</h1> -->\n<body><header id=\"top\"><h1 class=\"t\">Title <b>x</b></h1></header>\n<main><div id=a><img id=i src=x><h2>Part</h2></div></main>\n<script>var s = '<h1>no</h1>';</script><p>text</p></body>";
        var symbols = Outlines.Extract("html", html);
        Assert.Equal(["header#top", "Title", "main", "div#a", "img#i", "Part"], symbols.Select(s => s.Name));
        Assert.Equal([-1, 0, -1, 2, 3, 3], symbols.Select(s => s.Parent));
        Assert.Equal(["landmark", "h1", "landmark", "element", "element", "h2"], symbols.Select(s => s.Kind));
    }

    [Fact]
    public void Php_finds_namespaces_types_functions_and_methods_outside_comments_strings_and_html()
    {
        const string php = "<p>function nope() {}</p>\n<?php\nnamespace App\\Models;\n// function commented() {}\n$s = 'class Fake {}';\ninterface Shape { public function area(): float; }\nabstract class Box extends Base implements Shape\n{\n    const X = 1;\n    public function area(): float { return Foo::class; }\n    private static function make(int $a, string ...$rest) { function inner() {} }\n}\nfunction helper(array $x = []) { return new class { }; }\nenum Suit: string { case A = 'a'; }\n?>\n<p>class Html {}</p>";
        Assert.Equal([
            "namespace App\\Models [App\\Models]",
            "  interface Shape [Shape]",
            "    method area() [area]",
            "  class Box [Box]",
            "    method area() [area]",
            "    method make(int $a, string ...$rest) [make]",
            "      function inner() [inner]",
            "  function helper(array $x = []) [helper]",
            "  enum Suit [Suit]"], Tree("php", php));
    }

    [Fact]
    public void CSharp_finds_namespaces_types_and_members_and_skips_bodies_fields_comments_and_strings()
    {
        const string cs = """
            using System;
            namespace Demo.App
            {
                /// <summary>class Ghost { }</summary>
                public sealed partial class Box<T> : Base where T : class
                {
                    private int _field = 1;
                    private readonly string[] _names = new[] { "a", "b" };
                    public Box(int size) : base(size) { var s = "void Fake() { }"; }
                    [Obsolete("x")] public static int Count { get; set; }
                    public int Double => _field * 2;
                    public string Name { get => "n"; }
                    public abstract void Run(int a, string b);
                    public T Get<U>(U value) where U : struct
                    {
                        int Local() { return 1; }
                        return default;
                    }
                    public int Twice(int a) => a * 2;
                    public event Action Changed;
                    public static bool operator ==(Box<T> a, Box<T> b) => true;
                    public enum Mode { One, Two }
                    public record Point(int X, int Y);
                    public delegate void Handler(object sender);
                }
                public interface IThing { void Do(); int Size { get; } }
                public record struct Pair(int A, int B) { }
            }
            """;
        Assert.Equal([
            "namespace Demo.App [Demo.App]",
            "  class Box [Box]",
            "    constructor Box(int size) [Box]",
            "    property Count [Count]",
            "    property Double [Double]",
            "    property Name [Name]",
            "    method Run(int a, string b) [Run]",
            "    method Get(U value) [Get]",
            "    method Twice(int a) [Twice]",
            "    enum Mode [Mode]",
            "    record Point [Point]",
            "    delegate Handler(object sender) [Handler]",
            "  interface IThing [IThing]",
            "    method Do() [Do]",
            "    property Size [Size]",
            "  record Pair [Pair]"], Tree("csharp", cs));
    }

    [Fact]
    public void A_file_scoped_namespace_holds_the_types_after_it()
    {
        Assert.Equal(["namespace A.B [A.B]", "  class C [C]", "    method M() [M]"], Tree("csharp", "namespace A.B;\nclass C { void M() { if (true) { } } }\n"));
    }

    [Fact]
    public void Text_the_rules_do_not_understand_gives_no_symbols_and_never_throws()
    {
        foreach (var language in new[] { "html", "php", "css", "csharp", "markdown" })
        {
            Assert.Empty(Outlines.Extract(language, ""));
            _ = Outlines.Extract(language, "{{{ <<< \" ' /* <?php function ( class { [ ---\n```\n# ");
        }
        Assert.False(Outlines.Supports("lua"));
        Assert.Empty(Outlines.Extract("lua", "function f() end"));
    }

    [Theory]
    [InlineData("javascript", "// function no() {}\nexport class A {\n  go(x) { return `}`; }\n}\nconst f = async (a) => { };\nfunction g(b) { }\n", new[] { "class A", "  method go(x)", "function f(a)", "function g(b)" })]
    [InlineData("python", "# def no(): pass\nclass A:\n    \"\"\"def no(): pass\"\"\"\n    def m(self): pass\ndef f(): pass\n", new[] { "class A", "  method m(self)", "function f()" })]
    [InlineData("rust", "// fn no() {}\nmod m { pub fn f<T>(x: T) -> T { x } }\nimpl<T> Foo<T> {\n    fn g(&self) {}\n}\nstruct S { a: i32 }\n", new[] { "mod m", "  function f(x: T)", "impl Foo<T>", "  method g(&self)", "struct S" })]
    [InlineData("dart", "class A {\n  void m(int x) {\n    if (x > 0) { }\n  }\n}\nint f() => 1;\n", new[] { "class A", "  method m(int x)", "function f()" })]
    [InlineData("c", "#define MAX 3\nstruct P { int x; };\nstatic int add(int a, int b)\n{\n    if (a) { }\n    return a + b; // int no(void) {}\n}\n", new[] { "macro MAX", "struct P", "function add(int a, int b)" })]
    [InlineData("shell", "# no() {\nbuild() {\n  echo \"x() {\"\n}\nfunction deploy { :; }\n", new[] { "function build", "function deploy" })]
    [InlineData("cmake", "# project(no)\nproject(Demo)\nadd_executable(app main.c)\nfunction(helper arg)\nendfunction()\n", new[] { "project Demo", "add_executable app", "function helper" })]
    [InlineData("makefile", "CC := gcc\n.PHONY: all\nall: app\n\tgcc -o app\nclean:\n\trm app\n", new[] { "target all", "target clean" })]
    [InlineData("meson", "project('demo', 'c')\nexe = executable('app', 'main.c')\n# test('no', exe)\ntest('unit', exe)\n", new[] { "project demo", "executable app", "test unit" })]
    public void Other_languages_give_their_declarations_and_not_comments_or_strings(string language, string text, string[] expected) =>
        Assert.Equal(expected, Tree(language, text).Select(l => l[..l.LastIndexOf(" [", StringComparison.Ordinal)]));
}
