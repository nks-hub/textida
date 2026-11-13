using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public class StructurePanelTests
{
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static StructureNode Node(TreeViewItem item) => Assert.IsType<StructureNode>(item.Tag);
    private static IEnumerable<TreeViewItem> DataItems(ItemsControl parent) => parent.Items.OfType<TreeViewItem>().Where(item => item.Tag is StructureNode);

    private static async Task Drain(StructureView view)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Task running = view.Working;
            if (running.IsCompleted)
            {
                await running;
                Dispatcher.UIThread.RunJobs();
                if (ReferenceEquals(running, view.Working)) return;
            }
            await Task.Delay(10);
        }
        Assert.Fail("The structure worker did not finish.");
    }

    [AvaloniaFact]
    public async Task Lazy_pages_replace_siblings_and_path_copy_uses_the_real_node()
    {
        using var host = new Host("[" + string.Join(',', Enumerable.Range(0, 400)) + "]", StructureFormat.Json);
        host.View.Sync(); await Drain(host.View);
        var document = Assert.Single(DataItems(host.View.Tree));
        var array = Assert.Single(DataItems(document));
        Assert.Single(array.Items);
        array.IsExpanded = true; await Drain(host.View);
        Assert.Equal(Enumerable.Range(0, 128).Select(i => (long)i), DataItems(array).Select(item => Node(item).Ordinal));
        int firstCount = host.View.CachedItems;
        var pager = array.Items.OfType<TreeViewItem>().Single(item => item.Tag == null);
        Click(Assert.IsType<WrapPanel>(pager.Header).Children.OfType<Button>().Last());
        await Drain(host.View);
        Assert.Equal(Enumerable.Range(128, 128).Select(i => (long)i), DataItems(array).Select(item => Node(item).Ordinal));
        Assert.Equal(firstCount, host.View.CachedItems);
        pager = array.Items.OfType<TreeViewItem>().Single(item => item.Tag == null);
        Click(Assert.IsType<WrapPanel>(pager.Header).Children.OfType<Button>().First());
        await Drain(host.View);
        var first = DataItems(array).First();
        Assert.Equal(0, Node(first).Ordinal);
        host.View.Tree.SelectedItem = first;
        Assert.Equal(Node(first).Range, host.Jumped);
        Click(host.View.CopyPathButton); await Drain(host.View);
        Assert.Equal("$[0]", host.Copied);
        Assert.True(host.Reads > 0);
        Assert.InRange(host.View.CachedItems, 1, StructureView.MaximumItems);
    }

    [AvaloniaFact]
    public async Task A_source_caret_reveals_an_unloaded_array_page_and_selects_the_exact_node_without_jumping_back()
    {
        string text = "[" + string.Join(',', Enumerable.Range(0, 400)) + "]";
        using var host = new Host(text, StructureFormat.Json);
        host.View.Sync(); await Drain(host.View);
        host.Editor.Focus();
        host.Jumped = null;
        host.Offset = text.IndexOf("300", StringComparison.Ordinal);
        host.View.Sync(); await Drain(host.View);
        var selected = Assert.IsType<TreeViewItem>(host.View.Tree.SelectedItem);
        Assert.Equal(StructureKind.JsonNumber, Node(selected).Kind);
        Assert.Equal(300, Node(selected).Ordinal);
        Assert.Equal(host.Offset, Node(selected).ValueStart);
        Assert.Equal(Loc.Get("structure.caret"), AutomationProperties.GetItemStatus(selected));
        Assert.Same(host.Editor, host.Window.FocusManager!.GetFocusedElement());
        Assert.Null(host.Jumped);
        Assert.InRange(host.View.CachedItems, 1, StructureView.MaximumItems);
        var counts = Assert.IsType<StackPanel>(host.View.TypeCounts.Content);
        Assert.Equal(Enum.GetValues<StructureKind>().Length, counts.Children.Count);
        Assert.Contains(counts.Children.OfType<TextBlock>(), label => label.Text == Loc.Get("structure.kind.JsonNumber") + ": 400");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unicode_attribute_and_cdata_carets_reveal_their_own_physical_nodes_and_user_selection_jumps_to_source(bool utf16)
    {
        const string text = "<r 名='é😀'><![CDATA[Ž😀]]><x>tail</x></r>";
        var codec = utf16 ? TextCodec.Utf16BEBom : TextCodec.Utf8;
        using var host = new Host(text, StructureFormat.Xml, codec);
        host.View.Sync(); await Drain(host.View);
        host.Editor.Focus();
        foreach (var (fragment, kind) in new[] { ("é", StructureKind.XmlAttribute), ("Ž", StructureKind.XmlCData) })
        {
            host.Jumped = null;
            host.Offset = codec.Bom.Length + codec.Encode(text[..text.IndexOf(fragment, StringComparison.Ordinal)]).Length;
            host.View.Sync(); await Drain(host.View);
            var selected = Assert.IsType<TreeViewItem>(host.View.Tree.SelectedItem);
            Assert.Equal(kind, Node(selected).Kind);
            Assert.True(Node(selected).ValueStart <= host.Offset && host.Offset < Node(selected).ValueEnd);
            Assert.Null(host.Jumped);
            Assert.Same(host.Editor, host.Window.FocusManager!.GetFocusedElement());
        }
        var document = Assert.Single(DataItems(host.View.Tree));
        var element = Assert.Single(DataItems(document));
        var attribute = DataItems(element).Single(item => Node(item).Kind == StructureKind.XmlAttribute);
        host.View.Tree.SelectedItem = attribute;
        Assert.Equal(Node(attribute).Range, host.Jumped);
        Assert.False(host.Document.CanUndo);
    }

    [AvaloniaFact]
    public async Task Opening_many_branches_evicts_old_controls_and_an_evicted_branch_can_be_opened_again()
    {
        string source = "[" + string.Join(',', Enumerable.Range(0, 40).Select(group => "{" + string.Join(',', Enumerable.Range(0, 128).Select(i => "\"k" + i + "\":" + (group + i))) + "}")) + "]";
        using var host = new Host(source, StructureFormat.Json);
        host.View.Sync(); await Drain(host.View);
        var array = Assert.Single(DataItems(Assert.Single(DataItems(host.View.Tree))));
        array.IsExpanded = true; await Drain(host.View);
        var branches = DataItems(array).ToArray();
        foreach (var branch in branches)
        {
            branch.IsExpanded = true; await Drain(host.View);
            Assert.InRange(host.View.CachedItems, 1, StructureView.MaximumItems);
        }
        Assert.False(branches[0].IsExpanded);
        Assert.True(branches[^1].IsExpanded);
        branches[0].IsExpanded = true; await Drain(host.View);
        Assert.Equal(128, DataItems(branches[0]).Count());
        Assert.InRange(host.View.CachedItems, 1, StructureView.MaximumItems);
    }

    [AvaloniaFact]
    public async Task A_new_source_cancels_the_old_debounce_and_caret_marking_keeps_keyboard_focus()
    {
        using var host = new Host("{\"old\":1}", StructureFormat.Json);
        host.View.Sync();
        host.Change("{\"new\":2}");
        host.View.Sync(); await Drain(host.View);
        Assert.Equal(1, host.Snapshots);
        var root = Assert.Single(DataItems(host.View.Tree));
        var objectItem = Assert.Single(DataItems(root));
        objectItem.IsExpanded = true; await Drain(host.View);
        var value = Assert.Single(DataItems(objectItem));
        Assert.Contains("new", AutomationProperties.GetName(value), StringComparison.Ordinal);
        host.Editor.Focus();
        host.Offset = Node(value).ValueStart;
        host.View.Sync(); await Drain(host.View);
        Assert.Same(host.Editor, host.Window.FocusManager!.GetFocusedElement());
        Assert.Equal(Loc.Get("structure.caret"), AutomationProperties.GetItemStatus(value));
        host.View.Suspend(); await Drain(host.View);
        Assert.Empty(host.View.Tree.Items);
        host.View.Sync(); await Drain(host.View);
        Assert.Single(DataItems(host.View.Tree));
    }

    [AvaloniaFact]
    public async Task Filter_query_pages_and_edit_use_the_current_revision_and_one_document_undo()
    {
        using var host = new Host("[\"find me\",\"other\",\"find again\"]", StructureFormat.Json);
        host.View.Sync(); await Drain(host.View);
        host.View.SearchScope.SelectedIndex = 1;
        host.View.Filter.Text = "FIND";
        Click(host.View.FindButton); await Drain(host.View);
        Assert.Equal(2, DataItems(host.View.Tree).Count());
        host.View.Tree.SelectedItem = DataItems(host.View.Tree).First();
        Click(host.View.CopySourceButton); await Drain(host.View);
        Assert.Equal("\"find me\"", host.Copied);
        host.PromptValue = "new value";
        Click(host.View.EditButton); await Drain(host.View);
        Assert.Equal("find me", host.InitialValue);
        Assert.Equal(1, host.Document.UndoDepth);
        Assert.Equal("[\"new value\",\"other\",\"find again\"]", host.Text);
        Assert.NotNull(host.Document.Undo());
        Assert.Equal("[\"find me\",\"other\",\"find again\"]", host.Text);
    }

    [AvaloniaFact]
    public async Task XPath_text_group_jump_and_copy_cover_every_part_but_edit_requires_a_physical_tree_value()
    {
        using var host = new Host("<r>a<![CDATA[b]]>c</r>", StructureFormat.Xml);
        host.View.Sync(); await Drain(host.View);
        host.View.SearchScope.SelectedIndex = 3;
        host.View.Filter.Text = "/r/text()";
        Click(host.View.FindButton); await Drain(host.View);
        var result = Assert.Single(DataItems(host.View.Tree));
        Assert.Contains("abc", AutomationProperties.GetName(result), StringComparison.Ordinal);
        host.View.Tree.SelectedItem = result;
        Click(host.View.JumpButton);
        Assert.Equal(new ByteRange(3, 18), host.Jumped);
        Click(host.View.CopySourceButton); await Drain(host.View);
        Assert.Equal("a<![CDATA[b]]>c", host.Copied);
        Click(host.View.EditButton); await Drain(host.View);
        Assert.Null(host.InitialValue);
        Assert.Equal(Loc.Get("structure.groupEdit"), host.View.Message);
        Assert.False(host.Document.CanUndo);
    }

    [AvaloniaFact]
    public async Task Invalid_structure_offers_the_real_issue_location_instead_of_an_editable_partial_tree()
    {
        using var host = new Host("<r><x></r>", StructureFormat.Xml);
        host.View.Sync(); await Drain(host.View);
        Assert.True(host.View.IssueButton.IsVisible);
        Assert.False(host.View.EditButton.IsEnabled);
        Click(host.View.IssueButton);
        Assert.NotNull(host.Jumped);
        Assert.Equal(6, host.Jumped!.Value.Start);
        Assert.False(host.Document.CanUndo);
    }

    private sealed class Host : IDisposable
    {
        private readonly List<Doc> documents = [];
        private object key = new();
        private readonly StructureFormat format;
        private readonly TextCodec codec;
        public Doc Document { get; private set; }
        public Window Window { get; }
        public StructureView View { get; }
        public TextBox Editor { get; } = new() { Text = "Keyboard stays here" };
        public long Offset;
        public int Reads, Snapshots;
        public string? Copied, InitialValue, PromptValue;
        public ByteRange? Jumped;
        public string Text => Document.Codec.GetString(Document.ReadBytes(new(Document.BomLength, Document.ByteLength)));

        public Host(string text, StructureFormat format, TextCodec? codec = null)
        {
            this.format = format;
            this.codec = codec ?? TextCodec.Utf8;
            Document = NewDocument(text);
            View = new(Source, value => { Assert.True(Dispatcher.UIThread.CheckAccess()); Copied = value; return Task.CompletedTask; },
                (_, _, initial) => { InitialValue = initial; return Task.FromResult(PromptValue); });
            var content = new DockPanel(); DockPanel.SetDock(Editor, Dock.Bottom); content.Children.Add(Editor); content.Children.Add(View);
            Window = new Window { Width = 900, Height = 700, Content = content };
            Window.Show(); Window.UpdateLayout();
        }

        private Doc NewDocument(string text)
        {
            byte[] bytes = [.. codec.Bom, .. codec.Encode(text)];
            var document = Doc.FromBytes(bytes, new PagedBackendFactory(startIndexing: false), new DocumentOptions { Format = codec });
            documents.Add(document);
            return document;
        }

        public void Change(string text) { Document = NewDocument(text); key = new object(); }

        private StructureSource Source()
        {
            Assert.True(Dispatcher.UIThread.CheckAccess());
            var document = Document;
            return new(key, document.Id, document.Revision, document.Codec, document.BomLength, format, Offset, false,
                () =>
                {
                    Assert.True(Dispatcher.UIThread.CheckAccess());
                    Snapshots++;
                    var original = document.GetSnapshot();
                    return new(original.DocumentId, original.Revision, new TrackedSnapshot(original, this), original.Codec);
                }, range => { Jumped = range; Offset = range.Start; },
                (edit, revision, ct) =>
                {
                    Assert.True(Dispatcher.UIThread.CheckAccess());
                    ct.ThrowIfCancellationRequested();
                    return Task.FromResult(document.Revision == revision && document.Apply([edit], new EditOptions { BaseRevision = revision }).Applied);
                });
        }

        public void Dispose()
        {
            View.Dispose(); Window.Close();
            foreach (var document in documents) document.Dispose();
        }
    }

    private sealed class TrackedSnapshot(IByteSnapshot source, Host host) : IByteSnapshot
    {
        public long ByteLength => source.ByteLength;
        public long RetainedBytes => source.RetainedBytes;
        public int Read(long offset, Span<byte> destination)
        {
            if (Dispatcher.UIThread.CheckAccess()) throw new InvalidOperationException("Structure source bytes must be read on the worker.");
            Interlocked.Increment(ref host.Reads);
            return source.Read(offset, destination);
        }
    }
}
