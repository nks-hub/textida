using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace NksHub.Textida.AvaloniaEditAdapter;

/// <summary>
/// Colours stretches of text by the type a language server gave them (<c>keyword</c>, <c>class</c>, <c>parameter</c>, ...) with the colours of the syntax palette, over the colours of the
/// syntax colorizer. Only the foreground changes; the text and its offsets are untouched.
/// </summary>
public sealed class SemanticTokenColorizer(Func<SyntaxPalette> palette) : DocumentColorizingTransformer
{
    private (int Start, int End, string Kind)[] _tokens = [];

    /// <summary>The tokens coloured now, by start.</summary>
    public IReadOnlyList<(int Start, int End, string Kind)> Tokens => _tokens;

    /// <summary>Replaces the tokens (document offsets, UTF-16 units; a type name that has no colour is left out); the caller redraws the view.</summary>
    public void SetTokens(IEnumerable<(int Start, int End, string Type)> tokens) =>
        _tokens = [.. tokens.Where(t => t.End > t.Start && KindOf(t.Type) is not null).Select(t => (t.Start, t.End, KindOf(t.Type)!)).OrderBy(t => t.Start)];

    /// <summary>The kind of syntax in the palette that a type of the Language Server Protocol is coloured as; null for a type that keeps the colour of the syntax.</summary>
    public static string? KindOf(string type) => type switch
    {
        "keyword" or "modifier" or "operator" => "keyword",
        "string" or "regexp" => "string",
        "number" => "number",
        "comment" => "comment",
        "namespace" or "type" or "class" or "enum" or "interface" or "struct" or "typeParameter" => "type",
        "function" or "method" or "macro" or "event" => "function",
        "variable" or "parameter" => "variable",
        "property" or "enumMember" => "property",
        "decorator" => "meta",
        _ => null,
    };

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_tokens.Length == 0) return;
        int low = 0, high = _tokens.Length;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (_tokens[middle].End <= line.Offset && _tokens[middle].Start < line.Offset) low = middle + 1;
            else high = middle;
        }
        // tokens do not overlap, but one may end before this line and the next ones start after it; walk from the first that can touch the line
        var colors = palette();
        for (int i = Math.Max(low - 1, 0); i < _tokens.Length && _tokens[i].Start <= line.EndOffset; i++)
        {
            var (start, end, kind) = _tokens[i];
            int from = Math.Max(start, line.Offset), to = Math.Min(end, line.EndOffset);
            if (to <= from || colors.GetBrush(kind) is not { } brush) continue;
            ChangeLinePart(from, to, element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }
}
