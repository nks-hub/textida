namespace NksHub.Textida.Core.Markdown;

public readonly record struct FlowPoint(double X, double Y);

public readonly record struct FlowBox(double X, double Y, double Width, double Height)
{
    public FlowPoint Center => new(X + Width / 2, Y + Height / 2);
}

/// <summary>An edge drawn as a path from the border of its first node to the border of its last; <see cref="Label"/> is where the label goes, null for an edge without one.</summary>
public sealed record PlacedEdge(FlowEdge Edge, IReadOnlyList<FlowPoint> Points, FlowBox? Label);

/// <summary>
/// Where the parts of a <see cref="Flowchart"/> go: the box of every node (in the order of <see cref="Flowchart.Nodes"/>) and the path of every edge, in a drawing of the given size.
/// Layers by the longest path (an edge against the flow is reversed for the layering only), edges that skip a layer pass through spacer points, the order inside a layer follows the
/// average place of the neighbours, and sizes come from the display width of the text, so the result is the same on every platform and needs no font.
/// shortcut: no crossing minimisation beyond four sweeps and no separate routing around nodes; a very tangled chart is drawn correctly but not prettily.
/// </summary>
public sealed record FlowchartLayout(double Width, double Height, IReadOnlyList<FlowBox> Nodes, IReadOnlyList<PlacedEdge> Edges)
{
    public const double CharWidth = 7.5;
    public const double LineHeight = 18;
    private const double GapAcross = 28, GapAlong = 48, Margin = 12, Spacer = 6;

