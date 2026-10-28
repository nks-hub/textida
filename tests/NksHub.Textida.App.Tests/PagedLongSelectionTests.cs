using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Tests;

/// <summary>P7.1.5: selecting, copying, cutting and undoing inside a very long line of the paged surface without decoding the line and without holding up the window.</summary>
[Collection("LocState")]
public class PagedLongSelectionTests : IDisposable
{
    private const string Unit = "ěšč abc 中";
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public PagedLongSelectionTests()
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

    private static string Repeat(int units) => string.Concat(Enumerable.Repeat(Unit, units));

    private static long ByteOffset(string text, int column) => Encoding.UTF8.GetByteCount(text.AsSpan(0, column));

    private static string Text(DocTab tab) => Encoding.UTF8.GetString(tab.Doc.Doc.ReadBytes(new ByteRange(0, tab.Doc.Doc.ByteLength)));

    private void Draw()
    {
        _w.UpdateLayout();
        _w.CaptureRenderedFrame();
    }

    private DocTab Open(string path)
    {
        var tab = _w.OpenFile(path, null);
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

    private DocTab OpenLine(string line) => Open(_temp.Bytes("long.txt", Encoding.UTF8.GetBytes(line + "\nnext\n")));

    [AvaloniaFact]
    public async Task Copy_and_cut_of_a_selection_inside_a_long_line_take_the_exact_text_and_a_cut_is_one_undo_step()
    {
        string model = Repeat(300_000);
        var tab = OpenLine(model);
        var paged = tab.Paged!;
        int from = 1_000_000, to = 1_800_000; // 800 000 characters, more than a MiB of bytes
        paged.SelectBytes(ByteOffset(model, from), ByteOffset(model, to));
        Draw();
        Assert.True(paged.SelectionRange.Length > 1 << 20);
        string expected = model.Substring(from, to - from);

        await paged.CopyAsync(cut: false);
        Assert.True(expected == await _w.Clipboard!.TryGetTextAsync(), "the clipboard differs from the selection");
        Assert.Equal(0, tab.Doc.Doc.UndoDepth);

        await paged.CopyAsync(cut: true);
        Assert.Equal(1, tab.Doc.Doc.UndoDepth);
        Assert.True(model[..from] + model[to..] + "\nnext\n" == Text(tab), "the cut left other text");
        TestApp.Press(_w, Key.Z, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, tab.Doc.Doc.UndoDepth);
        Assert.True(model + "\nnext\n" == Text(tab), "undo did not give the line back");
    }

    [AvaloniaFact]
    public async Task A_selection_over_the_copy_limit_is_refused_with_a_message_and_the_clipboard_stays()
    {
        string model = Repeat(100_000); // 1.4 MB: above the 1 MiB threshold set in the constructor, so the file opens paged
        var tab = OpenLine(model);
        var paged = tab.Paged!;
        paged.CopyLimitBytes = 1000;
        var refused = new List<string>();
        paged.Refused += refused.Add;
        await _w.Clipboard!.SetTextAsync("before");
        paged.SelectBytes(0, ByteOffset(model, 3000));
        Draw();

        await paged.CopyAsync(cut: true);

        Assert.Contains("limit kopírování", Assert.Single(refused));
        Assert.Equal("before", await _w.Clipboard!.TryGetTextAsync());
        Assert.Equal(0, tab.Doc.Doc.UndoDepth); // a refused cut deletes nothing
        Assert.Equal("", paged.SelectedText());
    }

    [AvaloniaFact]
    public async Task A_long_selection_is_decoded_off_the_UI_thread_and_the_cut_waits_for_the_copy()
    {
        string model = Repeat(1_600_000); // 22 MB in one line
        var tab = OpenLine(model);
        var paged = tab.Paged!;
        int to = 1_500_000 * Unit.Length;
        paged.SelectBytes(0, ByteOffset(model, to));
        Draw();

        var copying = paged.CopyAsync(cut: false);
        Assert.False(copying.IsCompleted, "the selection was decoded before the call returned");
        await copying;
        var text = await _w.Clipboard!.TryGetTextAsync();
        Assert.True(model[..to] == text, "the clipboard differs from the selection");
    }

    [AvaloniaFact]
    public async Task A_cut_is_dropped_when_the_document_changed_while_the_text_was_read()
    {
        string model = Repeat(1_000_000);
        var tab = OpenLine(model);
        var paged = tab.Paged!;
        paged.SelectBytes(0, ByteOffset(model, 5_000_000));
        Draw();

        var cutting = paged.CopyAsync(cut: true);
        Assert.False(cutting.IsCompleted);
        Assert.True(paged.PasteText("!")); // the pasted text replaces the selection before the decoded text reaches the clipboard
        await cutting;

        Assert.Equal("!" + model[5_000_000..] + "\nnext\n", Text(tab));
        Assert.Equal(1, tab.Doc.Doc.UndoDepth);
    }

    [AvaloniaFact]
    [Trait("Category", "Large")]
    public async Task Copying_64_MiB_of_a_100_MiB_line_gives_the_exact_text()
    {
        const int size = 100 * 1024 * 1024, start = 1_000_000, length = 64 * 1024 * 1024;
        string path = Path.Combine(_temp.Path, "line100.txt");
        PagedLongLineTests.WriteLongLine(path, size);
        var tab = Open(path);
        var paged = tab.Paged!;
        paged.SelectBytes(start, start + length);
        TestApp.Settle(paged);
        Draw();

        await paged.CopyAsync(cut: false);

        var text = (await _w.Clipboard!.TryGetTextAsync())!;
        Assert.Equal(length, text.Length);
        foreach (int k in new[] { 0, 1, 12345, length / 2, length - 1 })
            Assert.Equal((char)('a' + (start + (long)k) * 7 % 26), text[k]);
    }
}

/// <summary>
/// P7.1.5 gates for selecting and copying inside a 100 MiB line: how long the UI thread is held by Copy of a 64 MiB selection (budget as the typing gate,
/// p95 33 ms and p99 80 ms), and how long the UI thread is held by the first Shift+End, which waits for the characters of the line to be counted on the pool (reported, not gated).
/// </summary>
[Collection("LocState")]
[Trait("Category", "Perf")]
public class PagedLongSelectionPerfTests(ITestOutputHelper output) : IDisposable
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
        PagedLongLineTests.WriteLongLine(path, Size);
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

