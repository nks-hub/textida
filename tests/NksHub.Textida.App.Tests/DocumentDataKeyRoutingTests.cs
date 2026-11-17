using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Data.Structure;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class DocumentDataKeyRoutingTests : IDisposable
{
    private readonly TempDir temporary = new();
    private readonly ScriptedDialogs dialogs = new();
    private readonly App app;
    private readonly MainWindow window;

    public DocumentDataKeyRoutingTests()
    {
        app = TestApp.Prepare(temporary, dialogs);
        Loc.SetLanguage("en");
        window = app.CreateWindow(null);
        window.Width = 1200; window.Height = 800;
        window.Show(); window.UpdateLayout();
    }

    public void Dispose()
    {
        TestApp.Cleanup(app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        temporary.Dispose();
    }

    [AvaloniaTheory]
    [InlineData("sample.csv", "name,value\nDemo,1\n")]
    [InlineData("sample.tsv", "name\tvalue\nDemo\t1\n")]
    public void F2_edits_the_focused_delimited_cell_before_the_global_bookmark_binding(string name, string text)
    {
        var tab = window.OpenFile(temporary.File(name, text), null);
        Assert.True(window.SetViewMode(tab.Doc, ViewMode.Table));
        var table = Assert.IsType<CsvView>(tab.Table);
        while (!table.RowsComplete) table.Tick();
        table.MoveTo(0, 1);
        Assert.True(table.Focus());
        Assert.Same(table, window.FocusManager!.GetFocusedElement());
        Assert.False(table.IsEditing);
        TestApp.Press(window, Key.F2);
        Assert.True(table.IsEditing);
        Assert.Equal("1", table.EditText);
        Assert.False(tab.Doc.Doc.IsModified);
    }

    [AvaloniaTheory]
    [InlineData("Table")]
    [InlineData("Graph")]
    public async Task F2_edits_the_focused_structure_value_before_the_global_bookmark_binding(string modeName)
    {
        var mode = Enum.Parse<ViewMode>(modeName);
        var tab = window.OpenFile(temporary.File("sample.json", "{\"value\":1}"), null);
        Assert.True(window.SetViewMode(tab.Doc, ViewMode.Tree));
        var view = Assert.IsType<StructureView>(tab.Structure);
        await Drain(view);
        var root = Assert.Single(view.Tree.VisibleNodes.Where(node => node.HasChildren));
        root.IsExpanded = true; await Drain(view);
        view.Tree.SelectedNode = Assert.Single(view.Tree.VisibleNodes.Where(node => node.Tag is StructureNode { Kind: StructureKind.JsonNumber }));
        Assert.True(window.SetViewMode(tab.Doc, mode));
        await Drain(view);
        Assert.True(view.FocusContent());
        if (mode == ViewMode.Table) { TestApp.Press(window, Key.Home); await Drain(view); }
        dialogs.Prompts.Enqueue("2");
        TestApp.Press(window, Key.F2);
        for (int attempt = 0; attempt < 300 && tab.Doc.Doc.UndoDepth == 0; attempt++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        await Drain(view);
        Assert.Equal("{\"value\":2}", tab.Surface!.Document.Text);
        Assert.Equal(1, tab.Doc.Doc.UndoDepth);
    }

    private Task Drain(StructureView view) => StructureLayoutTests.Drain(view, window);
}
