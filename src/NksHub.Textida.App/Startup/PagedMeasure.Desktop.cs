using System.Diagnostics;
using Avalonia.Rendering.Composition;
using NksHub.Textida.App.Editing;

namespace NksHub.Textida.App.Startup;

internal sealed partial class PagedMeasure
{
    private DesktopFrameProbe? desktopProbe;
    private CompositionFrameTrace? compositionTrace;
    private JsonPrettyView? compositionView;
    private Action<uint, long>? compositionDrawn;

    private async Task<DesktopFrame?> SetupDesktopAsync(JsonPrettyView view, long openedAt)
    {
        string? mode = Env("TEXTIDA_MEASURE_PRESENT");
        if (mode is not ("1" or "external")) return null;
        if (view.RenderedAt >= openedAt) throw new InvalidOperationException("The opening frame marker must be armed before the first draw");
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Desktop sampling requires Windows");
        using var process = Process.GetCurrentProcess();
        if (process.SessionId == 0) throw new InvalidOperationException("Desktop sampling requires an interactive session");
        nint hwnd = window.TryGetPlatformHandle()?.Handle ?? 0;
        if (hwnd == 0) throw new InvalidOperationException("The measured window has no native handle");
        bool external = mode == "external";
        if (Env("TEXTIDA_MEASURE_COMPOSITION") == "1")
        {
            var compositor = ElementComposition.GetElementVisual(view)?.Compositor
                ?? throw new InvalidOperationException("The measured view has no compositor");
            var trace = compositionTrace = new CompositionFrameTrace(_log, samples.RunId);
            compositionView = view;
            compositionDrawn = (marker, draw) =>
            {
                if (trace.HasMarker(marker)) return;
                var batch = compositor.RequestCompositionBatchCommitAsync();
                trace.Record(marker, draw, batch.Processed, batch.Rendered);
            };
            view.MeasuredFrameDrawn += compositionDrawn;
            Log("composition-trace", ("csv", Path.GetFileName(trace.Path)),
                ("scope", "batch processed/rendered notifications only; not successful presentation"));
        }
        desktopProbe = new DesktopFrameProbe(hwnd, view, external);
        var task = desktopProbe.Begin();
        desktopProbe.Release();
        if (external) await Frames(2);
        var seen = await task;
        Log("desktop-probe", ("method", external ? "frame markers for an external desktop capture; no internal pixel read"
                : "BitBlt(GetDC(NULL)) to a top-down 32-bit DIB, GdiFlush, two frame-specific colors"),
            ("markerBinding", "draw-state"),
            ("openMarkerArmedBeforeDraw", true),
            ("viewportScope", "fixed geometry and options; one fold target; no DPI or font changes"),
            ("requestedWin32Composition", Env("TEXTIDA_MEASURE_WIN32_COMPOSITION") ?? "platform-default"),
            ("transparency", window.ActualTransparencyLevel.ToString()),
            ("scope", external ? "presentation is unverified until joined to external capture timestamps"
                : "first observed interactive VM desktop pixels; upper bound, not physical display or RDP transport"),
            ("session", process.SessionId), ("x1", desktopProbe.Points.First.X), ("y1", desktopProbe.Points.First.Y),
            ("x2", desktopProbe.Points.Second.X), ("y2", desktopProbe.Points.Second.Y),
            ("status", seen.Status), ("openObservedMs", seen.Since(openedAt) ?? -1), ("readMs", seen.ReadMs), ("pollMs", seen.PollMs));
        return seen;
    }

    private static void RequireDesktop(DesktopFrame? seen)
    {
        if (seen is { Status: not ("seen" or "external") } failure)
            throw new InvalidOperationException($"Desktop frame {failure.Marker:X6} was not observed: {failure.Status}");
    }

    private async Task WriteCompositionTraceAsync()
    {
        if (compositionView is not null) compositionView.MeasuredFrameDrawn -= compositionDrawn;
        if (compositionTrace is not { } trace) return;
        compositionTrace = null;
        await trace.WriteAsync(TimeSpan.FromSeconds(2));
    }
}
