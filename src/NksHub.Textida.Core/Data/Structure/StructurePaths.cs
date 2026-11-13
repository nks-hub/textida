using System.Globalization;
using System.Text;

namespace NksHub.Textida.Core.Data.Structure;

public static class StructurePaths
{
    public static string Create(IStructureIndex index, StructureNode node, int maxChars = 1_048_576,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChars);
        var result = new StringBuilder(Math.Min(maxChars, 256));
        foreach (int value in ReadScalars(index, node, ct))
        {
            int width = value > char.MaxValue ? 2 : 1;
            if (result.Length + width > maxChars) throw new InvalidOperationException("The node path exceeds the copy limit.");
            if (width == 1) result.Append((char)value);
            else result.Append(char.ConvertFromUtf32(value));
        }
        return result.ToString();
    }

    public static IEnumerable<int> ReadScalars(IStructureIndex index, StructureNode node, CancellationToken ct = default)
        => new Reader(index, ct).ReadScalars(node);

    internal sealed class Reader(IStructureIndex index, CancellationToken ct)
    {
        private readonly Dictionary<long, Cursor> positions = [];
        private readonly StructureQuery.ValueReader values = new(index, ct);
        private readonly record struct Cursor(long LastId, long NextId, long Nodes, long Attributes, long Position);

        public IEnumerable<int> ReadScalars(StructureNode node)
        {
            var ancestors = new Stack<StructureNode>();
            for (var current = node; current.ParentId >= 0; current = index.ReadNode(current.ParentId))
            {
                ct.ThrowIfCancellationRequested();
                ancestors.Push(current);
            }
            if (index.Format != StructureFormat.Xml) yield return '$';
            else if (ancestors.Count == 0) yield return '/';
            while (ancestors.TryPop(out var current))
            {
                ct.ThrowIfCancellationRequested();
                if (index.Format != StructureFormat.Xml)
                {
                    if (current.ParentId == 0) continue;
                    yield return '[';
                    if (current.HasName)
                    {
                        yield return '"';
                        foreach (int value in values.ReadName(current))
                        {
                            if (value is '"' or '\\') { yield return '\\'; yield return value; }
                            else if (value < 32)
                            {
                                foreach (char c in "\\u" + value.ToString("x4", CultureInfo.InvariantCulture)) yield return c;
                            }
                            else yield return value;
                        }
                        yield return '"';
                    }
                    else foreach (char c in current.Ordinal.ToString(CultureInfo.InvariantCulture)) yield return c;
                    yield return ']';
                }
                else
                {
                    yield return '/';
                    if (current.Kind == StructureKind.XmlNamespace)
                    {
                        foreach (char c in "namespace::*[name()='") yield return c;
                        bool afterColon = false;
                        foreach (int value in values.ReadName(current))
                        {
                            if (afterColon) yield return value;
                            else if (value == ':') afterColon = true;
                        }
                        foreach (char c in "']") yield return c;
                        continue;
                    }
                    bool attribute = current.Kind == StructureKind.XmlAttribute;
                    foreach (char c in attribute ? "@*[" : "node()[") yield return c;
                    long position = PositionOf(current);
                    foreach (char c in position.ToString(CultureInfo.InvariantCulture)) yield return c;
                    yield return ']';
                }
            }
        }

        private long PositionOf(StructureNode node)
        {
            long nextId = node.ParentId + 1, nodes = 0, attributes = 0;
            if (positions.TryGetValue(node.ParentId, out var saved))
            {
                if (saved.LastId == node.Id) return saved.Position;
                if (saved.LastId < node.Id) (nextId, nodes, attributes) = (saved.NextId, saved.Nodes, saved.Attributes);
            }
            for (long id = nextId; id <= node.Id;)
            {
                ct.ThrowIfCancellationRequested();
                var sibling = index.ReadNode(id);
                if (sibling.Kind == StructureKind.XmlAttribute) attributes++;
                else if (sibling.Kind != StructureKind.XmlNamespace && StructureXmlNodes.IsXPathNode(index, sibling, ct)) nodes++;
                nextId = sibling.LastDescendantId + 1;
                id = nextId;
            }
            long position = node.Kind == StructureKind.XmlAttribute ? attributes : nodes;
            if (positions.Count >= 4096) positions.Clear();
            positions[node.ParentId] = new(node.Id, nextId, nodes, attributes, position);
            return position;
        }
    }
}
