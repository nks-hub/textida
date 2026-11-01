using Avalonia.Headless.XUnit;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Data;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.10.1: the commands that make types from JSON put them in a new tab, from the selection or from the whole document.</summary>
[Collection("LocState")]
public class GeneratedTextTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;

    public GeneratedTextTests() => _app = TestApp.Prepare(_temp);

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
}
