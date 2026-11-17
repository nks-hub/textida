using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class DocumentDataTablePagingTests : IDisposable
{
    private readonly TempDir temporary = new();
    private readonly App app;
    private readonly MainWindow window;

    public DocumentDataTablePagingTests()
    {
        app = TestApp.Prepare(temporary, new ScriptedDialogs());
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

    [AvaloniaFact]
    public async Task Table_keyboard_paging_keeps_the_page_after_the_host_updates_the_source_caret()
    {
        string text = "{\"alpha\":1,\"rows\":[" + string.Join(',', Enumerable.Range(0, 160)) + "]}";
        string path = temporary.File("sample.json", text);
        var tab = window.OpenFile(path, null);
        window.SelectTab(tab);
        var document = tab.Doc.Doc;
        long revision = document.Revision;
        Assert.True(window.SetViewMode(tab.Doc, ViewMode.Tree));
        var view = Assert.IsType<StructureView>(tab.Structure);
        await Drain(view);
        var root = Assert.Single(view.Tree.VisibleNodes, item => item.Tag is StructureNode { Kind: StructureKind.JsonObject });
        root.IsExpanded = true;
        await Drain(view);
        var rows = Assert.Single(root.Items, item => item.Tag is StructureNode { Kind: StructureKind.JsonArray });
        Assert.Equal("rows", rows.DisplayKey);
        view.Tree.SelectedNode = rows;
        await Drain(view);
        Assert.StartsWith("\"rows\":[", tab.Surface!.SelectedText, StringComparison.Ordinal);

        Assert.True(window.SetViewMode(tab.Doc, ViewMode.Table));
        await Drain(view);
        Assert.True(view.FocusContent());
        Assert.Same(view.Table, window.FocusManager!.GetFocusedElement());
        Assert.False(view.Tree.IsVisible);
        Assert.False(tab.View.IsVisible);
        await Press(view, Key.Home);
        await CopySelected(view, tab, 0, "0", "initial Home");

        await Press(view, Key.PageDown);
        FirstCell(view, 1, "64", "after PageDown, before selecting a cell");
        await Press(view, Key.Home);
        await CopySelected(view, tab, 1, "64", "Home on the second page");

        await Press(view, Key.PageUp);
        FirstCell(view, 0, "0", "after PageUp, before selecting a cell");
        await Press(view, Key.Home);
        await CopySelected(view, tab, 0, "0", "Home after returning to the first page");
        Assert.Equal(ViewMode.Table, tab.Doc.Mode);
        Assert.Same(document, tab.Doc.Doc);
        Assert.Equal(revision, document.Revision);
        Assert.False(document.IsModified);
        Assert.False(document.CanUndo);
        Assert.Equal(text, tab.Surface.Document.Text);
        Assert.Equal(text, File.ReadAllText(path));
    }

    private Task Drain(StructureView view) => StructureLayoutTests.Drain(view, window);

    private async Task Press(StructureView view, Key key)
    {
        Assert.Same(view.Table, window.FocusManager!.GetFocusedElement());
        TestApp.Press(window, key);
        await Drain(view);
        Assert.Same(view.Table, window.FocusManager.GetFocusedElement());
    }

    private static StructureTableCell FirstCell(StructureView view, int page, string expected, string stage)
    {
        var current = view.Table.CurrentPage;
        Assert.True(current.Page == page, $"{stage}: expected page {page}, actual page {current.Page}.");
        Assert.NotEmpty(current.Rows);
        var cell = Assert.Single(current.Rows[0].Cells).Value;
        Assert.Equal(expected, cell.Value);
        return cell;
    }

    private async Task CopySelected(StructureView view, DocTab tab, int page, string expected, string stage)
    {
        var cell = FirstCell(view, page, expected, stage);
        Assert.Equal(expected, tab.Surface!.SelectedText);
        Assert.Equal(cell.Node.ValueRange, view.SelectedRange);
        Assert.Equal(cell.Node.ValueRange.Start, view.SourceOffset);
        Assert.True(view.CopySourceButton.IsEnabled);
        var clipboard = Assert.IsAssignableFrom<IClipboard>(window.Clipboard);
        await clipboard.SetTextAsync("before table copy");
        view.CopySourceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Drain(view);
        Assert.Equal(expected, await clipboard.TryGetTextAsync());
        FirstCell(view, page, expected, stage + " after source copy");
    }
}
