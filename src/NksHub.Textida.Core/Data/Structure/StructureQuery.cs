namespace NksHub.Textida.Core.Data.Structure;

public enum StructureQueryLanguage { JsonPath, XPath }

public sealed class StructureQueryException(string message, int position) : FormatException(message)
{
    public int Position { get; } = position;
}

public sealed record StructureQueryPage(IReadOnlyList<long> NodeIds, long NextNodeId, bool Complete);

/// <summary>
/// JSONPath supports members, indexes, wildcards, recursive descent and scalar equality filters.
/// XPath supports child/descendant paths, names, namespace prefixes, attributes, text(), node(),
/// comment(), positions and attribute/child/text equality predicates. The context is the document.
/// </summary>
public sealed partial class StructureQuery
{
    private const int MaxExpressionLength = 16 * 1024;
    private const int MaxSteps = 256;
    private readonly Step[] steps;

    private StructureQuery(string expression, StructureQueryLanguage language, Step[] steps)
    {
        Expression = expression;
        Language = language;
        this.steps = steps;
    }

    public string Expression { get; }
    public StructureQueryLanguage Language { get; }

    public static StructureQuery Parse(string expression, StructureQueryLanguage language)
    {
        ArgumentNullException.ThrowIfNull(expression);
        if (expression.Length > MaxExpressionLength)
            throw new StructureQueryException("The query exceeds 16384 characters.", MaxExpressionLength);
        return new QueryParser(expression, language).Parse();
    }

    /// <summary>Returns at most 4096 IDs. Pass NextNodeId unchanged to resume this query on the same index revision.</summary>
    public StructureQueryPage ReadPage(IStructureIndex index, long nextNodeId, int pageSize, CancellationToken ct,
        IReadOnlyDictionary<string, string>? namespaces = null)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (pageSize is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (nextNodeId < 0 || nextNodeId > index.Count) throw new ArgumentOutOfRangeException(nameof(nextNodeId));
        if ((Language == StructureQueryLanguage.XPath) != (index.Format == StructureFormat.Xml))
            throw new ArgumentException("The query language does not match the document format.", nameof(index));
        ct.ThrowIfCancellationRequested();
        var execution = new Execution(this, index, ct, namespaces);
        var found = new List<long>(Math.Min(pageSize, 256));
        long id = nextNodeId;
        for (; id < index.Count; id++)
        {
            ct.ThrowIfCancellationRequested();
            if (execution.Matches(id, steps.Length - 1)) found.Add(id);
            if (found.Count == pageSize) { id++; break; }
            // A root-only query has at most one result.
            if (steps.Length == 0 && found.Count > 0) { id = index.Count; break; }
        }
        return new(found, id, id >= index.Count);
    }

    private enum Axis { Child, Descendant }
    private enum Test { JsonName, JsonIndex, JsonWildcard, XmlElement, XmlAttribute, XmlText, XmlNode, XmlComment }
    internal enum LiteralKind { String, Number, Boolean, Null }
    internal sealed record Literal(LiteralKind Kind, string Text)
    {
        public NumberValue? Number { get; } = Kind == LiteralKind.Number ? NumberValue.Parse(Text) : null;
    }
    private sealed record JsonPart(string? Name, long? Index);
    private sealed record Predicate(long? Position = null, Test? Test = null, string? Name = null,
        Literal? Value = null, JsonPart[]? JsonPath = null, bool SelfValue = false);
    private sealed record Step(Axis Axis, Test Test, string? Name, long Index, Predicate[] Predicates, int Position);

    private sealed class Execution
    {
        private readonly StructureQuery query;
        private readonly IStructureIndex index;
        private readonly CancellationToken ct;
        private readonly IReadOnlyDictionary<string, string>? namespaces;
        private readonly ValueReader values;
        private readonly Dictionary<(Step Step, int Predicate, long Parent), PositionCursor> positions = [];
        private readonly Dictionary<(long Declaration, string Uri), bool> namespaceMatches = [];

        private readonly record struct PositionCursor(long LastId, long NextId, long Count);

        public Execution(StructureQuery query, IStructureIndex index, CancellationToken ct,
            IReadOnlyDictionary<string, string>? namespaces)
        {
            this.query = query;
            this.index = index;
            this.ct = ct;
            this.namespaces = namespaces;
            values = new(index, ct);
            foreach (var step in query.steps)
            {
                CheckPrefix(step.Name, step.Position);
                foreach (var predicate in step.Predicates) CheckPrefix(predicate.Name, step.Position);
            }
        }

        private void CheckPrefix(string? name, int position)
        {
            if (query.Language != StructureQueryLanguage.XPath || name is null) return;
            int colon = name.IndexOf(':');
            if (colon < 0 || name[..colon] == "xml") return;
            if (namespaces is null || !namespaces.ContainsKey(name[..colon]))
                throw new StructureQueryException("The XPath prefix has no namespace mapping: " + name[..colon], position);
        }

        public bool Matches(long id, int stepIndex)
        {
            ct.ThrowIfCancellationRequested();
            var node = index.ReadNode(id);
            if (stepIndex < 0)
                return query.Language == StructureQueryLanguage.XPath
                    ? node.Kind == StructureKind.Document && node.ParentId < 0
                    : node.ParentId == 0 && IsJson(node.Kind);
            var step = query.steps[stepIndex];
            if (!MatchesTest(node, step.Test, step.Name, step.Index) || !MatchesPredicates(node, step, step.Predicates.Length)) return false;
            if (node.ParentId < 0) return false;
            if (step.Axis == Axis.Child) return Matches(node.ParentId, stepIndex - 1);
            for (long parent = node.ParentId; parent >= 0; parent = index.ReadNode(parent).ParentId)
            {
                ct.ThrowIfCancellationRequested();
                if (Matches(parent, stepIndex - 1)) return true;
            }
            return false;
        }

