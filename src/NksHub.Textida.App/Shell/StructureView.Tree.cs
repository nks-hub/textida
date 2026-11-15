using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Collections;
using Avalonia.Controls;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Shell;

internal sealed partial class StructureView
{
    private sealed record RowData(StructureNode Node, string Name, string Value, ByteRange? ResultRange = null)
    {
        public ByteRange Range => ResultRange ?? Node.Range;
        public bool Grouped => ResultRange.HasValue && ResultRange.Value != Node.Range;
    }
    private sealed record ChildPage(IReadOnlyList<RowData> Rows, long After, bool Complete);
    private sealed class Row(RowData data, TreeViewItem item, StructureRowHeader header, Row? parent)
    {
        public RowData Data { get; set; } = data;
        public TreeViewItem Item { get; } = item;
        public StructureRowHeader Header { get; } = header;
        public Row? Parent { get; set; } = parent;
        public long Touched, RequestId;
        public bool Loaded, Loading;
        public Request? Request;
    }
    private long rootParentId = -1;
    private readonly List<Row> markedRows = [];
    private readonly Stack<Row> freeRows = [];
    private static readonly RowData EmptyRow = new(default, "", "");

    private static RowData ReadRow(IStructureIndex index, StructureNode node, CancellationToken ct, StructureText.Reader? reader = null)
    {
        reader ??= new StructureText.Reader(index, ct);
        var name = reader.ReadName(node, 160);
        var value = node.Kind is StructureKind.Document or StructureKind.JsonArray or StructureKind.JsonObject or StructureKind.XmlElement
            ? new StructurePreview("", false) : reader.ReadValue(node, 256);
        return new(node, name.Text + (name.Truncated ? "…" : ""), value.Text + (value.Truncated ? "…" : ""));
    }

    private static ChildPage ReadPage(StructureIndex index, StructureNode parent, long after, CancellationToken ct)
    {
        var nodes = index.ReadChildren(parent.Id, after, PageSize, ct);
        var result = new List<RowData>(nodes.Count);
        var reader = new StructureText.Reader(index, ct);
        foreach (var node in nodes) { ct.ThrowIfCancellationRequested(); result.Add(ReadRow(index, node, ct, reader)); }
        return new(result, after, nodes.Count == 0 || nodes[^1].Ordinal + 1 >= parent.ChildCount);
    }

    private Row NewRow(RowData data, Row? parent, bool placeholder = true)
    {
        if (!freeRows.TryPop(out var row))
        {
            var item = new TreeViewItem();
            var header = new StructureRowHeader();
            item.Header = header;
            row = new(EmptyRow, item, header, null);
            item.PropertyChanged += (_, e) =>
            {
                if (e.Property != TreeViewItem.IsExpandedProperty || !rows.ContainsKey(item)) return;
                row.Touched = ++clock;
                if (!item.IsExpanded && row.Loading) { row.Request?.Cancel(); row.Loading = false; }
                if (item.IsExpanded && !row.Loaded && !row.Loading) LoadChildren(row, -1);
            };
        }
        row.RequestId++;
        row.Data = data; row.Parent = parent; row.Touched = ++clock;
        row.Item.Tag = data.Node;
        Label(row);
        if (placeholder && data.Node.ChildCount > 0) row.Item.Items.Add(Placeholder());
        rows.Add(row.Item, row);
        return row;
    }

