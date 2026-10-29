using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Icons;

namespace NksHub.Textida.App.Tests;

/// <summary>P3.6.14: the icons of the commands in menus and in the palette, drawn in the colours of the theme.</summary>
[Collection("LocState")]
public class CommandIconTests : IDisposable
{
    private static readonly ContrastColors YellowOnBlack = new(Color.Parse("#000000"), Color.Parse("#FFFF00"), Color.Parse("#00FF00"), Color.Parse("#0000FF"), Color.Parse("#FFFFFF"), Color.Parse("#00FFFF"));

    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public CommandIconTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null, look: TestApp.WindowsLook);
    }

    public void Dispose()
    {
        _app.ForceContrast = null;
        _app.Contrast.Restore();
        _app.RequestedThemeVariant = ThemeVariant.Default;
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static IEnumerable<MenuCommand> Commands(IEnumerable<MenuNode> nodes) => nodes.SelectMany(n => n switch
    {
        MenuCommand c => [c],
        MenuGroup g => Commands(g.Items),
        _ => [],
    });

    private static Color Of(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static Window Show(Control content, IBrush? foreground = null)
    {
        var window = new Window { Content = content, Width = 64, Height = 64, Background = Brushes.White, Foreground = foreground ?? Brushes.Black };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static IEnumerable<MenuItem> AllItems(ItemsControl parent) => parent.Items.OfType<MenuItem>().SelectMany(i => new[] { i }.Concat(AllItems(i)));

    private static IconView Icon(string name) => CommandIcons.Create(BuiltinIcons.All[name]);

    /// <summary>The colour of one pixel of the window as it is drawn.</summary>
    private static Color Pixel(Window window, int x, int y)
    {
        window.UpdateLayout();
        // the frame is drawn on the render thread: give it the few milliseconds it needs
        for (int i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame(); // every capture draws what changed since the one before
            Thread.Sleep(5);
        }
        var frame = window.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        var data = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, data, 0, data.Length);
        int at = y * buffer.RowBytes + x * 4;
        // the order of the bytes of a pixel is the one the platform draws in
        return frame.Format == PixelFormat.Bgra8888 ? Color.FromArgb(data[at + 3], data[at + 2], data[at + 1], data[at]) : Color.FromArgb(data[at + 3], data[at], data[at + 1], data[at + 2]);
    }

    private static int Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    [AvaloniaFact]
    public void An_icon_is_a_16_pixel_square_with_a_path_per_shape_that_the_screen_reader_skips()
    {
        var shape = BuiltinIcons.All["git-commit"];
        var icon = CommandIcons.Create(shape);
        Assert.Equal((16, 16), (icon.Width, icon.Height));
        Assert.Equal(shape.Paths.Count, icon.PathCount);
        Assert.Same(shape, icon.Shape);
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(icon));
        Assert.False(icon.Focusable);
        Assert.False(icon.IsHitTestVisible);
        var big = CommandIcons.Create(shape, 24);
        Assert.Equal((24, 24), (big.Width, big.Height));
        icon.Measure(Size.Infinity);
        Assert.Equal(new Size(16, 16), icon.DesiredSize);
    }

    [AvaloniaFact]
    public void Every_built_in_icon_can_be_drawn()
    {
        foreach (var (name, shape) in BuiltinIcons.All)
            Assert.True(shape.Paths.Count == CommandIcons.Create(shape).PathCount, name);
    }

    [AvaloniaFact]
    public void A_path_a_drawing_cannot_parse_is_left_out_and_the_rest_is_drawn()
    {
        var shape = new IconShape(16, [new IconPath("M0 0 L1", IconPaint.Line), new IconPath("M2 2 L8 8", IconPaint.Line)]);
        Assert.Equal(1, CommandIcons.Create(shape).PathCount);
    }

    [AvaloniaFact]
    public void The_lines_follow_the_foreground_of_the_host_and_the_marked_parts_the_accent_of_the_theme()
    {
        var icon = Icon("sort");
        var window = Show(icon, Brushes.Red);
        Assert.Equal(Colors.Red, Of(icon.LineBrush));
        window.Foreground = Brushes.Blue;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Colors.Blue, Of(icon.LineBrush));
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            _app.RequestedThemeVariant = variant;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Of(ThemeColors.Accent), Of(icon.AccentBrush));
        }
        Assert.Equal(Color.Parse("#3DC2AC"), Of(icon.AccentBrush)); // the dark one, set last
        _app.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Color.Parse("#0F7B6C"), Of(icon.AccentBrush));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("Black")]
    [InlineData("White")]
    [InlineData("YellowOnBlack")]
    public void In_high_contrast_the_marked_parts_take_the_colour_of_links_and_the_lines_the_text_of_the_host(string theme)
    {
        var colors = theme switch { "Black" => ContrastColors.Black, "White" => ContrastColors.White, _ => YellowOnBlack };
        var icon = Icon("sort");
        var window = Show(icon);
        _app.Contrast.Apply(colors);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(colors.HotLight, Of(icon.AccentBrush));
        var host = new Border { Child = Icon("sort") };
        var inner = (IconView)host.Child;
        window.Content = host;
        window.Foreground = new SolidColorBrush(colors.WindowText);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(colors.WindowText, Of(inner.LineBrush));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("Black")]
    [InlineData("White")]
    [InlineData("YellowOnBlack")]
    public void An_icon_has_3_to_1_against_the_window_in_high_contrast(string theme)
    {
        // the system promises the text colour and the colour of links on the window colour, and the shell fills with the window colour
        var palette = (theme switch { "Black" => ContrastColors.Black, "White" => ContrastColors.White, _ => YellowOnBlack }).Brushes;
        foreach (var colour in new[] { "Fg", "Accent" })
            foreach (var surface in new[] { "Bg0", "Bg1", "BgEditor", "ChromeBg", "TabRowBg" })
                Assert.True(ContrastColors.Ratio(palette[colour], palette[surface]) >= 3, $"{theme}: {colour} on {surface} is {ContrastColors.Ratio(palette[colour], palette[surface]):0.0} : 1");
    }

    [AvaloniaFact]
    public void An_icon_has_3_to_1_against_every_surface_it_sits_on_in_the_light_and_the_dark_theme()
    {
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Color Brush(string key)
            {
                Assert.True(_app.TryFindResource(key, variant, out var value), key);
                return Of((IBrush)value!);
            }
            foreach (var colour in new[] { "Fg", "Accent" })
                foreach (var surface in new[] { "Bg0", "Bg1", "BgEditor", "BgHover", "ChromeBg", "TabRowBg", "AccentSoft" })
                    Assert.True(ContrastColors.Ratio(Brush(colour), Brush(surface)) >= 3, $"{variant}: {colour} on {surface} is {ContrastColors.Ratio(Brush(colour), Brush(surface)):0.0} : 1");
        }
    }

    [AvaloniaFact]
    public void A_disabled_host_dims_the_marked_parts()
    {
        var on = Icon("sort");
        var off = Icon("sort");
        var window = Show(new StackPanel { Children = { on, new Button { Content = off, IsEnabled = false } } });
        Assert.Equal(1, on.AccentOpacity);
        Assert.Equal(IconView.DisabledAccentOpacity, off.AccentOpacity);
        window.Close();
    }

    [AvaloniaFact]
    public void The_icon_is_really_drawn_lines_in_the_foreground_of_the_host_and_fills_in_the_accent()
    {
        // "stop" is a square of 9 units from 3.5 to 12.5; "record" is a disc of the accent
        var square = new IconView(BuiltinIcons.All["stop"]) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = Show(square, Brushes.Black);
        Assert.True(Distance(Pixel(window, 3, 8), Colors.Black) < 90, "the left edge of the square is drawn in the foreground");
        Assert.True(Distance(Pixel(window, 8, 8), Colors.White) < 10, "the inside of the square is empty");
        Assert.True(Distance(Pixel(window, 20, 8), Colors.White) < 10, "nothing is drawn outside the icon");
        window.Foreground = Brushes.Red;
        Assert.True(Distance(Pixel(window, 3, 8), Colors.Red) < 90, "the line follows the foreground when it changes");
        window.Close();

        var disc = new IconView(BuiltinIcons.All["record"]) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var second = Show(disc);
        Assert.True(Distance(Pixel(second, 8, 8), Of(disc.AccentBrush)) < 10, "the centre of the disc is the accent");
        Assert.True(Distance(Pixel(second, 0, 0), Colors.White) < 10, "the corner is empty");
        second.Close();
    }

    [AvaloniaFact]
    public void A_menu_item_has_the_icon_of_its_command_unless_it_carries_a_check_mark()
    {
        var all = Commands(_w.MenuModel.SelectMany(g => g.Items)).ToList();
        var save = all.Single(c => c.CommandId == "nks.file.save");
        Assert.Same(BuiltinIcons.All["save"], save.Icon);
        var wrap = all.Single(c => c.CommandId == "nks.view.toggleWordWrap");
        Assert.NotNull(wrap.Checked);

        var menu = MenuRenderer.ToMenu(_w.MenuModel);
        var items = AllItems(menu).Where(i => i.Tag is MenuCommand).ToList();
        var saveItem = items.Single(i => ((MenuCommand)i.Tag!).CommandId == "nks.file.save");
        Assert.Same(BuiltinIcons.All["save"], Assert.IsType<IconView>(saveItem.Icon).Shape);
        Assert.Null(items.Single(i => ((MenuCommand)i.Tag!).CommandId == "nks.view.toggleWordWrap").Icon);
        // an icon never hides the name: the name stays the accessible name of the item
        Assert.Equal(save.Label, AutomationProperties.GetName(saveItem));
        Assert.All(items.Where(i => i.Icon is not null), i => Assert.NotNull(((MenuCommand)i.Tag!).Icon));
    }

    [AvaloniaFact]
    public void Most_commands_of_the_menus_have_an_icon_and_only_the_ones_that_say_text_have_none()
    {
        var all = Commands(_w.MenuModel.SelectMany(g => g.Items)).ToList();
        Assert.True(all.Count(c => c.Icon is not null) >= all.Count / 2, $"{all.Count(c => c.Icon is not null)} of {all.Count}");
        foreach (var command in all.Where(c => c.Icon is null))
            Assert.True(_app.Workbench.Commands.TryGet(command.CommandId!, out var descriptor) && descriptor.Icon == BuiltinIcons.TextOnly, command.CommandId);
    }

    [AvaloniaFact]
    public void A_palette_row_of_a_command_shows_its_icon_before_the_title()
    {
        _w.ShowPalette();
        _w.SetPaletteQuery(_app.Workbench.Strings.Get("cmd.file.saveAll"));
        Dispatcher.UIThread.RunJobs();
        var row = _w.QuickResults.First();
        Assert.Same(BuiltinIcons.All["save-all"], row.Icon);

        var list = _w.PaletteHost.GetLogicalDescendants().OfType<ListBox>().Single(l => l.Classes.Contains("paletteList"));
        var grid = Assert.IsType<Grid>(Assert.IsType<ListBoxItem>(list.Items[0]).Content);
        var icon = Assert.Single(grid.Children.OfType<IconView>());
        Assert.Equal(1, Grid.GetColumn(icon));
        Assert.Equal(2, grid.Children.OfType<TextBlock>().Select(Grid.GetColumn).Max());
    }

    [AvaloniaFact]
    public void A_palette_row_of_a_command_that_says_text_has_no_icon_and_keeps_the_column_free()
    {
        var wb = _app.Workbench;
        var command = wb.Commands.Commands.First(c => !c.Hidden && c.Icon == BuiltinIcons.TextOnly && wb.Commands.HasHandler(c.Id) && c.Id == "nks.edit.selectLine");
        string title = wb.Strings.Get(command.TitleKey);
        _w.ShowPalette();
        _w.SetPaletteQuery(title);
        Dispatcher.UIThread.RunJobs();
        var rows = _w.QuickResults.ToList();
        var index = rows.FindIndex(r => r.Title == title);
        Assert.True(index >= 0, title);
        Assert.Null(rows[index].Icon);
        var list = _w.PaletteHost.GetLogicalDescendants().OfType<ListBox>().Single(l => l.Classes.Contains("paletteList"));
        var grid = Assert.IsType<Grid>(Assert.IsType<ListBoxItem>(list.Items[index]).Content);
        Assert.Empty(grid.Children.OfType<IconView>());
    }
}
