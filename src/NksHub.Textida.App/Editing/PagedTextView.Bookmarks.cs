using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace NksHub.Textida.App.Editing;

/// <summary>Bookmark markers in the gutter of the paged surface.</summary>
internal sealed partial class PagedTextView
{
    private void RepaintBookmarks() => _canvas.InvalidateVisual();

    internal void DrawBookmarks(DrawingContext context, long top, double lineHeight, int visible)
    {
        if (!_doc.Bookmarks.HasAny) return;
        var brush = this.TryFindResource("Accent", ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.SteelBlue;
        foreach (long line in _doc.Bookmarks.Lines)
            if (line >= top && line < top + visible)
                context.DrawGeometry(brush, null, new EllipseGeometry(new Rect(3, (line - top) * lineHeight + (lineHeight - 8) / 2, 8, 8)));
    }
}
