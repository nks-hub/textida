namespace NksHub.Textida.Core.Markdown;

public readonly record struct FlowPoint(double X, double Y);

public readonly record struct FlowBox(double X, double Y, double Width, double Height)
{
    public FlowPoint Center => new(X + Width / 2, Y + Height / 2);
}

/// <summary>The frame of a subgraph (index into <see cref="Flowchart.Groups"/>) with its title, enclosing the boxes of its nodes.</summary>
public sealed record PlacedGroup(int Group, string Title, FlowBox Box);

/// <summary>An edge drawn as a path from the border of its first node to the border of its last; <see cref="Label"/> is where the label goes, null for an edge without one.</summary>
public sealed record PlacedEdge(FlowEdge Edge, IReadOnlyList<FlowPoint> Points, FlowBox? Label);

/// <summary>
/// Where the parts of a <see cref="Flowchart"/> go: the box of every node (in the order of <see cref="Flowchart.Nodes"/>) and the path of every edge, in a drawing of the given size.
/// Layers by the longest path (an edge against the flow is reversed for the layering only), edges that skip a layer pass through spacer points, the order inside a layer follows the
/// average place of the neighbours, and sizes come from the display width of the text, so the result is the same on every platform and needs no font.
/// The nodes of a subgraph are kept side by side inside every layer and given room for the frame, which is the box around them; the layers are not made to respect the frames, so a chart that
/// mixes the nodes of two subgraphs across layers can have frames that overlap a foreign node.
/// shortcut: no crossing minimisation beyond four sweeps and no separate routing around nodes; a very tangled chart is drawn correctly but not prettily.
/// </summary>
public sealed record FlowchartLayout(double Width, double Height, IReadOnlyList<FlowBox> Nodes, IReadOnlyList<PlacedEdge> Edges, IReadOnlyList<PlacedGroup> Groups)
{
    public const double CharWidth = 7.5;
    public const double LineHeight = 18;
    private const double GapAcross = 28, GapAlong = 48, Margin = 12, Spacer = 6, FramePad = 10, GroupSpace = 2 * FramePad + LineHeight;

    /// <summary>The height of the rim of a cylinder, drawn and left free of text.</summary>
    public const double CylinderCap = 9;

    public static FlowchartLayout Compute(Flowchart chart)
    {
        int n = chart.Nodes.Count;
        bool vertical = chart.Direction is FlowDirection.TopDown or FlowDirection.BottomUp;
        var size = chart.Nodes.Select(Measure).ToList();

        // the subgraphs a node is in, outermost first; a point that only keeps an edge apart is in none
        var groups = chart.Groups;
        var owner = Enumerable.Repeat(-1, n).ToArray();
        for (int g = 0; g < groups.Count; g++) foreach (int v in groups[g].Nodes) owner[v] = g;
        var chains = new List<int>[n];
        for (int v = 0; v < n; v++)
        {
            chains[v] = [];
            for (int g = owner[v]; g >= 0; g = groups[g].Parent) chains[v].Insert(0, g);
        }
        IReadOnlyList<int> Chain(int v) => v < n ? chains[v] : [];

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
            if (groups.Count > 0) sorted = Cluster(sorted, 0);
            layer.Clear();
            layer.AddRange(sorted);
            Number(layer);
        }
        // the members of a subgraph stay together in a layer, the group standing where the average of its members stood
        List<int> Cluster(List<int> items, int depth)
        {
            var units = new List<(List<int> Items, List<int> At)>();
            var byGroup = new Dictionary<int, int>();
            for (int i = 0; i < items.Count; i++)
            {
                var chain = Chain(items[i]);
                if (chain.Count <= depth) units.Add(([items[i]], [i]));
                else if (byGroup.TryGetValue(chain[depth], out int unit)) { units[unit].Items.Add(items[i]); units[unit].At.Add(i); }
                else { byGroup[chain[depth]] = units.Count; units.Add(([items[i]], [i])); }
            }
            return [.. units.OrderBy(u => u.At.Average()).SelectMany(u => Chain(u.Items[0]).Count <= depth ? u.Items : Cluster(u.Items, depth + 1))];
        }
        for (int pass = 0; pass < 4; pass++)
        {
            for (int l = 1; l < layers; l++) Sort(order[l], up);
            for (int l = layers - 2; l >= 0; l--) Sort(order[l], down);
        }

