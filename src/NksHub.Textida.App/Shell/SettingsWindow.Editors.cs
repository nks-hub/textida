using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Colors;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Workbench.Contributions;
using NksHub.Textida.Workbench.Icons;
using NksHub.Textida.Workbench.Keys;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The control of each setting, chosen from its declaration (<see cref="SettingFormats.ControlOf"/>): a switch, a list of choices, a number box with its range and step, a font list with each font drawn
/// in itself, a colour picker, a file or folder chosen in the system dialog, a list of items with add and remove, a shortcut caught from a key press, the icon set. A text box only for real text.
/// </summary>
internal sealed partial class SettingsWindow
{
    /// <summary>The control a setting got, by key; for tests.</summary>
    internal SettingControl? ControlOf(string key) => _app.Workbench.Host.Settings.TryGet(key, out var d) ? SettingFormats.ControlOf(d) : null;

    private JsonNode CurrentValue(string key) => _app.Workbench.Settings.Resolve(key)!.Value;

    private string CurrentText(string key) => CurrentValue(key) is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    /// <summary>Commits a string unless it is the value already in force.</summary>
    private void CommitText(string key, string value, TextBlock error, bool rebuild = false)
    {
        if (value == CurrentText(key)) return;
        Commit(key, JsonValue.Create(value), error, rebuild);
    }

    private Control MakeEditor(SettingDefinition definition, ResolvedSetting resolved, TextBlock error, string title, out bool wide)
    {
        string id = definition.Key;
        var control = SettingFormats.ControlOf(definition);
        wide = control is SettingControl.MultiSelect or SettingControl.FontList or SettingControl.File or SettingControl.Folder or SettingControl.Command or SettingControl.List
            or SettingControl.IconSet or SettingControl.Text or SettingControl.FallbackText or SettingControl.Font;
        Control editor = control switch
        {
            SettingControl.Toggle => ToggleEditor(id, resolved, error, title),
            SettingControl.Number => NumberEditor(definition, resolved, error, title),
            SettingControl.Select => SelectEditor(definition, resolved, error, title),
            SettingControl.MultiSelect => MultiEditor(definition, error, title),
            SettingControl.Encoding => EncodingEditor(id, error, title),
            SettingControl.Font => FontEditor(id, error, title),
            SettingControl.FontList => ListEditor(definition, error, title, fonts: true),
            SettingControl.List => ListEditor(definition, error, title, fonts: false),
            SettingControl.Color => ColorEditor(id, error, title),
            SettingControl.File or SettingControl.Folder or SettingControl.Command => PathEditor(id, control, error, title),
            SettingControl.Shortcut => ShortcutEditor(id, error, title),
            SettingControl.IconSet => IconSetEditor(id, error, title),
            _ => TextEditor(id, error, title),
        };
        return editor;
    }

    // ---- switch, number, choice ----

