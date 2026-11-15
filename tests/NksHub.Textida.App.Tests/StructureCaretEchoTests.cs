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
    private static IEnumerable<StructureTreeNode> DataItems(StructureTreeNode parent) => parent.Items.Where(item => item.Tag is StructureNode);
    private static IEnumerable<StructureTreeNode> DataItems(StructureTree parent) => parent.Items.OfType<StructureTreeNode>()
        .Where(item => item.Tag is StructureNode);
    private static StructureNode Node(StructureTreeNode item) => Assert.IsType<StructureNode>(item.Tag);

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Clicking_a_loaded_node_keeps_its_source_echo_synchronous_and_external_caret_moves_still_find_the_node(bool utf16)
    {
        using var host = new Host(utf16 ? TextCodec.Utf16BEBom : TextCodec.Utf8);
        var (alpha, delta) = await host.Open();
        Task previousWork = host.View.Working;
        host.Click(alpha);

        Assert.Equal(Node(alpha).Range, host.Jumped);
        Assert.Same(alpha, host.View.Tree.SelectedNode);
        Assert.Same(previousWork, host.View.Working);
        Assert.True(host.View.Working.IsCompleted, "The known selected node queued a redundant source-caret lookup.");
        Assert.True(alpha.IsCaret);

        int jumps = host.JumpCount;
        host.Offset = Node(delta).ValueStart;
        host.View.Sync(); await host.Drain();
        Assert.Same(delta, host.View.Tree.SelectedNode);
        Assert.Equal(jumps, host.JumpCount);
        Assert.True(delta.IsCaret);
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
        Assert.Same(delta, host.View.Tree.SelectedNode);
        Assert.Equal(1, host.JumpCount);
        Assert.True(delta.IsCaret);
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
        Assert.False(alpha.IsCaret);
        Assert.NotSame(alpha, host.View.Tree.SelectedNode);
        await host.Drain();

        Assert.NotSame(oldDocument, host.Document);
        var root = Assert.Single(DataItems(Assert.Single(DataItems(host.View.Tree))));
        root.IsExpanded = true; await host.Drain();
        var fresh = Assert.Single(DataItems(root));
        Assert.NotSame(alpha, fresh);
        Assert.Contains("fresh", fresh.DisplayName, StringComparison.Ordinal);
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

        public async Task<(StructureTreeNode Alpha, StructureTreeNode Delta)> Open()
        {
            View.Sync(); await Drain();
            var root = Assert.Single(DataItems(Assert.Single(DataItems(View.Tree))));
            root.IsExpanded = true; await Drain();
            var items = DataItems(root).ToArray();
            Assert.Equal(2, items.Length);
            Assert.StartsWith("alpha", items[0].DisplayName, StringComparison.Ordinal);
            return (items[0], items[1]);
        }

        public void Click(StructureTreeNode item)
        {
            Layout();
            var header = Assert.IsType<StructureTreeRow>(View.Tree.TryGetRealizedRow(item)).HeaderVisual;
            var inspected = StructureNativeDiagnostic.InspectHeader(Window, View.Tree, item);
            Assert.NotNull(inspected?["headerPoint"]);
            var position = inspected!["headerPoint"]!;
            var point = Window.PointToClient(new PixelPoint(position["x"]!.GetValue<int>(), position["y"]!.GetValue<int>()));
            var hit = Assert.IsAssignableFrom<Visual>(Window.InputHitTest(point));
            Assert.True(ReferenceEquals(header, hit) || header.IsVisualAncestorOf(hit));
            Assert.Same(item, hit.GetSelfAndVisualAncestors().OfType<StructureTreeRow>().First().BoundNode);
            var source = StructureNativeDiagnostic.ActualSource(View.Tree, hit);
            Assert.True(source["inHeader"]!.GetValue<bool>());
            Assert.Equal(Node(item).Id, source["nodeId"]!.GetValue<long>());
            Window.MouseDown(point, MouseButton.Left); Window.MouseUp(point, MouseButton.Left);
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