        // positions across the flow: packed and centred, then pulled towards the neighbours without changing the order
        double Across(int v) => vertical ? size[v].W : size[v].H;
        // room between neighbours: every frame that ends at one and starts at the other needs its own space
        double Gap(int a, int b)
        {
            var (x, y) = (Chain(a), Chain(b));
            int common = 0;
            while (common < x.Count && common < y.Count && x[common] == y[common]) common++;
            return GapAcross + GroupSpace * (x.Count + y.Count - 2 * common);
        }
        double Span(List<int> layer) => layer.Sum(Across) + Enumerable.Range(1, Math.Max(0, layer.Count - 1)).Sum(i => Gap(layer[i - 1], layer[i]));
        var across = new double[size.Count];
        double widest = order.Max(Span);
        foreach (var layer in order)
        {
            double x = (widest - Span(layer)) / 2;
            for (int i = 0; i < layer.Count; i++)
            {
                int v = layer[i];
                across[v] = x + Across(v) / 2;
                x += Across(v) + (i + 1 < layer.Count ? Gap(v, layer[i + 1]) : 0);
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
                if (i > 0) offset += (Across(layer[i - 1]) + Across(v)) / 2 + Gap(layer[i - 1], v);
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
        // a frame that starts or ends between two layers needs room there
        var extra = new double[layers];
        for (int g = 0; g < groups.Count; g++)
        {
            var members = Enumerable.Range(0, n).Where(v => chains[v].Contains(g)).Select(v => layerOf[v]).ToList();
            if (members.Count == 0) continue;
            if (members.Min() > 0) extra[members.Min() - 1] += GroupSpace;
            if (members.Max() < layers - 1) extra[members.Max()] += GroupSpace;
        }
        var along = new double[layers];
        double reach = 0;
        for (int l = 0; l < layers; l++)
        {
            along[l] = reach + thickness[l] / 2;
            reach += thickness[l] + (l < layers - 1 ? GapAlong + extra[l] : 0);
        }
        double length = reach;
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

        // the frames, the inner ones first so that an enclosing frame takes them in
        var frames = new FlowBox?[groups.Count];
        for (int g = groups.Count - 1; g >= 0; g--)
        {
            var parts = groups[g].Nodes.Select(v => boxes[v]).Concat(Enumerable.Range(g + 1, groups.Count - g - 1).Where(h => groups[h].Parent == g && frames[h] is not null).Select(h => frames[h]!.Value)).ToList();
            if (parts.Count == 0) continue;
            double x0 = parts.Min(b => b.X) - FramePad, x1 = parts.Max(b => b.X + b.Width) + FramePad, y0 = parts.Min(b => b.Y) - FramePad - LineHeight, y1 = parts.Max(b => b.Y + b.Height) + FramePad;
            double lacking = MarkdownTools.DisplayWidth(groups[g].Title) * CharWidth + 2 * FramePad - (x1 - x0);
            if (lacking > 0) (x0, x1) = (x0 - lacking / 2, x1 + lacking / 2);
            frames[g] = new FlowBox(x0, y0, x1 - x0, y1 - y0);
        }
        var framed = Enumerable.Range(0, groups.Count).Where(g => frames[g] is not null).Select(g => new PlacedGroup(g, groups[g].Title, frames[g]!.Value)).ToList();

        // move everything into view and measure the drawing
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        void Grow(double x0, double y0, double x1, double y1)
        {
            (minX, minY, maxX, maxY) = (Math.Min(minX, x0), Math.Min(minY, y0), Math.Max(maxX, x1), Math.Max(maxY, y1));
        }
        foreach (var b in boxes.Concat(framed.Select(f => f.Box))) Grow(b.X, b.Y, b.X + b.Width, b.Y + b.Height);
        foreach (var p in placed)
        {
            foreach (var pt in p.Points) Grow(pt.X, pt.Y, pt.X, pt.Y);
            if (p.Label is { } l) Grow(l.X, l.Y, l.X + l.Width, l.Y + l.Height);
        }
        double dx = Margin - minX, dy = Margin - minY;
        return new FlowchartLayout(maxX - minX + 2 * Margin, maxY - minY + 2 * Margin,
            [.. boxes.Select(b => b with { X = b.X + dx, Y = b.Y + dy })],
            [.. placed.Select(p => new PlacedEdge(p.Edge, [.. p.Points.Select(pt => new FlowPoint(pt.X + dx, pt.Y + dy))], p.Label is { } l ? l with { X = l.X + dx, Y = l.Y + dy } : null))],
            [.. framed.Select(f => f with { Box = f.Box with { X = f.Box.X + dx, Y = f.Box.Y + dy } })]);
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
            FlowShape.Stadium => (Math.Max(Math.Max(52, text) + height / 2, Math.Max(34, height) + 20), Math.Max(34, height)),
            FlowShape.Hexagon => (Math.Max(60, text + height * 2 / 3), Math.Max(34, height)),
            FlowShape.Parallelogram or FlowShape.ParallelogramBack => (Math.Max(60, text + height * 0.6), Math.Max(34, height)),
            FlowShape.Cylinder => (Math.Max(52, text), Math.Max(44, height + 3 * CylinderCap)),
            _ => (Math.Max(52, text), Math.Max(34, height)),
        };
    }

