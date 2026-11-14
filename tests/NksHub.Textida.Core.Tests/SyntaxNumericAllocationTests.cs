using System.Diagnostics;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Syntax;
using Xunit.Abstractions;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public sealed class SyntaxNumericAllocationTests(ITestOutputHelper output)
{
    private const string JsonNumberPattern = @"(?<![\w.])-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?";

    [Fact]
    public void Eight_viewports_over_8192_json_numbers_allocate_at_most_32_mib()
    {
        var tokenizer = BuiltinGrammars.Tokenizer("json");
        using (var warmDocument = Create("[12345,-6.75e+8,0,\"tail\"]", TextCodec.Utf8))
        using (var warmSession = new StreamingSyntaxSession(warmDocument, tokenizer))
            Complete(warmSession, 0, 128);
        string text = "[" + string.Join(',', Enumerable.Repeat("12345", 8192)) + ",\"tail\"]";
        var expected = SyntaxHelpers.TokenizeAll(tokenizer, [text]).Single();
        using var document = Create(text, TextCodec.Utf8);
        using var session = new StreamingSyntaxSession(document, tokenizer);
        int[] columns = [0, 4096, 8192, 12288, 16384, 24576, 32768, 45000];
        var windows = new SyntaxWindow[columns.Length];

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < columns.Length; i++) windows[i] = Complete(session, columns[i], 128);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine($"numbers=8192; viewports=8; allocated-bytes={allocated}; limit-bytes={32 << 20}");
        Assert.InRange(allocated, 0, 32L << 20);
        for (int i = 0; i < columns.Length; i++) AssertWindow(expected, windows[i], columns[i], 128);
    }

    [Fact]
    public void A_hundred_thousand_number_start_checks_allocate_at_most_4_kib()
    {
        var pattern = StreamPattern.Compile(JsonNumberPattern, true);
        char[] characters = ['1', '-', 'z', '0'];
        for (int i = 0; i < 256; i++) StreamingMatcher.CanStart(pattern, characters[i % characters.Length]);
        int accepted = 0;

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100_000; i++)
            if (StreamingMatcher.CanStart(pattern, characters[i % characters.Length])) accepted++;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine($"start-checks=100000; allocated-bytes={allocated}; limit-bytes=4096");
        Assert.Equal(75_000, accepted);
        Assert.InRange(allocated, 0, 4096);
    }

    [Theory]
    [InlineData("[0,12345,-67.8e+2,1E-4,1e+,012,1.2.3,+2,--3]")]
    [InlineData("α12 é-2 .42 _9 1١ 2e٣ 3.٤ [4]")]
    [InlineData("{\"n\":[12345,-67.8e+2],\"s\":\"12345e6\",\"x\":true}")]
    [InlineData("12345")]
    [InlineData("-")]
    [InlineData("[1\t,2,3] /* open")]
    public void Numeric_matches_and_rejections_keep_reference_tokens_and_states(string text)
    {
        AssertParity(text, TextCodec.Utf8);
    }

    [Fact]
    public void A_number_longer_than_64_characters_keeps_the_reference_match()
    {
        AssertParity("[" + new string('9', 129) + "e+12,0]", TextCodec.Utf8);
    }

    [Theory]
    [InlineData("UTF-8 with BOM")]
    [InlineData("UTF-16 LE with BOM")]
    [InlineData("UTF-16 BE with BOM")]
    [InlineData("UTF-32 LE with BOM")]
    [InlineData("UTF-32 BE with BOM")]
    public void A_numeric_token_split_at_the_byte_window_edge_keeps_columns_and_fallback(string codecName)
    {
        var codec = TextCodec.ByName(codecName)!;
        int edge = (SyntaxChunkReader.WindowBytes + 8) / codec.Alignment;
        string text = new string(' ', edge - 2) + "12345,-6.7e+8,1١,2e٣,3.٤]";
        AssertParity(text, codec, edge - 8, 96);
    }

    [Fact]
    public void Cancelling_after_numeric_prefix_progress_keeps_the_pending_scan_resumable()
    {
        string text = "[" + string.Join(',', Enumerable.Repeat("12345", 8192))
            + ",\"" + new string('x', 4 << 20) + "\"]";
        var tokenizer = BuiltinGrammars.Tokenizer("json");
        using var document = Create(text, TextCodec.Utf8);
        using var session = new StreamingSyntaxSession(document, tokenizer);
        SyntaxWindow pending;
        do pending = session.TokenizeWindow(0, 1, 0, 128, TimeSpan.FromMilliseconds(1));
        while (session.BytesRead == 0);
        Assert.Equal(SyntaxViewportStatus.Pending, pending.Status);
        Assert.Empty(pending.Lines);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        long read = session.BytesRead;

        Assert.Throws<OperationCanceledException>(() => session.TokenizeWindow(0, 1, 0, 128,
            TimeSpan.FromMilliseconds(10), cancellation.Token));
        Assert.Equal(read, session.BytesRead);
        var resumed = Complete(session, 0, 128);
        Assert.True(resumed.IsComplete);
        var line = Assert.Single(resumed.Lines);
        Assert.Same(tokenizer.InitialState, line.StartState);
        Assert.Same(tokenizer.InitialState, line.EndState);
        Assert.Contains(line.Tokens, token => token.Start == 1 && token.Kind == TokenKinds.Number && token.Length == 5);
    }

    private static void AssertParity(string text, TextCodec codec, int column = 0, int columns = 128)
    {
        var tokenizer = BuiltinGrammars.Tokenizer("json");
        var expected = SyntaxHelpers.TokenizeAll(tokenizer, [text]).Single();
        using var document = Create(text, codec);
        using var session = new StreamingSyntaxSession(document, tokenizer);
        AssertWindow(expected, Complete(session, column, columns), column, columns);
        int nextColumn = Math.Min(text.Length, column + 3);
        AssertWindow(expected, Complete(session, nextColumn, columns), nextColumn, columns);
    }

    private static void AssertWindow(LineTokens expected, SyntaxWindow actual, int column, int columns)
    {
        Assert.True(actual.IsComplete);
        var line = Assert.Single(actual.Lines);
        var clipped = expected.Tokens.Where(token => token.Start < column + columns && token.End > column)
            .Select(token => new Token(Math.Max(token.Start, column) - column,
                Math.Min(token.End, column + columns) - Math.Max(token.Start, column), token.Kind)).ToArray();
        Assert.Equal(clipped, line.Tokens);
        Assert.Equal((long)column, line.FirstColumn);
        Assert.Same(expected.StartState, line.StartState);
        Assert.Same(expected.EndState, line.EndState);
    }

    private static Doc Create(string text, TextCodec codec)
    {
        byte[] bytes = [.. codec.Bom, .. codec.Encode(text)];
        return Doc.FromBytes(bytes, new PagedBackendFactory(startIndexing: false), new DocumentOptions { Format = codec });
    }

    private static SyntaxWindow Complete(StreamingSyntaxSession session, long column, int columns)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromMinutes(3))
        {
            var window = session.TokenizeWindow(0, 1, column, columns, TimeSpan.FromMilliseconds(10));
            if (window.Status == SyntaxViewportStatus.Pending) continue;
            Assert.True(window.IsComplete);
            return window;
        }
        throw new TimeoutException("The numeric syntax fixture did not complete.");
    }
}
