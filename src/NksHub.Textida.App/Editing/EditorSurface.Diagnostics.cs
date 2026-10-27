using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Editing;

/// <summary>A finding a plugin published, as text offsets of the view: <see cref="Start"/> to <see cref="End"/> is underlined, <see cref="Message"/> shows when the pointer rests on it.</summary>
internal readonly record struct DiagnosticMark(int Start, int End, DiagnosticSeverity Severity, string Message);

/// <summary>Underlines for diagnostics: the core draws what plugins publish, a plugin never draws.</summary>
internal sealed partial class EditorSurface
{
    private DiagnosticLayer? _diagnosticLayer;

    /// <summary>The marks drawn now, in document order.</summary>
    public IReadOnlyList<DiagnosticMark> Diagnostics => _diagnosticLayer?.Marks ?? [];

    /// <summary>Replaces the underlines. Marks outside the text are left out; an empty one is drawn one character wide.</summary>
    public void SetDiagnostics(IEnumerable<DiagnosticMark> marks)
    {
        int length = Document.TextLength;
        var kept = marks.Where(m => m.Start >= 0 && m.Start <= length && m.End >= m.Start && m.End <= length)
            .Select(m => m.End == m.Start ? (m.Start < length ? m with { End = m.Start + 1 } : m with { Start = Math.Max(m.Start - 1, 0) }) : m)
            .OrderBy(m => m.Start).ToList();
        if (_diagnosticLayer is null)
        {
            if (kept.Count == 0) return;
            _diagnosticLayer = AddRenderer(new DiagnosticLayer());
            TextArea.TextView.PointerMoved += OnDiagnosticPointerMoved;
        }
        _diagnosticLayer.Marks = kept;
        if (kept.Count == 0) ToolTip.SetTip(TextArea.TextView, null);
        TextArea.TextView.InvalidateLayer(KnownLayer.Text);
    }

    /// <summary>The mark that covers <paramref name="offset"/>; the first one when several do.</summary>
    public DiagnosticMark? DiagnosticAt(int offset)
    {
        foreach (var m in Diagnostics)
            if (offset >= m.Start && offset < m.End) return m;
        return null;
    }

    private void OnDiagnosticPointerMoved(object? sender, PointerEventArgs e)
    {
        var view = TextArea.TextView;
        string? message = OffsetAt(e) is { } at && DiagnosticAt(at) is { } mark ? mark.Message : null;
        if (!Equals(ToolTip.GetTip(view), message)) ToolTip.SetTip(view, message);
    }

    private sealed class DiagnosticLayer : IBackgroundRenderer
    {
        public IReadOnlyList<DiagnosticMark> Marks { get; set; } = [];

        public KnownLayer Layer => KnownLayer.Text;

        private static readonly IPen Error = Pen(Color.FromRgb(0xC9, 0x4F, 0x4F));
        private static readonly IPen Warning = Pen(Color.FromRgb(0xC2, 0x8A, 0x2E));
        private static readonly IPen Information = Pen(Color.FromRgb(0x4A, 0x86, 0xC5));
        private static readonly IPen Hint = Pen(Color.FromRgb(0x8A, 0x8A, 0x8A));

        private static ImmutablePen Pen(Color color) => new(new ImmutableSolidColorBrush(color, 0.9), 1);

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (Marks.Count == 0 || !textView.VisualLinesValid || textView.VisualLines.Count == 0) return;
            int from = textView.VisualLines[0].FirstDocumentLine.Offset, to = textView.VisualLines[^1].LastDocumentLine.EndOffset;
            foreach (var mark in Marks)
            {
                if (mark.End < from || mark.Start > to) continue;
                var pen = mark.Severity switch { DiagnosticSeverity.Error => Error, DiagnosticSeverity.Warning => Warning, DiagnosticSeverity.Information => Information, _ => Hint };
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, new TextSegment { StartOffset = mark.Start, EndOffset = mark.End }))
                    DrawWave(drawingContext, pen, rect.Left, rect.Right, rect.Bottom - 1);
            }
        }

        /// <summary>A zigzag two pixels high under a stretch of text.</summary>
        private static void DrawWave(DrawingContext dc, IPen pen, double left, double right, double y)
        {
            if (right - left < 1) right = left + 1;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                bool up = false;
                g.BeginFigure(new Point(left, y), false);
                for (double x = left + 2; x < right + 2; x += 2)
                {
                    g.LineTo(new Point(Math.Min(x, right), up ? y : y - 2));
                    up = !up;
                }
                g.EndFigure(false);
            }
            dc.DrawGeometry(null, pen, geometry);
        }
    }
}
