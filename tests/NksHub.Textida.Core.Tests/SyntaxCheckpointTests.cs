using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Syntax;
using Xunit.Abstractions;
using Doc = NksHub.Textida.Core.Documents.Document;
using NksHub.Textida.Document.Contract;
using System.Runtime.CompilerServices;

namespace NksHub.Textida.Core.Tests;

public class SyntaxCheckpointTests(ITestOutputHelper output)
{
    private static Doc Create(string text, TextCodec? codec = null)
    {
        codec ??= TextCodec.Utf8;
        byte[] bytes = codec.Encode(text);
        IBackendFactory factory = codec.Kind is CodecKind.Utf16 or CodecKind.Utf32
            ? ReferenceBackendFactory.Instance : PagedBackendFactory.Instance;
        return Doc.FromBytes(bytes, factory, new DocumentOptions { Format = codec });
    }

    private static SyntaxViewport Complete(CheckpointSyntaxSession session, long first, int count,
        int firstColumn = 0, int columnCount = 4096)
    {
        for (int attempt = 0; attempt < 10000; attempt++)
        {
            var result = session.TokenizeViewport(first, count, TimeSpan.FromMilliseconds(2),
                firstColumn: firstColumn, columnCount: columnCount);
            if (result.IsComplete) return result;
        }
        throw new InvalidOperationException("Syntax catch-up did not finish.");
    }

    private static void AssertReference(string text, Tokenizer tokenizer, SyntaxViewport actual, int first, int count)
    {
        var expected = SyntaxHelpers.TokenizeAll(tokenizer, Regex.Split(text, "\r\n|\r|\n"));
        Assert.Equal(expected.Skip(first).Take(count).Count(), actual.Lines.Count);
        foreach (var line in actual.Lines)
        {
            var reference = expected[(int)line.Line];
            Assert.Same(reference.StartState, line.StartState);
            Assert.Same(reference.EndState, line.EndState);
            Assert.Equal(reference.Tokens, line.Tokens);
        }
    }

    [Theory]
    [InlineData("UTF-8")]
    [InlineData("UTF-16 LE")]
    [InlineData("UTF-16 BE")]
    [InlineData("UTF-32 LE")]
    [InlineData("UTF-32 BE")]
    [InlineData("Windows-1250")]
    public void Reads_multiline_state_with_each_codec_and_mixed_line_endings(string codecName)
    {
        string text = "/* příliš žluťoučký\r\n" + new string('x', 100_000) + "\r*/\nvar value = 12;\r\n";
        var tokenizer = BuiltinGrammars.Tokenizer("javascript");
        using var document = Create(text, TextCodec.ByName(codecName));
        using var session = new CheckpointSyntaxSession(document, tokenizer);
        AssertReference(text, tokenizer, Complete(session, 2, 3), 2, 3);
    }

