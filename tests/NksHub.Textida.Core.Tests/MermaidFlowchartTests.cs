using NksHub.Textida.Core.Markdown;

namespace NksHub.Textida.Core.Tests;

/// <summary>P9.3.1, P9.3.3: the Mermaid flowchart subset the preview draws without a script engine, and where its parts are placed.</summary>
public class MermaidFlowchartTests
{
    private static Flowchart? Parse(string source, int maxNodes = 60, int maxEdges = 120) => MermaidFlowchart.Parse(source, maxNodes, maxEdges);

    private static Flowchart Chart(string source) => Parse(source) ?? throw new InvalidOperationException("The chart was not read: " + source);

    private static string Names(Flowchart chart) => string.Join(",", chart.Nodes.Select(n => n.Id + "=" + n.Text + "/" + n.Shape));

    private static string Links(Flowchart chart) =>
        string.Join(",", chart.Edges.Select(e => chart.Nodes[e.From].Id + (e.Arrow ? ">" : "-") + chart.Nodes[e.To].Id + (e.Label is null ? "" : ":" + e.Label) + "/" + e.Line));

    [Fact]
    public void Nodes_shapes_and_labelled_connections_are_read()
    {
        var chart = Chart("graph TD\n A[Start] --> B{Is it?}\n B -->|yes| C(Done)\n B -- no --> D((Stop))\n");
        Assert.Equal(FlowDirection.TopDown, chart.Direction);
        Assert.Equal("A=Start/Rectangle,B=Is it?/Diamond,C=Done/Rounded,D=Stop/Circle", Names(chart));
        Assert.Equal("A>B/Solid,B>C:yes/Solid,B>D:no/Solid", Links(chart));
    }

    [Theory]
    [InlineData("graph", FlowDirection.TopDown)]
    [InlineData("graph TD", FlowDirection.TopDown)]
    [InlineData("flowchart TB", FlowDirection.TopDown)]
    [InlineData("GRAPH bt", FlowDirection.BottomUp)]
    [InlineData("flowchart LR", FlowDirection.LeftRight)]
    [InlineData("graph RL;", FlowDirection.RightLeft)]
    public void The_direction_follows_the_header(string header, FlowDirection expected) => Assert.Equal(expected, Chart(header + "\nA-->B").Direction);

    [Fact]
    public void Chains_groups_and_semicolons_make_the_connections()
    {
        Assert.Equal("A>B/Solid,B>C/Solid,A>D/Solid,B>D/Solid", Links(Chart("graph LR\nA --> B --> C\nA & B --> D")));
        var chart = Chart("graph LR; A-->B; B-->C");
        Assert.Equal("A=A/Rectangle,B=B/Rectangle,C=C/Rectangle", Names(chart));
        Assert.Equal("A>B/Solid,B>C/Solid", Links(chart));
    }

    [Fact]
    public void Lines_arrows_and_thick_or_dotted_connections_are_told_apart()
    {
        var chart = Chart("graph TD\nA --- B\nA -.-> C\nA ==> D\nA -.- E\nA === F\nA ---> G\nA -. why .-> H");
        Assert.Equal("A-B/Solid,A>C/Dotted,A>D/Thick,A-E/Dotted,A-F/Thick,A>G/Solid,A>H:why/Dotted", Links(chart));
    }

    [Fact]
    public void Styling_lines_and_comments_are_skipped()
    {
        var chart = Chart("graph TD\n%% a comment\nA-->B\nstyle A fill:#f9f\nclassDef x fill:#fff\nclass A x\nlinkStyle 0 stroke:red\nclick A callback");
        Assert.Equal("A=A/Rectangle,B=B/Rectangle", Names(chart));
        Assert.Equal("A>B/Solid", Links(chart));
    }

    [Fact]
    public void Quoted_text_line_breaks_and_slanted_boxes_give_plain_text()
    {
        var chart = Chart("graph TD\nA[\"Hello (world)\"] --> B[line one<br/>line two]\nC[/slanted/]\nD[\"a; b\"]");
        Assert.Equal("Hello (world)", chart.Nodes[0].Text);
        Assert.Equal("line one\nline two", chart.Nodes[1].Text);
        Assert.Equal("slanted", chart.Nodes[2].Text);
        Assert.Equal("a; b", chart.Nodes[3].Text);
    }

