using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class StructurePresentationTests
{
    [AvaloniaFact]
    public async Task Table_has_real_fields_bounded_pages_and_source_navigation_without_opening_source()
    {
        string records = string.Join(',', Enumerable.Range(0, 130).Select(i =>
            "{\"name\":\"row" + i + "\",\"a\":1,\"b\":2,\"c\":3,\"d\":4,\"e\":5}"));
        using var host = new Host("[" + records + "]", StructureFormat.Json);
        host.View.Presentation = StructureViewPresentation.Table;
        host.View.Sync(); await host.Drain();
        var table = host.View.Table;
        Assert.True(table.IsVisible); Assert.False(host.View.Tree.IsVisible);
        Assert.Equal(64, table.LoadedRows); Assert.Equal(256, table.LoadedCells);
        Assert.Equal(new[] { "name", "a", "b", "c" }, table.CurrentPage.Columns.Select(column => column.Name));
        Assert.Equal("\"row0\"", table.CurrentPage.Rows[0].Cells[new("name")].Value);
        Assert.True(table.CurrentPage.MoreRows); Assert.True(table.CurrentPage.MoreFields);
        Assert.True(host.View.FocusContent());
        TestApp.Press(host.Window, Key.Down); await host.Drain();
        Assert.NotNull(host.Jumped); Assert.Equal(0, host.Opened);
        Assert.Equal(host.Jumped, host.View.SelectedRange);
        Click(host.View.JumpButton); Assert.Equal(1, host.Opened);
        Click(host.View.CopySourceButton); await host.Drain(); Assert.Equal("\"name\":\"row0\"", host.Copied);

        TestApp.Press(host.Window, Key.PageDown); await host.Drain();
        Assert.Equal("\"row64\"", table.CurrentPage.Rows[0].Cells[new("name")].Value);
        TestApp.Press(host.Window, Key.PageDown); await host.Drain();
        TestApp.Press(host.Window, Key.Right, RawInputModifiers.Control); await host.Drain();
        Assert.Equal(2, table.LoadedRows); Assert.Equal(4, table.LoadedCells);
        Assert.Equal(new[] { "d", "e" }, table.CurrentPage.Columns.Select(column => column.Name));
        Assert.False(table.CurrentPage.MoreRows); Assert.False(table.CurrentPage.MoreFields);
        Assert.InRange(host.View.CachedItems, 0, StructureView.MaximumItems);
    }

    [AvaloniaTheory]
    [InlineData(StructureFormat.Json, "{\"name\":\"old\"}")]
    [InlineData(StructureFormat.Xml, "<root name='old'/>")]
    [InlineData(StructureFormat.Yaml, "name: old\n")]
    [InlineData(StructureFormat.Neon, "name: old\n")]
    public async Task Table_edit_uses_the_index_and_one_document_undo(StructureFormat format, string text)
    {
        using var host = new Host(text, format);
        host.View.Presentation = StructureViewPresentation.Table;
        host.View.Sync(); await host.Drain();
        Assert.Equal(1, host.View.Table.LoadedRows);
        host.View.FocusContent(); TestApp.Press(host.Window, Key.Down); await host.Drain();
        Assert.True(host.View.EditButton.IsEnabled);
        host.Replacement = "new";
        Click(host.View.EditButton); await host.Drain();
        Assert.Equal(1, host.Document.UndoDepth);
        Assert.Contains("new", host.Text); Assert.DoesNotContain("old", host.Text);
        Assert.Contains(host.View.Table.CurrentPage.Rows.SelectMany(row => row.Cells.Values), cell => cell.Value.Contains("new", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Graph_reuses_loaded_nodes_culls_offscreen_cards_and_ignores_old_layouts()
    {
        string json = "[" + string.Join(',', Enumerable.Range(0, 200)) + "]";
        using var host = new Host(json, StructureFormat.Json);
        host.View.Sync(); await host.Drain();
        var root = host.View.Tree.VisibleNodes.Single(node => node.Kind == StructureTreeNodeKind.Normal);
        root.IsExpanded = true; await host.Drain();
        host.View.Presentation = StructureViewPresentation.Graph;
        await host.GraphDrain();
        Assert.Equal(host.View.Tree.VisibleNodes.Count, host.View.Graph.LoadedCards);
        Assert.InRange(host.View.Graph.LoadedCards, 64, StructureView.MaximumItems);
        host.View.Graph.Zoom(10); StructureLayoutTests.Layout(host.Window);
        Assert.InRange(host.View.Graph.DrawnCards, 1, host.View.Graph.LoadedCards - 1);
        host.View.Graph.Zoom(0.1); StructureLayoutTests.Layout(host.Window);
        Assert.InRange(host.View.Graph.DrawnCards, 1, host.View.Graph.LoadedCards);
        host.View.Graph.Zoom(10); StructureLayoutTests.Layout(host.Window);
        Assert.InRange(host.View.Graph.DrawnCards, 1, host.View.Graph.LoadedCards - 1);
        host.View.Tree.SelectedNode = root;
        Assert.True(host.View.FocusContent());
        TestApp.Press(host.Window, Key.PageDown); await host.Drain(); await host.GraphDrain();
        var first = host.View.Tree.VisibleNodes.First(node => node.Tag is StructureNode { Kind: StructureKind.JsonNumber });
        Assert.Equal(64, ((StructureNode)first.Tag!).Ordinal);
        TestApp.Press(host.Window, Key.Left); await host.Drain(); await host.GraphDrain();
        Assert.False(root.IsExpanded); Assert.Equal(1, host.View.Graph.LoadedCards);
        Assert.Equal(1, host.View.Graph.DrawnCards);
        TestApp.Press(host.Window, Key.Right); await host.Drain(); await host.GraphDrain();
        Assert.True(root.IsExpanded); Assert.True(host.View.Graph.LoadedCards > 1);
        root.IsExpanded = false; root.IsExpanded = true; root.IsExpanded = false;
        await host.GraphDrain();
        Assert.Equal(1, host.View.Graph.LoadedCards);
        Assert.InRange(host.View.CachedItems, 0, StructureView.MaximumItems);
    }

    [AvaloniaFact]
    public async Task Table_filter_and_query_show_matching_cells_and_clear_restores_records()
    {
        using var host = new Host("{\"keep\":1,\"drop\":2}", StructureFormat.Json);
        host.View.Presentation = StructureViewPresentation.Table;
        host.View.Sync(); await host.Drain();
        Assert.Equal(2, host.View.Table.LoadedRows);
        host.View.Filter.Text = "keep"; Click(host.View.FindButton); await host.Drain();
        var result = Assert.Single(host.View.Table.CurrentPage.Rows);
        Assert.Equal("keep", result.Name); Assert.Equal("1", Assert.Single(result.Cells.Values).Value);
        host.View.Filter.Text = ""; await host.Drain();
        Assert.Equal(2, host.View.Table.LoadedRows);
        host.View.SearchScope.SelectedIndex = 3;
        host.View.Filter.Text = "$.drop"; Click(host.View.FindButton); await host.Drain();
        result = Assert.Single(host.View.Table.CurrentPage.Rows);
        Assert.Equal("drop", result.Name); Assert.Equal("2", Assert.Single(result.Cells.Values).Value);
    }

    [AvaloniaFact]
    public async Task Table_duplicate_keys_do_not_collide_with_literal_column_names()
    {
        using var host = new Host("[{\"a\":1,\"a\":2,\"a [2]\":3}]", StructureFormat.Json);
        host.View.Presentation = StructureViewPresentation.Table;
        host.View.Sync(); await host.Drain();
        var row = Assert.Single(host.View.Table.CurrentPage.Rows);
        Assert.Equal(3, row.Cells.Count);
        Assert.Equal("1", row.Cells[new("a")].Value);
        Assert.Equal("2", row.Cells[new("a", 2)].Value);
        Assert.Equal("3", row.Cells[new("a [2]")].Value);
    }

    [AvaloniaFact]
    public async Task Table_right_click_changes_the_copy_and_edit_target()
    {
        using var host = new Host("{\"a\":1,\"b\":2}", StructureFormat.Json);
        host.View.Presentation = StructureViewPresentation.Table;
        host.View.Sync(); await host.Drain();
        host.View.FocusContent(); TestApp.Press(host.Window, Key.Down); await host.Drain();
        Click(host.View.CopySourceButton); await host.Drain(); Assert.Equal("\"a\":1", host.Copied);
        var point = host.View.Table.TranslatePoint(new Point(180, 75), host.Window)!.Value;
        host.Window.MouseDown(point, MouseButton.Right); host.Window.MouseUp(point, MouseButton.Right);
        await host.Drain();
        Click(host.View.CopySourceButton); await host.Drain(); Assert.Equal("\"b\":2", host.Copied);
        host.Replacement = "7"; Click(host.View.EditButton); await host.Drain();
        Assert.Equal("{\"a\":1,\"b\":7}", host.Text);
        Assert.Equal(1, host.Document.UndoDepth);
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private sealed class Host : IDisposable
    {
        private readonly StructureFormat format;
        private long offset;
        public Doc Document { get; }
        public StructureView View { get; }
        public Window Window { get; }
        public ByteRange? Jumped;
        public int Opened;
        public string? Copied, Replacement;
        public string Text => Document.Codec.GetString(Document.ReadBytes(new(0, Document.ByteLength)));

        public Host(string text, StructureFormat format)
        {
            this.format = format;
            Document = Doc.FromBytes(TextCodec.Utf8.Encode(text), new PagedBackendFactory(startIndexing: false),
                new DocumentOptions { Format = TextCodec.Utf8 });
            View = new(Source, value => { Copied = value; return Task.CompletedTask; },
                (_, _, _) => Task.FromResult(Replacement));
            Window = new Window { Width = 900, Height = 700, Content = View };
            Window.Show(); StructureLayoutTests.Layout(Window);
        }

        private StructureSource Source() => new(Document, Document.Id, Document.Revision, Document.Codec, 0,
            format, offset, false, Document.GetSnapshot, range =>
            { Jumped = range; offset = range.Start; View.Sync(); },
            (edit, revision, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(Document.Apply([edit], new EditOptions { BaseRevision = revision }).Applied);
            }, _ => Opened++);

        public Task Drain() => StructureLayoutTests.Drain(View, Window);

        public async Task GraphDrain()
        {
            for (int i = 0; i < 100; i++)
            {
                var current = View.Graph.LayoutWork;
                try { await current; } catch (OperationCanceledException) { }
                StructureLayoutTests.Layout(Window);
                if (ReferenceEquals(current, View.Graph.LayoutWork)) return;
            }
            Assert.Fail("The graph layout did not settle.");
        }

        public void Dispose() { View.Dispose(); Window.Close(); Document.Dispose(); }
    }
}
