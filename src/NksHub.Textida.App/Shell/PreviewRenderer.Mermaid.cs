using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Markdown;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The Mermaid half of <see cref="PreviewRenderer"/>: a flowchart is drawn with borders, polygons, lines and text on a canvas, so nothing runs and nothing is fetched. A diagram of another kind, or one
/// that uses what the subset of <see cref="MermaidFlowchart"/> lacks, is shown as its source with a note. The drawing has the names of its nodes and connections as the name a screen reader gets.
/// </summary>
internal static partial class PreviewRenderer
{
    private static Control Diagram(string source, Ctx ctx)
    {
        if (MermaidFlowchart.Parse(source, PluginLimits.PreviewMermaidMaxNodes, PluginLimits.PreviewMermaidMaxEdges) is { } chart) return DrawFlowchart(chart);
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new SelectableTextBlock { Text = Loc.Get(K.PreviewMermaidUnsupported), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(CodeBlock(source, ctx));
        return panel;
    }

    internal static Control DrawFlowchart(Flowchart chart)
    {
        var layout = FlowchartLayout.Compute(chart);
        var canvas = new Canvas { Width = layout.Width, Height = layout.Height };
        foreach (var frame in layout.Groups) canvas.Children.Add(DrawGroup(frame));
        foreach (var placed in layout.Edges)
        {
            var edge = placed.Edge;
            var line = new Polyline { Points = [.. placed.Points.Select(p => new Point(p.X, p.Y))], StrokeThickness = edge.Line == FlowLine.Thick ? 3 : 1.5 };
            line.Classes.Add("mermaidedge");
            if (edge.Line == FlowLine.Dotted) line.StrokeDashArray = [4, 3];
            canvas.Children.Add(line);
            if (edge.Arrow) canvas.Children.Add(ArrowHead(placed.Points[^2], placed.Points[^1]));
            if (edge.Label is { } label && placed.Label is { } box)
            {
                var text = new TextBlock { Text = label, Width = box.Width, TextAlignment = TextAlignment.Center };
                text.Classes.Add("mermaidlabel");
                Canvas.SetLeft(text, box.X);
                Canvas.SetTop(text, box.Y);
                canvas.Children.Add(text);
            }
        }
        for (int i = 0; i < chart.Nodes.Count; i++) canvas.Children.Add(DrawNode(chart.Nodes[i], layout.Nodes[i]));

        string Name(int node) => chart.Nodes[node].Text.ReplaceLineEndings(" ");
        string End(int node, int group) => group >= 0 ? chart.Groups[group].Title.ReplaceLineEndings(" ") : Name(node);
        string Described(int node) => chart.Groups.FirstOrDefault(g => g.Nodes.Contains(node)) is { } group ? Name(node) + " (" + group.Title.ReplaceLineEndings(" ") + ")" : Name(node);
        var connections = chart.Edges.Select(e => e.Label is { } label
            ? Loc.Get(K.PreviewMermaidEdgeLabel, ("from", End(e.From, e.FromGroup)), ("to", End(e.To, e.ToGroup)), ("label", label))
            : Loc.Get(K.PreviewMermaidEdge, ("from", End(e.From, e.FromGroup)), ("to", End(e.To, e.ToGroup))));
        var scroll = new ScrollViewer { Content = canvas, Focusable = true, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(scroll, Loc.Get(K.PreviewMermaidName, ("nodes", string.Join(", ", chart.Nodes.Select((_, i) => Described(i)))), ("edges", string.Join("; ", connections))));
        return scroll;
    }

    private static Control DrawNode(FlowNode node, FlowBox box)
    {
        var text = new TextBlock { Text = node.Text, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Control shape;
        switch (node.Shape)
        {
            case FlowShape.Diamond:
                shape = new Polygon { Points = [new Point(box.Width / 2, 0), new Point(box.Width, box.Height / 2), new Point(box.Width / 2, box.Height), new Point(0, box.Height / 2)] };
                break;
            case FlowShape.Circle:
                shape = new Ellipse();
                break;
            case FlowShape.Hexagon or FlowShape.Parallelogram or FlowShape.ParallelogramBack:
                shape = new Polygon { Points = [.. FlowchartLayout.Polygon(box.Width, box.Height, node.Shape).Select(p => new Point(p.X, p.Y))] };
                break;
            case FlowShape.Cylinder:
                shape = new Avalonia.Controls.Shapes.Path { Data = Cylinder(box.Width, box.Height) };
                break;
            case FlowShape.Stadium:
                shape = new Border { CornerRadius = new CornerRadius(box.Height / 2) };
                break;
            default:
                shape = new Border { CornerRadius = new CornerRadius(node.Shape == FlowShape.Rounded ? box.Height / 2.5 : 4) };
                break;
        }
        shape.Classes.Add("mermaidshape");
        var holder = new Panel { Width = box.Width, Height = box.Height, Children = { shape, text } };
        Canvas.SetLeft(holder, box.X);
        Canvas.SetTop(holder, box.Y);
        return holder;
    }

    private static Geometry Cylinder(double width, double height)
    {
        double cap = FlowchartLayout.CylinderCap;
        string F(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        string rim = $"{F(width / 2)},{F(cap)} 0 0";
        return Geometry.Parse($"M 0,{F(cap)} A {rim} 1 {F(width)},{F(cap)} L {F(width)},{F(height - cap)} A {F(width / 2)},{F(cap)} 0 0 1 0,{F(height - cap)} Z M 0,{F(cap)} A {rim} 0 {F(width)},{F(cap)}");
    }

    private static Control DrawGroup(PlacedGroup frame)
    {
        var box = frame.Box;
        var title = new TextBlock { Text = frame.Title.ReplaceLineEndings(" "), Width = box.Width, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
        title.Classes.Add("mermaidlabel");
        var border = new Border { Width = box.Width, Height = box.Height, CornerRadius = new CornerRadius(6), Child = title, VerticalAlignment = VerticalAlignment.Top };
        border.Classes.Add("mermaidgroup");
        Canvas.SetLeft(border, box.X);
        Canvas.SetTop(border, box.Y);
        return border;
    }

    private static Polygon ArrowHead(FlowPoint from, FlowPoint tip)
    {
        double dx = tip.X - from.X, dy = tip.Y - from.Y, length = Math.Max(1e-6, Math.Sqrt(dx * dx + dy * dy)), ux = dx / length, uy = dy / length;
        var back = new Point(tip.X - ux * 10, tip.Y - uy * 10);
        var arrow = new Polygon { Points = [new Point(tip.X, tip.Y), new Point(back.X - uy * 4.5, back.Y + ux * 4.5), new Point(back.X + uy * 4.5, back.Y - ux * 4.5)] };
        arrow.Classes.Add("mermaidarrow");
        return arrow;
    }
}
