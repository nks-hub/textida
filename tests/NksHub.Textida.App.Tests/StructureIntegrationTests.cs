using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class StructureIntegrationTests : IDisposable
{
    private readonly TempDir temporary = new();
    private readonly ScriptedDialogs dialogs = new();
    private readonly App app;
    private readonly MainWindow window;

    public StructureIntegrationTests()
    {
        app = TestApp.Prepare(temporary, dialogs);
        Loc.SetLanguage("en");
        window = app.CreateWindow(null);
        window.Width = 1200; window.Height = 800; window.Show(); window.UpdateLayout();
    }

    public void Dispose()
    {
        TestApp.Cleanup(app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        temporary.Dispose();
    }

    private async Task Drain()
    {
        var view = window.StructurePanelView;
        for (int attempt = 0; attempt < 1000; attempt++)
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Task work = view.Working;
            if (work.IsCompleted)
            {
                await work;
                Dispatcher.UIThread.RunJobs();
                if (ReferenceEquals(work, view.Working)) return;
            }
            await Task.Delay(10);
        }
        Assert.Fail("The integrated structure panel did not finish.");
    }

    private static IEnumerable<StructureTreeNode> Rows(StructureTreeNode parent) => parent.Items.Where(item => item.Tag is StructureNode);
    private static IEnumerable<StructureTreeNode> Rows(StructureTree parent) => parent.Items.OfType<StructureTreeNode>().Where(item => item.Tag is StructureNode);

    [AvaloniaFact]
    public async Task Builtin_command_opens_the_document_tree_and_refuses_non_data_views()
    {
        var tab = window.OpenFile(temporary.File("sample.json", "{\"value\":1}"), null);
        window.SelectTab(tab);
        Assert.True(app.Workbench.Commands.TryGet("nks.view.structure", out _));
        Assert.True(window.CanShowStructure);
        Assert.Equal(CommandOutcome.Handled, window.ShowStructurePanel());
        await Drain();
        Assert.Equal(ViewMode.Tree, tab.Doc.Mode);
        Assert.True(tab.StructureShown);
        Assert.False(window.IsPanelOpen(SidePanel.Structure));
        Assert.False(tab.View.IsVisible);
        var analysis = tab.Structure;
        Assert.True(analysis!.Bounds.Width > window.ClientSize.Width * 0.65);

        var other = window.OpenFile(temporary.File("notes.txt", "ordinary text"), null);
        window.SelectTab(other); await Drain();
        Assert.False(window.CanShowStructure);
        Assert.Equal(CommandOutcome.NotHandled, window.ShowStructurePanel());
        Assert.Empty(window.StructurePanelView.Tree.Items);
        window.SelectTab(tab); await Drain();
        Assert.Same(analysis, window.StructurePanelView);
        Assert.Equal(ViewMode.Tree, tab.Doc.Mode);
    }

    [AvaloniaFact]
    public async Task Utf16_source_and_tree_select_the_same_attribute_and_cdata_without_moving_focus()
    {
        const string text = "<r xmlns:p='urn:sample' p:name='é😀'><![CDATA[bodyé😀]]></r>";
        string path = temporary.File("sample.xml", "");
        File.WriteAllBytes(path, [.. TextCodec.Utf16LEBom.Bom, .. TextCodec.Utf16LEBom.Encode(text)]);
        var tab = window.OpenFile(path, null);
        window.SelectTab(tab); window.ShowStructurePanel(); await Drain();
        var view = window.StructurePanelView;
        var root = Assert.Single(Rows(Assert.Single(Rows(view.Tree))));
        root.IsExpanded = true; await Drain();
        var attribute = Assert.Single(Rows(root).Where(item => ((StructureNode)item.Tag!).Kind == StructureKind.XmlAttribute));
        view.Tree.SelectedNode = attribute; await Drain();
        Assert.Equal("p:name='é😀'", tab.Surface!.SelectedText);

        view.Tree.SelectedNode = Assert.Single(Rows(view.Tree)); await Drain();
        Assert.Equal(text, tab.Surface.SelectedText);

        Assert.True(window.SetViewMode(tab.Doc, ViewMode.StructureSplit));
        await Drain();
        Assert.True(tab.Surface.TextArea.Focus());
        tab.Surface.Select(text.IndexOf("body", StringComparison.Ordinal) + 2, 0);
        await Drain();
        Assert.Equal(StructureKind.XmlCData, Assert.IsType<StructureNode>(Assert.IsType<StructureTreeNode>(view.Tree.SelectedNode).Tag).Kind);
        Assert.True(tab.Surface.IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public async Task Window_value_edit_uses_the_shared_document_and_one_undo_and_invalidates_old_rows()
    {
        const string before = "{\"value\":\"old\",\"keep\":3}";
        var tab = window.OpenFile(temporary.File("sample.json", before), null);
        window.SelectTab(tab); window.ShowStructurePanel(); await Drain();
        var view = window.StructurePanelView;
        var root = Assert.Single(Rows(Assert.Single(Rows(view.Tree))));
        root.IsExpanded = true; await Drain();
        var value = Rows(root).First(); view.Tree.SelectedNode = value;
        Assert.Same(window, TopLevel.GetTopLevel(view.Tree.TryGetRealizedRow(value)));
        dialogs.Prompts.Enqueue("é😀\nnew");
        int depth = tab.Doc.Doc.UndoDepth;
        view.EditButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Drain();
        Assert.Equal(depth + 1, tab.Doc.Doc.UndoDepth);
        string after = tab.Doc.Doc.ReadText(new ByteRange(0, tab.Doc.Doc.ByteLength));
        using var decoded = JsonDocument.Parse(after);
        Assert.Equal("é😀\nnew", decoded.RootElement.GetProperty("value").GetString());
        Assert.StartsWith("{\"value\":", after, StringComparison.Ordinal);
        Assert.EndsWith(",\"keep\":3}", after, StringComparison.Ordinal);
        Assert.Null(TopLevel.GetTopLevel(view.Tree.TryGetRealizedRow(value)));
        tab.Doc.Doc.Undo(); await Drain();
        Assert.Equal(before, tab.Doc.Doc.ReadText(new ByteRange(0, tab.Doc.Doc.ByteLength)));
    }

    [AvaloniaFact]
    public async Task Utf16_issue_jump_reaches_its_start_even_when_the_issue_end_is_inside_a_character()
    {
        const string text = "<!DOCTYPE r><r/>";
        string path = temporary.File("invalid.xml", "");
        File.WriteAllBytes(path, [.. TextCodec.Utf16LEBom.Bom, .. TextCodec.Utf16LEBom.Encode(text)]);
        var tab = window.OpenFile(path, null);
        window.SelectTab(tab); tab.Surface!.CaretOffset = text.Length;
        window.ShowStructurePanel(); await Drain();
        window.StructurePanelView.IssueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(0, tab.Surface.CaretOffset);
        Assert.Equal(text, tab.Doc.Doc.ReadText(new ByteRange(tab.Doc.Doc.BomLength, tab.Doc.Doc.ByteLength)));
    }

    [AvaloniaTheory]
    [InlineData("sample.jsonc")]
    [InlineData("tsconfig.json")]
    public async Task Jsonc_file_routing_accepts_comments_and_trailing_commas_and_query_selection_jumps_to_source(string name)
    {
        const string text = "/* source */\n{\"value\":7,\"label\":\"sample\",}\n";
        var tab = window.OpenFile(temporary.File(name, text), null);
        window.SelectTab(tab); window.ShowStructurePanel(); await Drain();
        Assert.Equal("json", tab.Doc.Language.Id);
        var view = window.StructurePanelView;
        Assert.False(view.IssueButton.IsVisible);
        Assert.True(view.FindButton.IsEnabled);
        view.SearchScope.SelectedIndex = 3;
        view.Filter.Text = "$.value";
        view.FindButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Drain();
        var result = Assert.Single(Rows(view.Tree));
        var node = Assert.IsType<StructureNode>(result.Tag);
        Assert.Equal(StructureKind.JsonNumber, node.Kind);
        Assert.Equal("7", tab.Doc.Doc.ReadText(node.ValueRange));
        view.Tree.SelectedNode = result; await Drain();
        Assert.Equal("\"value\":7", tab.Surface!.SelectedText);
        Assert.Equal(text, tab.Doc.Doc.ReadText(new ByteRange(tab.Doc.Doc.BomLength, tab.Doc.Doc.ByteLength)));
        Assert.False(tab.Doc.Doc.CanUndo);
    }

    [AvaloniaFact]
    public async Task A_regular_json_file_still_rejects_the_same_comments_and_trailing_commas()
    {
        const string text = "/* source */\n{\"value\":7,\"label\":\"sample\",}\n";
        var tab = window.OpenFile(temporary.File("sample.json", text), null);
        window.SelectTab(tab); window.ShowStructurePanel(); await Drain();
        var view = window.StructurePanelView;
        Assert.Equal("json", tab.Doc.Language.Id);
        Assert.True(view.IssueButton.IsVisible);
        Assert.False(view.FindButton.IsEnabled);
        Assert.Contains("Structure is incomplete", view.Message, StringComparison.Ordinal);
        Assert.Equal(text, tab.Doc.Doc.ReadText(new ByteRange(tab.Doc.Doc.BomLength, tab.Doc.Doc.ByteLength)));
        Assert.False(tab.Doc.Doc.CanUndo);
    }

    [AvaloniaFact]
    public async Task An_untitled_document_in_manual_json_mode_remains_strict_and_queries_valid_json()
    {
        var tab = window.NewDocument();
        window.SelectTab(tab);
        window.SetLanguage(tab, LanguageModes.All.Single(mode => mode.Id == "json"));
        var document = tab.Doc.Doc;
        Assert.Null(tab.Path);
        Assert.True(document.Insert(document.ByteLength, document.Codec.Encode("/* source */{\"value\":7,}")).Applied);
        window.ShowStructurePanel(); await Drain();
        var view = window.StructurePanelView;
        Assert.True(view.IssueButton.IsVisible);
        Assert.False(view.FindButton.IsEnabled);
        Assert.True(document.Replace(new ByteRange(document.BomLength, document.ByteLength), document.Codec.Encode("{\"value\":7}")).Applied);
        await Drain();
        Assert.False(view.IssueButton.IsVisible);
        view.SearchScope.SelectedIndex = 3;
        view.Filter.Text = "$.value";
        view.FindButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Drain();
        var value = Assert.Single(Rows(view.Tree));
        view.Tree.SelectedNode = value; await Drain();
        Assert.Equal("\"value\":7", tab.Surface!.SelectedText);
    }
}
