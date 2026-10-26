using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.Core.Tests;

public class SyntaxIncrementalTests
{
    private static readonly Dictionary<string, string[]> Fragments = new()
    {
        ["javascript"] = ["/*", "*/", "`", "${", "}", "'", "\"", "//", "x = 1;", "function f() {", "re = /a+/g", "\\", "return ", "0x1F", "1.5e3", "\n", "\n", " ", "foo(bar)"],
        ["html"] = ["<!--", "-->", "<script>", "</script>", "<style>", "</style>", "<?php", "<?=", "?>", "<p class=\"a\">", "</p>", "\"", "'", "/*", "*/", "`", "//", "$x", "&amp;", "\n", "\n", "text ", "{", "}", "color: red;"],
        ["php"] = ["<?php", "?>", "<<<EOT", "EOT;", "/*", "*/", "//", "\"", "'", "$a = 1;", "echo ", "<b>", "<!--", "-->", "\n", "\n", " "],
        ["sql"] = ["/*", "*/", "--", "'", "''", "\"", "`", "$$", "SELECT ", "FROM t", "WHERE ", ";", "\n", "\n", " ", "1"],
        ["python"] = ["\"\"\"", "'''", "\"", "'", "#", "def f():", "    return 1", "\\", "r'x'", "\n", "\n", " ", "class A:", "@dec"],
        ["markdown"] = ["```", "~~~", "# ", "*", "**", "`", "[a](b)", "<!--", "-->", "> ", "- ", "1. ", "---", "\n", "\n", "\n", "text ", "_"],
        ["csharp"] = ["/*", "*/", "//", "///", "\"", "@\"", "$\"", "\"\"\"", "{", "}", "'", "#region", "int x = 1;", "class A", "\n", "\n", " ", "\\"],
        ["yaml"] = ["key: ", "- ", "\"", "'", "#", "|", "---", "&a ", "*a", "{a: 1}", "[1, 2]", "\n", "\n", "  ", "true"],
        ["json"] = ["{", "}", "[", "]", "\"k\"", ":", ",", "\"", "/*", "*/", "//", "1.5", "null", "\n", "\n", " "],
        ["xml"] = ["<!--", "-->", "<![CDATA[", "]]>", "<?xml", "?>", "<a ", "b=\"1\"", "'", "\"", ">", "/>", "</a>", "&amp;", "\n", "\n", " ", "<!DOCTYPE"],
        ["shell"] = ["\"", "'", "`", "#", "$X", "${Y}", "if ", "then", "fi", "echo ", "\\", "\n", "\n", " ", "| ", "&& "],
        ["css"] = ["/*", "*/", "{", "}", "\"", "'", "a:hover", ".x", "#id", "color: red;", "@media ", "url(a.png)", "!important", "\n", "\n", " "],
        ["latte"] = ["{*", "*}", "{", "}", "{$x}", "{if $a}", "{/if}", "<p>", "</p>", "<!--", "-->", "'", "\"", "<script>", "</script>", "\n", "\n", " "],
        ["neon"] = ["key: ", "- ", "'", "''", "'''", "\"", "#", "@svc", "%p%", "Foo(", ")", "{", "}", "\n", "\n", "\t", "true"],
        ["ini"] = ["[s]", "k = v", ";", "#", "\"", "'", "=", "\n", "\n", " ", "1"],
    };

    public static TheoryData<string> GrammarIds() => new(Fragments.Keys);