        private bool MatchesTest(StructureNode node, Test test, string? name, long number = -1)
        {
            ct.ThrowIfCancellationRequested();
            return test switch
            {
                Test.JsonName => IsJson(node.Kind) && node.HasName && values.JsonNameEquals(node, name!),
                Test.JsonIndex => IsJson(node.Kind) && node.ParentId >= 0
                    && index.ReadNode(node.ParentId).Kind == StructureKind.JsonArray && node.Ordinal == number,
                Test.JsonWildcard => IsJson(node.Kind) && node.ParentId >= 0 && IsJson(index.ReadNode(node.ParentId).Kind),
                Test.XmlElement => node.Kind == StructureKind.XmlElement && MatchesXmlName(node, name!),
                Test.XmlAttribute => node.Kind == StructureKind.XmlAttribute && MatchesXmlName(node, name!),
                Test.XmlText => StructureXmlNodes.IsText(node) && StructureXmlNodes.IsXPathNode(index, node, ct),
                Test.XmlNode => node.Kind is not (StructureKind.Document or StructureKind.XmlAttribute)
                    && StructureXmlNodes.IsXPathNode(index, node, ct),
                Test.XmlComment => node.Kind == StructureKind.XmlComment,
                _ => false,
            };
        }

        private bool MatchesXmlName(StructureNode node, string name)
        {
            if (name == "*") return true;
            int colon = name.IndexOf(':');
            string local = colon < 0 ? name : name[(colon + 1)..];
            if (local != "*" && !values.XmlLocalNameEquals(node, local)) return false;
            string uri = colon < 0 ? "" : name[..colon] == "xml"
                ? "http://www.w3.org/XML/1998/namespace" : namespaces![name[..colon]];
            if (node.NamespaceId == -2) return uri == "http://www.w3.org/XML/1998/namespace";
            if (node.NamespaceId < 0) return uri.Length == 0;
            var key = (node.NamespaceId, uri);
            if (namespaceMatches.TryGetValue(key, out bool match)) return match;
            match = values.XmlValueEquals(index.ReadNode(node.NamespaceId), uri);
            if (namespaceMatches.Count >= 4096) namespaceMatches.Clear();
            namespaceMatches.Add(key, match);
            return match;
        }

        private bool MatchesPredicates(StructureNode node, Step step, int count)
        {
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var predicate = step.Predicates[i];
                if (predicate.Position is { } position)
                {
                    if (PositionOf(node, step, i) != position) return false;
                }
                else if (predicate.JsonPath is { } path)
                {
                    if (!AnyJsonValue(node, path, 0, predicate.Value!)) return false;
                }
                else if (predicate.SelfValue)
                {
                    if (!values.XmlStringValueEquals(node, predicate.Value!.Text)) return false;
                }
                else
                {
                    bool matched = false;
                    foreach (var child in Children(node))
                    {
                        if (!MatchesTest(child, predicate.Test!.Value, predicate.Name)) continue;
                        if (predicate.Value is null || values.XmlStringValueEquals(child, predicate.Value.Text))
                        {
                            matched = true;
                            break;
                        }
                    }
                    if (!matched) return false;
                }
            }
            return true;
        }

        private long PositionOf(StructureNode node, Step step, int predicateIndex)
        {
            if (node.ParentId < 0) return 0;
            var key = (step, predicateIndex, node.ParentId);
            var parent = index.ReadNode(node.ParentId);
            long cursor = parent.Id + 1, count = 0;
            if (positions.TryGetValue(key, out var saved) && saved.LastId < node.Id)
            {
                cursor = saved.NextId;
                count = saved.Count;
            }
            for (; cursor <= node.Id && cursor < index.Count;)
            {
                ct.ThrowIfCancellationRequested();
                var sibling = index.ReadNode(cursor);
                if (sibling.ParentId != parent.Id) break;
                if (MatchesTest(sibling, step.Test, step.Name, step.Index) && MatchesPredicates(sibling, step, predicateIndex)) count++;
                long next = Math.Max(cursor + 1, sibling.LastDescendantId + 1);
                if (cursor == node.Id)
                {
                    if (positions.Count >= 4096) positions.Clear();
                    positions[key] = new(node.Id, next, count);
                    return count;
                }
                cursor = next;
            }
            return 0;
        }

        private bool AnyJsonValue(StructureNode node, JsonPart[] path, int at, Literal literal)
        {
            ct.ThrowIfCancellationRequested();
            if (at == path.Length) return values.JsonValueEquals(node, literal);
            var part = path[at];
            foreach (var child in Children(node))
            {
                bool matches = part.Name is { } name
                    ? node.Kind == StructureKind.JsonObject && child.HasName && values.JsonNameEquals(child, name)
                    : node.Kind == StructureKind.JsonArray && child.Ordinal == part.Index;
                if (matches && AnyJsonValue(child, path, at + 1, literal)) return true;
            }
            return false;
        }

        private IEnumerable<StructureNode> Children(StructureNode parent)
        {
            for (long id = parent.Id + 1; id <= parent.LastDescendantId && id < index.Count;)
            {
                ct.ThrowIfCancellationRequested();
                var child = index.ReadNode(id);
                if (child.ParentId != parent.Id) yield break;
                yield return child;
                id = Math.Max(id + 1, child.LastDescendantId + 1);
            }
        }
    }

    private static bool IsJson(StructureKind kind) => kind is >= StructureKind.JsonObject and <= StructureKind.JsonNull;
}
