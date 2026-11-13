using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class StructureCaretEchoTests
{
    private static IEnumerable<TreeViewItem> DataItems(ItemsControl parent) => parent.Items.OfType<TreeViewItem>()
        .Where(item => item.Tag is StructureNode);
    private static StructureNode Node(TreeViewItem item) => Assert.IsType<StructureNode>(item.Tag);

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Clicking_a_loaded_node_keeps_its_source_echo_synchronous_and_external_caret_moves_still_find_the_node(bool utf16)
    {
        using var host = new Host(utf16 ? TextCodec.Utf16BEBom : TextCodec.Utf8);
        var (alpha, delta) = await host.Open();
        host.Click(alpha);

        Assert.Equal(Node(alpha).Range, host.Jumped);
        Assert.Same(alpha, host.View.Tree.SelectedItem);
        Assert.True(host.View.Working.IsCompleted, "The known selected node queued a redundant source-caret lookup.");
        Assert.Equal(Loc.Get("structure.caret"), AutomationProperties.GetItemStatus(alpha));

        int jumps = host.JumpCount;
        host.Offset = Node(delta).ValueStart;
        host.View.Sync(); await host.Drain();
        Assert.Same(delta, host.View.Tree.SelectedItem);
        Assert.Equal(jumps, host.JumpCount);
        Assert.Equal(Loc.Get("structure.caret"), AutomationProperties.GetItemStatus(delta));
    }

    [AvaloniaFact]
    public async Task A_source_that_reports_a_different_offset_still_reveals_the_actual_caret_node()
    {
        using var host = new Host(TextCodec.Utf8);
        var (alpha, delta) = await host.Open();
        host.JumpOffset = Node(delta).ValueStart;
        host.Click(alpha);
        await host.Drain();

        Assert.Equal(Node(alpha).Range, host.Jumped);
        Assert.Same(delta, host.View.Tree.SelectedItem);
        Assert.Equal(1, host.JumpCount);
        Assert.Equal(Loc.Get("structure.caret"), AutomationProperties.GetItemStatus(delta));
    }

    [AvaloniaFact]
    public async Task A_source_change_during_jump_cannot_mark_the_retired_selected_row()
    {
        using var host = new Host(TextCodec.Utf8);
        var (alpha, _) = await host.Open();
        var oldDocument = host.Document;
        host.ChangeOnJump = "{\"fresh\":9}";
        host.Click(alpha);
        Assert.Empty(host.View.Tree.Items);
        Assert.NotEqual(Loc.Get("structure.caret"), AutomationProperties.GetItemStatus(alpha));
        Assert.NotSame(alpha, host.View.Tree.SelectedItem);
        await host.Drain();

        Assert.NotSame(oldDocument, host.Document);
        var root = Assert.Single(DataItems(Assert.Single(DataItems(host.View.Tree))));
        root.IsExpanded = true; await host.Drain();
        var fresh = Assert.Single(DataItems(root));
        Assert.NotSame(alpha, fresh);
        Assert.Contains("fresh", AutomationProperties.GetName(fresh), StringComparison.Ordinal);
        Assert.Equal(1, host.JumpCount);
    }

    private sealed class Host : IDisposable
    {
        private readonly TextCodec codec;
        private readonly List<Doc> documents = [];
        public Doc Document { get; private set; } = null!;
        public StructureView View { get; }
        public Window Window { get; }
        public long Offset;
        public long? JumpOffset;
        public ByteRange? Jumped;
        public int JumpCount;
        public string? ChangeOnJump;

        public Host(TextCodec codec)
        {
            this.codec = codec;
            Change("{\"alpha\":1,\"δ\":2}");
            View = new(Source, _ => Task.CompletedTask, (_, _, _) => Task.FromResult<string?>(null));
            Window = new Window { Width = 700, Height = 800, Content = View };
            Window.Show(); Layout();
        }

        private void Change(string text)
        {
            byte[] bytes = [.. codec.Bom, .. codec.Encode(text)];
            Document = Doc.FromBytes(bytes, new PagedBackendFactory(startIndexing: false), new DocumentOptions { Format = codec });
            documents.Add(Document);
            Offset = Document.BomLength;
        }

        private StructureSource Source()
        {
            var document = Document;
            return new(document, document.Id, document.Revision, document.Codec, document.BomLength,
                StructureFormat.Json, Offset, false, document.GetSnapshot, range =>
                {
                    if (!ReferenceEquals(Document, document)) return;
                    Jumped = range; JumpCount++;
                    if (ChangeOnJump is { } text)
                    {
                        ChangeOnJump = null; Change(text);
                    }
                    else Offset = Math.Max(document.BomLength, JumpOffset ?? range.Start);
                    View.Sync();
                }, (_, _, _) => Task.FromResult(false));
        }

        public async Task<(TreeViewItem Alpha, TreeViewItem Delta)> Open()
        {
            View.Sync(); await Drain();
            var root = Assert.Single(DataItems(Assert.Single(DataItems(View.Tree))));
            root.IsExpanded = true; await Drain();
            var items = DataItems(root).ToArray();
            Assert.Equal(2, items.Length);
            Assert.StartsWith("alpha", AutomationProperties.GetName(items[0]), StringComparison.Ordinal);
            return (items[0], items[1]);
        }

        public void Click(TreeViewItem item)
        {
            Layout();
            var header = Assert.IsAssignableFrom<Control>(item.Header);
            var label = header.GetVisualDescendants().OfType<TextBlock>().OrderByDescending(text => text.Bounds.Width).First();
            var point = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), Window);
            Assert.NotNull(point);
            var hit = Assert.IsAssignableFrom<Visual>(Window.InputHitTest(point.Value));
            Assert.True(ReferenceEquals(header, hit) || header.IsVisualAncestorOf(hit));
            Assert.Same(item, hit.GetSelfAndVisualAncestors().OfType<TreeViewItem>().First());
            Window.MouseDown(point.Value, MouseButton.Left); Window.MouseUp(point.Value, MouseButton.Left);
        }

        private void Layout()
        {
            Dispatcher.UIThread.RunJobs(); Window.UpdateLayout();
            using var frame = Window.CaptureRenderedFrame(); Assert.NotNull(frame);
        }

        public async Task Drain()
        {
            var until = DateTime.UtcNow.AddSeconds(15);
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
            Assert.Fail("The source-caret fixture did not finish.");
        }

        public void Dispose()
        {
            View.Dispose(); Window.Close();
            foreach (var document in documents) document.Dispose();
        }
    }
}