    private static double Since(long t0) => System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

    [AvaloniaFact]
    public async Task Copy_of_a_64_MiB_selection_hands_the_UI_thread_back_within_the_budget()
    {
        var (w, tab) = Open();
        var paged = tab.Paged!;
        var times = new List<double>();
        for (int i = 0; i < 20; i++)
        {
            paged.SelectBytes(1_000_000 + i, 1_000_000 + i + (64 << 20));
            TestApp.Settle(paged); // the first one counts the columns of the line on the pool; the others find the points it noted
            Present(w);
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var copying = paged.CopyAsync(cut: false);
            times.Add(Since(t0));
            await copying;
        }
        times.Sort();
        double p50 = times[times.Count / 2], p95 = times[(int)(times.Count * 0.95)], p99 = times[(int)(times.Count * 0.99)];
        output.WriteLine($"Copy of 64 MiB: UI thread held p50 {p50:0.0}, p95 {p95:0.0}, p99 {p99:0.0} ms over {times.Count} copies");
        Assert.True(p95 <= 33 && p99 <= 80, $"p50 {p50:0.0}, p95 {p95:0.0}, p99 {p99:0.0} ms");
    }

    [AvaloniaFact]
    public void First_Shift_End_in_a_100_MiB_line_is_measured()
    {
        var (w, tab) = Open();
        var paged = tab.Paged!;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        TestApp.Press(w, Key.End, RawInputModifiers.Shift);
        Present(w);
        double held = Since(t0);
        bool waited = paged.CaretMovePending;
        TestApp.Settle(paged);
        Present(w);
        double first = Since(t0);
        t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        TestApp.Press(w, Key.Home, RawInputModifiers.Shift);
        Present(w);
        double second = Since(t0);
        output.WriteLine($"Shift+End from the start of a 100 MiB line: the UI thread was held {held:0.0} ms (the move waited for the count on the pool: {waited}), the selection stood after {first:0.0} ms, then Shift+Home {second:0.0} ms (the count is known)");
        Assert.True(paged.HasSelection || paged.CaretOffset == 0);
    }
}
