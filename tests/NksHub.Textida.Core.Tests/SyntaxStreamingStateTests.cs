using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Syntax;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class SyntaxStreamingStateTests
{
    public static TheoryData<string> Grammars => new(BuiltinGrammars.Registry.All.Select(grammar => grammar.Id));

    private static readonly string[] Fragments =
    [
        "/*", "*/", "//", "#", "--", "\n", "\r\n", " ", "\t", "'", "\"", "\\\"", "\\'", "`", "**", "__",
        "{", "}", "[", "]", "(", ")", ":", ";", "-", "=", "return ", "var x=1;", "class A", "value", "123.4e-2",
        "<?php", "?>", "<script>", "</script>", "<a x=\"", "\"/>", "&amp;", "<style>", "color: red;", "</style>",
        "$name", "@value", "${value}", "[label](url)", " Žluťoučký ", "😀", "\u200C", "\u200D", "\u0301",
    ];

    [Theory]
    [MemberData(nameof(Grammars))]
    public void Random_short_text_uses_the_same_rule_priority_and_states_as_the_full_scan(string grammar)
    {
        var random = new Random(7315 + grammar.Length);
        var tokenizer = BuiltinGrammars.Tokenizer(grammar);
        for (int attempt = 0; attempt < 30; attempt++)
        {
            string text = string.Concat(Enumerable.Range(0, 35).Select(_ => Fragments[random.Next(Fragments.Length)]));
            var expected = SyntaxHelpers.TokenizeAll(tokenizer, Regex.Split(text, "\r\n|\n|\r"));
            using var document = Doc.FromBytes(Encoding.UTF8.GetBytes(text), PagedBackendFactory.Instance,
                new DocumentOptions { Format = TextCodec.Utf8 });
            using var session = new StreamingSyntaxSession(document, tokenizer);
            var actual = Complete(session, 0, expected.Count, 0, 65536);
            Assert.Equal(expected.Count, actual.Lines.Count);
            for (int line = 0; line < expected.Count; line++)
            {
                Assert.True(expected[line].Tokens.SequenceEqual(actual.Lines[line].Tokens), $"{grammar}: tokens differ at line {line} in {text}");
                Assert.Same(expected[line].StartState, actual.Lines[line].StartState);
                Assert.Same(expected[line].EndState, actual.Lines[line].EndState);
            }
        }
    }

    [Fact]
    public void Cancellation_inside_long_lookahead_resumes_without_publishing_speculative_tokens()
    {
        string text = "{\"" + new string('x', 4 << 20) + "\":1}\nvar next=1;";
        using var document = Doc.FromBytes(Encoding.UTF8.GetBytes(text), PagedBackendFactory.Instance,
            new DocumentOptions { Format = TextCodec.Utf8 });
        using var session = new StreamingSyntaxSession(document, BuiltinGrammars.Tokenizer("json"));
        SyntaxWindow pending;
        do pending = session.TokenizeWindow(0, 1, 0, 32, TimeSpan.FromMilliseconds(1)); // a loaded machine can use the whole slice before the first step
        while (session.BytesRead == 0);
        Assert.Equal(SyntaxViewportStatus.Pending, pending.Status);
        Assert.Empty(pending.Lines);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        long read = session.BytesRead;
        Assert.Throws<OperationCanceledException>(() => session.TokenizeWindow(0, 1, 0, 32, TimeSpan.FromSeconds(1), cancellation.Token));
        Assert.Equal(read, session.BytesRead);
        var result = Complete(session, 0, 1, 0, 32);
        Assert.Equal(TokenKinds.Property, result.Lines[0].Tokens.Single(token => token.Start == 1).Kind);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Editing_a_pending_candidate_discards_its_snapshot_and_decision()
    {
        string text = "{\"" + new string('x', 2 << 20) + "\":1}";
        using var document = Doc.FromBytes(Encoding.UTF8.GetBytes(text), PagedBackendFactory.Instance,
            new DocumentOptions { Format = TextCodec.Utf8 });
        using var session = new StreamingSyntaxSession(document, BuiltinGrammars.Tokenizer("json"));
        Assert.Equal(SyntaxViewportStatus.Pending, session.TokenizeWindow(0, 1, 0, 32, TimeSpan.FromMilliseconds(1)).Status);
        Assert.True(document.Replace(new ByteRange(text.Length - 3, text.Length - 2), ","u8.ToArray()).Applied);
        var result = Complete(session, 0, 1, 0, 32);
        Assert.Equal(document.Revision, result.Revision);
        Assert.Equal(TokenKinds.String, result.Lines[0].Tokens.Single(token => token.Start == 1).Kind);
    }

    [Fact]
    public void A_nonbuiltin_grammar_has_an_explicit_terminal_result()
    {
        var registry = new GrammarRegistry();
        var grammar = Grammar.Parse("""{"formatVersion":1,"id":"local","name":"Local","rules":[{"match":"x","token":"keyword"}]}""", registry);
        using var document = Doc.FromBytes("x"u8.ToArray(), PagedBackendFactory.Instance);
        using var session = new StreamingSyntaxSession(document, new Tokenizer(grammar));
        var result = session.TokenizeWindow(0, 1, 0, 32, TimeSpan.FromSeconds(1));
        Assert.Equal(SyntaxViewportStatus.FullLineRequired, result.Status);
        Assert.False(result.IsComplete);
        Assert.Equal(0, session.BytesRead);
    }

    [Fact]
    public void The_legacy_facade_can_dispose_a_child_session_during_the_same_edit_event()
    {
        string text = "{\"" + new string('x', 2 << 20) + "\":1}";
        using var document = Doc.FromBytes(Encoding.UTF8.GetBytes(text), PagedBackendFactory.Instance,
            new DocumentOptions { Format = TextCodec.Utf8 });
        using var session = new CheckpointSyntaxSession(document, BuiltinGrammars.Tokenizer("json"));
        SyntaxViewport result;
        do { result = session.TokenizeViewport(0, 1, TimeSpan.FromMilliseconds(5)); }
        while (result.Status == SyntaxViewportStatus.Pending);
        Assert.True(result.IsComplete);
        Assert.True(document.Replace(new ByteRange(text.Length - 3, text.Length - 2), ","u8.ToArray()).Applied);
        do { result = session.TokenizeViewport(0, 1, TimeSpan.FromMilliseconds(5)); }
        while (result.Status == SyntaxViewportStatus.Pending);
        Assert.True(result.IsComplete);
        Assert.Equal(document.Revision, result.Revision);
        Assert.Equal(TokenKinds.String, result.Lines[0].Tokens.Single(token => token.Start == 1).Kind);
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void Sparse_map_replay_yields_after_each_window_and_can_be_abandoned_and_resumed()
    {
        var bytes = new byte[36 << 20];
        bytes.AsSpan().Fill((byte)'x');
        using var document = Doc.FromBytes(bytes, new PagedBackendFactory(startIndexing: false),
            new DocumentOptions { Format = TextCodec.Utf8 });
        var source = new StreamingLineSource(document.GetSnapshot(), 0);
        foreach (var _ in source.Ensure(bytes.Length - 1)) { }
        const long target = (3 << 20) + 100_000;
        long before = source.BytesRead;
        using (var replay = source.Ensure(target).GetEnumerator())
        {
            Assert.True(replay.MoveNext());
            Assert.InRange(source.BytesRead - before, 1, (32 << 10) + 8);
        }
        foreach (var _ in source.Ensure(target)) { }
        Assert.Equal('x', source.At(target));
    }

    private static SyntaxWindow Complete(StreamingSyntaxSession session, long line, int count, long column, int columns)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(30))
        {
            var result = session.TokenizeWindow(line, count, column, columns, TimeSpan.FromMilliseconds(5));
            if (result.Status != SyntaxViewportStatus.Pending)
            {
                Assert.True(result.IsComplete);
                return result;
            }
        }
        throw new TimeoutException("Streaming tokenization did not finish.");
    }
}
