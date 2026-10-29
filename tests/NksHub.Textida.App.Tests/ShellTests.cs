using System.Text;
using NksHub.Textida.Core.Encodings;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using NksHub.Textida.App.Profile;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Startup;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public partial class ShellTests : IDisposable
{
    private readonly TempDir _temp = new();

    static ShellTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public void Dispose()
    {
        if (Application.Current is App app && app.Journal is not null) TestApp.Cleanup(app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static readonly string[] CsMenus = ["Soubor", "Úpravy", "Hledat", "Zobrazit", "Formát", "Nástroje", "Nastavení", "Okno", "Nápověda"];
    private static readonly string[] EnMenus = ["File", "Edit", "Search", "View", "Format", "Tools", "Settings", "Window", "Help"];

    [GeneratedRegex(@"^[a-z0-9]+(\.[A-Za-z0-9]+)+$")]
    private static partial Regex LooksLikeKey();

    private static void AssertNoRawKeys(MainWindow w)
    {
        foreach (var v in w.GetVisualDescendants())
        {
            var shown = v switch { TextBlock t => t.Text, MenuItem m => m.Header as string, _ => null };
            if (shown is not null) Assert.False(LooksLikeKey().IsMatch(shown), $"raw key shown: {shown}");
            if (v is Control c && AutomationProperties.GetName(c) is { } name) Assert.False(string.IsNullOrWhiteSpace(name), $"empty accessible name on {c.GetType().Name}");
        }
    }

    private static IEnumerable<string> Labels(IEnumerable<MenuNode> nodes) => nodes.SelectMany(n => n switch
    {
        MenuCommand c => [c.Label],
        MenuGroup g => Labels(g.Items).Prepend(g.Label),
        _ => [],
    });

    [AvaloniaFact]
    public void Starts_into_an_empty_workspace_without_a_dialog()
    {
        var app = TestApp.Prepare(_temp);
        var w = app.CreateWindow(null);
        Assert.Empty(w.Tabs);
        Assert.Null(w.Active);
        Assert.Equal(Loc.Get(K.AppName), w.Title);
        Assert.Equal(CsMenus, w.MenuHeaders);
        Assert.Single(app.Windows);
        w.Close();
    }

    [AvaloniaFact]
    public void Language_switch_leaves_no_empty_label_and_does_not_touch_the_document()
    {
        var app = TestApp.Prepare(_temp);
        var w = app.CreateWindow(null);
        var doc = w.NewDocument();
        doc.Editor!.Text = "Příliš žluťoučký kůň\r\nřádek 2\n";
        var bytesBefore = Encoding.UTF8.GetBytes(doc.Editor.Text);
        app.SetLanguage("cs");
        Assert.Null(app.Workbench.Set("editor.fontSize", System.Text.Json.Nodes.JsonValue.Create(13)));
        Assert.Null(app.Workbench.Set("tools.diffArguments", "\"{left}\" \"{right}\" –ž"));
        var settingsBefore = File.ReadAllBytes(app.Workbench.UserSettingsPath);

        foreach (var lang in Loc.Languages.Concat(["cs"]))
        {
            app.SetLanguage(lang);
            Assert.Equal(lang, Loc.Language);
            Assert.All(Labels(w.MenuModel), l => Assert.False(string.IsNullOrWhiteSpace(l)));
            AssertNoRawKeys(w);
        }

        Assert.Equal(CsMenus, w.MenuHeaders);
        Assert.Equal(bytesBefore, Encoding.UTF8.GetBytes(doc.Editor.Document.Text));
        Assert.Equal(settingsBefore, File.ReadAllBytes(app.Workbench.UserSettingsPath));
        Assert.Same(doc, w.Active);
        w.Close();
    }

    [AvaloniaFact]
    public void Language_switch_translates_menu_tab_title_and_status()
    {
        var app = TestApp.Prepare(_temp);
        var w = app.CreateWindow(null);
        w.NewDocument();
        app.SetLanguage("en");
        Assert.Equal(EnMenus, w.MenuHeaders);
        Assert.Contains("Untitled", w.Active!.Title);
        Assert.Equal("1 result", Loc.Plural(K.SettingsResultCount, 1));
        app.SetLanguage("sk");
        Assert.Equal(["Súbor", "Úpravy", "Hľadať", "Zobraziť", "Formát", "Nástroje", "Nastavenia", "Okno", "Pomocník"], w.MenuHeaders);
        Assert.Contains("Bez názvu", w.Active!.Title);
        app.SetLanguage("de");
        Assert.Equal(["Datei", "Bearbeiten", "Suchen", "Ansicht", "Format", "Extras", "Einstellungen", "Fenster", "Hilfe"], w.MenuHeaders);
        Assert.Contains("Unbenannt", w.Active!.Title);
        app.SetLanguage("cs");
        Assert.Contains("Bez názvu", w.Active!.Title);
        w.Close();
    }

    [AvaloniaFact]
    public void Theme_switches_at_runtime()
    {
        var app = TestApp.Prepare(_temp);
        var w = app.CreateWindow(null);
        app.SetTheme("dark");
        Assert.Equal(ThemeVariant.Dark, app.ActualThemeVariant);
        Assert.Equal("dark", app.Settings.Theme);
        app.SetTheme("light");
        Assert.Equal(ThemeVariant.Light, app.ActualThemeVariant);
        app.SetTheme("system");
        Assert.Equal(ThemeVariant.Default, app.RequestedThemeVariant);
        w.Close();
    }

    [AvaloniaFact]
    public void Windows_11_uses_mica_and_Windows_10_a_solid_theme_background()
    {
        var app = TestApp.Prepare(_temp);
        var win11 = new MainWindow(app, ShellLook.Choose(true, false, 22621), null);
        Assert.Equal(WindowTransparencyLevel.Mica, win11.TransparencyLevelHint[0]);
        Assert.Equal(Brushes.Transparent, win11.Background);

        var win10 = new MainWindow(app, ShellLook.Choose(true, false, 19045), null);
        Assert.Equal([WindowTransparencyLevel.None], win10.TransparencyLevelHint);
        Assert.NotEqual(Brushes.Transparent, win10.Background); // the Bg0 resource of the theme applies
        win11.Close();
        win10.Close();
    }

    [AvaloniaFact]
    public void Chrome_is_only_menu_tabs_and_status_bar_no_toolbar()
    {
        var app = TestApp.Prepare(_temp);
        var w = app.CreateWindow(null, look: TestApp.WindowsLook);
        w.NewDocument();
        Assert.All(w.GetVisualDescendants().OfType<Button>(), b => Assert.True(b.Classes.Contains("tabclose") || b.Classes.Contains("statusitem") || b.Classes.Contains("panelaction") || b.Classes.Contains("outputtab") || b.Classes.Contains("outputaction") || b.FindAncestorOfType<FindBar>() is not null, "only tab close buttons, status bar items, the empty-panel action, the hidden output window and the find bar may have buttons in the chrome"));
        Assert.Single(w.GetVisualDescendants().OfType<Menu>());
        Assert.Single(w.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.TabStrip>());
        Assert.Equal(CsMenus, w.GetVisualDescendants().OfType<Menu>().Single().Items.OfType<MenuItem>().Select(m => MenuAccessKeys.Unmark((string)m.Header!)));
        w.Close();
    }

    [AvaloniaFact]
    public void The_menu_itself_carries_the_accessible_name_in_the_ui_language()
    {
        var app = TestApp.Prepare(_temp);
        var w = app.CreateWindow(null, look: TestApp.WindowsLook);
        try
        {
            Assert.Equal("Hlavní nabídka", AutomationProperties.GetName(w.GetVisualDescendants().OfType<Menu>().Single()));
            Loc.SetLanguage("de");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal("Hauptmenü", AutomationProperties.GetName(w.GetVisualDescendants().OfType<Menu>().Single()));
        }
        finally
        {
            Loc.SetLanguage("cs");
            w.Close();
        }
    }

    [AvaloniaFact]
    public void Hidden_documents_panel_opens_from_the_keyboard()
    {
        var app = TestApp.Prepare(_temp);
        var w = app.CreateWindow(null);
        w.NewDocument();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // let the deferred focus of the new tab settle first
        Assert.False(w.DocumentsPanelOpen);

        var mod = Shortcuts.Primary == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control;
        w.KeyPress(Key.B, mod, PhysicalKey.B, "b");
        Assert.True(w.DocumentsPanelOpen);
        w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(w.DocumentsPanelOpen);
        w.Close();
    }

    [AvaloniaFact]
    public void New_document_and_close_tab_from_the_keyboard()
    {
        var app = TestApp.Prepare(_temp);
        var w = app.CreateWindow(null);
        var mod = Shortcuts.Primary == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control;
        w.KeyPress(Key.N, mod, PhysicalKey.N, "n");
        w.KeyPress(Key.N, mod, PhysicalKey.N, "n");
        Assert.Equal(2, w.Tabs.Count());
        w.KeyPress(Key.W, mod, PhysicalKey.W, "w");
        Assert.Single(w.Tabs);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Request_with_wait_completes_only_after_the_tab_is_closed()
    {
        var app = TestApp.Prepare(_temp);
        var w = app.CreateWindow(null);
        var file = _temp.File("commit.txt", "one\ntwo\nthree\nfour\n");

        var wait = app.OpenRequestAsync(new IpcRequest { Wait = true, Files = [new IpcFile { Path = file, Line = 3 }] });
        Assert.False(wait.IsCompleted);
        var tab = w.Tabs.Single();
        Assert.Equal(file, tab.Path);
        Assert.Equal(3L, tab.CaretPosition().Line);

        w.CloseTab(tab);
        await wait.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(wait.IsCompletedSuccessfully);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Wait_also_completes_when_the_window_is_closed()
    {
        var app = TestApp.Prepare(_temp);
        var w = app.CreateWindow(null);
        var wait = app.OpenRequestAsync(new IpcRequest { Wait = true, Files = [new IpcFile { Path = _temp.File("a.txt", "x") }] });
        Assert.False(wait.IsCompleted);
        w.Close();
        await wait.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [AvaloniaFact]
    public void Request_without_wait_returns_at_once_and_reopening_does_not_duplicate_the_tab()
    {
        var app = TestApp.Prepare(_temp);
        var w = app.CreateWindow(null);
        var file = _temp.File("a.txt", "l1\nl2\nl3\n");

        Assert.True(app.OpenRequestAsync(new IpcRequest { Files = [new IpcFile { Path = file }] }).IsCompletedSuccessfully);
        Assert.True(app.OpenRequestAsync(new IpcRequest { Files = [new IpcFile { Path = file, Line = 2 }] }).IsCompletedSuccessfully);
        Assert.Single(w.Tabs);
        Assert.Equal(2L, w.Active!.CaretPosition().Line);
        w.Close();
    }

    [AvaloniaFact]
    public void New_window_request_opens_a_second_window_with_the_file()
    {
        var app = TestApp.Prepare(_temp);
        var first = app.CreateWindow(null);
        var file = _temp.File("b.txt", "x");

        app.OpenRequestAsync(new IpcRequest { NewWindow = true, Files = [new IpcFile { Path = file }] });

        Assert.Equal(2, app.Windows.Count);
        Assert.Empty(first.Tabs);
        Assert.Single(app.Windows[1].Tabs);
        foreach (var w in app.Windows.ToList()) w.Close();
    }

    [AvaloniaFact]
    public async Task Unreadable_file_fails_the_request_but_other_files_still_open()
    {
        var app = TestApp.Prepare(_temp);
        var w = app.CreateWindow(null);
        var good = _temp.File("good.txt", "x");
        var missing = Path.Combine(_temp.Path, "missing.txt");

        var task = app.OpenRequestAsync(new IpcRequest { Files = [new IpcFile { Path = missing }, new IpcFile { Path = good }] });

        var ex = await Assert.ThrowsAsync<IOException>(() => task);
        Assert.Contains(missing, ex.Message);
        Assert.Equal(good, w.Tabs.Single().Path);
        w.Close();
    }

    [AvaloniaFact]
    public void Single_byte_file_is_decoded_and_editable()
    {
        var app = TestApp.Prepare(_temp);
        var w = app.CreateWindow(null);
        var path = Path.Combine(_temp.Path, "latin2.txt");
        File.WriteAllBytes(path, Encoding.GetEncoding(1250).GetBytes("Příliš žluťoučký kůň"));

        var tab = w.OpenFile(path, null);

        Assert.Equal("Příliš žluťoučký kůň", tab.Editor!.Document.Text);
        Assert.False(tab.Editor.IsReadOnly);
        Assert.Equal(TextCodec.Cp1250, tab.Doc.Doc.Codec);
        w.Close();
    }

    [AvaloniaFact]
    public void File_over_the_four_mebibyte_skeleton_cap_opens_now()
    {
        var app = TestApp.Prepare(_temp);
        var w = app.CreateWindow(null);
        var path = Path.Combine(_temp.Path, "big.txt");
        File.WriteAllBytes(path, new byte[5 << 20].Select(_ => (byte)'x').ToArray());

        var tab = w.OpenFile(path, null);

        Assert.False(tab.Doc.IsLarge); // 5 MiB is still below the switch size
        Assert.True(tab.Doc.Doc.ByteLength > 4 << 20);
        Assert.NotNull(tab.Editor);
        w.Close();
    }

    [AvaloniaFact]
    public void Untitled_documents_are_editable_and_numbered()
    {
        var app = TestApp.Prepare(_temp);
        var w = app.CreateWindow(null);
        var a = w.NewDocument();
        var b = w.NewDocument();
        Assert.False(a.Editor!.IsReadOnly);
        Assert.NotEqual(a.Title, b.Title);
        w.Close();
    }

    [AvaloniaFact]
    public void Saved_position_off_every_monitor_is_not_used()
    {
        var app = TestApp.Prepare(_temp);
        var w = app.CreateWindow(new SavedWindow { X = 90000, Y = 90000, Width = 900, Height = 600 });
        var screens = w.Screens.All;
        Assert.NotEmpty(screens);
        Assert.Contains(screens, s => s.WorkingArea.Contains(w.Position));
        w.Close();
    }

    [AvaloniaFact]
    public void Geometry_is_captured_for_the_profile()
    {
        var app = TestApp.Prepare(_temp);
        var w = app.CreateWindow(new SavedWindow { X = 50, Y = 60, Width = 900, Height = 600 });
        var g = w.CaptureGeometry();
        Assert.Equal((900.0, 600.0, false), (g.Width, g.Height, g.Maximized));
        w.Close();
    }
}
