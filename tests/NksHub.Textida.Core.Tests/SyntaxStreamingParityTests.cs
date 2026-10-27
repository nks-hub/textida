using System.Diagnostics;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class SyntaxStreamingParityTests
{
    private const int LongRun = (1 << 20) + 333;

    public static TheoryData<string> Cases => new()
    {
        "json-key", "json-value", "json-key-spaces", "json-value-spaces",
        "javascript-identifier", "javascript-regex", "javascript-regex-fallback", "javascript-reopened-string",
        "html-script", "html-script-fallback", "html-style", "html-style-fallback",
        "css-property", "css-selector",
        "csharp-line-start", "csharp-not-line-start",
        "ini-key", "ini-string", "yaml-key", "yaml-string", "neon-key",
        "markdown-link", "markdown-code", "php-comment-close", "php-code-comment",
        "plaintext-url", "python-name", "shell-variable", "sql-quoted-name",
        "sql-mysql-comment", "sql-postgres-string", "sql-sqlite-quoted-name", "sql-tsql-bracket-name",
        "xml-entity", "latte-variable", "razor-expression", "sln-string",
        "c-string", "cpp-comment", "go-string", "gomod-comment", "gotmpl-action", "rust-comment", "toml-key", "toml-heading", "makefile-target",
        "cmake-comment", "meson-string", "ruby-comment", "erb-comment", "haml-tag", "slim-comment", "rbs-comment", "java-string",
        "kotlin-string", "lua-comment", "swift-string", "dart-string", "r-string", "powershell-string",
        "twig-comment", "blade-comment", "batch-comment", "vb-comment",
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Long_lines_match_the_full_grammar_at_the_start_middle_and_end(string scenario)
    {
        var sample = FixtureFor(scenario);
        Assert.True(sample.Line.Length > 1 << 20);
        var tokenizer = BuiltinGrammars.Tokenizer(sample.Grammar);
        var expected = Reference(tokenizer, [sample.Line, sample.NextLine]);
        Assert.Equal(sample.ProbeKind, KindAt(expected[0].Tokens, sample.Probe));

        using var document = Create(sample.Line + "\r\n" + sample.NextLine);
        using var session = new CheckpointSyntaxSession(document, tokenizer);
        int[] columns = [0, (32 << 10) - 16, (1 << 20) - 16, Math.Max(0, sample.Probe - 32), Math.Max(0, sample.Line.Length - 160)];
        foreach (int column in columns.Distinct())
            AssertWindow(expected[0], Complete(session, 0, 1, column, 192), column, 192);

        AssertWindow(expected[1], Complete(session, 1, 1, 0, 192), 0, 192);
    }

    [Fact]
    public void Fixtures_cover_every_builtin_grammar_including_the_embedded_php_grammar()
    {
        var covered = Cases.Cast<object[]>().Select(row => FixtureFor((string)row[0], 8).Grammar).Distinct().Order().ToArray();
        Assert.Equal(BuiltinGrammars.Registry.All.Select(grammar => grammar.Id).Order().ToArray(), covered);
    }

    [Fact]
    public void Editing_the_opening_comment_invalidates_the_viewport_after_the_long_line()
    {
        string body = new('x', LongRun);
        string text = "/*" + body + "\nvar after = 1;";
        var tokenizer = BuiltinGrammars.Tokenizer("javascript");
        using var document = Create(text);
        using var session = new CheckpointSyntaxSession(document, tokenizer);
        var before = Reference(tokenizer, ["/*" + body, "var after = 1;"]);
        AssertWindow(before[1], Complete(session, 1, 1, 0, 192), 0, 192);
        Assert.Equal("comment", KindAt(before[1].Tokens, 0));

        Assert.True(document.Replace(new ByteRange(0, 2), "//"u8.ToArray()).Applied);
        var pending = session.TokenizeViewport(1, 1, TimeSpan.Zero);
        Assert.False(pending.IsComplete);
        Assert.Empty(pending.Lines);
        Assert.Equal(document.Revision, pending.Revision);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        long scanned = session.BytesScanned;
        Assert.Throws<OperationCanceledException>(() =>
            session.TokenizeViewport(1, 1, TimeSpan.FromMilliseconds(2), cancellation.Token));
        Assert.Equal(scanned, session.BytesScanned);
        var after = Reference(tokenizer, ["//" + body, "var after = 1;"]);
        Assert.Equal("keyword", KindAt(after[1].Tokens, 0));
        AssertWindow(after[1], Complete(session, 1, 1, 0, 192), 0, 192);
    }

    [Theory]
    [InlineData("UTF-8")]
    [InlineData("UTF-16 LE")]
    [InlineData("UTF-16 BE")]
    [InlineData("UTF-32 LE")]
    [InlineData("UTF-32 BE")]
    public void Unicode_and_CRLF_boundaries_preserve_columns_after_long_lookahead(string codecName)
    {
        string key = new string('x', (32 << 10) - 5) + "😀ž" + new string('y', LongRun);
        string first = "{\"" + key + "\":\"value\"}";
        string second = "{\"další\": 12}";
        var codec = TextCodec.ByName(codecName);
        var tokenizer = BuiltinGrammars.Tokenizer("json");
        var expected = Reference(tokenizer, [first, second]);
        Assert.Equal("property", KindAt(expected[0].Tokens, (32 << 10) - 3));

        using var document = Create(first + "\r\n" + second, codec);
        using var session = new CheckpointSyntaxSession(document, tokenizer);
        AssertWindow(expected[0], Complete(session, 0, 1, (32 << 10) - 12, 48), (32 << 10) - 12, 48);
        AssertWindow(expected[0], Complete(session, 0, 1, first.Length - 48, 48), first.Length - 48, 48);
        AssertWindow(expected[1], Complete(session, 1, 1, 0, 192), 0, 192);
    }

    [Fact]
    public void Delimiters_split_at_the_byte_window_edge_do_not_change_following_line_state()
    {
        string line = "//" + new string('x', LongRun) + "/*";
        int fill = (32 << 10) + 7 - line.Length % ((32 << 10) + 8);
        line += new string('x', fill);
        var tokenizer = BuiltinGrammars.Tokenizer("javascript");
        string next = "var after = 1;";
        var expected = Reference(tokenizer, [line, next]);
        using var document = Create(line + "\r\n" + next);
        using var session = new CheckpointSyntaxSession(document, tokenizer);
        AssertWindow(expected[0], Complete(session, 0, 1, LongRun - 8, 32), LongRun - 8, 32);
        AssertWindow(expected[1], Complete(session, 1, 1, 0, 192), 0, 192);
    }

    private sealed record Fixture(string Grammar, string Line, string NextLine, int Probe, string? ProbeKind);

    private static Fixture FixtureFor(string scenario, int runLength = LongRun)
    {
        string run = new('x', runLength);
        string spaces = new(' ', runLength);
        Fixture InRun(string grammar, string prefix, string suffix, string? kind, string next = "var after = 1;") =>
            new(grammar, prefix + run + suffix, next, prefix.Length + runLength - 4, kind);
        Fixture At(string grammar, string line, string marker, string? kind, string next = "var after = 1;") =>
            new(grammar, line, next, line.IndexOf(marker, StringComparison.Ordinal), kind);

        return scenario switch
        {
            "json-key" => InRun("json", "{\"", "\":1}", "property", "{\"next\":2}"),
            "json-value" => InRun("json", "[\"", "\"]", "string", "{\"next\":2}"),
            "json-key-spaces" => At("json", "{\"key\"" + spaces + ":1}", "key", "property"),
            "json-value-spaces" => At("json", "[\"value\"" + spaces + ",1]", "value", "string"),
            "javascript-identifier" => At("javascript", run + "1var", "var", null),
            "javascript-regex" => InRun("javascript", "var value = /[", "\"a]/g;", "regex"),
            "javascript-regex-fallback" => At("javascript", "var value = /[" + run + "\";", "/", "operator"),
            "javascript-reopened-string" => At("javascript", "\"" + run + "\"; var r = /[\"" + new string('y', 40_000) + "]/;", "/[", "regex"),
            "html-script" => At("html", "<script data-name=\"" + run + "\">var after = 1;</script>", "var after", "keyword"),
            "html-script-fallback" => InRun("html", "<script data-name=\"", "\"", "string", ">var after = 1;</script>"),
            "html-style" => At("html", "<style data-name=\"" + run + "\">a {color:red;}</style>", "color", "property"),
            "html-style-fallback" => InRun("html", "<style data-name=\"", "\"", "string", ">a {color:red;}</style>"),
            "css-property" => At("css", "a {width:" + spaces + "1px;}", "width", "property", "b {height:2px;}"),
            "css-selector" => At("css", "a:" + spaces + "hover {color:red;}", "a:", null, "b {height:2px;}"),
            "csharp-line-start" => At("csharp", spaces + "#define FLAG", "#define", "preprocessor"),
            "csharp-not-line-start" => At("csharp", "x" + spaces + "#define FLAG", "#define", null),
            "ini-key" => InRun("ini", "", "=value", "property", "next=2"),
            "ini-string" => InRun("ini", "key=\"", "\"", "string", "next=2"),
            "yaml-key" => InRun("yaml", "", ": value", "property", "next: 2"),
            "yaml-string" => InRun("yaml", "key: \"", "\"", "string", "next: 2"),
            "neon-key" => InRun("neon", "", ": value", "property", "next: 2"),
            "markdown-link" => InRun("markdown", "[", "](https://example.test/)", "link", "# Next"),
            "markdown-code" => InRun("markdown", "`", "`", "code", "# Next"),
            "php-comment-close" => At("php", "<?php //" + run + "?> <b>text</b>", "<b>", "tag", "<i>next</i>"),
            "php-code-comment" => InRun("php-code", "//", "?> $next = 2;", "comment", "$next = 3;"),
            "plaintext-url" => InRun("plaintext", "https://example.test/", " end", "link", "plain text"),
            "python-name" => InRun("python", "def ", "():", "function", "pass"),
            "shell-variable" => InRun("shell", "echo ${", "}", "variable", "echo next"),
            "sql-quoted-name" => InRun("sql", "SELECT `", "`;", "variable", "SELECT 2;"),
            "sql-mysql-comment" => InRun("sql-mysql", "# ", "", "comment", "SELECT 2;"),
            "sql-postgres-string" => InRun("sql-postgres", "SELECT '", "';", "string", "SELECT 2;"),
            "sql-sqlite-quoted-name" => InRun("sql-sqlite", "SELECT \"", "\";", "variable", "SELECT 2;"),
            "sql-tsql-bracket-name" => InRun("sql-tsql", "SELECT [", "];", "variable", "SELECT 2;"),
            "xml-entity" => InRun("xml", "<a>&", ";</a>", "string.escape", "<next/>"),
            "latte-variable" => InRun("latte", "{$", "}", "variable", "{if true}next{/if}"),
            "razor-expression" => InRun("razor", "@(\"", "\")", "string", "<p>@next</p>"),
            "sln-string" => InRun("sln", "Project(\"", "\")", "string", "EndProject"),
            "c-string" => InRun("c", "char *s = \"", "\";", "string", "int x;"),
            "cpp-comment" => InRun("cpp", "// ", "", "comment", "int x;"),
            "go-string" => InRun("go", "s := \"", "\"", "string", "x := 1"),
            "gomod-comment" => InRun("gomod", "// ", "", "comment", "go 1.22"),
            "gotmpl-action" => InRun("gotmpl", "{{ .", " }}", "property", "<p>next</p>"),
            "rust-comment" => InRun("rust", "// ", "", "comment", "fn next() {}"),
            "toml-key" => InRun("toml", "", " = 1", "property", "next = 2"),
            "toml-heading" => InRun("toml", "[", "]", "heading", "[next]"),
            "makefile-target" => InRun("makefile", "", ": dep", "function", "next:"),
            "cmake-comment" => InRun("cmake", "# ", "", "comment", "set(X 1)"),
            "meson-string" => InRun("meson", "x = '", "'", "string", "y = 1"),
            "ruby-comment" => InRun("ruby", "# ", "", "comment", "puts 1"),
            "erb-comment" => InRun("erb", "<%# ", " %>", "comment", "<p>next</p>"),
            "haml-tag" => InRun("haml", "%", "", "tag", "%p next"),
            "slim-comment" => InRun("slim", "/ ", "", "comment", "p next"),
            "rbs-comment" => InRun("rbs", "# ", "", "comment", "class A end"),
            "java-string" => InRun("java", "String s = \"", "\";", "string", "int x;"),
            "kotlin-string" => InRun("kotlin", "val s = \"", "\"", "string", "val y = 1"),
            "lua-comment" => InRun("lua", "-- ", "", "comment", "print(1)"),
            "swift-string" => InRun("swift", "let s = \"", "\"", "string", "let y = 1"),
            "dart-string" => InRun("dart", "var s = '", "';", "string", "var y = 1;"),
            "r-string" => InRun("r", "x <- \"", "\"", "string", "y <- 1"),
            "powershell-string" => InRun("powershell", "$s = \"", "\"", "string", "$y = 1"),
            "twig-comment" => InRun("twig", "{# ", " #}", "comment", "<p>next</p>"),
            "blade-comment" => InRun("blade", "{{-- ", " --}}", "comment", "<p>next</p>"),
            "batch-comment" => InRun("batch", "rem ", "", "comment", "echo next"),
            "vb-comment" => InRun("vb", "' ", "", "comment", "Dim y = 1"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }

    private static Doc Create(string text, TextCodec? codec = null)
    {
        codec ??= TextCodec.Utf8;
        IBackendFactory backend = codec.Kind is CodecKind.Utf16 or CodecKind.Utf32
            ? ReferenceBackendFactory.Instance : new PagedBackendFactory(startIndexing: false);
        return Doc.FromBytes(codec.Encode(text), backend, new DocumentOptions { Format = codec });
    }

    private static LineTokens[] Reference(Tokenizer tokenizer, string[] text)
    {
        var state = tokenizer.InitialState;
        var result = new LineTokens[text.Length];
        for (int line = 0; line < text.Length; line++)
        {
            var tokens = new List<Token>();
            var end = tokenizer.TokenizeLine(text[line], state, tokens);
            result[line] = new(line, tokens, state, end);
            state = end;
        }
        return result;
    }

    private static string? KindAt(IReadOnlyList<Token> tokens, int column) =>
        tokens.Where(token => token.Start <= column && column < token.End).Select(token => token.Kind).FirstOrDefault();

    private static SyntaxViewport Complete(CheckpointSyntaxSession session, long first, int count, int column, int columns)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            var result = session.TokenizeViewport(first, count, TimeSpan.FromMilliseconds(2), firstColumn: column, columnCount: columns);
            Assert.NotEqual(SyntaxViewportStatus.FullLineRequired, result.Status);
            if (result.IsComplete) return result;
        } while (timer.Elapsed < TimeSpan.FromSeconds(20));
        throw new TimeoutException("The requested syntax viewport did not complete.");
    }

    private static void AssertWindow(LineTokens expected, SyntaxViewport viewport, int column, int columns)
    {
        Assert.True(viewport.IsComplete);
        var actual = Assert.Single(viewport.Lines);
        Assert.Equal(expected.Line, actual.Line);
        Assert.Same(expected.StartState, actual.StartState);
        Assert.Same(expected.EndState, actual.EndState);
        int endColumn = column + columns;
        var clipped = expected.Tokens.Where(token => token.End > column && token.Start < endColumn)
            .Select(token => new Token(Math.Max(token.Start, column), Math.Min(token.End, endColumn) - Math.Max(token.Start, column), token.Kind))
            .ToArray();
        Assert.Equal(clipped, actual.Tokens);
    }
}
