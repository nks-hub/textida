using System.Diagnostics;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// Drag gate of the minimap: the pointer moves, the editor scrolls, and the minimap, layout and render jobs run. Headless runs measure CPU work, not the compositor, like every other number of the Perf category.
/// </summary>
[Collection("LocState")]
[Trait("Category", "Perf")]
public class MinimapDragPerfTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempDir _temp = new();
    private App? _app;

    public void Dispose()
    {
        if (_app is not null) TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    [AvaloniaTheory]
    [InlineData(10_000)]
    [InlineData(100_000)]
    public void A_drag_frame_of_the_minimap_is_cheap(int lines)
    {
        _app = TestApp.Prepare(_temp);
        var w = _app.CreateWindow(null);
        var sb = new StringBuilder();
        for (int i = 0; i < lines; i++) sb.Append("    var value").Append(i).Append(" = Compute(").Append(i).Append(", \"text\"); // note\n");
        var tab = w.OpenFile(_temp.File("drag.cs", sb.ToString()), null);
        w.UpdateLayout();
        for (int i = 0; i < 200 && tab.Minimap is null; i++) Dispatcher.UIThread.RunJobs();
        var host = (IMinimapHost)tab.Editor!;
        var times = new List<double>();
        var scrollMs = new List<double>(); var layoutMs = new List<double>(); var jobsMs = new List<double>();
        Core.TypingTrace.Enabled = true;
        Core.TypingTrace.Reset();
        for (int round = 0; round < 2; round++)
            for (int step = 0; step < 60; step++)
            {
                long line = (round == 0 ? step : 60 - step) * (lines / 200L);
                long started = Stopwatch.GetTimestamp();
                host.ScrollToLine(line);
                long t1 = Stopwatch.GetTimestamp();
                w.UpdateLayout();
                long t2 = Stopwatch.GetTimestamp();
                Dispatcher.UIThread.RunJobs();
                if (round == 1)
                {
                    times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    scrollMs.Add(Stopwatch.GetElapsedTime(started, t1).TotalMilliseconds);
                    layoutMs.Add(Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds);
                    jobsMs.Add(Stopwatch.GetElapsedTime(t2).TotalMilliseconds);
                }
            }
        output.WriteLine($"mean ms: scroll {scrollMs.Average():F1}, layout {layoutMs.Average():F1}, jobs {jobsMs.Average():F1}\n{Core.TypingTrace.Summary()}");
        Core.TypingTrace.Enabled = false;
        times.Sort();
        output.WriteLine($"{lines} lines: drag frame p50 {times[times.Count / 2]:F1} ms, p95 {times[(int)(times.Count * 0.95)]:F1} ms, builds started {tab.Minimap!.BuildsStarted}");
        Assert.True(times[(int)(times.Count * 0.95)] < 1000);
    }
}
