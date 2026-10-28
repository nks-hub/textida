using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P9.2.2: what the four languages do to the shell that a catalogue test cannot see. Menus and the palette are laid out in every language and measured,
/// the menus are searched for texts, access keys and shortcuts that collide, every command is looked up in the palette by its translated name,
/// a switch of the language is made with panels open and a screen that still shows a text of the old language is reported, and the switch must change
/// nothing but the language setting. Nobody who speaks Slovak or German has read the texts yet; these gates find what a program can find.
/// </summary>
[Collection("LocState")]
public class UiLanguageGateTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public UiLanguageGateTests()
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

    private void Tick()
    {
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private CommandOutcome Run(string id) => _app.Workbench.Commands.ExecuteAsync(id, new ShellContext(_w, forMenu: true)).AsTask().GetAwaiter().GetResult();

    /// <summary>The text of a label; a label made of runs (the palette marks what was typed) has no <c>Text</c> of its own.</summary>
    private static string TextOf(TextBlock block) =>
        block.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(r => r.Text)) : block.Text ?? "";

    /// <summary>What a person would type for a title on a keyboard without the letters of the language: no marks, "ss" for the sharp s.</summary>
    private static string Plain(string text) =>
        string.Concat(text.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).Replace("ß", "ss");

    // ---- the longest texts in menus and in the palette ----

    private static IEnumerable<(string Label, KeyGesture? Gesture, bool Submenu)> PopupRows(IEnumerable<MenuNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is not MenuGroup group) continue;
            foreach (var row in group.Items)
            {
                if (row is MenuCommand command) yield return (command.Label, command.Gesture, false);
                else if (row is MenuGroup submenu) yield return (submenu.Label, null, true);
            }
            foreach (var nested in PopupRows(group.Items)) yield return nested;
        }
    }

    /// <summary>The items of the menus that are not part of the menu bar: the status bar flyouts and the menu of a tab. Each is read while its flyout is open and closed again.</summary>
    private IEnumerable<(string Label, KeyGesture? Gesture, bool Submenu)> OtherMenuRows()
    {
        foreach (var open in new Func<MenuFlyout?>[] { _w.ShowIndentMenu, _w.ShowEolMenu, _w.ShowLanguageMenu })
        {
            var flyout = open();
            Assert.NotNull(flyout);
            foreach (var item in A11yAudit.MenuItems(flyout.Items))
                if (item.Header is string header) yield return (header, item.InputGesture, item.Items.Count > 0);
            flyout.Hide();
        }
        var tabMenus = _w.ActiveGroup.Strip.Items.OfType<TabStripItem>().Select(i => i.ContextMenu).OfType<ContextMenu>();
        foreach (var item in tabMenus.SelectMany(m => A11yAudit.MenuItems(m.Items)))
            if (item.Header is string header) yield return (header, item.InputGesture, item.Items.Count > 0);
    }

    private static MenuItem Probe(string label, KeyGesture? gesture, bool submenu)
    {
        var item = new MenuItem { Header = label, InputGesture = gesture };
        if (submenu) item.Items.Add(new MenuItem { Header = "-" });
        return item;
    }

    [AvaloniaFact]
    public void Every_menu_row_fits_a_menu_as_wide_as_the_narrowest_window_in_every_language()
    {
        _w.SelectTab(_w.OpenFile(_temp.File("alpha.txt", "alpha\n"), null));
        var problems = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var lang in Loc.Languages)
        {
            _app.SetLanguage(lang);
            Tick();
            // a popup is as wide as its widest row, so each row is laid out alone in a column of the narrowest window the editor allows
            var rows = new StackPanel { Width = _w.MinWidth };
            foreach (var (label, gesture, submenu) in PopupRows(_w.MenuModel).Concat(OtherMenuRows())) rows.Children.Add(Probe(label, gesture, submenu));
            Assert.True(rows.Children.Count > 100, $"{lang}: only {rows.Children.Count} menu rows were found");
            var window = new Window { Width = _w.MinWidth, Height = 800, Content = rows };
            window.Show();
            PseudoLocalizationTests.Layout(window);
            foreach (var block in window.GetVisualDescendants().OfType<TextBlock>())
            {
                if (!block.IsEffectivelyVisible || !PseudoLocalizationTests.IsDrawn(block) || TextOf(block) is not { Length: > 0 } text) continue;
                if (PseudoLocalizationTests.Problem(block, text, window) is { } problem) problems.Add($"{lang}: \"{PseudoLocalizationTests.Shorten(text)}\" {problem}");
            }
            window.Close();
        }
        Assert.True(problems.Count == 0, $"{problems.Count} menu rows do not fit:\n" + string.Join("\n", problems));
    }

    [AvaloniaFact]
    public void The_palette_shows_every_category_and_the_longest_titles_whole_in_every_language()
    {
        var wb = _app.Workbench;
        _w.SelectTab(_w.OpenFile(_temp.File("alpha.txt", "alpha\n"), null));
        var problems = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var lang in Loc.Languages)
        {
            _app.SetLanguage(lang);
            Tick();
            var commands = wb.Commands.Commands.Where(c => !c.Hidden && wb.Commands.HasHandler(c.Id)).ToList();
            var queries = commands.Where(c => c.CategoryKey is not null).Select(c => wb.Strings.Get(c.CategoryKey!)).Distinct()
                .Concat(commands.Select(c => wb.Strings.Get(c.TitleKey)).OrderByDescending(t => t.Length).Take(5)).ToList();
            Assert.True(queries.Count > 10, $"{lang}: only {queries.Count} palette queries");
            _w.ShowPalette();
            foreach (var query in queries)
            {
                _w.SetPaletteQuery(query);
                PseudoLocalizationTests.Layout(_w);
                foreach (var block in _w.PaletteHost.GetVisualDescendants().OfType<TextBlock>())
                {
                    if (!block.IsEffectivelyVisible || TextOf(block) is not { Length: > 0 } text) continue;
                    if (PseudoLocalizationTests.Problem(block, text, _w) is { } problem) problems.Add($"{lang}: query \"{query}\": \"{PseudoLocalizationTests.Shorten(text)}\" {problem}");
                }
            }
            _w.ClosePalette();
        }
        Assert.True(problems.Count == 0, $"{problems.Count} palette labels do not fit:\n" + string.Join("\n", problems));
    }

    // ---- collisions in the menus ----

    /// <summary>The access key Avalonia reads from a menu text: the letter after a single underscore; a doubled underscore is a literal one.</summary>
    internal static char? AccessKey(string label)
    {
        for (int i = 0; i < label.Length - 1; i++)
        {
            if (label[i] != '_') continue;
            if (label[i + 1] == '_') { i++; continue; }
            return char.ToUpperInvariant(label[i + 1]);
        }
        return null;
    }

    /// <summary>
    /// What a person cannot tell apart in one menu: two rows with the same text (a trailing ellipsis does not make them different), two rows with the same
    /// access key and two different commands that show the same shortcut anywhere in the menu. A submenu is searched like the menu it belongs to.
    /// </summary>
    internal static IEnumerable<string> Collisions(string path, IReadOnlyList<MenuNode> nodes, Dictionary<string, (string? Id, string Label)>? shortcuts = null)
    {
        shortcuts ??= [];
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        var keys = new Dictionary<char, string>();
        foreach (var node in nodes)
        {
            string? label = node switch { MenuCommand c => c.Label, MenuGroup g => g.Label, _ => null };
            if (label is null) continue;
            string text = label.TrimEnd('.', '…', ' ').ToLowerInvariant();
            if (texts.TryGetValue(text, out var same)) yield return $"{path}: \"{label}\" and \"{same}\" read the same";
            else texts[text] = label;
            if (AccessKey(label) is { } key)
            {
                if (keys.TryGetValue(key, out var holder)) yield return $"{path}: \"{label}\" and \"{holder}\" share the access key {key}";
                else keys[key] = label;
            }
            if (node is MenuCommand { Gesture: { } gesture } command)
            {
                string shown = gesture.ToString();
                if (shortcuts.TryGetValue(shown, out var other) && (other.Id is null || other.Id != command.CommandId)) yield return $"{path}: \"{label}\" and \"{other.Label}\" both show {shown}";
                else shortcuts[shown] = (command.CommandId, label);
            }
            if (node is MenuGroup group)
                foreach (var inner in Collisions(path + " > " + label, group.Items, shortcuts)) yield return inner;
        }
    }

    [AvaloniaFact]
    public void No_menu_has_two_rows_with_the_same_text_access_key_or_shortcut_in_any_language()
    {
        _w.SelectTab(_w.OpenFile(_temp.File("alpha.txt", "alpha\n"), null));
        var problems = new List<string>();
        foreach (var lang in Loc.Languages)
        {
            _app.SetLanguage(lang);
            Tick();
            Assert.NotEmpty(_w.MenuModel);
            // the bar is a menu of its own (its rows are the menus, without their items); each menu is searched with its submenus
            problems.AddRange(Collisions(lang, [.. _w.MenuModel.Select(g => g with { Items = [] })]));
            foreach (var menu in _w.MenuModel) problems.AddRange(Collisions(lang + " > " + menu.Label, menu.Items));
        }
        Assert.True(problems.Count == 0, $"{problems.Count} collisions:\n" + string.Join("\n", problems.Distinct()));
    }

    [AvaloniaFact]
    public void The_collision_check_finds_a_repeated_text_access_key_and_shortcut()
    {
        var save = new KeyGesture(Key.S, KeyModifiers.Control);
        MenuNode[] menu =
        [
            new MenuCommand("_Save", () => { }, save), new MenuCommand("_Search", () => { }, new KeyGesture(Key.F, KeyModifiers.Control)),
            new MenuCommand("Open...", () => { }), new MenuCommand("Open", () => { }), new MenuCommand("Save As", () => { }, save),
            new MenuGroup("More", [new MenuCommand("Un__derscore", () => { }), new MenuCommand("Un__derscore ", () => { })]),
        ];
        var found = Collisions("test", menu).ToList();
        Assert.Contains(found, p => p.Contains("share the access key S", StringComparison.Ordinal));
        Assert.Contains(found, p => p.Contains("\"Open\" and \"Open...\" read the same", StringComparison.Ordinal));
        Assert.Contains(found, p => p.Contains("both show Ctrl+S", StringComparison.Ordinal));
        Assert.Contains(found, p => p.Contains("test > More", StringComparison.Ordinal));
        Assert.Equal(4, found.Count);
        Assert.Null(AccessKey("Plain"));
        Assert.Null(AccessKey("sql__dialect"));
        Assert.Equal('D', AccessKey("sql_dialect"));
    }

    // ---- the palette finds the translated name ----

    [AvaloniaFact]
    public void Every_command_is_found_in_the_palette_by_its_translated_name_in_every_language()
    {
        var wb = _app.Workbench;
        _w.SelectTab(_w.OpenFile(_temp.File("alpha.txt", "alpha\n"), null));
        var problems = new List<string>();
        int tried = 0;
        foreach (var lang in Loc.Languages)
        {
            _app.SetLanguage(lang);
            var context = new ShellContext(_w, forMenu: true);
            // two commands that read the same in the list cannot be told apart by the name
            foreach (var same in wb.Commands.Commands.Where(c => !c.Hidden && wb.Commands.HasHandler(c.Id))
                .GroupBy(c => (c.CategoryKey is null ? "" : wb.Strings.Get(c.CategoryKey)) + ": " + wb.Strings.Get(c.TitleKey)).Where(g => g.Count() > 1))
                problems.Add($"{lang}: {string.Join(", ", same.Select(c => c.Id))} all read \"{same.Key}\" in the palette");
            foreach (var command in wb.Commands.Commands.Where(c => !c.Hidden && wb.Commands.HasHandler(c.Id) && c.IsAvailable(context)))
            {
                string title = wb.Strings.Get(command.TitleKey);
                foreach (var typed in new[] { title, Plain(title) }.Distinct())
                {
                    tried++;
                    var found = CommandPalette.Query(wb.Commands, wb.Keymap, wb.Strings, context, typed, 400)
                        .Where(i => wb.Commands.HasHandler(i.CommandId)).Take(60).FirstOrDefault(i => i.CommandId == command.Id);
                    if (found is null) problems.Add($"{lang}: typing \"{typed}\" does not list {command.Id}");
                    else if (found.Title != title || found.Category != (command.CategoryKey is null ? null : wb.Strings.Get(command.CategoryKey)))
                        problems.Add($"{lang}: {command.Id} is listed as \"{found.Category}: {found.Title}\", not in the language");
                }
            }
        }
        Assert.True(tried > 800, $"only {tried} palette queries were made");
        Assert.True(problems.Count == 0, $"{problems.Count} commands are not found by their name:\n" + string.Join("\n", problems.Take(60)));
    }

    [AvaloniaFact]
    public void The_palette_window_lists_a_command_typed_in_its_language_with_and_without_the_marks()
    {
        var wb = _app.Workbench;
        _w.SelectTab(_w.OpenFile(_temp.File("alpha.txt", "alpha\n"), null));
        string[] ids = ["nks.file.close", "nks.search.find", "nks.view.zoomIn"];
        foreach (var lang in Loc.Languages)
        {
            _app.SetLanguage(lang);
            foreach (var id in ids)
            {
                Assert.True(wb.Commands.TryGet(id, out var command), id);
                string title = wb.Strings.Get(command.TitleKey);
                foreach (var typed in new[] { title, Plain(title), title.ToUpperInvariant() }.Distinct())
                {
                    _w.ShowPalette();
                    _w.SetPaletteQuery(typed);
                    var row = _w.PaletteResults.FirstOrDefault(r => r.CommandId == id);
                    Assert.True(row is not null, $"{lang}: typing \"{typed}\" does not list {id}");
                    Assert.NotNull(row);
                    Assert.Equal(title, row.Title);
                    Assert.Equal(wb.Strings.Get(command.CategoryKey!), row.Category);
                    _w.ClosePalette();
                }
            }
        }
    }

    // ---- a switch of the language with the panels open ----

    /// <summary>Every text a screen shows right now: labels, button texts, hints, tooltips and the names a screen reader reads.</summary>
    private static IEnumerable<(string Control, string Text)> ShownTexts(Visual root)
    {
        foreach (var visual in root.GetSelfAndVisualDescendants())
        {
            if (visual is not Control c || !c.IsEffectivelyVisible) continue;
            string where = A11yAudit.Describe(c);
            if (c is TextBlock block) yield return (where, TextOf(block));
            else if (c is ContentControl { Content: string content }) yield return (where, content);
            else if (c is MenuItem { Header: string header }) yield return (where, header);
            if (c is TextBox { PlaceholderText: { Length: > 0 } hint }) yield return (where, hint);
            if (ToolTip.GetTip(c) is string tip) yield return (where, tip);
            if (AutomationProperties.GetName(c) is { Length: > 0 } name) yield return (where, name);
        }
    }

    /// <summary>The texts of a language that need no placeholder filled in, from both catalogues.</summary>
    private static HashSet<string> Texts(string lang) =>
        [.. Loc.Catalog(lang).Values.Concat(TranslationGateTests.WorkbenchTexts(lang).Values).Where(t => !t.Contains('{') && t.Trim().Length >= 3).Select(t => t.Trim())];

    /// <summary>Switches through the languages and back and reports every text on the screen that belongs to the language just left and not to the new one.</summary>
    private void SwitchAcross(string scenario, Func<IEnumerable<Visual>> roots, List<string> problems)
    {
        string previous = Loc.Language;
        int checkedTexts = 0;
        foreach (var lang in new[] { "en", "sk", "de", "cs" })
        {
            _app.SetLanguage(lang);
            Tick();
            var stale = Texts(previous);
            stale.ExceptWith(Texts(lang));
            foreach (var root in roots())
                foreach (var (control, text) in ShownTexts(root))
                {
                    checkedTexts++;
                    if (stale.Contains(text.Trim())) problems.Add($"[{scenario}] {previous} -> {lang}: {control} still shows \"{PseudoLocalizationTests.Shorten(text)}\"");
                }
            previous = lang;
        }
        if (checkedTexts == 0) problems.Add($"[{scenario}] no text was found on the screen: the check covers nothing");
    }

    [AvaloniaFact]
    public async Task Switching_the_language_with_panels_open_leaves_no_text_of_the_old_language()
    {
        var problems = new List<string>();
        var text = _w.OpenFile(_temp.File("alpha.txt", "alpha\nbeta\n"), null);
        var csv = _w.OpenFile(_temp.File("data.csv", "id,name\r\n1,Ann\r\n2,Bob\r\n"), null);
        var bin = _w.OpenFile(_temp.Bytes("blob.bin", [.. Enumerable.Range(0, 64).Select(i => (byte)i)]), null);

        // the text tab with the panels around it
        _w.SelectTab(text);
        _w.ToggleDocuments(true);
        _w.ToggleOutput(true);
        _w.ToggleAgentPanel(true);
        _w.FindBar.Open(files: false, replace: true);
        Tick();
        SwitchAcross("text tab with panels", () => [_w], problems);
        _w.FindBar.Close();
        _w.ToggleAgentPanel(false);
        _w.ToggleOutput(false);
        _w.ToggleDocuments(false);

        // the files of a folder
        Assert.True(await _w.ShowFolderAsync(_temp.Path));
        Tick();
        SwitchAcross("files panel", () => [_w], problems);
        _w.ToggleFiles(false);

        // the table of a CSV file
        _w.SelectTab(csv);
        Assert.Equal(CommandOutcome.Handled, Run("nks.view.table"));
        Tick();
        SwitchAcross("table view", () => [_w], problems);
        Run("nks.view.table");

        // the hex view with the panels beside it
        Assert.True(_w.SetHex(bin.Doc, true));
        _w.SelectTab(bin.Doc.Views.First(v => v.Hex is not null));
        _w.ToggleInspector();
        _w.ToggleHexBookmarkPanel();
        _w.ToggleHexTemplate();
        _w.ToggleHexStatistics();
        Tick();
        SwitchAcross("hex view with panels", () => [_w], problems);

        // the windows of their own, which follow the language by themselves
        _app.OpenSettings();
        _app.OpenHelp();
        _w.ShowRegexTester();
        _w.ShowToolEditor();
        _w.ShowLogPatternTester();
        Tick();
        SwitchAcross("windows", () => [_app.SettingsView!, _app.HelpView!, .. _w.OwnedWindows], problems);

        Assert.True(problems.Count == 0, $"{problems.Count} texts were not refreshed:\n" + string.Join("\n", problems.Distinct().Take(80)));
    }

    // ---- a switch of the language changes nothing else ----

    private Dictionary<string, byte[]> Config()
    {
        string root = _app.ProfileDirectory;
        // the data folders of the plugins are written by the plugin processes, whatever the language
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => (Name: Path.GetRelativePath(root, f).Replace('\\', '/'), Path: f))
            .Where(f => ConfigBackup.Belongs(f.Name) && !f.Name.StartsWith("plugins/", StringComparison.Ordinal))
            .ToDictionary(f => f.Name, f => File.ReadAllBytes(f.Path), StringComparer.Ordinal);
    }

    private static JsonNode ParseSettings(byte[] json) => JsonNode.Parse(Encoding.UTF8.GetString(json).TrimStart('﻿'))!;

    /// <summary>The settings file without the setting that holds the language; nothing else may differ between two languages.</summary>
    private static string WithoutLanguage(byte[] json)
    {
        var root = ParseSettings(json).AsObject();
        root["settings"]!.AsObject().Remove("workbench.uiLanguage");
        return root.ToJsonString();
    }

    [AvaloniaFact]
    public async Task A_language_change_writes_only_the_language_setting_and_leaves_documents_and_commands_alone()
    {
        var wb = _app.Workbench;
        _app.SetLanguage("cs");
        Assert.Null(wb.Set("editor.fontSize", JsonValue.Create(13)));
        Assert.Null(wb.Set("tools.diffArguments", "\"{left}\" \"{right}\" –ž"));

        byte[] bom = [0xEF, 0xBB, 0xBF];
        string[] files =
        [
            _temp.Bytes("bom-crlf.txt", [.. bom, .. Encoding.UTF8.GetBytes("Příliš žluťoučký kůň\r\nÜber größere Straße\r\n")]),
            _temp.Bytes("lf.txt", Encoding.UTF8.GetBytes("jedna\ndva\ntři")),
        ];
        var tabs = files.Select(f => _w.OpenFile(f, null)).ToArray();
        // the first save is the editor's own doing; what the language must not change is every save after it
        foreach (var tab in tabs) await _w.SaveAsync(tab);
        var diskBefore = files.Select(File.ReadAllBytes).ToArray();
        var textBefore = tabs.Select(t => t.Editor!.Document.Text).ToArray();
        var configBefore = Config();
        Assert.Contains(AppWorkbench.UserSettingsFile, configBefore.Keys);
        string ids = string.Join('\n', wb.Commands.Commands.Select(c => c.Id).Order(StringComparer.Ordinal));
        string keys = string.Join('\n', wb.Keymap.Bindings.Select(b => b.CommandId + " " + b.Sequence.Display(wb.Platform)).Order(StringComparer.Ordinal));
        string menuIds = string.Join('\n', _w.MenuModel.AllCommands().Select(c => c.CommandId));

        foreach (var lang in new[] { "en", "sk", "de", "cs" })
        {
            _app.SetLanguage(lang);
            Tick();
            var now = Config();
            Assert.Equal(configBefore.Keys.Order(StringComparer.Ordinal), now.Keys.Order(StringComparer.Ordinal));
            foreach (var (name, bytes) in configBefore)
            {
                if (name == AppWorkbench.UserSettingsFile) Assert.Equal(WithoutLanguage(bytes), WithoutLanguage(now[name]));
                else Assert.True(bytes.AsSpan().SequenceEqual(now[name]), $"{lang}: {name} was rewritten");
            }
            Assert.Equal(lang, ParseSettings(now[AppWorkbench.UserSettingsFile])["settings"]!["workbench.uiLanguage"]!.GetValue<string>());
            for (int i = 0; i < tabs.Length; i++)
            {
                Assert.Equal(textBefore[i], tabs[i].Editor!.Document.Text);
                Assert.False(tabs[i].Doc.Doc.IsModified, $"{lang}: {files[i]} was marked as changed");
                Assert.Equal(diskBefore[i], File.ReadAllBytes(files[i]));
            }
            Assert.Equal(ids, string.Join('\n', wb.Commands.Commands.Select(c => c.Id).Order(StringComparer.Ordinal)));
            Assert.Equal(keys, string.Join('\n', wb.Keymap.Bindings.Select(b => b.CommandId + " " + b.Sequence.Display(wb.Platform)).Order(StringComparer.Ordinal)));
            Assert.Equal(menuIds, string.Join('\n', _w.MenuModel.AllCommands().Select(c => c.CommandId)));
        }

        // back in the first language the settings file is the file it was, byte for byte, and a save after the round trip writes the documents as they were
        Assert.Equal(configBefore[AppWorkbench.UserSettingsFile], Config()[AppWorkbench.UserSettingsFile]);
        foreach (var tab in tabs) await _w.SaveAsync(tab);
        for (int i = 0; i < files.Length; i++) Assert.Equal(diskBefore[i], File.ReadAllBytes(files[i]));
    }
}
