using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class StructureExpandPublicationTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Page_work_publishes_children_and_the_actual_source_caret_together(bool xml, bool insidePage)
    {
        using var host = new Host(xml);
        var (branch, _) = await host.Open();
        host.View.Tree.SelectedNode = branch;
        if (insidePage) host.Offset = Node(branch).ValueStart + (xml ? 0 : 1);
        long offset = host.Offset;
        int jumps = host.JumpCount;

        branch.IsExpanded = true;
        Task page = host.View.Working;
        await host.Drain();

        var children = DataItems(branch).ToArray();
        Assert.Equal(StructureView.PageSize, children.Length);
        Assert.All(children, child => Assert.Equal(Node(branch).Id, Node(child).ParentId));
        var expected = insidePage ? children[0] : branch;
        Assert.Same(expected, host.View.Tree.SelectedNode);
        Assert.True(expected.IsCaret);
        Assert.Equal(offset, host.Offset);
        Assert.Equal(jumps, host.JumpCount);
        Assert.Same(page, host.View.Working);
        Assert.True(page.IsCompletedSuccessfully);
        Assert.InRange(host.View.CachedItems, children.Length + 1, StructureView.MaximumItems);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_caret_moved_while_the_page_loads_is_not_replaced_by_the_old_page_target(bool xml)
    {
        using var host = new Host(xml);
        var (branch, other) = await host.Open();
        host.View.Tree.SelectedNode = branch;
        host.Gate.Arm();
        branch.IsExpanded = true;
        try
        {
            await host.Gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            host.Offset = Node(other).Start;
            host.View.Sync();
        }
        finally { host.Gate.Release(); }
        await host.Drain();

        Assert.Equal(StructureView.PageSize, DataItems(branch).Count());
        Assert.Same(other, host.View.Tree.SelectedNode);
        Assert.True(other.IsCaret);
        Assert.False(branch.IsCaret);
        Assert.Equal(Node(other).Start, host.Offset);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A_cancelled_or_replaced_source_cannot_publish_its_pending_page_or_caret(bool xml, bool replace)
    {
        using var host = new Host(xml);
        var (branch, _) = await host.Open();
        host.View.Tree.SelectedNode = branch;
        var oldTag = Node(branch);
        host.Gate.Arm();
        branch.IsExpanded = true;
        try
        {
            await host.Gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(host.View.Working.IsCompleted);
            if (replace)
            {
                host.Change(xml ? "<fresh/>" : "{\"fresh\":9}");
                host.View.Sync();
            }
            else host.View.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        finally { host.Gate.Release(); }
        await host.Drain();

        Assert.Empty(DataItems(branch));
        if (replace)
        {
            Assert.NotSame(branch, host.View.Tree.SelectedNode);
            Assert.False(branch.IsCaret);
            Assert.False(host.View.Tree.IsCurrent(branch, branch.BindingVersion));
        }
        else
        {
            Assert.Same(branch, host.View.Tree.SelectedNode);
            Assert.Equal(oldTag, Node(branch));
        }
    }

    private static StructureNode Node(StructureTreeNode item) => Assert.IsType<StructureNode>(item.Tag);
    private static IEnumerable<StructureTreeNode> DataItems(StructureTreeNode item) => item.Items.Where(child => child.Tag is StructureNode);

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
            if (!released.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("The pending page read was not released.");
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
        private readonly bool xml;
        private readonly List<Doc> documents = [];
        private Doc document = null!;
        public StructureView View { get; }
        public Window Window { get; }
        public ReadGate Gate { get; } = new();
        public long Offset;
        public int JumpCount;

        public Host(bool xml)
        {
            this.xml = xml;
            string values = string.Join(',', Enumerable.Range(11, 100));
            Change(xml ? "<root><branch>" + string.Concat(Enumerable.Range(11, 100).Select(i => "<n>" + i + "</n>"))
                + "</branch><other>9</other></root>" : "{\"branch\":[" + values + "],\"other\":9}");
            View = new(Source, _ => Task.CompletedTask, (_, _, _) => Task.FromResult<string?>(null));
            Window = new Window { Width = 900, Height = 900, Content = View };
            Window.Show(); Layout();
        }

        public void Change(string text)
        {
            document = Doc.FromBytes(TextCodec.Utf8.Encode(text), new PagedBackendFactory(startIndexing: false),
                new DocumentOptions { Format = TextCodec.Utf8 });
            documents.Add(document);
            Offset = document.BomLength;
        }

        private StructureSource Source()
        {
            var current = document;
            return new(current, current.Id, current.Revision, current.Codec, current.BomLength,
                xml ? StructureFormat.Xml : StructureFormat.Json, Offset, false, () =>
                {
                    var snapshot = current.GetSnapshot();
                    return new(snapshot.DocumentId, snapshot.Revision, new GatedSnapshot(snapshot, Gate), snapshot.Codec);
                }, range =>
                {
                    if (!ReferenceEquals(current, document)) return;
                    JumpCount++; Offset = range.Start; View.Sync();
                }, (_, _, _) => Task.FromResult(false));
        }

        public async Task<(StructureTreeNode Branch, StructureTreeNode Other)> Open()
        {
            View.Sync(); await Drain();
            var root = Assert.Single(DataItems(Assert.Single(View.Tree.Items)));
            root.IsExpanded = true; await Drain();
            var children = DataItems(root).ToArray();
            Assert.Equal(2, children.Length);
            Assert.StartsWith("branch", children[0].Heading, StringComparison.Ordinal);
            return (children[0], children[1]);
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
            Assert.Fail("The expanded-page fixture did not finish.");
        }

        public void Dispose()
        {
            Gate.Release(); View.Dispose(); Window.Close();
            foreach (var item in documents) item.Dispose();
            Gate.Dispose();
        }
    }
}
