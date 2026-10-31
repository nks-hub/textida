using NksHub.Textida.AvaloniaEditAdapter;

namespace NksHub.Textida.App.Editing;

/// <summary>Inlay hints and semantic token colours: the core draws what plugins publish, a plugin never draws.</summary>
internal sealed partial class EditorSurface
{
    private InlayHintGenerator? _inlays;
    private SemanticTokenColorizer? _semantic;

    /// <summary>The inlay hints shown now, by text offset.</summary>
    public IReadOnlyList<(int Offset, string Label)> InlayHints => _inlays?.Hints ?? [];

    /// <summary>The semantic tokens coloured now, by text offset, with the kind of syntax they are coloured as.</summary>
    public IReadOnlyList<(int Start, int End, string Kind)> SemanticTokens => _semantic?.Tokens ?? [];

    /// <summary>Replaces the inlay hints. A hint outside the text is left out.</summary>
    public void SetInlayHints(IEnumerable<(int Offset, string Label)> hints)
    {
        int length = Document.TextLength;
        var kept = hints.Where(h => h.Offset >= 0 && h.Offset <= length && h.Label.Length > 0).ToList();
        if (_inlays is null)
        {
            if (kept.Count == 0) return;
            _inlays = new InlayHintGenerator();
            TextArea.TextView.ElementGenerators.Add(_inlays);
        }
        _inlays.SetHints(kept);
        TextArea.TextView.Redraw();
    }

    /// <summary>Replaces the semantic tokens. A token outside the text is left out.</summary>
    public void SetSemanticTokens(IEnumerable<(int Start, int End, string Type)> tokens)
    {
        int length = Document.TextLength;
        var kept = tokens.Where(t => t.Start >= 0 && t.End <= length && t.End > t.Start).ToList();
        if (_semantic is null)
        {
            if (kept.Count == 0) return;
            _semantic = new SemanticTokenColorizer(() => _colorizer.Palette);
            TextArea.TextView.LineTransformers.Add(_semantic);
        }
        _semantic.SetTokens(kept);
        TextArea.TextView.Redraw();
    }
}