    public static FlowchartLayout Compute(Flowchart chart)
    {
        int n = chart.Nodes.Count;
        bool vertical = chart.Direction is FlowDirection.TopDown or FlowDirection.BottomUp;
        var size = chart.Nodes.Select(Measure).ToList();

        // layering: reverse the edges that close a cycle, then the longest path from the sources
        var outgoing = Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray();
        foreach (var e in chart.Edges) outgoing[e.From].Add(e.To);
        var back = new HashSet<(int, int)>();
        var state = new int[n];
        void Visit(int u)
        {
            state[u] = 1;
            foreach (int v in outgoing[u])
            {
                if (v == u) continue;
                if (state[v] == 1) back.Add((u, v));
                else if (state[v] == 0) Visit(v);
            }
            state[u] = 2;
        }
        for (int u = 0; u < n; u++) if (state[u] == 0) Visit(u);

        var forward = chart.Edges.Select((e, k) => (K: k, A: back.Contains((e.From, e.To)) ? e.To : e.From, B: back.Contains((e.From, e.To)) ? e.From : e.To)).Where(e => e.A != e.B).ToList();
        var layerOf = new List<int>(new int[n]);
        var below = Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray();
        var indegree = new int[n];
        foreach (var (_, a, b) in forward) { below[a].Add(b); indegree[b]++; }
        var ready = new Queue<int>(Enumerable.Range(0, n).Where(v => indegree[v] == 0));
        while (ready.Count > 0)
        {
            int u = ready.Dequeue();
            foreach (int v in below[u])
            {
                layerOf[v] = Math.Max(layerOf[v], layerOf[u] + 1);
                if (--indegree[v] == 0) ready.Enqueue(v);
            }
        }

        // a spacer point in every layer an edge passes
        var up = Enumerable.Range(0, n).Select(_ => new List<int>()).ToList();
        var down = Enumerable.Range(0, n).Select(_ => new List<int>()).ToList();
        var paths = new List<int>[chart.Edges.Count];
        foreach (var (k, a, b) in forward)
        {
            var chain = new List<int> { a };
            for (int l = layerOf[a] + 1; l < layerOf[b]; l++)
            {
                chain.Add(size.Count);
                size.Add((Spacer, Spacer));
                layerOf.Add(l);
                up.Add([]);
                down.Add([]);
            }
            chain.Add(b);
            for (int i = 0; i + 1 < chain.Count; i++)
            {
                down[chain[i]].Add(chain[i + 1]);
                up[chain[i + 1]].Add(chain[i]);
            }
            if (back.Contains((chart.Edges[k].From, chart.Edges[k].To))) chain.Reverse();
            paths[k] = chain;
        }

        // order inside the layers
        int layers = layerOf.Max() + 1;
        var order = Enumerable.Range(0, layers).Select(_ => new List<int>()).ToArray();
        for (int v = 0; v < size.Count; v++) order[layerOf[v]].Add(v);
        var place = new double[size.Count];
        void Number(List<int> layer)
        {
            for (int i = 0; i < layer.Count; i++) place[layer[i]] = i;
        }
        foreach (var layer in order) Number(layer);
        void Sort(List<int> layer, List<List<int>> neighbours)
        {
            var sorted = layer.OrderBy(v => neighbours[v].Count == 0 ? place[v] : neighbours[v].Average(u => place[u])).ToList();
            layer.Clear();
            layer.AddRange(sorted);
            Number(layer);
        }
        for (int pass = 0; pass < 4; pass++)
        {
            for (int l = 1; l < layers; l++) Sort(order[l], up);
            for (int l = layers - 2; l >= 0; l--) Sort(order[l], down);
        }

        // positions across the flow: packed and centred, then pulled towards the neighbours without changing the order
        double Across(int v) => vertical ? size[v].W : size[v].H;
        var across = new double[size.Count];
        double widest = order.Max(layer => layer.Sum(Across) + (layer.Count - 1) * GapAcross);
        foreach (var layer in order)
        {
            double x = (widest - (layer.Sum(Across) + (layer.Count - 1) * GapAcross)) / 2;
            foreach (int v in layer)
            {
                across[v] = x + Across(v) / 2;
                x += Across(v) + GapAcross;
            }
        }
        for (int pass = 0; pass < 2; pass++)
        {
            for (int l = 1; l < layers; l++) Relax(order[l], up);
            for (int l = layers - 2; l >= 0; l--) Relax(order[l], down);
        }
        // every node wants the average place of its neighbours; where that would make nodes overlap they share the move, so a pair of branches stays centred under its parent
        void Relax(List<int> layer, List<List<int>> neighbours)
        {
            var starts = new double[layer.Count];
            var blocks = new List<(double Sum, int Count)>();
            double offset = 0;
            for (int i = 0; i < layer.Count; i++)
            {
                int v = layer[i];
                if (i > 0) offset += (Across(layer[i - 1]) + Across(v)) / 2 + GapAcross;
                starts[i] = offset;
                blocks.Add(((neighbours[v].Count == 0 ? across[v] : neighbours[v].Average(u => across[u])) - offset, 1));
                while (blocks.Count > 1 && blocks[^2].Sum / blocks[^2].Count > blocks[^1].Sum / blocks[^1].Count)
                {
                    var last = blocks[^1];
                    blocks.RemoveAt(blocks.Count - 1);
                    blocks[^1] = (blocks[^1].Sum + last.Sum, blocks[^1].Count + last.Count);
                }
            }
            int index = 0;
            foreach (var (sum, count) in blocks)
                for (int k = 0; k < count; k++, index++) across[layer[index]] = sum / count + starts[index];
        }

        // positions along the flow
        var thickness = order.Select(layer => layer.Where(v => v < n).Select(v => vertical ? size[v].H : size[v].W).DefaultIfEmpty(0).Max()).ToArray();
        var along = new double[layers];
        double reach = 0;
        for (int l = 0; l < layers; l++)
        {
            along[l] = reach + thickness[l] / 2;
            reach += thickness[l] + GapAlong;
        }
        double length = reach - GapAlong;
        FlowPoint At(int v)
        {
            double a = across[v], b = along[layerOf[v]];
            return chart.Direction switch
            {
                FlowDirection.TopDown => new(a, b),
                FlowDirection.BottomUp => new(a, length - b),
                FlowDirection.LeftRight => new(b, a),
                _ => new(length - b, a),
            };
        }

        var boxes = new FlowBox[n];
        for (int v = 0; v < n; v++)
        {
            var c = At(v);
            boxes[v] = new FlowBox(c.X - size[v].W / 2, c.Y - size[v].H / 2, size[v].W, size[v].H);
        }

        var placed = new List<PlacedEdge>();
        for (int k = 0; k < chart.Edges.Count; k++)
        {
            var e = chart.Edges[k];
            List<FlowPoint> points;
            if (e.From == e.To)
            {
                var b = boxes[e.From];
                var (right, middle) = (b.X + b.Width + 24, b.Y + b.Height / 2);
                var (top, bottom) = (new FlowPoint(right, middle - b.Height / 4), new FlowPoint(right, middle + b.Height / 4));
                points = [Border(b, chart.Nodes[e.From].Shape, top), top, bottom, Border(b, chart.Nodes[e.From].Shape, bottom)];
            }
            else
            {
                var centres = paths[k].Select(At).ToList();
                points = [Border(boxes[e.From], chart.Nodes[e.From].Shape, centres[1]), .. centres.Skip(1).Take(centres.Count - 2), Border(boxes[e.To], chart.Nodes[e.To].Shape, centres[^2])];
            }
            FlowBox? label = null;
            if (e.Label is { } text)
            {
                int s = (points.Count - 1) / 2;
                var mid = new FlowPoint((points[s].X + points[s + 1].X) / 2, (points[s].Y + points[s + 1].Y) / 2);
                double w = MarkdownTools.DisplayWidth(text) * CharWidth + 10;
                label = new FlowBox(mid.X - w / 2, mid.Y - LineHeight / 2, w, LineHeight);
            }
            placed.Add(new PlacedEdge(e, points, label));
        }

        // move everything into view and measure the drawing
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        void Grow(double x0, double y0, double x1, double y1)
        {
            (minX, minY, maxX, maxY) = (Math.Min(minX, x0), Math.Min(minY, y0), Math.Max(maxX, x1), Math.Max(maxY, y1));
        }
        foreach (var b in boxes) Grow(b.X, b.Y, b.X + b.Width, b.Y + b.Height);
        foreach (var p in placed)
        {
            foreach (var pt in p.Points) Grow(pt.X, pt.Y, pt.X, pt.Y);
            if (p.Label is { } l) Grow(l.X, l.Y, l.X + l.Width, l.Y + l.Height);
        }
        double dx = Margin - minX, dy = Margin - minY;
        return new FlowchartLayout(maxX - minX + 2 * Margin, maxY - minY + 2 * Margin,
            [.. boxes.Select(b => b with { X = b.X + dx, Y = b.Y + dy })],
            [.. placed.Select(p => new PlacedEdge(p.Edge, [.. p.Points.Select(pt => new FlowPoint(pt.X + dx, pt.Y + dy))], p.Label is { } l ? l with { X = l.X + dx, Y = l.Y + dy } : null))]);
    }

