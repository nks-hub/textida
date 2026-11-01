using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

[Collection("Latency")]
public sealed class SaveResilienceTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [Trait("Category", "Perf")]
    [InlineData(false)]
    [InlineData(true)]
    public void A_stream_flush_delayed_by_1600_ms_keeps_keyboard_navigation_responsive_and_only_confirms_after_flush(bool cancel)
    {
        using var temporary = new TempDir();
        var app = TestApp.Prepare(temporary, new ScriptedDialogs());
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
            var window = app.CreateWindow(null);
            var original = new byte[64 << 10];
            new Random(71).NextBytes(original);
            original[0] = 0;
            var tab = window.OpenHex(temporary.Bytes("save.bin", original), null);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(tab.Hex);
            tab.Hex.Focus();
            var document = tab.Doc.Doc;
            Assert.True(document.Insert(100, "pending revision"u8.ToArray(), new() { RawBytes = true }).Applied);
            byte[] expected = [.. original[..100], .. "pending revision"u8.ToArray(), .. original[100..]];
            for (int i = 0; i < 5; i++)
            {
                TestApp.Press(window, Key.Right);
                Dispatcher.UIThread.RunJobs();
            }
            var baseline = MeasureNavigation(window);
            output.WriteLine($"navigation before save: n=30, input-to-render p95 {baseline.P95:F3} ms, max {baseline.Max:F3} ms; draw p95 {baseline.RenderP95:F3} ms, max {baseline.RenderMax:F3} ms");
            int confirmed = 0;
            document.Saved += (_, _) => confirmed++;
            var delay = new Stopwatch();
            document.BeforeStreamFlush = ct =>
            {
                delay.Start();
                entered.Set();
                ct.WaitHandle.WaitOne(1600);
                release.Wait(ct);
            };
            TestApp.Press(window, Key.S, TestApp.Mod);
            PumpUntil(() => entered.IsSet);

            long before = tab.Hex.Caret;
            var during = MeasureNavigation(window);
            output.WriteLine($"navigation during flush: n=30, input-to-render p95 {during.P95:F3} ms, max {during.Max:F3} ms; draw p95 {during.RenderP95:F3} ms, max {during.RenderMax:F3} ms");
            Assert.True(tab.Hex.Caret > before);
            Assert.True(document.IsSaving);
            Assert.Equal(0, confirmed);
            Assert.Equal(Hash(original), Hash(File.ReadAllBytes(tab.Path!)));
            PumpUntil(() => delay.ElapsedMilliseconds >= 1600);
            Assert.True(document.IsSaving);
            Assert.Equal(0, confirmed);

            if (cancel) TestApp.Press(window, Key.Escape);
            else release.Set();
            PumpUntil(() => !document.IsSaving);

            Assert.Equal(cancel ? 0 : 1, confirmed);
            Assert.Equal(cancel, document.IsModified);
            Assert.Equal(Hash(cancel ? original : expected), Hash(File.ReadAllBytes(tab.Path!)));
            Assert.Equal(Hash(expected), Hash(document.ReadBytes(new(0, document.ByteLength))));
            Assert.True(during.P95 < 33, $"Keyboard p95 during flush: {during.P95:F1} ms (limit 33 ms); before save {baseline.P95:F1} ms");
        }
        finally
        {
            release.Set();
            TestApp.Cleanup(app);
            Loc.SetLanguage(Loc.DefaultLanguage);
        }
    }

    private static (double P95, double Max, double RenderP95, double RenderMax) MeasureNavigation(MainWindow window)
    {
        var samples = new List<double>();
        var renders = new List<double>();
        var hex = window.Active!.Hex!;
        for (int i = 0; i < 30; i++)
        {
            long caret = hex.Caret, previousFrame = hex.RenderedAt;
            long start = Stopwatch.GetTimestamp();
            TestApp.Press(window, Key.Right);
            window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(caret + 1, hex.Caret);
            long renderedAt = hex.RenderedAt;
            Assert.True(renderedAt > previousFrame && renderedAt >= start, "The moved caret did not produce a new hex frame.");
            // RenderedAt marks the changed caret's drawing, before the headless helper drains further frames and reads the bitmap back.
            samples.Add(Stopwatch.GetElapsedTime(start, renderedAt).TotalMilliseconds);
            renders.Add(hex.RenderMs);
        }
        samples.Sort();
        renders.Sort();
        return (samples[(int)(samples.Count * 0.95)], samples[^1], renders[(int)(renders.Count * 0.95)], renders[^1]);
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(20))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Assert.True(condition(), "The asynchronous save did not reach the expected state.");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
