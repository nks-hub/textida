using System.Diagnostics;
using Avalonia.Headless.XUnit;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.6.4: the callers of the symbol under the caret from the graph of the project; a bar on a tab with unsaved text; nothing without a .codegraph folder.</summary>
[Collection("LocState")]
public class CallersAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;

    public CallersAppTests() => _app = TestApp.Prepare(_temp, _dialogs);

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        _temp.Dispose();
    }

    [AvaloniaFact]
    public async Task Without_a_graph_folder_the_command_says_how_to_make_one_and_starts_nothing()
    {
        var w = _app.CreateWindow(null);
        var tab = w.OpenFile(_temp.File("a.js", "function helper() {}\n"), null);
        tab.Editor!.CaretOffset = 12;

        await w.ShowCallersAsync(tab);

        Assert.Contains("codegraph init", w.StatusMessageText);
        Assert.Empty(_dialogs.Asked);
    }

    [AvaloniaFact]
    public async Task The_callers_are_listed_with_their_places_and_a_tab_with_unsaved_text_gets_the_bar()
    {
        if (ToolLocator.Find("codegraph") is not { } program) return; // codegraph is not installed here
        string src = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "a.js"), "function helper(x) { return x + 1; }\nfunction caller(y) { return helper(y); }\n");
        File.WriteAllText(Path.Combine(src, "b.js"), "import { helper } from \"./a.js\";\nexport function third() { return helper(3); }\n");
        var init = ProcessLaunch.IsBatch(program) ? new ProcessStartInfo(program, "init -i .") : new ProcessStartInfo(program) { ArgumentList = { "init", "-i", "." } };
        init.WorkingDirectory = _temp.Path;
        init.UseShellExecute = false;
        init.CreateNoWindow = true;
        init.RedirectStandardOutput = init.RedirectStandardError = true;
        using (var process = Process.Start(init)!) await process.WaitForExitAsync();

        var w = _app.CreateWindow(null);
        var tab = w.OpenFile(Path.Combine(src, "a.js"), null);
        tab.Editor!.CaretOffset = 10; // inside "helper"
        tab.Editor.Document.Insert(tab.Editor.Document.TextLength, "// unsaved\n");

        _dialogs.Answers.Enqueue(0); // approve the program
        await w.ShowCallersAsync(tab);

        Assert.True(w.IsPaletteOpen);
        var rows = w.QuickResults;
        Assert.Equal(["caller", "third"], rows.Skip(1).Select(r => r.Title).Order());
        Assert.Contains(rows.Skip(1), r => r.Detail == "src/b.js:2");
        Assert.NotNull(tab.Bar); // the graph is the state on disk
        rows.Single(r => r.Title == "third").Run();
        Assert.EndsWith("b.js", w.Active!.Path);
    }
}
