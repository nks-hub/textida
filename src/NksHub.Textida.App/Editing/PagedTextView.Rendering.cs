using System.Diagnostics;
using Avalonia.Media;
using NksHub.Textida.Core;

namespace NksHub.Textida.App.Editing;

internal sealed partial class PagedTextView
{
    private sealed partial class LinesSurface
    {
        public override void Render(DrawingContext context)
        {
            if (!owner.IsEffectivelyVisible) return;
            long started = TypingTrace.Begin();
            long clock = Stopwatch.GetTimestamp();
            Draw(context);
            long ended = Stopwatch.GetTimestamp();
            TypingTrace.End("paged.render", started);
            var state = (owner.Doc.Revision, owner._caret, owner.TopLine, owner._hScroll, owner.WindowsPending);
            if (owner._drawn == state) return;
            owner._drawn = state;
            owner.RenderedAt = ended;
            owner.RenderMs = (ended - clock) * 1000.0 / Stopwatch.Frequency;
        }
    }
}
