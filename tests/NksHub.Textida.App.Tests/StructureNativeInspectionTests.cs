using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.Core.Data.Structure;

namespace NksHub.Textida.App.Tests;

public sealed class StructureNativeInspectionTests
{
    [AvaloniaFact]
    public void Pointer_identity_comes_from_the_actual_header_even_when_a_different_item_was_requested()
    {
        var requested = Item(11, 10, "requested", 40);
        var actual = Item(12, 10, "actual", 40);
        var parent = Item(10, -1, "parent", 40);
        parent.Items.Add(requested); parent.Items.Add(actual); parent.IsExpanded = true;
        var tree = new TreeView(); tree.Items.Add(parent);
        var outside = new Border { Height = 45, Background = Avalonia.Media.Brushes.Gray, Child = new TextBlock { Text = "Outside the measured tree" } };
        var content = new DockPanel(); DockPanel.SetDock(outside, Dock.Bottom);
        content.Children.Add(outside); content.Children.Add(tree);
        var window = new Window { Width = 420, Height = 360, Content = content };
        try
        {
            window.Show(); Layout(window);
            var requestedHeader = StructureNativeDiagnostic.InspectHeader(window, tree, requested);
            var actualHeader = StructureNativeDiagnostic.InspectHeader(window, tree, actual);
            Assert.NotNull(requestedHeader?["headerPoint"]);
            Assert.NotNull(actualHeader?["headerPoint"]);
            JsonObject? observed = null;
            window.AddHandler(InputElement.PointerPressedEvent, (_, e) => observed = StructureNativeDiagnostic.ActualSource(tree, e.Source),
                RoutingStrategies.Tunnel, handledEventsToo: true);
            Point point = ClientPoint(window, actualHeader!["headerPoint"]!);
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Assert.NotNull(observed);
            Assert.True(observed["inTree"]!.GetValue<bool>());
            Assert.True(observed["inHeader"]!.GetValue<bool>());
            Assert.Equal(12, observed["nodeId"]!.GetValue<long>());
            Assert.Equal(10, observed["parentNodeId"]!.GetValue<long>());
            Assert.Equal(10, observed["treeParentNodeId"]!.GetValue<long>());
            Assert.NotEqual(requestedHeader!["headerPoint"]!["nodeId"]!.GetValue<long>(), observed["nodeId"]!.GetValue<long>());
            Assert.NotNull(observed["sourceType"]?.GetValue<string>());

            var outsidePoint = outside.TranslatePoint(new Point(outside.Bounds.Width / 2, outside.Bounds.Height / 2), window);
            Assert.NotNull(outsidePoint);
            window.MouseDown(outsidePoint.Value, MouseButton.Left); window.MouseUp(outsidePoint.Value, MouseButton.Left);
            Assert.False(observed!["inTree"]!.GetValue<bool>());
            Assert.Null(observed["nodeId"]); Assert.Null(observed["parentNodeId"]); Assert.Null(observed["treeParentNodeId"]);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Header_inspection_clips_to_the_actual_viewport_and_rejects_hidden_or_covered_headers()
    {
        var first = Item(1, -1, "first", 60);
        var partial = Item(2, -1, "partially visible", 60);
        var hidden = Item(3, -1, "outside clip", 60);
        var tree = new TreeView { Height = 240, VerticalAlignment = VerticalAlignment.Top };
        tree.Items.Add(first); tree.Items.Add(partial); tree.Items.Add(hidden);
        var clip = new Border { Width = 300, Height = 100, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top, Child = tree };
        var overlay = new Border { Background = Avalonia.Media.Brushes.Black, IsVisible = false };
        var content = new Grid(); content.Children.Add(clip); content.Children.Add(overlay);
        var window = new Window { Width = 380, Height = 300, Content = content };
        try
        {
            window.Show(); Layout(window);
            var inspection = StructureNativeDiagnostic.InspectHeader(window, tree, partial);
            Assert.NotNull(inspection?["headerRect"]);
            var rectangle = inspection!["headerRect"]!;
            Assert.InRange(rectangle["height"]!.GetValue<int>(), 1, 59);
            var point = inspection["headerPoint"];
            Assert.NotNull(point);
            Assert.Equal(2, point["nodeId"]!.GetValue<long>());
            Assert.True(point["headerMatches"]!.GetValue<bool>());
            Assert.True(point["inHeader"]!.GetValue<bool>());
            var client = ClientPoint(window, point);
            var clipOrigin = clip.TranslatePoint(default, window);
            Assert.NotNull(clipOrigin);
            Assert.True(new Rect(clipOrigin.Value, clip.Bounds.Size).Contains(client));
            var actual = StructureNativeDiagnostic.ActualSource(tree, window.InputHitTest(client));
            Assert.Equal(point["nodeId"]!.GetValue<long>(), actual["nodeId"]!.GetValue<long>());
            Assert.Null(StructureNativeDiagnostic.InspectHeader(window, tree, hidden));

            overlay.IsVisible = true; Layout(window);
            var covered = StructureNativeDiagnostic.InspectHeader(window, tree, partial);
            Assert.NotNull(covered?["headerRect"]);
            Assert.Null(covered!["headerPoint"]);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void An_overlay_inside_the_same_item_is_not_a_header_hit_or_a_header_input()
    {
        var item = Item(20, -1, "real header", 60);
        var overlay = new Border { Background = Avalonia.Media.Brushes.Black, IsVisible = false };
        item.Template = new FuncControlTemplate<TreeViewItem>((owner, _) =>
        {
            var panel = new Grid();
            panel.Children.Add(new ContentPresenter { Content = owner.Header });
            panel.Children.Add(overlay);
            return panel;
        });
        var tree = new TreeView(); tree.Items.Add(item);
        var window = new Window { Width = 360, Height = 200, Content = tree };
        try
        {
            window.Show(); Layout(window);
            var original = StructureNativeDiagnostic.InspectHeader(window, tree, item);
            Assert.NotNull(original?["headerPoint"]);
            var point = ClientPoint(window, original!["headerPoint"]!);
            Assert.True(original["headerPoint"]!["inHeader"]!.GetValue<bool>());

            overlay.IsVisible = true; Layout(window);
            Assert.Same(overlay, window.InputHitTest(point));
            var covered = StructureNativeDiagnostic.InspectHeader(window, tree, item);
            Assert.NotNull(covered?["headerRect"]);
            Assert.Null(covered!["headerPoint"]);
            JsonObject? observed = null;
            window.AddHandler(InputElement.PointerPressedEvent, (_, e) => observed = StructureNativeDiagnostic.ActualSource(tree, e.Source),
                RoutingStrategies.Tunnel, handledEventsToo: true);
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Assert.NotNull(observed);
            Assert.True(observed["inTree"]!.GetValue<bool>());
            Assert.Equal(20, observed["nodeId"]!.GetValue<long>());
            Assert.False(observed["inHeader"]!.GetValue<bool>());
        }
        finally { window.Close(); }
    }

    private static TreeViewItem Item(long id, long parent, string text, double height) => new()
    {
        Tag = new StructureNode(id, parent, StructureKind.JsonNumber, id, id + 1, -1, -1, id, id + 1, 0, 0, id, 1),
        Header = new Border { Height = height, Background = Avalonia.Media.Brushes.Transparent,
            Child = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } },
    };

    private static Point ClientPoint(Window window, JsonNode point) => window.PointToClient(
        new PixelPoint(point["x"]!.GetValue<int>(), point["y"]!.GetValue<int>()));

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    }
}
