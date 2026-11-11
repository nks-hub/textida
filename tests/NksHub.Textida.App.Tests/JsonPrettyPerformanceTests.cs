using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Tests;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
[Trait("Category", "Perf")]
public sealed class JsonPrettyPerformanceTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempDir temp = new();
    private App? app;
    private MainWindow? window;

    public void Dispose()
    {
        if (app is not null) TestApp.Cleanup(app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        temp.Dispose();
    }

    private string WriteJson()
    {
        string path = Path.Combine(temp.Path, "large.json");
        HugeText.RequireRoom(path, 3L << 30);
        byte[] record = "{\"id\":\"abcdefgh\",\"n\":12345,\"ok\":true},"u8.ToArray();
        byte[] block = new byte[record.Length * 4096];
        for (int i = 0; i < block.Length; i += record.Length) record.CopyTo(block, i);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.WriteByte((byte)'[');
        while (file.Position < 1L << 30) file.Write(block);
        file.Position--;
        file.WriteByte((byte)']');
        output.WriteLine($"Single-line JSON file: {file.Length} bytes");
        return path;
    }

    private void Frame()
    {
        window!.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(frame.PixelSize.Width > 0 && frame.PixelSize.Height > 0);
    }

    private double Measure(Action action)
    {
        long start = Stopwatch.GetTimestamp();
        action();
        Frame();
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private void CheckLatency(string name, List<double> times)
    {
        times.Sort();
        double p95 = times[(int)(times.Count * 0.95)];
        output.WriteLine($"{name}: {times.Count} samples, p50={times[times.Count / 2]:F2} ms, p95={p95:F2} ms, max={times[^1]:F2} ms (input through headless rendered frame)");
        Assert.True(p95 <= 33, $"{name} p95={p95:F2} ms exceeds 33 ms");
    }

    private static byte[] Hash(string path)
    {
        using var file = File.OpenRead(path);
        return SHA256.HashData(file);
    }

    [AvaloniaFact]
    public async Task A_gibibyte_json_opens_within_one_second_and_scroll_fold_and_typing_stay_within_33_ms()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("TEXTIDA_HUGE") != "1", "Requires TEXTIDA_HUGE=1.");
        string path = WriteJson();
        byte[] original = Hash(path);
        app = TestApp.Prepare(temp);
        Loc.SetLanguage("en");
        app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        window = app.CreateWindow(null);
        Frame();
        using var self = Process.GetCurrentProcess();
        self.Refresh();
        long before = self.PrivateMemorySize64;
        var clock = Stopwatch.StartNew();
        var tab = window.OpenFile(path, null);
        Assert.NotNull(tab.Paged);
        window.SetJsonPretty(tab, true);
        var view = Assert.IsType<JsonPrettyView>(window.ActiveJson);
        Frame();
        double firstScreen = clock.Elapsed.TotalMilliseconds;
        output.WriteLine($"First screen including open: {firstScreen:F2} ms, viewport reads={view.Outline!.BytesRead} bytes");
        Assert.Equal("[", view.RowText(0));
        Assert.Equal("    \"id\": \"abcdefgh\",", view.RowText(2));
        Assert.True(firstScreen <= 1000, $"First screen took {firstScreen:F2} ms");
        Assert.True(view.Outline.BytesRead < 1L << 20);

        Assert.True(await window.SaveAsync(tab));
        Assert.Equal(original, Hash(path));
        for (var wait = Stopwatch.StartNew(); view.Outline.State.Lines < 10_000 && wait.Elapsed < TimeSpan.FromSeconds(30);)
            await Task.Delay(5);
        Assert.True(view.Outline.State.Lines >= 10_000);
        view.Focus();

        var scroll = new List<double>();
        for (int i = 0; i < 270; i++)
        {
            double ms = Measure(() => TestApp.Press(window, i % 40 < 30 ? Key.PageDown : Key.PageUp));
            if (i >= 20) scroll.Add(ms);
        }
        Assert.True(view.CaretLine > 0);

        view.MoveCaret(1, 0);
        var fold = new List<double>();
        for (int i = 0; i < 270; i++)
        {
            bool toggled = false;
            double ms = Measure(() => toggled = view.ToggleFold(1));
            Assert.True(toggled);
            Assert.Equal(i % 2 == 0, view.Folds.IsFolded(1));
            if (i >= 20) fold.Add(ms);
        }

        view.MoveCaret(2, "    \"id\": \"abc".Length);
        long at = view.SourceOffset;
        var typing = new List<double>();
        for (int i = 0; i < 270; i++)
        {
            double ms = Measure(() => TestApp.Type(window, "x"));
            if (i >= 20) typing.Add(ms);
        }
        Assert.Equal(at + 270, view.SourceOffset);
        Assert.Equal(new string('x', 270), System.Text.Encoding.UTF8.GetString(tab.Doc.Doc.ReadBytes(new(at, at + 270))));
        TestApp.Press(window, Key.Z, TestApp.Mod);
        Assert.False(tab.Doc.Doc.IsModified);
        self.Refresh();
        long grown = self.PrivateMemorySize64 - before;
        output.WriteLine($"Private memory growth after input: {grown / 1048576.0:F2} MiB, scanned={view.Outline.ScannedBytes} bytes, complete={view.Outline.State.Complete}");
        CheckLatency("Scroll", scroll);
        CheckLatency("Fold", fold);
        CheckLatency("Typing", typing);
        Assert.True(grown <= 350L << 20, $"Private memory grew by {grown} bytes");
    }
}
