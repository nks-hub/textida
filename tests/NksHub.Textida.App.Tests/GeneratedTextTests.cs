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

    private static async Task Until(Func<bool> done)
    {
        for (var clock = System.Diagnostics.Stopwatch.StartNew(); !done() && clock.ElapsedMilliseconds < 5000;)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
