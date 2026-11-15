using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class StructureRowCacheTests(ITestOutputHelper output)
{
    private static StructureNode Node(TreeViewItem item) => Assert.IsType<StructureNode>(item.Tag);
    private static IEnumerable<TreeViewItem> DataItems(ItemsControl parent) => parent.Items.OfType<TreeViewItem>().Where(item => item.Tag is StructureNode);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Button Pager(TreeViewItem item, bool next) => Assert.IsType<WrapPanel>(
        item.Items.OfType<TreeViewItem>().Single(child => child.Tag == null).Header).Children.OfType<Button>().ElementAt(next ? 1 : 0);

    private static int ActiveItems(ItemsControl parent) => parent.Items.OfType<TreeViewItem>().Sum(item => 1 + ActiveItems(item));

    private static void Bound(StructureView view)
    {
        Assert.InRange(ActiveItems(view.Tree), 1, StructureView.MaximumItems);
        Assert.InRange(view.CachedItems, ActiveItems(view.Tree), StructureView.MaximumItems);
    }

    private static Control[] RowControls(TreeViewItem item)
    {
        var header = Assert.IsAssignableFrom<Control>(item.Header);
        return [item, header, .. header.GetVisualDescendants().OfType<Control>()];
    }

    [AvaloniaFact]
    public async Task Installing_32_cold_pages_of_64_rows_allocates_at_most_64_mib()
    {
        bool tracing = TypingTrace.Enabled;
        try
        {
            TypingTrace.Enabled = false;
            string values = "[" + string.Join(',', Enumerable.Range(0, 2048)) + "]";
            string text = "[" + string.Join(',', Enumerable.Repeat(values, 32)) + "]";
            using var host = new Host(text, StructureFormat.Json);
            Assert.Contains(Application.Current!.Styles, style => style is Avalonia.Themes.Fluent.FluentTheme);
            host.View.Sync(); await host.Drain();
            var array = Assert.Single(DataItems(Assert.Single(DataItems(host.View.Tree))));
            array.IsExpanded = true; await host.Drain();
            var branches = DataItems(array).ToArray();
            Assert.Equal(32, branches.Length);

            TypingTrace.Reset(); TypingTrace.Enabled = true;
            int maximum = 0;
            foreach (var branch in branches)
            {
                Assert.False(branch.IsExpanded);
                Assert.Empty(DataItems(branch));
                host.View.Tree.SelectedItem = branch; await host.Drain();
                Assert.Empty(DataItems(branch));
                branch.IsExpanded = true; await host.Drain();
                var children = DataItems(branch).ToArray();
                Assert.Equal(64, children.Length);
                Assert.Equal(Enumerable.Range(0, 64).Select(i => (long)i), children.Select(item => Node(item).Ordinal));
                Assert.All(children, child => Assert.Equal(Node(branch).Id, Node(child).ParentId));
                maximum = Math.Max(maximum, host.View.CachedItems);
                Bound(host.View);
                branch.IsExpanded = false; await host.Drain();
            }
            Assert.InRange(maximum, StructureView.MaximumItems - StructureView.PageSize, StructureView.MaximumItems);
            Assert.Empty(DataItems(branches[0]));
            string summary = TypingTrace.Summary();
            output.WriteLine(summary);
            var install = Regex.Match(summary, @"(?m)^structure\.installPage\s+n=\s*(\d+)[^\r\n]*\balloc=(\d+)");
            Assert.True(install.Success, "The actual page installation span was not recorded.");
            int pages = int.Parse(install.Groups[1].Value, CultureInfo.InvariantCulture);
            long mean = long.Parse(install.Groups[2].Value, CultureInfo.InvariantCulture);
            Assert.Equal(32, pages);
            long allocatedUpper = checked(pages * (mean + 1));
            output.WriteLine($"installed-pages={pages}; rows-per-page=64; rounded-allocation-upper-bytes={allocatedUpper}; limit-bytes={64L << 20}");
            foreach (var (name, expectedCount) in new[] { ("structure.row.prepare", pages * StructureView.PageSize), ("structure.page.attach", pages) })
            {
                var detail = Regex.Match(summary, @"(?m)^" + Regex.Escape(name) + @"\s+n=\s*(\d+)[^\r\n]*\balloc=(\d+)");
                Assert.True(detail.Success, $"The {name} span was not recorded.");
                int count = int.Parse(detail.Groups[1].Value, CultureInfo.InvariantCulture);
                long rowMean = long.Parse(detail.Groups[2].Value, CultureInfo.InvariantCulture);
                Assert.Equal(expectedCount, count);
                long rowAllocatedUpper = checked(count * (rowMean + 1));
                output.WriteLine($"{name}; count={count}; rounded-allocation-upper-bytes={rowAllocatedUpper}; share-of-install-percent={100.0 * rowAllocatedUpper / allocatedUpper:0.00}");
            }
            Assert.InRange(allocatedUpper, 0, 64L << 20);
        }
        finally { TypingTrace.Enabled = tracing; TypingTrace.Reset(); }
    }

    [AvaloniaFact]
    public async Task Rotating_32_branches_reuses_the_row_and_single_header_and_keeps_exact_tags_parents_and_pages()
    {
        string text = "[" + string.Join(',', Enumerable.Range(0, 32).Select(group => "["
            + string.Join(',', Enumerable.Range(0, 135).Select(i => group * 1000 + i)) + "]")) + "]";
        using var host = new Host(text, StructureFormat.Json);
        host.View.Sync(); await host.Drain();
        var array = Assert.Single(DataItems(Assert.Single(DataItems(host.View.Tree))));
        array.IsExpanded = true; await host.Drain();
        var branches = DataItems(array).ToArray();
        Assert.Equal(32, branches.Length);
        var identities = new Dictionary<TreeViewItem, (long Id, Control[] Controls)>();
        var prepared = new List<(TreeViewItem Item, StructureNode Node, StyledElement? Parent)>();
        foreach (var branch in branches)
            branch.ContainerPrepared += (_, e) =>
            {
                if (e.Container is TreeViewItem item && item.Tag is StructureNode node)
                    prepared.Add((item, node, item.Parent));
            };
        int reused = 0;
        foreach (var branch in branches.Concat(branches))
        {
            prepared.Clear();
            host.View.Tree.SelectedItem = branch;
            branch.IsExpanded = true; await host.Drain();
            Assert.Equal(Enumerable.Range(0, StructureView.PageSize).Select(i => (long)i), DataItems(branch).Select(item => Node(item).Ordinal));
            Assert.Equal(StructureView.PageSize, prepared.Count);
            Assert.Equal(DataItems(branch), prepared.Select(row => row.Item));
            Assert.All(prepared, row =>
            {
                Assert.Same(branch, row.Parent);
                Assert.Equal(Node(branch).Id, row.Node.ParentId);
                Assert.Equal(row.Node, Node(row.Item));
            });
            foreach (var leaf in DataItems(branch))
            {
                var node = Node(leaf);
                var controls = RowControls(leaf);
                Assert.Equal(2, controls.Length);
                Assert.IsType<StructureRowHeader>(leaf.Header);
                if (identities.TryGetValue(leaf, out var old) && old.Id != node.Id)
                {
                    reused++;
                    foreach (var (before, after) in old.Controls.Zip(controls)) Assert.Same(before, after);
                    Assert.Equal("", AutomationProperties.GetItemStatus(leaf));
                }
                identities[leaf] = (node.Id, controls);
                Assert.Equal(Node(branch).Id, node.ParentId);
                Assert.False(leaf.IsExpanded); Assert.False(leaf.IsSelected);
                Assert.Empty(leaf.Items);
                string value = (Node(branch).Ordinal * 1000 + node.Ordinal).ToString(CultureInfo.InvariantCulture);
                Assert.EndsWith(" " + value, AutomationProperties.GetName(leaf), StringComparison.Ordinal);
                Assert.Contains("[" + node.Ordinal + "]", AutomationProperties.GetName(leaf), StringComparison.Ordinal);
            }
            Bound(host.View);
            branch.IsExpanded = false; await host.Drain();
            Bound(host.View);
        }
        Assert.True(reused >= StructureView.PageSize);
        Assert.InRange(identities.Count, StructureView.PageSize, StructureView.MaximumItems);

        var last = branches[^1];
        last.IsExpanded = true; await host.Drain();
        var staleNext = Pager(last, next: true);
        Click(staleNext); await host.Drain();
        Assert.Equal(64, Node(DataItems(last).First()).Ordinal);
        Click(staleNext); await host.Drain();
        Assert.Equal(64, Node(DataItems(last).First()).Ordinal);
        Click(Pager(last, next: true)); await host.Drain();
        Assert.Equal(Enumerable.Range(128, 7).Select(i => (long)i), DataItems(last).Select(item => Node(item).Ordinal));
        Assert.True(host.View.CachedItems > ActiveItems(host.View.Tree));
        var selected = DataItems(last).First();
        var selectedNode = Node(selected);
        host.View.Tree.SelectedItem = selected;
        Assert.Equal(selectedNode.Range, host.Jumped);
        Click(host.View.CopyPathButton); await host.Drain();
        Assert.Equal("$[31][128]", host.Copied);
        Click(Pager(last, next: false)); await host.Drain();
        Assert.Equal(64, Node(DataItems(last).First()).Ordinal);
        Assert.DoesNotContain(selected, DataItems(last));
        Assert.Equal(selectedNode, Node(selected));
        Bound(host.View);
    }

    [AvaloniaFact]
    public async Task Pooled_leaves_restore_branch_templates_and_children_and_return_to_leaf_templates()
    {
        string text = "[" + string.Join(',', Enumerable.Range(0, 192).Select(i => i is >= 64 and < 128 ? "[1]" : i.ToString(CultureInfo.InvariantCulture))) + "]";
        using var host = new Host(text, StructureFormat.Json);
        host.View.Sync(); await host.Drain();
        var array = Assert.Single(DataItems(Assert.Single(DataItems(host.View.Tree))));
        array.IsExpanded = true; await host.Drain();
        var leaves = DataItems(array).ToArray();
        Assert.Equal(StructureView.PageSize, leaves.Length);
        Assert.All(leaves, item => Assert.DoesNotContain(item.GetVisualDescendants(), visual => visual is ToggleButton or ItemsPresenter));
        var headers = leaves.ToDictionary(item => item, item => item.Header);
        host.View.Tree.SelectedItem = array; array.Focus();
        Click(Pager(array, next: true)); await host.Drain();
        var branches = DataItems(array).ToArray();
        Assert.Equal(Enumerable.Range(64, 64).Select(i => (long)i), branches.Select(item => Node(item).Ordinal));
        Assert.All(branches, item =>
        {
            Assert.True(headers.TryGetValue(item, out var header)); Assert.Same(header, item.Header);
            Assert.Null(item.Theme);
            Assert.Contains(item.GetVisualDescendants().OfType<ItemsPresenter>(), presenter => presenter.TemplatedParent == item);
            Assert.Contains(item.GetVisualDescendants().OfType<ToggleButton>(), button => button.TemplatedParent == item && button.IsVisible);
        });
        var branch = branches[0];
        branch.IsExpanded = true; await host.Drain();
        var child = Assert.Single(DataItems(branch));
        Assert.Equal(Node(branch).Id, Node(child).ParentId); Assert.Same(branch, child.Parent);
        headers[child] = child.Header;
        host.View.Tree.SelectedItem = array; array.Focus();
        Click(Pager(array, next: true)); await host.Drain();
        var reused = DataItems(array).ToArray();
        Assert.Equal(Enumerable.Range(128, 64).Select(i => (long)i), reused.Select(item => Node(item).Ordinal));
        Assert.True(reused.Count(item => leaves.Contains(item)) >= StructureView.PageSize - 1);
        Assert.All(reused, item =>
        {
            Assert.True(headers.TryGetValue(item, out var header)); Assert.Same(header, item.Header);
            Assert.NotNull(item.Theme?.BasedOn);
            Assert.Equal(Node(array).Id, Node(item).ParentId); Assert.Same(array, item.Parent);
            Assert.Empty(item.Items);
            Assert.DoesNotContain(item.GetVisualDescendants(), visual => visual is ToggleButton or ItemsPresenter);
        });
        host.View.Tree.SelectedItem = reused[0];
        Assert.Equal(Node(reused[0]).Range, host.Jumped);
        Bound(host.View);
    }

    [AvaloniaFact]
    public async Task Xml_container_pages_anchor_when_needed_and_parent_caret_and_attribute_edit_keep_source_identity()
    {
        string children = string.Concat(Enumerable.Range(0, 65).Select(i => "<x 名='é'><![CDATA[Ž" + i + "]]></x>"));
        string text = "<r xmlns='urn:test'>" + string.Concat(Enumerable.Range(0, 64).Select(i => "<n" + i + ">" + children + "</n" + i + ">")) + "</r>";
        using var host = new Host(text, StructureFormat.Xml, TextCodec.Utf16BEBom);
        host.View.Sync(); await host.Drain();
        var root = Assert.Single(DataItems(Assert.Single(DataItems(host.View.Tree))));
        var rootNode = Node(root);
        root.IsExpanded = true; await host.Drain();
        var branch = DataItems(root).First(item => Node(item).Kind == StructureKind.XmlElement);
        var branchNode = Node(branch);
        branch.IsExpanded = true; await host.Drain();
        var anchored = Assert.Single(DataItems(host.View.Tree));
        Assert.Equal(branchNode, Node(anchored));
        Assert.Equal(StructureView.PageSize, DataItems(anchored).Count());
        Bound(host.View);
        Click(Pager(anchored, next: true)); await host.Drain();
        var last = Assert.Single(DataItems(anchored));
        Assert.Equal(64, Node(last).Ordinal);
        last.IsExpanded = true; await host.Drain();
        var attribute = DataItems(last).Single(item => Node(item).Kind == StructureKind.XmlAttribute);
        var cdata = DataItems(last).Single(item => Node(item).Kind == StructureKind.XmlCData);
        var cdataNode = Node(cdata);
        Assert.Equal(-1, Node(attribute).NamespaceId);
        Assert.True(Node(last).NamespaceId >= 0);
        host.View.Tree.SelectedItem = attribute;
        Assert.Equal(Node(attribute).Range, host.Jumped);
        Click(host.View.CopySourceButton); await host.Drain();
        Assert.Equal("名='é'", host.Copied);

        var up = host.View.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, Loc.Get("structure.up")));
        Click(up); await host.Drain();
        Assert.Equal(rootNode, Node(Assert.Single(DataItems(host.View.Tree))));
        host.Jumped = null;
        host.Offset = cdataNode.ValueStart; host.View.Sync(); await host.Drain();
        var revealed = Assert.IsType<TreeViewItem>(host.View.Tree.SelectedItem);
        Assert.Equal(cdataNode, Node(revealed));
        Assert.Null(host.Jumped);
        Assert.Equal(Loc.Get("structure.caret"), AutomationProperties.GetItemStatus(revealed));
        Bound(host.View);

        var element = Assert.IsType<TreeViewItem>(revealed.Parent);
        attribute = DataItems(element).Single(item => Node(item).Kind == StructureKind.XmlAttribute);
        host.View.Tree.SelectedItem = attribute;
        host.PromptValue = "new & é";
        Click(host.View.EditButton); await host.Drain();
        Assert.Contains("名='new &amp; é'", host.Text, StringComparison.Ordinal);
        Assert.Equal(1, host.Document.UndoDepth);
        Assert.NotNull(host.Document.Undo());
        Assert.Equal(text, host.Text);
    }

    [AvaloniaFact]
    public async Task A_cancelled_inflight_page_cannot_publish_into_a_new_source_or_leave_its_rows_in_the_pool()
    {
        using var host = new Host("{\"old\":[" + string.Join(',', Enumerable.Range(0, 200)) + "]}", StructureFormat.Json);
        host.View.Sync(); await host.Drain();
        var root = Assert.Single(DataItems(Assert.Single(DataItems(host.View.Tree))));
        root.IsExpanded = true; await host.Drain();
        var old = Assert.Single(DataItems(root));
        host.Gate.Arm();
        try
        {
            old.IsExpanded = true;
            await host.Gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            host.Change("{\"fresh\":[9]}"); host.View.Sync();
            Assert.Empty(host.View.Tree.Items);
            Assert.Equal(0, host.View.CachedItems);
        }
        finally { host.Gate.Release(); }
        await host.Drain();
        root = Assert.Single(DataItems(Assert.Single(DataItems(host.View.Tree))));
        root.IsExpanded = true; await host.Drain();
        var fresh = Assert.Single(DataItems(root));
        Assert.NotSame(old, fresh);
        Assert.Contains("fresh", AutomationProperties.GetName(fresh), StringComparison.Ordinal);
        fresh.IsExpanded = true; await host.Drain();
        var value = Assert.Single(DataItems(fresh));
        Assert.EndsWith(" 9", AutomationProperties.GetName(value), StringComparison.Ordinal);
        Assert.Equal(Node(fresh).Id, Node(value).ParentId);
        Bound(host.View);
    }

    private sealed class ReadGate : IDisposable
    {
        private int armed;
        private readonly ManualResetEventSlim released = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() => Volatile.Write(ref armed, 1);
        public void Release() => released.Set();
        public void BeforeRead()
        {
            if (Interlocked.Exchange(ref armed, 0) == 0) return;
            Assert.False(Dispatcher.UIThread.CheckAccess());
            Entered.TrySetResult();
            if (!released.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("The delayed source read was not released.");
        }
        public void Dispose() => released.Dispose();
    }

    private sealed class GatedSnapshot(IByteSnapshot source, ReadGate gate) : IByteSnapshot
    {
        public long ByteLength => source.ByteLength;
        public long RetainedBytes => source.RetainedBytes;
        public int Read(long offset, Span<byte> destination) { gate.BeforeRead(); return source.Read(offset, destination); }
    }

    private sealed class Host : IDisposable
    {
        private readonly List<Doc> documents = [];
        private readonly StructureFormat format;
        private readonly TextCodec codec;
        public Doc Document { get; private set; } = null!;
        public StructureView View { get; }
        public Window Window { get; }
        public ReadGate Gate { get; } = new();
        public long Offset;
        public ByteRange? Jumped;
        public string? Copied, PromptValue;
        public string Text => Document.Codec.GetString(Document.ReadBytes(new(Document.BomLength, Document.ByteLength)));

        public Host(string text, StructureFormat format, TextCodec? codec = null)
        {
            this.format = format; this.codec = codec ?? TextCodec.Utf8;
            Change(text);
            View = new(Source, value => { Copied = value; return Task.CompletedTask; }, (_, _, _) => Task.FromResult(PromptValue));
            Window = new Window { Width = 900, Height = 900, Content = View };
            Window.Show(); Layout();
        }

        public void Change(string text)
        {
            byte[] bytes = [.. codec.Bom, .. codec.Encode(text)];
            Document = Doc.FromBytes(bytes, new PagedBackendFactory(startIndexing: false), new DocumentOptions { Format = codec });
            documents.Add(Document); Offset = Document.BomLength;
        }

        private StructureSource Source()
        {
            var document = Document;
            return new(document, document.Id, document.Revision, document.Codec, document.BomLength,
                format, Offset, false, () =>
                {
                    var snapshot = document.GetSnapshot();
                    return new(snapshot.DocumentId, snapshot.Revision, new GatedSnapshot(snapshot, Gate), snapshot.Codec);
                }, range =>
                {
                    if (!ReferenceEquals(Document, document)) return;
                    Jumped = range; Offset = range.Start; View.Sync();
                }, (edit, revision, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(Document, document)) return Task.FromResult(false);
                    bool applied = document.Apply([edit], new EditOptions { BaseRevision = revision }).Applied;
                    if (applied) View.Sync();
                    return Task.FromResult(applied);
                });
        }

        private void Layout()
        {
            Dispatcher.UIThread.RunJobs(); Window.UpdateLayout();
            using var frame = Window.CaptureRenderedFrame(); Assert.NotNull(frame);
        }

        public async Task Drain()
        {
            var until = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < until)
            {
                Layout();
                Task current = View.Working;
                if (current.IsCompleted)
                {
                    await current; Layout();
                    if (ReferenceEquals(current, View.Working)) return;
                }
                await Task.Delay(10);
            }
            Assert.Fail("The structure cache fixture did not finish.");
        }

        public void Dispose()
        {
            Gate.Release(); View.Dispose(); Window.Close();
            foreach (var document in documents) document.Dispose();
            Gate.Dispose();
        }
    }
}
