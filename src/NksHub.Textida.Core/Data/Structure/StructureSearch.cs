using System.Text;

namespace NksHub.Textida.Core.Data.Structure;

public enum StructureSearchScope { Name, Value, Path }

public static class StructureSearch
{
    public static StructureQueryPage ReadPage(IStructureIndex index, string text, StructureSearchScope scope,
        long nextNodeId, int pageSize, CancellationToken ct = default)
    {
        if (text.Length is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(text));
        if (pageSize is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (nextNodeId < 0 || nextNodeId > index.Count) throw new ArgumentOutOfRangeException(nameof(nextNodeId));
        int[] pattern = text.EnumerateRunes().Select(rune => Rune.ToUpperInvariant(rune).Value).ToArray();
        int[] prefix = Prefix(pattern);
        var values = new StructureQuery.ValueReader(index, ct);
        var paths = new StructurePaths.Reader(index, ct);
        var result = new List<long>(pageSize);
        long id = Math.Max(1, nextNodeId);
        for (; id < index.Count && result.Count < pageSize; id++)
        {
            ct.ThrowIfCancellationRequested();
            var node = index.ReadNode(id);
            if (scope == StructureSearchScope.Name && !node.HasName && !node.IntegerKey.HasValue) continue;
            if (scope == StructureSearchScope.Value && node.Kind is StructureKind.JsonObject or StructureKind.JsonArray
                or StructureKind.XmlElement) continue;
            if (scope == StructureSearchScope.Path && index.Format == StructureFormat.Xml
                && node.Kind != StructureKind.XmlNamespace && !StructureXmlNodes.IsXPathNode(index, node, ct)) continue;
            IEnumerable<int> content = scope switch
            {
                StructureSearchScope.Name => values.ReadName(node),
                StructureSearchScope.Value => values.ReadValue(node),
                _ => paths.ReadScalars(node),
            };
            if (Contains(content, pattern, prefix, ct)) result.Add(id);
        }
        return new(result, id, id >= index.Count);
    }

    private static bool Contains(IEnumerable<int> content, int[] pattern, int[] prefix, CancellationToken ct)
    {
        int matched = 0;
        foreach (int scalar in content)
        {
            ct.ThrowIfCancellationRequested();
            int value = Rune.ToUpperInvariant(new Rune(scalar)).Value;
            while (matched > 0 && pattern[matched] != value) matched = prefix[matched - 1];
            if (pattern[matched] == value) matched++;
            if (matched == pattern.Length) return true;
        }
        return false;
    }

    private static int[] Prefix(int[] pattern)
    {
        var result = new int[pattern.Length];
        int matched = 0;
        for (int i = 1; i < pattern.Length; i++)
        {
            while (matched > 0 && pattern[i] != pattern[matched]) matched = result[matched - 1];
            if (pattern[i] == pattern[matched]) matched++;
            result[i] = matched;
        }
        return result;
    }
}
