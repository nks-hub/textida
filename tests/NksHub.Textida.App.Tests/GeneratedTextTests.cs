using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Data;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.10.1 and PZ.3.3: the commands that make text (types from JSON, Lorem ipsum) and the one that tells what a character is.</summary>
[Collection("LocState")]
public class GeneratedTextTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;

    public GeneratedTextTests() => _app = TestApp.Prepare(_temp, _dialogs);

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        _temp.Dispose();
    }

    [AvaloniaFact]
    public void JSON_becomes_types_in_a_new_tab_from_the_whole_document_or_from_the_selection()
    {
        var w = _app.CreateWindow(null);
        var source = w.OpenFile(_temp.File("order.json", "{\"id\": 1, \"lines\": [{\"sku\": \"a\"}]}\nnot json after\n"), null);
        var surface = source.Editor!;
        surface.SetSelections([new Sel(0, "{\"id\": 1, \"lines\": [{\"sku\": \"a\"}]}".Length)]); // only the object: the rest of the file is not JSON

        w.JsonToTypes(TypeLanguage.Go);
        var tab = w.Active!;
        Assert.NotSame(source, tab);
        Assert.Contains("type Order struct {", tab.Editor!.Document.Text);
        Assert.Contains("Lines []Line `json:\"lines\"`", tab.Editor.Document.Text);
        Assert.Equal("go", tab.Doc.Language.Id);

        w.SelectTab(source); // the whole document is not JSON: nothing is made, the reason is said
        surface.SetSelections([Sel.At(0)]);
        int tabs = w.Tabs.Count();
        w.JsonToTypes(TypeLanguage.CSharp);
        Assert.Equal(tabs, w.Tabs.Count());
    }

    [AvaloniaFact]
    public async Task Lorem_ipsum_goes_in_at_every_caret_as_one_undo_step_and_a_bad_count_changes_nothing()
    {
        var w = _app.CreateWindow(null);
        var tab = w.OpenFile(_temp.File("lorem.txt", "a\nb\n"), null);
        var surface = tab.Editor!;
        surface.SetSelections([Sel.At(1), Sel.At(3)]);

        _dialogs.Prompts.Enqueue("4");
        w.InsertLorem(LoremUnit.Words);
        await Until(() => surface.Document.Text.Contains("lorem ipsum dolor sit"));
        Assert.Equal("alorem ipsum dolor sit\nblorem ipsum dolor sit\n", surface.Document.Text);
        tab.Doc.Doc.Undo();
        Assert.Equal("a\nb\n", surface.Document.Text); // one step for both carets

        _dialogs.Prompts.Enqueue("zero");
        w.InsertLorem(LoremUnit.Words);
        await Task.Delay(100);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("a\nb\n", surface.Document.Text);
    }

    [AvaloniaFact]
    public void The_character_inspector_shows_the_code_point_name_category_and_bytes_in_the_encoding_of_the_file()
    {
        var w = _app.CreateWindow(null);
        var tab = w.OpenFile(_temp.File("chars.txt", "café\n"), null);
        tab.Editor!.CaretOffset = 3;
        _dialogs.Answers.Enqueue(0);

        w.InspectCharacter();

        var asked = Assert.Single(_dialogs.Asked);
        Assert.Contains("U+00E9", asked.Message);
        Assert.Contains("LATIN SMALL LETTER E WITH ACUTE", asked.Message);
        Assert.Contains("Ll", asked.Message);
        Assert.Contains("C3 A9", asked.Message); // UTF-8
        Assert.Contains("00 E9", asked.Message); // UTF-16
    }

    [AvaloniaFact]
    public void Keywords_change_case_in_one_undo_step_in_a_language_that_allows_it_and_not_in_one_that_does_not()
    {
        var w = _app.CreateWindow(null);
        var sql = w.OpenFile(_temp.File("q.sql", "select a from t -- where x\nwhere 'from' = a\n"), null);
        w.ChangeKeywordCase(Core.Syntax.KeywordCaseMode.Upper);
        Assert.Equal("SELECT a FROM t -- where x\nWHERE 'from' = a\n", sql.Editor!.Document.Text);
        sql.Doc.Doc.Undo();
        Assert.Equal("select a from t -- where x\nwhere 'from' = a\n", sql.Editor.Document.Text);

        var cs = w.OpenFile(_temp.File("a.cs", "class A { void F() { if (x) return; } }\n"), null);
        w.ChangeKeywordCase(Core.Syntax.KeywordCaseMode.Upper);
        Assert.Equal("class A { void F() { if (x) return; } }\n", cs.Editor!.Document.Text);
    }

    [AvaloniaFact]
    public async Task The_line_at_the_caret_goes_to_a_python_that_stays_open_and_its_output_is_in_the_output_panel()
    {
        Assert.SkipWhen(Core.Tools.InterpreterKind.Python.Find(null) is null, "no python on this machine");
        var w = _app.CreateWindow(null);
        var tab = w.OpenFile(_temp.File("calc.py", "x = 6\nprint(x * 7)\nprint('again', x)\n"), null);
        var surface = tab.Editor!;
        try
        {
            surface.CaretOffset = 0;
            w.SendToInterpreter(); // x = 6
            surface.CaretOffset = "x = 6\n".Length;
            w.SendToInterpreter(); // print(x * 7): x is known from the line before, so it is the same session
            await Until(() => _app.Output.Read(NksHub.Textida.App.Services.OutputChannel.Tasks).Any(l => l.Text == "42"));
            var lines = _app.Output.Read(NksHub.Textida.App.Services.OutputChannel.Tasks);
            Assert.Contains(lines, l => l.Source == "interpreter:python" && l.Text == "> print(x * 7)");
            Assert.Contains(lines, l => l.Source == "interpreter:python" && l.Text == "42");
            Assert.DoesNotContain(lines, l => l.Text.Contains(">>>"));
        }
        finally { w.StopInterpreters(); }

        // a language without an interpreter is said, not guessed
        var cs = w.OpenFile(_temp.File("a.cs", "int x;\n"), null);
        cs.Editor!.CaretOffset = 0;
        w.SendToInterpreter();
        Assert.Contains("C#", w.StatusMessageText);
    }

    private static async Task Until(Func<bool> done)
    {
        for (var clock = System.Diagnostics.Stopwatch.StartNew(); !done() && clock.ElapsedMilliseconds < 5000;)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
