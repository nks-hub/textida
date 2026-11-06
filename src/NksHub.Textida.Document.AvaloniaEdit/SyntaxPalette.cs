using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Rendering;

namespace NksHub.Textida.AvaloniaEditAdapter;

/// <summary>Bold and italic of a token kind, as a colour theme declares them.</summary>
[Flags]
public enum TokenStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
}

public sealed class SyntaxPalette
{
    private readonly IReadOnlyDictionary<string, IBrush> brushes;
    private readonly IReadOnlyDictionary<string, TokenStyle> styles;

    public SyntaxPalette(IReadOnlyDictionary<string, Color> colors, IReadOnlyDictionary<string, TokenStyle>? styles = null)
    {
        brushes = colors.ToDictionary(pair => pair.Key, pair => (IBrush)new ImmutableSolidColorBrush(pair.Value), StringComparer.Ordinal);
        this.styles = styles ?? new Dictionary<string, TokenStyle>();
    }

    public static SyntaxPalette Light { get; } = Create(false);
    public static SyntaxPalette Dark { get; } = Create(true);

    /// <summary>Unknown kinds retain the editor foreground; dotted kinds inherit their nearest known parent.</summary>
    public IBrush? GetBrush(string kind)
    {
        while (true)
        {
            if (brushes.TryGetValue(kind, out var brush)) return brush;
            int dot = kind.LastIndexOf('.');
            if (dot < 0) return null;
            kind = kind[..dot];
        }
    }

    /// <summary>Bold and italic of a kind; a dotted kind takes the style of the nearest kind the palette colours, as <see cref="GetBrush"/> takes its colour.</summary>
    public TokenStyle GetStyle(string kind)
    {
        if (styles.Count == 0) return TokenStyle.None;
        while (true)
        {
            if (brushes.ContainsKey(kind)) return styles.GetValueOrDefault(kind);
            int dot = kind.LastIndexOf('.');
            if (dot < 0) return TokenStyle.None;
            kind = kind[..dot];
        }
    }

    /// <summary>Makes the text of an editor element bold or italic as <paramref name="style"/> asks; what the style leaves out keeps the face of the editor.</summary>
    public static void ApplyStyle(VisualLineElementTextRunProperties properties, TokenStyle style)
    {
        if (style == TokenStyle.None) return;
        var face = properties.Typeface;
        properties.SetTypeface(new Typeface(face.FontFamily, style.HasFlag(TokenStyle.Italic) ? FontStyle.Italic : face.Style, style.HasFlag(TokenStyle.Bold) ? FontWeight.Bold : face.Weight, face.Stretch));
    }

    private static SyntaxPalette Create(bool dark)
    {
        var colors = new Dictionary<string, Color>(StringComparer.Ordinal);
        void Add(string light, string night, params string[] kinds)
        {
            var color = Color.Parse(dark ? night : light);
            foreach (string kind in kinds) colors.Add(kind, color);
        }

        Add("#008000", "#6C9B57", "comment");
        Add("#0451A5", "#CE9178", "string", "code", "regex");
        Add("#0000FF", "#569CD6", "keyword", "constant");
        Add("#800000", "#569CD6", "tag");
        Add("#C4321F", "#9CDCFE", "attribute");
        Add("#077A50", "#B5CEA8", "number");
        Add("#1F7590", "#4EC9B0", "type");
        Add("#795E26", "#DCDCAA", "function");
        Add("#001080", "#9CDCFE", "variable", "property");
        Add("#AF00DB", "#C586C0", "preprocessor", "meta");
        Add("#0000FF", "#569CD6", "heading", "link");
        Add("#C0392B", "#F47067", "log.error");
        Add("#8F5E00", "#D29922", "log.warning");
        Add("#0451A5", "#79C0FF", "log.info");
        Add("#656E77", "#8B949E", "log.debug");
        Add("#FFE58F", "#5C4B00", "log.highlight"); // background of the highlight expression of a log view
        return new SyntaxPalette(colors);
    }
}
