using System.Diagnostics;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Syntax;
using Xunit.Abstractions;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public sealed class SyntaxXmlAllocationTests(ITestOutputHelper output)
{
    [Fact]
    public void Eight_viewports_over_8192_xml_elements_keep_tokens_and_allocate_at_most_32_mib()
    {
        var tokenizer = BuiltinGrammars.Tokenizer("xml");
        string text = "<root>" + string.Concat(Enumerable.Repeat("<n>123</n>", 8192)) + "</root>";
        var expected = SyntaxHelpers.TokenizeAll(tokenizer, [text]).Single();
        using var document = Create(text, TextCodec.Utf8);
        using var session = new StreamingSyntaxSession(document, tokenizer);
        int[] columns = [0, 4096, 8192, 16384, 24000, 40000, 60000, text.Length - 128];
        var windows = new SyntaxWindow[columns.Length];

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < columns.Length; i++) windows[i] = Complete(session, 0, columns[i], 128);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine($"xml-chars={text.Length}; viewports=8; allocated-bytes={allocated}; limit-bytes={32 << 20}");
        for (int i = 0; i < columns.Length; i++) AssertWindow(expected, windows[i], columns[i], 128);
        Assert.InRange(allocated, 0, 32L << 20);
    }

    [Theory]
    [InlineData("UTF-8")]
    [InlineData("UTF-16 LE with BOM")]
    [InlineData("UTF-16 BE with BOM")]
    [InlineData("UTF-32 LE with BOM")]
    [InlineData("UTF-32 BE with BOM")]
    public void Xml_tags_across_byte_windows_keep_unicode_names_attributes_and_following_line_state(string codecName)
    {
        var codec = TextCodec.ByName(codecName)!;
        int edge = SyntaxChunkReader.BufferBytes / codec.Alignment;
        string name = "n" + new string('ž', 300);
        string text = new string(' ', edge - 3)
            + "<n:ž a='😀&amp;'/> <!--comment--> <![CDATA[ž😀]]> <?pi?> <!DOCTYPE root>"
            + "<" + name + ">α</" + name + "> <open a='value';";
        string next = "more'>text</open>";
        var tokenizer = BuiltinGrammars.Tokenizer("xml");
        var expected = SyntaxHelpers.TokenizeAll(tokenizer, [text, next]);
        using var document = Create(text + "\r\n" + next, codec);
        using var session = new StreamingSyntaxSession(document, tokenizer);

        foreach (int column in new[] { edge - 8, edge + 20, text.Length - 160 })
            AssertWindow(expected[0], Complete(session, 0, column, 192), column, 192);
        AssertWindow(expected[1], Complete(session, 1, 0, 128), 0, 128);
    }

    private static void AssertWindow(LineTokens expected, SyntaxWindow actual, int column, int columns)
    {
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

    private static SyntaxWindow Complete(StreamingSyntaxSession session, long line, int column, int columns)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromMinutes(3))
        {
            var window = session.TokenizeWindow(line, 1, column, columns, TimeSpan.FromMilliseconds(2));
            if (window.Status == SyntaxViewportStatus.Pending) continue;
            Assert.True(window.IsComplete);
            return window;
        }
        throw new TimeoutException("The XML syntax fixture did not complete.");
    }
}
