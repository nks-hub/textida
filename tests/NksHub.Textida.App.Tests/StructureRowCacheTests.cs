using System.Globalization;
using System.Reflection;
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
    private static StructureNode Node(StructureTreeNode item) => Assert.IsType<StructureNode>(item.Tag);
    private static IEnumerable<StructureTreeNode> DataItems(StructureTreeNode parent) => parent.Items.Where(item => item.Tag is StructureNode);
    private static IEnumerable<StructureTreeNode> DataItems(StructureTree parent) => parent.Items.OfType<StructureTreeNode>().Where(item => item.Tag is StructureNode);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Button Pager(StructureTreeNode item, bool next) => Assert.IsType<WrapPanel>(
        item.Items.OfType<StructureTreeNode>().Single(child => child.Kind == StructureTreeNodeKind.Pager).PagerContent).Children.OfType<Button>().ElementAt(next ? 1 : 0);

    private static int ActiveItems(StructureTree parent) => parent.Items.OfType<StructureTreeNode>().Sum(item => 1 + ActiveItems(item));

    private static void Bound(StructureView view)
    {
        Assert.InRange(ActiveItems(view.Tree), 1, StructureView.MaximumItems);
        Assert.InRange(view.CachedItems, ActiveItems(view.Tree), StructureView.MaximumItems);
    }

    private static int ActiveItems(StructureTreeNode parent) => parent.Items.Sum(item => 1 + ActiveItems(item));

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
                host.View.Tree.SelectedNode = branch; await host.Drain();
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
    [Trait("Category", "Perf")]
    public async Task Counting_a_populated_cache_allocates_at_most_1024_bytes_per_query()
    {
        string values = "[" + string.Join(',', Enumerable.Range(0, 2048)) + "]";
        string text = "[" + string.Join(',', Enumerable.Repeat(values, 32)) + "]";
        using var host = new Host(text, StructureFormat.Json);
        host.View.Sync(); await host.Drain();
        var array = Assert.Single(DataItems(Assert.Single(DataItems(host.View.Tree))));
        array.IsExpanded = true; await host.Drain();
        var branches = DataItems(array).ToArray();
        Assert.Equal(32, branches.Length);
        foreach (var branch in branches)
        {
            host.View.Tree.SelectedNode = branch;
            branch.IsExpanded = true; await host.Drain();
            Assert.Equal(StructureView.PageSize, DataItems(branch).Count());
            Bound(host.View);
            branch.IsExpanded = false; await host.Drain();
        }

        Bound(host.View);
        int activeItems = ActiveItems(host.View.Tree);
        Assert.InRange(activeItems, 192, StructureView.MaximumItems);
        var pool = Assert.IsAssignableFrom<System.Collections.ICollection>(typeof(StructureView)
            .GetField("freeRows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host.View));
        int expected = activeItems + pool.Count;
        Assert.InRange(expected, activeItems, StructureView.MaximumItems);
        Assert.Equal(expected, host.View.CachedItems);
        using (host.View.Tree.DeferRefresh())
        {
            var loading = new StructureTreeNode { Kind = StructureTreeNodeKind.Loading };
            var pager = new StructureTreeNode { Kind = StructureTreeNodeKind.Pager };
            branches[^1].Items.Add(loading);
            Assert.Equal(expected + 1, host.View.CachedItems);
            branches[^1].Items.Add(pager);
            Assert.Equal(expected + 2, host.View.CachedItems);
            branches[^1].Items.Remove(loading);
            Assert.Equal(expected + 1, host.View.CachedItems);
            branches[^1].Items.Remove(pager);
            Assert.Equal(expected, host.View.CachedItems);
        }
        for (int i = 0; i < 32; i++) _ = host.View.CachedItems;

        const int queries = 1000;
        long checksum = 0;
        int thread = Environment.CurrentManagedThreadId;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < queries; i++) checksum += host.View.CachedItems;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(thread, Environment.CurrentManagedThreadId);
        Assert.Equal((long)expected * queries, checksum);
        Assert.Equal(expected, host.View.CachedItems);
        output.WriteLine(FormattableString.Invariant($"cache-items={expected}; active-items={activeItems}; queries={queries}; allocated-bytes={allocated}; bytes-per-query={allocated / (double)queries:0.000}; limit-bytes-per-query=1024"));
        Assert.InRange(allocated, 0, queries * 1024L);
    }

    [AvaloniaFact]
    public async Task Rotating_32_branches_reuses_models_and_keeps_exact_tags_parents_and_pages()
    {
        string text = "[" + string.Join(',', Enumerable.Range(0, 32).Select(group => "["
            + string.Join(',', Enumerable.Range(0, 135).Select(i => group * 1000 + i)) + "]")) + "]";
        using var host = new Host(text, StructureFormat.Json);
        host.View.Sync(); await host.Drain();
        var array = Assert.Single(DataItems(Assert.Single(DataItems(host.View.Tree))));
        array.IsExpanded = true; await host.Drain();
        var branches = DataItems(array).ToArray();
        Assert.Equal(32, branches.Length);
        var identities = new Dictionary<StructureTreeNode, long>();
        int reused = 0;
        foreach (var branch in branches.Concat(branches))
        {
            host.View.Tree.SelectedNode = branch;
            branch.IsExpanded = true; await host.Drain();
            var children = DataItems(branch).ToArray();
            Assert.Equal(Enumerable.Range(0, StructureView.PageSize).Select(i => (long)i), children.Select(item => Node(item).Ordinal));
            foreach (var leaf in children)
            {
                var node = Node(leaf);
                if (identities.TryGetValue(leaf, out var old) && old != node.Id) reused++;
                identities[leaf] = node.Id;
                Assert.Same(branch, leaf.Parent);
                Assert.Equal(Node(branch).Id, node.ParentId);
                Assert.False(leaf.IsExpanded); Assert.False(leaf.IsCaret);
                Assert.Empty(leaf.Items);
                string value = (Node(branch).Ordinal * 1000 + node.Ordinal).ToString(CultureInfo.InvariantCulture);
                Assert.EndsWith(" " + value, leaf.DisplayName, StringComparison.Ordinal);
                Assert.Contains("[" + node.Ordinal + "]", leaf.DisplayName, StringComparison.Ordinal);
            }
            Bound(host.View);
            Assert.InRange(host.View.Tree.GetRealizedRows().Count, 1, 63);
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
        host.View.Tree.SelectedNode = selected;
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
    public async Task Recycled_models_restore_branch_expansion_and_return_to_leaf_rows()
    {
        string text = "[" + string.Join(',', Enumerable.Range(0, 192).Select(i => i is >= 64 and < 128 ? "[1]" : i.ToString(CultureInfo.InvariantCulture))) + "]";
        using var host = new Host(text, StructureFormat.Json);
        host.View.Sync(); await host.Drain();
        var array = Assert.Single(DataItems(Assert.Single(DataItems(host.View.Tree))));
        array.IsExpanded = true; await host.Drain();
        var leaves = DataItems(array).ToArray();
        Assert.Equal(StructureView.PageSize, leaves.Length);
        Assert.All(leaves, item => Assert.False(item.HasChildren));
        host.View.Tree.SelectAndReveal(array);
        Click(Pager(array, next: true)); await host.Drain();
        var branches = DataItems(array).ToArray();
        Assert.Equal(Enumerable.Range(64, 64).Select(i => (long)i), branches.Select(item => Node(item).Ordinal));
        Assert.All(branches, item => Assert.True(item.HasChildren));
        Assert.InRange(branches.Count(item => leaves.Contains(item)), 1, StructureView.PageSize);
        var branch = branches[0];
        host.View.Tree.SelectAndReveal(branch); await host.Drain();
        Assert.True(host.View.Tree.TryGetRealizedRow(branch)!.ExpanderVisual.IsVisible);
        branch.IsExpanded = true; await host.Drain();
        var child = Assert.Single(DataItems(branch));
        Assert.Equal(Node(branch).Id, Node(child).ParentId); Assert.Same(branch, child.Parent);
        host.View.Tree.SelectAndReveal(array);
        Click(Pager(array, next: true)); await host.Drain();
        var reused = DataItems(array).ToArray();
        Assert.Equal(Enumerable.Range(128, 64).Select(i => (long)i), reused.Select(item => Node(item).Ordinal));
        Assert.True(reused.Count(item => leaves.Contains(item)) >= StructureView.PageSize - 1);
        Assert.All(reused, item =>
        {
            Assert.False(item.HasChildren); Assert.False(item.IsExpanded);
            Assert.Equal(Node(array).Id, Node(item).ParentId); Assert.Same(array, item.Parent);
            Assert.Empty(item.Items);
        });
        host.View.Tree.SelectAndReveal(reused[0]); await host.Drain();
        Assert.False(host.View.Tree.TryGetRealizedRow(reused[0])!.ExpanderVisual.IsVisible);
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
        host.View.Tree.SelectedNode = attribute;
        Assert.Equal(Node(attribute).Range, host.Jumped);
        Click(host.View.CopySourceButton); await host.Drain();
        Assert.Equal("名='é'", host.Copied);

        var up = host.View.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == Loc.Get("structure.up"));
        Click(up); await host.Drain();
        Assert.Equal(rootNode, Node(Assert.Single(DataItems(host.View.Tree))));
        host.Jumped = null;
        host.Offset = cdataNode.ValueStart; host.View.Sync(); await host.Drain();
        var revealed = Assert.IsType<StructureTreeNode>(host.View.Tree.SelectedNode);
        Assert.Equal(cdataNode, Node(revealed));
        Assert.Null(host.Jumped);
        Assert.True(revealed.IsCaret);
        Bound(host.View);

        var element = Assert.IsType<StructureTreeNode>(revealed.Parent);
        attribute = DataItems(element).Single(item => Node(item).Kind == StructureKind.XmlAttribute);
        host.View.Tree.SelectedNode = attribute;
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
        Assert.Contains("fresh", fresh.DisplayName, StringComparison.Ordinal);
        fresh.IsExpanded = true; await host.Drain();
        var value = Assert.Single(DataItems(fresh));
        Assert.EndsWith(" 9", value.DisplayName, StringComparison.Ordinal);
        Assert.Equal(Node(fresh).Id, Node(value).ParentId);
        Bound(host.View);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_observation_is_opt_in_and_waits_for_the_actual_worker_exit(bool observe)
    {
        using var host = new Host("[1,2,3]", StructureFormat.Json);
        if (observe) host.View.EnableCancellationObservation();
        Guid nonce = default;
        host.Gate.Arm(); host.View.Sync();
        try
        {
            await host.Gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(host.View.Working.IsCompleted);
            Click(host.View.CancelButton);
            await Task.Delay(20);
            Assert.False(host.View.Working.IsCompleted);
            if (observe)
            {
                var pending = Assert.IsType<StructureView.CancellationObservation>(host.View.ObservedCancellation);
                Assert.True(pending.WorkerWasActive); Assert.False(pending.WorkerCompleted);
                Assert.True(pending.RequestedQpc > 0); Assert.Equal(0, pending.WorkerStoppedQpc);
                Assert.NotEqual(Guid.Empty, pending.Nonce);
                nonce = pending.Nonce;
            }
            else Assert.Null(host.View.ObservedCancellation);
        }
        finally { host.Gate.Release(); }
        await host.Drain();
        if (!observe) { Assert.Null(host.View.ObservedCancellation); return; }
        var stopped = await CompletedCancellation(host);
        Assert.Equal(nonce, stopped.Nonce);
        Assert.True(stopped.WorkerStoppedQpc >= stopped.RequestedQpc);
        Assert.NotEqual(stopped.RequestedThreadId, stopped.WorkerThreadId);
        Assert.Equal(host.Document.Id, stopped.DocumentId); Assert.Equal(host.Document.Revision, stopped.Revision);
        Assert.Equal(host.View.SourceGeneration, stopped.Generation);
        host.Change("[9]");
        Assert.Null(host.View.ObservedCancellation);
        host.View.Sync(); await host.Drain();
        Assert.Null(host.View.ObservedCancellation);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_cancelled_page_and_queued_query_capture_only_their_own_source_generation(bool changeSource)
    {
        using var host = new Host("{\"old\":[1,2,3]}", StructureFormat.Json);
        host.View.EnableCancellationObservation(); host.View.Sync(); await host.Drain();
        var root = Assert.Single(DataItems(Assert.Single(DataItems(host.View.Tree))));
        root.IsExpanded = true; await host.Drain();
        var branch = Assert.Single(DataItems(root));
        Guid nonce = default;
        host.Gate.Arm();
        try
        {
            branch.IsExpanded = true;
            await host.Gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Task page = host.View.Working;
            host.View.Filter.Text = "2";
            Click(host.View.FindButton);
            Assert.NotSame(page, host.View.Working);
            Click(host.View.CancelButton);
            var pending = Assert.IsType<StructureView.CancellationObservation>(host.View.ObservedCancellation);
            nonce = pending.Nonce;
            Assert.False(pending.WorkerCompleted); Assert.Equal(0, pending.WorkerStoppedQpc);
            if (changeSource)
            {
                host.Change("{\"fresh\":9}"); host.View.Filter.Text = ""; host.View.Sync();
                Assert.Null(host.View.ObservedCancellation);
                Assert.True(host.View.SourceGeneration > pending.Generation);
            }
        }
        finally { host.Gate.Release(); }
        await host.Drain();
        if (!changeSource)
        {
            var stopped = await CompletedCancellation(host);
            Assert.Equal(nonce, stopped.Nonce); Assert.Equal(host.Document.Id, stopped.DocumentId);
            Assert.Equal(host.View.SourceGeneration, stopped.Generation);
            return;
        }
        Assert.Null(host.View.ObservedCancellation);
        root = Assert.Single(DataItems(Assert.Single(DataItems(host.View.Tree))));
        root.IsExpanded = true; await host.Drain();
        Assert.StartsWith("fresh", Assert.Single(DataItems(root)).Heading, StringComparison.Ordinal);
    }

    private static async Task<StructureView.CancellationObservation> CompletedCancellation(Host host)
    {
        for (int attempt = 0; attempt < 200 && host.View.ObservedCancellation?.WorkerCompleted != true; attempt++)
            await Task.Delay(5);
        var stopped = Assert.IsType<StructureView.CancellationObservation>(host.View.ObservedCancellation);
        Assert.True(stopped.WorkerCompleted); Assert.True(stopped.WorkerSucceeded);
        Assert.True(stopped.WorkerWasActive); Assert.False(stopped.WorkerOnDispatcher);
        Assert.True(stopped.WorkerStoppedQpc >= stopped.RequestedQpc);
        return stopped;
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
