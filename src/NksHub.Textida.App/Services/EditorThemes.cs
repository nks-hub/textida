using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;

namespace NksHub.Textida.App.Services;

/// <summary>
/// The colour theme the editors draw in (P3.2.5): the theme chosen in <c>editor.colorTheme</c> with the colours of <c>editor.themeColors</c> over it. "Automatic" follows the light or
/// dark theme of the window. While the system asks for high contrast its colours win and neither the theme nor the custom colours apply. Custom themes are files in the
/// <c>themes</c> folder of the profile, written in the VS Code format.
/// </summary>
internal sealed class EditorThemes
{
    public const string ThemeSetting = "editor.colorTheme";
    public const string ColorsSetting = "editor.themeColors";
    public const string FolderName = "themes";

    private readonly App _app;
    private readonly Dictionary<ThemeVariant, EditorColorTheme> _active = [];
    private List<EditorColorTheme> _custom = [];

    public EditorThemes(App app)
    {
        _app = app;
        Folder = Path.Combine(app.ProfileDirectory, FolderName);
        LoadCustom();
    }

    /// <summary>The folder of the custom themes.</summary>
    public string Folder { get; }

    /// <summary>Problems met while reading the custom themes, one per file.</summary>
    public List<string> Problems { get; } = [];

    public IReadOnlyList<EditorColorTheme> Custom => _custom;

    /// <summary>Every theme the list offers: the built-in ones, then the custom ones by name.</summary>
    public IEnumerable<EditorColorTheme> All => EditorColorTheme.BuiltIn.Concat(_custom);

    public string SelectedId => _app.Workbench.Settings.GetString(ThemeSetting);

    public IReadOnlyDictionary<string, Color> Overrides => EditorColorTheme.ParseOverrides(_app.Workbench.Settings.GetString(ColorsSetting));

    public EditorColorTheme? Find(string id) => All.FirstOrDefault(t => t.Id == id);

    /// <summary>The name of a theme as the list shows it.</summary>
    public static string DisplayName(EditorColorTheme theme) => theme.Id switch
    {
        EditorColorTheme.AutoId => Loc.Get(K.ColorThemeAuto),
        EditorColorTheme.LightId => Loc.Get(K.ColorThemeLight),
        EditorColorTheme.DarkId => Loc.Get(K.ColorThemeDark),
        _ => theme.Name,
    };

    /// <summary>The theme the editors of a window in <paramref name="variant"/> draw in, with the custom colours over it.</summary>
    public EditorColorTheme Active(ThemeVariant variant)
    {
        bool dark = variant == ThemeVariant.Dark;
        variant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        if (_active.TryGetValue(variant, out var known)) return known;
        EditorColorTheme theme;
        if (_app.Contrast.IsOn) theme = FromResources(variant);
        else
        {
            var chosen = Find(SelectedId);
            theme = (chosen ?? FromResources(variant)).With(Overrides);
        }
        return _active[variant] = theme;
    }

    /// <summary>The theme the editors draw in without a choice: the brushes of the window theme (or of high contrast) and the palette of the light or dark theme.</summary>
    private EditorColorTheme FromResources(ThemeVariant variant)
    {
        var basis = variant == ThemeVariant.Dark ? EditorColorTheme.Dark : EditorColorTheme.Light;
        Color Pick(string key, string surface) => _app.TryFindResource(key, variant, out var value) && value is ISolidColorBrush brush ? brush.Color : basis[surface];
        var colors = basis.Colors.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        colors[EditorColorTheme.Background] = colors[EditorColorTheme.Minimap] = Pick("BgEditor", EditorColorTheme.Background);
        colors[EditorColorTheme.Foreground] = Pick("EditorFg", EditorColorTheme.Foreground);
        colors[EditorColorTheme.CurrentLine] = Pick("CurrentLine", EditorColorTheme.CurrentLine);
        colors[EditorColorTheme.Selection] = Pick("SelectionBg", EditorColorTheme.Selection);
        colors[EditorColorTheme.LineNumbers] = Pick("LineNumbers", EditorColorTheme.LineNumbers);
        return new EditorColorTheme(EditorColorTheme.AutoId, Loc.Get(K.ColorThemeAuto), variant == ThemeVariant.Dark, colors, basis.Palette);
    }

