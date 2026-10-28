using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P8.2.2 with a real GNU make and a real C compiler (category RealTools, skipped where they are not in PATH): a target picked from the list of the open Makefile runs, a compiler error in its output opens the file at the
/// line and column, and so does an error of make itself. The make of PATH is used; <c>TEXTIDA_TEST_GNUMAKE</c> names a second one (a newer GNU make next to the one the system has).
/// </summary>
[Collection("LocState")]
[Trait("Category", "RealTools")]
public class MakeRealToolsTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public MakeRealToolsTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        _w.StopTool();
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private List<string> Lines() => [.. _app.Output.Read(OutputChannel.Tasks).Select(l => l.Text)];

    private int OutputLineIndex(string contains) => Array.FindIndex(_w.OutputPanelText.Split('\n'), l => l.Contains(contains, StringComparison.Ordinal));

    /// <summary>Picks <paramref name="target"/> from the list of the open Makefile (the yes to the question is given) and waits until make has ended.</summary>
    private static string FinishedPrefix => Loc.Get(K.ToolOutFinished, ("code", "\u0001"), ("seconds", "\u0001")).Split('\u0001')[0];

    private int Finished() => Lines().Count(l => l.StartsWith(FinishedPrefix, StringComparison.Ordinal));

    private void Pick(string target)
    {
        _dialogs.Answers.Enqueue(0);
        int before = Finished();
        _w.ShowMakeTargets();
        _w.SetPaletteQuery(target);
        Assert.Equal(target, Assert.Single(_w.QuickResults).Title);
        _w.RunPaletteSelection();
        ToolTestKit.WaitFor(() => Finished() > before && !_w.ToolRunningNow, $"make {target} did not end", rounds: 4000);
    }

    private (string Folder, string Makefile) Project(string compiler)
    {
        string folder = Directory.CreateDirectory(Path.Combine(_temp.Path, "proj")).FullName;
        File.WriteAllText(Path.Combine(folder, "bad.c"), "int main(void) {\n  return undefined_thing;\n}\n");
        string makefile = Path.Combine(folder, "Makefile");
        File.WriteAllText(makefile, $"CC := {compiler}\n\n.PHONY: all good broken\n\nall: good\n\ngood:\n\t@echo made good\n\nbroken:\n\t$(CC) -c bad.c -o bad.o\n");
        _w.OpenFile(makefile, null);
        return (folder, makefile);
    }

    private void RunScenario(string? makeProgram)
    {
        string? compiler = new[] { "cc", "gcc", "clang" }.FirstOrDefault(c => ToolLocator.Find(c) is not null);
        Assert.SkipWhen(compiler is null, "No C compiler (cc, gcc, clang) is in PATH.");
        var (folder, makefile) = Project(compiler!);
        if (makeProgram is not null) _app.Workbench.Settings.SetLayer(NksHub.Textida.Workbench.Settings.SettingSource.User, new JsonObject { ["tools.makeProgram"] = makeProgram });
        _w.ShowMakeTargets();
        Assert.Equal(["all", "good", "broken"], _w.QuickResults.Select(r => r.Title));

        Pick("good");
        Assert.Contains("made good", Lines());
        Assert.StartsWith(ToolTestKit.Before(K.ToolOutFinished, ("code", 0)), Lines().Last(l => l.StartsWith(FinishedPrefix, StringComparison.Ordinal)));

        Pick("broken");
        Assert.DoesNotContain(ToolTestKit.Before(K.ToolOutFinished, ("code", 0)), Lines()[^1]); // make ended with an error code
        int error = OutputLineIndex("bad.c:2:");
        Assert.True(error >= 0, "the compiler error is not in the output: " + string.Join(" | ", Lines()));
        Assert.True(_w.OpenOutputLine(error));
        Assert.Equal(Path.Combine(folder, "bad.c"), _w.Active!.Path);
        var (line, column) = _w.Active.CaretPosition();
        Assert.Equal(2L, line);
        Assert.True(column > 1, "the column of the compiler is the place of the word");

        // make itself names the Makefile where its recipe failed (GNU make 4: "make: *** [Makefile:11: broken] Error 1")
        int make = OutputLineIndex("Makefile:");
        if (make >= 0)
        {
            Assert.True(_w.OpenOutputLine(make));
            Assert.Equal(makefile, _w.Active!.Path);
        }
    }

    [AvaloniaFact]
    public void A_target_picked_from_the_list_runs_with_the_make_of_the_PATH_and_the_compiler_error_opens_its_file()
    {
        Assert.SkipWhen(ToolLocator.Find(MakefileTargets.DefaultProgram) is null, "make is not installed here (not found in PATH).");
        RunScenario(null);
    }

    [AvaloniaFact]
    public void A_target_picked_from_the_list_runs_with_the_make_named_by_TEXTIDA_TEST_GNUMAKE()
    {
        string? make = Environment.GetEnvironmentVariable("TEXTIDA_TEST_GNUMAKE");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(make) || !File.Exists(make), "TEXTIDA_TEST_GNUMAKE does not name a make program.");
        RunScenario(make);
    }
}
