using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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
using NksHub.Textida.Core;
using NksHub.Textida.Core.Data.Structure;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class StructureRowHeaderTests
{
    [AvaloniaFact]
    public void Repeated_measure_and_caret_changes_reuse_heading_and_value_layouts()
    {
        bool tracing = TypingTrace.Enabled;
        var ink = Color.Parse("#125DA7");
        var header = new StructureRowHeader();
        header.SetText(new string('\u2588', 6) + " heading", new string('\u2588', 3) + " preview");
        var item = new TreeViewItem { Header = header };
        TextElement.SetForeground(item, new SolidColorBrush(ink));
        var tree = new TreeView(); tree.Items.Add(item);
        var window = new Window { Width = 900, Height = 300, Content = tree, FlowDirection = FlowDirection.LeftToRight };
        try
        {
            TypingTrace.Enabled = false;
            window.Show();
            var before = Frame(window, header);
            Assert.True(before.Count(ink) > 20);
            Size desired = header.DesiredSize;
            Rect bounds = new(header.TranslatePoint(default, window)!.Value, header.Bounds.Size);
            TypingTrace.Reset(); TypingTrace.Enabled = true;
            for (int i = 0; i < 5; i++)
            {
                header.InvalidateMeasure();
                var measured = Frame(window, header);
                Assert.Equal(before.Width, measured.Width); Assert.Equal(before.Height, measured.Height);
                Assert.Equal(before.Pixels, measured.Pixels);
                Assert.Equal(desired, header.DesiredSize);
                Assert.Equal(bounds, new Rect(header.TranslatePoint(default, window)!.Value, header.Bounds.Size));
            }
            Assert.Equal(0, Layouts());

            TypingTrace.Reset();
            header.SetCaret(true);
            var marked = Frame(window, header);
            Assert.Equal(1, Layouts());
            Assert.Equal(before.Width, marked.Width); Assert.Equal(before.Height, marked.Height);
            Assert.Equal(desired, header.DesiredSize);
            Assert.False(before.Pixels.AsSpan().SequenceEqual(marked.Pixels));
            int marker = (int)Math.Ceiling(14 * window.RenderScaling);
            for (int y = 0; y < before.Height; y++)
            {
                int at = (y * before.Width + marker) * 4, count = (before.Width - marker) * 4;
                Assert.True(before.Pixels.AsSpan(at, count).SequenceEqual(marked.Pixels.AsSpan(at, count)));
            }

            TypingTrace.Reset();
            header.SetText("replacement heading", "new preview");
            var changed = Frame(window, header);
            Assert.InRange(Layouts(), 2, int.MaxValue);
            Assert.True(changed.Count(ink) > 20);
            Assert.False(marked.Pixels.AsSpan().SequenceEqual(changed.Pixels));
        }
        finally { window.Close(); TypingTrace.Enabled = tracing; TypingTrace.Reset(); }

    }

    [AvaloniaFact]
    public void Fitting_start_and_left_ltr_text_share_one_layout_per_line_and_keep_fallbacks()
    {
        bool tracing = TypingTrace.Enabled;
        string title = new string('\u2588', 6) + " heading", preview = new string('\u2588', 3) + " value";
        var ink = Color.Parse("#125DA7");
        var header = new StructureRowHeader(); header.SetText(title, preview);
        var item = new TreeViewItem { Header = header };
        TextElement.SetForeground(item, new SolidColorBrush(ink));
        var tree = new TreeView(); tree.Items.Add(item);
        var window = new Window { Width = 900, Height = 300, Content = tree, FlowDirection = FlowDirection.LeftToRight };
        try
        {
            TypingTrace.Enabled = false;
            window.Show(); _ = Frame(window, null);
            var reference = Capture(TextAlignment.DetectFromContent);
            Assert.Equal(4, reference.Layouts);
            var start = Capture(null);
            Assert.Equal(TextAlignment.Start, TextBlock.GetTextAlignment(header));
            EqualFrames(reference, start, 2);
            header.InvalidateMeasure();
            EqualFrames(start, Capture(null), 0);
            var left = Capture(TextAlignment.Left);
            EqualFrames(reference, left, 2);
            header.InvalidateMeasure();
            EqualFrames(left, Capture(TextAlignment.Left), 0);

            foreach (var (direction, expected, actual, heading, maxLines) in new[]
            {
                (FlowDirection.LeftToRight, TextAlignment.End, TextAlignment.Right, title, 0),
                (FlowDirection.RightToLeft, TextAlignment.End, TextAlignment.Left, title, 0),
                (FlowDirection.LeftToRight, TextAlignment.DetectFromContent, TextAlignment.Left, title + "\nsecond line", 0),
                (FlowDirection.LeftToRight, TextAlignment.DetectFromContent, TextAlignment.Left, title + "\nsecond line", 1),
                (FlowDirection.LeftToRight, TextAlignment.DetectFromContent, TextAlignment.Left, title + new string('\u2588', 100), 0),
            })
            {
                window.FlowDirection = direction;
                header.SetText(heading, preview);
                header.SetValue(TextBlock.MaxLinesProperty, maxLines);
                var fallback = Capture(expected);
                EqualFrames(fallback, Capture(actual), fallback.Layouts);
            }
            header.SetText(title, preview); header.SetValue(TextBlock.MaxLinesProperty, 0);
            var centered = Capture(TextAlignment.Center);
            Assert.Equal(4, centered.Layouts);
            header.InvalidateMeasure();
            EqualFrames(centered, Capture(TextAlignment.Center), 0);

            (HeaderFrame Image, Size Desired, Rect Bounds, int Layouts) Capture(TextAlignment? alignment)
            {
                if (alignment is { } actual) header.SetValue(TextBlock.TextAlignmentProperty, actual);
                else header.ClearValue(TextBlock.TextAlignmentProperty);
                TypingTrace.Reset(); TypingTrace.Enabled = true;
                var image = Frame(window, header);
                Assert.True(image.Count(ink) > 20);
                return (image, header.DesiredSize, new Rect(header.TranslatePoint(default, window)!.Value, header.Bounds.Size), Layouts());
            }

            static void EqualFrames((HeaderFrame Image, Size Desired, Rect Bounds, int Layouts) expected,
                (HeaderFrame Image, Size Desired, Rect Bounds, int Layouts) actual, int layouts)
            {
                Assert.Equal(expected.Image.Width, actual.Image.Width); Assert.Equal(expected.Image.Height, actual.Image.Height);
                Assert.Equal(expected.Image.Pixels, actual.Image.Pixels);
                Assert.Equal(expected.Desired, actual.Desired); Assert.Equal(expected.Bounds, actual.Bounds);
                Assert.Equal(layouts, actual.Layouts);
            }
        }
        finally { window.Close(); TypingTrace.Enabled = tracing; TypingTrace.Reset(); }
    }

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

    private static int Layouts()
    {
        var count = Regex.Match(TypingTrace.Summary(), @"(?m)^structure\.header\.layout\s+n=\s*(\d+)");
        return count.Success ? int.Parse(count.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
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
