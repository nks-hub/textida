using System.Diagnostics;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Syntax;
using Xunit.Abstractions;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public sealed class SyntaxPunctuationAllocationTests(ITestOutputHelper output)
{
    [Fact]
    public void Eight_viewports_over_8192_empty_json_arrays_allocate_at_most_4_mib()
    {
        var tokenizer = BuiltinGrammars.Tokenizer("json");
        using (var warmDocument = Create("[[],{},\"warm\"]", TextCodec.Utf8))
        using (var warmSession = new StreamingSyntaxSession(warmDocument, tokenizer))
            Complete(warmSession, 0, 128);
        string text = "[" + string.Join(',', Enumerable.Repeat("[]", 8192)) + "]";
        var expected = SyntaxHelpers.TokenizeAll(tokenizer, [text]).Single();
        using var document = Create(text, TextCodec.Utf8);
        using var session = new StreamingSyntaxSession(document, tokenizer);
        int[] columns = [0, 4096, 8192, 12288, 16384, 20000, 24000, text.Length + 1];
        var windows = new SyntaxWindow[columns.Length];

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < columns.Length; i++) windows[i] = Complete(session, columns[i], 128);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine($"punctuation-chars={text.Length}; viewports=8; allocated-bytes={allocated}; limit-bytes={4 << 20}");
        Assert.InRange(allocated, 0, 4L << 20);
        for (int i = 0; i < columns.Length; i++) AssertWindow(expected, windows[i], columns[i], 128);
    }

    [Theory]
    [InlineData("UTF-8")]
    [InlineData("UTF-8 with BOM")]
    [InlineData("UTF-16 LE with BOM")]
    [InlineData("UTF-16 BE with BOM")]
    [InlineData("UTF-32 LE with BOM")]
    [InlineData("UTF-32 BE with BOM")]
    public void Punctuation_beside_a_unicode_scalar_at_the_byte_window_edge_keeps_tokens_and_states(string codecName)
    {
        var codec = TextCodec.ByName(codecName)!;
        int edge = SyntaxChunkReader.BufferBytes / codec.Alignment;
        string text = new string(' ', edge - 3) + "[\"😀\",{},[]]";
        var tokenizer = BuiltinGrammars.Tokenizer("json");
        var expected = SyntaxHelpers.TokenizeAll(tokenizer, [text]).Single();
        using var document = Create(text, codec);
        using var session = new StreamingSyntaxSession(document, tokenizer);

        AssertWindow(expected, Complete(session, edge - 5, 32), edge - 5, 32);
        AssertWindow(expected, Complete(session, text.Length + 1, 16), text.Length + 1, 16);
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
            var window = session.TokenizeWindow(0, 1, column, columns, TimeSpan.FromMilliseconds(1));
            if (window.Status == SyntaxViewportStatus.Pending) continue;
            Assert.True(window.IsComplete);
            return window;
        }
        throw new TimeoutException("The punctuation syntax fixture did not complete.");
    }
}