    /// <summary>The size a node is drawn at: its text plus room, and for a diamond or an ellipse enough room that the text stays inside the outline.</summary>
    public static (double W, double H) Measure(FlowNode node)
    {
        var lines = node.Text.Split('\n');
        double text = lines.Max(MarkdownTools.DisplayWidth) * CharWidth + 28, height = lines.Length * LineHeight + 16;
        return node.Shape switch
        {
            FlowShape.Diamond => (Math.Max(72, text * 1.6), Math.Max(52, height * 1.7)),
            FlowShape.Circle => (Math.Max(52, text * 1.2), Math.Max(52, height * 1.5)),
            _ => (Math.Max(52, text), Math.Max(34, height)),
        };
    }

    /// <summary>The point where the line from the centre of the node to <paramref name="toward"/> leaves the outline of the shape.</summary>
    public static FlowPoint Border(FlowBox box, FlowShape shape, FlowPoint toward)
    {
        var c = box.Center;
        double dx = toward.X - c.X, dy = toward.Y - c.Y, hw = box.Width / 2, hh = box.Height / 2;
        if (dx == 0 && dy == 0) return c;
        double t = shape switch
        {
            FlowShape.Diamond => 1 / (Math.Abs(dx) / hw + Math.Abs(dy) / hh),
            FlowShape.Circle => 1 / Math.Sqrt(dx / hw * (dx / hw) + dy / hh * (dy / hh)),
            _ => 1 / Math.Max(Math.Abs(dx) / hw, Math.Abs(dy) / hh),
        };
        t = Math.Min(t, 1);
        return new FlowPoint(c.X + dx * t, c.Y + dy * t);
    }
}
