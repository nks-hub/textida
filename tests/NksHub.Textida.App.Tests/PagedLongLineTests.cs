using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Tests;

/// <summary>Horizontal windows of a long line on the paged surface: reads off the UI thread, stale reads, the scroll range in characters.</summary>
[Collection("LocState")]
public class PagedLongLineTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public PagedLongLineTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private DocTab OpenLine(string line)
    {
        var tab = _w.OpenFile(_temp.Bytes("long.txt", Encoding.UTF8.GetBytes(line + "\nnext\n")), null);
        Assert.True(tab.Doc.IsLarge);
        for (int i = 0; i < 400 && !tab.Paged!.Index.Complete; i++)
        {
            Thread.Sleep(25);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(tab.Paged!.Index.Complete);
        tab.Paged.Focus();
        Draw();
        Dispatcher.UIThread.RunJobs();
        return tab;
    }

    private void Draw()
    {
        _w.UpdateLayout();
        _w.CaptureRenderedFrame();
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(condition());
    }

    [AvaloniaFact]
    public async Task A_far_window_is_read_off_the_UI_thread_and_a_stale_read_is_never_painted()
    {
        var tab = OpenLine(new string('a', 3_000_000));
        var paged = tab.Paged!;
        using var gate = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        paged.WindowRead = (_, column, _, _) =>
        {
            if (column < 2_400_000) { started.Set(); gate.Wait(TimeSpan.FromSeconds(10)); return "STALE"; }
            return "FRESH";
        };

        long t0 = Stopwatch.GetTimestamp();
        paged.ScrollToColumn(2_000_000);
        Draw();
        Assert.True(Stopwatch.GetElapsedTime(t0).TotalSeconds < 5, "the jump waited for the read");
        Assert.False(gate.IsSet);
        Assert.True(paged.WindowsPending);
        Assert.Null(paged.CachedWindow(0));
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        paged.ScrollToColumn(2_500_000);
        Draw();
        await Eventually(() => paged.CachedWindow(0) == "FRESH");
        Assert.False(paged.WindowsPending);

        gate.Set(); // the older read ends now and must be dropped
        await Task.Delay(200);
        Dispatcher.UIThread.RunJobs();
        Draw();
        Assert.Equal("FRESH", paged.CachedWindow(0));
    }

    [AvaloniaFact]
    public async Task The_scroll_range_of_a_utf8_line_ends_at_its_last_character_not_its_last_byte()
    {
        var tab = OpenLine(new string('ě', 1_500_000)); // two bytes each
        var paged = tab.Paged!;
        paged.ScrollToColumn(1_400_000);
        Draw();
        await Eventually(() => !paged.WindowsPending && paged.CachedWindow(0) is { Length: > 0 });
        await Eventually(() => { Draw(); return paged.HorizontalMaximum is > 1_499_000 and <= 1_500_000; });
        paged.ScrollToColumn(3_000_000);
        Assert.InRange(paged.HorizontalMaximum, 1_499_000, 1_500_000);
    }
}

/// <summary>
/// P3.1.3 gate: jumps to column 0, 50 000 000 and 90 000 000 of a 100 MiB line, each as the first jump without a column index and as a scroll after the index exists.
/// The time is from the jump until the frame is rendered; a first jump answers with the placeholder frame and the window follows when the read ends (reported, not gated).
/// </summary>
[Collection("LocState")]
[Trait("Category", "Perf")]
public class PagedLongLinePerfTests(ITestOutputHelper output) : IDisposable
{
    private const int Size = 100 * 1024 * 1024;
    private readonly TempDir _temp = new();
    private App? _app;

