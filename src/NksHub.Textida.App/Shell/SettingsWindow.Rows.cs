using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.App.Shell;

/// <summary>The rows of the settings page: one setting with its editor, or one shortcut.</summary>
internal sealed partial class SettingsWindow
{
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

    /// <summary>The words that name a value of a setting that is a choice: the theme, the language of the interface. Other values are shown as they are written in the settings file.</summary>
    private static readonly Dictionary<(string Key, string Value), string> ValueLabels = new()
    {
        [("workbench.theme", "system")] = K.SettingsValueThemeSystem,
        [("workbench.theme", "light")] = K.SettingsValueThemeLight,
        [("workbench.theme", "dark")] = K.SettingsValueThemeDark,
        [("workbench.uiLanguage", "auto")] = K.SettingsValueLanguageAuto,
        [("workbench.uiLanguage", "cs")] = K.SettingsValueLanguageCs,
        [("workbench.uiLanguage", "en")] = K.SettingsValueLanguageEn,
        [("workbench.uiLanguage", "sk")] = K.SettingsValueLanguageSk,
        [("workbench.uiLanguage", "de")] = K.SettingsValueLanguageDe,
    };

    internal static string ValueLabel(string key, string value) => ValueLabels.TryGetValue((key, value), out var textKey) ? Loc.Get(textKey) : value;

    private static TextBlock Marked(string text, IReadOnlyList<string> words, TextBlock block)
    {
        SearchHighlight.Fill(block, text, words, ThemeColors.Mark);
        return block;
    }

    private Control KeyRow(SearchHit hit, IReadOnlyList<string> words)
    {
        var title = Marked(hit.Title, words, new TextBlock { FontWeight = FontWeight.SemiBold });
        var shortcut = Marked(hit.Shortcut ?? "", words, new TextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace") });
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

    private Control SettingRow(SearchHit hit, IReadOnlyList<string> words, List<string> changedKeys)
    {
        var wb = _app.Workbench;
        wb.Host.Settings.TryGet(hit.Id, out var definition);
        var resolved = wb.Settings.Resolve(hit.Id)!;
        bool changed = resolved.Source is SettingSource.User or SettingSource.UserLanguage;
        if (changed) changedKeys.Add(hit.Id);

        var title = Marked(hit.Title, words, new TextBlock { FontWeight = FontWeight.SemiBold });
        var key = Marked(hit.Id, words, new TextBlock { Opacity = 0.55, FontSize = 12, VerticalAlignment = VerticalAlignment.Bottom });
        var mark = new TextBlock { Text = Loc.Get(K.SettingsChanged), IsVisible = changed, VerticalAlignment = VerticalAlignment.Bottom };
        mark.Classes.Add("settingchanged");
        var description = Marked(hit.Description ?? "", words, new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
        var source = new TextBlock { Text = Loc.Get(K.SettingsValueSource, ("source", SourceName(resolved.Source) + (resolved.Source == SettingSource.EditorConfig && resolved.LayerName is { } layer ? " (" + layer + ")" : ""))), Opacity = 0.65, FontSize = 12 };
        var error = new TextBlock { Foreground = ThemeColors.Danger, IsVisible = false, TextWrapping = TextWrapping.Wrap };

        var editor = MakeEditor(definition, resolved, error, hit.Title);
        var reset = new Button { Content = Loc.Get(K.SettingsReset), IsVisible = changed, Padding = new Thickness(8, 2), MinHeight = 24 };
        AutomationProperties.SetName(reset, Loc.Get(K.SettingsResetRowA11y, ("title", hit.Title)));
        reset.Click += (_, _) => Commit(hit.Id, null, error);

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { title, key, mark } };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { editor, reset } };
        var stack = new StackPanel { Spacing = 3, Children = { header, description, controls, source, error } };
        var row = new Border { Child = stack, Padding = new Thickness(12, 8), Tag = hit.Id, CornerRadius = new CornerRadius(6) };
        row.Classes.Add("settingrow");
        AutomationProperties.SetName(row, changed ? Loc.Get(K.SettingsRowChanged, ("title", hit.Title), ("value", resolved.ValueText)) : $"{hit.Title}: {resolved.ValueText}");
        return row;
    }

    private Control MakeEditor(Workbench.Contributions.SettingDefinition definition, ResolvedSetting resolved, TextBlock error, string title)
    {
        string id = definition.Key;
        if (definition.Type == "boolean")
        {
            var box = new CheckBox { IsChecked = resolved.Value.GetValue<bool>() };
            AutomationProperties.SetName(box, title);
            box.IsCheckedChanged += (_, _) => Commit(id, JsonValue.Create(box.IsChecked == true), error);
            return box;
        }
        if (definition.Values is { Count: > 0 } values)
        {
            var combo = new ComboBox { ItemsSource = values, SelectedItem = resolved.Value.GetValue<string>(), MinWidth = 180 };
            if (values.Any(v => ValueLabels.ContainsKey((id, v))))
                combo.ItemTemplate = new FuncDataTemplate<string>((tag, _) => new TextBlock { Text = ValueLabel(id, tag ?? "") }, supportsRecycling: true);
            AutomationProperties.SetName(combo, title);
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string s && s != resolved.Value.GetValue<string>()) Commit(id, JsonValue.Create(s), error); };
            return combo;
        }
        string current = resolved.Value is JsonValue jv && jv.TryGetValue<string>(out var str) ? str : resolved.ValueText;
        var text = new TextBox { Text = current, MinWidth = 260 };
        AutomationProperties.SetName(text, title);
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
        RefreshKeepingPlace();
    }
}
