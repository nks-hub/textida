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
