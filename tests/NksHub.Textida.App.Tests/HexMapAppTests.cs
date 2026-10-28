using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>P5.6.4 in the window: the map of the file beside the grid, its marks, and the statistics of the selection.</summary>
[Collection("LocState")]
public class HexMapAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    private readonly ITestOutputHelper _output;

    public HexMapAppTests(ITestOutputHelper output)
    {
        _output = output;
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _w.Width = 1200;
        _w.Height = 800;
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    /// <summary>A file in four equal parts: zeros, 0xFF, text, noise.</summary>
    private static byte[] Quarters(int quarter = 100_000)
    {
        var bytes = new byte[quarter * 4];
        Array.Fill(bytes, (byte)0xFF, quarter, quarter);
        var text = "the quick brown fox jumps over the lazy dog\n"u8;
        for (int i = 2 * quarter; i < 3 * quarter; i++) bytes[i] = text[i % text.Length];
        new Random(2).NextBytes(bytes.AsSpan(3 * quarter));
        return bytes;
    }

    private HexView OpenHex(byte[] bytes)
    {
        _w.OpenFile(_temp.Bytes("map.bin", bytes), null);
        TestApp.Press(_w, Key.K, TestApp.Mod);
        TestApp.Press(_w, Key.H, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
        var hex = _w.Active!.Hex!;
        hex.Focus();
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return hex;
    }

    private static void PumpUntil(Func<bool> condition, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(20))
        {
            Thread.Sleep(5);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(condition(), what);
    }

    [AvaloniaFact]
    public void The_map_is_built_in_the_background_and_ends_exact_with_the_class_of_each_part()
    {
        var hex = OpenHex(Quarters());
        Assert.True(hex.Minimap.IsVisible);
        hex.Minimap.Rebuild();
        PumpUntil(() => hex.Minimap.Map is { IsExact: true }, "the map did not become exact");

        var map = hex.Minimap.Map!;
        Assert.Equal(ByteClass.Zero, map.Classes[map.BucketOf(50_000)]);
        Assert.Equal(ByteClass.Ones, map.Classes[map.BucketOf(150_000)]);
        Assert.Equal(ByteClass.Text, map.Classes[map.BucketOf(250_000)]);
        Assert.Equal(ByteClass.High, map.Classes[map.BucketOf(350_000)]);
        Assert.True(map.Entropy[map.BucketOf(350_000)] > 240);
        Assert.Equal(0, map.Entropy[map.BucketOf(50_000)]);
        Assert.NotNull(_w.CaptureRenderedFrame());
    }

    [AvaloniaFact]
    public void The_first_map_starts_as_soon_as_the_view_has_its_size_without_the_settle_delay()
    {
        var hex = OpenHex(Quarters(10_000));
        Assert.True(hex.Minimap.Map is not null || hex.Minimap.IsBuilding, "no build started when the view got its size");
        PumpUntil(() => hex.Minimap.Map is { IsExact: true }, "the first map did not become exact");
        Assert.Equal(ByteClass.Text, hex.Minimap.Map!.Classes[hex.Minimap.Map.BucketOf(25_000)]);
    }

    [AvaloniaFact]
    public void The_commands_show_the_map_switch_its_mode_and_open_the_statistics()
    {
        var hex = OpenHex(Quarters(10_000));
        Assert.Equal(CommandOutcome.Handled, _w.ToggleHexMinimap());
        Assert.False(hex.Minimap.IsVisible);
        Assert.Equal(CommandOutcome.Handled, _w.ToggleHexMinimap());
        Assert.True(hex.Minimap.IsVisible);
        Assert.Equal(ByteMapMode.Entropy, hex.Minimap.Mode);
        Assert.Equal(CommandOutcome.Handled, _w.CycleHexMapMode());
        Assert.Equal(ByteMapMode.Class, hex.Minimap.Mode);
        Assert.Contains(Loc.Get(K.HexMapClass), _w.StatusLeftText);
        Assert.False(hex.Statistics.IsVisible);
        Assert.Equal(CommandOutcome.Handled, _w.ToggleHexStatistics());
        Assert.True(hex.Statistics.IsVisible);
        Assert.Equal(CommandOutcome.Handled, _w.ToggleHexStatistics());
        Assert.False(hex.Statistics.IsVisible);
        hex.SetMinimapVisible(true);
    }

    [AvaloniaFact]
    public void A_click_on_the_map_scrolls_the_grid_there()
    {
        var hex = OpenHex(Quarters(100_000));
        var map = hex.Minimap;
        Assert.True(map.Bounds.Height > 100, $"map height {map.Bounds.Height}");
        map.ScrollAt(map.Bounds.Height * 0.75);
        long middle = hex.FirstByte + hex.VisibleBytes / 2;
        Assert.InRange(middle, 300_000 - hex.VisibleBytes, 300_000 + hex.VisibleBytes);
        map.ScrollAt(0);
        Assert.Equal(0, hex.FirstByte);
        map.ScrollAt(map.Bounds.Height);
        Assert.True(hex.FirstByte + hex.VisibleBytes >= 400_000 - hex.VisibleBytes);
    }

    [AvaloniaFact]
    public void The_matches_of_a_search_are_marked_in_the_lane()
    {
        var bytes = Quarters(20_000);
        foreach (int at in new[] { 100, 30_000, 70_000 }) new byte[] { 0x7E, 0x7E, 0x7E, 0x7E }.CopyTo(bytes, at);
        bytes[0] = 0;
        var hex = OpenHex(bytes);
        _dialogs.Prompts.Enqueue("7E 7E 7E 7E");
        TestApp.Press(_w, Key.F, TestApp.Mod);
        PumpUntil(() => hex.Minimap.Matches.Count > 0, "no matches were marked");
        Assert.Equal(new long[] { 100, 30_000, 70_000 }, hex.Minimap.Matches.Where(m => m is 100 or 30_000 or 70_000));
        Assert.True(hex.Minimap.Matches.Count >= 3);
        hex.Editor.Write(new ByteRange(0, 1), [9]);
        Assert.Empty(hex.Minimap.Matches); // the offsets belong to the revision they were found in
    }

    private sealed record Shot(byte[] Data, int Stride);

    private Shot Capture()
    {
        _w.UpdateLayout();
        for (int i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(2);
        }
        using var buffer = _w.CaptureRenderedFrame()!.Lock();
        var data = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, data, 0, data.Length);
        return new Shot(data, buffer.RowBytes);
    }

    private static List<(int X, int Y)> Changed(Shot a, Shot b, int left, int top, int width, int height)
    {
        var changed = new List<(int, int)>();
        for (int y = top; y < top + height; y++)
            for (int x = left; x < left + width; x++)
            {
                int at = y * a.Stride + x * 4;
                if (at + 3 < a.Data.Length && !a.Data.AsSpan(at, 4).SequenceEqual(b.Data.AsSpan(at, 4))) changed.Add((x, y));
            }
        return changed;
    }

    /// <summary>What a change makes in the lane of the map (the six pixels right of the map): the pixels that differ, as screen offsets inside the lane (X from its left edge, Y from the top of the map), and the row the offset has.</summary>
    private (List<(int X, int Y)> Pixels, Func<long, int> RowOf) LaneAfter(HexView hex, Action change)
    {
        hex.Minimap.Rebuild();
        PumpUntil(() => hex.Minimap.Map is { IsExact: true }, "the map did not become exact");
        var map = hex.Minimap.Map!;
        double height = hex.Minimap.Bounds.Height;
        var origin = hex.Minimap.TranslatePoint(default, _w)!.Value;
        int left = (int)origin.X + (int)HexMinimap.MapWidth - 10 + 2, top = (int)origin.Y;
        var before = Capture();
        change();
        var after = Capture();
        var pixels = Changed(before, after, left, top, 6, (int)height).Select(p => (p.X - left, p.Y - top)).ToList();
        return (pixels, offset => (int)Math.Min(height - 2, Math.Floor(map.BucketOf(offset) * height / map.Buckets)));
    }

    [AvaloniaFact]
    public void A_bookmark_is_marked_in_the_left_half_of_the_lane_at_its_place_in_the_file()
    {
        var hex = OpenHex(Quarters());
        var (pixels, rowOf) = LaneAfter(hex, () => hex.Marks.Add(new ByteRange(300_000, 300_004), "b"));

        Assert.NotEmpty(pixels);
        Assert.All(pixels, p => { Assert.InRange(p.Y, rowOf(300_000), rowOf(300_000) + 1); Assert.InRange(p.X, 0, 2); });
    }

    [AvaloniaFact]
    public void A_change_since_the_save_is_marked_in_the_right_half_of_the_lane_at_its_place_in_the_file()
    {
        var hex = OpenHex(Quarters());
        var (pixels, rowOf) = LaneAfter(hex, () => hex.Editor.Write(new ByteRange(50_000, 50_001), [9]));

        Assert.NotEmpty(pixels);
        Assert.All(pixels, p => { Assert.InRange(p.Y, rowOf(50_000), rowOf(50_000) + 1); Assert.InRange(p.X, 3, 5); });
    }

    [AvaloniaFact]
    public void A_match_is_marked_across_the_lane_at_its_place_in_the_file()
    {
        var hex = OpenHex(Quarters());
        var (pixels, rowOf) = LaneAfter(hex, () => hex.Minimap.Matches = [150_000]);

        Assert.NotEmpty(pixels);
        Assert.All(pixels, p => Assert.Equal(rowOf(150_000), p.Y));
        Assert.Contains(pixels, p => p.X < 3);
        Assert.Contains(pixels, p => p.X >= 3);
    }

    [AvaloniaFact]
    public void The_rectangle_of_the_part_on_screen_follows_the_scrolling_of_the_grid()
    {
        var hex = OpenHex(Quarters());
        hex.Minimap.Rebuild();
        PumpUntil(() => hex.Minimap.Map is { IsExact: true }, "the map did not become exact");
        var origin = hex.Minimap.TranslatePoint(default, _w)!.Value;
        int left = (int)origin.X, top = (int)origin.Y, width = (int)HexMinimap.MapWidth - 10, height = (int)hex.Minimap.Bounds.Height;

        var atTop = Capture();
        hex.Minimap.ScrollAt(height * 0.75);
        var scrolled = Capture();

        Assert.True(Changed(atTop, scrolled, left, top, width, height / 2).Count > 0, "the rectangle did not leave the top of the map");
        Assert.True(Changed(atTop, scrolled, left, top + height / 2, width, height / 2).Count > 0, "the rectangle did not arrive at three quarters of the map");
        hex.Minimap.ScrollAt(0);
        var back = Capture();
        Assert.Equal(0, Changed(atTop, back, left, top, width, height).Count);
    }

    /// <summary>A hex view of a small file whose watcher is off and whose reports of an external change are collected with the thread they came on.</summary>
    private (HexView Hex, string Path, FileFingerprint Print, List<int> Threads) WatchedHex()
    {
        var hex = OpenHex(Quarters(1000));
        var doc = hex.Document;
        doc.StopWatching();
        var threads = new List<int>();
        doc.ExternalChangeDetected += (_, _) => { lock (threads) threads.Add(Environment.CurrentManagedThreadId); };
        return (hex, doc.FilePath!, doc.Fingerprint!, threads);
    }

    [AvaloniaFact]
    public async Task The_file_check_leaves_a_file_with_the_recorded_size_and_time_alone()
    {
        var (hex, path, print, threads) = WatchedHex();

        // other bytes with the same size and write time: the hash would tell, the quick look at size and time does not start a check
        var bytes = File.ReadAllBytes(path);
        bytes[10] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, print.LastWriteUtc);
        Assert.False(FileFingerprint.TryRead(path)!.SameContent(print));
        hex.FileChangedBehind();
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(500);

        lock (threads) Assert.Empty(threads);
    }

    [AvaloniaFact]
    public void The_file_check_of_a_file_of_another_size_hashes_it_on_a_worker_not_on_the_ui_thread()
    {
        var (hex, path, _, threads) = WatchedHex();
        int uiThread = Environment.CurrentManagedThreadId;

        File.AppendAllText(path, "more");
        hex.FileChangedBehind();
        Dispatcher.UIThread.RunJobs();
        PumpUntil(() => { lock (threads) return threads.Count > 0; }, "the changed file was not reported");

        lock (threads) Assert.NotEqual(uiThread, Assert.Single(threads));
    }

    [AvaloniaFact]
    public void The_statistics_follow_the_selection_and_fall_back_to_the_whole_file()
    {
        var hex = OpenHex(Quarters(10_000));
        _w.ToggleHexStatistics();
        var panel = hex.Statistics;
        PumpUntil(() => panel.Shown is { Total: 40_000 }, "the whole file was not counted");
        Assert.Contains(Loc.Get(K.HexStatsFile, ("bytes", 40_000)), panel.RangeText);

        hex.Select(0, 10_000); // zeros only
        panel.Refresh(); // the panel waits a moment for the selection to settle; the test does not
        PumpUntil(() => panel.Shown is { Total: 10_000 }, "the selection was not counted");
        Assert.Equal(1, panel.Shown!.Distinct);
        Assert.Equal(0.0, panel.Shown.Entropy);
        Assert.Contains("1", panel.FiguresText);
        Assert.Contains("100%", panel.FiguresText);

        hex.Select(30_000, 40_000); // noise
        panel.Refresh();
        PumpUntil(() => panel.Shown is { Total: 10_000 } h && h.Distinct > 250, "the noise was not counted");
        Assert.True(panel.Shown!.Entropy > 7.9);
        Assert.Contains(Loc.Get(K.HexStatsDistinct, ("n", panel.Shown.Distinct)), panel.FiguresText);
    }

    [AvaloniaFact]
    [Trait("Category", "Perf")]
    public void The_map_of_a_fifty_gibibyte_file_shows_within_a_second_of_opening_it_in_hex()
    {
        const long length = 50L << 30;
        string path = Path.Combine(_temp.Path, "fifty.bin");
        File.WriteAllBytes(path, []);
        if (OperatingSystem.IsWindows())
        {
            var command = new ProcessStartInfo("fsutil") { CreateNoWindow = true, UseShellExecute = false };
            foreach (string arg in new[] { "sparse", "setflag", path }) command.ArgumentList.Add(arg);
            using var process = Process.Start(command)!;
            process.WaitForExit();
        }
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write)) stream.SetLength(length);

        NksHub.Textida.Core.TypingTrace.Reset();
        NksHub.Textida.Core.TypingTrace.Enabled = true;
        var clock = Stopwatch.StartNew();
        var hex = _w.OpenHex(path, null).Hex!;
        double opened = clock.Elapsed.TotalMilliseconds;
        NksHub.Textida.Core.TypingTrace.Enabled = false;
        _output.WriteLine("50 GiB hex, spans of the open:\n" + NksHub.Textida.Core.TypingTrace.Summary());
        _w.UpdateLayout();
        PumpUntil(() => hex.Minimap.Map is not null, "the map of the 50 GiB file did not arrive");
        _output.WriteLine($"50 GiB hex: open {opened:0} ms, map {clock.Elapsed.TotalMilliseconds:0} ms");
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), $"the map showed {clock.Elapsed.TotalMilliseconds:0} ms after the open started (the open took {opened:0} ms)");
        Assert.Equal(length, hex.Document.ByteLength);
        Assert.False(hex.Minimap.Map!.IsExact);
    }

    [AvaloniaFact]
    [Trait("Category", "Perf")]
    public void Scrolling_while_the_map_is_refined_in_the_background_stays_within_the_budget()
    {
        const long length = (1L << 30) + 1; // the biggest file that is read whole; its map is refined for a good while
        string path = Path.Combine(_temp.Path, "refine.bin");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write)) stream.SetLength(length);
        _w.OpenFile(path, null);
        TestApp.Press(_w, Key.K, TestApp.Mod);
        TestApp.Press(_w, Key.H, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
        var hex = _w.Active!.Hex!;
        _w.Width = 1800;
        _w.Height = 1300;
        _w.UpdateLayout();
        hex.Minimap.Rebuild();
        PumpUntil(() => hex.Minimap.Map is not null, "the first pass of the map did not arrive");
        Assert.True(hex.Minimap.IsBuilding, "the map was already exact; nothing was refined during the scrolling");

        var times = new List<double>();
        var random = new Random(1);
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < 300; i++)
        {
            Assert.True(clock.Elapsed < TimeSpan.FromMinutes(2), $"{i} of 300 scrolls took {clock.Elapsed.TotalSeconds:0} s; something blocks the UI thread");
            hex.MoveTo(random.NextInt64(0, length - 1000));
            hex.Center(hex.Caret);
            _w.UpdateLayout();
            _w.CaptureRenderedFrame();
            times.Add(hex.RenderMs);
        }
        times.Sort();
        double p95 = times[(int)(times.Count * 0.95)], p99 = times[(int)(times.Count * 0.99)];
        string figures = $"p50 {times[times.Count / 2]:0.0}, p95 {p95:0.0}, p99 {p99:0.0}, max {times[^1]:0.0} ms over {times.Count} scrolls";
        Assert.True(p95 <= 33, figures);
        Assert.True(p99 <= 80, figures);
    }
}
