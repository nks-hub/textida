using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace NksHub.Textida.App.Tests;

public partial class ViewOptionsTests
{
    [AvaloniaFact]
    [Trait("Category", "Perf")]
    public void Dragging_the_minimap_of_a_file_of_100000_lines_moves_the_editor_within_the_budget()
    {
        var tab = Open("p100k.cs", KeywordFile(100_000));
        var map = tab.Minimap!;
        var origin = map.TranslatePoint(default, _w)!.Value;
        var start = new Point(origin.X + 40, origin.Y + 20);
        if (Core.TypingTrace.Enabled) Core.TypingTrace.Reset();
        _w.MouseDown(start, MouseButton.Left);
        var times = new List<double>();
        List<double> moves = new(200), layouts = new(200), jobs = new(200);
        for (int i = 0; i < 200; i++)
        {
            var clock = Stopwatch.StartNew();
            _w.MouseMove(new Point(start.X, start.Y + i * 3.5));
            double afterMove = clock.Elapsed.TotalMilliseconds;
            _w.UpdateLayout();
            double afterLayout = clock.Elapsed.TotalMilliseconds;
            Dispatcher.UIThread.RunJobs();
            times.Add(clock.Elapsed.TotalMilliseconds);
            moves.Add(afterMove);
            layouts.Add(afterLayout - afterMove);
            jobs.Add(times[^1] - afterLayout);
        }
        _w.MouseUp(new Point(start.X, start.Y + 700), MouseButton.Left);
        static string Spread(List<double> values) { var sorted = values.Order().ToArray(); return $"p50 {sorted[sorted.Length / 2]:0.0}, p95 {sorted[(int)(sorted.Length * 0.95)]:0.0}"; }
        _output.WriteLine($"100k minimap drag, parts: move [{Spread(moves)}], layout [{Spread(layouts)}], jobs [{Spread(jobs)}] ms");
        if (Core.TypingTrace.Enabled)
        {
            _output.WriteLine("100k minimap drag, spans including press/release:\n" + Core.TypingTrace.Summary());
            Core.TypingTrace.Reset();
        }
        times.Sort();
        double p95 = times[(int)(times.Count * 0.95)];
        _output.WriteLine($"minimap drag over 100000 lines: p50 {times[times.Count / 2]:0.0}, p95 {p95:0.0}, max {times[^1]:0.0} ms");
        Assert.True(p95 <= 33, $"p95 {p95:0.0} ms");
    }

    [AvaloniaFact]
    [Trait("Category", "Perf")]
    public void Dragging_the_minimap_moves_the_editor_within_the_budget()
    {
        var tab = Open("p.cs", KeywordFile(20_000));
        var map = tab.Minimap!;
        var origin = map.TranslatePoint(default, _w)!.Value;
        var start = new Point(origin.X + 40, origin.Y + 100);
        _w.MouseDown(start, MouseButton.Left);
        var times = new List<double>();
        for (int i = 0; i < 200; i++)
        {
            var clock = Stopwatch.StartNew();
            _w.MouseMove(new Point(start.X, start.Y + i * 2.5));
            _w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            times.Add(clock.Elapsed.TotalMilliseconds);
        }
        _w.MouseUp(new Point(start.X, start.Y + 500), MouseButton.Left);
        times.Sort();
        double p95 = times[(int)(times.Count * 0.95)];
        Assert.True(p95 <= 33, $"p95 {p95:0.0} ms");
    }

    [AvaloniaFact]
    [Trait("Category", "Perf")]
    public void Dragging_the_sampled_minimap_moves_the_paged_surface_within_the_budget()
    {
        var tab = Open("pp.cs", KeywordFile(400_000), paged: true);
        var map = tab.Minimap!;
        Assert.True(map.IsSampled);
        var origin = map.TranslatePoint(default, _w)!.Value;
        var start = new Point(origin.X + 40, origin.Y + 2);
        _w.MouseDown(start, MouseButton.Left);
        var times = new List<double>();
        List<double> moves = [], layouts = [], jobs = [];
        long before = tab.Paged!.TopLine;
        for (int i = 0; i < 200; i++)
        {
            var clock = Stopwatch.StartNew();
            _w.MouseMove(new Point(start.X, start.Y + i * 2.5));
            double afterMove = clock.Elapsed.TotalMilliseconds;
            _w.UpdateLayout();
            double afterLayout = clock.Elapsed.TotalMilliseconds;
            Dispatcher.UIThread.RunJobs();
            times.Add(clock.Elapsed.TotalMilliseconds);
            moves.Add(afterMove);
            layouts.Add(afterLayout - afterMove);
            jobs.Add(times[^1] - afterLayout);
        }
        _w.MouseUp(new Point(start.X, start.Y + 500), MouseButton.Left);
        Assert.True(tab.Paged.TopLine > before + 100_000, $"{before} -> {tab.Paged.TopLine}");
        static string Spread(List<double> v) { var s = v.Order().ToArray(); return $"p50 {s[s.Length / 2]:0.0}, p95 {s[(int)(s.Length * 0.95)]:0.0}"; }
        _output.WriteLine($"sampled minimap drag, parts: move [{Spread(moves)}], layout [{Spread(layouts)}], jobs [{Spread(jobs)}] ms");
        times.Sort();
        double p95 = times[(int)(times.Count * 0.95)];
        _output.WriteLine($"sampled minimap drag: p50 {times[times.Count / 2]:0.0}, p95 {p95:0.0}, max {times[^1]:0.0} ms");

        // where the time goes: the spans over a second drag back (the measured one above runs without them)
        _w.MouseDown(new Point(start.X, start.Y + 500), MouseButton.Left);
        NksHub.Textida.Core.TypingTrace.Reset();
        NksHub.Textida.Core.TypingTrace.Enabled = true;
        try
        {
            for (int i = 0; i < 200; i++)
            {
                _w.MouseMove(new Point(start.X, start.Y + 500 - i * 2.5));
                _w.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally { NksHub.Textida.Core.TypingTrace.Enabled = false; }
        _w.MouseUp(new Point(start.X, start.Y), MouseButton.Left);
        _output.WriteLine("sampled minimap drag, spans of 200 moves:\n" + NksHub.Textida.Core.TypingTrace.Summary());
        NksHub.Textida.Core.TypingTrace.Reset();
        Assert.True(p95 <= 33, $"p95 {p95:0.0} ms");
    }

}
