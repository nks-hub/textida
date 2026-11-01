using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.6.4: the callers panel over a stand-in for the codegraph program; a tab with unsaved text says in the panel and above the text that the graph matches the files on disk.</summary>
[Collection("LocState")]
public class CallersAppTests : IDisposable
{
    private const string Answer = "{\"symbol\":\"target\",\"definitions\":[{\"definition\":{\"name\":\"target\",\"kind\":\"function\",\"filePath\":\"a.txt\",\"startLine\":1},\"callers\":[{\"name\":\"main\",\"kind\":\"function\",\"filePath\":\"b.txt\",\"startLine\":3,\"relationships\":[\"calls\"]}]}]}";

    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly string? _path = Environment.GetEnvironmentVariable("PATH");

    public CallersAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _path);
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private DocTab OpenInGraph()
    {
        string bin = Directory.CreateDirectory(Path.Combine(_temp.Path, "bin")).FullName;
        string exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;
        foreach (var extension in new[] { ".dll", ".runtimeconfig.json", ".deps.json" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.ToolEcho" + extension), Path.Combine(bin, "NksHub.Textida.ToolEcho" + extension), overwrite: true);
        string tool = Path.Combine(bin, exe("codegraph"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, exe("NksHub.Textida.ToolEcho")), tool, overwrite: true);
        File.WriteAllText(tool + ".sample", Answer);
        Environment.SetEnvironmentVariable("PATH", bin + Path.PathSeparator + _path);

        string project = Directory.CreateDirectory(Path.Combine(_temp.Path, "proj")).FullName;
        Directory.CreateDirectory(Path.Combine(project, ".codegraph"));
        var tab = _w.OpenFile(Path.Combine(project, "a.txt") is var file && !File.Exists(file) ? _temp.File("proj/a.txt", "target\n") : file, null);
        tab.Editor!.CaretOffset = 2;
        return tab;
    }

    private async Task RunAsync()
    {
        _dialogs.Answers.Enqueue(0); // the program is run once the user agreed
        Assert.Equal(Workbench.Commands.CommandOutcome.Handled, _w.ShowCallers());
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!_w.CallersWork.IsCompleted)
        {
            Assert.True(sw.Elapsed.TotalSeconds < 60, "The callers did not arrive.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        await _w.CallersWork;
    }

    [AvaloniaFact]
    public async Task A_saved_tab_lists_the_callers_without_a_bar_and_a_tab_with_unsaved_text_says_the_graph_matches_the_disk_in_the_panel_and_above_the_text()
    {
        var tab = OpenInGraph();

        await RunAsync();
        Assert.Null(tab.Bar);
        Assert.Contains(_w.ChoiceRows, r => r.Title == "main" && r.Detail == "b.txt:3");
        Assert.DoesNotContain(Loc.Get(K.CallersUnsaved), _w.ChoiceRows[0].Detail ?? "");

        tab.Editor!.Document.Insert(0, "edited ");
        await RunAsync();
        Assert.Equal(Loc.Get(K.CallersUnsaved), Assert.IsType<InfoBar>(tab.Bar).Message);
        Assert.Contains(Loc.Get(K.CallersUnsaved), _w.ChoiceRows[0].Detail);
    }
}
