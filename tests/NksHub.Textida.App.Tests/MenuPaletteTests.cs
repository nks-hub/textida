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
internal static class MenuNodeExtensions
{
    /// <summary>Every command of the nodes, submenus included.</summary>
    public static IEnumerable<MenuCommand> AllCommands(this IEnumerable<MenuNode> nodes) => nodes.SelectMany(n => n switch
    {
        MenuCommand c => [c],
        MenuGroup g => g.Items.AllCommands(),
        _ => [],
    });
}

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
        _w = _app.CreateWindow(null, look: TestApp.WindowsLook);
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
        Assert.Equal(["Soubor", "Úpravy", "Hledat", "Zobrazit", "Formát", "Nástroje", "Nastavení", "Okno", "Nápověda"], _w.MenuHeaders);
        var shown = MenuBuilder.Order.Select(o => o.Id).ToList();
        Assert.Equal(["file", "projects", "edit", "search", "view", "format", "tools", "hex", "html", "settings", "window", "help"], shown);
        // projects has no commands in the manifest, so it is not drawn; hex and html have them, but only for their kind of document, so a window without one draws neither
        var withCommands = _app.Workbench.Commands.MenuContributions.Select(m => m.Menu).ToHashSet();
        Assert.DoesNotContain("projects", withCommands);
        Assert.Contains("tools", withCommands);
        Assert.Contains("hex", withCommands);
        Assert.Contains("html", withCommands);
    }

    private MenuGroup Menu(string header) => _w.MenuModel.Single(g => g.Label == header);

    /// <summary>The header of a menu item without the access key mark.</summary>
    private static string Plain(object? header) => MenuAccessKeys.Unmark((string)header!);

    [AvaloniaFact]
    public void Access_keys_are_unique_in_every_menu_of_every_language_and_the_marks_reach_the_menu_items()
    {
        foreach (var lang in Loc.Languages)
        {
            _app.SetLanguage(lang);
            AssertUnique(lang, _w.MenuModel.Select(g => (g.Label, g.AccessAt)).ToList());
            foreach (var topMenu in _w.MenuModel) AssertLevel(lang + " > " + topMenu.Label, topMenu.Items);
        }

        static void AssertUnique(string where, IReadOnlyList<(string Label, int At)> rows)
        {
            var keys = rows.Where(r => r.At >= 0).Select(r => char.ToLowerInvariant(r.Label[r.At])).ToList();
            Assert.True(keys.Count == keys.Distinct().Count(), $"{where}: an access key is used twice");
            Assert.All(rows.Where(r => r.At >= 0), r => Assert.True(char.IsAsciiLetterOrDigit(r.Label[r.At]), $"{where}: {r.Label} has a key that is not A to Z or 0 to 9"));
        }

        static void AssertLevel(string where, IReadOnlyList<MenuNode> nodes)
        {
            AssertUnique(where, [.. nodes.Select(n => n switch { MenuCommand c => (c.Label, c.AccessAt), MenuGroup g => (g.Label, g.AccessAt), _ => ("", -1) }).Where(r => r.Item1.Length > 0)]);
            foreach (var group in nodes.OfType<MenuGroup>()) AssertLevel(where + " > " + group.Label, group.Items);
        }

        _app.SetLanguage("cs");
        var menu = _w.GetVisualDescendants().OfType<Menu>().Single();
        var file = menu.Items.OfType<MenuItem>().First();
        Assert.Equal("_Soubor", (string)file.Header!);
        Assert.Equal("Soubor", Avalonia.Automation.AutomationProperties.GetName(file));
        Assert.Equal("Nový", Plain(file.Items.OfType<MenuItem>().First().Header));
    }

    [AvaloniaFact] // the constructor of the class needs the application
    public void Access_key_assignment_prefers_the_first_letter_then_other_words_then_any_letter_and_never_repeats()
    {
        // the second and the third word that start with N take a later letter; the marked letter of an accented first letter is not a key, so Ú gives way to P
        Assert.Equal([0, 0, 0, 3], MenuAccessKeys.Assign(["Soubor", "Hledat", "Nastavení", "Nástroje"])); // N is taken, á is no key and s belongs to Soubor, so Nástroje gets t
        Assert.Equal([1], MenuAccessKeys.Assign(["Úpravy"]));
        Assert.Equal([0, 5, 0], MenuAccessKeys.Assign(["Open Folder", "Open File", "Save"])); // the first letter of the second word beats any other letter
        Assert.Equal([-1], MenuAccessKeys.Assign(["..."]));
        Assert.Equal("_Soubor", MenuAccessKeys.Mark("Soubor", 0));
        Assert.Equal("a__b_c", MenuAccessKeys.Mark("a_bc", 3));
        Assert.Equal("a__b", MenuAccessKeys.Mark("a_b", -1));
        Assert.Equal("a_b", MenuAccessKeys.Unmark("_a__b"));
    }

    [AvaloniaFact]
    public void Submenus_are_built_from_the_submenu_field_with_separators_between_their_groups()
    {
        // the tools of a language show for a file of that language
        _w.OpenFile(_temp.File("a.sql", "select 1;\n"), null);
        var tools = Menu("Nástroje");
        var macros = Assert.Single(tools.Items.OfType<MenuGroup>(), g => g.Label == "Makra");
        Assert.Contains(Commands(macros.Items), c => c.CommandId == "nks.macro.record");
        Assert.DoesNotContain(tools.Items.OfType<MenuCommand>(), c => c.CommandId!.StartsWith("nks.macro.", StringComparison.Ordinal));
        Assert.Contains(tools.Items.OfType<MenuCommand>(), c => c.CommandId == "nks.plugins.manage");

        var code = Assert.Single(tools.Items.OfType<MenuGroup>(), g => g.Label == "Kód a jazyk");
        Assert.Contains(Commands(code.Items), c => c.CommandId == "nks.tools.sqlDialect");

        // a submenu may hold another: the language server is a submenu of the code tools
        var lsp = Assert.Single(code.Items.OfType<MenuGroup>(), g => g.Label == "Jazykový server");
        Assert.Contains(lsp.Items.OfType<MenuCommand>(), c => c.CommandId == "nks.lsp.goToDefinition");
        Assert.DoesNotContain(code.Items.OfType<MenuCommand>(), c => c.CommandId!.StartsWith("nks.lsp.", StringComparison.Ordinal));
        // the carets are a block of the selection submenu, not a submenu of their own
        var select = Assert.Single(Menu("Úpravy").Items.OfType<MenuGroup>(), g => g.Label == "Výběr");
        Assert.Contains(select.Items.OfType<MenuCommand>(), c => c.CommandId == "nks.edit.addNextOccurrence");
        Assert.Empty(select.Items.OfType<MenuGroup>());

        var format = Assert.Single(Menu("Formát").Items.OfType<MenuGroup>(), g => g.Label == "Formátovat kód a data");
        Assert.Contains(Commands(format.Items), c => c.CommandId == "nks.edit.formatJson");
        Assert.Contains(Commands(format.Items), c => c.CommandId == "nks.edit.formatSql");
        Assert.Contains(Menu("Úpravy").Items.OfType<MenuCommand>(), c => c.CommandId == "nks.edit.undo");

        // one submenu gathers entries of several groups: they stay in their order, split by a separator
        _w.OpenFile(_temp.File("a.md", "# t\n"), null);
        var mode = Assert.Single(Menu("Zobrazit").Items.OfType<MenuGroup>(), g => g.Label == "Režim zobrazení");
        Assert.Contains(mode.Items, n => n is MenuSeparator);
        Assert.Contains(Commands(mode.Items), c => c.CommandId == "nks.view.modeRead");
    }

    /// <summary>
    /// Rozhodnuto 7. 11. 2026 (autonomně): a menu has at most 15 entries and 19 rows with its separators. A row is 30 px and a separator 9 px, so 15 + 4 make 494 px and fit under the menu bar of a
    /// 1366 × 768 screen at 125 % (about 514 px are free there). A submenu has at most 12 entries; submenus nest at most two deep (menu › submenu › submenu).
    /// </summary>
    internal const int MaxMenuEntries = 15, MaxMenuRows = 19, MaxSubmenuEntries = 12, MaxSubmenuLevels = 2;

    [AvaloniaFact]
    public void No_menu_or_submenu_is_longer_than_the_limit_and_submenus_nest_at_most_two_deep_for_any_kind_of_document()
    {
        void Walk(string where, IReadOnlyList<MenuNode> nodes, int level)
        {
            int entries = nodes.Count(n => n is not MenuSeparator);
            Assert.True(entries <= (level == 0 ? MaxMenuEntries : MaxSubmenuEntries), $"{where} has {entries} entries");
            if (level == 0) Assert.True(nodes.Count <= MaxMenuRows, $"{where} has {nodes.Count} rows");
            Assert.True(level <= MaxSubmenuLevels, $"{where} is a submenu {level} deep");
            Assert.False(nodes.FirstOrDefault() is MenuSeparator || nodes.LastOrDefault() is MenuSeparator, $"{where} starts or ends with a separator");
            Assert.DoesNotContain(nodes.Zip(nodes.Skip(1)), p => p.First is MenuSeparator && p.Second is MenuSeparator);
            foreach (var group in nodes.OfType<MenuGroup>()) Walk(where + " > " + group.Label, group.Items, level + 1);
        }
        foreach (var (file, text) in new[] { ("a.txt", "a\n"), ("a.md", "# t\n"), ("a.json", "{}\n"), ("a.html", "<p>x</p>\n"), ("a.csv", "a,b\n1,2\n"), ("a.sql", "select 1;\n"), ("a.log", "ERROR x\n") })
        {
            _w.SelectTab(_w.OpenFile(_temp.File(file, text), null));
            foreach (var menu in _w.MenuModel) Walk(file + ": " + menu.Label, menu.Items, 0);
            // the context menu of the editor is short: the clipboard, the comment and the few actions of the kind of file
            var context = MenuBuilder.Nodes(_app.Workbench, new ShellContext(_w, forMenu: true), _ => null, _ => { }, "editor.context");
            Assert.True(context.Count(n => n is not MenuSeparator) <= MaxSubmenuEntries, $"{file}: the context menu has {context.Count} rows");
        }
        _w.SelectTab(_w.OpenHex(_temp.File("h.bin", "hello\n"), null));
        Assert.Contains(_w.MenuModel, g => g.Label == "Hex");
        foreach (var menu in _w.MenuModel) Walk("hex: " + menu.Label, menu.Items, 0);
    }

    [AvaloniaFact]
    public void Every_command_with_a_handler_is_reachable_from_a_menu_the_palette_or_a_key()
    {
        var wb = _app.Workbench;
        var inMenus = wb.Commands.CommandsInMenus();
        var bound = wb.Keymap.Bindings.Select(b => b.CommandId).ToHashSet(StringComparer.Ordinal);
        // the palette lists every command that is not hidden; a hidden one needs a menu entry or a key, or is a caret step the keys of the editor run and a macro records
        var lost = wb.Commands.Commands.Where(c => wb.Commands.HasHandler(c.Id) && c.Hidden && !c.Macro && !inMenus.Contains(c.Id) && !bound.Contains(c.Id)).Select(c => c.Id).ToList();
        Assert.True(lost.Count == 0, "No way to run: " + string.Join(", ", lost));
        // commands the review took out of the menus are found in the palette by their title
        _w.SelectTab(_w.OpenFile(_temp.File("a.txt", "alpha\n"), null));
        _w.ShowPalette();
        foreach (var id in new[] { "nks.settings.theme.dark", "nks.remote.proxy", "nks.macro.delete", "nks.edit.fragmentJson", "nks.lsp.syntaxTree" })
        {
            Assert.DoesNotContain(id, inMenus);
            wb.Commands.TryGet(id, out var command);
            _w.SetPaletteQuery(wb.Strings.Get(command.TitleKey));
            Assert.True(_w.PaletteResults.Any(r => r.CommandId == id), id + " is not in the palette");
        }
        _w.ClosePalette();
    }

    [AvaloniaFact]
    public void A_command_in_a_submenu_is_enabled_and_disabled_like_one_at_the_top_level_and_the_item_has_a_name()
    {
        var menu = _w.GetVisualDescendants().OfType<Menu>().Single();
        var edit = menu.Items.OfType<MenuItem>().Single(m => Plain(m.Header) == "Úpravy");
        var casing = edit.Items.OfType<MenuItem>().Single(m => Plain(m.Header) == "Převody a recepty");
        Assert.Equal("Převody a recepty", Avalonia.Automation.AutomationProperties.GetName(casing));
        var upper = casing.Items.OfType<MenuItem>().First(m => m.Tag is MenuCommand { CommandId: "nks.edit.upperCase" });
        Assert.False(upper.IsEnabled);
        var gesture = ((MenuCommand)upper.Tag!).Gesture;
        Assert.Equal(gesture, upper.InputGesture);

        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = "abc";
        tab.Editor.Select(0, 2);
        edit.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
        Assert.True(upper.IsEnabled);
        tab.Editor.Select(0, 0);
        edit.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
        Assert.False(upper.IsEnabled);
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
        Assert.Contains(all, c => c.CommandId == "nks.search.find");
        Assert.Contains(all, c => c.CommandId == "nks.search.findInFiles");
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
    public void Theme_profile_and_language_are_set_in_the_settings_and_their_commands_stay_for_the_palette_and_keys()
    {
        // the review of 7. 11. 2026 took the theme, the profile and the language out of the menu: the settings window has them, so the menu items were duplicates
        var shown = Commands(_w.MenuModel.SelectMany(g => g.Items)).Select(c => c.CommandId!).ToList();
        Assert.DoesNotContain(shown, id => id.StartsWith("nks.settings.theme.", StringComparison.Ordinal) || id.StartsWith("nks.settings.profile.", StringComparison.Ordinal)
            || id.StartsWith(AppCommands.LanguageCommandPrefix, StringComparison.Ordinal));
        var context = new ShellContext(_w, forMenu: true);
        Assert.Equal(CommandOutcome.Handled, _app.Workbench.Commands.ExecuteAsync("nks.settings.theme.dark", context).AsTask().GetAwaiter().GetResult());
        Assert.Equal("dark", _app.Settings.Theme);
        _app.Workbench.Commands.ExecuteAsync("nks.settings.theme.system", context).AsTask().GetAwaiter().GetResult();
        Assert.Equal(CommandOutcome.Handled, _app.Workbench.Commands.ExecuteAsync("nks.settings.language.en", context).AsTask().GetAwaiter().GetResult());
        Assert.Equal(["File", "Edit", "Search", "View", "Format", "Tools", "Settings", "Window", "Help"], _w.MenuHeaders);
        foreach (var lang in Loc.Languages.Reverse())
        {
            _app.Workbench.Commands.ExecuteAsync(AppCommands.LanguageCommandPrefix + lang, context).AsTask().GetAwaiter().GetResult();
            Assert.Equal(lang, _app.Settings.Language);
        }
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
        Assert.Equal("Soubor", Plain(file.Header));
        var labels = file.Items.OfType<MenuItem>().Select(m => Plain(m.Header)).ToList();
        Assert.Contains("Nový", labels);
        Assert.Contains("Otevřít...", labels);
        Assert.Contains(file.Items, i => i is Separator);
        var save = file.Items.OfType<MenuItem>().First(m => Plain(m.Header) == "Uložit");
        Assert.False(save.IsEnabled);
        _w.NewDocument();
        file.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
        Assert.True(save.IsEnabled);
    }

    [AvaloniaFact]
    public void On_macOS_the_menu_is_the_system_menu_with_the_headers_of_the_model_and_the_window_draws_none()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "the system menu bar exists on macOS only");
        var w = _app.CreateWindow(null);
        var native = NativeMenu.GetMenu(w);
        Assert.NotNull(native);
        Assert.Equal(w.MenuHeaders, native.Items.OfType<NativeMenuItem>().Select(i => i.Header));
        var file = native.Items.OfType<NativeMenuItem>().First();
        Assert.Contains(file.Menu!.Items.OfType<NativeMenuItem>(), i => i.Header == "Nový" && new KeyGesture(Key.N, KeyModifiers.Meta).Equals(i.Gesture));
        Assert.Empty(w.GetVisualDescendants().OfType<Menu>());
        w.Close();
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
        Assert.Contains(_w.PaletteResults, r => r.CommandId == "nks.search.find");
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
