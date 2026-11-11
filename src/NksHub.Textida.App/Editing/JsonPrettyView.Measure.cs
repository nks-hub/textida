using System.Diagnostics;

namespace NksHub.Textida.App.Editing;

internal sealed partial class JsonPrettyView
{
    internal long RenderedAt { get; private set; }
    internal double RenderMs { get; private set; }

    private void MarkRendered(long start)
    {
        RenderedAt = Stopwatch.GetTimestamp();
        RenderMs = Stopwatch.GetElapsedTime(start, RenderedAt).TotalMilliseconds;
    }
}
