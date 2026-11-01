using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace NksHub.Textida.App.Editing;

/// <summary>The underlines of the spelling check (PZ.3.1): a layer of its own, so the findings of a plugin and of the spelling never replace each other.</summary>
internal sealed partial class EditorSurface
{
    private SpellLayer? _spellLayer;

    /// <summary>The misspelled stretches drawn now (text offsets of the view), in text order.</summary>
    public IReadOnlyList<(int Start, int End)> SpellMarks => _spellLayer?.Marks ?? [];

    public void SetSpellMarks(IEnumerable<(int Start, int End)> marks)
    {
        int length = Document.TextLength;
        var kept = marks.Where(m => m.Start >= 0 && m.End > m.Start && m.End <= length).OrderBy(m => m.Start).ToList();
        if (_spellLayer is null)
        {
            if (kept.Count == 0) return;
            _spellLayer = AddRenderer(new SpellLayer());
        }
        _spellLayer.Marks = kept;
        TextArea.TextView.InvalidateLayer(KnownLayer.Text);
    }

    private sealed class SpellLayer : IBackgroundRenderer
    {
        public IReadOnlyList<(int Start, int End)> Marks { get; set; } = [];

        public KnownLayer Layer => KnownLayer.Text;

        private static readonly IPen Pen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xD0, 0x4A, 0x4A), 0.85), 1, new ImmutableDashStyle([1, 2], 0));

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (Marks.Count == 0 || !textView.VisualLinesValid || textView.VisualLines.Count == 0) return;
            int from = textView.VisualLines[0].FirstDocumentLine.Offset, to = textView.VisualLines[^1].LastDocumentLine.EndOffset;
            foreach (var (start, end) in Marks)
            {
                if (end < from || start > to) continue;
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, new TextSegment { StartOffset = start, EndOffset = end }))
                    drawingContext.DrawLine(Pen, new Point(rect.Left, rect.Bottom - 1), new Point(rect.Right, rect.Bottom - 1));
            }
        }
    }
}
