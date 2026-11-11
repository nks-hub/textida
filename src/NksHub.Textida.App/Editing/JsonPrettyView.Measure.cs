using System.Diagnostics;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Editing;

internal sealed partial class JsonPrettyView
{
    private (long Revision, long Line, long Part, int Column, long Top, double Scroll, int Folds, long Expanded, Pos? Anchor, ByteRange? Mark)? drawn;

    // A later scrollbar repaint must not replace the first frame that already showed the new text.
    internal long RenderedAt { get; private set; }
    internal long LastRenderedAt { get; private set; }
    internal double RenderMs { get; private set; }
    internal double HorizontalScroll => _hScroll;
    internal event Action<long, long>? Drawn;

    private void MarkRendered(long start)
    {
        LastRenderedAt = Stopwatch.GetTimestamp();
        Drawn?.Invoke(start, LastRenderedAt);
        var state = (Doc.Revision, _caretLine, _caretPart, _caretColumn, _top, _hScroll, _folds.Count, _expanded, _anchor, _mark);
        if (drawn == state) return;
        drawn = state;
        RenderedAt = LastRenderedAt;
        RenderMs = Stopwatch.GetElapsedTime(start, RenderedAt).TotalMilliseconds;
    }
}
