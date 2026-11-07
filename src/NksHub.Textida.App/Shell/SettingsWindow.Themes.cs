using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Colors;


namespace NksHub.Textida.App.Shell;

/// <summary>
/// The colour theme of the editor (P3.2.5): the list of themes, each with a strip of its colours, with import of TextMate and VS Code themes, export, saving the theme as a custom one and
/// deleting a custom one; and the custom colours, one picker for every editor area and token kind over the chosen theme. The preview at the top of the page draws in the result.
/// </summary>
internal sealed partial class SettingsWindow
{
    /// <summary>What the last import, export or save did; shown under the list in every build of the page until the user dismisses it or goes to another page.</summary>
    private string? _themeNotice;

    /// <summary>The page the last build showed (the category, or the search results); a build of another page drops <see cref="_themeNotice"/>.</summary>
    private string? _builtPage;

    private static readonly Dictionary<string, string> ThemeColorLabels = new(StringComparer.Ordinal)
    {
        [EditorColorTheme.Background] = K.ThemeColorBackground,
        [EditorColorTheme.Foreground] = K.ThemeColorForeground,
        [EditorColorTheme.CurrentLine] = K.ThemeColorCurrentLine,
        [EditorColorTheme.Selection] = K.ThemeColorSelection,
        [EditorColorTheme.LineNumbers] = K.ThemeColorLineNumbers,
        [EditorColorTheme.Minimap] = K.ThemeColorMinimap,
        ["keyword"] = K.ThemeColorKeyword,
        ["string"] = K.ThemeColorString,
        ["comment"] = K.ThemeColorComment,
        ["number"] = K.ThemeColorNumber,
        ["type"] = K.ThemeColorType,
        ["function"] = K.ThemeColorFunction,
        ["variable"] = K.ThemeColorVariable,
        ["property"] = K.ThemeColorProperty,
        ["constant"] = K.ThemeColorConstant,
        ["tag"] = K.ThemeColorTag,
        ["attribute"] = K.ThemeColorAttribute,
        ["preprocessor"] = K.ThemeColorPreprocessor,
        ["meta"] = K.ThemeColorMeta,
        ["regex"] = K.ThemeColorRegex,
        ["heading"] = K.ThemeColorHeading,
        ["link"] = K.ThemeColorLink,
        ["code"] = K.ThemeColorCode,
        ["log.error"] = K.ThemeColorLogError,
        ["log.warning"] = K.ThemeColorLogWarning,
        ["log.info"] = K.ThemeColorLogInfo,
        ["log.debug"] = K.ThemeColorLogDebug,
        ["log.highlight"] = K.ThemeColorLogHighlight,
    };

    internal static string ThemeColorLabel(string key) => Loc.Get(ThemeColorLabels[key]);

    /// <summary>The name of a theme by its id as the list shows it; a custom theme that is gone shows its file name.</summary>
    internal static string ThemeLabel(string id)
    {
        if (id == EditorColorTheme.AutoId) return Loc.Get(K.ColorThemeAuto);
        if (Application.Current is App { EditorThemesOrNull: { } themes } && themes.Find(id) is { } theme) return EditorThemes.DisplayName(theme);
        if (EditorColorTheme.BuiltIn.FirstOrDefault(t => t.Id == id) is { } builtIn) return EditorThemes.DisplayName(builtIn);
        return id.StartsWith(EditorColorTheme.UserPrefix, StringComparison.Ordinal) ? id[EditorColorTheme.UserPrefix.Length..] : id;
    }

    private static IBrush Solid(Color c) => new ImmutableSolidColorBrush(c);

