using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace NksHub.Textida.AvaloniaEditAdapter;

public sealed class SyntaxPalette
{
    private readonly IReadOnlyDictionary<string, IBrush> brushes;

    public SyntaxPalette(IReadOnlyDictionary<string, Color> colors) =>
        brushes = colors.ToDictionary(pair => pair.Key, pair => (IBrush)new ImmutableSolidColorBrush(pair.Value), StringComparer.Ordinal);

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

    private static SyntaxPalette Create(bool dark)
    {
        var colors = new Dictionary<string, Color>(StringComparer.Ordinal);
        void Add(string light, string night, params string[] kinds)
        {
            var color = Color.Parse(dark ? night : light);
            foreach (string kind in kinds) colors.Add(kind, color);
        }

        Add("#008000", "#6A9955", "comment");
        Add("#0451A5", "#CE9178", "string", "code", "regex");
        Add("#0000FF", "#569CD6", "keyword", "constant");
        Add("#800000", "#569CD6", "tag");
        Add("#C4321F", "#9CDCFE", "attribute");
        Add("#098658", "#B5CEA8", "number");
        Add("#267F99", "#4EC9B0", "type");
        Add("#795E26", "#DCDCAA", "function");
        Add("#001080", "#9CDCFE", "variable", "property");
        Add("#AF00DB", "#C586C0", "preprocessor", "meta");
        Add("#0000FF", "#569CD6", "heading", "link");
        Add("#C0392B", "#F47067", "log.error");
        Add("#9A6700", "#D29922", "log.warning");
        Add("#0451A5", "#79C0FF", "log.info");
        Add("#6E7781", "#8B949E", "log.debug");
        Add("#FFE58F", "#5C4B00", "log.highlight"); // background of the highlight expression of a log view
        return new SyntaxPalette(colors);
    }
}