    private ToggleSwitch ToggleEditor(string id, ResolvedSetting resolved, TextBlock error, string title)
    {
        var toggle = new ToggleSwitch { IsChecked = resolved.Value.GetValue<bool>(), OnContent = Loc.Get(K.SettingsOn), OffContent = Loc.Get(K.SettingsOff), VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(toggle, title);
        toggle.IsCheckedChanged += (_, _) =>
        {
            bool on = toggle.IsChecked == true;
            if (CurrentValue(id) is JsonValue v && v.TryGetValue<bool>(out var now) && now == on) return;
            Commit(id, JsonValue.Create(on), error);
        };
        return toggle;
    }

    /// <summary>
    /// A number box with the range and the step of the setting. The value is written when the user steps it, presses Enter or leaves the box, not on every typed digit:
    /// typing 4096 into a limit of the history must not prune it to 4 MiB on the way.
    /// </summary>
    private NumericUpDown NumberEditor(SettingDefinition d, ResolvedSetting resolved, TextBlock error, string title)
    {
        bool integer = d.Type == "integer";
        var box = new NumericUpDown
        {
            Minimum = d.Minimum is { } min ? (decimal)min : decimal.MinValue,
            Maximum = d.Maximum is { } max ? (decimal)max : decimal.MaxValue,
            Increment = (decimal)(d.Step ?? 1),
            FormatString = integer ? "0" : "0.###",
            ParsingNumberStyle = integer ? NumberStyles.Integer : NumberStyles.Float,
            ClipValueToMinMax = true,
            Value = (decimal)(SettingRules.ToNumber(resolved.Value) ?? 0),
            MinWidth = 150,
            VerticalAlignment = VerticalAlignment.Top,
        };
        AutomationProperties.SetName(box, title);
        void Apply()
        {
            if (box.Value is not { } typed) return;
            var value = Math.Clamp(typed, box.Minimum, box.Maximum);
            if (value != typed) box.Value = value;
            double number = (double)value;
            if (SettingRules.ToNumber(CurrentValue(d.Key)) == number) return;
            Commit(d.Key, integer ? JsonValue.Create((long)value) : JsonValue.Create(number), error);
        }
        box.ValueChanged += (_, _) => { if (!box.IsKeyboardFocusWithin) Apply(); };
        box.Spinned += (_, _) => Dispatcher.UIThread.Post(Apply);
        box.LostFocus += (_, _) => Apply();
        box.AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Enter) Dispatcher.UIThread.Post(Apply); }, RoutingStrategies.Bubble, handledEventsToo: true);
        return box;
    }

    private ComboBox Choice(IReadOnlyList<string> values, string selected, Func<string, string> label, string title, Action<string> chosen)
    {
        var combo = new ComboBox { ItemsSource = values, SelectedItem = values.Contains(selected) ? selected : null, MinWidth = 200, VerticalAlignment = VerticalAlignment.Top };
        combo.ItemTemplate = new FuncDataTemplate<string>((value, _) => new TextBlock { Text = label(value ?? "") }, supportsRecycling: true);
        AutomationProperties.SetName(combo, title);
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string s) chosen(s); };
        return combo;
    }

    private ComboBox SelectEditor(SettingDefinition d, ResolvedSetting resolved, TextBlock error, string title) =>
        Choice(d.Values!, resolved.Value.GetValue<string>(), v => ValueLabel(d.Key, v), title, s => CommitText(d.Key, s, error));

    private WrapPanel MultiEditor(SettingDefinition d, TextBlock error, string title)
    {
        var chosen = SettingFormats.Split(d, CurrentText(d.Key)).ToHashSet(StringComparer.Ordinal);
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        AutomationProperties.SetName(panel, title);
        foreach (var value in d.Values!)
        {
            var box = new CheckBox { Content = ValueLabel(d.Key, value), IsChecked = chosen.Contains(value), Margin = new Thickness(0, 0, 16, 0) };
            box.IsCheckedChanged += (_, _) =>
            {
                if (box.IsChecked == true) chosen.Add(value); else chosen.Remove(value);
                CommitText(d.Key, SettingFormats.Join(d, d.Values!.Where(chosen.Contains)), error);
            };
            panel.Children.Add(box);
        }
        return panel;
    }

    // ---- encodings ----

    /// <summary>The encodings the editor knows (<see cref="TextCodec.All"/>) that a tool's output can be read in, by their .NET name, with the empty value (the default) first.</summary>
    internal static IReadOnlyList<(string Value, string Label)> Encodings()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var list = new List<(string, string)> { ("", Loc.Get(K.SettingsEncodingDefault)) };
        foreach (var codec in TextCodec.All.Where(c => c.CodePage > 0))
        {
            string name;
            try { name = Encoding.GetEncoding(codec.CodePage).WebName; }
            catch (Exception e) when (e is ArgumentException or NotSupportedException) { continue; }
            if (list.All(item => !string.Equals(item.Item1, name, StringComparison.OrdinalIgnoreCase))) list.Add((name, $"{codec.Name} ({name})"));
        }
        return list;
    }

    private ComboBox EncodingEditor(string id, TextBlock error, string title)
    {
        var list = Encodings().ToList();
        string current = CurrentText(id);
        string selected = list.FirstOrDefault(e => string.Equals(e.Value, current, StringComparison.OrdinalIgnoreCase)).Value ?? current;
        if (!list.Any(e => e.Value == selected)) list.Insert(1, (current, current)); // a name typed into the file by hand stays visible
        var labels = list.ToDictionary(e => e.Value, e => e.Label, StringComparer.Ordinal);
        return Choice([.. list.Select(e => e.Value)], selected, v => labels.GetValueOrDefault(v, v), title, s => CommitText(id, s, error));
    }

    // ---- fonts ----

    internal static List<string> FontNames(string value) => [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static TextBlock FontSample(string name) => new() { Text = name, FontFamily = FontCatalog.SafeFamily(name), FontSize = 14 };

    /// <summary>The main font of the editor: a list of the installed fonts, each drawn in itself, narrowed to fixed-width fonts unless the user asks for all. The rest of the stored list stays as the fallback.</summary>
    private StackPanel FontEditor(string id, TextBlock error, string title)
    {
        var families = FontNames(CurrentText(id));
        string first = families.FirstOrDefault() ?? "";
        var combo = new ComboBox { MinWidth = 300, MaxDropDownHeight = 380, HorizontalAlignment = HorizontalAlignment.Left };
        combo.ItemTemplate = new FuncDataTemplate<string>((name, _) => name is null ? new TextBlock() :
            FontCatalog.IsInstalled(name) ? FontSample(name) : new TextBlock { Text = Loc.Get(K.SettingsFontNotInstalled, ("font", name)) }, supportsRecycling: false);
        AutomationProperties.SetName(combo, title);
        var monoOnly = new CheckBox { Content = Loc.Get(K.SettingsFontMonoOnly), IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        bool filling = false;
        void Fill()
        {
            filling = true;
            var names = FontCatalog.Families(monoOnly.IsChecked == true).ToList();
            if (first.Length > 0 && !names.Contains(first, StringComparer.OrdinalIgnoreCase)) names.Insert(0, first);
            combo.ItemsSource = names;
            combo.SelectedItem = names.FirstOrDefault(n => string.Equals(n, first, StringComparison.OrdinalIgnoreCase));
            filling = false;
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (filling || combo.SelectedItem is not string chosen || string.Equals(chosen, first, StringComparison.OrdinalIgnoreCase)) return;
            first = chosen;
            families = [chosen, .. families.Where(f => !string.Equals(f, chosen, StringComparison.OrdinalIgnoreCase))];
            CommitText(id, string.Join(", ", families), error);
        };
        monoOnly.IsCheckedChanged += (_, _) => Fill();
        Fill();
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { combo, monoOnly } };
    }

    // ---- lists ----

    /// <summary>
    /// A list of items as tags with a remove button each, and a way to add one: a box for a pattern, a path or a rule, or a list of installed fonts for a list of fonts.
    /// The value is the items joined by the separator of the setting.
    /// </summary>
    private StackPanel ListEditor(SettingDefinition d, TextBlock error, string title, bool fonts)
    {
        var items = SettingFormats.Split(d, CurrentText(d.Key));
        var tags = new WrapPanel { Orientation = Orientation.Horizontal };
        AutomationProperties.SetName(tags, title);
        var panel = new StackPanel { Spacing = 6 };
        void Save() => CommitText(d.Key, SettingFormats.Join(d, items), error);
        void Show()
        {
            tags.Children.Clear();
            if (items.Count == 0) tags.Children.Add(new TextBlock { Text = Loc.Get(K.SettingsListEmpty), Opacity = 0.65, Margin = new Thickness(0, 2) });
            foreach (var item in items.ToList())
            {
                var label = fonts ? FontSample(item) : new TextBlock { Text = item, FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace") };
                label.VerticalAlignment = VerticalAlignment.Center;
                label.TextWrapping = TextWrapping.Wrap;
                var remove = new Button { Content = "×", Padding = new Thickness(6, 0), MinHeight = 20, VerticalAlignment = VerticalAlignment.Center };
                remove.Classes.Add("settingchipremove");
                AutomationProperties.SetName(remove, Loc.Get(K.SettingsListRemoveA11y, ("item", item)));
                remove.Click += (_, _) =>
                {
                    items.Remove(item);
                    Save();
                    Show();
                };
                var chip = new Border { Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { label, remove } }, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 2, 4, 2), Margin = new Thickness(0, 0, 6, 6), Tag = item };
                chip.Classes.Add("settingchip");
                tags.Children.Add(chip);
            }
        }
        void Add(string? text)
        {
            var added = text is null ? [] : SettingFormats.Split(d, text).Where(t => !items.Contains(t, StringComparer.Ordinal)).ToList();
            if (added.Count == 0) return;
            items.AddRange(added);
            Save();
            Show();
        }
        Control input;
        Button add = new() { Content = Loc.Get(fonts ? K.SettingsFontAdd : K.SettingsListAdd), Padding = new Thickness(10, 3), VerticalAlignment = VerticalAlignment.Center };
        if (fonts)
        {
            var combo = new ComboBox { MinWidth = 260, MaxDropDownHeight = 380, ItemsSource = FontCatalog.Families(false) };
            combo.ItemTemplate = new FuncDataTemplate<string>((name, _) => name is null ? new TextBlock() : FontSample(name), supportsRecycling: false);
            AutomationProperties.SetName(combo, Loc.Get(K.SettingsListNewA11y, ("title", title)));
            add.Click += (_, _) => Add(combo.SelectedItem as string);
            input = combo;
        }
        else
        {
            var box = new TextBox { MinWidth = 260, PlaceholderText = Loc.Get(K.SettingsListPlaceholder) };
            AutomationProperties.SetName(box, Loc.Get(K.SettingsListNewA11y, ("title", title)));
            void AddTyped()
            {
                Add(box.Text);
                box.Text = "";
            }
            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                AddTyped();
                e.Handled = true;
            };
            add.Click += (_, _) => AddTyped();
            input = box;
        }
        Show();
        panel.Children.Add(tags);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { input, add } });
        return panel;
    }

    // ---- colour ----

    private Button ColorEditor(string id, TextBlock error, string title)
    {
        string current = CurrentText(id);
        if (!ColorLiterals.TryParse(current.Trim(), out var match)) ColorLiterals.TryParse("#000000", out match);
        var swatch = new Border { Width = 28, Height = 18, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(110, 128, 128, 128)), Background = Brush(match.Color) };
        var label = new TextBlock { Text = current.Length > 0 ? current : Loc.Get(K.SettingsEmptyValue), VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace") };
        var button = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { swatch, label } }, Padding = new Thickness(8, 3), VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(button, Loc.Get(K.SettingsColorA11y, ("title", title)));
        button.Click += (_, _) =>
        {
            var panel = new ColorPickerPanel(match);
            var flyout = new Flyout { Content = panel, Placement = PlacementMode.BottomEdgeAlignedLeft };
            bool cancelled = false;
            panel.CancelRequested += () =>
            {
                cancelled = true;
                flyout.Hide();
            };
            flyout.Closed += (_, _) =>
            {
                if (cancelled) return;
                string value = ColorLiterals.FormatLike(match, panel.Color);
                match = match with { Color = panel.Color };
                swatch.Background = Brush(panel.Color);
                label.Text = value;
                CommitText(id, value, error);
            };
            FlyoutBase.SetAttachedFlyout(button, flyout);
            FlyoutBase.ShowAttachedFlyout(button);
        };
        return button;
    }

    private static SolidColorBrush Brush(Rgba c) => new(Color.FromArgb((byte)Math.Round(c.A * 255), c.R, c.G, c.B));

    // ---- files, folders and commands ----

    /// <summary>
    /// A path chosen in the system dialog. The path of a file or a folder is shown, not typed (the dialog and the clear button change it); a command may carry arguments,
    /// so its box stays editable and the dialog puts the chosen program in, quoted when it has a space.
    /// </summary>
    private StackPanel PathEditor(string id, SettingControl kind, TextBlock error, string title)
    {
        bool command = kind == SettingControl.Command;
        var box = new TextBox { Text = CurrentText(id), IsReadOnly = !command, MinWidth = 320, PlaceholderText = Loc.Get(K.SettingsEmptyValue) };
        box.FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace");
        AutomationProperties.SetName(box, title);
        var browse = new Button { Content = Loc.Get(kind == SettingControl.Folder ? K.SettingsBrowseFolder : K.SettingsBrowse), Padding = new Thickness(10, 3), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(browse, Loc.Get(K.SettingsBrowseA11y, ("title", title)));
        var clear = new Button { Content = Loc.Get(K.SettingsClear), Padding = new Thickness(10, 3), VerticalAlignment = VerticalAlignment.Center, IsVisible = !command && box.Text!.Length > 0 };
        AutomationProperties.SetName(clear, Loc.Get(K.SettingsClearA11y, ("title", title)));
        void Set(string value)
        {
            box.Text = value;
            clear.IsVisible = !command && value.Length > 0;
            CommitText(id, value, error);
        }
        browse.Click += async (_, _) =>
        {
            string? picked = kind == SettingControl.Folder ? await _app.Dialogs.PickFolderAsync(this) : await _app.Dialogs.PickFileAsync(this, title, title, ["*"]);
            if (picked is null) return;
            Set(command && picked.Contains(' ') ? $"\"{picked}\"" : picked);
        };
        clear.Click += (_, _) => Set("");
        if (command)
        {
            box.LostFocus += (_, _) => CommitText(id, box.Text ?? "", error);
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitText(id, box.Text ?? "", error); e.Handled = true; } };
        }
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        Grid.SetColumn(browse, 1);
        Grid.SetColumn(clear, 2);
        browse.Margin = clear.Margin = new Thickness(8, 0, 0, 0);
        grid.Children.Add(box);
        grid.Children.Add(browse);
        grid.Children.Add(clear);
        return new StackPanel { Children = { grid } };
    }

    // ---- text ----

    private StackPanel TextEditor(string id, TextBlock error, string title)
    {
        var box = new TextBox { Text = CurrentText(id), MinWidth = 320 };
        AutomationProperties.SetName(box, title);
        var panel = new StackPanel { Spacing = 4, Children = { box } };
        void Apply() => CommitText(id, box.Text ?? "", error);
        box.LostFocus += (_, _) => Apply();
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Apply();
            e.Handled = true;
        };
        if (id == "editor.dateTimeFormat")
        {
            var sample = new TextBlock { Opacity = 0.75, FontSize = 12 };
            void Sample()
            {
                try { sample.Text = Loc.Get(K.SettingsDateTimeSample, ("text", DateTime.Now.ToString(box.Text ?? "", CultureInfo.CurrentUICulture))); }
                catch (FormatException) { sample.Text = Loc.Get(K.SettingsDateTimeInvalid); }
            }
            box.TextChanged += (_, _) => Sample();
            Sample();
            panel.Children.Add(sample);
        }
        return panel;
    }

    // ---- shortcuts ----

    /// <summary>
    /// A button that catches the next key press as a shortcut: a click (or Enter) starts it, a key with its modifiers ends it, Escape or leaving the button cancels it.
    /// Modifier keys alone wait for the key that follows.
    /// </summary>
    private Button ShortcutCapture(string label, string name, Action<KeySequence> done)
    {
        var button = new Button { Content = label, Padding = new Thickness(10, 3), VerticalAlignment = VerticalAlignment.Center };
        button.Classes.Add("shortcutcapture");
        AutomationProperties.SetName(button, name);
        bool catching = false;
        void Stop()
        {
            catching = false;
            button.Content = label;
            button.Classes.Remove("catching");
        }
        button.Click += (_, _) =>
        {
            if (catching) return;
            catching = true;
            button.Content = Loc.Get(K.SettingsShortcutPress);
            button.Classes.Add("catching");
            button.Focus();
        };
        button.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (!catching) return;
            e.Handled = true;
            if (e.Key == Key.Escape && e.KeyModifiers == Avalonia.Input.KeyModifiers.None) { Stop(); return; }
            if (!KeyMapping.TryChord(e.Key, e.KeyModifiers, out var chord)) return;
            Stop();
            if (KeySequence.TryParse(chord.ToString(), _app.Workbench.Platform, out var sequence, out string? _)) done(sequence);
        }, RoutingStrategies.Tunnel);
        button.LostFocus += (_, _) => { if (catching) Stop(); };
        return button;
    }

    private StackPanel ShortcutEditor(string id, TextBlock error, string title)
    {
        string current = CurrentText(id);
        var shown = new TextBlock
        {
            Text = KeySequence.TryParse(current, _app.Workbench.Platform, out var sequence, out _) ? sequence.Display(_app.Workbench.Platform) : current.Length > 0 ? current : Loc.Get(K.SettingsShortcutNone),
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"), VerticalAlignment = VerticalAlignment.Center,
        };
        var capture = ShortcutCapture(Loc.Get(K.SettingsShortcutChange), Loc.Get(K.SettingsShortcutChangeA11y, ("title", title)), caught =>
        {
            shown.Text = caught.Display(_app.Workbench.Platform);
            CommitText(id, caught.ToString(), error);
        });
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { shown, capture } };
    }

    // ---- the icon set ----

    /// <summary>Icons shown in the preview of the icon set.</summary>
    private static readonly string[] PreviewIcons = ["new", "open", "save", "undo", "redo", "cut", "copy", "paste", "find", "replace", "settings", "hash"];

    /// <summary>
    /// Built-in (vector) icons or a folder of pictures (PZ.4.3): a choice of the two, the folder with a button for the system dialog, what the folder must hold, and a preview of the icons as they are
    /// drawn now. A folder without one usable picture is refused with the reason and the setting stays.
    /// </summary>
    private StackPanel IconSetEditor(string id, TextBlock error, string title)
    {
        string folder = CurrentText(id);
        var values = new[] { "builtin", "folder" };
        var combo = Choice(values, folder.Length == 0 ? "builtin" : "folder", v => Loc.Get(v == "builtin" ? K.SettingsIconSetBuiltin : K.SettingsIconSetFolder), title, _ => { });
        var path = new TextBlock { Text = folder, IsVisible = folder.Length > 0, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace") };
        var choose = new Button { Content = Loc.Get(K.SettingsBrowseFolder), IsVisible = folder.Length > 0, Padding = new Thickness(10, 3), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(choose, Loc.Get(K.SettingsBrowseA11y, ("title", title)));
        var status = new TextBlock { Opacity = 0.75, FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        if (folder.Length > 0 && IconSkin.Current is { } skin && skin.Folder == folder)
        {
            status.Text = Loc.Get(K.SettingsIconSetLoaded, ("icons", skin.Images.Count), ("problems", skin.Problems.Count));
            status.IsVisible = true;
        }

        async Task PickAsync()
        {
            string? picked = await _app.Dialogs.PickFolderAsync(this);
            if (picked is null)
            {
                if (CurrentText(id).Length == 0) combo.SelectedItem = "builtin";
                return;
            }
            var loaded = IconSkin.Load(picked);
            if (loaded.Images.Count == 0)
            {
                ShowError(error, Loc.Get(K.IconSkinEmpty, ("folder", picked), ("reason", loaded.Problems.FirstOrDefault() ?? Loc.Get(K.IconSkinNoPictures))));
                if (CurrentText(id).Length == 0) combo.SelectedItem = "builtin";
                return;
            }
            CommitText(id, picked, error, rebuild: true);
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem as string == "builtin") CommitText(id, "", error, rebuild: true);
            else if (CurrentText(id).Length == 0) _ = PickAsync();
        };
        choose.Click += (_, _) => _ = PickAsync();

        var icons = new WrapPanel { Orientation = Orientation.Horizontal };
        AutomationProperties.SetName(icons, Loc.Get(K.SettingsIconSetPreviewA11y));
        foreach (var name in PreviewIcons.Where(BuiltinIcons.All.ContainsKey))
            icons.Children.Add(new Border { Child = CommandIcons.Create(BuiltinIcons.All[name], 20), Padding = new Thickness(6), Margin = new Thickness(0, 0, 4, 4), CornerRadius = new CornerRadius(4), Classes = { "settingiconcell" } });
        var help = new TextBlock { Text = Loc.Get(K.SettingsIconSetHelp), TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12 };
        var top = new WrapPanel { Orientation = Orientation.Horizontal, Children = { combo, choose, path } };
        choose.Margin = path.Margin = new Thickness(10, 0, 0, 0);
        return new StackPanel { Spacing = 6, Children = { top, status, icons, help } };
    }
}
