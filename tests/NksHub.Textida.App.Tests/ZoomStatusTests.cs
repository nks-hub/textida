using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>Text zoom (P3.6.1): Ctrl+0 and the Zoom submenu go back to the original size, and the status bar shows the zoom in percent and offers levels.</summary>
[Collection("LocState")]
public class ZoomStatusTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public ZoomStatusTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null, look: TestApp.WindowsLook);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private double FontSize => _app.Workbench.Settings.GetNumber("editor.fontSize");

    [AvaloniaFact]
    public void Ctrl_wheel_zooms_the_status_bar_shows_the_percent_and_Ctrl_0_goes_back_to_100()
    {
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = "text\n";
        Assert.Equal(14, FontSize);
        Assert.True(_w.ZoomItem.IsVisible);
        Assert.Equal("100 %", _w.ZoomItem.Content);
        Assert.Equal("Přiblížení textu 100 %", AutomationProperties.GetName(_w.ZoomItem));

        var inside = tab.Editor.TranslatePoint(new Point(100, 50), _w)!.Value;
        _w.MouseWheel(inside, new Vector(0, 1), TestApp.Mod);
        _w.MouseWheel(inside, new Vector(0, 1), TestApp.Mod);
        Assert.Equal(16, FontSize);
        Assert.Equal("114 %", _w.ZoomItem.Content); // 16 of 14
        Assert.Contains("114 %", _w.StatusRightText);

        _w.KeyPress(Key.D0, TestApp.Mod, PhysicalKey.Digit0, "0");
        Assert.Equal(14, FontSize);
        Assert.Equal("100 %", _w.ZoomItem.Content);
        Assert.Equal(14, tab.Editor.FontSize);
    }

    [AvaloniaFact]
    public void The_zoom_submenu_holds_reset_zoom()
    {
        var view = _w.MenuModel.Single(g => g.Label == "Zobrazit");
        var zoom = Assert.Single(view.Items.OfType<MenuGroup>(), g => g.Label == "Lupa");
        var reset = Assert.Single(zoom.Items.OfType<MenuCommand>(), c => c.CommandId == "nks.view.zoomReset");
        Assert.Equal(new KeyGesture(Key.D0, Shortcuts.Primary), reset.Gesture);

        _w.NewDocument();
        _w.Zoom(3);
        reset.Run();
        Assert.Equal(14, FontSize);
    }

    [AvaloniaFact]
    public void A_click_on_the_percent_offers_levels_and_reset()
    {
        _w.NewDocument();
        _w.Zoom(1);
        var flyout = _w.ShowZoomMenu();
        var items = flyout.Items.OfType<MenuItem>().ToList();
        Assert.Equal("Původní velikost (100 %)", items[0].Header);
        // the levels read as the zoom the whole font size gives: 75 % of 14 is 10.5, so 11, which is 79 %
        Assert.Equal(["50 %", "79 %", "93 %", "100 %", "107 %", "129 %", "150 %", "200 %"], items.Skip(1).Select(i => (string)i.Header!));
        Assert.True(items.Single(i => (string)i.Header! == "107 %").IsChecked); // 15 of 14 is the current zoom

        items.Single(i => (string)i.Header! == "150 %").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(21, FontSize);
        Assert.Equal("150 %", _w.ZoomItem.Content);

        _w.ShowZoomMenu().Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(14, FontSize);
        Assert.Equal(SettingSourceOf("editor.fontSize"), NksHub.Textida.Workbench.Settings.SettingSource.Default); // back to the default, not a copy of it
    }

    [AvaloniaFact]
    public void The_percent_is_translated()
    {
        _w.NewDocument();
        _app.SetLanguage("en");
        Assert.Equal("100%", _w.ZoomItem.Content);
        Assert.Equal("Original Size (100%)", _w.ShowZoomMenu().Items.OfType<MenuItem>().First().Header);
    }

    private NksHub.Textida.Workbench.Settings.SettingSource SettingSourceOf(string key) => _app.Workbench.Settings.Resolve(key)!.Source;
}
