using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>Menus, palette and keys read the command registry and nothing else.</summary>
[Collection("LocState")]
public class MenuPaletteTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public MenuPaletteTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
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

    private MenuCommand Item(string id) => Commands(_w.MenuModel.SelectMany(g => g.Items)).Single(c => c.CommandId == id);

    [AvaloniaFact]
    public void Menu_bar_has_the_d5_menus_that_have_commands_and_leaves_the_others_out()
    {
        Assert.Equal(["Soubor", "Úpravy", "Hledat", "Zobrazit", "Formát", "Nastavení", "Okno", "Nápověda"], _w.MenuHeaders);
        var shown = MenuBuilder.Order.Select(o => o.Id).ToList();
        Assert.Equal(["file", "projects", "edit", "search", "view", "format", "tools", "html", "settings", "window", "help"], shown);
        // projects, tools and html have no commands in the manifest, so they are not drawn
        var withCommands = _app.Workbench.Commands.MenuContributions.Select(m => m.Menu).ToHashSet();
        Assert.DoesNotContain("projects", withCommands);
        Assert.DoesNotContain("tools", withCommands);
        Assert.DoesNotContain("html", withCommands);
    }

    [AvaloniaFact]
    public void Every_menu_item_is_a_registered_command_with_a_handler_never_a_placeholder()
    {
        var all = Commands(_w.MenuModel.SelectMany(g => g.Items)).ToList();
        Assert.True(all.Count > 40);
        Assert.All(all, c =>
        {
            Assert.NotNull(c.CommandId);
            Assert.True(_app.Workbench.Commands.TryGet(c.CommandId!, out _), c.CommandId);
            Assert.True(_app.Workbench.Commands.HasHandler(c.CommandId!), c.CommandId + " has no handler");
            Assert.False(string.IsNullOrWhiteSpace(c.Label));
        });
        // the document search belongs to P2.4; its commands exist in the manifest but are not offered yet
        Assert.DoesNotContain(all, c => c.CommandId is "nks.search.find" or "nks.search.replace" or "nks.search.findNext");
        Assert.Contains(all, c => c.CommandId == "nks.search.gotoLine");
    }

    [AvaloniaFact]
    public void Menu_items_show_the_registry_state_and_the_keymap_shortcut()
    {
        Assert.False(Item("nks.file.save").EnabledNow!());
        Assert.False(Item("nks.edit.undo").EnabledNow!());
        Assert.True(Item("nks.file.new").EnabledNow!());
        var save = Item("nks.file.save");
        Assert.Equal(new KeyGesture(Key.S, Shortcuts.Primary), save.Gesture);

        var tab = _w.NewDocument();
        Assert.True(Item("nks.file.save").EnabledNow!());
        Assert.True(Item("nks.edit.undo").EnabledNow!());
        Assert.False(Item("nks.edit.upperCase").EnabledNow!()); // needs a selection
        tab.Editor!.Document.Text = "abc";
        tab.Editor.Select(0, 2);
        Assert.True(Item("nks.edit.upperCase").EnabledNow!());
    }

    [AvaloniaFact]
    public void Menu_commands_run_through_the_registry()
    {
        Item("nks.file.new").Run();
        Item("nks.file.new").Run();
        Assert.Equal(2, _w.Tabs.Count());
        Item("nks.window.new").Run();
        Assert.Equal(2, _app.Windows.Count);
        foreach (var w in _app.Windows.Skip(1).ToList()) w.Close();
    }

    [AvaloniaFact]
    public void Settings_menu_switches_theme_and_language_with_a_checked_item()
    {
        Assert.True(Item("nks.settings.theme.system").Checked);
        Item("nks.settings.theme.dark").Run();
        Assert.Equal("dark", _app.Settings.Theme);
        Assert.True(Item("nks.settings.theme.dark").Checked);
        Assert.False(Item("nks.settings.theme.system").Checked);
        Item("nks.settings.language.en").Run();
        Assert.Equal(["File", "Edit", "Search", "View", "Format", "Settings", "Window", "Help"], _w.MenuHeaders);
        Item("nks.settings.language.cs").Run();
        Item("nks.settings.theme.system").Run();
    }

    [AvaloniaFact]
    public void Settings_menu_opens_the_settings_view()
    {
        Item("nks.app.openSettings").Run();
        Assert.NotNull(_app.SettingsView);
        _app.SettingsView!.Close();
    }

    [AvaloniaFact]
    public void Menu_in_the_window_is_rendered_from_the_model()
    {
        var menu = _w.GetVisualDescendants().OfType<Menu>().Single();
        var file = menu.Items.OfType<MenuItem>().First();
        Assert.Equal("Soubor", file.Header);
        var labels = file.Items.OfType<MenuItem>().Select(m => (string)m.Header!).ToList();
        Assert.Contains("Nový", labels);
        Assert.Contains("Otevřít...", labels);
        Assert.Contains(file.Items, i => i is Separator);
        var save = file.Items.OfType<MenuItem>().First(m => (string)m.Header! == "Uložit");
        Assert.False(save.IsEnabled);
        _w.NewDocument();
        file.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
        Assert.True(save.IsEnabled);
    }

    // ---- keys ----

    [AvaloniaFact]
    public void Keys_come_from_the_keymap_so_a_user_binding_works_without_any_hard_wired_handler()
    {
        File.WriteAllText(Path.Combine(_app.ProfileDirectory, AppWorkbench.UserKeymapFile),
            """{ "schemaVersion": 1, "bindings": [ { "key": "mod+alt+9", "command": "nks.view.zoomIn" } ] }""");
        var app = TestApp.Restart(_temp, _app, _dialogs);
        var w = app.CreateWindow(null);
        w.NewDocument();
        double before = app.Workbench.Settings.GetNumber("editor.fontSize");
        TestApp.Press(w, Key.D9, TestApp.Mod | RawInputModifiers.Alt);
        Assert.Equal(before + 1, app.Workbench.Settings.GetNumber("editor.fontSize"));
        w.Close();
    }

    [AvaloniaFact]
    public void Plain_typing_is_not_swallowed_by_the_dispatcher()
    {
        var tab = _w.NewDocument();
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();
        TestApp.Press(_w, Key.A);
        TestApp.Type(_w, "a");
        Assert.Equal("a", tab.Editor.Document.Text);
    }

    [AvaloniaFact]
    public void Key_of_a_disabled_command_does_nothing_and_does_not_fall_through_to_typing()
    {
        // Ctrl+Shift+K (delete line) with no document: the command is disabled, the keys are swallowed and nothing is created
        TestApp.Press(_w, Key.K, TestApp.Mod | RawInputModifiers.Shift);
        Assert.Empty(_w.Tabs);
    }

    // ---- palette ----

    [AvaloniaFact]
    public void Palette_opens_with_the_shortcut_filters_with_the_registry_and_runs_the_choice()
    {
        var path = _temp.File("p.txt", "x");
        var tab = _w.OpenFile(path, null);
        tab.Editor!.Document.Insert(1, "y");
        TestApp.Press(_w, Key.P, TestApp.Mod | RawInputModifiers.Shift);
        Assert.True(_w.IsPaletteOpen);

        _w.SetPaletteQuery("uložit");
        Assert.Equal("nks.file.save", _w.PaletteResults[0].CommandId);
        Assert.Equal(Shortcuts.Display(Key.S), _w.PaletteResults[0].Shortcut);

        TestApp.Press(_w, Key.Enter);
        Assert.False(_w.IsPaletteOpen);
        Assert.Equal("xy", File.ReadAllText(path));
    }

    [AvaloniaFact]
    public void Palette_lists_only_commands_that_exist_and_closes_on_escape()
    {
        _w.ShowPalette();
        _w.SetPaletteQuery("");
        Assert.True(_w.PaletteResults.Count > 30);
        Assert.All(_w.PaletteResults, r => Assert.True(_app.Workbench.Commands.HasHandler(r.CommandId)));
        _w.SetPaletteQuery("najít");
        Assert.DoesNotContain(_w.PaletteResults, r => r.CommandId.StartsWith("nks.search.find", StringComparison.Ordinal));
        _w.SetPaletteQuery("zzzzzz");
        Assert.Empty(_w.PaletteResults);
        TestApp.Press(_w, Key.Escape);
        Assert.False(_w.IsPaletteOpen);
    }

    [AvaloniaFact]
    public void Palette_arrow_keys_move_the_choice_and_disabled_commands_are_dimmed()
    {
        _w.ShowPalette();
        _w.SetPaletteQuery("nov");
        var first = _w.PaletteResults[0];
        Assert.True(_w.PaletteResults.Count >= 2);
        TestApp.Press(_w, Key.Down);
        TestApp.Press(_w, Key.Enter);
        Assert.False(_w.IsPaletteOpen);

        foreach (var t in _w.Tabs.ToList()) _w.CloseTab(t);
        _w.ShowPalette();
        _w.SetPaletteQuery("uložit vše");
        var item = _w.PaletteResults.First(r => r.CommandId == "nks.file.saveAll");
        Assert.False(item.Enabled); // no document yet
    }

    [AvaloniaFact]
    public void Palette_matches_by_category_and_without_diacritics_through_the_query_service()
    {
        _w.ShowPalette();
        _w.SetPaletteQuery("zobrazeni");
        Assert.Contains(_w.PaletteResults, r => r.Category == "Zobrazit" || r.CommandId.StartsWith("nks.view.", StringComparison.Ordinal));
    }
}
