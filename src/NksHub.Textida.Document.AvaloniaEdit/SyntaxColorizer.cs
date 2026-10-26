using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.AvaloniaEditAdapter;

/// <summary>
/// Paints prepared UTF-16 tokens on the UI thread. Tokenization never runs during line construction. An edit keeps the prepared tokens
/// (clamped to the line) until the caller replaces them, so the text does not flash plain between keystroke and new tokens.
/// </summary>
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
        document = value;
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
        var previous = lines;
        lines = replacement;
        RedrawChanged(previous, replacement);
        return true;
    }

    /// <summary>Rebuilds only the lines whose colors differ, so typing does not lay out the whole viewport again.</summary>
    private void RedrawChanged(Dictionary<long, Token[]> previous, Dictionary<long, Token[]> current)
    {
        var changed = new List<long>();
        foreach (long line in previous.Keys.Union(current.Keys))
        {
            previous.TryGetValue(line, out var before);
            current.TryGetValue(line, out var after);
            if (!(before ?? []).AsSpan().SequenceEqual(after ?? [])) changed.Add(line);
        }
        if (changed.Count > 256) view.Redraw();
        else
            foreach (long line in changed)
                if (line < document!.LineCount)
                {
                    var textLine = document.GetLineByNumber((int)line + 1);
                    view.Redraw(textLine.Offset, textLine.TotalLength);
                }
    }

    public void ClearTokens()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (lines.Count == 0) return;
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
            int end = Math.Min(token.End, line.Length);
            if (brush is null || token.Start >= end) continue;
            ChangeLinePart(line.Offset + token.Start, line.Offset + end,
                element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        document = null;
        lines.Clear();
        disposed = true;
        view.LineTransformers.Remove(this);
        view.Redraw();
    }
}
