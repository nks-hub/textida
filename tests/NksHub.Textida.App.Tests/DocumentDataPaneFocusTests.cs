using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class DocumentDataPaneFocusTests : IDisposable
{
    private readonly TempDir temporary = new();
    private readonly App app;
    private readonly MainWindow window;

    public DocumentDataPaneFocusTests()
    {
        app = TestApp.Prepare(temporary, new ScriptedDialogs());
        Loc.SetLanguage("en");
        window = app.CreateWindow(null);
        window.Width = 1400; window.Height = 900;
        window.Show(); window.UpdateLayout();
    }

    public void Dispose()
    {
        TestApp.Cleanup(app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        temporary.Dispose();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Split_F6_visits_source_then_tree_and_keeps_the_surrounding_pane_order(bool paged)
    {
        var (tab, view) = await Open(ViewMode.StructureSplit, paged);
        window.ToggleDocuments(true);
        window.MovePanel(SidePanel.Documents, PanelDock.Left);
        window.ToggleFiles(true);
        window.MovePanel(SidePanel.Files, PanelDock.Right);
        window.ToggleOutput(true);
        window.FindBar.Open(files: false, replace: false);
        await Drain(view);
        Assert.True(tab.Surface is { } surface ? surface.TextArea.Focus() : tab.View.Focus());
        await Drain(view);
        SourceHasFocus(tab);

        foreach (string expected in new[] { "tree", "files", "output", "find", "status", "documents", "source" })
        {
            await Press(view, forward: true);
            Assert.Equal(expected, FocusPart(tab, view));
            if (expected == "tree") Assert.True(tab.OverlayShown);
            if (expected == "source") SourceHasFocus(tab);
        }
        foreach (string expected in new[] { "documents", "status", "find", "output", "files", "tree", "source" })
        {
            await Press(view, forward: false);
            Assert.Equal(expected, FocusPart(tab, view));
            if (expected == "tree") Assert.True(tab.OverlayShown);
            if (expected == "source") SourceHasFocus(tab);
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Split_F6_from_the_menu_starts_at_the_source_in_both_directions(bool forward)
    {
        var (tab, view) = await Open(ViewMode.StructureSplit);
        window.ToggleDocuments(true);
        await Drain(view);
        var menu = Assert.Single(window.GetVisualDescendants().OfType<Menu>());
        var item = menu.Items.OfType<MenuItem>().First();
        Assert.True(item.Focus());
        Assert.True(menu.IsKeyboardFocusWithin);

        await Press(view, forward);

        SourceHasFocus(tab);
        Assert.False(view.IsKeyboardFocusWithin);
        Assert.False(window.DocumentFilter.IsKeyboardFocusWithin);
    }

    [AvaloniaTheory]
    [InlineData("Tree")]
    [InlineData("Table")]
    [InlineData("Graph")]
    public async Task A_single_data_view_stays_one_stop_between_the_other_panes(string modeName)
    {
        var mode = Enum.Parse<ViewMode>(modeName);
        var (tab, view) = await Open(mode);
        window.ToggleOutput(true);
        await Drain(view);
        Assert.True(view.FocusContent());
        await Drain(view);
        Assert.True(view.IsKeyboardFocusWithin);

        await Press(view, forward: true);
        Assert.True(window.OutputPanelControl.IsKeyboardFocusWithin);
        await Press(view, forward: false);

        Assert.True(view.IsKeyboardFocusWithin);
        Assert.False(tab.View.IsVisible);
        Assert.True(tab.OverlayShown);
        Assert.True(mode switch
        {
            ViewMode.Tree => view.Tree.IsKeyboardFocusWithin,
            ViewMode.Table => view.Table.IsKeyboardFocusWithin,
            ViewMode.Graph => view.Graph.IsKeyboardFocusWithin,
            _ => false,
        });
    }

    private async Task<(DocTab Tab, StructureView View)> Open(ViewMode mode, bool paged = false)
    {
        if (paged) app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        string text = paged ? "{\"alpha\":1,\"padding\":\"" + new string('x', 1 << 20) + "\"}" : "{\"alpha\":1,\"rows\":[0,1,2]}";
        var tab = window.OpenFile(temporary.File("sample.json", text), null);
        Assert.Equal(paged, tab.Doc.IsLarge);
        Assert.True(window.SetViewMode(tab.Doc, mode));
        var view = Assert.IsType<StructureView>(tab.Structure);
        await Drain(view);
        return (tab, view);
    }

    private string FocusPart(DocTab tab, StructureView view) =>
        tab.View.IsKeyboardFocusWithin ? "source" :
        view.Tree.IsKeyboardFocusWithin ? "tree" :
        window.FilePanelControl.IsKeyboardFocusWithin ? "files" :
        window.OutputPanelControl.IsKeyboardFocusWithin ? "output" :
        window.FindBar.IsKeyboardFocusWithin ? "find" :
        window.IndentItem.IsKeyboardFocusWithin ? "status" :
        window.DocumentFilter.IsKeyboardFocusWithin ? "documents" : "elsewhere";

    private void SourceHasFocus(DocTab tab)
    {
        Assert.True(tab.View.IsKeyboardFocusWithin);
        Assert.Same(tab.Surface is { } surface ? (object)surface.TextArea : tab.View, window.FocusManager!.GetFocusedElement());
        Assert.False(tab.OverlayShown);
    }

    private async Task Press(StructureView view, bool forward)
    {
        TestApp.Press(window, Key.F6, forward ? RawInputModifiers.None : RawInputModifiers.Shift);
        await Drain(view);
    }

    private Task Drain(StructureView view) => StructureLayoutTests.Drain(view, window);
}
