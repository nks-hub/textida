using Avalonia.Media;

namespace NksHub.Textida.App.Services;

/// <summary>The fonts installed in the system, sorted by name, and which of them draw every character with the same width (the ones an editor font is chosen from).</summary>
internal static class FontCatalog
{
    private static List<(string Name, bool Fixed)>? s_fonts;

    private static List<(string Name, bool Fixed)> Fonts() => s_fonts ??= [.. FontManager.Current.SystemFonts
        .Select(f => f.Name)
        .Where(n => !string.IsNullOrWhiteSpace(n))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.CurrentCultureIgnoreCase)
        .Select(n => (n, IsFixedWidth(n)))];

    /// <summary>The names of the installed fonts; with <paramref name="fixedWidthOnly"/> only those whose characters are all as wide as each other.</summary>
    public static IReadOnlyList<string> Families(bool fixedWidthOnly) => [.. Fonts().Where(f => !fixedWidthOnly || f.Fixed).Select(f => f.Name)];

    public static bool IsInstalled(string name) => Fonts().Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The family to draw a sample of a font in; a font that is not installed falls back to the default one.</summary>
    public static FontFamily SafeFamily(string name) => IsInstalled(name) ? new FontFamily(name) : FontFamily.Default;

    /// <summary>A font that says it is fixed pitch, or draws 'i' and 'W' with the same advance (some fixed-width fonts do not set the flag).</summary>
    internal static bool IsFixedWidth(string name)
    {
        if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(name), out var face)) return false;
        if (face.Metrics.IsFixedPitch) return true;
        var map = face.CharacterToGlyphMap;
        return map.TryGetGlyph('i', out var narrow) && map.TryGetGlyph('W', out var wide)
            && face.TryGetHorizontalGlyphAdvance(narrow, out var a) && face.TryGetHorizontalGlyphAdvance(wide, out var b) && a == b && a > 0;
    }
}