    /// <summary>The settings, the custom themes or the high contrast changed: the editors draw again.</summary>
    public void Reload()
    {
        _active.Clear();
        _app.NotifyEditorColors();
    }

    // ---- custom themes ----

    private void LoadCustom()
    {
        Problems.Clear();
        var list = new List<EditorColorTheme>();
        if (Directory.Exists(Folder))
            foreach (var file in Directory.EnumerateFiles(Folder, "*.json").Order(StringComparer.OrdinalIgnoreCase))
            {
                try { list.Add(EditorColorTheme.Load(file, EditorColorTheme.UserPrefix + Path.GetFileNameWithoutExtension(file))); }
                catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException) { Problems.Add($"{Path.GetFileName(file)}: {e.Message}"); }
            }
        _custom = [.. list.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>A file name for a theme name: the characters a file system refuses become underscores.</summary>
    private static string FileNameOf(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        string file = new string([.. name.Trim().Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c)]).Trim('.', ' ');
        return file.Length == 0 ? "theme" : file;
    }

    /// <summary>Writes a theme into the custom folder under <paramref name="name"/> (replacing a custom theme of the same name) and returns its id.</summary>
    public string Save(EditorColorTheme theme, string name)
    {
        Directory.CreateDirectory(Folder);
        string file = FileNameOf(name);
        string path = Path.Combine(Folder, file + ".json");
        string temp = path + ".tmp";
        File.WriteAllText(temp, theme.Renamed(EditorColorTheme.UserPrefix + file, name.Trim()).ToVsCodeJson());
        File.Move(temp, path, overwrite: true);
        LoadCustom();
        Reload();
        return EditorColorTheme.UserPrefix + file;
    }

    /// <summary>Reads a TextMate or VS Code theme and keeps it as a custom theme; a name already taken gets a number. Throws <see cref="FormatException"/> or an I/O error.</summary>
    public string Import(string path)
    {
        var theme = EditorColorTheme.Load(path, "import");
        string name = theme.Name.Trim().Length > 0 ? theme.Name.Trim() : Path.GetFileNameWithoutExtension(path);
        string unique = name;
        for (int n = 2; File.Exists(Path.Combine(Folder, FileNameOf(unique) + ".json")); n++) unique = $"{name} ({n})";
        return Save(theme, unique);
    }

    /// <summary>Removes a custom theme; false when it is not one.</summary>
    public bool Delete(string id)
    {
        if (!id.StartsWith(EditorColorTheme.UserPrefix, StringComparison.Ordinal)) return false;
        string path = Path.Combine(Folder, id[EditorColorTheme.UserPrefix.Length..] + ".json");
        if (!File.Exists(path)) return false;
        File.Delete(path);
        LoadCustom();
        Reload();
        return true;
    }

    /// <summary>The theme as the editors of <paramref name="variant"/> show it now, named for a file: what Export writes and Save as keeps.</summary>
    public EditorColorTheme Effective(ThemeVariant variant)
    {
        var theme = Active(variant);
        return theme.Renamed(theme.Id, DisplayName(theme));
    }
}

/// <summary>What one editor control draws with: the active colour theme of its window, looked up by the variant the control is shown in.</summary>
internal static class EditorLook
{
    public static EditorColorTheme Of(StyledElement control)
    {
        var variant = control.ActualThemeVariant;
        if (Application.Current is App { EditorThemesOrNull: { } themes }) return themes.Active(variant);
        return variant == ThemeVariant.Dark ? EditorColorTheme.Dark : EditorColorTheme.Light;
    }

    public static SyntaxPalette PaletteOf(StyledElement control) => Of(control).Palette;
}
