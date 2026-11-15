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
    public async Task Returning_from_a_tail_window_recolors_the_prefix_without_reshaping_or_moving_its_selection(int width)
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
        Note("branch00-prefix");
        var beforeRequest = host.Paged.Syntax.Requested;
        var before = host.Paged.TokensOf(0)!.ToArray();
        object? prefixEntry = host.Entry();
        Assert.NotNull(prefixEntry);
        object? prefixGeometry = host.Geometry();
        Assert.NotNull(prefixGeometry);
        Assert.NotEmpty(before);
        Assert.Equal(0, beforeRequest.Column);
        Assert.True(beforeRequest.Columns > 11091);

        host.Paged.SelectBytes(75093, 84235);
        await Settled();
        Note("branch08-window");
        Assert.Equal(84235, host.Paged.CaretOffset);
        Assert.Equal(new ByteRange(75093, 84235), host.Paged.SelectionRange);
        Assert.Equal(1, host.Paged.CaptureReadiness(document, backend).WindowRows);
        Assert.Same(prefixEntry, host.Entry());
        Assert.Same(prefixGeometry, host.Geometry());

        host.Paged.SelectBytes(41, 1939);
        await Settled();
        Note("rows-prefix");
        var afterRequest = host.Paged.Syntax.Requested;
        var after = host.Paged.TokensOf(0)!.ToArray();
        Assert.Equal(1939, host.Paged.CaretOffset);
        Assert.Equal(new ByteRange(41, 1939), host.Paged.SelectionRange);
        Assert.Equal("\"rows\":[" + rows + "]", host.Paged.SelectedText());
        Assert.Equal(0, host.Paged.CaptureReadiness(document, backend).WindowRows);
        Assert.NotEmpty(after);
        Assert.Equal(0, afterRequest.Column);
        Assert.True(afterRequest.Columns < beforeRequest.Columns);
        Assert.False(before.AsSpan().SequenceEqual(after));
        Assert.Same(prefixGeometry, host.Geometry());
        output.WriteLine(JsonSerializer.Serialize(new
        {
            width,
            beforeTokens = before.Select(t => new { t.Start, t.Length, t.Kind }),
            afterTokens = after.Select(t => new { t.Start, t.Length, t.Kind }),
            viewport = host.Paged.CaptureReadinessViewport(),
            cache = host.CacheProbe(prefixEntry, 0)
        }));

        void Note(string stage)
        {
            var requested = host.Paged.Syntax.Requested;
            output.WriteLine(JsonSerializer.Serialize(new
            {
                stage, width, caret = host.Paged.CaretOffset, selection = host.Paged.SelectionRange,
                requested = new { requested.First, requested.Count, requested.Column, requested.Columns },
                viewport = host.Paged.CaptureReadinessViewport(),
                tokens = host.Paged.TokensOf(0)?.Select(t => new { t.Start, t.Length, t.Kind })
            }));
        }

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
