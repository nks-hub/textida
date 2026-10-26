using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Keys;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Searchable settings: every setting with its effective value and where the value comes from, and every shortcut with its conflicts.
/// Search runs over <see cref="SettingsSearchIndex"/>; a change is written to the user settings file and takes effect at once.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private readonly App _app;
    private readonly TextBox _search = new();
    private readonly StackPanel _results = new() { Spacing = 8, Margin = new Thickness(0, 0, 14, 0) };
    private readonly StackPanel _banners = new() { Spacing = 6 };
    private readonly TextBlock _count = new() { Opacity = 0.7, Margin = new Thickness(2, 0, 0, 0) };

    public SettingsWindow(App app)
    {
        _app = app;
        Classes.Add("shell");
        Width = 780;
        Height = 660;
        MinWidth = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var top = new StackPanel { Spacing = 8, Margin = new Thickness(18, 16, 18, 8), Children = { _search, _banners, _count } };
        var scroller = new ScrollViewer { Content = _results, Margin = new Thickness(18, 0, 4, 12) };
        var root = new DockPanel { Children = { Dock(top), scroller } };
        Content = root;
        _search.TextChanged += (_, _) => Refresh();
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.F1) { app.OpenHelp("app.settings"); e.Handled = true; } };
        app.Workbench.SettingChanged += OnSettingChanged;
        Loc.Changed += OnLanguageChanged;
        Closed += (_, _) =>
        {
            app.Workbench.SettingChanged -= OnSettingChanged;
            Loc.Changed -= OnLanguageChanged;
        };
        ApplyTexts();
        Refresh();
    }

    private static Control Dock(Control c)
    {
        DockPanel.SetDock(c, Avalonia.Controls.Dock.Top);
        return c;
    }

    private void OnSettingChanged(string key)
    {
        if (!_editing) Refresh();
    }

    private void OnLanguageChanged()
    {
        ApplyTexts();
        Refresh();
    }

    private void ApplyTexts()
    {
        Title = Loc.Get(K.SettingsTitle);
        _search.PlaceholderText = Loc.Get(K.SettingsSearchWatermark);
        AutomationProperties.SetName(_search, Loc.Get(K.SettingsSearchA11y));
    }

    internal void SetQuery(string text)
    {
        _search.Text = text;
        Refresh();
    }

    internal string Query => _search.Text ?? "";

    /// <summary>Rows shown, for tests: setting keys and command ids.</summary>
    internal IReadOnlyList<string> ShownIds => [.. _results.Children.OfType<Control>().Select(c => c.Tag as string).OfType<string>()];

    internal Control? RowOf(string id) => _results.Children.OfType<Control>().FirstOrDefault(c => c.Tag as string == id);

    internal IEnumerable<Control> RowsOf(string id) => _results.Children.OfType<Control>().Where(c => c.Tag as string == id);

    internal IReadOnlyList<string> BannerTexts => [.. _banners.Children.OfType<TextBlock>().Select(t => t.Text ?? "")];

    private bool _editing;

    internal void Refresh()
    {
        var wb = _app.Workbench;
        var hits = wb.SearchIndex.Search(_search.Text ?? "", wb.Settings, wb.Commands, wb.Keymap);
        _results.Children.Clear();
        foreach (var hit in hits)
            _results.Children.Add(hit.Kind == SearchHitKind.Setting ? SettingRow(hit) : KeyRow(hit));
        _count.Text = Loc.Plural(K.SettingsResultCount, hits.Count);
        BuildBanners();
    }

    private void BuildBanners()
    {
        var wb = _app.Workbench;
        _banners.Children.Clear();
        if (wb.SettingsProblem is { } problem) _banners.Children.Add(Banner(Loc.Get(K.SettingsFileProblem, ("reason", problem))));
        if (wb.KeymapProblem is { } keymapProblem) _banners.Children.Add(Banner(Loc.Get(K.SettingsKeymapProblem, ("reason", keymapProblem))));
        foreach (var c in wb.Keymap.Conflicts)
        {
            string Title(KeyBinding b) => wb.Commands.TryGet(b.CommandId, out var cmd) ? wb.Strings.Get(cmd.TitleKey) : b.CommandId;
            var kind = c.Kind switch { KeyConflictKind.SameLayer => K.ConflictSameLayer, KeyConflictKind.Prefix => K.ConflictPrefix, _ => K.ConflictOverridden };
            _banners.Children.Add(Banner(Loc.Get(K.SettingsConflict, ("shortcut", c.Winner.Sequence.Display(wb.Platform)), ("winner", Title(c.Winner)), ("loser", Title(c.Loser)), ("kind", Loc.Get(kind))), c.IsAmbiguous));
        }
    }

    private static TextBlock Banner(string text, bool strong = true) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = strong ? new SolidColorBrush(Color.Parse("#C0392B")) : null, Opacity = strong ? 1 : 0.7 };

    private static string SourceName(SettingSource source) => source switch
    {
        SettingSource.Default => Loc.Get(K.SourceDefault),
        SettingSource.User => Loc.Get(K.SourceUser),
        SettingSource.UserLanguage => Loc.Get(K.SourceUserLanguage),
        SettingSource.EditorConfig => Loc.Get(K.SourceEditorConfig),
        SettingSource.Document => Loc.Get(K.SourceDocument),
        SettingSource.Plugin => Loc.Get(K.SourcePlugin),
        SettingSource.Profile => Loc.Get(K.SourceProfile),
        _ => Loc.Get(K.SourceWorkspace),
    };

    // ---- rows ----

    private Control KeyRow(SearchHit hit)
    {
        var title = new TextBlock { Text = hit.Title, FontWeight = FontWeight.SemiBold };
        var shortcut = new TextBlock { Text = hit.Shortcut ?? "", FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace") };
        var info = new TextBlock { Text = Loc.Get(K.SettingsShortcutSource, ("source", hit.ShortcutSource ?? ""), ("id", hit.Id)), Opacity = 0.65, FontSize = 12 };
        var stack = new StackPanel { Spacing = 2, Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { title, shortcut } }, info } };
        if (hit.HasConflict)
            foreach (var c in hit.Conflicts.Where(c => c.IsAmbiguous))
                stack.Children.Add(Banner(Loc.Get(K.SettingsRowConflict, ("other", c.Winner.CommandId == hit.Id ? c.Loser.CommandId : c.Winner.CommandId))));
        var row = new Border { Child = stack, Padding = new Thickness(12, 8), Tag = hit.Id, CornerRadius = new CornerRadius(6) };
        row.Classes.Add("settingrow");
        AutomationProperties.SetName(row, $"{hit.Title} {hit.Shortcut}");
        return row;
    }

    private Control SettingRow(SearchHit hit)
    {
        var wb = _app.Workbench;
        wb.Host.Settings.TryGet(hit.Id, out var definition);
        var resolved = wb.Settings.Resolve(hit.Id)!;

        var title = new TextBlock { Text = hit.Title, FontWeight = FontWeight.SemiBold };
        var key = new TextBlock { Text = hit.Id, Opacity = 0.55, FontSize = 12, VerticalAlignment = VerticalAlignment.Bottom };
        var description = new TextBlock { Text = hit.Description ?? "", TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
        var source = new TextBlock { Text = Loc.Get(K.SettingsValueSource, ("source", SourceName(resolved.Source) + (resolved.Source == SettingSource.EditorConfig && resolved.LayerName is { } layer ? " (" + layer + ")" : ""))), Opacity = 0.65, FontSize = 12 };
        var error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#C0392B")), IsVisible = false, TextWrapping = TextWrapping.Wrap };

        var editor = MakeEditor(definition, resolved, error);
        var reset = new Button { Content = Loc.Get(K.SettingsReset), IsVisible = resolved.Source != SettingSource.Default, Padding = new Thickness(8, 2), MinHeight = 24 };
        reset.Click += (_, _) => Commit(hit.Id, null, error);

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { title, key } };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { editor, reset } };
        var stack = new StackPanel { Spacing = 3, Children = { header, description, controls, source, error } };
        var row = new Border { Child = stack, Padding = new Thickness(12, 8), Tag = hit.Id, CornerRadius = new CornerRadius(6) };
        row.Classes.Add("settingrow");
        AutomationProperties.SetName(row, $"{hit.Title}: {resolved.ValueText}");
        return row;
    }

    private Control MakeEditor(Workbench.Contributions.SettingDefinition definition, ResolvedSetting resolved, TextBlock error)
    {
        string id = definition.Key;
        if (definition.Type == "boolean")
        {
            var box = new CheckBox { IsChecked = resolved.Value.GetValue<bool>() };
            AutomationProperties.SetName(box, id);
            box.IsCheckedChanged += (_, _) => Commit(id, JsonValue.Create(box.IsChecked == true), error);
            return box;
        }
        if (definition.Values is { Count: > 0 } values)
        {
            var combo = new ComboBox { ItemsSource = values, SelectedItem = resolved.Value.GetValue<string>(), MinWidth = 180 };
            AutomationProperties.SetName(combo, id);
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string s && s != resolved.Value.GetValue<string>()) Commit(id, JsonValue.Create(s), error); };
            return combo;
        }
        string current = resolved.Value is JsonValue jv && jv.TryGetValue<string>(out var str) ? str : resolved.ValueText;
        var text = new TextBox { Text = current, MinWidth = 260 };
        AutomationProperties.SetName(text, id);
        void Apply()
        {
            string value = text.Text ?? "";
            JsonNode? node = definition.Type switch
            {
                "integer" when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => JsonValue.Create(l),
                "number" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => JsonValue.Create(d),
                "string" => JsonValue.Create(value),
                _ => null,
            };
            if (node is null) { error.Text = Loc.Get(K.SettingsInvalidNumber, ("text", value)); error.IsVisible = true; return; }
            if (value == current) return;
            Commit(id, node, error);
        }
        text.LostFocus += (_, _) => Apply();
        text.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { Apply(); e.Handled = true; } };
        return text;
    }

    private void Commit(string key, JsonNode? value, TextBlock error)
    {
        _editing = true;
        string? problem;
        try { problem = _app.Workbench.Set(key, value); }
        finally { _editing = false; }
        if (problem is not null)
        {
            error.Text = Loc.Get(K.SettingsRefused, ("reason", problem));
            error.IsVisible = true;
            return;
        }
        Refresh();
    }
}
