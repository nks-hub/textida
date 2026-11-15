using System.Diagnostics;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Syntax;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public sealed class SyntaxStreamingTokenBudgetTests
{
    public enum SessionKind { Checkpoint, Streaming, AutomaticStreaming }

    [Theory]
    [InlineData(SessionKind.Checkpoint)]
    [InlineData(SessionKind.Streaming)]
    [InlineData(SessionKind.AutomaticStreaming)]
    public void Viewport_accepts_65536_runs_rejects_more_and_recovers_for_a_later_request(SessionKind kind)
    {
        string denseLine = string.Join(' ', Enumerable.Repeat("[]", 1024));
        const string suffix = "/*\ninside\n*/ {\"tail\":1}";
        string prefix = kind == SessionKind.AutomaticStreaming ? new string(' ', (1 << 20) + 1) + "\n" : "";
        string text = prefix + string.Join('\n', Enumerable.Repeat(denseLine, 128)) + "\n" + suffix;
        long firstLine = prefix.Length == 0 ? 0 : 1;
        var tokenizer = BuiltinGrammars.Tokenizer("json");
        using var document = Doc.FromBytes(TextCodec.Utf8.Encode(text), new PagedBackendFactory(startIndexing: false),
            new DocumentOptions { Format = TextCodec.Utf8 });
        using var checkpoint = kind == SessionKind.Streaming ? null : new CheckpointSyntaxSession(document, tokenizer);
        using var streaming = kind == SessionKind.Streaming ? new StreamingSyntaxSession(document, tokenizer) : null;

        IReadOnlyList<WindowLineTokens> Read(long first, int count)
        {
            if (streaming is not null)
                return Complete(() => streaming.TokenizeWindow(first, count, 0, 4096, TimeSpan.FromMilliseconds(5)), view => view.Status).Lines;
            var viewport = Complete(() => checkpoint!.TokenizeViewport(first, count, TimeSpan.FromMilliseconds(5)), view => view.Status);
            return viewport.Lines.Select(line => new WindowLineTokens(line.Line, 0, line.Tokens, line.StartState, line.EndState)).ToArray();
        }

        var atLimit = Read(firstLine, 64);
        Assert.Equal(64, atLimit.Count);
        Assert.Equal(65536, atLimit.Sum(line => line.Tokens.Count));
        var expectedDense = SyntaxHelpers.TokenizeAll(tokenizer, [denseLine]).Single();
        Assert.All(atLimit, line => Assert.Equal(expectedDense.Tokens, line.Tokens));

        Assert.Throws<InvalidOperationException>(() => Read(firstLine, 128));
        Assert.Throws<InvalidOperationException>(() => Read(firstLine, 128));

        var recovered = Read(firstLine + 129, 2);
        var expectedSuffix = SyntaxHelpers.TokenizeAll(tokenizer, suffix.Split('\n'));
        Assert.False(expectedSuffix[1].StartState.IsInitial);
        Assert.Equal(2, recovered.Count);
        for (int i = 0; i < recovered.Count; i++)
        {
            var expected = expectedSuffix[i + 1];
            Assert.Equal(firstLine + 129 + i, recovered[i].Line);
            Assert.Equal(expected.Tokens, recovered[i].Tokens);
            Assert.Same(expected.StartState, recovered[i].StartState);
            Assert.Same(expected.EndState, recovered[i].EndState);
        }
    }

    private static T Complete<T>(Func<T> scan, Func<T, SyntaxViewportStatus> status)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromMinutes(3))
        {
            var view = scan();
            var current = status(view);
            if (current == SyntaxViewportStatus.Pending) continue;
            Assert.Equal(SyntaxViewportStatus.Complete, current);
            return view;
        }
        throw new TimeoutException("The syntax token budget fixture did not complete.");
    }
}
