using System.Diagnostics;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public class SyntaxFeederSchedulingTests
{
    [AvaloniaFact]
    public async Task Pending_syntax_leaves_idle_work_runnable_and_delivers_the_latest_viewport()
    {
        using var document = PendingDocument();
        var applied = new List<SyntaxViewport>();
        using var feeder = new SyntaxFeeder(document, applied.Add, () => { }, _ => null, () => -1, () => { });
        feeder.SetVisible(1, 1, 0, 80);
        Assert.Empty(applied);

        bool idleRan = false, idleSawPending = false;
        Dispatcher.UIThread.Post(() =>
        {
            idleRan = true;
            idleSawPending = applied.Count == 0;
        }, DispatcherPriority.SystemIdle);
        Dispatcher.UIThread.RunJobs();
        Assert.True(idleRan);
        Assert.True(idleSawPending, "Pending background syntax drained the document before idle work could run.");

        feeder.SetVisible(20, 20, 0, 80);
        var clock = Stopwatch.StartNew();
        while (!applied.Any(view => view.Lines.Any(line => line.Line == 20 && line.Tokens.Any(token => token.Kind == "keyword")))
            && clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(5);
            Dispatcher.UIThread.RunJobs();
        }

        var result = Assert.Single(applied, view => view.Lines.Any(line => line.Line == 20));
        Assert.Equal(document.Doc.Id, result.DocumentId);
        Assert.Equal(document.Doc.Revision, result.Revision);
        Assert.True(result.IsComplete);
        Assert.Contains(Assert.Single(result.Lines, line => line.Line == 20).Tokens, token => token.Kind == "keyword");
    }

    [AvaloniaFact]
    public async Task Disposing_pending_syntax_stops_continuations_and_stale_callbacks()
    {
        using var document = PendingDocument();
        int callbacks = 0;
        bool completed = false;
        using var feeder = new SyntaxFeeder(document, _ => { completed = true; callbacks++; }, () => callbacks++, _ => null, () => -1, () => callbacks++);
        feeder.SetVisible(20, 20, 0, 80);
        Assert.False(completed);
        int runs = feeder.ViewportRuns;
        feeder.Dispose();
        int before = callbacks;

        await Task.Delay(30);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(runs, feeder.ViewportRuns);
        Assert.Equal(before, callbacks);
    }

    private static OpenDocument PendingDocument()
    {
        // The line exceeds the full-scan limit, so the real session must resume its streaming scan.
        var text = "/*" + new string('x', 16 << 20) + "*/\n"
            + string.Concat(Enumerable.Range(1, 40).Select(line => $"const value{line} = 1;\n"));
        return OpenDocument.FromSource(Encoding.UTF8.GetBytes(text), "pending.js");
    }
}
