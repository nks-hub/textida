using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace NksHub.Textida.App.Shell;

internal sealed partial class StructureTree
{
    private readonly Dictionary<StructureTreeNode, AutomationAnchor> automationAnchors = [];
    private Canvas? automationLayer;
    private TreeAutomationPeer? treeAutomationPeer;
    private long automationEpoch;

    internal int AutomationAnchorCount => automationAnchors.Count;

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        PrepareAutomation();
        return treeAutomationPeer = new TreeAutomationPeer(this);
    }

    private void PrepareAutomation()
    {
        RequestAutomation();
        if (automationLayer is not null) return;
        // These plain, zero-sized controls only provide Avalonia's visual-root mapping.
        // Model peers never use their bounds and the layer has no hit-test surface.
        automationLayer = new Canvas { Width = 0, Height = 0, IsHitTestVisible = false, Focusable = false };
        Children.Add(automationLayer);
        ProjectionChanged += (_, _) => AutomationProjectionChanged();
        SelectionChanged += (_, _) => AutomationSelectionChanged();
        LayoutUpdated += (_, _) =>
        {
            foreach (var anchor in automationAnchors.Values.ToArray()) anchor.Peer.RefreshLayout();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            automationEpoch++;
            foreach (var anchor in automationAnchors.Values) anchor.Release();
            automationAnchors.Clear();
            automationLayer.Children.Clear();
            treeAutomationPeer?.RefreshChildren();
        };
        AttachedToVisualTree += (_, _) => treeAutomationPeer?.RefreshChildren();
    }

    internal AutomationPeer? GetNodeAutomationPeer(StructureTreeNode node)
    {
        PrepareAutomation();
        if (!IsCurrent(node, node.BindingVersion) || TopLevel.GetTopLevel(this) is null) return null;
        if (automationAnchors.TryGetValue(node, out var anchor) && !anchor.IsCurrent)
        {
            ReleaseAnchor(node, anchor);
            anchor = null;
        }
        if (anchor is null)
        {
            anchor = new AutomationAnchor(this, node, automationEpoch);
            automationAnchors.Add(node, anchor);
            automationLayer!.Children.Add(anchor);
        }
        return anchor.Peer;
    }

    private void ReleaseAnchor(StructureTreeNode node, AutomationAnchor anchor)
    {
        anchor.Release();
        automationAnchors.Remove(node);
        automationLayer!.Children.Remove(anchor);
    }

    private void AutomationProjectionChanged()
    {
        foreach (var pair in automationAnchors.ToArray())
        {
            if (!pair.Value.IsCurrent) ReleaseAnchor(pair.Key, pair.Value);
            else pair.Value.Peer.RefreshModel();
        }
        treeAutomationPeer?.RefreshChildren();
    }

    private void AutomationSelectionChanged()
    {
        treeAutomationPeer?.SelectionChanged();
        foreach (var anchor in automationAnchors.Values.ToArray()) anchor.Peer.RefreshSelection();
    }

    private sealed class AutomationAnchor : Control
    {
        private bool released;
        public AutomationAnchor(StructureTree tree, StructureTreeNode node, long epoch)
        {
            Tree = tree; Node = node; Version = node.BindingVersion; Epoch = epoch;
            Width = Height = 0; IsHitTestVisible = Focusable = false;
            node.PropertyChanged += NodeChanged;
        }

        public StructureTree Tree { get; }
        public StructureTreeNode Node { get; }
        public long Version { get; }
        public long Epoch { get; }
        public bool IsCurrent => !released && Epoch == Tree.automationEpoch
            && TopLevel.GetTopLevel(Tree) is not null && Tree.IsCurrent(Node, Version);
        public NodeAutomationPeer Peer => (NodeAutomationPeer)ControlAutomationPeer.CreatePeerForElement(this)!;

        protected override AutomationPeer OnCreateAutomationPeer() => new NodeAutomationPeer(this);

        private void NodeChanged(object? sender, PropertyChangedEventArgs e) => Peer.RefreshModel();

        public void Release()
        {
            if (released) return;
            released = true;
            Node.PropertyChanged -= NodeChanged;
            if (ControlAutomationPeer.FromElement(this) is NodeAutomationPeer peer) peer.RefreshModel();
        }
    }

    private sealed class TreeAutomationPeer(StructureTree tree) : ControlAutomationPeer(tree), ISelectionProvider
    {
        public bool CanSelectMultiple => false;
        public bool IsSelectionRequired => false;

        public IReadOnlyList<AutomationPeer> GetSelection()
            => tree.SelectedNode is { } node && tree.GetNodeAutomationPeer(node) is { } peer ? [peer] : [];

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Tree;
        protected override string GetClassNameCore() => nameof(StructureTree);
        protected override bool IsKeyboardFocusableCore() => tree.IsEffectivelyEnabled;
        protected override bool HasKeyboardFocusCore() => tree.List.IsFocused;
        protected override bool IsEnabledCore() => TopLevel.GetTopLevel(tree) is not null && tree.IsEffectivelyEnabled;

        protected override IReadOnlyList<AutomationPeer> GetChildrenCore()
        {
            if (TopLevel.GetTopLevel(tree) is null) return [];
            return tree.Items.Select(tree.GetNodeAutomationPeer).OfType<AutomationPeer>().ToArray();
        }

        protected override object? GetProviderCore(Type providerType)
        {
            if (providerType == typeof(IScrollProvider))
            {
                var scroller = tree.List.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                return scroller is null ? null : CreatePeerForElement(scroller)?.GetProvider<IScrollProvider>();
            }
            return base.GetProviderCore(providerType);
        }

        protected override void SetFocusCore()
        {
            EnsureEnabled();
            if (tree.SelectedNode is { } selected) tree.SelectAndReveal(selected);
            else tree.List.Focus();
        }

        public void RefreshChildren() => InvalidateChildren();
        public void SelectionChanged()
            => RaisePropertyChangedEvent(SelectionPatternIdentifiers.SelectionProperty, null, GetSelection());
    }

    private sealed class NodeAutomationPeer : ControlAutomationPeer, ISelectionItemProvider, IExpandCollapseProvider
    {
        private readonly AutomationAnchor anchor;
        private string name;
        private bool selected;
        private ExpandCollapseState expanded;
        private StructureTreeRow? realizedRow;
        private Rect bounds;

        public NodeAutomationPeer(AutomationAnchor anchor) : base(anchor)
        {
            this.anchor = anchor;
            name = CurrentName;
            selected = IsSelected;
            expanded = ExpandCollapseState;
        }

        private StructureTree Tree => anchor.Tree;
        private StructureTreeNode Node => anchor.Node;
        private bool Current => anchor.IsCurrent;
        private bool Selectable => Current && Node.Kind == StructureTreeNodeKind.Normal;
        private string CurrentName => Current ? (Node.Heading + " " + Node.Value).TrimEnd() : "";
        public bool IsSelected => Selectable && ReferenceEquals(Tree.SelectedNode, Node);
        public ISelectionProvider? SelectionContainer => Current ? Tree.GetRootAutomationPeer().GetProvider<ISelectionProvider>() : null;
        public bool ShowsMenu => false;
        public ExpandCollapseState ExpandCollapseState => !Current || !Node.HasChildren || Node.Kind != StructureTreeNodeKind.Normal
            ? ExpandCollapseState.LeafNode : Node.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TreeItem;
        protected override string GetClassNameCore() => nameof(StructureTreeNode);
        protected override string GetNameCore() => CurrentName;
        protected override bool IsEnabledCore() => Current && Node.IsEnabled && Tree.IsEffectivelyEnabled;
        protected override bool IsKeyboardFocusableCore() => Selectable && IsEnabled();
        protected override bool HasKeyboardFocusCore() => Current && CurrentRow() is { IsKeyboardFocusWithin: true };
        protected override Rect GetBoundingRectangleCore() => CurrentBounds();
        protected override bool IsOffscreenCore()
        {
            if (!Current || CurrentRow() is not { IsEffectivelyVisible: true } row
                || CurrentBounds() is not { Width: > 0, Height: > 0 }) return true;
            return row.GetTransformedBounds() is not { Clip: { Width: > 0, Height: > 0 } };
        }

        protected override AutomationPeer? GetParentCore()
            => !Current ? null : Node.Parent is { } parent ? Tree.GetNodeAutomationPeer(parent) : Tree.GetRootAutomationPeer();

        protected override IReadOnlyList<AutomationPeer> GetChildrenCore()
        {
            if (!Current) return [];
            var children = new List<AutomationPeer>();
            // Keep actual template controls connected to their canonical row peer.
            // Loaded model children follow their logical parent, even outside the viewport.
            if (CurrentRow() is { } row)
                foreach (var visual in row.GetVisualChildren().OfType<Control>())
                    if (CreatePeerForElement(visual) is { } peer) children.Add(peer);
            if (Node.IsExpanded)
                foreach (var child in Node.Items)
                    if (Tree.GetNodeAutomationPeer(child) is { } peer) children.Add(peer);
            return children;
        }

        protected override object? GetProviderCore(Type providerType)
        {
            if (providerType == typeof(ISelectionItemProvider)) return Selectable ? this : null;
            if (providerType == typeof(IExpandCollapseProvider))
                return Current && Node.Kind == StructureTreeNodeKind.Normal && Node.HasChildren ? this : null;
            return base.GetProviderCore(providerType);
        }

        public void Select()
        {
            EnsureSelectable();
            Tree.SelectAndReveal(Node);
        }

        public void AddToSelection() => Select();

        public void RemoveFromSelection()
        {
            EnsureSelectable();
            if (IsSelected) Tree.SelectedNode = null;
        }

        public void Expand()
        {
            EnsureExpandable();
            Node.IsExpanded = true;
        }

        public void Collapse()
        {
            EnsureExpandable();
            Node.IsExpanded = false;
        }

        protected override void SetFocusCore() => Select();

        protected override void BringIntoViewCore()
        {
            EnsureEnabled();
            for (var parent = Node.Parent; parent is not null; parent = parent.Parent) parent.IsExpanded = true;
            Tree.RefreshProjection();
            if (Current) Tree.List.ScrollIntoView(Node);
        }

        private void EnsureSelectable()
        {
            EnsureEnabled();
            if (!Selectable) throw new InvalidOperationException("The tree node cannot be selected.");
        }

        private void EnsureExpandable()
        {
            EnsureEnabled();
            if (Node.Kind != StructureTreeNodeKind.Normal || !Node.HasChildren)
                throw new InvalidOperationException("The tree node cannot be expanded.");
        }

        private StructureTreeRow? CurrentRow()
            => Current && Tree.TryGetRealizedRow(Node) is { } row && row.BindingVersion == anchor.Version ? row : null;

        private Rect CurrentBounds()
        {
            if (CurrentRow() is not { } row || TopLevel.GetTopLevel(Tree) is not { } root
                || row.TransformToVisual(root) is not { } transform) return default;
            return new Rect(row.Bounds.Size).TransformToAABB(transform);
        }

        public void RefreshModel()
        {
            string nextName = CurrentName;
            if (name != nextName)
            {
                string previous = name; name = nextName;
                RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, previous, nextName);
            }
            var nextExpanded = ExpandCollapseState;
            if (expanded != nextExpanded)
            {
                var previous = expanded; expanded = nextExpanded;
                RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty, previous, nextExpanded);
            }
            InvalidateParent();
            InvalidateChildren();
            RefreshSelection();
            RefreshLayout();
        }

        public void RefreshSelection()
        {
            bool next = IsSelected;
            if (next == selected) return;
            bool previous = selected; selected = next;
            RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, previous, next);
        }

        public void RefreshLayout()
        {
            var nextRow = CurrentRow();
            if (!ReferenceEquals(nextRow, realizedRow))
            {
                realizedRow = nextRow;
                InvalidateChildren();
            }
            var nextBounds = CurrentBounds();
            if (nextBounds == bounds) return;
            var previous = bounds; bounds = nextBounds;
            RaisePropertyChangedEvent(AutomationElementIdentifiers.BoundingRectangleProperty, previous, nextBounds);
        }
    }
}

