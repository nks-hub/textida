using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Shell;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class StructureTreeAutomationTests
{
    [AvaloniaFact]
    public async Task Logical_children_and_parents_remain_a_tree_outside_the_viewport()
    {
        var root = Node("root"); root.HasChildren = true; root.IsExpanded = true;
        var children = Nodes(200); root.Items.AddRange(children);
        using var host = new Host([root]);
        Assert.Equal(0, host.Tree.AutomationAnchorCount);
        var treePeer = host.Peer;
        Assert.True(host.Tree.AutomationRequested);
        Assert.Equal(AutomationControlType.Tree, treePeer.GetAutomationControlType());
        Assert.NotNull(treePeer.GetProvider<IScrollProvider>());
        var rootPeer = Assert.Single(TreeItems(treePeer));
        Assert.Same(treePeer, rootPeer.GetParent());
        Assert.Equal(1, host.Tree.AutomationAnchorCount);
        var childPeers = TreeItems(rootPeer);
        Assert.Equal(200, childPeers.Length);
        Assert.Equal(201, host.Tree.AutomationAnchorCount);
        Assert.InRange(host.Tree.GetRealizedRows().Count, 1, 63);
        Assert.All(childPeers, peer => Assert.Same(rootPeer, peer.GetParent()));
        var last = childPeers[^1];
        Assert.True(last.IsOffscreen()); Assert.Equal(default, last.GetBoundingRectangle());
        last.BringIntoView();
        await host.Until(() => host.Tree.TryGetRealizedRow(children[^1]) is not null);
        Assert.Null(host.Tree.SelectedNode);
        Assert.Same(rootPeer, last.GetParent());
        Assert.True(rootPeer.IsOffscreen());
        Assert.False(last.IsOffscreen());
        AssertActualBounds(host, children[^1], last);
    }

    [AvaloniaFact]
    public async Task Offscreen_expand_selection_and_focus_use_the_current_model_identity()
    {
        var roots = Nodes(200);
        var parent = roots[^1]; parent.HasChildren = true;
        var child = Node("child"); parent.Items.Add(child);
        using var host = new Host(roots);
        var treePeer = host.Peer;
        var parentPeer = TreeItems(treePeer)[^1];
        Assert.True(parentPeer.IsOffscreen());
        var expand = Assert.IsAssignableFrom<IExpandCollapseProvider>(parentPeer.GetProvider<IExpandCollapseProvider>());
        expand.Expand();
        Assert.True(parent.IsExpanded);
        Assert.Null(host.Tree.SelectedNode);
        var childPeer = Assert.Single(TreeItems(parentPeer));
        var selection = Assert.IsAssignableFrom<ISelectionItemProvider>(childPeer.GetProvider<ISelectionItemProvider>());
        selection.Select();
        await host.Until(() => host.Tree.TryGetRealizedRow(child) is { IsFocused: true });
        Assert.Same(child, host.Tree.SelectedNode);
        Assert.True(selection.IsSelected);
        Assert.Same(treePeer.GetProvider<ISelectionProvider>(), selection.SelectionContainer);
        Assert.Same(childPeer, Assert.Single(treePeer.GetProvider<ISelectionProvider>()!.GetSelection()));
        var focused = Assert.IsAssignableFrom<Control>(host.Window.FocusManager!.GetFocusedElement());
        var focusPeer = ControlAutomationPeer.CreatePeerForElement(focused);
        Assert.Same(childPeer, focusPeer);
        Assert.Equal(AutomationControlType.TreeItem, focusPeer!.GetAutomationControlType());
        Assert.Same(parentPeer, focusPeer.GetParent());
        expand.Collapse(); await host.Drain();
        Assert.Same(parent, host.Tree.SelectedNode);
        Assert.True(childPeer.IsOffscreen());
        Assert.Empty(TreeItems(parentPeer));
        childPeer.BringIntoView();
        await host.Until(() => host.Tree.TryGetRealizedRow(child) is not null);
        Assert.True(parent.IsExpanded);
        Assert.Same(parent, host.Tree.SelectedNode);
    }

    [AvaloniaFact]
    public async Task A_cached_row_peer_never_moves_to_another_recycled_model()
    {
        var nodes = Nodes(256);
        using var host = new Host(nodes);
        var firstRow = Assert.IsType<StructureTreeRow>(host.Tree.TryGetRealizedRow(nodes[0]));
        var firstPeer = ControlAutomationPeer.CreatePeerForElement(firstRow)!;
        Assert.True(host.Tree.AutomationRequested);
        var canonical = TreeItems(host.Peer)[0];
        Assert.Same(canonical, firstPeer);
        var selection = Assert.IsAssignableFrom<ISelectionItemProvider>(firstPeer.GetProvider<ISelectionItemProvider>());
        selection.Select(); await host.Drain();
        Assert.True(host.Tree.TryGetRealizedRow(nodes[1])!.Focus());
        host.Tree.SelectedNode = nodes[0]; Assert.True(host.Outside.Focus());
        Assert.True(selection.IsSelected);
        host.Tree.List.ScrollIntoView(255);
        await host.Until(() => host.Tree.TryGetRealizedRow(nodes[^1]) is not null);
        Assert.Null(firstRow.BoundNode);
        Assert.True(firstPeer.IsOffscreen());
        Assert.Equal(default, firstPeer.GetBoundingRectangle());
        Assert.True(selection.IsSelected);
        Assert.Equal("Node 0", firstPeer.GetName());
        var lastRow = host.Tree.TryGetRealizedRow(nodes[^1])!;
        Assert.NotSame(firstRow, lastRow);
        Assert.NotSame(firstPeer, ControlAutomationPeer.CreatePeerForElement(lastRow));
        selection.Select();
        await host.Until(() => host.Tree.TryGetRealizedRow(nodes[0]) is { IsFocused: true });
        var newFirstRow = host.Tree.TryGetRealizedRow(nodes[0])!;
        Assert.NotSame(firstRow, newFirstRow);
        Assert.Same(firstPeer, ControlAutomationPeer.CreatePeerForElement(newFirstRow));
        AssertActualBounds(host, nodes[0], firstPeer);
    }

    [AvaloniaFact]
    public async Task Requesting_a_retired_unbound_row_peer_also_disables_reuse()
    {
        var nodes = Nodes(64);
        using var host = new Host(nodes);
        var oldRow = host.Tree.TryGetRealizedRow(nodes[0])!;
        Assert.False(host.Tree.AutomationRequested);
        host.Tree.Items.Clear(); await host.Drain();
        Assert.Null(oldRow.BoundNode);
        var retired = ControlAutomationPeer.CreatePeerForElement(oldRow)!;
        Assert.True(host.Tree.AutomationRequested);
        Assert.False(retired.IsEnabled()); Assert.True(retired.IsOffscreen());
        Assert.Null(retired.GetProvider<ISelectionItemProvider>());
        Assert.Equal(default, retired.GetBoundingRectangle());
        Assert.Throws<ElementNotEnabledException>(() => retired.SetFocus());
        Assert.Throws<ElementNotEnabledException>(() => retired.BringIntoView());
        var replacements = Nodes(64); host.Tree.Items.AddRange(replacements);
        await host.Until(() => host.Tree.TryGetRealizedRow(replacements[0]) is not null);
        Assert.Null(oldRow.BoundNode);
        Assert.DoesNotContain(oldRow, host.Tree.GetRealizedRows());
        Assert.NotSame(retired, ControlAutomationPeer.CreatePeerForElement(host.Tree.TryGetRealizedRow(replacements[0])!));
    }

    [AvaloniaFact]
    public async Task Requesting_an_expander_peer_first_protects_its_invoke_from_recycling()
    {
        var nodes = Nodes(256); nodes[0].HasChildren = nodes[^1].HasChildren = true;
        using var host = new Host(nodes);
        var oldRow = host.Tree.TryGetRealizedRow(nodes[0])!;
        Assert.False(host.Tree.AutomationRequested);
        var buttonPeer = ControlAutomationPeer.CreatePeerForElement(oldRow.ExpanderVisual)!;
        var invoke = buttonPeer.GetProvider<IInvokeProvider>()!;
        Assert.True(host.Tree.AutomationRequested);
        Assert.Contains(ControlAutomationPeer.CreatePeerForElement(oldRow), Parents(buttonPeer));
        for (var current = buttonPeer; current.GetParent() is { } parent; current = parent)
            Assert.Contains(current, parent.GetChildren());
        invoke.Invoke(); await host.Drain();
        Assert.True(nodes[0].IsExpanded);
        Assert.True(host.Tree.TryGetRealizedRow(nodes[1])!.Focus());
        host.Tree.SelectedNode = nodes[0]; Assert.True(host.Outside.Focus());
        host.Tree.List.ScrollIntoView(255);
        await host.Until(() => host.Tree.TryGetRealizedRow(nodes[^1]) is not null);
        Assert.Null(oldRow.BoundNode);
        Assert.False(buttonPeer.IsEnabled()); Assert.True(buttonPeer.IsOffscreen());
        Assert.Equal(default, buttonPeer.GetBoundingRectangle());
        Assert.Throws<ElementNotEnabledException>(() => invoke.Invoke());
        Assert.Throws<ElementNotEnabledException>(() => buttonPeer.SetFocus());
        Assert.Throws<ElementNotEnabledException>(() => buttonPeer.BringIntoView());
        Assert.False(nodes[^1].IsExpanded);
        host.Tree.List.ScrollIntoView(0);
        await host.Until(() => host.Tree.TryGetRealizedRow(nodes[0]) is not null);
        Assert.Throws<ElementNotEnabledException>(() => invoke.Invoke());
        Assert.True(nodes[0].IsExpanded);
    }

    [AvaloniaFact]
    public async Task Updating_a_visible_binding_retires_its_peer_and_focused_container()
    {
        var node = Node("before"); node.HasChildren = true;
        using var host = new Host([node]);
        var oldPeer = Assert.Single(TreeItems(host.Peer));
        var oldSelection = oldPeer.GetProvider<ISelectionItemProvider>()!;
        var oldExpand = oldPeer.GetProvider<IExpandCollapseProvider>()!;
        oldSelection.Select(); await host.Drain();
        var oldRow = host.Tree.TryGetRealizedRow(node)!;
        Assert.Same(oldPeer, ControlAutomationPeer.CreatePeerForElement(oldRow));
        node.Update("after", "value", new object(), expandable: true);
        await host.Drain();
        AssertStale(oldPeer, oldSelection, oldExpand);
        var newPeer = Assert.Single(TreeItems(host.Peer));
        Assert.NotSame(oldPeer, newPeer);
        Assert.Equal("after value", newPeer.GetName());
        newPeer.GetProvider<ISelectionItemProvider>()!.Select();
        await host.Until(() => host.Tree.TryGetRealizedRow(node) is { IsFocused: true });
        var newRow = host.Tree.TryGetRealizedRow(node)!;
        Assert.NotSame(oldRow, newRow);
        Assert.Same(newPeer, ControlAutomationPeer.CreatePeerForElement(newRow));
    }

    [AvaloniaFact]
    public async Task Eviction_reset_and_reinsertion_do_not_reactivate_old_actions()
    {
        var root = Node("root"); root.HasChildren = root.IsExpanded = true;
        var child = Node("old"); child.HasChildren = true; root.Items.Add(child);
        using var host = new Host([root]);
        var rootPeer = Assert.Single(TreeItems(host.Peer));
        var oldPeer = Assert.Single(TreeItems(rootPeer));
        var oldSelection = oldPeer.GetProvider<ISelectionItemProvider>()!;
        var oldExpand = oldPeer.GetProvider<IExpandCollapseProvider>()!;
        root.Items.Remove(child); await host.Drain();
        Assert.Equal(1, host.Tree.AutomationAnchorCount);
        AssertStale(oldPeer, oldSelection, oldExpand);
        child.Reset(); child.Update("replacement", "", new object(), expandable: false);
        root.Items.Add(child); await host.Drain();
        var replacement = Assert.Single(TreeItems(rootPeer));
        Assert.NotSame(oldPeer, replacement);
        Assert.Equal("replacement", replacement.GetName());
        AssertStale(oldPeer, oldSelection, oldExpand);
        replacement.GetProvider<ISelectionItemProvider>()!.Select();
        await host.Drain();
        Assert.Same(child, host.Tree.SelectedNode);
    }

    [AvaloniaFact]
    public async Task Detaching_releases_anchors_and_old_peers_stay_inert_after_reattachment()
    {
        var node = Node("node"); node.HasChildren = true;
        using var host = new Host([node]);
        var rootPeer = host.Peer;
        var oldPeer = Assert.Single(TreeItems(rootPeer));
        var selection = oldPeer.GetProvider<ISelectionItemProvider>()!;
        var expand = oldPeer.GetProvider<IExpandCollapseProvider>()!;
        selection.Select(); await host.Drain();
        Assert.Same(oldPeer, ControlAutomationPeer.CreatePeerForElement(host.Tree.TryGetRealizedRow(node)!));
        host.Panel.Children.Remove(host.Tree);
        Assert.True(host.Outside.Focus()); await host.Drain();
        Assert.Equal(0, host.Tree.AutomationAnchorCount);
        Assert.Empty(rootPeer.GetChildren());
        AssertStale(oldPeer, selection, expand);
        host.Panel.Children.Insert(0, host.Tree); await host.Drain();
        var newPeer = Assert.Single(TreeItems(rootPeer));
        Assert.NotSame(oldPeer, newPeer);
        AssertStale(oldPeer, selection, expand);
        newPeer.GetProvider<ISelectionItemProvider>()!.Select();
        await host.Until(() => host.Tree.TryGetRealizedRow(node) is { IsFocused: true });
        Assert.Same(newPeer, ControlAutomationPeer.CreatePeerForElement(host.Tree.TryGetRealizedRow(node)!));
    }

    [AvaloniaFact]
    public async Task Disabled_placeholder_and_pager_keep_their_actual_available_actions()
    {
        var disabled = Node("disabled"); disabled.IsEnabled = false; disabled.HasChildren = true;
        var loading = new StructureTreeNode { Heading = "Loading", Kind = StructureTreeNodeKind.Loading, IsEnabled = false };
        int clicks = 0;
        var more = new Button { Content = "More" }; more.Click += (_, _) => clicks++;
        var pager = new StructureTreeNode { Heading = "More", Kind = StructureTreeNodeKind.Pager, PagerContent = more };
        using var host = new Host([disabled, loading, pager]);
        var peers = TreeItems(host.Peer);
        var disabledSelection = peers[0].GetProvider<ISelectionItemProvider>()!;
        Assert.False(peers[0].IsEnabled());
        Assert.Throws<ElementNotEnabledException>(() => disabledSelection.Select());
        Assert.Null(peers[1].GetProvider<ISelectionItemProvider>());
        Assert.Null(peers[1].GetProvider<IExpandCollapseProvider>());
        Assert.Null(peers[2].GetProvider<ISelectionItemProvider>());
        Assert.Null(peers[2].GetProvider<IExpandCollapseProvider>());
        var buttonPeer = Descendants(peers[2]).Single(peer => peer.GetAutomationControlType() == AutomationControlType.Button);
        buttonPeer.GetProvider<IInvokeProvider>()!.Invoke(); await host.Drain();
        Assert.Equal(1, clicks);
        Assert.Null(host.Tree.SelectedNode);
        Assert.Contains(peers[2], Parents(buttonPeer));
        disabled.IsEnabled = true;
        disabledSelection.Select(); await host.Drain();
        Assert.Same(disabled, host.Tree.SelectedNode);
    }

    private static void AssertStale(AutomationPeer peer, ISelectionItemProvider selection, IExpandCollapseProvider expand)
    {
        Assert.False(peer.IsEnabled()); Assert.True(peer.IsOffscreen());
        Assert.Equal(default, peer.GetBoundingRectangle());
        Assert.Null(peer.GetParent()); Assert.Empty(peer.GetChildren());
        Assert.Throws<ElementNotEnabledException>(() => selection.Select());
        Assert.Throws<ElementNotEnabledException>(() => expand.Expand());
        Assert.Throws<ElementNotEnabledException>(() => peer.BringIntoView());
    }

    private static void AssertActualBounds(Host host, StructureTreeNode node, AutomationPeer peer)
    {
        var row = host.Tree.TryGetRealizedRow(node)!;
        var transform = row.TransformToVisual(host.Window)!.Value;
        Assert.Equal(new Rect(row.Bounds.Size).TransformToAABB(transform), peer.GetBoundingRectangle());
        Assert.True(peer.GetBoundingRectangle() is { Width: > 0, Height: > 0 });
    }

    private static AutomationPeer[] TreeItems(AutomationPeer peer)
        => peer.GetChildren().Where(child => child.GetAutomationControlType() == AutomationControlType.TreeItem).ToArray();

    private static IEnumerable<AutomationPeer> Descendants(AutomationPeer peer)
    {
        foreach (var child in peer.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static IEnumerable<AutomationPeer> Parents(AutomationPeer peer)
    {
        while (peer.GetParent() is { } parent) { yield return parent; peer = parent; }
    }

    private static StructureTreeNode Node(string name) => new() { Heading = name, Tag = new object() };
    private static StructureTreeNode[] Nodes(int count)
        => Enumerable.Range(0, count).Select(index => Node($"Node {index}")).ToArray();

    private sealed class Host : IDisposable
    {
        public StructureTree Tree { get; } = new();
        public Button Outside { get; } = new() { Content = "Outside" };
        public Grid Panel { get; } = new() { RowDefinitions = new("*,Auto") };
        public Window Window { get; }
        public AutomationPeer Peer => ControlAutomationPeer.CreatePeerForElement(Tree)!;

        public Host(IEnumerable<StructureTreeNode> roots)
        {
            Tree.Items.AddRange(roots);
            Grid.SetRow(Outside, 1); Panel.Children.Add(Tree); Panel.Children.Add(Outside);
            Window = new() { Width = 360, Height = 300, Content = Panel };
            Window.Show(); Layout();
        }

        private void Layout()
        {
            Dispatcher.UIThread.RunJobs(); Window.UpdateLayout();
            using var frame = Window.CaptureRenderedFrame(); Assert.NotNull(frame);
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
            Assert.Fail("The requested automation target did not become available.");
        }

        public void Dispose() => Window.Close();
    }
}
