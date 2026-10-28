using System.Text;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Markdown;

public enum FlowDirection { TopDown, BottomUp, LeftRight, RightLeft }

public enum FlowShape { Rectangle, Rounded, Diamond, Circle }

public enum FlowLine { Solid, Dotted, Thick }

/// <summary>A node of a flowchart: the id used in the source, the text drawn in it and its shape.</summary>
public sealed record FlowNode(string Id, string Text, FlowShape Shape);

/// <summary>A connection between two nodes (indexes into <see cref="Flowchart.Nodes"/>), with an optional label, a line style and an arrow head at the end.</summary>
public sealed record FlowEdge(int From, int To, string? Label, FlowLine Line, bool Arrow);

public sealed record Flowchart(FlowDirection Direction, IReadOnlyList<FlowNode> Nodes, IReadOnlyList<FlowEdge> Edges);

/// <summary>
/// The flowchart subset of Mermaid that the preview draws without a script engine (P9.3.1, P9.3.3): <c>graph</c> or <c>flowchart</c> with the direction TD, TB, BT, LR or RL; nodes <c>A</c>,
/// <c>A[text]</c>, <c>A(text)</c>, <c>A([text])</c>, <c>A((text))</c>, <c>A{text}</c>; connections <c>--&gt;</c>, <c>---</c>, <c>-.-&gt;</c>, <c>==&gt;</c> with a label as <c>--&gt;|text|</c> or
/// <c>-- text --&gt;</c>; chains (<c>A --&gt; B --&gt; C</c>) and groups (<c>A &amp; B --&gt; C</c>). <c>style</c>, <c>classDef</c>, <c>class</c>, <c>linkStyle</c>, <c>click</c> and <c>direction</c> lines are skipped.
/// shortcut: subgraphs, other node shapes and other diagram types make <see cref="Parse"/> return null and the preview shows the source; add them when a user needs them.
/// </summary>
public static partial class MermaidFlowchart
{
    [GeneratedRegex(@"^(?:graph|flowchart)(?:\s+(?<d>TD|TB|BT|LR|RL))?$", RegexOptions.IgnoreCase)]
    private static partial Regex Header();

    [GeneratedRegex(@"\G\s*(?<op>-{2,}>|-{3,}|-\.+->|-\.+-|={2,}>|={3,})")]
    private static partial Regex Arrow();

    [GeneratedRegex(@"\G\s*\|(?<l>[^|]*)\|")]
    private static partial Regex ArrowLabel();

    [GeneratedRegex(@"\G\s*(?:--\s+(?<l>.+?)\s+(?<op>-{2,}>|-{3,})|==\s+(?<l>.+?)\s+(?<op>={2,}>|={3,})|-\.\s+(?<l>.+?)\s+(?<op>\.+->|\.+-))(?=\s|$|[A-Za-z0-9_])")]
    private static partial Regex TextArrow();

    [GeneratedRegex(@"\G[\p{L}\p{N}_]+")]
    private static partial Regex NodeId();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    private static readonly (string Open, string Close, FlowShape Shape)[] Shapes =
    [
        ("((", "))", FlowShape.Circle), ("([", "])", FlowShape.Rounded), ("[[", "]]", FlowShape.Rectangle), ("{{", "}}", FlowShape.Rectangle),
        ("[", "]", FlowShape.Rectangle), ("(", ")", FlowShape.Rounded), ("{", "}", FlowShape.Diamond), (">", "]", FlowShape.Rectangle),
    ];

    private static readonly string[] Skipped = ["style ", "classDef ", "class ", "linkStyle ", "click ", "direction "];