    [Fact]
    public void Zero_slice_and_cancellation_do_not_publish_guessed_state()
    {
        using var document = Create("/*\ncomment\n*/\nvar x = 1;");
        using var session = new CheckpointSyntaxSession(document, BuiltinGrammars.Tokenizer("javascript"));
        var pending = session.TokenizeViewport(3, 1, TimeSpan.Zero);
        Assert.False(pending.IsComplete);
        Assert.Empty(pending.Lines);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => session.TokenizeViewport(3, 1, TimeSpan.FromSeconds(1), cts.Token));
        var complete = Complete(session, 3, 1);
        Assert.True(complete.Lines[0].StartState.IsInitial);
        long scanned = session.BytesScanned;
        Assert.True(session.TokenizeViewport(3, 1, TimeSpan.Zero).IsComplete);
        Assert.Equal(scanned, session.BytesScanned);
    }

    [Fact]
    public void Editing_a_comment_before_the_viewport_discards_stale_tokens()
    {
        string text = "/*\n" + string.Concat(Enumerable.Repeat("comment\n", 30_000)) + "*/\nvar end = 1;";
        var tokenizer = BuiltinGrammars.Tokenizer("javascript");
        using var document = Create(text);
        using var session = new CheckpointSyntaxSession(document, tokenizer);
        AssertReference(text, tokenizer, Complete(session, 29_999, 4), 29_999, 4);
        Assert.True(document.Replace(new ByteRange(0, 2), "//"u8.ToArray()).Applied);
        text = "//" + text[2..];
        var pending = session.TokenizeViewport(29_999, 4, TimeSpan.Zero);
        Assert.False(pending.IsComplete);
        Assert.Empty(pending.Lines);
        var result = Complete(session, 29_999, 4);
        Assert.Equal(document.Revision, result.Revision);
        AssertReference(text, tokenizer, result, 29_999, 4);
    }

    [Fact]
    public void Random_edits_and_viewport_changes_match_a_full_pass()
    {
        var random = new Random(1973);
        var tokenizer = BuiltinGrammars.Tokenizer("javascript");
        string text = string.Concat(Enumerable.Repeat("var x = 1; /* same */\n", 1000));
        using var document = Create(text);
        using var session = new CheckpointSyntaxSession(document, tokenizer);
        string[] fragments = ["/*", "*/", "\n", "\r\n", "'", "\"", "var a=2;", "", "//", "`"];
        for (int step = 0; step < 60; step++)
        {
            int offset = random.Next(text.Length + 1);
            int removed = Math.Min(random.Next(8), text.Length - offset);
            string inserted = fragments[random.Next(fragments.Length)];
            Assert.True(document.Replace(new ByteRange(offset, offset + removed), Encoding.UTF8.GetBytes(inserted)).Applied);
            text = text[..offset] + inserted + text[(offset + removed)..];
            int count = Regex.Split(text, "\r\n|\r|\n").Length;
            int first = random.Next(count);
            AssertReference(text, tokenizer, Complete(session, first, 5), first, 5);
        }
    }

    [Fact]
    public void Resumes_inside_a_long_line_and_keeps_only_visible_columns()
    {
        string text = "/*" + new string('x', 8 << 20) + "*/ var y = 2;\nnext();";
        using var document = Create(text);
        var tokenizer = BuiltinGrammars.Tokenizer("javascript");
        using var session = new CheckpointSyntaxSession(document, tokenizer);
        var pending = session.TokenizeViewport(1, 1, TimeSpan.FromMilliseconds(1));
        Assert.False(pending.IsComplete);
        Assert.Empty(pending.Lines);
        Assert.InRange(session.BytesScanned, 1, document.ByteLength - 1);
        var result = Complete(session, 1, 1);
        AssertReference(text, tokenizer, result, 1, 1);
        var longLine = Complete(session, 0, 1, (8 << 20) + 2, 13);
        Assert.All(longLine.Lines[0].Tokens, token => Assert.InRange(token.Start, (8 << 20) + 2, (8 << 20) + 15));
        Assert.Contains(longLine.Lines[0].Tokens, token => token.Kind == TokenKinds.Keyword);
    }

    [Fact]
    public void CrLf_at_a_byte_window_edge_is_one_terminator()
    {
        string text = "/*" + new string('x', (32 << 10) + 5) + "\r\n*/\nvar x = 1;";
        var tokenizer = BuiltinGrammars.Tokenizer("javascript");
        using var document = Create(text);
        using var session = new CheckpointSyntaxSession(document, tokenizer);
        AssertReference(text, tokenizer, Complete(session, 1, 3), 1, 3);
    }

    [Fact]
    public void State_convergence_reuses_unmodified_checkpoints()
    {
        string text = string.Concat(Enumerable.Repeat("var x = 1;\n", 80_000));
        var tokenizer = BuiltinGrammars.Tokenizer("javascript");
        using var document = Create(text);
        using var session = new CheckpointSyntaxSession(document, tokenizer);
        Complete(session, 79_998, 2);
        Assert.True(document.Replace(new ByteRange(4, 5), "y"u8.ToArray()).Applied);
        long before = session.BytesScanned;
        AssertReference("var y" + text[5..], tokenizer, Complete(session, 79_998, 2), 79_998, 2);
        Assert.True(session.BytesScanned - before < 200_000);
    }

    [Fact]
    public void Empty_and_trailing_empty_lines_have_real_complete_states()
    {
        foreach (string text in new[] { "", "\n", "\r\n", "a\n" })
        {
            var tokenizer = BuiltinGrammars.Tokenizer("javascript");
            using var document = Create(text);
            using var session = new CheckpointSyntaxSession(document, tokenizer);
            AssertReference(text, tokenizer, Complete(session, 0, 5), 0, 5);
        }
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void Eight_MiB_comment_catch_up_uses_the_approved_budget()
    {
        string comment = "/*" + new string('x', (8 << 20) - 4) + "*/\nvar after = 1;";
        var tokenizer = BuiltinGrammars.Tokenizer("javascript");
        tokenizer.TokenizeLine("/* warm */ var x;", tokenizer.InitialState, []);
        using var document = Create(comment);
        long memoryBefore = GC.GetTotalMemory(true);
        using var session = new CheckpointSyntaxSession(document, tokenizer);
        var started = Stopwatch.StartNew();
        var result = Complete(session, 1, 1);
        started.Stop();
        AssertReference(comment, tokenizer, result, 1, 1);
        long retained = GC.GetTotalMemory(true) - memoryBefore;
        output.WriteLine($"catch-up-ms={started.Elapsed.TotalMilliseconds:F2}; checkpoint-bound-bytes={session.CheckpointMemoryUpperBoundBytes}; retained-session-bytes={retained}; scanned={session.BytesScanned}");
        Assert.True(session.CheckpointMemoryUpperBoundBytes <= 16L << 20);
        Assert.True(started.Elapsed <= TimeSpan.FromMilliseconds(250), $"Catch-up took {started.Elapsed.TotalMilliseconds:F2} ms.");
        GC.KeepAlive(session);
    }

    [Theory]
    [InlineData("javascript", "//", "/*")]
    [InlineData("csharp", "///", "/*")]
    [InlineData("sql", "--", "/*")]
    [InlineData("python", "#", "\"\"\"")]
    [InlineData("shell", "#", "'")]
    [InlineData("json", "//", "/*")]
    [InlineData("neon", "#", "'''")]
    [InlineData("yaml", "#", "'")]
    [InlineData("ini", ";", "'")]
    public void A_line_comment_crossing_windows_does_not_open_a_region(string grammar, string start, string ending)
    {
        string text = start + new string('x', (32 << 10) + 8 - start.Length) + ending + "\nvar next = 1;";
        var tokenizer = BuiltinGrammars.Tokenizer(grammar);
        using var document = Create(text);
        using var session = new CheckpointSyntaxSession(document, tokenizer);
        var actual = Complete(session, 0, 2, columnCount: 65536);
        AssertReference(text, tokenizer, actual, 0, 2);
    }

    [Fact]
    public void Keyword_suffix_of_a_word_crossing_windows_is_not_a_new_keyword()
    {
        string text = new string('x', (32 << 10) + 8) + "var\n";
        var tokenizer = BuiltinGrammars.Tokenizer("javascript");
        using var document = Create(text);
        using var session = new CheckpointSyntaxSession(document, tokenizer);
        AssertReference(text, tokenizer, Complete(session, 0, 2, columnCount: 65536), 0, 2);
    }

    [Fact]
    public void Interned_unused_child_state_can_be_collected_while_its_parent_lives()
    {
        var registry = new GrammarRegistry();
        var grammar = Grammar.Parse("""
            {"formatVersion":1,"id":"gc-test","name":"State lifetime","rules":[
              {"name":"comment","begin":"/\\*","end":"\\*/","token":"comment","multiline":true}
            ]}
            """, registry);
        var tokenizer = new Tokenizer(grammar);
        var parent = tokenizer.InitialState;
        var weak = MakeUnusedState(tokenizer, parent);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(weak.TryGetTarget(out _));
        Assert.Same(parent, tokenizer.InitialState);
        GC.KeepAlive(parent);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<LineState> MakeUnusedState(Tokenizer tokenizer, LineState parent) =>
        new(tokenizer.TokenizeLine("/*", parent, []));

    public static IEnumerable<object[]> LongLookaheadCases()
    {
        string word = new('x', 40_000);
        yield return ["json", "{\"" + word + "\":1}"];
        yield return ["html", "<script data-x=\"" + word + "\">const x=1;\n</script>"];
        yield return ["csharp", "x" + new string(' ', 40_000) + "#define X"];
        yield return ["css", "a:" + new string(' ', 40_000) + "hover {}"];
        yield return ["ini", "key=\"" + word + "\""];
        yield return ["yaml", "key: \"" + word + "\""];
        yield return ["markdown", "**" + word + "**"];
        yield return ["javascript", word + "1var\n"];
    }

    [Theory]
    [MemberData(nameof(LongLookaheadCases))]
    public void Full_line_scan_preserves_unbounded_lookahead_and_real_line_start(string grammar, string text)
    {
        var tokenizer = BuiltinGrammars.Tokenizer(grammar);
        using var document = Create(text);
        using var session = new CheckpointSyntaxSession(document, tokenizer);
        AssertReference(text, tokenizer, Complete(session, 0, 3, columnCount: 65536), 0, 3);
    }

    [Theory]
    [InlineData("json", "{\"", "\":1}")]
    [InlineData("html", "<script data-x=\"", "\">const x=1;</script>")]
    [InlineData("css", "a:", "hover {}")]
    [InlineData("javascript", "x", "var")]
    public void Unresolved_long_line_stops_with_an_explicit_status(string grammar, string before, string after)
    {
        string text = before + new string('x', CheckpointSyntaxSession.FullLineLimitChars + 1) + after + "\nnext();";
        using var document = Create(text);
        using var session = new CheckpointSyntaxSession(document, BuiltinGrammars.Tokenizer(grammar));
        SyntaxViewport result;
        do { result = session.TokenizeViewport(1, 1, TimeSpan.FromMilliseconds(20)); }
        while (result.Status == SyntaxViewportStatus.Pending);
        Assert.Equal(SyntaxViewportStatus.FullLineRequired, result.Status);
        Assert.False(result.IsComplete);
        Assert.Empty(result.Lines);
        long read = session.BytesScanned;
        Assert.Equal(SyntaxViewportStatus.FullLineRequired, session.TokenizeViewport(1, 1, TimeSpan.FromSeconds(1)).Status);
        Assert.Equal(read, session.BytesScanned);
    }

    [Fact]
    public void Leaving_and_reentering_the_same_region_does_not_prove_streaming_continuity()
    {
        string text = "\"" + new string('x', 2 << 20) + "\"; var r = /[\"" + new string('y', 40000) + "]/;\nnext();";
        using var document = Create(text);
        using var session = new CheckpointSyntaxSession(document, BuiltinGrammars.Tokenizer("javascript"));
        SyntaxViewport result;
        do { result = session.TokenizeViewport(1, 1, TimeSpan.FromMilliseconds(20)); }
        while (result.Status == SyntaxViewportStatus.Pending);
        Assert.Equal(SyntaxViewportStatus.FullLineRequired, result.Status);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void Tokenization_does_not_wait_for_the_document_line_index()
    {
        const string text = "/*\ncomment\n*/\nvar x=1;";
        using var document = Doc.FromBytes(Encoding.UTF8.GetBytes(text), new PagedBackendFactory(startIndexing: false),
            new DocumentOptions { Format = TextCodec.Utf8 });
        var tokenizer = BuiltinGrammars.Tokenizer("javascript");
        using var session = new CheckpointSyntaxSession(document, tokenizer);
        AssertReference(text, tokenizer, Complete(session, 3, 1), 3, 1);
    }
}