    /// <summary>A small picture of a theme: its background with its text and four token colours on it.</summary>
    private static Border ThemeStrip(EditorColorTheme theme)
    {
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        foreach (var key in new[] { EditorColorTheme.Foreground, "keyword", "string", "comment", "type" })
            dots.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = Solid(theme[key]) });
        return new Border { Child = dots, Background = Solid(theme[EditorColorTheme.Background]), Padding = new Thickness(6, 3), CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = Solid(Color.FromArgb(110, 128, 128, 128)) };
    }

    private EditorColorTheme ThemeOf(string id)
    {
        var themes = _app.EditorThemes;
        return themes.Find(id) ?? (_app.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? EditorColorTheme.Dark : EditorColorTheme.Light);
    }

    // ---- the list of themes ----

    private StackPanel ColorThemeEditor(string id, TextBlock error, string title)
    {
        var themes = _app.EditorThemes;
        var ids = new List<string> { EditorColorTheme.AutoId };
        ids.AddRange(themes.All.Select(t => t.Id));
        string current = CurrentText(id);
        if (!ids.Contains(current)) current = EditorColorTheme.AutoId;
        var combo = Choice(ids, current, ThemeLabel, title, chosen => CommitText(id, chosen, error, rebuild: true));
        combo.MinWidth = 300;
        combo.MaxDropDownHeight = 420;
        combo.ItemTemplate = new FuncDataTemplate<string>((value, _) =>
        {
            if (value is null) return new TextBlock();
            var strip = ThemeStrip(ThemeOf(value));
            AutomationProperties.SetName(strip, Loc.Get(K.ThemeColorsSwatchesA11y, ("name", ThemeLabel(value))));
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { strip, new TextBlock { Text = ThemeLabel(value), VerticalAlignment = VerticalAlignment.Center } } };
        }, supportsRecycling: false);

        Button Action(string text)
        {
            var button = new Button { Content = text, Padding = new Thickness(10, 3), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(button, text);
            return button;
        }
        var import = Action(Loc.Get(K.ColorThemeImport));
        var export = Action(Loc.Get(K.ColorThemeExport));
        var saveAs = Action(Loc.Get(K.ColorThemeSaveAs));
        var delete = Action(Loc.Get(K.ColorThemeDelete));
        delete.IsVisible = current.StartsWith(EditorColorTheme.UserPrefix, StringComparison.Ordinal);
        import.Click += async (_, _) => await ImportThemeAsync(error);
        export.Click += async (_, _) => await ExportThemeAsync(error);
        saveAs.Click += async (_, _) => await SaveThemeAsync(error);
        delete.Click += async (_, _) => await DeleteThemeAsync(current, error);

        var notice = new TextBlock { Text = _themeNotice, TextWrapping = TextWrapping.Wrap, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        notice.Classes.Add("themenotice");
        AutomationProperties.SetLiveSetting(notice, AutomationLiveSetting.Polite);
        var dismiss = new Button { Content = "×", Padding = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(dismiss, Loc.Get(K.DialogClose));
        var noticeRow = new DockPanel { IsVisible = _themeNotice is not null, Children = { Docked(dismiss, Dock.Right), notice } };        dismiss.Click += (_, _) =>
        {
            _themeNotice = null;
            noticeRow.IsVisible = false;
        };
        var help = new TextBlock { Text = Loc.Get(K.ColorThemeHelp, ("folder", themes.Folder)), TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12 };
        var panel = new StackPanel { Spacing = 6, Children = { combo, new WrapPanel { Orientation = Orientation.Horizontal, Children = { import, export, saveAs, delete }, ItemSpacing = 8, LineSpacing = 6 }, noticeRow, help } };
        foreach (var problem in themes.Problems)
            panel.Children.Add(new TextBlock { Text = Loc.Get(K.ColorThemeCustomProblem, ("reason", problem)), Foreground = ThemeColors.Danger, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        return panel;
    }

    /// <summary>Reads a TextMate or VS Code theme, keeps it as a custom theme and chooses it.</summary>
    internal async Task ImportThemeAsync(TextBlock? error = null)
    {
        string? picked = await _app.Dialogs.PickFileAsync(this, Loc.Get(K.ColorThemeImportTitle), Loc.Get(K.ColorThemeImportFilter), ["*.tmTheme", "*.json"]);
        if (picked is null) return;
        string id;
        bool semanticIgnored;
        try { id = _app.EditorThemes.Import(picked, out semanticIgnored); }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
        {
            _themeNotice = Loc.Get(K.ColorThemeImportFailed, ("reason", e.Message));
            if (error is not null) ShowError(error, _themeNotice);
            RefreshKeepingPlace();
            return;
        }
        _themeNotice = Loc.Get(K.ColorThemeImported, ("name", ThemeLabel(id)));
        if (semanticIgnored) _themeNotice += " " + Loc.Get(K.ColorThemeSemanticIgnored);
        ChooseTheme(id);
    }

    /// <summary>Writes the theme the editors show (with the custom colours) as a VS Code theme.</summary>
    internal async Task ExportThemeAsync(TextBlock? error = null)
    {
        var theme = _app.EditorThemes.Effective(_app.ActualThemeVariant);
        string? path = await _app.Dialogs.PickSaveFileAsync(this, Loc.Get(K.ColorThemeExportTitle), theme.Name + ".json");
        if (path is null) return;
        try
        {
            File.WriteAllText(path, theme.ToVsCodeJson());
            _themeNotice = Loc.Get(K.ColorThemeExported, ("path", path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _themeNotice = Loc.Get(K.ColorThemeWriteFailed, ("reason", e.Message));
            if (error is not null) ShowError(error, _themeNotice);
        }
        RefreshKeepingPlace();
    }

    /// <summary>Keeps the theme the editors show (with the custom colours) under a name; the custom colours are then part of it and are cleared.</summary>
    internal async Task SaveThemeAsync(TextBlock? error = null)
    {
        var theme = _app.EditorThemes.Effective(_app.ActualThemeVariant);
        string? name = await _app.Dialogs.PromptAsync(this, Loc.Get(K.ColorThemeSaveTitle), Loc.Get(K.ColorThemeSaveLabel), theme.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        string id;
        try { id = _app.EditorThemes.Save(theme, name); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _themeNotice = Loc.Get(K.ColorThemeWriteFailed, ("reason", e.Message));
            if (error is not null) ShowError(error, _themeNotice);
            RefreshKeepingPlace();
            return;
        }
        _themeNotice = Loc.Get(K.ColorThemeSaved, ("name", name.Trim()));
        _editing = true;
        try { _app.Workbench.Set(EditorThemes.ColorsSetting, (System.Text.Json.Nodes.JsonNode?)null); }
        finally { _editing = false; }
        ChooseTheme(id);
    }

    private async Task DeleteThemeAsync(string id, TextBlock error)
    {
        var buttons = new List<DialogButton> { new(Loc.Get(K.ColorThemeDelete), IsDefault: true), new(Loc.Get(K.DialogCancel), IsCancel: true) };
        if (await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.ColorThemeDelete), Loc.Get(K.ColorThemeDeleteAsk, ("name", ThemeLabel(id))), buttons)) != 0) return;
        try { _app.EditorThemes.Delete(id); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowError(error, Loc.Get(K.ColorThemeWriteFailed, ("reason", e.Message)));
            return;
        }
        ChooseTheme(EditorColorTheme.AutoId);
    }

    private void ChooseTheme(string id)
    {
        _editing = true;
        try { _app.Workbench.Set(EditorThemes.ThemeSetting, id == EditorColorTheme.AutoId ? null : System.Text.Json.Nodes.JsonValue.Create(id)); }
        finally { _editing = false; }
        RefreshKeepingPlace();
    }

    // ---- the custom colours ----

    private StackPanel ThemeColorsEditor(string id, TextBlock error, string title)
    {
        var panel = new StackPanel { Spacing = 4 };
        AutomationProperties.SetName(panel, title);
        var cells = new Dictionary<string, (Border Swatch, Button Reset)>(StringComparer.Ordinal);
        var toggles = new Dictionary<string, (ToggleButton Bold, ToggleButton Italic)>(StringComparer.Ordinal);
        bool showing = false;
        void Show()
        {
            var theme = _app.EditorThemes.Active(_app.ActualThemeVariant);
            var overrides = _app.EditorThemes.Overrides;
            var styles = _app.EditorThemes.StyleOverrides;
            showing = true;
            foreach (var (key, (swatch, reset)) in cells)
            {
                swatch.Background = Solid(theme[key]);
                reset.IsVisible = overrides.ContainsKey(key) || styles.ContainsKey(key);
                if (toggles.TryGetValue(key, out var pair))
                {
                    pair.Bold.IsChecked = theme.Style(key).HasFlag(TokenStyle.Bold);
                    pair.Italic.IsChecked = theme.Style(key).HasFlag(TokenStyle.Italic);
                }
            }
            showing = false;
        }
        ToggleButton StyleToggle(string key, string label, TokenStyle flag)
        {
            bool bold = flag == TokenStyle.Bold;
            var toggle = new ToggleButton
            {
                Content = new TextBlock { Text = Loc.Get(bold ? K.ThemeStyleBold : K.ThemeStyleItalic), FontWeight = bold ? FontWeight.Bold : FontWeight.Normal, FontStyle = bold ? FontStyle.Normal : FontStyle.Italic },
                Padding = new Thickness(6, 0), MinWidth = 24, MinHeight = 20, VerticalAlignment = VerticalAlignment.Center, Tag = key,
            };
            toggle.Classes.Add("themestyle");
            string name = Loc.Get(bold ? K.ThemeStyleBoldA11y : K.ThemeStyleItalicA11y, ("title", label));
            AutomationProperties.SetName(toggle, name);
            ToolTip.SetTip(toggle, name);
            toggle.IsCheckedChanged += (_, _) =>
            {
                if (showing) return;
                var now = _app.EditorThemes.Active(_app.ActualThemeVariant).Style(key);
                SetThemeStyle(key, toggle.IsChecked == true ? now | flag : now & ~flag, error);
            };
            return toggle;
        }
        void Group(string heading, IEnumerable<string> keys)
        {
            panel.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var key in keys)
            {
                string label = ThemeColorLabel(key);
                var swatch = new Border { Width = 26, Height = 16, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = Solid(Color.FromArgb(110, 128, 128, 128)) };
                var button = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { swatch, new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center } } }, Padding = new Thickness(6, 3), Tag = key };
                button.Classes.Add("themecolor");
                AutomationProperties.SetName(button, Loc.Get(K.SettingsColorA11y, ("title", label)));
                var reset = new Button { Content = "×", Padding = new Thickness(6, 0), MinHeight = 20, VerticalAlignment = VerticalAlignment.Center, Tag = key };
                reset.Classes.Add("settingchipremove");
                AutomationProperties.SetName(reset, Loc.Get(K.SettingsResetRowA11y, ("title", label)));
                button.Click += (_, _) =>
                {
                    var now = _app.EditorThemes.Active(_app.ActualThemeVariant)[key];
                    ColorLiterals.TryParse(EditorColorTheme.Format(now), out var match);
                    var picker = new ColorPickerPanel(match);
                    var flyout = new Flyout { Content = picker, Placement = PlacementMode.BottomEdgeAlignedLeft };
                    bool cancelled = false;
                    picker.CancelRequested += () =>
                    {
                        cancelled = true;
                        flyout.Hide();
                    };
                    flyout.Closed += (_, _) =>
                    {
                        if (cancelled) return;
                        var c = picker.Color;
                        SetThemeColor(key, Color.FromArgb((byte)Math.Round(c.A * 255), c.R, c.G, c.B), error);
                    };
                    FlyoutBase.SetAttachedFlyout(button, flyout);
                    FlyoutBase.ShowAttachedFlyout(button);
                };
                reset.Click += (_, _) => ResetThemeKey(key, error);
                cells[key] = (swatch, reset);
                var cell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Width = 250, Margin = new Thickness(0, 0, 8, 6), Children = { button } };
                if (EditorColorTheme.StyleKinds.Contains(key))
                {
                    var pair = (StyleToggle(key, label, TokenStyle.Bold), StyleToggle(key, label, TokenStyle.Italic));
                    toggles[key] = pair;
                    cell.Width = 310;
                    cell.Children.Add(pair.Item1);
                    cell.Children.Add(pair.Item2);
                }
                cell.Children.Add(reset);
                wrap.Children.Add(cell);
            }
            panel.Children.Add(wrap);
        }
        Group(Loc.Get(K.ThemeColorsSurfaces), EditorColorTheme.Surfaces);
        Group(Loc.Get(K.ThemeColorsTokens), EditorColorTheme.Tokens);
        Show();
        _themeColorsShow = Show;
        return panel;
    }

    private Action? _themeColorsShow;

    /// <summary>Puts a custom colour over the theme (null takes it off again), as the picker does when it closes; the editors, the preview and the swatches follow.</summary>
    internal void SetThemeColor(string key, Color? color, TextBlock? error = null)
    {
        var overrides = _app.EditorThemes.Overrides.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        if (color is { } c) overrides[key] = c;
        else overrides.Remove(key);
        CommitThemeOverrides(overrides, _app.EditorThemes.StyleOverrides, error);
    }

    /// <summary>Makes a token kind bold, italic, both or plain over the theme, as the B and I toggles do; the style the chosen theme has itself needs no custom entry.</summary>
    internal void SetThemeStyle(string key, TokenStyle style, TextBlock? error = null)
    {
        var styles = _app.EditorThemes.StyleOverrides.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var own = _app.EditorThemes.Find(_app.EditorThemes.SelectedId)?.Style(key) ?? TokenStyle.None;
        if (style == own) styles.Remove(key);
        else styles[key] = style;
        CommitThemeOverrides(_app.EditorThemes.Overrides, styles, error);
    }

    /// <summary>Takes the custom colour and style of a key off, back to the theme.</summary>
    private void ResetThemeKey(string key, TextBlock? error)
    {
        var overrides = _app.EditorThemes.Overrides.Where(p => p.Key != key).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var styles = _app.EditorThemes.StyleOverrides.Where(p => p.Key != key).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        CommitThemeOverrides(overrides, styles, error);
    }

    private void CommitThemeOverrides(IReadOnlyDictionary<string, Color> colors, IReadOnlyDictionary<string, TokenStyle> styles, TextBlock? error)
    {
        string value = EditorColorTheme.FormatOverrides(colors, styles);
        if (value == CurrentText(EditorThemes.ColorsSetting)) return;
        Commit(EditorThemes.ColorsSetting, value.Length == 0 ? null : System.Text.Json.Nodes.JsonValue.Create(value), error ?? new TextBlock());
        _themeColorsShow?.Invoke();
    }
}
