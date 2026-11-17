using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class DocumentDataLifecycleTests : IDisposable
{
    private readonly TempDir temporary = new();
    private readonly ScriptedDialogs dialogs = new();
    private readonly App app;
    private readonly MainWindow window;

    public DocumentDataLifecycleTests()
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
    [InlineData("json", "{\"value\":\"old\"}", "{\"value\":\"fresh\"}", "$.value", "Tree")]
    [InlineData("xml", "<root><value>old</value></root>", "<root><value>fresh</value></root>", "/root/value", "Graph")]
    [InlineData("yaml", "value: old\n", "value: fresh\n", "$.value", "Table")]
    [InlineData("neon", "value: old\n", "value: fresh\n", "$.value", "StructureSplit")]
    public async Task Reload_replaces_the_source_and_visible_analysis_in_the_current_data_mode(
        string extension, string before, string after, string query, string modeName)
    {
        string path = temporary.Bytes("sample." + extension, TextCodec.Utf8.Encode(before));
        var tab = window.OpenFile(path, null);
        var document = tab.Doc;
        var previousSource = document.Doc;
        var mode = Enum.Parse<ViewMode>(modeName);
        Assert.True(window.SetViewMode(document, mode));
        var view = Assert.IsType<StructureView>(tab.Structure);
        await Value(view, query, "old");
        Task previousParsing = view.Parsing;

        byte[] freshBytes = TextCodec.Utf8.Encode(after);
        File.WriteAllBytes(path, freshBytes);
        Assert.True(await window.ReloadAsync(tab));
        var currentTab = Assert.Single(document.Views);
        var currentView = Assert.IsType<StructureView>(currentTab.Structure);
        await Drain(currentView);

        Assert.Same(document, currentTab.Doc);
        Assert.NotSame(previousSource, document.Doc);
        Assert.Same(document.Doc, currentTab.StructureDocument);
        Assert.NotSame(previousParsing, currentView.Parsing);
        Assert.Equal(mode, document.Mode);
        Assert.True(currentTab.StructureShown);
        Assert.Equal(mode == ViewMode.StructureSplit, currentTab.View.IsVisible);
        Assert.Equal(after, currentTab.Surface!.Document.Text);
        Assert.Equal(freshBytes, SourceBytes(currentTab));
        Assert.False(document.Doc.IsModified);
        Assert.False(document.Doc.CanUndo);
        await Value(currentView, query, "fresh");
        Assert.Empty(dialogs.Asked);
    }

    [AvaloniaTheory]
    [InlineData("json", "{\"value\":\"café\"}", "Graph")]
    [InlineData("yaml", "value: café\n", "Tree")]
    [InlineData("neon", "value: café\n", "Table")]
    public async Task Rereading_the_same_bytes_invalidates_data_analysis_without_a_content_edit(
        string extension, string text, string modeName)
    {
        byte[] bytes = TextCodec.Utf8.Encode(text);
        string path = temporary.Bytes("sample." + extension, bytes);
        var tab = window.OpenFile(path, null);
        var document = tab.Doc.Doc;
        Assert.Same(TextCodec.Utf8, document.Codec);
        Assert.True(window.SetViewMode(tab.Doc, Enum.Parse<ViewMode>(modeName)));
        var view = Assert.IsType<StructureView>(tab.Structure);
        await Value(view, "$.value", "café");
        Task initialParsing = view.Parsing;
        long revision = document.Revision;
        int undoDepth = document.UndoDepth;
        var fingerprint = document.Fingerprint;

        window.RereadAs(tab, TextCodec.Cp1252);
        await Drain(view);
        Assert.NotSame(initialParsing, view.Parsing);
        await Value(view, "$.value", "cafÃ©");
        Assert.Same(document, tab.Doc.Doc);
        Assert.Same(TextCodec.Cp1252, document.Codec);
        Assert.Equal(revision + 1, document.Revision);
        Assert.Equal(fingerprint, document.Fingerprint);
        Assert.Equal(bytes, SourceBytes(tab));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.False(document.IsModified);
        Assert.Equal(undoDepth, document.UndoDepth);
        Assert.False(document.CanUndo);

        Task alternateParsing = view.Parsing;
        window.RereadAs(tab, TextCodec.Utf8);
        await Drain(view);
        Assert.NotSame(alternateParsing, view.Parsing);
        await Value(view, "$.value", "café");
        Assert.Equal(text, tab.Surface!.Document.Text);
        Assert.Same(TextCodec.Utf8, document.Codec);
        Assert.Equal(revision + 2, document.Revision);
        Assert.Equal(fingerprint, document.Fingerprint);
        Assert.Equal(bytes, SourceBytes(tab));
        Assert.False(document.IsModified);
        Assert.Equal(undoDepth, document.UndoDepth);
        Assert.False(document.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData("json", "{\"value\":\"old\",\"keep\":3}")]
    [InlineData("yaml", "value: old\nkeep: 3\n")]
    [InlineData("neon", "value: old\nkeep: 3\n")]
    public async Task Two_views_keep_the_same_edit_and_undo_across_data_mode_switches(string extension, string before)
    {
        byte[] originalBytes = TextCodec.Utf8.Encode(before);
        string path = temporary.Bytes("sample." + extension, originalBytes);
        var first = window.OpenFile(path, null);
        Assert.True(window.SetViewMode(first.Doc, ViewMode.Tree));
        var firstView = Assert.IsType<StructureView>(first.Structure);
        await Value(firstView, "$.value", "old");
        var second = Assert.IsType<DocTab>(window.SplitView());
        var secondView = Assert.IsType<StructureView>(second.Structure);
        await Value(secondView, "$.value", "old");
        var document = first.Doc.Doc;
        Assert.Same(first.Doc, second.Doc);
        Assert.Same(document, second.Doc.Doc);
        Assert.NotSame(firstView, secondView);

        Assert.True(window.SetViewMode(first.Doc, ViewMode.StructureSplit));
        Assert.True(first.View.IsVisible);
        Assert.True(second.View.IsVisible);
        Assert.True(first.StructureShown);
        Assert.True(second.StructureShown);
        Assert.Equal(before, first.Surface!.Document.Text);
        Assert.Equal(before, second.Surface!.Document.Text);
        Assert.True(window.SetViewMode(first.Doc, ViewMode.Tree));
        window.SelectTab(first);
        var target = await Value(firstView, "$.value", "old");
        firstView.Tree.SelectedNode = target;
        await Drain(firstView);
        Assert.True(firstView.EditButton.IsEnabled);
        int undoDepth = document.UndoDepth;
        dialogs.Prompts.Enqueue("updatedé");
        Click(firstView.EditButton);
        await Drain(firstView);
        await Drain(secondView);

        Assert.Equal(undoDepth + 1, document.UndoDepth);
        Assert.True(document.IsModified);
        string edited = first.Surface.Document.Text;
        Assert.Equal(edited, second.Surface.Document.Text);
        Assert.DoesNotContain("old", edited, StringComparison.Ordinal);
        await Value(firstView, "$.value", "updatedé");
        await Value(secondView, "$.value", "updatedé");
        Assert.True(window.SetViewMode(first.Doc, ViewMode.Table));
        await Value(firstView, "$.value", "updatedé");
        await Value(secondView, "$.value", "updatedé");
        Assert.False(first.View.IsVisible);
        Assert.False(second.View.IsVisible);

        window.SelectTab(second);
        Assert.NotNull(document.Undo());
        await Drain(firstView);
        await Drain(secondView);
        Assert.Equal(before, first.Surface.Document.Text);
        Assert.Equal(before, second.Surface.Document.Text);
        Assert.Equal(originalBytes, SourceBytes(first));
        Assert.Equal(originalBytes, SourceBytes(second));
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.Same(document, first.StructureDocument);
        Assert.Same(document, second.StructureDocument);
        await Value(firstView, "$.value", "old");
        await Value(secondView, "$.value", "old");
        Assert.Empty(dialogs.Prompts);
    }

    private Task Drain(StructureView view) => StructureLayoutTests.Drain(view, window);

    private async Task<StructureTreeNode> Value(StructureView view, string query, string expected)
    {
        await Drain(view);
        Assert.False(view.IssueButton.IsVisible);
        Assert.True(view.FindButton.IsEnabled);
        view.SearchScope.SelectedIndex = 3;
        view.Filter.Text = query;
        Click(view.FindButton);
        await Drain(view);
        var result = Assert.Single(view.Tree.Items.OfType<StructureTreeNode>(), item => item.Tag is StructureNode);
        Assert.Contains(expected, result.Value, StringComparison.Ordinal);
        if (view.Presentation == StructureViewPresentation.Table)
        {
            var row = Assert.Single(view.Table.CurrentPage.Rows);
            Assert.Contains(row.Cells.Values, cell => cell.Value.Contains(expected, StringComparison.Ordinal));
        }
        return result;
    }

    private static byte[] SourceBytes(DocTab tab) => tab.Doc.Doc.ReadBytes(new ByteRange(0, tab.Doc.Doc.ByteLength));

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