    /// <summary>The corners of a hexagon or a parallelogram that fills a box of the given size, from its top left; empty for the other shapes.</summary>
    public static IReadOnlyList<FlowPoint> Polygon(double width, double height, FlowShape shape)
    {
        double inset = height / 3, slant = height * 0.3;
        return shape switch
        {
            FlowShape.Hexagon => [new(inset, 0), new(width - inset, 0), new(width, height / 2), new(width - inset, height), new(inset, height), new(0, height / 2)],
            FlowShape.Parallelogram => [new(slant, 0), new(width, 0), new(width - slant, height), new(0, height)],
            FlowShape.ParallelogramBack => [new(0, 0), new(width - slant, 0), new(width, height), new(slant, height)],
            _ => [],
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
            FlowShape.Stadium => StadiumReach(hw, hh, dx, dy),
            FlowShape.Hexagon or FlowShape.Parallelogram or FlowShape.ParallelogramBack => PolygonReach(Polygon(box.Width, box.Height, shape), box, dx, dy),
            _ => 1 / Math.Max(Math.Abs(dx) / hw, Math.Abs(dy) / hh),
        };
        t = Math.Min(t, 1);
        return new FlowPoint(c.X + dx * t, c.Y + dy * t);
    }

    /// <summary>How far along (dx, dy), as a fraction of it, a ray from the centre reaches the outline of a stadium: flat top and bottom, half circles at the ends.</summary>
    private static double StadiumReach(double hw, double hh, double dx, double dy)
    {
        double length = Math.Sqrt(dx * dx + dy * dy), ux = dx / length, uy = dy / length, flat = Math.Max(0, hw - hh);
        if (uy != 0 && Math.Abs(ux * (hh / Math.Abs(uy))) <= flat) return hh / Math.Abs(uy) / length;
        double along = Math.Sign(ux) * flat * ux;
        return (along + Math.Sqrt(Math.Max(0, along * along - flat * flat + hh * hh))) / length;
    }

    private static double PolygonReach(IReadOnlyList<FlowPoint> corners, FlowBox box, double dx, double dy)
    {
        var c = box.Center;
        double best = double.MaxValue;
        for (int i = 0; i < corners.Count; i++)
        {
            var (p, q) = (corners[i], corners[(i + 1) % corners.Count]);
            double ex = q.X - p.X, ey = q.Y - p.Y, px = box.X + p.X - c.X, py = box.Y + p.Y - c.Y, den = dx * ey - dy * ex;
            if (Math.Abs(den) < 1e-12) continue;
            double t = (px * ey - py * ex) / den, along = (px * dy - py * dx) / den;
            if (t > 0 && along >= -1e-9 && along <= 1 + 1e-9) best = Math.Min(best, t);
        }
        return best;
    }
}
