using System.Text.Json;
using NksPad.App.Profile;
using NksPad.App.Shell;

namespace NksPad.App.Tests;

public class ProfileLocationTests
{
    [Fact]
    public void Marker_next_to_the_executable_makes_the_profile_portable()
    {
        var dir = ProfileLocation.Resolve(Path.Combine("apps", "textida"), markerExists: true, isMac: false, "home", "appdata");
        Assert.Equal(Path.Combine("apps", "textida", ProfileLocation.PortableFolder), dir);
    }

    [Fact]
    public void Without_marker_the_profile_is_in_the_user_profile()
    {
        Assert.Equal(Path.Combine("appdata", "Textida"), ProfileLocation.Resolve("exe", false, isMac: false, "home", "appdata"));
        Assert.Equal(Path.Combine("home", "Library", "Application Support", "Textida"), ProfileLocation.Resolve("exe", false, isMac: true, "home", "appdata"));
    }

    [Fact]
    public void Portable_wins_on_macOS_too() =>
        Assert.StartsWith("exe", ProfileLocation.Resolve("exe", true, isMac: true, "home", "appdata"));
}

public class ProfileStoreTests
{
    [Fact]
    public void Window_and_settings_round_trip()
    {
        using var temp = new TempDir();
        var store = new ProfileStore(Path.Combine(temp.Path, "nested", "profile"));
        store.SaveWindow(new SavedWindow { X = -1500, Y = 40, Width = 1000.5, Height = 700, Maximized = true });
        store.SaveSettings(new Settings { Language = "en", Theme = "dark" });

        var w = store.LoadWindow();
        Assert.NotNull(w);
        Assert.Equal((-1500, 40, 1000.5, 700, true), (w.X, w.Y, w.Width, w.Height, w.Maximized));
        var s = store.LoadSettings();
        Assert.Equal(("en", "dark"), (s.Language, s.Theme));
    }

    [Fact]
    public void Missing_or_damaged_files_give_defaults()
    {
        using var temp = new TempDir();
        var store = new ProfileStore(temp.Path);
        Assert.Null(store.LoadWindow());
        Assert.Equal("cs", store.LoadSettings().Language);

        temp.File("window.json", "{ not json");
        temp.File("settings.json", "\0\0\0");
        Assert.Null(store.LoadWindow());
        Assert.Equal("system", store.LoadSettings().Theme);
    }