    [Fact]
    public void A_node_defined_again_with_text_takes_the_new_text_and_a_bare_mention_keeps_it()
    {
        Assert.Equal("A=Alpha/Rectangle,B=B/Rectangle", Names(Chart("graph TD\nA --> B\nA[Alpha]")));
        Assert.Equal("A=Alpha/Rectangle,B=B/Rectangle,C=C/Rectangle", Names(Chart("graph TD\nA[Alpha] --> B\nA --> C")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("graph TD")]
    [InlineData("sequenceDiagram\nA->>B: hi")]
    [InlineData("pie title Pets\n\"Dogs\" : 3")]
    [InlineData("graph TD\nsubgraph s\nA-->B\nend")]
    [InlineData("graph TD\nA-->B:::red")]
    [InlineData("graph TD\nA--oB")]
    [InlineData("graph TD\nA[unterminated --> B")]
    [InlineData("---\ntitle: x\n---\ngraph TD\nA-->B")]
    public void Anything_outside_the_subset_is_not_read_so_the_preview_shows_the_source(string source) => Assert.Null(Parse(source));

    [Fact]
    public void A_chart_over_the_limits_is_not_read()
    {
        Assert.Null(Parse("graph TD\nA-->B-->C-->D", maxNodes: 3));
        Assert.NotNull(Parse("graph TD\nA-->B-->C", maxNodes: 3));
        Assert.Null(Parse("graph TD\nA-->B-->C-->D", maxEdges: 2));
        Assert.NotNull(Parse("graph TD\nA-->B-->C", maxEdges: 2));
    }

    // ---- layout ----

    private static FlowchartLayout Layout(string source) => FlowchartLayout.Compute(Chart(source));

    /// <summary>Whether the point lies on the outline of the shape drawn in the box.</summary>
    private static bool OnOutline(FlowPoint p, FlowBox b, FlowShape shape)
    {
        double dx = Math.Abs(p.X - b.Center.X) / (b.Width / 2), dy = Math.Abs(p.Y - b.Center.Y) / (b.Height / 2);
        return shape switch
        {
            FlowShape.Diamond => Math.Abs(dx + dy - 1) < 0.01,
            FlowShape.Circle => Math.Abs(dx * dx + dy * dy - 1) < 0.01,
            _ => Math.Abs(Math.Max(dx, dy) - 1) < 0.01,
        };
    }

    private static void Near(FlowPoint expected, FlowPoint actual)
    {
        Assert.Equal(expected.X, actual.X, 0.001);
        Assert.Equal(expected.Y, actual.Y, 0.001);
    }

    private static bool Overlap(FlowBox a, FlowBox b) => a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    [Theory]
    [InlineData("graph TD", 0)]
    [InlineData("graph BT", 1)]
    [InlineData("graph LR", 2)]
    [InlineData("graph RL", 3)]
    public void A_chain_runs_along_its_direction_and_stays_on_one_line_across_it(string header, int kind)
    {
        var nodes = Layout(header + "\nA-->B\nB-->C").Nodes;
        double[] along = [.. nodes.Select(n => kind < 2 ? n.Center.Y : n.Center.X)];
        double[] across = [.. nodes.Select(n => kind < 2 ? n.Center.X : n.Center.Y)];
        Assert.True(kind is 0 or 2 ? along[0] < along[1] && along[1] < along[2] : along[0] > along[1] && along[1] > along[2], string.Join(",", along));
        Assert.All(across, a => Assert.Equal(across[0], a, 0.5));
    }

    [Fact]
    public void Nodes_of_one_layer_do_not_overlap_and_a_join_sits_between_its_branches()
    {
        var layout = Layout("graph TD\nA-->B\nA-->C\nB-->D\nC-->D");
        var n = layout.Nodes;
        Assert.False(Overlap(n[1], n[2]));
        Assert.Equal(n[1].Center.Y, n[2].Center.Y, 0.5);
        Assert.True(n[0].Center.Y < n[1].Center.Y && n[1].Center.Y < n[3].Center.Y);
        Assert.Equal((n[1].Center.X + n[2].Center.X) / 2, n[3].Center.X, 1.0);
        for (int i = 0; i < n.Count; i++)
            for (int j = i + 1; j < n.Count; j++)
                Assert.False(Overlap(n[i], n[j]), $"{i} and {j} overlap");
    }

    [Fact]
    public void Connections_start_and_end_on_the_borders_of_their_nodes_and_everything_fits_the_drawing()
    {
        var chart = Chart("graph LR\nA[Start] --> B{Check}\nB -->|yes| C((Go))\nB -->|no| D(Stop)\nC --> E[End]\nD --> E");
        var layout = FlowchartLayout.Compute(chart);
        foreach (var edge in layout.Edges)
        {
            Assert.True(OnOutline(edge.Points[0], layout.Nodes[edge.Edge.From], chart.Nodes[edge.Edge.From].Shape), $"start of {edge.Edge}");
            Assert.True(OnOutline(edge.Points[^1], layout.Nodes[edge.Edge.To], chart.Nodes[edge.Edge.To].Shape), $"end of {edge.Edge}");
        }
        foreach (var box in layout.Nodes) Assert.True(box.X >= 0 && box.Y >= 0 && box.X + box.Width <= layout.Width && box.Y + box.Height <= layout.Height);
        foreach (var point in layout.Edges.SelectMany(e => e.Points)) Assert.True(point.X >= 0 && point.Y >= 0 && point.X <= layout.Width && point.Y <= layout.Height);
        foreach (var edge in layout.Edges.Where(e => e.Label is not null))
            Assert.True(edge.Label!.Value.X >= 0 && edge.Label.Value.X + edge.Label.Value.Width <= layout.Width);
    }

    [Fact]
    public void A_cycle_is_laid_out_in_the_direction_of_its_first_edges_and_the_closing_edge_goes_back()
    {
        var layout = Layout("graph TD\nA-->B\nB-->C\nC-->A");
        var n = layout.Nodes;
        Assert.True(n[0].Center.Y < n[1].Center.Y && n[1].Center.Y < n[2].Center.Y);
        var back = layout.Edges[2];
        Assert.Equal(3, back.Points.Count); // it skips a layer, so it passes one spacer point
        Assert.True(OnOutline(back.Points[0], n[2], FlowShape.Rectangle));
        Assert.True(OnOutline(back.Points[^1], n[0], FlowShape.Rectangle));
        Assert.True(back.Points[0].Y > back.Points[^1].Y);
    }

    [Fact]
    public void An_edge_that_skips_a_layer_passes_beside_the_node_in_between()
    {
        var layout = Layout("graph TD\nA-->B\nB-->C\nA-->C");
        var skipping = layout.Edges[2];
        Assert.Equal(3, skipping.Points.Count);
        var b = layout.Nodes[1];
        var spacer = skipping.Points[1];
        Assert.False(spacer.X >= b.X && spacer.X <= b.X + b.Width && spacer.Y >= b.Y && spacer.Y <= b.Y + b.Height);
    }

    [Fact]
    public void A_loop_is_drawn_beside_its_node_and_a_label_sits_on_its_edge()
    {
        var loop = Layout("graph TD\nA-->A").Edges[0];
        Assert.Equal(4, loop.Points.Count);
        var layout = Layout("graph TD\nA-->|yes|B");
        var edge = layout.Edges[0];
        var label = edge.Label!.Value;
        Assert.Equal((edge.Points[0].X + edge.Points[1].X) / 2, label.Center.X, 0.5);
        Assert.Equal((edge.Points[0].Y + edge.Points[1].Y) / 2, label.Center.Y, 0.5);
        Assert.Null(Layout("graph TD\nA-->B").Edges[0].Label);
    }

    [Fact]
    public void A_diamond_and_a_circle_get_more_room_than_a_box_for_the_same_text_and_the_border_follows_the_shape()
    {
        var box = FlowchartLayout.Measure(new FlowNode("A", "Decide", FlowShape.Rectangle));
        var diamond = FlowchartLayout.Measure(new FlowNode("A", "Decide", FlowShape.Diamond));
        var circle = FlowchartLayout.Measure(new FlowNode("A", "Decide", FlowShape.Circle));
        Assert.True(diamond.W > box.W && diamond.H > box.H);
        Assert.True(circle.W > box.W && circle.H > box.H);

        var b = new FlowBox(0, 0, 100, 60);
        Near(new FlowPoint(100, 30), FlowchartLayout.Border(b, FlowShape.Rectangle, new FlowPoint(300, 30)));
        Near(new FlowPoint(100, 30), FlowchartLayout.Border(b, FlowShape.Diamond, new FlowPoint(300, 30)));
        Near(new FlowPoint(50, 0), FlowchartLayout.Border(b, FlowShape.Circle, new FlowPoint(50, -300)));
        var onDiamond = FlowchartLayout.Border(b, FlowShape.Diamond, new FlowPoint(300, 300));
        Assert.Equal(1.0, Math.Abs(onDiamond.X - 50) / 50 + Math.Abs(onDiamond.Y - 30) / 30, 0.01);
    }
}