    /// <summary>The flowchart of the source, or null when it is another kind of diagram, uses what the subset lacks, has no node, or exceeds the limits.</summary>
    public static Flowchart? Parse(string source, int maxNodes, int maxEdges)
    {
        var lines = source.ReplaceLineEndings("\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("%%", StringComparison.Ordinal)).ToList();
        if (lines.Count == 0) return null;
        var headerParts = lines[0].TrimEnd(';').Split(';', 2);
        if (Header().Match(headerParts[0].Trim()) is not { Success: true } header) return null;
        var direction = header.Groups["d"].Value.ToUpperInvariant() switch { "BT" => FlowDirection.BottomUp, "LR" => FlowDirection.LeftRight, "RL" => FlowDirection.RightLeft, _ => FlowDirection.TopDown };

        var nodes = new List<FlowNode>();
        var edges = new List<FlowEdge>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var statements = Statements(string.Join('\n', headerParts.Skip(1).Concat(lines.Skip(1))));
        foreach (var statement in statements)
        {
            if (Skipped.Any(s => statement.StartsWith(s, StringComparison.OrdinalIgnoreCase))) continue;
            if (!Statement(statement, nodes, edges, index, maxNodes, maxEdges)) return null;
        }
        return nodes.Count == 0 ? null : new Flowchart(direction, nodes, edges);
    }

    /// <summary>The statements of the body: split at line ends and at semicolons that are outside brackets and quotes.</summary>
    private static IEnumerable<string> Statements(string body)
    {
        var current = new StringBuilder();
        int depth = 0;
        bool quoted = false;
        foreach (char c in body)
        {
            if (c == '"') quoted = !quoted;
            else if (!quoted && c is '[' or '(' or '{') depth++;
            else if (!quoted && c is ']' or ')' or '}') depth = Math.Max(0, depth - 1);
            if (!quoted && depth == 0 && c is '\n' or ';')
            {
                if (current.ToString().Trim() is { Length: > 0 } done) yield return done;
                current.Clear();
            }
            else current.Append(c);
        }
        if (current.ToString().Trim() is { Length: > 0 } last) yield return last;
    }

    private static bool Statement(string s, List<FlowNode> nodes, List<FlowEdge> edges, Dictionary<string, int> index, int maxNodes, int maxEdges)
    {
        if (s.StartsWith("subgraph", StringComparison.OrdinalIgnoreCase) || s.Equals("end", StringComparison.OrdinalIgnoreCase)) return false;
        int p = 0;
        List<int>? previous = null;
        FlowLine line = FlowLine.Solid;
        bool arrow = false;
        string? label = null;
        while (true)
        {
            var group = new List<int>();
            while (true)
            {
                while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
                if (!Node(s, ref p, nodes, index, out int node) || nodes.Count > maxNodes) return false;
                group.Add(node);
                while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
                if (p < s.Length && s[p] == '&') p++;
                else break;
            }
            if (previous is not null)
                foreach (int from in previous)
                    foreach (int to in group)
                        edges.Add(new FlowEdge(from, to, label, line, arrow));
            if (edges.Count > maxEdges) return false;
            while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
            if (p >= s.Length) return true;
            if (!ReadArrow(s, ref p, out line, out arrow, out label)) return false;
            previous = group;
        }
    }

    private static bool ReadArrow(string s, ref int p, out FlowLine line, out bool arrow, out string? label)
    {
        line = FlowLine.Solid;
        arrow = false;
        label = null;
        string op;
        if (Arrow().Match(s, p) is { Success: true } plain)
        {
            op = plain.Groups["op"].Value;
            p += plain.Length;
            if (ArrowLabel().Match(s, p) is { Success: true } piped)
            {
                label = piped.Groups["l"].Value.Trim().Trim('"');
                p += piped.Length;
            }
        }
        else if (TextArrow().Match(s, p) is { Success: true } worded)
        {
            op = worded.Groups["op"].Value;
            label = worded.Groups["l"].Value.Trim().Trim('"');
            p += worded.Length;
        }
        else return false;
        line = op[0] == '=' ? FlowLine.Thick : op.Contains('.') ? FlowLine.Dotted : FlowLine.Solid;
        arrow = op[^1] == '>';
        if (label is { Length: 0 }) label = null;
        return true;
    }

    private static bool Node(string s, ref int p, List<FlowNode> nodes, Dictionary<string, int> index, out int node)
    {
        node = -1;
        if (NodeId().Match(s, p) is not { Success: true } id) return false;
        p += id.Length;
        string text = id.Value;
        FlowShape? shape = null;
        foreach (var (open, close, kind) in Shapes)
        {
            if (string.CompareOrdinal(s, p, open, 0, open.Length) != 0) continue;
            int from = p + open.Length, end;
            if (from < s.Length && s[from] == '"')
            {
                int quote = s.IndexOf('"', from + 1);
                if (quote < 0 || string.CompareOrdinal(s, quote + 1, close, 0, close.Length) != 0) return false;
                (text, end) = (s[(from + 1)..quote], quote + 1);
            }
            else
            {
                end = s.IndexOf(close, from, StringComparison.Ordinal);
                if (end < 0) return false;
                text = s[from..end].Trim();
                if (open == "[" && text.Length > 2 && text[0] is '/' or '\\' && text[^1] is '/' or '\\') text = text[1..^1].Trim();
            }
            p = end + close.Length;
            shape = kind;
            break;
        }
        text = LineBreak().Replace(text, "\n");
        if (text.Length == 0) text = id.Value;
        if (!index.TryGetValue(id.Value, out node))
        {
            node = nodes.Count;
            index[id.Value] = node;
            nodes.Add(new FlowNode(id.Value, text, shape ?? FlowShape.Rectangle));
        }
        else if (shape is { } known) nodes[node] = new FlowNode(id.Value, text, known);
        return true;
    }
}
