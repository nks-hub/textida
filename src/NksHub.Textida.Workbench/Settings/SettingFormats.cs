using NksHub.Textida.Workbench.Contributions;

namespace NksHub.Textida.Workbench.Settings;

/// <summary>The control the settings window gives a setting.</summary>
public enum SettingControl
{
    Toggle,
    Select,
    MultiSelect,
    Number,
    Color,
    Font,
    FontList,
    File,
    Folder,
    Command,
    List,
    Encoding,
    Shortcut,
    IconSet,
    /// <summary>The colour theme of the editor: the list of themes with import, export and save as custom.</summary>
    ColorTheme,
    /// <summary>The colours of the editor areas and the token kinds over the chosen colour theme, each with a colour picker.</summary>
    ThemeColors,
    /// <summary>Free text the setting declares as such (<c>"format": "text"</c>): a pattern, a template, an address.</summary>
    Text,
    /// <summary>A string setting that says nothing about its content: a plain text box. Built-in settings never land here (a test checks it).</summary>
    FallbackText,
}

/// <summary>The values of <see cref="SettingDefinition.Format"/> and how they map to a control.</summary>
public static class SettingFormats
{
    public const string Text = "text";
    public const string Font = "font";
    public const string FontList = "font-list";
    public const string Color = "color";
    public const string File = "file";
    public const string Folder = "folder";
    public const string Command = "command";
    public const string List = "list";
    public const string GlobList = "glob-list";
    public const string Encoding = "encoding";
    public const string Shortcut = "shortcut";
    public const string IconSet = "icon-set";
    public const string Multi = "multi";
    public const string ColorTheme = "color-theme";
    public const string ThemeColors = "theme-colors";

    public static IReadOnlyList<string> All { get; } = [Text, Font, FontList, Color, File, Folder, Command, List, GlobList, Encoding, Shortcut, IconSet, Multi, ColorTheme, ThemeColors];

    public static bool IsList(string? format) => format is List or GlobList or FontList or Multi;

    public static string SeparatorOf(SettingDefinition definition) => string.IsNullOrEmpty(definition.Separator) ? ";" : definition.Separator;

    /// <summary>The items of a list value: split at the separator (and at line breaks), trimmed, empty ones dropped.</summary>
    public static List<string> Split(SettingDefinition definition, string value) =>
        [.. value.Split([SeparatorOf(definition), "\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>The value of a list: the items joined by the separator, with a space after a comma as the built-in defaults are written.</summary>
    public static string Join(SettingDefinition definition, IEnumerable<string> items)
    {
        string separator = SeparatorOf(definition);
        return string.Join(separator == "," ? ", " : separator, items.Select(i => i.Trim()).Where(i => i.Length > 0));
    }

    public static SettingControl ControlOf(SettingDefinition definition) => definition.Type switch
    {
        "boolean" => SettingControl.Toggle,
        "integer" or "number" => SettingControl.Number,
        _ => definition.Format switch
        {
            Multi when definition.Values is { Count: > 0 } => SettingControl.MultiSelect,
            _ when definition.Values is { Count: > 0 } => SettingControl.Select,
            Text => SettingControl.Text,
            Font => SettingControl.Font,
            FontList => SettingControl.FontList,
            Color => SettingControl.Color,
            File => SettingControl.File,
            Folder => SettingControl.Folder,
            Command => SettingControl.Command,
            List or GlobList => SettingControl.List,
            Encoding => SettingControl.Encoding,
            Shortcut => SettingControl.Shortcut,
            IconSet => SettingControl.IconSet,
            ColorTheme => SettingControl.ColorTheme,
            ThemeColors => SettingControl.ThemeColors,
            _ => SettingControl.FallbackText,
        },
    };

    /// <summary>What is wrong with the format of a declaration, or null.</summary>
    public static string? Problem(SettingDefinition s)
    {
        if (s.Format is null) return null;
        if (!All.Contains(s.Format)) return $"Setting '{s.Key}' has an unknown format '{s.Format}'.";
        if (s.Type != "string") return $"Setting '{s.Key}': a format is only allowed for strings.";
        if (s.Format == Multi && s.Values is not { Count: > 0 }) return $"Setting '{s.Key}': the format multi needs an enum.";
        return null;
    }
}