    private static string RandomText(Random r, string[] fragments, int count)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < count; i++) sb.Append(fragments[r.Next(fragments.Length)]);
        return sb.ToString();
    }

    private static void AssertSame(IReadOnlyList<LineTokens> expected, IReadOnlyList<LineTokens> actual, string what)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.True(ReferenceEquals(expected[i].StartState, actual[i].StartState), $"{what}: line {expected[i].Line} starts in '{actual[i].StartState}', full scan says '{expected[i].StartState}'");
            Assert.True(expected[i].Tokens.SequenceEqual(actual[i].Tokens), $"{what}: tokens of line {expected[i].Line} differ");
        }
    }

    [Theory]
    [MemberData(nameof(GrammarIds))]
    public void Incremental_tokenization_equals_full_tokenization_over_random_edits(string grammar)
    {
        var tokenizer = BuiltinGrammars.Tokenizer(grammar);
        var fragments = Fragments[grammar];
        var r = new Random(20261026 + grammar.Length * 7919);
        var lines = StringLines.Of(RandomText(r, fragments, 300));
        var inc = new IncrementalTokenizer(tokenizer, lines);

        for (int step = 0; step < 250; step++)
        {
            int edits = r.Next(1, 4);
            for (int e = 0; e < edits; e++)
            {
                int line = r.Next(lines.Lines.Count);
                int column = r.Next(lines.Lines[line].Length + 1);
                int length = r.Next(4) == 0 ? r.Next(0, 90) : r.Next(0, 4);
                string insert = r.Next(5) == 0 ? "" : RandomText(r, fragments, r.Next(1, 4));
                var (first, removed, inserted) = lines.Edit(line, column, length, insert);
                if (r.Next(12) == 0) inc.Invalidate(first);
                else inc.OnEdit(first, removed, inserted);
            }

            var full = SyntaxHelpers.TokenizeAll(tokenizer, lines.Lines);
            if (step % 10 == 0)
            {
                AssertSame(full, inc.TokenizeViewport(0, lines.Lines.Count), $"{grammar} step {step} whole text");
            }
            else
            {
                int first = r.Next(lines.Lines.Count);
                int count = r.Next(1, 45);
                AssertSame(full.Skip(first).Take(count).ToList(), inc.TokenizeViewport(first, count), $"{grammar} step {step} viewport {first}+{count}");
                int probe = r.Next(lines.Lines.Count);
                Assert.True(ReferenceEquals(full[probe].StartState, inc.GetStartState(probe)), $"{grammar} step {step}: state of line {probe}");
            }
        }
    }

    private static StringLines JsLines(int count) => new(Enumerable.Range(0, count).Select(i => $"var x{i} = {i}; // line {i}"));

    [Fact]
    public void An_edit_that_keeps_the_state_rescans_one_line()
    {
        var lines = JsLines(1000);
        var inc = new IncrementalTokenizer(BuiltinGrammars.Tokenizer("javascript"), lines);
        inc.GetStartState(1000);
        long before = inc.LinesScanned;

        var (f, r, i) = lines.Edit(500, 4, 1, "yy");
        inc.OnEdit(f, r, i);
        inc.GetStartState(1000);

        Assert.Equal(1, inc.LinesScanned - before);
        Assert.Equal(1001, inc.ValidLines);
    }

    [Fact]
    public void An_opened_comment_is_followed_until_its_end_and_not_further()
    {
        var lines = JsLines(1000);
        lines.Lines[520] = "end */ var z = 1;";
        var inc = new IncrementalTokenizer(BuiltinGrammars.Tokenizer("javascript"), lines);
        inc.GetStartState(1000);
        long before = inc.LinesScanned;

        var (f, r, i) = lines.Edit(500, 0, 0, "/* ");
        inc.OnEdit(f, r, i);
        inc.GetStartState(1000);

        // Lines 500 to 520 end in a changed state; line 520 closes the comment, and the old state after it is the same again.
        Assert.Equal(21, inc.LinesScanned - before);
        Assert.True(ReferenceEquals(SyntaxHelpers.TokenizeAll(inc.Tokenizer, lines.Lines)[^1].EndState, inc.GetStartState(1000)));
    }

    [Fact]
    public void An_opened_comment_that_never_closes_runs_to_the_end_and_closing_it_converges_back()
    {
        var lines = JsLines(300);
        var inc = new IncrementalTokenizer(BuiltinGrammars.Tokenizer("javascript"), lines);
        inc.GetStartState(300);

        var (f, r, i) = lines.Edit(100, 0, 0, "/* ");
        inc.OnEdit(f, r, i);
        long before = inc.LinesScanned;
        inc.GetStartState(300);
        Assert.Equal(200, inc.LinesScanned - before);

        // Every kept state below is a comment state now, so closing the comment again changes all of them.
        (f, r, i) = lines.Edit(100, 0, 3, "");
        inc.OnEdit(f, r, i);
        before = inc.LinesScanned;
        inc.GetStartState(300);
        Assert.Equal(200, inc.LinesScanned - before);
        Assert.True(inc.GetStartState(300).IsInitial);
    }

    [Fact]
    public void Inserting_and_removing_lines_shifts_the_kept_states()
    {
        var lines = JsLines(200);
        var inc = new IncrementalTokenizer(BuiltinGrammars.Tokenizer("javascript"), lines);
        inc.GetStartState(200);
        long before = inc.LinesScanned;

        var (f, r, i) = lines.Edit(50, 3, 0, "\nvar a;\nvar b;\n");
        Assert.Equal((50, 1, 4), (f, r, i));
        inc.OnEdit(f, r, i);
        inc.GetStartState(lines.Lines.Count);
        Assert.Equal(4, inc.LinesScanned - before);

        before = inc.LinesScanned;
        (f, r, i) = lines.Edit(40, 5, 400, "");
        inc.OnEdit(f, r, i);
        inc.GetStartState(lines.Lines.Count);
        Assert.True(inc.LinesScanned - before <= 1);
        AssertSame(SyntaxHelpers.TokenizeAll(inc.Tokenizer, lines.Lines), inc.TokenizeViewport(0, lines.Lines.Count), "after removal");
    }

    [Fact]
    public void Cancellation_stops_the_catch_up_and_the_next_call_continues()
    {
        var lines = JsLines(5000);
        var inc = new IncrementalTokenizer(BuiltinGrammars.Tokenizer("javascript"), lines);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => inc.GetStartState(4000, cts.Token));
        Assert.Throws<OperationCanceledException>(() => inc.TokenizeViewport(10, 5, cts.Token));
        Assert.Equal(1, inc.ValidLines);

        Assert.True(inc.GetStartState(4000).IsInitial);
        Assert.Equal(4001, inc.ValidLines);
    }

    [Fact]
    public void A_viewport_costs_the_lines_down_to_it_and_no_more()
    {
        var lines = JsLines(10_000);
        var inc = new IncrementalTokenizer(BuiltinGrammars.Tokenizer("javascript"), lines);
        var view = inc.TokenizeViewport(2000, 50);
        Assert.Equal(50, view.Count);
        Assert.Equal(2000 + 50, inc.LinesScanned);
        Assert.Equal(2051, inc.ValidLines);
    }

    [Fact]
    public void The_text_is_not_read_beyond_what_the_viewport_needs()
    {
        var counting = new CountingLines(JsLines(100_000));
        var inc = new IncrementalTokenizer(BuiltinGrammars.Tokenizer("javascript"), counting);
        inc.TokenizeViewport(0, 40);
        Assert.Equal(40, counting.Reads);
    }

    private sealed class CountingLines(ILineSource inner) : ILineSource
    {
        public int Reads { get; private set; }
        public long LineCount => inner.LineCount;
        public string GetLine(long line)
        {
            Reads++;
            return inner.GetLine(line);
        }
    }
}