    [Fact]
    public void Saving_leaves_no_temporary_file()
    {
        using var temp = new TempDir();
        new ProfileStore(temp.Path).SaveSettings(new Settings());
        Assert.Equal(["settings.json"], Directory.GetFiles(temp.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void Settings_hold_language_tags_not_translated_text()
    {
        using var temp = new TempDir();
        var store = new ProfileStore(temp.Path);
        store.SaveSettings(new Settings { Language = "cs" });
        var cs = JsonDocument.Parse(File.ReadAllText(Path.Combine(temp.Path, "settings.json"))).RootElement.EnumerateObject().Select(p => p.Name).ToList();
        store.SaveSettings(new Settings { Language = "en" });
        var en = JsonDocument.Parse(File.ReadAllText(Path.Combine(temp.Path, "settings.json"))).RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(cs, en);
        Assert.Equal("en", store.LoadSettings().Language);
    }
}

public class WindowPlacementTests
{
    private static readonly ScreenBox Main = new(new PixelBox(0, 0, 1920, 1040), 1.0, Primary: true);
    private static readonly ScreenBox Right = new(new PixelBox(1920, 0, 2560, 1400), 1.0, Primary: false);
    private static readonly ScreenBox Left = new(new PixelBox(-1280, 100, 1280, 900), 1.0, Primary: false);

    private static SavedWindow Saved(int x, int y, double w = 1000, double h = 700, bool max = false) => new() { X = x, Y = y, Width = w, Height = h, Maximized = max };

    [Fact]
    public void Window_still_on_its_monitor_stays_where_it_was()
    {
        var p = WindowPlacement.Restore(Saved(200, 100), [Main, Right]);
        Assert.Equal(new Placement(200, 100, 1000, 700, false, false), p);
    }

    [Fact]
    public void Window_on_a_second_monitor_is_kept_there()
    {
        var p = WindowPlacement.Restore(Saved(2400, 300), [Main, Right]);
        Assert.Equal((2400, 300, false), (p.X, p.Y, p.Relocated));
    }

    [Fact]
    public void Unplugged_monitor_moves_the_window_to_the_primary_one()
    {
        var p = WindowPlacement.Restore(Saved(2400, 300), [Main]);
        Assert.True(p.Relocated);
        Assert.InRange(p.X, 0, 1920 - 1000);
        Assert.InRange(p.Y, 0, 1040 - 700);
        Assert.Equal((1000.0, 700.0), (p.Width, p.Height));
    }

    [Fact]
    public void Monitor_left_of_primary_with_negative_coordinates_is_handled()
    {
        Assert.False(WindowPlacement.Restore(Saved(-1200, 200), [Main, Left]).Relocated);
        var p = WindowPlacement.Restore(Saved(-1200, 200), [Main]);
        Assert.True(p.Relocated);
        Assert.True(p.X >= 0);
    }

    [Fact]
    public void Window_whose_title_strip_is_off_screen_is_recovered()
    {
        Assert.True(WindowPlacement.Restore(Saved(100, 5000), [Main]).Relocated);
        Assert.True(WindowPlacement.Restore(Saved(1910, 100), [Main]).Relocated); // only 10 px of the strip visible
    }

    [Fact]
    public void Window_hanging_over_the_edge_is_pulled_back_inside()
    {
        var p = WindowPlacement.Restore(Saved(1500, 600), [Main]);
        Assert.Equal((920, 340, true), (p.X, p.Y, p.Relocated));
    }

    [Fact]
    public void Window_larger_than_the_monitor_is_shrunk()
    {
        var p = WindowPlacement.Restore(Saved(0, 0, 3000, 2000), [Main]);
        Assert.Equal((1920.0, 1040.0), (p.Width, p.Height));
    }

    [Fact]
    public void Dpi_scale_converts_size_between_units_and_pixels()
    {
        var hiDpi = new ScreenBox(new PixelBox(0, 0, 3840, 2120), 2.0, true);
        var p = WindowPlacement.Restore(Saved(100, 100, 1000, 700), [hiDpi]);
        Assert.Equal((100, 100, 1000.0, 700.0, false), (p.X, p.Y, p.Width, p.Height, p.Relocated));
        var shrunk = WindowPlacement.Restore(Saved(0, 0, 3000, 2000), [hiDpi]);
        Assert.Equal((1920.0, 1060.0), (shrunk.Width, shrunk.Height));
    }

    [Fact]
    public void No_saved_state_centres_the_default_size_on_the_primary_monitor()
    {
        var p = WindowPlacement.Restore(null, [Right, Main]);
        Assert.Equal(((1920 - 1100) / 2, (1040 - 720) / 2, false), (p.X, p.Y, p.Relocated));
        Assert.Equal((WindowPlacement.DefaultWidth, WindowPlacement.DefaultHeight), (p.Width, p.Height));
    }

    [Fact]
    public void Maximised_flag_is_carried_over_even_when_relocated()
    {
        Assert.True(WindowPlacement.Restore(Saved(5000, 5000, max: true), [Main]).Maximized);
        Assert.True(WindowPlacement.Restore(Saved(10, 10, max: true), [Main]).Maximized);
    }

    [Fact]
    public void Primary_flag_picks_the_monitor_not_the_list_order()
    {
        var p = WindowPlacement.Restore(Saved(9000, 9000), [Right, Main]);
        Assert.InRange(p.X, 0, 1919);
    }

    [Fact]
    public void Without_any_monitor_information_the_saved_size_is_used()
    {
        var p = WindowPlacement.Restore(Saved(5, 5, 800, 600), []);
        Assert.Equal((800.0, 600.0), (p.Width, p.Height));
    }
}

public class ShellLookTests
{
    [Theory]
    [InlineData(19045, false)] // Windows 10 22H2
    [InlineData(21996, false)]
    [InlineData(22000, true)]  // Windows 11 21H2
    [InlineData(22621, true)]  // 22H2
    [InlineData(26100, true)]  // 24H2
    public void Mica_only_on_Windows_11(int build, bool mica)
    {
        var look = ShellLook.Choose(isWindows: true, isMac: false, build);
        Assert.Equal(mica, look.Mica);
        Assert.False(look.NativeMenu);
        Assert.False(look.TabsInTitleBar);
    }

    [Fact]
    public void Mac_has_native_menu_and_tabs_in_the_title_bar_without_mica()
    {
        var look = ShellLook.Choose(isWindows: false, isMac: true, 0);
        Assert.Equal(new ShellLook(false, true, true), look);
    }
}
