using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>The status bar update that follows every caret or text change must stay small: it used to resolve every setting again, with several allocations each.</summary>
[Collection("LocState")]
public class StatusBarAllocationTests(ITestOutputHelper output) : IDisposable
{
    private const int Keys = 100;
    private const int BoundPerUpdate = 8_000; // 18 000 before the settings were read without copies and number text round trips

    private readonly TempDir _temp = new();
    private App? _app;

    public void Dispose()
    {
        if (_app is not null) TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    /// <summary>Bytes the current thread allocates in one call of the status update, averaged over keystrokes in the active tab.</summary>
    private long StatusBytesPerUpdate(MainWindow w)
    {
        var update = typeof(MainWindow).GetMethod("UpdateStatus", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int i = 0; i < 20; i++) { w.KeyTextInput("a"); update.Invoke(w, null); }
        long status = 0;
        for (int i = 0; i < Keys; i++)
        {
            w.KeyTextInput("a");
            long before = GC.GetAllocatedBytesForCurrentThread();
            update.Invoke(w, null);
            status += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        output.WriteLine($"{status / Keys} B per status update");
        return status / Keys;
    }

    [AvaloniaFact]
    public void Status_update_on_the_text_surface_allocates_little()
    {
        _app = TestApp.Prepare(_temp);
        var w = _app.CreateWindow(null);
        var tab = w.NewDocument();
        tab.Editor!.Document.Text = string.Concat(Enumerable.Range(0, 3000).Select(i => $"line {i} " + new string('x', i % 50) + "\n")) + new string('y', 200_000);
        Dispatcher.UIThread.RunJobs();
        tab.Doc.FlushEol();
        tab.Editor.TextArea.Focus();
        tab.Editor.CaretOffset = 1000;

        Assert.True(StatusBytesPerUpdate(w) < BoundPerUpdate);
    }

    [AvaloniaFact]
    public void Status_update_on_the_paged_surface_allocates_little()
    {
        _app = TestApp.Prepare(_temp);
        var w = _app.CreateWindow(null);
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        string text = string.Concat(Enumerable.Range(0, 60_000).Select(i => $"line {i} " + new string('x', i % 50) + "\n"));
        var tab = w.OpenFile(_temp.Bytes("big.txt", Encoding.UTF8.GetBytes(text)), null);
        Assert.True(tab.Doc.IsLarge);
        w.UpdateLayout();
        tab.Paged!.Focus();
        Dispatcher.UIThread.RunJobs();
        for (int i = 0; i < 600 && !tab.Paged.Index.Complete; i++) { Thread.Sleep(25); Dispatcher.UIThread.RunJobs(); }
        tab.Paged.GoToOffset(1000);

        Assert.True(StatusBytesPerUpdate(w) < BoundPerUpdate);
    }
}
