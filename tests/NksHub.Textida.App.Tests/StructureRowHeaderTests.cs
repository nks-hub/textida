using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Shell;
using NksHub.Textida.Core.Data.Structure;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class StructureRowHeaderTests
{
    [AvaloniaFact]
    public void Header_redraws_text_marker_font_and_theme_and_keeps_its_actual_tree_hit()
    {
        var pink = new SolidColorBrush(Color.Parse("#D412A7"));
        var blue = new SolidColorBrush(Color.Parse("#1279D4"));
        var green = Color.Parse("#187B39");
        string title = new string('\u2588', 8) + " title", preview = new string('\u2588', 4) + " value";
        var header = new StructureRowHeader();
        header.SetText(title, preview);
        var item = new TreeViewItem
        {
            Tag = new StructureNode(7, -1, StructureKind.JsonNumber, 0, 1, -1, -1, 0, 1, 0, 0, 7, 1),
            Header = header,
        };
        AutomationProperties.SetName(item, "Example value");
        TextElement.SetFontSize(item, 18);
        item.Bind(TextElement.ForegroundProperty, new DynamicResourceExtension("RowForeground"));
        var parent = new TreeViewItem { Header = "Container", IsExpanded = true };
        parent.Items.Add(item);
        var tree = new TreeView(); tree.Items.Add(parent);
        var window = new Window { Width = 1000, Height = 400, Content = tree, RequestedThemeVariant = ThemeVariant.Light };
        window.Resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary { ["RowForeground"] = pink };
        window.Resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary { ["RowForeground"] = blue };
        try
        {
            window.Show();
            var stockBody = Frame(window, item);
            Point stockOrigin = header.TranslatePoint(default, window)!.Value;
            var leafTheme = Assert.IsType<ControlTheme>(StructureRowThemes.TryCreateLeaf(tree));
            Assert.NotNull(leafTheme.BasedOn);
            item.Theme = leafTheme;
            var leafBody = Frame(window, item);
            Assert.Equal(stockBody.Width, leafBody.Width); Assert.Equal(stockBody.Height, leafBody.Height);
            Assert.Equal(stockBody.Pixels, leafBody.Pixels);
            Assert.Equal(stockOrigin, header.TranslatePoint(default, window)!.Value);
            Assert.DoesNotContain(item.GetVisualDescendants(), visual => visual is ToggleButton or ItemsPresenter);
            Assert.DoesNotContain(item.GetVisualDescendants(), visual => visual is Control { Name: "PART_ExpandCollapseChevronContainer" });
            Rect? bringTarget = null;
            tree.AddHandler(Control.RequestBringIntoViewEvent, (_, e) =>
            {
                if (ReferenceEquals(e.TargetObject, item)) bringTarget = e.TargetRect;
            }, RoutingStrategies.Bubble, handledEventsToo: true);
            foreach (var direction in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
            {
                CompareRow(direction, selected: false, enabled: true, padding: default, narrow: false);
                CompareRow(direction, selected: true, enabled: true, padding: new(3, 2, 7, 5), narrow: false);
                CompareRow(direction, selected: true, enabled: false, padding: new(3, 2, 7, 5), narrow: false);
                CompareRow(direction, selected: false, enabled: true, padding: new(3, 2, 7, 5), narrow: true);
            }
            item.Theme = leafTheme; item.IsEnabled = true; item.Padding = default;
            tree.SelectedItem = null; window.FocusManager?.Focus(null);
            window.Width = 1000; window.FlowDirection = FlowDirection.LeftToRight;
            header.SetText(title, preview);
            _ = Frame(window, null);
            var first = Frame(window, header);
            Assert.True(first.Count(pink.Color) > 20);
            Assert.Empty(header.GetVisualDescendants().OfType<Control>());
            double twoLines = header.DesiredSize.Height;
            header.SetCaret(true);
            var marked = Frame(window, header);
            Assert.Equal(twoLines, header.DesiredSize.Height);
            Assert.True(marked.Count(pink.Color, 0, 14) > first.Count(pink.Color, 0, 14) + 5);

            header.SetText(new string('\u2588', 100), "replacement");
            var replaced = Frame(window, header);
            Assert.InRange(header.DesiredSize.Width, 14, 714);
            Assert.False(marked.Pixels.AsSpan().SequenceEqual(replaced.Pixels));
            header.SetText(title, "");
            _ = Frame(window, header);
            Assert.True(header.DesiredSize.Height < twoLines);
            header.SetText(title, preview); header.SetCaret(false);
            var restored = Frame(window, header);
            Assert.Equal(first.Pixels, restored.Pixels);

            TextElement.SetFontSize(item, 24);
            TextElement.SetFontFamily(item, new FontFamily("monospace"));
            var larger = Frame(window, header);
            Assert.True(header.DesiredSize.Height > twoLines);
            Assert.True(larger.Count(pink.Color) > 20);
            window.RequestedThemeVariant = ThemeVariant.Dark;
            var dark = Frame(window, header);
            Assert.Equal(0, dark.Count(pink.Color));
            Assert.True(dark.Count(blue.Color) > 20);
            blue.Color = green;
            var changedBrush = Frame(window, header);
            Assert.Equal(0, changedBrush.Count(Color.Parse("#1279D4")));
            Assert.True(changedBrush.Count(green) > 20);

            window.FlowDirection = FlowDirection.RightToLeft;
            header.SetCaret(true);
            var rightToLeft = Frame(window, header);
            Assert.True(rightToLeft.Count(green, rightToLeft.Width - 14, rightToLeft.Width) > 5);
            var inspected = StructureNativeDiagnostic.InspectHeader(window, tree, item);
            Assert.NotNull(inspected?["headerPoint"]);
            var point = inspected!["headerPoint"]!;
            var client = window.PointToClient(new PixelPoint(point["x"]!.GetValue<int>(), point["y"]!.GetValue<int>()));
            Assert.Same(header, window.InputHitTest(client));
            object? actual = null;
            window.AddHandler(InputElement.PointerPressedEvent, (_, e) => actual = e.Source,
                RoutingStrategies.Tunnel, handledEventsToo: true);
            window.MouseDown(client, MouseButton.Left); window.MouseUp(client, MouseButton.Left);
            Assert.Same(header, actual);
            Assert.True(item.IsSelected);
            Assert.True(item.IsKeyboardFocusWithin);
            Assert.Equal("Example value", AutomationProperties.GetName(item));
            var source = StructureNativeDiagnostic.ActualSource(tree, actual);
            Assert.True(source["inHeader"]!.GetValue<bool>());
            Assert.Equal(7, source["nodeId"]!.GetValue<long>());
            Point body = item.TranslatePoint(new Point(item.Bounds.Width - 2, item.Bounds.Height / 2), window)!.Value;
            var bodyHit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(body));
            Assert.Contains(item, bodyHit.GetSelfAndVisualAncestors());
            item.IsSelected = false;
            window.MouseDown(body, MouseButton.Left); window.MouseUp(body, MouseButton.Left);
            Assert.True(item.IsSelected); Assert.True(item.IsKeyboardFocusWithin);

            tree.Items.Clear();
            _ = Frame(window, null);
            header.SetText("reused", "new value");
            tree.Items.Add(parent);
            var reused = Frame(window, header);
            Assert.True(reused.Count(green) > 5);
            Assert.NotNull(StructureNativeDiagnostic.InspectHeader(window, tree, item)?["headerPoint"]);

            void CompareRow(FlowDirection direction, bool selected, bool enabled, Thickness padding, bool narrow)
            {
                var stock = CaptureRow(null);
                var leaf = CaptureRow(leafTheme);
                Assert.Equal(stock.Image.Width, leaf.Image.Width); Assert.Equal(stock.Image.Height, leaf.Image.Height);
                Assert.Equal(stock.Image.Pixels, leaf.Image.Pixels);
                Assert.Equal(stock.HeaderBounds, leaf.HeaderBounds);
                Assert.Equal(stock.BringTarget, leaf.BringTarget); Assert.Equal(stock.Offset, leaf.Offset);

                (HeaderFrame Image, Rect HeaderBounds, Rect BringTarget, Vector Offset) CaptureRow(ControlTheme? theme)
                {
                    item.IsEnabled = true; item.Theme = theme; item.Padding = padding;
                    tree.SelectedItem = null; window.FocusManager?.Focus(null);
                    window.Width = narrow ? 240 : 1000; window.FlowDirection = direction;
                    header.SetText(narrow ? new string('\u2588', 100) : title, narrow ? "" : preview);
                    window.MouseMove(new Point(5, window.Height - 5));
                    _ = Frame(window, null);
                    var scroller = Assert.Single(tree.GetVisualDescendants().OfType<ScrollViewer>());
                    scroller.Offset = default;
                    _ = Frame(window, null);
                    if (selected)
                    {
                        var hit = StructureNativeDiagnostic.InspectHeader(window, tree, item)?["headerPoint"];
                        Assert.NotNull(hit);
                        var at = window.PointToClient(new PixelPoint(hit!["x"]!.GetValue<int>(), hit["y"]!.GetValue<int>()));
                        Assert.Same(header, window.InputHitTest(at));
                        window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left);
                        Assert.True(item.IsSelected); Assert.True(item.IsKeyboardFocusWithin);
                        window.FocusManager?.Focus(null);
                        Assert.True(item.Focus(NavigationMethod.Directional));
                        Assert.True(item.IsKeyboardFocusWithin);
                    }
                    item.IsEnabled = enabled;
                    bringTarget = null; item.BringIntoView();
                    var image = Frame(window, window);
                    if (!enabled) Assert.False(item.IsKeyboardFocusWithin);
                    Assert.NotNull(bringTarget);
                    return (image, new Rect(header.TranslatePoint(default, window)!.Value, header.Bounds.Size), bringTarget.Value, scroller.Offset);
                }
            }
        }
        finally { window.Close(); }
    }

    private sealed record HeaderFrame(int Width, int Height, byte[] Pixels)
    {
        public int Count(Color color, int from = 0, int? to = null)
        {
            int count = 0;
            for (int y = 0; y < Height; y++)
                for (int x = Math.Max(0, from); x < Math.Min(Width, to ?? Width); x++)
                {
                    int at = (y * Width + x) * 4;
                    if (Pixels[at] == color.R && Pixels[at + 1] == color.G && Pixels[at + 2] == color.B) count++;
                }
            return count;
        }
    }

    private static HeaderFrame Frame(Window window, Control? header)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        using var frame = Assert.IsType<WriteableBitmap>(window.CaptureRenderedFrame());
        if (header is null) return new(0, 0, []);
        using var buffer = frame.Lock();
        Point origin = header.TranslatePoint(default, window)!.Value;
        double scale = frame.PixelSize.Width / frame.Size.Width;
        int x = (int)(origin.X * scale), y = (int)(origin.Y * scale);
        int width = Math.Min((int)Math.Ceiling(header.Bounds.Width * scale), buffer.Size.Width - x);
        int height = Math.Min((int)Math.Ceiling(header.Bounds.Height * scale), buffer.Size.Height - y);
        Assert.True(x >= 0 && y >= 0 && width > 0 && height > 0);
        var pixels = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
            Marshal.Copy(buffer.Address + (y + row) * buffer.RowBytes + x * 4, pixels, row * width * 4, width * 4);
        if (frame.Format == Avalonia.Platform.PixelFormat.Bgra8888)
            for (int at = 0; at < pixels.Length; at += 4) (pixels[at], pixels[at + 2]) = (pixels[at + 2], pixels[at]);
        return new(width, height, pixels);
    }
}