internal sealed partial class StructureTreeRow
{
    protected override AutomationPeer OnCreateAutomationPeer()
    {
        // Control caches its first peer. Stop container recycling before returning one.
        var tree = Owner ?? LastOwner;
        tree?.RequestAutomation();
        return Owner is not null && BoundNode is { } node && IsBindingCurrent
            && tree?.GetNodeAutomationPeer(node) is { } peer ? peer : new RetiredRowAutomationPeer(this);
    }

    private sealed class RetiredRowAutomationPeer(StructureTreeRow row) : ControlAutomationPeer(row)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TreeItem;
        protected override string GetNameCore() => "";
        protected override bool IsEnabledCore() => false;
        protected override bool IsOffscreenCore() => true;
        protected override bool HasKeyboardFocusCore() => false;
        protected override bool IsKeyboardFocusableCore() => false;
        protected override Rect GetBoundingRectangleCore() => default;
        protected override AutomationPeer? GetParentCore() => null;
        protected override IReadOnlyList<AutomationPeer> GetChildrenCore() => [];
        protected override void SetFocusCore() => EnsureEnabled();
        protected override void BringIntoViewCore() => EnsureEnabled();
    }

    private sealed partial class StructureExpander
    {
        protected override AutomationPeer OnCreateAutomationPeer()
        {
            var rowPeer = RowOwner is { } row ? ControlAutomationPeer.CreatePeerForElement(row) : null;
            return new ExpanderAutomationPeer(this, RowOwner, rowPeer);
        }

        internal void Invoke() => OnClick();
    }

    private sealed class ExpanderAutomationPeer : ControlAutomationPeer, IInvokeProvider
    {
        private readonly StructureExpander button;
        private readonly StructureTreeRow? row;
        private readonly StructureTreeNode? node;
        private readonly long version;
        private readonly AutomationPeer? rowPeer;

        public ExpanderAutomationPeer(StructureExpander button, StructureTreeRow? row, AutomationPeer? rowPeer) : base(button)
        {
            this.button = button; this.row = row; this.rowPeer = rowPeer;
            node = row?.BoundNode; version = row?.BindingVersion ?? 0;
            button.DetachedFromVisualTree += (_, _) => { InvalidateParent(); InvalidateChildren(); };
        }

        private bool Current => node is not null && row is { Owner: { } tree }
            && ReferenceEquals(row.BoundNode, node) && row.BindingVersion == version
            && TopLevel.GetTopLevel(button) is not null && tree.IsCurrent(node, version);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override string GetNameCore() => Current ? rowPeer?.GetName() ?? "" : "";
        protected override bool IsEnabledCore() => Current && button.IsEffectivelyEnabled;
        protected override bool IsOffscreenCore() => !Current || !button.IsEffectivelyVisible
            || button.GetTransformedBounds() is not { Clip: { Width: > 0, Height: > 0 } };
        protected override Rect GetBoundingRectangleCore() => Current ? base.GetBoundingRectangleCore() : default;
        protected override AutomationPeer? GetParentCore() => Current ? base.GetParentCore() : null;
        protected override IReadOnlyList<AutomationPeer> GetChildrenCore() => Current ? base.GetChildrenCore() ?? [] : [];

        protected override void SetFocusCore()
        {
            EnsureEnabled();
            rowPeer?.SetFocus();
        }

        protected override void BringIntoViewCore()
        {
            EnsureEnabled();
            rowPeer?.BringIntoView();
        }

        public void Invoke()
        {
            EnsureEnabled();
            button.Invoke();
        }
    }
}
