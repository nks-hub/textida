using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.5.4: opening a .NET assembly shows decompiled C# in a read-only tab.</summary>
[Collection("LocState")]
public class AssemblyViewAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;

    public AssemblyViewAppTests() => _app = TestApp.Prepare(_temp, _dialogs);

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        _temp.Dispose();
    }

    [AvaloniaFact]
    public async Task A_managed_dll_opens_as_read_only_csharp_and_the_same_file_opens_one_tab_only()
    {
        string dll = typeof(System.Collections.Generic.SortedSet<int>).Assembly.Location;
        var w = _app.CreateWindow(null);

        var tab = w.OpenFile(dll, null);
        Assert.Null(tab.Path);
        Assert.True(tab.Doc.ReadOnly);
        Assert.Equal("csharp", tab.Doc.Language.Id);
        for (var clock = System.Diagnostics.Stopwatch.StartNew(); !w.AssemblyWork.IsCompleted && clock.ElapsedMilliseconds < 60_000;)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }
        await w.AssemblyWork;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("class SortedSet<T>", tab.Editor!.Document.Text);

        int tabs = w.Tabs.Count();
        Assert.Same(tab, w.OpenFile(dll, null));
        Assert.Equal(tabs, w.Tabs.Count());
    }

    /// <summary>Closing the tab stops the decompiler (System.Private.CoreLib runs into its time limit otherwise) and the text that would have come is not shown anywhere.</summary>
    [AvaloniaFact]
    public async Task Closing_the_tab_stops_a_long_decompile_and_shows_nothing_afterwards()
    {
        string dll = typeof(object).Assembly.Location;
        var w = _app.CreateWindow(null);
        var tab = w.OpenFile(dll, null);
        var text = tab.Editor!.Document;
        string working = text.Text;
        await Task.Delay(500); // the decompiler is under way
        Assert.False(w.AssemblyWork.IsCompleted, "the decompile ended before the tab was closed");

        var close = w.CloseTabAsync(tab);
        Dispatcher.UIThread.RunJobs();
        Assert.True(await close);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!w.AssemblyWork.IsCompleted && clock.ElapsedMilliseconds < 30_000)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(w.AssemblyWork.IsCompleted && clock.ElapsedMilliseconds < 5_000, $"the decompile ran on for {clock.ElapsedMilliseconds} ms after the tab was closed");
        await w.AssemblyWork;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(working, text.Text);
    }

    [AvaloniaFact]
    public void The_setting_turns_it_off_and_a_file_that_is_not_an_assembly_opens_as_before()
    {
        string dll = typeof(System.Collections.Generic.SortedSet<int>).Assembly.Location;
        var w = _app.CreateWindow(null);
        _app.Workbench.Set("files.decompileAssemblies", false);

        var tab = w.OpenFile(dll, null);
        Assert.NotNull(tab.Path);

        _app.Workbench.Set("files.decompileAssemblies", true);
        var fake = w.OpenFile(_temp.File("not.dll", "plain text"), null);
        Assert.NotNull(fake.Path);
    }
}
