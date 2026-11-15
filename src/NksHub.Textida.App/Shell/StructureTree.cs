using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NksHub.Textida.App.Shell;

internal sealed partial class StructureTree : Grid
{
    private readonly TreeList list;
    private readonly AvaloniaList<StructureTreeNode> projection = [];
    private readonly HashSet<StructureTreeNode> watched = [];
    private readonly HashSet<StructureTreeRow> boundRows = [];
    private readonly Dictionary<StructureTreeNode, StructureTreeNode?> previousParents = [];
    private StructureTreeNode? selectedNode;
    private long selectedVersion, focusRequest;
    private int suppressListSelection;
    private bool refreshing, refreshAgain, attached, observing = true, refreshQueued;

    public StructureTree()
    {
        list = new(this) { ItemsSource = projection };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        Children.Add(list);
        Items.CollectionChanged += RootsChanged;
        list.SelectionChanged += (_, _) =>
        {
            if (suppressListSelection != 0) return;
            if (list.SelectedItem is StructureTreeNode { Kind: not StructureTreeNodeKind.Normal }) SetSelected(selectedNode);
            else SetSelected(list.SelectedItem as StructureTreeNode);
        };
        AddHandler(KeyDownEvent, TreeKeyDown, RoutingStrategies.Tunnel);
    }

    public AvaloniaList<StructureTreeNode> Items { get; } = [];
    public StructureTreeNode? SelectedNode
    {
        get => selectedNode;
        set
        {
            if (value is not null && !IsSelectable(value)) throw new ArgumentException("The node is not selectable in this tree.", nameof(value));
            SetSelected(value);
        }
    }
    public event EventHandler? SelectionChanged;
    public event EventHandler? ProjectionChanged;
    public event EventHandler<StructureTreeNode>? NodeActivated;
    internal ListBox List => list;
    internal IReadOnlyList<StructureTreeNode> VisibleNodes => projection;
    internal bool AutomationRequested { get; private set; }
    internal int CreatedRowCount { get; private set; }
    internal int RecyclingRowCreationCount { get; private set; }
    internal int BoundRowCount => boundRows.Count;

    internal void RequestAutomation() => AutomationRequested = true;
    internal void RowBound(StructureTreeRow row) => boundRows.Add(row);
    internal void RowUnbound(StructureTreeRow row) => boundRows.Remove(row);
    internal AutomationPeer GetRootAutomationPeer() => ControlAutomationPeer.CreatePeerForElement(this)!;

    public void RefreshProjection()
    {
        if (refreshing) { refreshAgain = true; return; }
        refreshing = true;
        try
        {
            do
            {
                refreshAgain = false;
                RebuildProjection();
            } while (refreshAgain);
        }
        finally { refreshing = false; }
    }

