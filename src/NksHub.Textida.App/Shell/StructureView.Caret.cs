using Avalonia.Automation;
using Avalonia.Controls;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Data.Structure;

namespace NksHub.Textida.App.Shell;

internal sealed partial class StructureView
{
    private sealed record RevealLevel(long ParentId, ChildPage Page);
    private sealed record Reveal(long Id, RowData? Anchor, IReadOnlyList<RevealLevel> Levels);
    private const int MaximumRevealLevels = 32;

    private void MarkCaret(Session owner, bool reveal = true, (StructureSource Source, long Id)? known = null)
    {
        if (owner.Analysis is not { IsValid: true } analysis || !Current(owner)) return;
        if (source() is not { } latest || !SameSource(latest, owner.Source)) return;
        owner.Source = latest;
        caretRequest?.Cancel();
        long offset = owner.Source.Offset;
        if (!reveal && known is { } target && SameSource(latest, target.Source) && target.Source.Offset == offset)
        {
            Mark(CaretRow(target.Id, offset, showingResults), revealParents: false);
            return;
        }
        var request = caretRequest = new Request(owner.Token);
        long version = treeVersion;
        bool results = showingResults;
        var loaded = rows.Values.Select(row => row.Data.Node.Id).ToHashSet();
        Task task = Run(owner, ct =>
        {
            var target = analysis.Index.FindContaining(offset, ct);
            if (!target.HasValue) return new Reveal(-1, null, []);
            if (results || !reveal || loaded.Contains(target.Value.Id)) return new Reveal(target.Value.Id, null, []);
            return ReadReveal(analysis.Index, target.Value, ct);
        }, plan =>
        {
            if (showingResults != results) { MarkCaret(owner, reveal); return; }
            if (results)
            {
                Mark(CaretRow(plan.Id, offset, results: true), revealParents: false);
                return;
            }
            Row? actual = CaretRow(plan.Id, offset, results: false);
            if (actual == null && plan.Anchor != null)
            {
                if (treeVersion != version) { MarkCaret(owner, reveal); return; }
                var root = tree.Items.OfType<StructureTreeNode>().FirstOrDefault();
                if (root == null || !rows.TryGetValue(root, out var rootRow) || rootRow.Data.Node.Id != plan.Anchor.Node.Id)
                {
                    var firstPage = plan.Levels.Count > 0 ? plan.Levels[0].Page : new ChildPage([], -1, true);
                    InstallRoot(plan.Anchor, firstPage);
                }
                foreach (var level in plan.Levels)
                {
                    var parent = rows.Values.FirstOrDefault(row => row.Data.Node.Id == level.ParentId);
                    if (parent == null) break;
                    bool samePage = parent.Loaded && DataChildIds(parent).SequenceEqual(level.Page.Rows.Select(data => data.Node.Id));
                    if (!samePage) InstallPage(parent, level.Page);
                }
                actual = rows.Values.FirstOrDefault(row => row.Data.Node.Id == plan.Id);
            }
            Mark(actual, revealParents: reveal);
        }, request.Token);
        var cleanup = task.ContinueWith(_ => request.Dispose(), TaskScheduler.Default);
        ObserveCancellationCleanup(owner, cleanup);
    }

    private Row? CaretRow(long id, long offset, bool results) => results
        ? rows.Values.Where(row => row.Data.Node.Id == id || row.Data.Range.Start <= offset && offset < row.Data.Range.End)
            .OrderBy(row => row.Data.Node.Id == id ? 0 : 1).ThenBy(row => row.Data.Range.Length).FirstOrDefault()
        : rows.Values.FirstOrDefault(row => row.Data.Node.Id == id);

    private static IEnumerable<long> DataChildIds(Row row) => row.Item.Items.OfType<StructureTreeNode>()
        .Where(item => item.Tag is StructureNode).Select(item => ((StructureNode)item.Tag!).Id);

    private static Reveal ReadReveal(StructureIndex index, StructureNode target, CancellationToken ct)
    {
        var levels = new List<RevealLevel>();
        var anchor = target;
        int controls = 1;
        for (int depth = 0; anchor.ParentId >= 0 && depth < MaximumRevealLevels; depth++)
        {
            ct.ThrowIfCancellationRequested();
            var parent = index.ReadNode(anchor.ParentId);
            long firstOrdinal = anchor.Ordinal / PageSize * PageSize;
            var first = anchor;
            while (first.Ordinal > firstOrdinal) first = PreviousSibling(index, parent, first, ct);
            long after = first.Ordinal == 0 ? -1 : PreviousSibling(index, parent, first, ct).Id;
            var page = ReadPage(index, parent, after, ct);
            int additional = page.Rows.Sum(row => row.Node.ChildCount > 0 ? 2 : 1) + 1;
            if (levels.Count > 0 && controls + additional > MaximumItems - 8) break;
            controls += additional;
            levels.Add(new(parent.Id, page));
            anchor = parent;
        }
        levels.Reverse();
        return new(target.Id, ReadRow(index, anchor, ct), levels);
    }

    private static StructureNode PreviousSibling(StructureIndex index, StructureNode parent, StructureNode child, CancellationToken ct)
    {
        if (child.Ordinal == 0 || child.Id <= parent.Id + 1) throw new InvalidDataException("The node has no previous sibling.");
        var previous = index.ReadNode(child.Id - 1);
        while (previous.ParentId != parent.Id)
        {
            ct.ThrowIfCancellationRequested();
            if (previous.ParentId < 0 || previous.ParentId >= previous.Id)
                throw new InvalidDataException("The structural index contains an invalid parent link.");
            previous = index.ReadNode(previous.ParentId);
        }
        return previous;
    }

    private void Mark(Row? row, bool revealParents)
    {
        if (row is { Item.HeaderHidden: true })
            row = row.Item.Items.Select(item => rows.GetValueOrDefault(item)).FirstOrDefault(item => item is not null && !item.Item.HeaderHidden);
        foreach (var marked in markedRows) { marked.Item.IsCaret = false; }
        markedRows.Clear();
        if (row == null) return;
        Programmatic(() =>
        {
            if (revealParents)
            {
                var parents = new Stack<Row>();
                for (var parent = row.Parent; parent != null; parent = parent.Parent) parents.Push(parent);
                while (parents.TryPop(out var parent)) parent.Item.IsExpanded = true;
            }
            tree.SelectedNode = row.Item;
        });
        row.Item.IsCaret = true;
        if (revealParents) tree.SelectAndReveal(row.Item, focus: false);
        markedRows.Add(row);
    }
}
