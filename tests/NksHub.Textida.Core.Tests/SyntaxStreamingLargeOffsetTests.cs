using System.Diagnostics;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Syntax;
using Xunit.Abstractions;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class SyntaxStreamingLargeOffsetTests(ITestOutputHelper output)
{
    [Fact]
    public void Window_tokens_use_local_columns_and_preserve_the_following_line_state() =>
        AssertSparseWindow((32L << 10) + 65);

    [Fact]
    [Trait("Category", "Perf")]
    public void A_real_line_beyond_two_gibibytes_preserves_64_bit_viewport_columns() =>
        AssertSparseWindow((long)int.MaxValue + 65);

    private void AssertSparseWindow(long closingOffset)
    {
        using var file = new TempFile();
        if (OperatingSystem.IsWindows())
        {
            var command = new ProcessStartInfo("fsutil") { CreateNoWindow = true, UseShellExecute = false };
            foreach (string argument in new[] { "sparse", "setflag", file.Path }) command.ArgumentList.Add(argument);
            using var process = Process.Start(command)!;
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }

        byte[] ending = "*/ var value = 1;\r\nvar next = 2;"u8.ToArray();
        using (var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Write))
        {
            stream.SetLength(closingOffset + ending.Length);
            stream.Write("/*"u8);
            stream.Position = closingOffset;
            stream.Write(ending);
        }

        // Sparse zero bytes decode to one UTF-16 NUL each, so the byte and character positions coincide.
        using var document = Doc.Open(file.Path, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Utf8 });
        using var session = new StreamingSyntaxSession(document, BuiltinGrammars.Tokenizer("javascript"));
        long firstColumn = closingOffset - 4;
        var pending = session.TokenizeWindow(0, 1, firstColumn, 32, TimeSpan.Zero);
        Assert.Equal(SyntaxViewportStatus.Pending, pending.Status);
        Assert.Empty(pending.Lines);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            session.TokenizeWindow(0, 1, firstColumn, 32, TimeSpan.FromMilliseconds(5), cancellation.Token));

        var timer = Stopwatch.StartNew();
        var window = Complete(session, 0, firstColumn);
        Assert.Equal(document.Id, window.DocumentId);
        Assert.Equal(document.Revision, window.Revision);
        var line = Assert.Single(window.Lines);
        Assert.Equal(0, line.Line);
        Assert.Equal(firstColumn, line.FirstColumn);
        Assert.True(line.StartState.IsInitial);
        Assert.True(line.EndState.IsInitial);
        Token[] expected =
        [
            new(0, 6, "comment"),
            new(7, 3, "keyword"),
            new(17, 1, "operator"),
            new(19, 1, "number"),
            new(20, 1, "punctuation"),
        ];
        Assert.Equal(expected, line.Tokens);

        var next = Assert.Single(Complete(session, 1, 0).Lines);
        Assert.Equal(1, next.Line);
        Assert.Equal(0, next.FirstColumn);
        Assert.True(next.StartState.IsInitial);
        Assert.True(next.EndState.IsInitial);
        Token[] expectedNext =
        [
            new(0, 3, "keyword"),
            new(9, 1, "operator"),
            new(11, 1, "number"),
            new(12, 1, "punctuation"),
        ];
        Assert.Equal(expectedNext, next.Tokens);
        output.WriteLine($"file-bytes={document.ByteLength}; first-column={firstColumn}; elapsed-ms={timer.Elapsed.TotalMilliseconds:F1}");
    }

    private static SyntaxWindow Complete(StreamingSyntaxSession session, long firstLine, long firstColumn)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        while (true)
        {
            var result = session.TokenizeWindow(firstLine, 1, firstColumn, 32, TimeSpan.FromMilliseconds(5), deadline.Token);
            Assert.NotEqual(SyntaxViewportStatus.FullLineRequired, result.Status);
            if (result.Status == SyntaxViewportStatus.Complete) return result;
        }
    }
}
