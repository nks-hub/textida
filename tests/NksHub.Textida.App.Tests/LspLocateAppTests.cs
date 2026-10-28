using Avalonia.Headless.XUnit;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P8.5.3 in the window: where the editor finds the program of a language server before it tells the plugin. A fake program (an empty file with the right name) stands in for the server; nothing is started here,
/// the plugin process and the protocol are tested in Rpc.Tests. A program of the project is run only after a yes, a path of the user wins, and a wrong path starts nothing.
/// </summary>
[Collection("LocState")]
public class LspLocateAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly string _project;
    private readonly string _local;

    public LspLocateAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _project = Directory.CreateDirectory(Path.Combine(_temp.Path, "proj")).FullName;
        File.WriteAllText(Path.Combine(_project, "package.json"), "{}");
        Directory.CreateDirectory(Path.Combine(_project, "src"));
        File.WriteAllText(Path.Combine(_project, "src", "a.ts"), "let a = 1;\n");
        _local = Fake(Path.Combine(_project, "node_modules", ".bin"), "typescript-language-server");
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static string Fake(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, OperatingSystem.IsWindows() ? name + ".exe" : name);
        File.WriteAllText(path, "not a program");
        return path;
    }

    private string ActionsFile => Path.Combine(_app.ProfileDirectory, SaveActionConfig.FileName);

    private DocTab Open() => _w.OpenFile(Path.Combine(_project, "src", "a.ts"), null);

    /// <summary>The program is found through the extensions of PATHEXT, which Windows writes in capitals (<c>.EXE</c>), and a path there is the same file in either case.</summary>
    private static StringComparison PathCase => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    [AvaloniaFact]
    public async Task A_server_a_folder_of_the_project_brings_is_run_only_after_a_yes_that_is_asked_with_its_command_and_kept_until_the_file_changes()
    {
        var tab = Open();

        _dialogs.Answers.Enqueue(1); // cancel
        var declined = await _w.LocateLspProgramAsync(tab, "typescript");
        Assert.True(declined!.Declined);
        var asked = Assert.Single(_dialogs.Asked);
        Assert.Contains(_local, asked.Details![0], PathCase);
        Assert.Contains("--stdio", asked.Details[0]);
        Assert.Contains("typescript-language-server", asked.Message);

        _dialogs.Answers.Enqueue(0); // run
        var yes = await _w.LocateLspProgramAsync(tab, "typescript");
        Assert.False(yes!.Declined);
        Assert.Equal("typescript-language-server", yes.Name);
        Assert.Equal(_local, yes.Path, ignoreCase: OperatingSystem.IsWindows());

        var again = await _w.LocateLspProgramAsync(tab, "typescript"); // asked once: no answer is queued, so a question would throw
        Assert.Equal(_local, again!.Path, ignoreCase: OperatingSystem.IsWindows());
        Assert.Equal(2, _dialogs.Asked.Count);

        File.WriteAllText(_local, "a different program"); // the file changed: the answer is not for it
        _dialogs.Answers.Enqueue(1);
        Assert.True((await _w.LocateLspProgramAsync(tab, "typescript"))!.Declined);
        Assert.Equal(3, _dialogs.Asked.Count);
    }

    [AvaloniaFact]
    public async Task A_path_of_the_user_wins_and_is_not_asked_about_and_a_path_that_is_not_there_starts_nothing()
    {
        string own = Fake(Path.Combine(_temp.Path, "mine"), "typescript-language-server");
        SaveActionConfig.Write(ActionsFile, new SaveActionsFile { ToolPaths = new Dictionary<string, string> { ["lsp:typescript"] = own } });
        var tab = Open();

        var found = await _w.LocateLspProgramAsync(tab, "typescript");
        Assert.Equal((own, false), (found!.Path, found.Declined));
        Assert.Empty(_dialogs.Asked); // the user's own program needs no question

        SaveActionConfig.Write(ActionsFile, new SaveActionsFile { ToolPaths = new Dictionary<string, string> { ["lsp:typescript"] = Path.Combine(_temp.Path, "nowhere") } });
        var gone = await _w.LocateLspProgramAsync(tab, "typescript");
        Assert.True(gone!.Declined);
        Assert.Equal(Loc.Get(K.LspToolPathMissing, ("language", "typescript"), ("path", Path.Combine(_temp.Path, "nowhere"))), _w.StatusMessageText);
        Assert.Empty(_dialogs.Asked);
    }

    [AvaloniaFact]
    public async Task A_folder_of_the_user_is_searched_for_the_program_and_a_language_without_a_server_gets_nothing()
    {
        string folder = Path.Combine(_temp.Path, "tools");
        string program = Fake(folder, "typescript-language-server");
        SaveActionConfig.Write(ActionsFile, new SaveActionsFile { ToolPaths = new Dictionary<string, string> { ["lsp:typescript"] = folder } });
        var tab = Open();

        Assert.Equal(program, (await _w.LocateLspProgramAsync(tab, "typescript"))!.Path, ignoreCase: OperatingSystem.IsWindows());
        Assert.Null(await _w.LocateLspProgramAsync(tab, "markdown"));
        Assert.Null(await _w.LocateLspProgramAsync(tab, "r")); // the table has a mode for it and no server
    }

    [AvaloniaFact]
    public async Task A_program_of_the_second_entry_is_found_when_the_first_is_not_there()
    {
        Assert.SkipWhen(ToolLocator.Find("Microsoft.CodeAnalysis.LanguageServer") is not null || ToolLocator.Find("roslyn-language-server") is not null, "A Roslyn server is installed in PATH here, so csharp-ls is not the first one found.");
        string only = Fake(Path.Combine(_temp.Path, "bin"), "csharp-ls"); // Roslyn is not installed, csharp-ls is
        SaveActionConfig.Write(ActionsFile, new SaveActionsFile { ToolPaths = new Dictionary<string, string> { ["lsp:csharp"] = Path.GetDirectoryName(only)! } });
        var tab = _w.OpenFile(_temp.File("Program.cs", "class C {}\n"), null);

        var found = await _w.LocateLspProgramAsync(tab, "csharp");

        Assert.Equal("csharp-ls", found!.Name);
        Assert.Equal(only, found.Path, ignoreCase: OperatingSystem.IsWindows());
    }
}
