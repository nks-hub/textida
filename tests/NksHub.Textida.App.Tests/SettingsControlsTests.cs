using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Contributions;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.App.Tests;

/// <summary>P3.6.15 (owner request of 2. 11. 2026): every setting has its own interactive control, writes the right value and goes back to its default; shortcuts are caught from a key press; the icon set is a setting.</summary>
[Collection("LocState")]
public class SettingsControlsTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private SettingsWindow? _view;

    public SettingsControlsTests() => _app = TestApp.Prepare(_temp, _dialogs);

    public void Dispose()
    {
        _view?.Close();
        IconSkin.Use("");
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private SettingsWindow Open(string? key = null)
    {
        _app.OpenSettings(key: key);
        Dispatcher.UIThread.RunJobs();
        return _view = _app.SettingsView!;
    }

    private Control Row(string key)
    {
        var view = _view ?? Open();
        view.ShowKey(key);
        Dispatcher.UIThread.RunJobs();
        return view.RowOf(key) ?? throw new InvalidOperationException("no row for " + key);
    }

    private static T One<T>(Control row) where T : Control => row.GetVisualDescendants().OfType<T>().First();

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Button ButtonWith(Control row, string text) => row.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == text);

    private string Stored(string key) => _app.Workbench.Settings.GetString(key);

    /// <summary>Ctrl, Command on macOS: the modifier the default keymap writes as <c>mod</c>.</summary>
    private static KeyModifiers Primary => Shortcuts.Primary;

    private NksHub.Textida.Workbench.Keys.KeySequence Keys(string text) =>
        NksHub.Textida.Workbench.Keys.KeySequence.TryParse(text, _app.Workbench.Platform, out var sequence, out string? error) ? sequence : throw new ArgumentException(error);

    private string Shown(string text) => Keys(text).Display(_app.Workbench.Platform);

    private string Written(string text) => Keys(text).ToString();

    [AvaloniaFact]
    public void Every_setting_is_on_one_page_with_the_control_its_declaration_asks_for_and_no_free_text_where_none_belongs()
    {
        var view = Open();
        foreach (var d in _app.Workbench.Host.Settings.Definitions)
        {
            var row = Row(d.Key);
            Assert.Single(view.ShownIds, id => id == d.Key);
            var control = SettingFormats.ControlOf(d);
            Assert.NotEqual(SettingControl.FallbackText, control);
            var editable = row.GetVisualDescendants().OfType<TextBox>().Where(t => !t.IsReadOnly && t.IsEffectivelyVisible && t.FindAncestorOfType<NumericUpDown>() is null && t.FindAncestorOfType<ComboBox>() is null).ToList();
            switch (control)
            {
                case SettingControl.Toggle: Assert.Single(row.GetVisualDescendants().OfType<ToggleSwitch>()); break;
                case SettingControl.Number: Assert.Single(row.GetVisualDescendants().OfType<NumericUpDown>()); break;
                case SettingControl.Select or SettingControl.Encoding or SettingControl.Font or SettingControl.IconSet: Assert.NotEmpty(row.GetVisualDescendants().OfType<ComboBox>()); break;
                case SettingControl.File or SettingControl.Folder: Assert.Contains(row.GetVisualDescendants().OfType<Button>(), b => b.Content as string is { } s && (s == Loc.Get(K.SettingsBrowse) || s == Loc.Get(K.SettingsBrowseFolder))); break;
                case SettingControl.List or SettingControl.FontList: Assert.Contains(row.GetVisualDescendants().OfType<Button>(), b => b.Content as string is { } s && (s == Loc.Get(K.SettingsListAdd) || s == Loc.Get(K.SettingsFontAdd))); break;
            }
            // a box to type into only for real text, a command with arguments and the new item of a list
            if (control is not (SettingControl.Text or SettingControl.Command or SettingControl.List)) Assert.True(editable.Count == 0, d.Key + " has a free text box");
            // every row says what the default is
            Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), t => (t.Text ?? "").StartsWith(Loc.Get(K.SettingsDefaultValue, ("value", "")), StringComparison.Ordinal));
        }
    }

    [AvaloniaFact]
    public void A_toggle_a_choice_and_a_number_write_their_value_and_the_reset_brings_the_default_back()
    {
        Open();
        One<ToggleSwitch>(Row("editor.minimap")).IsChecked = false;
        Assert.False(_app.Workbench.Settings.GetBool("editor.minimap"));
        Assert.Contains("\"editor.minimap\": false", File.ReadAllText(_app.Workbench.UserSettingsPath));
        Click(ButtonWith(Row("editor.minimap"), Loc.Get(K.SettingsReset)));
        Assert.True(_app.Workbench.Settings.GetBool("editor.minimap"));
        Assert.True(One<ToggleSwitch>(Row("editor.minimap")).IsChecked);

        One<ComboBox>(Row("files.eol")).SelectedItem = "crlf";
        Assert.Equal("crlf", Stored("files.eol"));
        var row = Row("files.eol");
        Assert.True(row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("settingchanged")).IsVisible);
        Click(ButtonWith(row, Loc.Get(K.SettingsReset)));
        Assert.Equal("auto", Stored("files.eol"));

        // a number box has the step of the declaration; typing does not write until Enter or leaving the box
        var number = One<NumericUpDown>(Row("history.maxTotalMiB"));
        Assert.Equal(256m, number.Increment);
        Assert.True(number.IsKeyboardFocusWithin); // the link to the setting put the keyboard into its box
        number.Value = 4;
        number.Value = 4096;
        Assert.Equal(2048, _app.Workbench.Settings.GetInt("history.maxTotalMiB"));
        number.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(4096, _app.Workbench.Settings.GetInt("history.maxTotalMiB"));
    }

    [AvaloniaFact]
    public void A_list_is_edited_as_tags_with_add_and_remove_and_keeps_its_separator()
    {
        Open();
        var row = Row("files.secretFilePatterns");
        var chips = row.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("settingchip")).Select(b => b.Tag as string).ToList();
        Assert.Equal([".env", ".env.*", "*.env"], chips);
        var box = row.GetVisualDescendants().OfType<TextBox>().Single(t => !t.IsReadOnly);
        box.Text = "*.pem";
        Click(ButtonWith(row, Loc.Get(K.SettingsListAdd)));
        Assert.Equal(".env;.env.*;*.env;*.pem", Stored("files.secretFilePatterns"));
        var remove = row.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == Loc.Get(K.SettingsListRemoveA11y, ("item", ".env.*")));
        Click(remove);
        Assert.Equal(".env;*.env;*.pem", Stored("files.secretFilePatterns"));

        // the structure templates are separated by commas
        row = Row("hex.structureTemplates");
        box = row.GetVisualDescendants().OfType<TextBox>().Single(t => !t.IsReadOnly);
        box.Text = "bin=elf_file";
        Click(ButtonWith(row, Loc.Get(K.SettingsListAdd)));
        box.Text = "dat=png_chunks";
        Click(ButtonWith(row, Loc.Get(K.SettingsListAdd)));
        Assert.Equal("bin=elf_file, dat=png_chunks", Stored("hex.structureTemplates"));
    }

    [AvaloniaFact]
    public void The_font_is_chosen_from_the_installed_fonts_each_drawn_in_itself_and_the_fallbacks_stay()
    {
        var all = FontCatalog.Families(false);
        Assert.NotEmpty(all);
        var fixedWidth = FontCatalog.Families(true);
        Assert.NotEmpty(fixedWidth);
        Assert.True(fixedWidth.Count < all.Count);
        Assert.All(fixedWidth, f => Assert.Contains(f, all));

        Open();
        var row = Row("editor.fontFamily");
        var combo = One<ComboBox>(row);
        var items = combo.ItemsSource!.Cast<string>().ToList();
        Assert.Contains(fixedWidth[0], items);
        Assert.DoesNotContain(items, i => all.Contains(i) && !fixedWidth.Contains(i)); // the filter is on
        string chosen = fixedWidth.First(f => !SettingsWindow.FontNames(Stored("editor.fontFamily")).Contains(f, StringComparer.OrdinalIgnoreCase));
        combo.SelectedItem = chosen;
        Assert.StartsWith(chosen + ", Cascadia Mono, Consolas", Stored("editor.fontFamily"));
        Assert.Contains(chosen, _view!.Preview!.FontText);

        // without the filter every installed font is offered
        row = Row("editor.fontFamily");
        row.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = false;
        Assert.Equal(all.Count, One<ComboBox>(row).ItemsSource!.Cast<string>().Count(all.Contains));
    }

    [AvaloniaFact]
    public void The_preview_follows_the_size_and_the_line_numbers()
    {
        var view = Open();
        view.SelectCategory(SettingCategories.Appearance);
        Assert.NotNull(view.Preview);
        One<NumericUpDown>(Row("editor.fontSize")).Value = 20;
        Assert.Equal(20, view.Preview!.FontSizeValue);
        view.SelectCategory(SettingCategories.EditorDisplay);
        One<ToggleSwitch>(Row("editor.lineNumbers")).IsChecked = false;
        Assert.False(view.Preview!.ShowsLineNumbers);
    }

    [AvaloniaFact]
    public void A_folder_and_a_file_are_chosen_in_the_system_dialog_and_cleared_with_a_button()
    {
        Open();
        string folder = Directory.CreateDirectory(Path.Combine(_temp.Path, "journal")).FullName;
        _dialogs.Folders.Enqueue(folder);
        var row = Row("files.journalDirectory");
        Assert.True(One<TextBox>(row).IsReadOnly);
        Click(ButtonWith(row, Loc.Get(K.SettingsBrowseFolder)));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(folder, Stored("files.journalDirectory"));
        Click(ButtonWith(Row("files.journalDirectory"), Loc.Get(K.SettingsClear)));
        Assert.Equal("", Stored("files.journalDirectory"));

        string tidy = _temp.File("tidy.exe", "");
        _dialogs.Picked.Add(tidy);
        Click(ButtonWith(Row("html.tidyPath"), Loc.Get(K.SettingsBrowse)));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(tidy, Stored("html.tidyPath"));
    }

    [AvaloniaFact]
    public void The_encoding_is_chosen_from_the_list_of_the_editor()
    {
        Open();
        var combo = One<ComboBox>(Row("tools.outputEncoding"));
        var values = combo.ItemsSource!.Cast<string>().ToList();
        Assert.Equal("", values[0]);
        Assert.Contains("windows-1250", values);
        Assert.Contains("ibm852", values);
        combo.SelectedItem = "ibm852";
        Assert.Equal("ibm852", Stored("tools.outputEncoding"));
    }

    [AvaloniaFact]
    public void A_shortcut_is_caught_from_a_key_press_replaces_the_default_and_goes_back()
    {
        var view = Open();
        var wb = _app.Workbench;
        var before = wb.Keymap.ShortcutsFor("nks.view.zoomIn");
        Assert.NotEmpty(before);
        var row = Row("nks.view.zoomIn");
        var capture = row.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("shortcutcapture"));
        Click(capture);
        Assert.Equal(Loc.Get(K.SettingsShortcutPress), capture.Content);
        // a modifier alone waits for the key
        capture.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.LeftCtrl, KeyModifiers = KeyModifiers.Control });
        Assert.Equal(Loc.Get(K.SettingsShortcutPress), capture.Content);
        capture.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.K, KeyModifiers = Primary | KeyModifiers.Alt | KeyModifiers.Shift });
        // the capture waits for a second stroke before it takes a single one
        Assert.Equal(Loc.Get(K.SettingsShortcutPending, ("keys", Shown("mod+alt+shift+k"))), capture.Content);
        string caught = Written("mod+alt+shift+k");
        PumpUntil(() => wb.Keymap.Bindings.Any(b => b.CommandId == "nks.view.zoomIn" && b.Sequence.ToString() == caught));

        Assert.Equal([caught], wb.Keymap.Bindings.Where(b => b.CommandId == "nks.view.zoomIn").Select(b => b.Sequence.ToString()));
        var file = JsonNode.Parse(File.ReadAllText(Path.Combine(_app.ProfileDirectory, AppWorkbench.UserKeymapFile)))!["bindings"]!.AsArray()
            .Select(b => (Key: b!["key"]!.GetValue<string>(), Command: b["command"]!.GetValue<string>())).ToList();
        Assert.Contains((caught, "nks.view.zoomIn"), file);
        Assert.Contains(file, b => b.Command == "-nks.view.zoomIn");
        Assert.Contains("found", view.RowOf("nks.view.zoomIn")!.Classes);

        // Escape cancels a catch without a change
        row = view.RowOf("nks.view.zoomIn")!;
        capture = row.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("shortcutcapture"));
        Click(capture);
        capture.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.Equal(Loc.Get(K.SettingsShortcutChange), capture.Content);

        Click(ButtonWith(row, Loc.Get(K.SettingsShortcutReset)));
        Assert.Equal(before, wb.Keymap.ShortcutsFor("nks.view.zoomIn"));
        Assert.DoesNotContain("zoomIn", File.ReadAllText(Path.Combine(_app.ProfileDirectory, AppWorkbench.UserKeymapFile)));
    }

    private static void Press(Button capture, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        capture.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });

    private static void PumpUntil(Func<bool> done)
    {
        for (int i = 0; i < 1000 && !done(); i++)
        {
            Thread.Sleep(5);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(done());
    }

    [AvaloniaFact]
    public void A_two_step_shortcut_is_caught_shown_as_the_keymap_formats_it_and_its_prefix_conflict_is_shown()
    {
        var view = Open();
        var wb = _app.Workbench;
        var capture = Row("nks.view.zoomIn").GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("shortcutcapture"));
        Click(capture);
        Press(capture, Key.K, Primary);
        Assert.Equal(Loc.Get(K.SettingsShortcutPending, ("keys", Shown("mod+k"))), capture.Content);
        // Backspace clears what was pressed and keeps catching
        Press(capture, Key.Back);
        Assert.Equal(Loc.Get(K.SettingsShortcutPress), capture.Content);
        Press(capture, Key.S, Primary);
        Press(capture, Key.U, Primary);
        string shown = Shown("mod+s mod+u");
        Assert.Equal(Loc.Get(K.SettingsShortcutPending, ("keys", shown)), capture.Content);
        Assert.DoesNotContain(wb.Keymap.Bindings, b => b.CommandId == "nks.view.zoomIn" && b.Sequence.Chords.Count == 2);
        PumpUntil(() => wb.Keymap.Bindings.Any(b => b.CommandId == "nks.view.zoomIn" && b.Sequence.ToString() == Written("mod+s mod+u")));

        Assert.Equal([shown], wb.Keymap.ShortcutsFor("nks.view.zoomIn"));
        var row = view.RowOf("nks.view.zoomIn")!;
        var texts = row.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(texts, t => t is not null && t.Contains(shown, StringComparison.Ordinal));
        // Ctrl+S (Command+S on macOS) alone saves, so the chord after it never comes
        Assert.Contains(wb.Keymap.Conflicts, c => c.Kind == NksHub.Textida.Workbench.Keys.KeyConflictKind.Prefix && c.Winner.CommandId == "nks.file.save" && c.Loser.CommandId == "nks.view.zoomIn");
        Assert.Contains(Loc.Get(K.SettingsRowConflict, ("other", "nks.file.save")), texts);

        // Escape in the middle of a chord leaves the shortcut as it is
        capture = row.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("shortcutcapture"));
        Click(capture);
        Press(capture, Key.K, Primary);
        Press(capture, Key.Escape);
        Assert.Equal(Loc.Get(K.SettingsShortcutChange), capture.Content);
        Thread.Sleep(PluginLimits.ShortcutChordWaitMilliseconds + 100);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([shown], wb.Keymap.ShortcutsFor("nks.view.zoomIn"));
    }

    private static void Png(string path)
    {
        var bitmap = new WriteableBitmap(new PixelSize(16, 16), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = bitmap.Lock()) for (int i = 0; i < 16 * 16; i++) System.Runtime.InteropServices.Marshal.WriteInt32(fb.Address, i * 4, unchecked((int)0xFF2080D0));
        bitmap.Save(path);
    }

    [AvaloniaFact]
    public void The_icon_set_is_one_setting_under_Appearance_and_the_two_menu_items_are_gone()
    {
        var menus = _app.Workbench.Commands.CommandsInMenus();
        Assert.DoesNotContain("nks.view.iconSkin", menus);
        Assert.DoesNotContain("nks.view.iconSkinOff", menus);
        Assert.Equal(SettingCategories.Appearance, _app.Workbench.Host.Settings.TryGet("appearance.iconSkin", out var d) ? d.Category : null);

        var view = Open("appearance.iconSkin");
        Assert.Equal(SettingCategories.Appearance, view.SelectedCategory);
        var row = view.RowOf("appearance.iconSkin")!;
        var combo = One<ComboBox>(row);
        Assert.Equal("builtin", combo.SelectedItem);
        Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get(K.SettingsIconSetHelp));
        Assert.True(row.GetVisualDescendants().OfType<IconView>().Count() >= 8); // the preview

        // a folder without pictures is refused and the built-in icons stay
        _dialogs.Folders.Enqueue(Directory.CreateDirectory(Path.Combine(_temp.Path, "empty")).FullName);
        combo.SelectedItem = "folder";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("", Stored("appearance.iconSkin"));
        Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), t => t.IsVisible && (t.Text ?? "").Contains("empty", StringComparison.Ordinal));
        Assert.Equal("builtin", combo.SelectedItem);

        string folder = Directory.CreateDirectory(Path.Combine(_temp.Path, "skin")).FullName;
        Png(Path.Combine(folder, "save.png"));
        _dialogs.Folders.Enqueue(folder);
        combo.SelectedItem = "folder";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(folder, Stored("appearance.iconSkin"));
        Assert.Equal(folder, IconSkin.Current?.Folder);
        row = view.RowOf("appearance.iconSkin")!;
        Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get(K.SettingsIconSetLoaded, ("icons", 1), ("problems", 0)));
        Assert.Contains(row.GetVisualDescendants().OfType<IconView>(), i => i.SkinBitmap is not null);

        One<ComboBox>(row).SelectedItem = "builtin";
        Assert.Equal("", Stored("appearance.iconSkin"));
        Assert.Null(IconSkin.Current);
    }

    [AvaloniaFact]
    public void A_plugin_setting_gets_a_colour_picker_a_multiselect_and_a_caught_shortcut_from_its_format()
    {
        var manifest = new ContributionManifest
        {
            Id = "acme.kit",
            Settings =
            [
                new SettingDefinition { Key = "acme.kit.accent", Default = JsonValue.Create("#336699"), Title = "acme.kit.accent", Scope = "application", Format = "color" },
                new SettingDefinition { Key = "acme.kit.langs", Default = JsonValue.Create("cs"), Title = "acme.kit.langs", Scope = "application", Format = "multi", Values = ["cs", "en", "de"] },
                new SettingDefinition { Key = "acme.kit.key", Default = JsonValue.Create(""), Title = "acme.kit.key", Scope = "application", Format = "shortcut" },
            ],
        };
        Assert.Empty(_app.Workbench.AddPlugin(manifest, "Acme Kit"));
        var view = Open();
        view.SelectCategory(SettingsWindow.PluginNodePrefix + "Acme Kit");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(SettingControl.Color, view.ControlOf("acme.kit.accent"));

        var color = view.RowOf("acme.kit.accent")!.GetVisualDescendants().OfType<Button>().First(b => AutomationProperties.GetName(b) == Loc.Get(K.SettingsColorA11y, ("title", "acme.kit.accent")));
        Click(color);
        Dispatcher.UIThread.RunJobs();
        var flyout = (Flyout)FlyoutBase.GetAttachedFlyout(color)!;
        Assert.True(flyout.IsOpen);
        var panel = (ColorPickerPanel)flyout.Content!;
        panel.ValueBox.Text = "#ff0000";
        Dispatcher.UIThread.RunJobs(); // the text box raises its change a moment later
        Assert.Equal(new NksHub.Textida.Core.Colors.Rgba(255, 0, 0), panel.Color);
        Assert.True(flyout.IsOpen, "closed before Hide");
        flyout.Hide();
        Dispatcher.UIThread.RunJobs();
        Assert.False(flyout.IsOpen);
        Assert.True(Stored("acme.kit.accent") == "#ff0000", string.Join(" | ", view.RowOf("acme.kit.accent")!.GetVisualDescendants().OfType<TextBlock>().Select(b => (b.IsVisible ? "+" : "-") + b.Text)));

        var boxes = view.RowOf("acme.kit.langs")!.GetVisualDescendants().OfType<CheckBox>().ToList();
        Assert.Equal(3, boxes.Count);
        Assert.True(boxes[0].IsChecked);
        boxes[2].IsChecked = true;
        Assert.Equal("cs;de", Stored("acme.kit.langs"));
        boxes[0].IsChecked = false;
        Assert.Equal("de", Stored("acme.kit.langs"));

        var capture = view.RowOf("acme.kit.key")!.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("shortcutcapture"));
        Click(capture);
        Press(capture, Key.F7, KeyModifiers.Shift);
        PumpUntil(() => Stored("acme.kit.key") == "shift+f7");

        // a chord whose first stroke is the shortcut of a command is written and warned about
        capture = view.RowOf("acme.kit.key")!.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("shortcutcapture"));
        Click(capture);
        Press(capture, Key.S, Primary);
        Press(capture, Key.F7, Primary);
        PumpUntil(() => Stored("acme.kit.key") == Written("mod+s mod+f7"));
        Assert.True(_app.Workbench.Commands.TryGet("nks.file.save", out var save));
        var visible = view.RowOf("acme.kit.key")!.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text).ToList();
        Assert.Contains(Shown("mod+s mod+f7"), visible);
        Assert.Contains(Loc.Get(K.SettingsRowConflict, ("other", _app.Workbench.Strings.Get(save.TitleKey))), visible);
    }
}
