using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public class HistoryAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public HistoryAppTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static async Task Pump(int milliseconds)
    {
        for (var sw = Stopwatch.StartNew(); sw.ElapsedMilliseconds < milliseconds;)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    [AvaloniaFact]
    public async Task A_file_opened_in_the_paged_backend_is_not_copied_into_the_history()
    {
        string small = _temp.File("small.txt", "one\n");
        _w.OpenFile(small, null);
        for (var sw = Stopwatch.StartNew(); _app.History.List(small).Count == 0 && sw.ElapsedMilliseconds < 10_000;) await Pump(20);
        Assert.NotEmpty(_app.History.List(small));

        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        // the threshold is in MiB
        string large = _temp.File("large.txt", string.Concat(Enumerable.Repeat("a line of the large file\n", 50_000)));
        var tab = _w.OpenFile(large, null);
        Assert.True(tab.Doc.IsLarge);
        await Pump(500);
        Assert.Empty(_app.History.List(large));
    }
}