    public void Dispose()
    {
        if (_app is not null) TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private (MainWindow Window, DocTab Tab) Open()
    {
        _app = TestApp.Prepare(_temp);
        var w = _app.CreateWindow(null);
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        string path = Path.Combine(_temp.Path, "line.txt");
        using (var file = File.Create(path))
        {
            var chunk = new byte[1 << 20];
            for (long done = 0; done < Size; done += chunk.Length)
            {
                for (int i = 0; i < chunk.Length; i++) chunk[i] = (byte)('a' + (done + i) * 7 % 26);
                file.Write(chunk);
            }
            file.Write("\nnext\n"u8);
        }
        var tab = w.OpenFile(path, null);
        Assert.True(tab.Doc.IsLarge);
        for (int i = 0; i < 400 && !tab.Paged!.Index.Complete; i++)
        {
            Thread.Sleep(25);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(tab.Paged!.Index.Complete);
        tab.Paged.Focus();
        Present(w);
        Dispatcher.UIThread.RunJobs();
        return (w, tab);
    }

    private static void Present(MainWindow w)
    {
        w.UpdateLayout();
        w.CaptureRenderedFrame();
    }

    private static double Since(long t0) => Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

    private static (double P50, double P95, double P99) Percentiles(List<double> times)
    {
        times.Sort();
        return (times[times.Count / 2], times[(int)(times.Count * 0.95)], times[(int)(times.Count * 0.99)]);
    }

    private static MappedFileSource SourceOf(DocTab tab) => ((PagedBackend)tab.Doc.Doc.Backend).Paged.Source;

    private static async Task BuildIndex(MainWindow w, DocTab tab, int column)
    {
        var source = SourceOf(tab);
        if (column == 0)
        {
            source.ReadColumns(0, long.MaxValue, 0);
            // one scroll through the UI like the far columns get below, so the first measured jump does not pay for the first paint path
            tab.Paged!.ScrollToColumn(5);
            Present(w);
            return;
        }
        tab.Paged!.ScrollToColumn(column + 5);
        Present(w);
        for (int i = 0; i < 2000 && (tab.Paged.WindowsPending || !source.TryGetLineColumns(0, out _)); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
        Assert.True(source.TryGetLineColumns(0, out _));
    }

    private void Gate(string name, List<double> times)
    {
        var (p50, p95, p99) = Percentiles(times);
        output.WriteLine($"{name}: p50 {p50:0.0}, p95 {p95:0.0}, p99 {p99:0.0} ms over {times.Count} jumps");
        Assert.True(p95 <= 33 && p99 <= 80, $"{name}: p50 {p50:0.0}, p95 {p95:0.0}, p99 {p99:0.0} ms");
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(50_000_000)]
    [InlineData(90_000_000)]
    public async Task First_jump_without_an_index_answers_within_the_budget(int column)
    {
        var (w, tab) = Open();
        var paged = tab.Paged!;
        var source = SourceOf(tab);
        await BuildIndex(w, tab, column); // the first read starts the background build of the index; wait for it, then every jump starts cold

        var first = new List<double>();
        var complete = new List<double>();
        for (int i = 0; i < 20; i++)
        {
            source.Columns.Clear();
            long t0 = Stopwatch.GetTimestamp();
            paged.ScrollToColumn(column + 10 + i * 3);
            Present(w);
            first.Add(Since(t0));
            while (paged.WindowsPending) { await Task.Delay(1); Dispatcher.UIThread.RunJobs(); }
            Present(w);
            complete.Add(Since(t0));
            if (column > 0) Assert.NotEmpty(paged.CachedWindow(0) ?? "");
        }
        output.WriteLine($"column {column}: window complete after " + string.Join(", ", complete.Order().Select(v => v.ToString("0.0"))) + " ms");
        Gate($"first jump to column {column}", first);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(50_000_000)]
    [InlineData(90_000_000)]
    public async Task Scrolling_after_the_index_exists_paints_the_window_within_the_budget(int column)
    {
        var (w, tab) = Open();
        var paged = tab.Paged!;
        var source = SourceOf(tab);
        await BuildIndex(w, tab, column);

        var times = new List<double>();
        for (int i = 0; i < 200; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            paged.ScrollToColumn(column + 10 + i * 37 % 5000);
            Present(w);
            times.Add(Since(t0));
            Assert.False(paged.WindowsPending);
            if (column > 0) Assert.NotEmpty(paged.CachedWindow(0) ?? "");
        }
        Gate($"scroll to column {column}", times);
    }
}