    private static string KindKey(StructureKind kind) => kind switch
    {
        StructureKind.Document => K.StructureKindDocument,
        StructureKind.JsonObject => K.StructureKindJsonObject,
        StructureKind.JsonArray => K.StructureKindJsonArray,
        StructureKind.JsonString => K.StructureKindJsonString,
        StructureKind.JsonNumber => K.StructureKindJsonNumber,
        StructureKind.JsonBoolean => K.StructureKindJsonBoolean,
        StructureKind.JsonNull => K.StructureKindJsonNull,
        StructureKind.XmlElement => K.StructureKindXmlElement,
        StructureKind.XmlAttribute => K.StructureKindXmlAttribute,
        StructureKind.XmlNamespace => K.StructureKindXmlNamespace,
        StructureKind.XmlText => K.StructureKindXmlText,
        StructureKind.XmlCData => K.StructureKindXmlCData,
        StructureKind.XmlComment => K.StructureKindXmlComment,
        StructureKind.XmlProcessingInstruction => K.StructureKindXmlProcessingInstruction,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private void Label(Row row)
    {
        var node = row.Data.Node;
        string kind = Loc.Get(KindKey(node.Kind));
        string name = row.Data.Name.Length > 0 ? row.Data.Name : node.Kind == StructureKind.Document ? kind : "[" + node.Ordinal.ToString(CultureInfo.CurrentCulture) + "]";
        string heading = name + "  ·  " + kind + (node.ChildCount > 0 ? "  (" + node.ChildCount.ToString(CultureInfo.CurrentCulture) + ")" : "");
        row.Header.SetText(heading, row.Data.Value);
        AutomationProperties.SetName(row.Item, heading + " " + row.Data.Value);
    }

    private static TreeViewItem Placeholder() => new() { Header = Loc.Get(K.StructureLoading), IsEnabled = false };

    private void InstallRoot(RowData root, ChildPage page)
        => Programmatic(() => InstallRootCore(root, page));

    private void InstallRootCore(RowData root, ChildPage page)
    {
        ResetRows();
        rootParentId = root.Node.ParentId;
        ReserveRows(1, 0);
        var row = NewRow(root, null, placeholder: false);
        tree.Items.Add(row.Item);
        InstallPage(row, page);
        row.Item.IsExpanded = true;
        showingResults = false;
        UpdateButtons();
    }

    private void ResetRows(bool retainPool = true)
    {
        treeVersion++;
        RetireItems(tree.Items.OfType<TreeViewItem>().ToArray(), () => tree.Items.Clear(), retainPool);
        rows.Clear(); markedRows.Clear(); rootParentId = -1;
        if (!retainPool) freeRows.Clear();
    }

    private void RemoveChildren(Row row)
        => RetireItems(row.Item.Items.OfType<TreeViewItem>().ToArray(), () => row.Item.Items.Clear(), retainPool: true);

    private void RetireItems(TreeViewItem[] roots, Action detach, bool retainPool)
    {
        long started = Core.TypingTrace.Begin();
        try
        {
            var selected = Selected;
            var items = new List<TreeViewItem>();
            var retired = new List<(Row Row, bool Reuse)>();
            var pending = new Stack<TreeViewItem>(roots);
            while (pending.TryPop(out var item))
            {
                items.Add(item);
                foreach (var child in item.Items.OfType<TreeViewItem>()) pending.Push(child);
                if (!rows.Remove(item, out var row)) continue;
                row.RequestId++; row.Request?.Cancel();
                bool reuse = retainPool && !item.IsFocused && !item.IsKeyboardFocusWithin
                    && (selected == null || !Protects(row, selected));
                retired.Add((row, reuse));
                markedRows.Remove(row);
            }
            Programmatic(detach);
            foreach (var item in items) item.Items.Clear();
            foreach (var (row, reuse) in retired)
            {
                row.Parent = null; row.Request = null; row.Data = EmptyRow;
                row.Loaded = row.Loading = false; row.Touched = 0;
                row.Header.SetCaret(false);
                AutomationProperties.SetItemStatus(row.Item, "");
                if (!reuse) continue;
                row.Item.SetCurrentValue(TreeViewItem.IsSelectedProperty, false);
                row.Item.SetCurrentValue(TreeViewItem.IsExpandedProperty, false);
                row.Item.Tag = null;
                row.Header.SetText("", "");
                AutomationProperties.SetName(row.Item, "");
                freeRows.Push(row);
            }
        }
        finally { Core.TypingTrace.End("structure.retireItems", started); }
    }

    private void TrimPool(int maximum)
    {
        while (freeRows.Count > Math.Max(0, maximum)) freeRows.Pop();
    }

    private void ReserveRows(int count, int additionalItems)
    {
        int active = CountItems();
        if (active + count + additionalItems > MaximumItems)
            throw new InvalidOperationException("The structure control cache exceeded its bound.");
        // Reusing data rows leaves active + max(free, count) + additionalItems retained slots.
        TrimPool(MaximumItems - active - additionalItems);
    }

    private int CountItems()
    {
        int count = 0;
        var pending = new Stack<TreeViewItem>(tree.Items.OfType<TreeViewItem>());
        while (pending.TryPop(out var item))
        {
            count++;
            foreach (var child in item.Items.OfType<TreeViewItem>()) pending.Push(child);
        }
        return count;
    }

    private static bool Protects(Row candidate, Row target)
    {
        for (Row? row = target; row != null; row = row.Parent) if (ReferenceEquals(row, candidate)) return true;
        return false;
    }

    private bool ProtectsUserState(Row candidate) => candidate.Item.IsFocused || candidate.Item.IsKeyboardFocusWithin
        || Selected is { } selected && Protects(candidate, selected);

    private Row MakeRoom(Row parent, int needed)
    {
        RemoveChildren(parent);
        int depth = 0;
        for (var ancestor = parent.Parent; ancestor != null; ancestor = ancestor.Parent) depth++;
        if (depth >= MaximumRevealLevels) parent = Anchor(parent.Data);
        while (CountItems() + needed > MaximumItems)
        {
            var victim = rows.Values.Where(row => row.Loaded && row.Item.Items.Count > 0 && !Protects(row, parent) && !ProtectsUserState(row))
                .OrderBy(row => row.Item.IsExpanded).ThenBy(row => row.Touched).FirstOrDefault();
            if (victim != null)
            {
                victim.Request?.Cancel(); victim.RequestId++;
                RemoveChildren(victim); victim.Loaded = false; victim.Loading = false;
                victim.Item.IsExpanded = false;
                if (victim.Data.Node.ChildCount > 0)
                {
                    TrimPool(MaximumItems - CountItems() - 1);
                    victim.Item.Items.Add(Placeholder());
                }
                continue;
            }
            // A path can be deeper than the control cache. Keep its current subtree navigable via Parent.
            parent = Anchor(parent.Data);
            break;
        }
        return parent;
    }

    private Row Anchor(RowData data)
    {
        ResetRows(); rootParentId = data.Node.ParentId;
        ReserveRows(1, 0);
        var row = NewRow(data, null, placeholder: false); tree.Items.Add(row.Item);
        return row;
    }

    private void InstallPage(Row original, ChildPage page)
        => Programmatic(() => InstallPageCore(original, page));

    private void InstallPageCore(Row original, ChildPage page)
    {
        long started = Core.TypingTrace.Begin();
        try
        {
            treeVersion++;
            bool pager = page.After >= 0 || !page.Complete;
            int additional = page.Rows.Count(row => row.Node.ChildCount > 0) + (pager ? 1 : 0);
            int needed = page.Rows.Count + additional;
            Row parent = MakeRoom(original, needed);
            ReserveRows(page.Rows.Count, additional);
            var items = new TreeViewItem[page.Rows.Count];
            for (int i = 0; i < items.Length; i++)
            {
                long rowStarted = Core.TypingTrace.Begin();
                try { items[i] = NewRow(page.Rows[i], parent).Item; }
                finally { Core.TypingTrace.End("structure.row.prepare", rowStarted); }
            }
            long attachStarted = Core.TypingTrace.Begin();
            try
            {
                if (items.Length > 0)
                {
                    parent.Item.Items.Add(items[0]);
                    if (parent.Item.Items.Source is IAvaloniaList<object?> list) list.AddRange(items[1..]);
                    else foreach (var item in items.Skip(1)) parent.Item.Items.Add(item);
                }
            }
            finally { Core.TypingTrace.End("structure.page.attach", attachStarted); }
            if (pager)
            {
                long version = parent.RequestId, nodeId = parent.Data.Node.Id, epoch = generation;
                var before = new Button { Content = Loc.Get(K.StructurePrevious), IsEnabled = page.After >= 0 };
                var after = new Button { Content = Loc.Get(K.StructureNext), IsEnabled = !page.Complete };
                before.Click += (_, _) => { if (generation == epoch && CurrentRow(parent, version, nodeId)) LoadChildren(parent, page.After, previousPage: true); };
                after.Click += (_, _) => { if (generation == epoch && CurrentRow(parent, version, nodeId) && page.Rows.Count > 0) LoadChildren(parent, page.Rows[^1].Node.Id); };
                parent.Item.Items.Add(new TreeViewItem { Header = Bar(before, after), Focusable = false });
            }
            parent.Loaded = true; parent.Loading = false; parent.Touched = ++clock;
            parent.Item.IsExpanded = true;
            UpdateButtons();
        }
        finally { Core.TypingTrace.End("structure.installPage", started); }
    }

    private void LoadChildren(Row row, long after, bool previousPage = false)
    {
        if (session is not { Analysis.IsValid: true } owner || !Current(owner) || !rows.ContainsKey(row.Item)) return;
        var requestedNode = row.Data.Node;
        row.Request?.Cancel();
        var request = row.Request = new Request(owner.Token);
        long version = ++row.RequestId;
        row.Loading = true;
        Task task = Run(owner, ct =>
        {
            var index = owner.Analysis!.Index;
            if (previousPage)
            {
                long ordinal = after < 0 ? 0 : index.ReadNode(after).Ordinal + 1;
                long target = Math.Max(0, ordinal - PageSize);
                after = -1;
                for (long id = requestedNode.Id + 1; id <= requestedNode.LastDescendantId;)
                {
                    ct.ThrowIfCancellationRequested();
                    var candidate = index.ReadNode(id);
                    if (candidate.Ordinal >= target) break;
                    after = candidate.Id; id = candidate.LastDescendantId + 1;
                }
            }
            return ReadPage(index, requestedNode, after, ct);
        }, page =>
        {
            if (!CurrentRow(row, version, requestedNode.Id)) return;
            InstallPage(row, page); MarkCaret(owner, reveal: false);
        }, request.Token);
        _ = task.ContinueWith(_ =>
        {
            request.Dispose();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (CurrentRow(row, version, requestedNode.Id)) row.Loading = false; });
        }, TaskScheduler.Default);
    }

    private bool CurrentRow(Row row, long version, long nodeId) => rows.TryGetValue(row.Item, out var current)
        && ReferenceEquals(current, row) && row.RequestId == version && row.Data.Node.Id == nodeId;

    private void ShowDocument()
    {
        filterRequest?.Cancel();
        changing = true; filter.Text = ""; changing = false;
        LoadRoot(0);
    }

    private void ShowParent() { if (rootParentId >= 0) LoadRoot(rootParentId); }

    private void LoadRoot(long id)
    {
        if (session is not { Analysis.IsValid: true } owner || !Current(owner)) return;
        Run(owner, ct =>
        {
            var index = owner.Analysis!.Index;
            var node = index.ReadNode(id);
            return (ReadRow(index, node, ct), ReadPage(index, node, -1, ct));
        }, result => { InstallRoot(result.Item1, result.Item2); ShowStats(owner); MarkCaret(owner, reveal: false); });
    }

}
