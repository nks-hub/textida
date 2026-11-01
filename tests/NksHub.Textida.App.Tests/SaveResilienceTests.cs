using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class SaveResilienceTests
{
    [AvaloniaTheory]
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

            var samples = new List<double>();
            long before = tab.Hex.Caret;
            for (int i = 0; i < 30; i++)
            {
                var sample = Stopwatch.StartNew();
                TestApp.Press(window, Key.Right);
                samples.Add(sample.Elapsed.TotalMilliseconds);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(tab.Hex.Caret > before);
            samples.Sort();
            Assert.True(samples[28] < 33, $"Keyboard p95 during flush: {samples[28]:F1} ms (limit 33 ms)");
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
        }
        finally
        {
            release.Set();
            TestApp.Cleanup(app);
            Loc.SetLanguage(Loc.DefaultLanguage);
        }
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
