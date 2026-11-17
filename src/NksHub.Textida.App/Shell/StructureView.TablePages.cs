using NksHub.Textida.Core.Data.Structure;

namespace NksHub.Textida.App.Shell;

internal sealed class StructureTablePages
{
    private readonly record struct Key(long Parent, int Page, int Size);
    private sealed record Cursor(long After, StructureNode First, StructureNode Last, long Touched);
    private readonly Dictionary<Key, Cursor> cursors = [];
    private long clock;
    internal int Count => cursors.Count;

    internal StructureTablePages Clone()
    {
        var result = new StructureTablePages { clock = clock };
        foreach (var pair in cursors) result.cursors.Add(pair.Key, pair.Value);
        return result;
    }

    internal IReadOnlyList<StructureNode> Read(IStructureIndex index, StructureNode parent, int page, int size, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        long ordinal = (long)page * size;
        if (ordinal >= parent.ChildCount) return [];
        var key = new Key(parent.Id, page, size);
        long after;
        if (cursors.TryGetValue(key, out var exact)) after = exact.After;
        else if (cursors.TryGetValue(key with { Page = page - 1 }, out var previous)) after = previous.Last.Id;
        else
        {
            var nearest = cursors.Where(pair => pair.Key.Parent == parent.Id && pair.Key.Size == size)
                .OrderBy(pair => Math.Abs((long)pair.Key.Page - page)).FirstOrDefault().Value;
            var first = nearest?.First ?? index.ReadNode(parent.Id + 1);
            while (first.Ordinal > ordinal) first = Previous(index, parent, first, ct);
            while (first.Ordinal < ordinal)
            {
                ct.ThrowIfCancellationRequested();
                first = index.ReadNode(first.LastDescendantId + 1);
                if (first.ParentId != parent.Id) throw new InvalidDataException("Invalid table sibling link.");
            }
            after = first.Ordinal == 0 ? -1 : Previous(index, parent, first, ct).Id;
        }
        long id = after < 0 ? parent.Id + 1 : index.ReadNode(after).LastDescendantId + 1;
        var rows = new List<StructureNode>(size);
        while (id <= parent.LastDescendantId && rows.Count < size)
        {
            ct.ThrowIfCancellationRequested();
            var node = index.ReadNode(id);
            if (node.ParentId != parent.Id) throw new InvalidDataException("Invalid table child link.");
            rows.Add(node); id = node.LastDescendantId + 1;
        }
        ct.ThrowIfCancellationRequested();
        if (rows.Count > 0) cursors[key] = new(after, rows[0], rows[^1], ++clock);
        // Reserve a separate cursor budget for record pages so field paging cannot evict their anchors.
        bool records = size == StructureView.PageSize;
        int limit = records ? StructureView.PageSize : StructureView.MaximumItems - StructureView.PageSize;
        var bucket = cursors.Where(pair => (pair.Key.Size == StructureView.PageSize) == records).ToArray();
        if (bucket.Length > limit)
            foreach (var victim in bucket.OrderBy(pair => pair.Value.Touched).Take(bucket.Length - limit)) cursors.Remove(victim.Key);
        return rows;
    }

    private static StructureNode Previous(IStructureIndex index, StructureNode parent, StructureNode node, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var previous = index.ReadNode(node.Id - 1);
        while (previous.ParentId != parent.Id)
        {
            ct.ThrowIfCancellationRequested();
            if (previous.ParentId < 0 || previous.ParentId >= previous.Id) throw new InvalidDataException("Invalid table parent link.");
            previous = index.ReadNode(previous.ParentId);
        }
        return previous;
    }
}
