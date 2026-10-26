using System.Diagnostics;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using AdapterCarets = NksHub.Textida.AvaloniaEditAdapter.Carets;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P2.2.1 gate: typing with many carets in a 1 MiB file. The cycle measured is the one of the single-caret gate in <c>EditorTests</c>: a key goes in, the document
/// changes, layout and the render jobs run. Headless runs measure CPU work, not the compositor, like every other number of the Perf category.
/// </summary>
[Collection("LocState")]
[Trait("Category", "Perf")]
public class MultiCaretPerfTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempDir _temp = new();
    private App? _app;

    public void Dispose()
    {
        if (_app is not null) TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private (MainWindow Window, DocTab Tab) OpenMiB()
    {
        _app = TestApp.Prepare(_temp);
        var w = _app.CreateWindow(null);
        var sb = new StringBuilder();
        for (int i = 0; sb.Length < 1 << 20; i++) sb.Append("line ").Append(i).Append(" lorem ipsum dolor sit amet consectetur\n");
        var tab = w.OpenFile(_temp.File("one-mib.txt", sb.ToString()), null);
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();
        w.UpdateLayout();
        return (w, tab);
    }

    private static (double P50, double P95, double P99) Percentiles(List<double> times)
    {
        times.Sort();
        return (times[times.Count / 2], times[(int)(times.Count * 0.95)], times[(int)(times.Count * 0.99)]);
    }

    private static void SpreadCarets(DocTab tab, int count)
    {
        var d = tab.Editor!.Document;
        int step = Math.Max(1, d.LineCount / count);
        var offsets = Enumerable.Range(0, count).Select(i => d.GetLineByNumber(1 + i * step).Offset + 5).ToArray();
        if (count == 1) tab.Editor.CaretOffset = offsets[0];
        else tab.Editor.SetCarets(AdapterCarets.Create([.. offsets.Select(Sel.At)], count - 1));
        Assert.Equal(count, tab.Editor.CaretCount);
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public void Typing_with_many_carets_in_a_one_mebibyte_file(int carets)
    {
        var (w, tab) = OpenMiB();
        SpreadCarets(tab, carets);
        w.UpdateLayout();
        for (int i = 0; i < 20; i++) { TestApp.Type(w, "w"); Dispatcher.UIThread.RunJobs(); } // warm up

        var times = new List<double>();
        for (int i = 0; i < 200; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            TestApp.Type(w, "x");
            w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            times.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
        }
        var (p50, p95, p99) = Percentiles(times);
        output.WriteLine($"carets {carets}: typing p50 {p50:0.0} ms, p95 {p95:0.0} ms, p99 {p99:0.0} ms (200 keystrokes after 20 warm-up)");
        Assert.Equal(carets, tab.Editor!.CaretCount);
        Assert.Equal(1, tab.Doc.Doc.UndoDepth); // one typing run is one undo step
        Assert.True(p95 <= 33 && p99 <= 80, $"{carets} carets: p95 {p95:0.0} ms, p99 {p99:0.0} ms");
    }

    [AvaloniaFact]
    public void Undo_and_redo_of_a_thousand_carets_take_their_time_in_one_step()
    {
        var (w, tab) = OpenMiB();
        string original = tab.Editor!.Document.Text;
        SpreadCarets(tab, 1000);
        TestApp.Type(w, "Q");
        Assert.Equal(1, tab.Doc.Doc.UndoDepth);
        long t0 = Stopwatch.GetTimestamp();
        TestApp.Press(w, Key.Z, TestApp.Mod);
        double undo = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        Assert.Equal(original, tab.Editor.Document.Text);
        t0 = Stopwatch.GetTimestamp();
        TestApp.Press(w, Key.Y, TestApp.Mod);
        double redo = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        output.WriteLine($"1000 carets: undo {undo:0.0} ms, redo {redo:0.0} ms");
        Assert.Equal(1000, tab.Editor.CaretCount);
        Assert.Equal(1000, tab.Editor.Document.Text.Split('Q').Length - 1);
    }
}
