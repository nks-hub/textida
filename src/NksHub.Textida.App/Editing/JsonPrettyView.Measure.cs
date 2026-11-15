using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Editing;

internal sealed partial class JsonPrettyView
{
    private readonly record struct DrawState(long Revision, long Line, long Part, int Column, long Top, double Scroll,
        int Folds, long Expanded, Pos? Anchor, ByteRange? Mark);
    private DrawState? drawn;
    private DrawState CurrentDrawState => new(Doc.Revision, _caretLine, _caretPart, _caretColumn, _top, _hScroll,
        _folds.Count, _expanded, _anchor, _mark);

    // A later scrollbar repaint must not replace the first frame that already showed the new text.
    internal long RenderedAt { get; private set; }
    internal long LastRenderedAt { get; private set; }
    internal double RenderMs { get; private set; }
    internal double HorizontalScroll => _hScroll;
    internal event Action<long, long>? Drawn;
    internal event Action<uint, long>? MeasuredFrameDrawn;
    private (uint Id, IBrush First, IBrush Second, long? Revision, bool Ready)? measureMarker;
    private (uint Id, IBrush First, IBrush Second, DrawState State)? visibleMeasureMarker;

    internal (PixelPoint First, PixelPoint Second) MeasureMarkerPoints
    {
        get
        {
            if (_canvas.Bounds.Width < 20 || _canvas.Bounds.Height < 12)
                throw new InvalidOperationException("The JSON viewport is too small for a frame marker");
            return (_canvas.PointToScreen(new Point(_canvas.Bounds.Width - 12, _canvas.Bounds.Height - 6)),
                _canvas.PointToScreen(new Point(_canvas.Bounds.Width - 4, _canvas.Bounds.Height - 6)));
        }
    }

    internal void SetMeasureMarker(uint rgb, long? revision)
    {
        static IBrush Brush(uint value) => new ImmutableSolidColorBrush(Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value));
        measureMarker = (rgb, Brush(rgb), Brush(rgb ^ 0xFFFFFF), revision, false);
    }

    internal void ReleaseMeasureMarker()
    {
        if (measureMarker is not { } marker) return;
        measureMarker = marker with { Ready = true };
        _canvas.InvalidateVisual();
    }

    private void MarkRendered(long start)
    {
        LastRenderedAt = Stopwatch.GetTimestamp();
        Drawn?.Invoke(start, LastRenderedAt);
        var state = CurrentDrawState;
        if (visibleMeasureMarker is { } visible && visible.State == state)
            MeasuredFrameDrawn?.Invoke(visible.Id, LastRenderedAt);
        if (drawn == state) return;
        drawn = state;
        RenderedAt = LastRenderedAt;
        RenderMs = Stopwatch.GetElapsedTime(start, RenderedAt).TotalMilliseconds;
    }

    private sealed partial class Surface
    {
        public override void Render(DrawingContext context)
        {
            try
            {
                long start = Stopwatch.GetTimestamp();
                Draw(context);
                var state = owner.CurrentDrawState;
                if (owner.measureMarker is { } marker && (marker.Ready || marker.Revision is { } revision && owner.Doc.Revision >= revision))
                {
                    owner.visibleMeasureMarker = (marker.Id, marker.First, marker.Second, state);
                    owner.measureMarker = null;
                }
                // A later unmeasured change must not present the previous marker with different content.
                if (owner.visibleMeasureMarker is { } visible && visible.State == state)
                {
                    context.FillRectangle(visible.First, new Rect(Bounds.Width - 16, Bounds.Height - 10, 8, 8));
                    context.FillRectangle(visible.Second, new Rect(Bounds.Width - 8, Bounds.Height - 10, 8, 8));
                }
                owner.MarkRendered(start);
            }
            catch (IOException) { }
        }
    }
}
