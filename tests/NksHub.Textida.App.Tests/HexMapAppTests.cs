using System.Diagnostics;
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

    public HexMapAppTests()
    {
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
        Assert.True(p95 <= 33, $"p95 {p95:0.0} ms");
        Assert.True(p99 <= 80, $"p99 {p99:0.0} ms");
    }
}
