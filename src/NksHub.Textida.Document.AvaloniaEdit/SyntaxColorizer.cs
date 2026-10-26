using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.AvaloniaEditAdapter;

/// <summary>Paints prepared UTF-16 tokens on the UI thread. Tokenization never runs during line construction.</summary>
public sealed class SyntaxColorizer : DocumentColorizingTransformer, IDisposable
{
    private readonly TextView view;
    private Dictionary<long, Token[]> lines = [];
    private TextDocument? document;
    private SyntaxPalette palette;
    private bool disposed;

    public SyntaxColorizer(TextView view, SyntaxPalette? palette = null)
    {
        this.view = view;
        this.palette = palette ?? SyntaxPalette.Light;
    }

    public SyntaxPalette Palette
    {
        get => palette;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentNullException.ThrowIfNull(value);
            palette = value;
            view.Redraw();
        }
    }

    public void BindDocument(TextDocument? value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (document is not null) document.Changed -= OnChanged;
        document = value;
        if (document is not null) document.Changed += OnChanged;
        ClearTokens();
    }

    /// <summary>Replaces the prepared viewport only when its captured text version is still current.</summary>
    public bool SetTokens(ITextSourceVersion version, IReadOnlyList<LineTokens> tokens)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(tokens);
        if (document is null || !version.BelongsToSameDocumentAs(document.Version) || version.CompareAge(document.Version) != 0)
            return false;

        var replacement = new Dictionary<long, Token[]>();
        foreach (var line in tokens)
        {
            if (line.Line < 0 || line.Line >= document.LineCount)
                throw new ArgumentOutOfRangeException(nameof(tokens), "A token line is outside the document.");
            var textLine = document.GetLineByNumber(checked((int)line.Line + 1));
            var copy = line.Tokens.ToArray();
            foreach (var token in copy)
                if (token.Start < 0 || token.Length < 0 || (long)token.Start + token.Length > textLine.Length)
                    throw new ArgumentOutOfRangeException(nameof(tokens), "A token is outside its line.");
            replacement.Add(line.Line, copy);
        }
        lines = replacement;
        view.Redraw();
        return true;
    }

    public void ClearTokens()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lines.Clear();
        view.Redraw();
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (disposed || !ReferenceEquals(CurrentContext.Document, document) || !lines.TryGetValue(line.LineNumber - 1, out var tokens))
            return;
        foreach (var token in tokens)
        {
            var brush = palette.GetBrush(token.Kind);
            if (brush is null || token.Length == 0) continue;
            ChangeLinePart(line.Offset + token.Start, line.Offset + token.End,
                element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }

    private void OnChanged(object? sender, DocumentChangeEventArgs e) => ClearTokens();

    public void Dispose()
    {
        if (disposed) return;
        if (document is not null) document.Changed -= OnChanged;
        document = null;
        lines.Clear();
        disposed = true;
        view.LineTransformers.Remove(this);
        view.Redraw();
    }
}
