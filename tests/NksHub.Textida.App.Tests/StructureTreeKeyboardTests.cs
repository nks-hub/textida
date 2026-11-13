using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Shell;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class StructureTreeKeyboardTests
{
    [AvaloniaFact]
    public async Task Header_click_then_one_Left_collapses_with_inner_border_focus_and_keeps_parent_navigation()
    {
        using var host = new Host();
        host.View.Sync(); await host.Drain();
        var document = Assert.Single(DataItems(host.View.Tree));
        var root = Assert.Single(DataItems(document));
        host.ClickHeader(root);
        host.Press(Key.Right); await host.Drain();
        var branch = Assert.Single(DataItems(root));
        Assert.Equal(StructureKind.JsonObject, Node(branch).Kind);

        host.ClickHeader(branch);
        host.Press(Key.Right); await host.Drain();
        var leaf = Assert.Single(DataItems(branch));
        Assert.Equal(StructureKind.JsonNumber, Node(leaf).Kind);
        Assert.True(branch.IsExpanded);
        var focused = Assert.IsType<Border>(host.Window.FocusManager!.GetFocusedElement());
        Assert.Equal("PART_LayoutRoot", focused.Name);
        Assert.Same(branch, focused.GetVisualAncestors().OfType<TreeViewItem>().First());
        Assert.True(branch.IsKeyboardFocusWithin);
        Assert.False(branch.IsFocused);
        Assert.Same(branch, host.View.Tree.SelectedItem);
        object? actualLeftSource = null;
        host.Window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Left) actualLeftSource = e.Source;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        host.Press(Key.Left); await host.Drain();
        Assert.Same(focused, actualLeftSource);
        Assert.False(branch.IsExpanded);
        Assert.Same(branch, host.View.Tree.SelectedItem);
        host.Press(Key.Right); await host.Drain();
        Assert.True(branch.IsExpanded);
        Assert.Same(leaf, Assert.Single(DataItems(branch)));

        host.ClickHeader(leaf);
        host.Press(Key.Left); await host.Drain();
        Assert.Same(branch, host.View.Tree.SelectedItem);
        Assert.True(branch.IsExpanded);
        host.ClickHeader(branch);
        host.Press(Key.Left); await host.Drain();
        Assert.False(branch.IsExpanded);
        host.Press(Key.Left); await host.Drain();
        Assert.Same(root, host.View.Tree.SelectedItem);
    }

    private static StructureNode Node(TreeViewItem item) => Assert.IsType<StructureNode>(item.Tag);
    private static IEnumerable<TreeViewItem> DataItems(ItemsControl parent) => parent.Items.OfType<TreeViewItem>().Where(item => item.Tag is StructureNode);

    private sealed class Host : IDisposable
    {
        private readonly Doc document = Doc.FromBytes(TextCodec.Utf8.Encode("{\"branch\":{\"leaf\":1}}"),
            new PagedBackendFactory(startIndexing: false), new DocumentOptions { Format = TextCodec.Utf8 });
        private long offset;
        public StructureView View { get; }
        public Window Window { get; }

        public Host()
        {
            View = new(Source, _ => Task.CompletedTask, (_, _, _) => Task.FromResult<string?>(null));
            Window = new Window { Width = 900, Height = 900, Content = View };
            Window.Show(); Layout();
        }

        private StructureSource Source() => new(document, document.Id, document.Revision, document.Codec,
            document.BomLength, StructureFormat.Json, offset, false, document.GetSnapshot, Jump,
            (_, _, _) => Task.FromResult(false));

        private void Jump(ByteRange range) { offset = range.Start; View.Sync(); }

        private void Layout()
        {
            Dispatcher.UIThread.RunJobs(); Window.UpdateLayout();
            using var frame = Window.CaptureRenderedFrame();
            Assert.NotNull(frame);
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
            Assert.Fail("The structure worker did not finish.");
        }

        public void ClickHeader(TreeViewItem item)
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
            Layout();
            Assert.Same(item, View.Tree.SelectedItem);
        }

        public void Press(Key key) { TestApp.Press(Window, key); Layout(); }

        public void Dispose() { View.Dispose(); Window.Close(); document.Dispose(); }
    }
}
