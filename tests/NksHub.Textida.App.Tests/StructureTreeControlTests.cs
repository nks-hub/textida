using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Shell;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class StructureTreeControlTests
{
    [AvaloniaFact]
    public async Task Scrolling_256_models_recycles_rows_and_preserves_an_offscreen_selection()
    {
        var nodes = Nodes(256);
        using var host = new Host(nodes);
        host.Tree.SelectAndReveal(nodes[0]);
        await host.Until(() => host.Tree.TryGetRealizedRow(nodes[0]) is not null);
        Assert.True(host.Tree.TryGetRealizedRow(nodes[1])!.Focus());
        host.Tree.SelectedNode = nodes[0];
        Assert.True(host.Outside.Focus());
        var seen = host.Tree.GetRealizedRows().ToHashSet();
        Assert.InRange(seen.Count, 1, 63);
        for (int iteration = 0; iteration < 4; iteration++)
        {
            int index = iteration % 2 == 0 ? 255 : 0;
            host.Tree.List.ScrollIntoView(index);
            await host.Until(() => host.Tree.TryGetRealizedRow(nodes[index]) is not null);
            var realized = host.Tree.GetRealizedRows();
            Assert.InRange(realized.Count, 1, 63);
            seen.UnionWith(realized);
            Assert.Same(nodes[0], host.Tree.SelectedNode);
            Assert.Same(nodes[0], host.Tree.List.SelectedItem);
            if (index == 255) Assert.Null(host.Tree.TryGetRealizedRow(nodes[0]));
        }
        Assert.True(seen.Count < 128, $"Scrolling created {seen.Count} different row containers.");
        var first = Assert.IsType<StructureTreeRow>(host.Tree.TryGetRealizedRow(nodes[0]));
        Assert.True(first.IsSelected);
    }

    [AvaloniaFact]
    public async Task The_last_focused_row_stays_bounded_and_its_peer_reports_offscreen_geometry()
    {
        var nodes = Nodes(256);
        using var host = new Host(nodes);
        host.Tree.SelectAndReveal(nodes[0]); await host.Drain();
        var first = host.Tree.TryGetRealizedRow(nodes[0])!;
        host.Tree.List.ScrollIntoView(255);
        await host.Until(() => host.Tree.TryGetRealizedRow(nodes[^1]) is not null);
        Assert.Same(nodes[0], first.BoundNode); Assert.True(first.IsFocused);
        Assert.Same(nodes[0], host.Tree.SelectedNode);
        Assert.InRange(host.Tree.BoundRowCount, 1, 64);
        var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(first)!;
        Assert.True(peer.IsOffscreen());
        Assert.False(new Rect(host.Window.ClientSize).Intersects(peer.GetBoundingRectangle()));
        Assert.Equal("Node 0", peer.GetName());
    }

    [AvaloniaFact]
    public async Task Keyboard_navigation_realizes_neighbors_beyond_the_viewport()
    {
        var nodes = Nodes(256);
        using var host = new Host(nodes);
        host.Tree.SelectAndReveal(nodes[0]); await host.Drain();
        for (int i = 0; i < 40; i++) { host.Press(Key.Down); await host.Drain(); }
        Assert.Same(nodes[40], host.Tree.SelectedNode);
        AssertFocused(host, nodes[40]);
        host.Press(Key.End); await host.Drain();
        Assert.Same(nodes[255], host.Tree.SelectedNode); AssertFocused(host, nodes[255]);
        host.Press(Key.Up); await host.Drain();
        Assert.Same(nodes[254], host.Tree.SelectedNode);
        host.Press(Key.Home); await host.Drain();
        Assert.Same(nodes[0], host.Tree.SelectedNode);
        host.Press(Key.PageDown); await host.Drain();
        int afterPage = Array.IndexOf(nodes, host.Tree.SelectedNode);
        Assert.InRange(afterPage, 1, 20);
        AssertFocused(host, nodes[afterPage]);
        host.Press(Key.PageUp); await host.Drain();
        Assert.True(Array.IndexOf(nodes, host.Tree.SelectedNode) < afterPage);
    }

    [AvaloniaFact]
    public async Task Right_Left_and_collapse_keep_the_logical_parent_and_focus()
    {
        var root = Node("root"); var branch = Node("branch"); var leaf = Node("leaf");
        root.HasChildren = branch.HasChildren = true;
        root.Items.Add(branch); branch.Items.Add(leaf); root.IsExpanded = true;
        using var host = new Host([root]);
        host.Tree.SelectAndReveal(root); await host.Drain();
        host.Press(Key.Right); await host.Drain();
        Assert.Same(branch, host.Tree.SelectedNode);
        host.Press(Key.Right); await host.Drain();
        Assert.True(branch.IsExpanded);
        host.Press(Key.Right); await host.Drain();
        Assert.Same(leaf, host.Tree.SelectedNode);
        host.Press(Key.Left); await host.Drain();
        Assert.Same(branch, host.Tree.SelectedNode);
        host.Press(Key.Left); await host.Drain();
        Assert.False(branch.IsExpanded);
        host.Tree.SelectAndReveal(leaf); await host.Drain();
        branch.IsExpanded = false; await host.Drain();
        Assert.Same(branch, host.Tree.SelectedNode); AssertFocused(host, branch);
        host.Press(Key.Left); await host.Drain();
        Assert.Same(root, host.Tree.SelectedNode);
    }

    [AvaloniaFact]
    public async Task Double_clicking_a_branch_header_expands_its_loaded_children()
    {
        var branch = Node("branch"); branch.HasChildren = true;
        var child = Node("child"); branch.Items.Add(child);
        using var host = new Host([branch]);
        await host.Until(() => host.Tree.TryGetRealizedRow(branch) is not null);
        var header = host.Tree.TryGetRealizedRow(branch)!.HeaderVisual;
        var origin = Assert.IsType<Point>(header.TranslatePoint(default, host.Window));
        var point = origin + new Vector(Math.Min(20, header.Bounds.Width / 2), header.Bounds.Height / 2);
        for (int i = 0; i < 2; i++)
        {
            host.Window.MouseDown(point, MouseButton.Left); host.Window.MouseUp(point, MouseButton.Left);
        }
        await host.Drain();
        Assert.True(branch.IsExpanded); Assert.Same(branch, host.Tree.SelectedNode);
        Assert.NotNull(host.Tree.TryGetRealizedRow(child));
    }

    [AvaloniaFact]
    public async Task Reparenting_and_removal_do_not_leave_a_selected_or_current_old_node()
    {
        var first = Node("first"); var second = Node("second"); var child = Node("child");
        first.IsExpanded = second.IsExpanded = first.HasChildren = second.HasChildren = true;
        first.Items.Add(child);
        using var host = new Host([first, second]);
        host.Tree.SelectAndReveal(child); await host.Drain();
        second.Items.Add(child); await host.Drain();
        Assert.Empty(first.Items); Assert.Same(second, child.Parent);
        Assert.Same(child, host.Tree.SelectedNode);
        long version = child.BindingVersion;
        second.Items.Remove(child); await host.Drain();
        Assert.Null(child.Parent);
        Assert.False(host.Tree.IsCurrent(child, version));
        Assert.Same(second, host.Tree.SelectedNode);
        host.Tree.Items.Remove(second); await host.Drain();
        Assert.False(host.Tree.IsCurrent(second, second.BindingVersion));
        Assert.NotSame(second, host.Tree.SelectedNode);
    }

    [AvaloniaFact]
    public async Task Rebinding_before_deferred_focus_cannot_focus_the_old_identity()
    {
        var nodes = Nodes(256);
        using var host = new Host(nodes);
        host.Tree.SelectAndReveal(nodes[0]); await host.Drain();
        long previous = nodes[255].BindingVersion;
        host.Tree.SelectAndReveal(nodes[255]);
        nodes[255].Update("replacement", "", new object(), expandable: false);
        Assert.False(host.Tree.IsCurrent(nodes[255], previous));
        host.Tree.SelectAndReveal(nodes[1]); await host.Drain();
        Assert.Same(nodes[1], host.Tree.SelectedNode); AssertFocused(host, nodes[1]);
        var rebound = host.Tree.TryGetRealizedRow(nodes[255]);
        if (rebound is not null) Assert.Equal(nodes[255].BindingVersion, rebound.BindingVersion);
    }

    [AvaloniaFact]
    public async Task Placeholder_and_pager_do_not_consume_tree_activation_or_button_Enter()
    {
        var first = Node("first"); var last = Node("last");
        var loading = new StructureTreeNode { Heading = "Loading", Kind = StructureTreeNodeKind.Loading, IsEnabled = false };
        int clicks = 0, activations = 0;
        var button = new Button { Content = "More" };
        button.Click += (_, _) => clicks++;
        var pager = new StructureTreeNode { Kind = StructureTreeNodeKind.Pager, PagerContent = button };
        using var host = new Host([first, loading, last, pager]);
        host.Tree.NodeActivated += (_, _) => activations++;
        host.Tree.SelectAndReveal(first); await host.Drain();
        host.Press(Key.Down); await host.Drain();
        Assert.Same(last, host.Tree.SelectedNode);
        host.Press(Key.Enter); await host.Drain();
        Assert.Equal(1, activations);
        Assert.True(button.Focus());
        host.Press(Key.Enter); await host.Drain();
        Assert.Equal(1, clicks); Assert.Equal(1, activations);
    }

    [AvaloniaFact]
    public async Task Detaching_cancels_queued_focus_and_attaching_refreshes_changed_models()
    {
        var nodes = Nodes(256);
        using var host = new Host(nodes);
        host.Tree.SelectAndReveal(nodes[255]);
        host.Panel.Children.Remove(host.Tree);
        Assert.True(host.Outside.Focus());
        nodes[255].Invalidate();
        await host.Drain();
        Assert.False(host.Tree.IsCurrent(nodes[255], nodes[255].BindingVersion));
        host.Panel.Children.Insert(0, host.Tree); await host.Drain();
        Assert.True(host.Tree.IsCurrent(nodes[255], nodes[255].BindingVersion));
        Assert.Null(host.Tree.SelectedNode);
        Assert.DoesNotContain(host.Tree.GetRealizedRows(), row => row.IsFocused && ReferenceEquals(row.BoundNode, nodes[255]));
    }

    [AvaloniaFact]
    public void Updating_a_binding_publishes_complete_content_before_any_notification()
    {
        var node = Node("old"); node.Value = "old preview";
        var identity = new object(); long previous = node.BindingVersion;
        int notifications = 0;
        node.PropertyChanged += (_, _) =>
        {
            notifications++;
            Assert.Equal("new", node.Heading); Assert.Equal("new preview", node.Value);
            Assert.Same(identity, node.Tag); Assert.True(node.HasChildren);
            Assert.True(node.IsBindingValid); Assert.Equal(previous + 1, node.BindingVersion);
        };
        node.Update("new", "new preview", identity, expandable: true);
        Assert.True(notifications > 0);
    }

    [AvaloniaFact]
    public async Task Reset_rejects_old_callbacks_during_parent_and_child_collection_notifications()
    {
        var parent = Node("parent"); var node = Node("retired"); var child = Node("child");
        parent.Items.Add(node); node.Items.Add(child);
        parent.IsExpanded = node.IsExpanded = parent.HasChildren = node.HasChildren = true;
        using var host = new Host([parent]);
        host.Tree.SelectAndReveal(child); await host.Drain();
        long previous = node.BindingVersion, childVersion = child.BindingVersion;
        int notifications = 0;
        void CheckReset()
        {
            notifications++;
            Assert.False(node.IsBindingValid);
            Assert.False(host.Tree.IsCurrent(node, previous));
            Assert.False(host.Tree.IsCurrent(node, node.BindingVersion));
            Assert.False(host.Tree.IsCurrent(child, childVersion));
            Assert.Equal("", node.Heading); Assert.Equal("", node.Value);
            Assert.Null(node.Tag); Assert.False(node.HasChildren);
        }
        node.PropertyChanged += (_, _) => CheckReset();
        child.PropertyChanged += (_, _) => CheckReset();
        parent.Items.CollectionChanged += (_, _) => CheckReset();
        node.Items.CollectionChanged += (_, _) => CheckReset();
        node.Reset();
        Assert.True(notifications > 0); Assert.Empty(node.Items); Assert.Null(node.Parent);
        Assert.False(host.Tree.IsCurrent(node, node.BindingVersion));
    }

    [AvaloniaFact]
    public async Task A_detached_row_does_not_observe_a_model_update_and_refreshes_after_reattach()
    {
        var node = Node("old");
        using var host = new Host([node]);
        await host.Until(() => host.Tree.TryGetRealizedRow(node) is not null);
        var row = host.Tree.TryGetRealizedRow(node)!;
        host.Panel.Children.Remove(host.Tree);
        long detachedVersion = row.BindingVersion;
        var detachedNode = row.BoundNode;
        node.Update("replacement", "preview", new object(), expandable: false);
        Assert.Equal(detachedVersion, row.BindingVersion); Assert.Same(detachedNode, row.BoundNode);
        host.Panel.Children.Insert(0, host.Tree); await host.Drain();
        var current = Assert.IsType<StructureTreeRow>(host.Tree.TryGetRealizedRow(node));
        Assert.Equal(node.BindingVersion, current.BindingVersion); Assert.Same(node.Tag, current.Tag);
    }

    [AvaloniaFact]
    public async Task Middle_click_and_touch_pan_preserve_selection_while_touch_tap_selects_on_release()
    {
        var nodes = Nodes(256);
        using var host = new Host(nodes);
        host.Tree.SelectAndReveal(nodes[0]); await host.Drain();
        var point = HeaderPoint(host, nodes[3]);
        host.Window.MouseDown(point, MouseButton.Middle); host.Window.MouseUp(point, MouseButton.Middle);
        Assert.Same(nodes[0], host.Tree.SelectedNode);
        using (var touch = host.Window.TouchBegin(point))
        {
            Assert.Same(nodes[0], host.Tree.SelectedNode);
            host.Window.TouchEnd(touch, point);
        }
        await host.Drain(); Assert.Same(nodes[3], host.Tree.SelectedNode);
        host.Tree.SelectAndReveal(nodes[0]); await host.Drain();
        var scroll = host.Tree.List.GetVisualDescendants().OfType<ScrollViewer>().Single();
        double before = scroll.Offset.Y;
        point = HeaderPoint(host, nodes[6]);
        using (var touch = host.Window.TouchBegin(point))
        {
            Assert.Same(nodes[0], host.Tree.SelectedNode);
            host.Window.TouchMove(touch, point - new Vector(0, 30));
            host.Window.TouchMove(touch, point - new Vector(0, 70));
            host.Window.TouchMove(touch, point - new Vector(0, 110));
            host.Window.TouchEnd(touch, point - new Vector(0, 110));
        }
        await host.Drain();
        Assert.True(scroll.Offset.Y > before); Assert.Same(nodes[0], host.Tree.SelectedNode);
    }

    [AvaloniaFact]
    public async Task Revealing_a_deep_path_scrolls_its_header_into_the_horizontal_viewport()
    {
        var root = Node("root"); var leaf = root;
        for (int depth = 0; depth < 28; depth++)
        {
            var child = Node($"Level {depth + 1}");
            leaf.HasChildren = true; leaf.Items.Add(child); leaf = child;
        }
        using var host = new Host([root]);
        host.Tree.SelectAndReveal(leaf); await host.Drain();
        AssertFocused(host, leaf);
        var header = host.Tree.TryGetRealizedRow(leaf)!.HeaderVisual;
        var position = Assert.IsType<Point>(header.TranslatePoint(default, host.Tree.List));
        Assert.InRange(position.X, -1, host.Tree.List.Bounds.Width - 32);
        Assert.InRange(position.Y, -1, host.Tree.List.Bounds.Height - 1);
    }

    private static Point HeaderPoint(Host host, StructureTreeNode node)
    {
        var header = Assert.IsType<StructureTreeRow>(host.Tree.TryGetRealizedRow(node)).HeaderVisual;
        var origin = Assert.IsType<Point>(header.TranslatePoint(default, host.Window));
        return origin + new Vector(Math.Min(20, header.Bounds.Width / 2), header.Bounds.Height / 2);
    }

    private static StructureTreeNode Node(string name) => new() { Heading = name, Tag = new object() };
    private static StructureTreeNode[] Nodes(int count)
        => Enumerable.Range(0, count).Select(index => Node($"Node {index}")).ToArray();

    private static void AssertFocused(Host host, StructureTreeNode node)
    {
        var row = Assert.IsType<StructureTreeRow>(host.Tree.TryGetRealizedRow(node));
        var focus = Assert.IsAssignableFrom<Visual>(host.Window.FocusManager!.GetFocusedElement());
        Assert.True(ReferenceEquals(row, focus) || row.IsVisualAncestorOf(focus));
    }

    private sealed class Host : IDisposable
    {
        public StructureTree Tree { get; } = new();
        public Button Outside { get; } = new() { Content = "Outside" };
        public Grid Panel { get; } = new() { RowDefinitions = new("*,Auto") };
        public Window Window { get; }

        public Host(IEnumerable<StructureTreeNode> roots)
        {
            Tree.Items.AddRange(roots);
            Grid.SetRow(Outside, 1); Panel.Children.Add(Tree); Panel.Children.Add(Outside);
            Window = new() { Width = 360, Height = 300, Content = Panel };
            Window.Show(); Layout();
        }

        public void Layout()
        {
            Dispatcher.UIThread.RunJobs(); Window.UpdateLayout();
            using var frame = Window.CaptureRenderedFrame();
            Assert.NotNull(frame);
        }

        public async Task Drain()
        {
            for (int i = 0; i < 3; i++) { Layout(); await Task.Delay(5); }
        }

        public async Task Until(Func<bool> predicate)
        {
            for (int i = 0; i < 200; i++)
            {
                Layout();
                if (predicate()) return;
                await Task.Delay(10);
            }
            Assert.Fail("The virtual tree did not realize the requested viewport.");
        }

        public void Press(Key key) => TestApp.Press(Window, key);
        public void Dispose() => Window.Close();
    }
}