    private void RootsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var root in Items.ToArray()) root.Parent?.Items.Remove(root);
        RefreshProjection();
    }

    private void RebuildProjection()
    {
        bool hadFocus = IsKeyboardFocusWithin;
        var originalSelection = selectedNode;
        var reachable = new HashSet<StructureTreeNode>();
        var visible = new List<StructureTreeNode>();
        foreach (var root in Items) Collect(root, true, reachable, visible);
        foreach (var old in watched)
            if (!reachable.Contains(old)) old.PropertyChanged -= NodeChanged;
        if (observing)
            foreach (var node in reachable)
                if (watched.Add(node)) node.PropertyChanged += NodeChanged;
        watched.IntersectWith(reachable);

        var wanted = selectedNode;
        if (wanted is not null && selectedVersion != wanted.BindingVersion) wanted = null;
        while (wanted is not null && (!visible.Contains(wanted) || !IsSelectable(wanted)))
            wanted = wanted.Parent ?? previousParents.GetValueOrDefault(wanted);

        suppressListSelection++;
        try
        {
            for (int index = 0; index < visible.Count; index++)
            {
                if (index < projection.Count && ReferenceEquals(projection[index], visible[index])) continue;
                int old = projection.IndexOf(visible[index]);
                if (old >= 0) projection.RemoveAt(old);
                projection.Insert(index, visible[index]);
            }
            while (projection.Count > visible.Count) projection.RemoveAt(projection.Count - 1);
            list.SelectedItem = wanted;
        }
        finally { suppressListSelection--; }
        previousParents.Clear();
        foreach (var node in reachable) previousParents[node] = node.Parent;
        SetSelected(wanted);
        ProjectionChanged?.Invoke(this, EventArgs.Empty);
        if (hadFocus && wanted is not null && !ReferenceEquals(originalSelection, wanted)) Reveal(wanted, focus: true);
    }

    private static void Collect(StructureTreeNode node, bool visible, HashSet<StructureTreeNode> reachable,
        List<StructureTreeNode> projection)
    {
        if (!reachable.Add(node)) throw new InvalidOperationException("A tree node occurs more than once.");
        if (visible && node.IsBindingValid) projection.Add(node);
        foreach (var child in node.Items) Collect(child, visible && node.IsBindingValid && node.IsExpanded, reachable, projection);
    }

    private void NodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StructureTreeNode.Parent) or nameof(StructureTreeNode.Items))
        {
            // Reparenting publishes a removal before the new parent is assigned.
            if (!refreshQueued)
            {
                refreshQueued = true;
                Dispatcher.UIThread.Post(() =>
                {
                    refreshQueued = false;
                    if (observing) RefreshProjection();
                }, DispatcherPriority.Loaded);
            }
            return;
        }
        if (AutomationRequested && e.PropertyName == nameof(StructureTreeNode.BindingVersion)
            && sender is StructureTreeNode changed)
        {
            int index = projection.IndexOf(changed);
            if (index >= 0)
            {
                suppressListSelection++;
                try { projection.RemoveAt(index); projection.Insert(index, changed); }
                finally { suppressListSelection--; }
            }
        }
        if (e.PropertyName is nameof(StructureTreeNode.IsExpanded) or nameof(StructureTreeNode.Items)
            or nameof(StructureTreeNode.Parent) or nameof(StructureTreeNode.BindingVersion)
            or nameof(StructureTreeNode.IsBindingValid) or nameof(StructureTreeNode.IsEnabled) or nameof(StructureTreeNode.Kind))
            RefreshProjection();
    }

    private bool IsSelectable(StructureTreeNode node)
        => node.IsEnabled && node.Kind == StructureTreeNodeKind.Normal && IsCurrent(node, node.BindingVersion);

    private void SetSelected(StructureTreeNode? node)
    {
        if (node is not null && !IsSelectable(node)) node = null;
        bool changed = !ReferenceEquals(selectedNode, node) || node is not null && selectedVersion != node.BindingVersion;
        selectedNode = node; selectedVersion = node?.BindingVersion ?? 0;
        if (!ReferenceEquals(list.SelectedItem, node))
        {
            suppressListSelection++;
            try { list.SelectedItem = node; }
            finally { suppressListSelection--; }
        }
        if (changed) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsCurrent(StructureTreeNode node, long version)
    {
        if (!observing || !node.IsBindingValid || node.BindingVersion != version) return false;
        for (var parent = node.Parent; parent is not null; parent = node.Parent)
        {
            if (!parent.IsBindingValid || !parent.Items.Contains(node)) return false;
            node = parent;
        }
        return Items.Contains(node);
    }

    public StructureTreeRow? TryGetRealizedRow(StructureTreeNode node)
    {
        int index = projection.IndexOf(node);
        return index >= 0 && list.ContainerFromIndex(index) is StructureTreeRow row
            && ReferenceEquals(row.BoundNode, node) && row.IsBindingCurrent ? row : null;
    }

    public IReadOnlyList<StructureTreeRow> GetRealizedRows()
        => list.GetRealizedContainers().OfType<StructureTreeRow>().Where(row => row.IsBindingCurrent).ToArray();

    public void SelectAndReveal(StructureTreeNode node, bool focus = true)
    {
        RefreshProjection();
        if (!IsSelectable(node)) return;
        for (var parent = node.Parent; parent is not null; parent = parent.Parent) parent.IsExpanded = true;
        RefreshProjection();
        int index = projection.IndexOf(node);
        if (index < 0) return;
        SetSelected(node);
        Reveal(node, focus);
    }

    private void Reveal(StructureTreeNode node, bool focus)
    {
        int index = projection.IndexOf(node);
        if (index < 0) return;
        long request = ++focusRequest, version = node.BindingVersion;
        list.ScrollIntoView(index);
        if (focus && !FocusRow(node, version, request))
            Dispatcher.UIThread.Post(() => FocusRow(node, version, request), DispatcherPriority.Loaded);
    }

    private bool FocusRow(StructureTreeNode node, long version, long request)
    {
        if (!attached || request != focusRequest || !IsCurrent(node, version) || !ReferenceEquals(selectedNode, node)) return false;
        if (TryGetRealizedRow(node) is not { } row) return false;
        bool focused = row.Focus();
        var header = row.HeaderVisual;
        header.BringIntoView(new Rect(0, 0, Math.Min(header.Bounds.Width, Math.Max(32, list.Bounds.Width - 40)), header.Bounds.Height));
        return focused;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = observing = true; RefreshProjection();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        attached = observing = false; focusRequest++;
        foreach (var node in watched) node.PropertyChanged -= NodeChanged;
        watched.Clear();
        if (AutomationRequested)
        {
            suppressListSelection++;
            try { projection.Clear(); }
            finally { suppressListSelection--; }
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void TreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (FromButton(e.Source) || e.KeyModifiers != KeyModifiers.None) return;
        var current = selectedNode;
        switch (e.Key)
        {
            case Key.Down: Move(current, 1); break;
            case Key.Up: Move(current, -1); break;
            case Key.Home: SelectEdge(first: true); break;
            case Key.End: SelectEdge(first: false); break;
            case Key.PageDown: Move(current, PageStep()); break;
            case Key.PageUp: Move(current, -PageStep()); break;
            case Key.Right:
                if (current is { HasChildren: true, IsExpanded: false }) current.IsExpanded = true;
                else if (current is { IsExpanded: true })
                {
                    var first = current.Items.FirstOrDefault(IsSelectable);
                    if (first is not null) SelectAndReveal(first);
                }
                break;
            case Key.Left:
                if (current is { IsExpanded: true }) current.IsExpanded = false;
                else if (current?.Parent is { } parent) SelectAndReveal(parent);
                break;
            case Key.Enter:
                if (current is not null) NodeActivated?.Invoke(this, current);
                break;
            default: return;
        }
        e.Handled = true;
    }

    private bool FromButton(object? source)
        => source is Visual visual && visual.GetSelfAndVisualAncestors().TakeWhile(v => !ReferenceEquals(v, this)).Any(v => v is Button);

    private int PageStep()
    {
        int visible = GetRealizedRows().Count(row => row.TranslatePoint(default, list) is { } point
            && point.Y < list.Bounds.Height && point.Y + row.Bounds.Height > 0);
        return Math.Max(1, visible - 1);
    }

    private void Move(StructureTreeNode? current, int distance)
    {
        if (projection.Count == 0) return;
        int direction = Math.Sign(distance);
        int origin = current is null ? direction > 0 ? -1 : projection.Count : projection.IndexOf(current);
        int target = Math.Clamp(origin + distance, 0, projection.Count - 1);
        for (int i = target; i >= 0 && i < projection.Count; i += direction)
            if (IsSelectable(projection[i])) { SelectAndReveal(projection[i]); return; }
        for (int i = target - direction; i != origin && i >= 0 && i < projection.Count; i -= direction)
            if (IsSelectable(projection[i])) { SelectAndReveal(projection[i]); return; }
    }

    private void SelectEdge(bool first)
    {
        var node = first ? projection.FirstOrDefault(IsSelectable) : projection.LastOrDefault(IsSelectable);
        if (node is not null) SelectAndReveal(node);
    }

    private sealed class TreeList(StructureTree owner) : ListBox
    {
        protected override Type StyleKeyOverride => typeof(ListBox);
        protected override AutomationPeer OnCreateAutomationPeer() => owner.GetRootAutomationPeer();
        protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
        {
            owner.CreatedRowCount++;
            if (!owner.AutomationRequested) owner.RecyclingRowCreationCount++;
            return new StructureTreeRow();
        }
        protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
        {
            recycleKey = owner.AutomationRequested ? null : typeof(StructureTreeRow);
            return true;
        }
        protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
        {
            base.PrepareContainerForItemOverride(container, item, index);
            ((StructureTreeRow)container).Bind(owner, (StructureTreeNode)item!);
        }
        protected override void ClearContainerForItemOverride(Control container)
        {
            base.ClearContainerForItemOverride(container);
            ((StructureTreeRow)container).Unbind();
        }
    }
}
