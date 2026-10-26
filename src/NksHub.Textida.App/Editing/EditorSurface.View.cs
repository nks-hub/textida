using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using System.Globalization;

namespace NksHub.Textida.App.Editing;

/// <summary>View switches of the editor surface (line numbers, hidden characters, margin, links, wrapping) and the minimap host.</summary>
internal sealed partial class EditorSurface : IMinimapHost
{
    /// <summary>A document with a line longer than this is never wrapped automatically: laying out one giant line as many rows would stall the view.</summary>
    public const int WrapLineLimit = 50_000;

    private LineEndingRenderer? _lineEndings;
    private RightMarginRenderer? _margin;
    private TextDocument? _guarded;
    private ITextSourceVersion? _giantVersion;
    private bool _giant;
    private Action? _mapChanged;
    private (long First, long Count) _mapVisible = (0, 1);

    /// <summary>Word wrap is switched on in the settings but not applied, because the document has a line longer than <see cref="WrapLineLimit"/>.</summary>
    public bool WrapSuppressed { get; private set; }

    private void InitViewOptions()
    {
        DocumentChanged += (_, _) =>
        {
            GuardWrap();
            ApplyWrap(Settings);
        };
        GuardWrap();
        var view = TextArea.TextView;
        view.VisualLinesChanged += (_, _) => _mapChanged?.Invoke();
        view.ScrollOffsetChanged += (_, _) => _mapChanged?.Invoke();
    }

    private void ApplyViewOptions(EditorOptions o)
    {
        ShowLineNumbers = o.LineNumbers;
        Options.HighlightCurrentLine = o.CurrentLine;
        Options.EnableHyperlinks = o.Links;
        Options.EnableEmailHyperlinks = false;
        _lineEndings ??= AddRenderer(new LineEndingRenderer(this));
        _margin ??= AddRenderer(new RightMarginRenderer(this));
        _lineEndings.Enabled = o.ShowLineEndings;
        _margin.Column = o.RightMargin;
        TextArea.TextView.InvalidateLayer(KnownLayer.Text);
        TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        ApplyWrap(o);
    }

    private void ApplyWrap(EditorOptions o)
    {
        WrapSuppressed = o.WordWrap && HasGiantLine();
        WordWrap = o.WordWrap && !WrapSuppressed;
    }

    private T AddRenderer<T>(T renderer) where T : IBackgroundRenderer
    {
        TextArea.TextView.BackgroundRenderers.Add(renderer);
        return renderer;
    }

    /// <summary>Whether a line is longer than <see cref="WrapLineLimit"/>; the scan runs once per version of the text.</summary>
    private bool HasGiantLine()
    {
        var version = Document.Version;
        if (_giantVersion is not null && version.BelongsToSameDocumentAs(_giantVersion) && version.CompareAge(_giantVersion) == 0) return _giant;
        _giantVersion = version;
        _giant = false;
        foreach (var line in Document.Lines)
            if (line.Length > WrapLineLimit)
            {
                _giant = true;
                break;
            }
        return _giant;
    }

    /// <summary>Watches the document the surface shows: an edit that makes a line giant turns wrapping off at once.</summary>
    private void GuardWrap()
    {
        if (ReferenceEquals(_guarded, Document)) return;
        if (_guarded is not null) _guarded.Changed -= OnTextChangedForWrap;
        _guarded = Document;
        if (_guarded is not null) _guarded.Changed += OnTextChangedForWrap;
    }

    private void OnTextChangedForWrap(object? sender, DocumentChangeEventArgs e)
    {
        if (!WordWrap) return;
        var line = Document.GetLineByOffset(Math.Min(e.Offset, Document.TextLength));
        for (; line is not null && line.Offset <= e.Offset + e.InsertionLength; line = line.NextLine)
        {
            if (line.Length <= WrapLineLimit) continue;
            WrapSuppressed = true;
            WordWrap = false;
            return;
        }
    }

    // ---- minimap ----

    event Action? IMinimapHost.MapChanged
    {
        add => _mapChanged += value;
        remove => _mapChanged -= value;
    }

    long IMinimapHost.LineCount => Document.LineCount;

    string? IMinimapHost.LineText(long line)
    {
        if (line < 0 || line >= Document.LineCount) return null;
        var textLine = Document.GetLineByNumber((int)line + 1);
        return Document.GetText(textLine.Offset, Math.Min(textLine.Length, Minimap.MaxChars));
    }

    (long First, long Count) IMinimapHost.Visible
    {
        get
        {
            var view = TextArea.TextView;
            if (view.VisualLinesValid && view.VisualLines is { Count: > 0 } lines)
            {
                long first = lines[0].FirstDocumentLine.LineNumber - 1;
                _mapVisible = (first, lines[^1].LastDocumentLine.LineNumber - 1 - first + 1);
            }
            return _mapVisible;
        }
    }

    void IMinimapHost.ScrollToLine(long line) =>
        ScrollToVerticalOffset(TextArea.TextView.GetVisualTopByDocumentLine((int)Math.Clamp(line + 1, 1, Document.LineCount)));
}

/// <summary>Draws CR, LF or CRLF after the last character of each line; the document is not touched.</summary>
internal sealed class LineEndingRenderer(TextEditor owner) : IBackgroundRenderer
{
    public bool Enabled { get; set; }

    public KnownLayer Layer => KnownLayer.Text;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!Enabled || !textView.VisualLinesValid || textView.Document is not { } document) return;
        var color = owner.Foreground is ISolidColorBrush brush ? brush.Color : Colors.Gray;
        var dim = new ImmutableSolidColorBrush(color, 0.4);
        var typeface = new Typeface(owner.FontFamily);
        double size = Math.Max(6, owner.FontSize * 0.7);
        foreach (var line in textView.VisualLines)
        {
            var last = line.LastDocumentLine;
            if (last.DelimiterLength == 0) continue;
            string? name = document.GetText(last.EndOffset, last.DelimiterLength) switch { "\r\n" => "CRLF", "\r" => "CR", "\n" => "LF", _ => null };
            if (name is null) continue;
            var textLine = line.TextLines[^1];
            double x = line.GetTextLineVisualXPosition(textLine, line.VisualLength) - textView.ScrollOffset.X;
            double y = line.GetTextLineVisualYPosition(textLine, VisualYPosition.TextTop) - textView.ScrollOffset.Y;
            var text = new FormattedText(name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, dim);
            drawingContext.DrawText(text, new Point(x + textView.WideSpaceWidth * 0.5, y + Math.Max(0, (textLine.Height - text.Height) / 2)));
        }
    }
}

/// <summary>Draws a thin vertical line after a given number of columns; the column is counted in the width of a space.</summary>
internal sealed class RightMarginRenderer(TextEditor owner) : IBackgroundRenderer
{
    public int Column { get; set; }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Column <= 0) return;
        double x = Math.Round(textView.WideSpaceWidth * Column - textView.ScrollOffset.X) + 0.5;
        if (x < 0 || x > textView.Bounds.Width) return;
        var color = owner.Foreground is ISolidColorBrush brush ? brush.Color : Colors.Gray;
        drawingContext.DrawLine(new Pen(new ImmutableSolidColorBrush(color, 0.2), 1), new Point(x, 0), new Point(x, textView.Bounds.Height));
    }
}
