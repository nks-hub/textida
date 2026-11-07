using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Contributions;
using NksHub.Textida.Workbench.Keys;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The rows of the settings page. A setting is a row of a card: on the left its title with a mark when the user changed it, its description and a line with the default value, the range and where the
/// value comes from; its control on the right (or under the texts for a control that needs the width) with the way back to the default beside it. A shortcut is a row with a button that catches a new one.
/// </summary>
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
        [("editor.wordWrap", "off")] = K.SettingsValueEditorWordWrapOff,
        [("editor.wordWrap", "on")] = K.SettingsValueEditorWordWrapOn,
        [("editor.renderWhitespace", "none")] = K.SettingsValueEditorRenderWhitespaceNone,
        [("editor.renderWhitespace", "selection")] = K.SettingsValueEditorRenderWhitespaceSelection,
        [("editor.renderWhitespace", "all")] = K.SettingsValueEditorRenderWhitespaceAll,
        [("files.encoding", "utf-8")] = K.SettingsValueFilesEncodingUtf8,
        [("files.encoding", "utf-8-bom")] = K.SettingsValueFilesEncodingUtf8Bom,
        [("files.encoding", "utf-16le")] = K.SettingsValueFilesEncodingUtf16le,
        [("files.encoding", "utf-16be")] = K.SettingsValueFilesEncodingUtf16be,
        [("files.encoding", "windows-1250")] = K.SettingsValueFilesEncodingWindows1250,
        [("files.encoding", "windows-1252")] = K.SettingsValueFilesEncodingWindows1252,
        [("files.encoding", "iso-8859-2")] = K.SettingsValueFilesEncodingIso88592,
        [("files.eol", "auto")] = K.SettingsValueFilesEolAuto,
        [("files.eol", "lf")] = K.SettingsValueFilesEolLf,
        [("files.eol", "crlf")] = K.SettingsValueFilesEolCrlf,
        [("files.autoSave", "off")] = K.SettingsValueFilesAutoSaveOff,
        [("files.autoSave", "afterDelay")] = K.SettingsValueFilesAutoSaveAfterDelay,
        [("remote.versionCheck", "stat")] = K.SettingsValueRemoteVersionCheckStat,
        [("remote.versionCheck", "content")] = K.SettingsValueRemoteVersionCheckContent,
        [("git.gutterBase", "index")] = K.SettingsValueGitGutterBaseIndex,
        [("git.gutterBase", "head")] = K.SettingsValueGitGutterBaseHead,
        [("git.pullMode", "ask")] = K.SettingsValueGitPullModeAsk,
        [("git.pullMode", "merge")] = K.SettingsValueGitPullModeMerge,
        [("git.pullMode", "rebase")] = K.SettingsValueGitPullModeRebase,
        [("editor.profile", "default")] = K.SettingsValueEditorProfileDefault,
        [("editor.profile", "pspad")] = K.SettingsValueEditorProfilePspad,
        [("editor.altDragMode", "carets")] = K.SettingsValueEditorAltDragModeCarets,
        [("editor.altDragMode", "block")] = K.SettingsValueEditorAltDragModeBlock,
        [("editor.pairStyle", "default")] = K.SettingsValueEditorPairStyleDefault,
        [("editor.pairStyle", "pspad")] = K.SettingsValueEditorPairStylePspad,
        [("files.binaryAsHex", "ask")] = K.SettingsValueFilesBinaryAsHexAsk,
        [("files.binaryAsHex", "always")] = K.SettingsValueFilesBinaryAsHexAlways,
        [("files.binaryAsHex", "never")] = K.SettingsValueFilesBinaryAsHexNever,
        [("files.imageOpen", "image")] = K.SettingsValueFilesImageOpenImage,
        [("files.imageOpen", "text")] = K.SettingsValueFilesImageOpenText,
        [("files.imageOpen", "hex")] = K.SettingsValueFilesImageOpenHex,
        [("markdown.defaultMode", "read")] = K.SettingsValueMarkdownDefaultModeRead,
        [("markdown.defaultMode", "edit")] = K.SettingsValueMarkdownDefaultModeEdit,
        [("markdown.defaultMode", "source")] = K.SettingsValueMarkdownDefaultModeSource,
        [("sql.dialect", "generic")] = K.SettingsValueSqlDialectGeneric,
        [("sql.dialect", "mysql")] = K.SettingsValueSqlDialectMysql,
        [("sql.dialect", "postgres")] = K.SettingsValueSqlDialectPostgres,
        [("sql.dialect", "sqlite")] = K.SettingsValueSqlDialectSqlite,
        [("sql.dialect", "tsql")] = K.SettingsValueSqlDialectTsql,
    };

    internal static string ValueLabel(string key, string value) => ValueLabels.TryGetValue((key, value), out var textKey) ? Loc.Get(textKey) : value;

    /// <summary>A value as the user reads it: on and off for a switch, the label of a choice, the name of an encoding, "empty" for an empty text.</summary>
    internal static string ValueWords(SettingDefinition definition, JsonNode? value)
    {
        if (value is JsonValue v && v.TryGetValue<bool>(out var on)) return Loc.Get(on ? K.SettingsOn : K.SettingsOff);
        if (value is JsonValue n && SettingRules.ToNumber(n) is { } number) return number.ToString(CultureInfo.CurrentCulture);
        string text = value is JsonValue s && s.TryGetValue<string>(out var str) ? str : SettingRules.ToText(value);
        if (text.Length == 0) return definition.Format switch
        {
            SettingFormats.IconSet => Loc.Get(K.SettingsIconSetBuiltin),
            SettingFormats.Encoding => Loc.Get(K.SettingsEncodingDefault),
            _ => Loc.Get(K.SettingsEmptyValue),
        };
        if (definition.Format == SettingFormats.ColorTheme) return ThemeLabel(text);
        if (definition.Format == SettingFormats.Multi) return string.Join(", ", SettingFormats.Split(definition, text).Select(item => ValueLabel(definition.Key, item)));
        return ValueLabel(definition.Key, text);
    }

    private static TextBlock Marked(string text, IReadOnlyList<string> words, TextBlock block)
    {
        SearchHighlight.Fill(block, text, words, ThemeColors.Mark);
        return block;
    }

    /// <summary>The parts of a setting row that follow its value without building the page again.</summary>
    private sealed record RowView(Border Row, string Title, TextBlock Mark, TextBlock Default, TextBlock Source, Button Reset, TextBlock Error);

    private readonly Dictionary<string, RowView> _rowViews = new(StringComparer.Ordinal);

    // ---- cards ----

    /// <summary>A card of the page: an optional section heading and rows divided by thin lines.</summary>
    private static (Border Card, StackPanel Body) Card(string? heading, string headingClass = "settingsection")
    {
        var body = new StackPanel();
        if (heading is not null)
        {
            var header = new TextBlock { Text = heading, Margin = new Thickness(14, 10, 14, 4), TextWrapping = TextWrapping.Wrap };
            header.Classes.Add(headingClass);
            body.Children.Add(header);
        }
        var card = new Border { Child = body, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 2, 0, 6) };
        card.Classes.Add("settingcard");
        return (card, body);
    }

    private static void AddRow(StackPanel body, Control row)
    {
        if (body.Children.OfType<Border>().Any(b => b.Classes.Contains("settingrow")))
        {
            var divider = new Border { Height = 1, Margin = new Thickness(14, 0) };
            divider.Classes.Add("settingdivider");
            body.Children.Add(divider);
        }
        body.Children.Add(row);
    }

    private static Border RowFrame(Control content, string id)
    {
        var row = new Border { Child = content, Padding = new Thickness(14, 10), Tag = id, CornerRadius = new CornerRadius(6) };
        row.Classes.Add("settingrow");
        return row;
    }

    // ---- a shortcut ----

    private Control KeyRow(SearchHit hit, IReadOnlyList<string> words, HashSet<string> userShortcuts)
    {
        var wb = _app.Workbench;
        var title = Marked(hit.Title, words, new TextBlock { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        var info = new TextBlock { Text = Loc.Get(K.SettingsShortcutSource, ("source", hit.ShortcutSource ?? ""), ("id", hit.Id)), Opacity = 0.65, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        var texts = new StackPanel { Spacing = 2, Children = { title, info } };
        if (hit.HasConflict)
            foreach (var c in hit.Conflicts.Where(c => c.IsAmbiguous))
                texts.Children.Add(Banner(Loc.Get(K.SettingsRowConflict, ("other", c.Winner.CommandId == hit.Id ? c.Loser.CommandId : c.Winner.CommandId))));
        var error = new TextBlock { Foreground = ThemeColors.Danger, IsVisible = false, TextWrapping = TextWrapping.Wrap };
        texts.Children.Add(error);

        var shortcut = Marked(hit.Shortcut is { Length: > 0 } s ? s : Loc.Get(K.SettingsShortcutNone), words, new TextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"), VerticalAlignment = VerticalAlignment.Center });
        var capture = ShortcutCapture(Loc.Get(K.SettingsShortcutChange), Loc.Get(K.SettingsShortcutChangeA11y, ("title", hit.Title)), sequence =>
        {
            if (wb.SetUserShortcut(hit.Id, sequence) is { } problem) ShowError(error, Loc.Get(K.SettingsShortcutFailed, ("reason", problem)));
            else ShortcutsChanged(hit.Id);
        });
        var reset = new Button { Content = Loc.Get(K.SettingsShortcutReset), IsVisible = userShortcuts.Contains(hit.Id), Padding = new Thickness(8, 2), MinHeight = 24, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(reset, Loc.Get(K.SettingsShortcutResetA11y, ("title", hit.Title)));
        reset.Click += (_, _) =>
        {
            if (wb.SetUserShortcut(hit.Id, null) is { } problem) ShowError(error, Loc.Get(K.SettingsShortcutFailed, ("reason", problem)));
            else ShortcutsChanged(hit.Id);
        };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { shortcut, capture, reset } };

        var row = RowFrame(TwoColumns(texts, controls), hit.Id);
        AutomationProperties.SetName(row, $"{hit.Title} {hit.Shortcut}");
        return row;
    }

    /// <summary>The keymap changed: the menus show the new shortcut and the page lists it, framed.</summary>
    private void ShortcutsChanged(string commandId)
    {
        foreach (var window in _app.Windows) window.ApplyTexts();
        ShowKey(commandId, keepSearch: true);
    }

    private static void ShowError(TextBlock error, string text)
    {
        error.Text = text;
        error.IsVisible = true;
    }

    /// <summary>Texts on the left, controls on the right; the texts wrap and the controls keep their width.</summary>
    private static Grid TwoColumns(Control texts, Control controls)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(controls, 1);
        controls.Margin = new Thickness(16, 0, 0, 0);
        grid.Children.Add(texts);
        grid.Children.Add(controls);
        return grid;
    }

    // ---- a setting ----

    private Control SettingRow(SearchHit hit, IReadOnlyList<string> words, List<string> changedKeys)
    {
        var wb = _app.Workbench;
        wb.Host.Settings.TryGet(hit.Id, out var definition);
        var resolved = wb.Settings.Resolve(hit.Id)!;
        bool changed = resolved.Source is SettingSource.User or SettingSource.UserLanguage;
        if (changed) changedKeys.Add(hit.Id);

        var title = Marked(hit.Title, words, new TextBlock { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        var mark = new TextBlock { Text = Loc.Get(K.SettingsChanged), IsVisible = changed, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 0) };
        mark.Classes.Add("settingchanged");
        var header = new WrapPanel { Orientation = Orientation.Horizontal, Children = { title, mark } };
        var description = Marked(hit.Description ?? "", words, new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, IsVisible = !string.IsNullOrEmpty(hit.Description) });

        // one quiet line: the default only once the value differs (it says what Reset gives back), the range, the source only when something other than the user or the default sets the value
        var defaults = Meta(Loc.Get(K.SettingsDefaultValue, ("value", ValueWords(definition, definition.Default))), changed);
        var range = Meta(RangeText(definition) ?? "", RangeText(definition) is not null);
        var source = Meta(SourceText(resolved), ShowsSource(resolved));
        var key = Marked(hit.Id, words, new TextBlock { Opacity = 0.5, FontSize = 11, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        var meta = new WrapPanel { Children = { defaults, range, source, key } };
        var error = new TextBlock { Foreground = ThemeColors.Danger, IsVisible = false, TextWrapping = TextWrapping.Wrap };

        var editor = MakeEditor(definition, resolved, error, hit.Title, out bool wide);
        var reset = new Button { Content = Loc.Get(K.SettingsReset), IsVisible = changed, Padding = new Thickness(8, 2), MinHeight = 24, VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(reset, Loc.Get(K.SettingsResetRowA11y, ("title", hit.Title)));
        reset.Click += (_, _) => Commit(hit.Id, null, error, rebuild: true);

        var texts = new StackPanel { Spacing = 3, Children = { header, description } };
        Control content;
        if (wide)
        {
            texts.Children.Add(editor);
            editor.Margin = new Thickness(0, 6, 0, 0);
            texts.Children.Add(meta);
            texts.Children.Add(error);
            content = TwoColumns(texts, reset);
        }
        else
        {
            texts.Children.Add(meta);
            texts.Children.Add(error);
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Top, Children = { editor, reset } };
            content = TwoColumns(texts, controls);
        }
        var row = RowFrame(content, hit.Id);
        _rowViews[hit.Id] = new RowView(row, hit.Title, mark, defaults, source, reset, error);
        AutomationProperties.SetName(row, RowName(hit.Title, resolved, changed));
        return row;
    }

    private static TextBlock Meta(string text, bool shown) =>
        new() { Text = text, IsVisible = shown, Opacity = 0.65, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The default and the user's own value need no line: the "changed" mark and the default already say it.</summary>
    private static bool ShowsSource(ResolvedSetting resolved) => resolved.Source is not (SettingSource.Default or SettingSource.User or SettingSource.UserLanguage);

    private static string RowName(string title, ResolvedSetting resolved, bool changed) =>
        changed ? Loc.Get(K.SettingsRowChanged, ("title", title), ("value", resolved.ValueText)) : $"{title}: {resolved.ValueText}";

    private static string SourceText(ResolvedSetting resolved) =>
        Loc.Get(K.SettingsValueSource, ("source", SourceName(resolved.Source) + (resolved.Source == SettingSource.EditorConfig && resolved.LayerName is { } layer ? " (" + layer + ")" : "")));

    private static string? RangeText(SettingDefinition d)
    {
        string N(double v) => v.ToString(CultureInfo.CurrentCulture);
        return (d.Minimum, d.Maximum) switch
        {
            ({ } min, { } max) => Loc.Get(K.SettingsRange, ("min", N(min)), ("max", N(max))),
            ({ } min, null) => Loc.Get(K.SettingsRangeMin, ("min", N(min))),
            _ => null,
        };
    }

    /// <summary>Writes a value (null: back to the default). The row follows in place, so a control the user is in keeps the keyboard; <paramref name="rebuild"/> builds the page again (the editors show the new value).</summary>
    private void Commit(string key, JsonNode? value, TextBlock error, bool rebuild = false)
    {
        _editing = true;
        string? problem;
        try { problem = _app.Workbench.Set(key, value); }
        finally { _editing = false; }
        if (problem is not null)
        {
            ShowError(error, Loc.Get(K.SettingsRefused, ("reason", problem)));
            return;
        }
        error.IsVisible = false;
        // the profile puts values of its own into many settings
        if (rebuild || key == "editor.profile") RefreshKeepingPlace();
        else UpdateRow(key);
    }

    private void UpdateRow(string key)
    {
        _preview?.Refresh();
        if (!_rowViews.TryGetValue(key, out var view)) return;
        var resolved = _app.Workbench.Settings.Resolve(key)!;
        bool changed = resolved.Source is SettingSource.User or SettingSource.UserLanguage;
        view.Mark.IsVisible = changed;
        view.Reset.IsVisible = changed;
        view.Default.IsVisible = changed;
        view.Source.Text = SourceText(resolved);
        view.Source.IsVisible = ShowsSource(resolved);
        AutomationProperties.SetName(view.Row, RowName(view.Title, resolved, changed));
        _pendingReset.Remove(key);
        if (changed) _pendingReset.Add(key);
        _resetCategory.IsVisible = (_search.Text ?? "").Trim().Length == 0 && _pendingReset.Count > 0;
    }
}
