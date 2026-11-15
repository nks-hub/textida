using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Tests;

public sealed partial class PagedLongLineFormattingTests
{
    [AvaloniaTheory]
    [InlineData(900)]
    [InlineData(3800)]
    public async Task Returning_from_a_tail_window_keeps_the_selection_end_and_requests_different_prefix_tokens(int width)
    {
        string rows = string.Join(',', Enumerable.Range(0, 500));
        string branch = string.Join(',', Enumerable.Range(0, 2048));
        string cache = string.Join(',', Enumerable.Range(0, 32).Select(i => $"\"branch{i:D2}\":[{branch}]"));
        string text = $"{{\"alpha\":1,\"delta\":2,\"bravo\":{{\"child\":3}},\"rows\":[{rows}],\"cache\":{{{cache}}},\"blob\":\"" + new string('x', 2 << 20) + "\"}";
        Assert.Equal(41, text.IndexOf("\"rows\":", StringComparison.Ordinal));
        Assert.Equal(1949, text.IndexOf("\"branch00\":", StringComparison.Ordinal));
        Assert.Equal(75093, text.IndexOf("\"branch08\":", StringComparison.Ordinal));
        using var host = new Host(text, "long.json", width);
        var document = host.Tab.Doc.Doc;
        var backend = Assert.IsType<PagedBackend>(document.Backend);
        await Settled();
        host.Paged.SelectBytes(1949, 11091);
        await Settled();
        var beforeRequest = host.Paged.Syntax.Requested;
        var before = host.Paged.TokensOf(0)!.ToArray();
        object? prefixEntry = host.Entry();
        Assert.NotNull(prefixEntry);
        Assert.NotEmpty(before);
        Assert.InRange(beforeRequest.Column, 9000, 11091);

        host.Paged.SelectBytes(75093, 84235);
        await Settled();
        Assert.Equal(84235, host.Paged.CaretOffset);
        Assert.Equal(new ByteRange(75093, 84235), host.Paged.SelectionRange);
        Assert.Equal(1, host.Paged.CaptureReadiness(document, backend).WindowRows);
        Assert.Same(prefixEntry, host.Entry());

        host.Paged.SelectBytes(41, 1939);
        await Settled();
        var afterRequest = host.Paged.Syntax.Requested;
        var after = host.Paged.TokensOf(0)!.ToArray();
        Assert.Equal(1939, host.Paged.CaretOffset);
        Assert.Equal(new ByteRange(41, 1939), host.Paged.SelectionRange);
        Assert.Equal("\"rows\":[" + rows + "]", host.Paged.SelectedText());
        Assert.Equal(0, host.Paged.CaptureReadiness(document, backend).WindowRows);
        Assert.NotEmpty(after);
        Assert.True(afterRequest.Column + afterRequest.Columns < beforeRequest.Column);
        Assert.False(before.AsSpan().SequenceEqual(after));
        Assert.NotSame(prefixEntry, host.Entry());
        output.WriteLine(JsonSerializer.Serialize(new
        {
            width, beforeRequest, afterRequest,
            beforeTokens = before.Select(t => new { t.Start, t.Length, t.Kind }),
            afterTokens = after.Select(t => new { t.Start, t.Length, t.Kind }),
            viewport = host.Paged.CaptureReadinessViewport(),
            cache = host.CacheProbe(prefixEntry, 0)
        }));

        async Task Settled()
        {
            for (int i = 0; i < 2000; i++)
            {
                host.Draw();
                if (!host.Paged.CaretMovePending && !host.Paged.WindowsPending
                    && host.Paged.Syntax.CaptureReadiness(host.Tab.Doc.Doc).Ready(host.Tab.Doc.Doc.Revision)) return;
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Fail("The actual source selection, window read and syntax request did not settle.");
        }
    }
}
