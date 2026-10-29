using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The colours of a high contrast theme. The system promises that the text colour reads on the window colour, the highlight text on the highlight, and the
/// hot light (the colour of links) on the window; nothing else is promised, so nothing else is paired.
/// </summary>
internal sealed record ContrastColors(Color Window, Color WindowText, Color GrayText, Color Highlight, Color HighlightText, Color HotLight)
{
    /// <summary>The "High Contrast Black" theme of Windows.</summary>
    public static ContrastColors Black { get; } = new(Rgb(0x000000), Rgb(0xFFFFFF), Rgb(0x3FF23F), Rgb(0x1AEBFF), Rgb(0x000000), Rgb(0xFFFF00));

    /// <summary>The "High Contrast White" theme of Windows.</summary>
    public static ContrastColors White { get; } = new(Rgb(0xFFFFFF), Rgb(0x000000), Rgb(0x600000), Rgb(0x37006E), Rgb(0xFFFFFF), Rgb(0x00009F));

    /// <summary>True when the window is dark; the controls of the theme are drawn from the dark or the light theme accordingly.</summary>
    public bool IsDark => Luminance(Window) < 0.5;

    private static Color Rgb(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>The colours the system has set, or null where it has no list of them to give (macOS increases contrast on its own).</summary>
    public static ContrastColors? FromSystem() => OperatingSystem.IsWindows() ? ReadWindows() : null;

    [SupportedOSPlatform("windows")]
    private static ContrastColors ReadWindows()
    {
        static Color Read(int index)
        {
            uint v = ContrastNative.GetSysColor(index); // 0x00BBGGRR
            return Color.FromRgb((byte)v, (byte)(v >> 8), (byte)(v >> 16));
        }
        const int Window = 5, Highlight = 13, HighlightText = 14, WindowText = 8, GrayText = 17, HotLight = 26;
        return new(Read(Window), Read(WindowText), Read(GrayText), Read(Highlight), Read(HighlightText), Read(HotLight));
    }

    /// <summary>Relative luminance as the WCAG defines it.</summary>
    public static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary>The contrast ratio of two colours, 1 to 21.</summary>
    public static double Ratio(Color a, Color b)
    {
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private Color Soft(double amount) => Color.FromRgb(
        (byte)Math.Round(Window.R + (WindowText.R - Window.R) * amount),
        (byte)Math.Round(Window.G + (WindowText.G - Window.G) * amount),
        (byte)Math.Round(Window.B + (WindowText.B - Window.B) * amount));

    /// <summary>
    /// The brushes of the shell in these colours. A fill that text is drawn on is the window colour, or a mix of window and text colour that keeps the text readable;
    /// a selection is the highlight, whose text is <c>SelectedFg</c>. Nothing is told by colour alone: borders, signs and markers stay.
    /// </summary>
    public IReadOnlyDictionary<string, Color> Brushes => new Dictionary<string, Color>
    {
        ["Bg0"] = Window, ["Bg1"] = Window, ["BgEditor"] = Window, ["ChromeBg"] = Window, ["TabRowBg"] = Window, ["TabBg"] = Window,
        ["InfoBg"] = Window, ["WarnBg"] = Window,
        ["BgHover"] = Soft(0.2), ["TabHoverBg"] = Soft(0.2), ["CurrentLine"] = Soft(0.12),
        ["Highlight"] = Soft(0.3), ["BracketMatch"] = Soft(0.3), ["DiffRemoved"] = Soft(0.3), ["DiffAdded"] = Soft(0.3),
        // the other occurrences of the selection: a lighter fill than the selection, always with an outline of the text colour, so they differ from it by shape and not by colour alone
        ["OccurrenceBg"] = Soft(0.2), ["OccurrenceBorder"] = WindowText,
        ["Border"] = WindowText, ["Fg"] = WindowText, ["FgMuted"] = WindowText, ["EditorFg"] = WindowText, ["LineNumbers"] = WindowText,
        ["Accent"] = HotLight, ["AccentFg"] = Window, ["Danger"] = HotLight, ["Good"] = HotLight,
        ["AccentSoft"] = Highlight, ["SelectedFg"] = HighlightText, ["SelectionBg"] = Soft(0.45),
        ["Scrim"] = Color.FromArgb(0xB3, Window.R, Window.G, Window.B),
        // the file tree: the kind is told by the shape of the icon, the Git state by the letter after the name, so no colour is needed to tell them apart
        ["FileFolder"] = WindowText, ["FileText"] = WindowText, ["FileCode"] = WindowText, ["FileMarkup"] = WindowText, ["FileData"] = WindowText,
        ["FileStyle"] = WindowText, ["FileImage"] = WindowText, ["FileArchive"] = WindowText, ["FileBinary"] = WindowText,
        ["GitModified"] = HotLight, ["GitNew"] = HotLight, ["GitDeleted"] = HotLight, ["GitConflict"] = HotLight, ["GitIgnored"] = GrayText,
    };
}

/// <summary>Colours the code needs by name, so a text that is red in one theme is not a fixed red that fails to read on the dark one or in high contrast.</summary>
internal static class ThemeColors
{
    /// <summary>The colour of errors in the theme in use. It reads on the window in the light and the dark theme, and in high contrast it is the colour the system uses for links.</summary>
    public static IBrush Danger => Of("Danger", Brushes.Firebrick);

    /// <summary>The fill behind the part of a text that matches a search: a highlight in the light and the dark theme, the lighter mix of window and text colour in high contrast.</summary>
    public static IBrush Mark => Of("Highlight", Brushes.Gold);

    /// <summary>The colour of the accent of the theme in use.</summary>
    public static IBrush Accent => Of("Accent", Brushes.Teal);

    /// <summary>The text colour of the theme in use.</summary>
    public static IBrush Text => Of("Fg", Brushes.Black);

    private static IBrush Of(string key, IBrush fallback) =>
        Application.Current is { } app && app.TryFindResource(key, app.ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback;
}

[SupportedOSPlatform("windows")]
internal static partial class ContrastNative
{
    [LibraryImport("user32.dll")]
    internal static partial uint GetSysColor(int index);
}

/// <summary>
/// Puts the colours of the system over the shell when the system asks for high contrast, and takes them off again. The brushes of the shell are replaced in the theme, so every
/// control that looks one up by name repaints; the controls of the theme are drawn from its dark or light variant, whichever the window colour is closer to.
/// </summary>
internal sealed class ContrastTheme(Application app)
{
    private readonly Dictionary<(ThemeVariant Variant, string Key), object?> _saved = [];
    private (ThemeVariant Variant, Color Accent, Color Region)? _palette;

    public bool IsOn => Colors is not null;
    public ContrastColors? Colors { get; private set; }

    /// <summary>Raised after the colours were put on or taken off.</summary>
    public event Action? Changed;

    public void Apply(ContrastColors colors)
    {
        Restore(raise: false);
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var (key, color) in colors.Brushes) SetBrush(variant, key, color);
        var control = colors.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        if (app.Styles.OfType<FluentTheme>().FirstOrDefault() is { } fluent && fluent.Palettes.TryGetValue(control, out var palette))
        {
            _palette = (control, palette.Accent, palette.RegionColor);
            palette.Accent = colors.HotLight;
            palette.RegionColor = colors.Window;
        }
        Colors = colors;
        app.RequestedThemeVariant = control;
        Changed?.Invoke();
    }

    public void Restore() => Restore(raise: true);

    private void Restore(bool raise)
    {
        if (Colors is null) return;
        foreach (var ((variant, key), entry) in _saved)
            if (Dictionary(variant) is { } dictionary) dictionary[key] = entry;
        _saved.Clear();
        if (_palette is { } palette0 && app.Styles.OfType<FluentTheme>().FirstOrDefault() is { } fluent && fluent.Palettes.TryGetValue(palette0.Variant, out var palette))
        {
            palette.Accent = palette0.Accent;
            palette.RegionColor = palette0.Region;
        }
        _palette = null;
        Colors = null;
        if (raise) Changed?.Invoke();
    }

    private IDictionary<object, object?>? Dictionary(ThemeVariant variant) =>
        app.Resources.ThemeDictionaries.TryGetValue(variant, out var provider) ? provider as IDictionary<object, object?> : null;

    /// <summary>
    /// Gives the brush its new colour. The brushes the theme holds are immutable, so the entry is replaced by a new brush, which tells the controls that look it
    /// up by name to draw again; the entry as it was is kept to put back.
    /// </summary>
    private void SetBrush(ThemeVariant variant, string key, Color color)
    {
        if (!app.Resources.ThemeDictionaries.TryGetValue(variant, out var provider) || provider is not IDictionary<object, object?> dictionary || provider is not IResourceNode node
            || !node.TryGetResource(key, null, out var value) || value is not IBrush) return;
        if (!_saved.ContainsKey((variant, key)))
        {
            dictionary.TryGetValue(key, out var entry);
            _saved[(variant, key)] = entry;
        }
        dictionary[key] = new SolidColorBrush(color);
    }
}
