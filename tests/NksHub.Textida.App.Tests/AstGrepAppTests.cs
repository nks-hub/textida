using Avalonia.Headless.XUnit;
using NksHub.Textida.AvaloniaEditAdapter;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.6.5: a rewrite becomes a proposal in the change preview; an open tab goes through standard input and is changed as a document.</summary>
[Collection("LocState")]
public class AstGrepAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly string? _path = Environment.GetEnvironmentVariable("PATH");

    public AstGrepAppTests() => _app = TestApp.Prepare(_temp, _dialogs);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _path);
        TestApp.Cleanup(_app);
        _temp.Dispose();
    }

    [AvaloniaFact]
    public async Task A_language_without_a_grammar_and_a_missing_program_say_so_and_ask_nothing_more()
    {
        var w = _app.CreateWindow(null);
        w.OpenFile(_temp.File("notes.txt", "x"), null);
        await w.StructuralSearchAsync();
        Assert.Contains("ast-grep", w.StatusMessageText);

        Environment.SetEnvironmentVariable("PATH", _temp.Path); // no ast-grep anywhere
        w.OpenFile(_temp.File("a.js", "console.log(1)"), null);
        _dialogs.Prompts.Enqueue("console.log($A)");
        _dialogs.Prompts.Enqueue("");
        await w.StructuralSearchAsync();
        Assert.Contains("ast-grep", w.StatusMessageText);
        Assert.Empty(_dialogs.Asked);
    }

    [AvaloniaFact]
    public async Task A_rewrite_is_proposed_for_the_files_and_for_the_unsaved_tab_and_applied_as_ticked()
    {
        if (Environment.GetEnvironmentVariable("TEXTIDA_TEST_AST_GREP") is not { } program) return; // ast-grep is not installed on this machine
        Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(program) + Path.PathSeparator + _path);
        File.WriteAllText(Path.Combine(_temp.Path, "disk.js"), "console.log('d');\n");
        var w = _app.CreateWindow(null);
        var tab = w.OpenFile(_temp.File("open.js", "console.log('o');\n"), null);
        var surface = tab.Editor!;
        surface.Document.Insert(surface.Document.TextLength, "// unsaved\nconsole.log('u');\n");

        _dialogs.Prompts.Enqueue("console.log($A)");
        _dialogs.Prompts.Enqueue("logger.info($A)");
        _dialogs.Answers.Enqueue(0); // approve the program
        await w.StructuralSearchAsync();

        Assert.NotNull(w.StructuralDecision);
        Assert.False(w.StructuralDecision!.IsCompleted);
        var report = w.ApplyPlan()!;

        Assert.Equal(2, report.Applied);
        Assert.Equal("logger.info('d');\n", File.ReadAllText(Path.Combine(_temp.Path, "disk.js")));
        Assert.Equal("logger.info('o');\n// unsaved\nlogger.info('u');\n", surface.Document.Text); // the unsaved text was searched, not the file
        Assert.Equal("console.log('o');\n", File.ReadAllText(tab.Path!)); // the tab is changed, not saved
    }
}
