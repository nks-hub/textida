using System.Diagnostics;
using NksHub.Textida.App.Editing;

namespace NksHub.Textida.App.Startup;

internal sealed partial class PagedMeasure
{
    private DesktopFrameProbe? desktopProbe;

    private async Task<DesktopFrame?> SetupDesktopAsync(JsonPrettyView view, long openedAt)
    {
        if (Env("TEXTIDA_MEASURE_PRESENT") != "1") return null;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Desktop sampling requires Windows");
        using var process = Process.GetCurrentProcess();
        if (process.SessionId == 0) throw new InvalidOperationException("Desktop sampling requires an interactive session");
        nint hwnd = window.TryGetPlatformHandle()?.Handle ?? 0;
        if (hwnd == 0) throw new InvalidOperationException("The measured window has no native handle");
        desktopProbe = new DesktopFrameProbe(hwnd, view);
        var task = desktopProbe.Begin();
        desktopProbe.Release();
        var seen = await task;
        Log("desktop-probe", ("method", "BitBlt(GetDC(NULL)) to a top-down 32-bit DIB, GdiFlush, two frame-specific colors"),
            ("scope", "first observed interactive VM desktop pixels; upper bound, not physical display or RDP transport"),
            ("session", process.SessionId), ("x1", desktopProbe.Points.First.X), ("y1", desktopProbe.Points.First.Y),
            ("x2", desktopProbe.Points.Second.X), ("y2", desktopProbe.Points.Second.Y),
            ("status", seen.Status), ("openObservedMs", seen.Since(openedAt) ?? -1), ("readMs", seen.ReadMs), ("pollMs", seen.PollMs));
        return seen;
    }

    private static void RequireDesktop(DesktopFrame? seen)
    {
        if (seen is { Status: not "seen" } failure)
            throw new InvalidOperationException($"Desktop frame {failure.Marker:X6} was not observed: {failure.Status}");
    }
}
