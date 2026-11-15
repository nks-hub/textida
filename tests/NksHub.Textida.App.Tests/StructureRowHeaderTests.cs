using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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
        var tree = new TreeView(); tree.Items.Add(item);
        var window = new Window { Width = 1000, Height = 400, Content = tree, RequestedThemeVariant = ThemeVariant.Light };
        window.Resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary { ["RowForeground"] = pink };
        window.Resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary { ["RowForeground"] = blue };
        try
        {
            window.Show();
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
            Assert.Equal("Example value", AutomationProperties.GetName(item));
            var source = StructureNativeDiagnostic.ActualSource(tree, actual);
            Assert.True(source["inHeader"]!.GetValue<bool>());
            Assert.Equal(7, source["nodeId"]!.GetValue<long>());

            tree.Items.Clear();
            _ = Frame(window, null);
            header.SetText("reused", "new value");
            tree.Items.Add(item);
            var reused = Frame(window, header);
            Assert.True(reused.Count(green) > 5);
            Assert.NotNull(StructureNativeDiagnostic.InspectHeader(window, tree, item)?["headerPoint"]);
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
