using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Editing;

/// <summary>Left margin of the text editor that marks the lines with a bookmark.</summary>
internal sealed class BookmarkMargin : AbstractMargin
{
    public const double MarginWidth = 14;
    private readonly Bookmarks _bookmarks;

    public BookmarkMargin(Bookmarks bookmarks)
    {
        _bookmarks = bookmarks;
        bookmarks.Changed += () => InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(MarginWidth, 0);

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView is not null) oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null) newTextView.VisualLinesChanged += OnVisualLinesChanged;
        InvalidateVisual();
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        var view = TextView;
        if (view is null || !view.VisualLinesValid || !_bookmarks.HasAny) return;
        var marked = _bookmarks.Lines.ToHashSet();
        var brush = this.TryFindResource("Accent", ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.SteelBlue;
        foreach (var line in view.VisualLines)
        {
            if (!marked.Contains(line.FirstDocumentLine.LineNumber - 1)) continue;
            var text = line.TextLines[0];
            double top = line.GetTextLineVisualYPosition(text, VisualYPosition.LineTop) - view.VerticalOffset;
            context.DrawGeometry(brush, null, new EllipseGeometry(new Rect(3, top + (text.Height - 8) / 2, 8, 8)));
        }
    }
}
