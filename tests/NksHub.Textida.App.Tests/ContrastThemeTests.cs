using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>P9.1.1 and PR.4.3: the colours of the system are used where the system asks for high contrast, and then every text reads on the colour it is drawn on.</summary>
[Collection("LocState")]
public class ContrastThemeTests : IDisposable
{
    private static readonly ContrastColors YellowOnBlack = new(Color.Parse("#000000"), Color.Parse("#FFFF00"), Color.Parse("#00FF00"), Color.Parse("#0000FF"), Color.Parse("#FFFFFF"), Color.Parse("#00FFFF"));

    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public ContrastThemeTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
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

    public static IEnumerable<object[]> Themes() =>
        [[nameof(ContrastColors.Black)], [nameof(ContrastColors.White)], ["YellowOnBlack"]];

    private static ContrastColors Colors(string name) => name switch
    {
        nameof(ContrastColors.Black) => ContrastColors.Black,
        nameof(ContrastColors.White) => ContrastColors.White,
        _ => YellowOnBlack,
    };

    private Color Brush(string key)
    {
        Assert.True(_app.TryFindResource(key, _app.ActualThemeVariant, out var value), key);
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    [AvaloniaTheory, MemberData(nameof(Themes))]
    public void Every_text_of_the_shell_reads_on_the_fill_it_is_drawn_on(string theme)
    {
        var palette = Colors(theme).Brushes;
        double Ratio(string text, string fill) => ContrastColors.Ratio(palette[text], palette[fill]);
        string[] fills = ["Bg0", "Bg1", "BgEditor", "ChromeBg", "TabRowBg", "TabBg", "InfoBg", "WarnBg", "BgHover", "TabHoverBg", "CurrentLine", "Highlight", "BracketMatch", "OccurrenceBg", "SelectionBg", "DiffAdded", "DiffRemoved"];
        foreach (var text in new[] { "Fg", "EditorFg", "FgMuted", "LineNumbers" })
            foreach (var fill in fills)
                Assert.True(Ratio(text, fill) >= 4.5, $"{theme}: {text} on {fill} is {Ratio(text, fill):0.0} : 1");
        foreach (var text in new[] { "Accent", "Danger", "Good" })
            foreach (var fill in new[] { "Bg0", "Bg1", "BgEditor", "InfoBg", "WarnBg" })
                Assert.True(Ratio(text, fill) >= 4.5, $"{theme}: {text} on {fill} is {Ratio(text, fill):0.0} : 1");
        Assert.True(Ratio("SelectedFg", "AccentSoft") >= 4.5, $"{theme}: a chosen row");
        Assert.True(Ratio("AccentFg", "Accent") >= 4.5, $"{theme}: text on the accent");
        // lines and marks that carry meaning need 3 : 1 against what is behind them
        Assert.True(Ratio("Border", "Bg0") >= 3 && Ratio("Accent", "Bg0") >= 3, $"{theme}: borders and the focus ring");
        Assert.True(ContrastColors.Ratio(palette["BgHover"], palette["Bg0"]) > 1.2, $"{theme}: the hover can be told from the window");
    }

    [AvaloniaTheory, MemberData(nameof(Themes))]
    public void The_changed_bytes_and_the_marks_of_the_hex_grid_read_in_every_high_contrast_theme(string theme)
    {
        var palette = Colors(theme).Brushes;
        double Ratio(string text, string fill) => ContrastColors.Ratio(palette[text], palette[fill]);
        // the text of a byte reads on the tint of a changed byte, a selection and the row of the caret
        foreach (var tint in new[] { Editing.HexView.ChangedTintKey(true), "SelectionBg", "CurrentLine" })
            Assert.True(Ratio("Fg", tint) >= 4.5, $"{theme}: bytes on {tint} are {Ratio("Fg", tint):0.0} : 1");
        // the underline of a change, a difference and a bookmark bar are marks that carry meaning, so they need 3 : 1 against the window
        foreach (var mark in new[] { "Accent", "Danger", "Fg" })
            Assert.True(Ratio(mark, "BgEditor") >= 3, $"{theme}: {mark} on the window is {Ratio(mark, "BgEditor"):0.0} : 1");
        // the tint of the normal themes is the selection colour of the system here, and the text would not read on it (not so in every theme)
        if (theme != "YellowOnBlack") Assert.True(Ratio("Fg", Editing.HexView.ChangedTintKey(false)) < 4.5, $"{theme}: the accent tint reads, so the highlight need not replace it");
    }

    [AvaloniaFact]
    public void The_two_themes_of_windows_are_what_the_system_sets()
    {
        Assert.True(ContrastColors.Black.IsDark);
        Assert.False(ContrastColors.White.IsDark);
        Assert.True(ContrastColors.Ratio(ContrastColors.Black.WindowText, ContrastColors.Black.Window) >= 21 - 0.01);
        Assert.True(ContrastColors.Ratio(ContrastColors.White.WindowText, ContrastColors.White.Window) >= 21 - 0.01);
    }

    [AvaloniaTheory, MemberData(nameof(Themes))]
    public void The_colours_of_the_system_go_over_the_shell_and_taking_them_off_restores_it(string theme)
    {
        var colors = Colors(theme);
        _app.RequestedThemeVariant = ThemeVariant.Light;
        var before = new[] { "Bg0", "Fg", "Accent", "AccentSoft", "SelectedFg", "SelectionBg" }.ToDictionary(k => k, Brush);
        var fluent = _app.Styles.OfType<FluentTheme>().First();
        var accents = new[] { ThemeVariant.Light, ThemeVariant.Dark }.ToDictionary(v => v, v => fluent.Palettes[v].Accent);

        _app.Contrast.Apply(colors);
        Dispatcher.UIThread.RunJobs();
        Assert.True(_app.Contrast.IsOn);
        Assert.Equal(colors.IsDark ? ThemeVariant.Dark : ThemeVariant.Light, _app.RequestedThemeVariant);
        foreach (var (key, color) in colors.Brushes) Assert.Equal(color, Brush(key));
        Assert.Equal(colors.WindowText, Brush("Fg"));
        Assert.Equal(colors.Window, Brush("Bg0"));
        Assert.Equal(colors.HotLight, Brush("Accent"));

        var palette = fluent.Palettes[colors.IsDark ? ThemeVariant.Dark : ThemeVariant.Light];
        Assert.Equal(colors.HotLight, palette.Accent);
        Assert.Equal(colors.Window, palette.RegionColor);

        _app.Contrast.Restore();
        _app.UpdateContrast();
        Dispatcher.UIThread.RunJobs();
        Assert.False(_app.Contrast.IsOn);
        _app.RequestedThemeVariant = ThemeVariant.Light;
        foreach (var (key, color) in before) Assert.Equal(color, Brush(key));
        foreach (var (variant, accent) in accents) Assert.Equal(accent, fluent.Palettes[variant].Accent);
    }

    [AvaloniaFact]
    public void When_the_system_asks_for_high_contrast_its_colours_are_used_and_when_it_stops_the_theme_of_the_settings_comes_back()
    {
        _app.ForceContrast = true;
        _app.UpdateContrast();
        Assert.True(_app.Contrast.IsOn);
        var system = ContrastColors.FromSystem();
        if (OperatingSystem.IsWindows())
        {
            Assert.NotNull(system);
            Assert.Equal(system, _app.Contrast.Colors);
            Assert.Equal(system.WindowText, Brush("Fg"));
        }
        else
        {
            Assert.Null(system); // no list of colours to read: the black or white pair of the system theme stands in
            Assert.Contains(_app.Contrast.Colors, new[] { ContrastColors.Black, ContrastColors.White });
        }

        _app.SetTheme("light");
        Assert.Equal(_app.Contrast.Colors!.IsDark ? ThemeVariant.Dark : ThemeVariant.Light, _app.RequestedThemeVariant); // the settings do not take the high contrast away

        _app.ForceContrast = false;
        _app.UpdateContrast();
        Assert.False(_app.Contrast.IsOn);
        Assert.Equal(ThemeVariant.Light, _app.RequestedThemeVariant);
        _app.SetTheme("system");
    }

    [AvaloniaFact]
    public void The_focus_ring_takes_the_hot_light_colour_of_the_system()
    {
        _app.Contrast.Apply(ContrastColors.Black);
        var button = new Button { Content = "focus" };
        var window = new Window { Content = button, Width = 200, Height = 100 };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(); // the adorner layer is there once the window has been through its first jobs
        Assert.True(button.Focus(NavigationMethod.Tab));
        Dispatcher.UIThread.RunJobs();
        var ring = Assert.Single(AdornerLayer.GetAdornerLayer(button)!.Children.OfType<Border>());
        Assert.Equal(ContrastColors.Black.HotLight, Assert.IsAssignableFrom<ISolidColorBrush>(ring.BorderBrush).Color);
        window.Close();
    }
}
