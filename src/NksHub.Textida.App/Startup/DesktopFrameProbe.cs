using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using NksHub.Textida.App.Editing;

namespace NksHub.Textida.App.Startup;

internal readonly record struct DesktopFrame(uint Marker, long ObservedAt, double ReadMs, double PollMs, int Reads, string Status)
{
    internal double? Since(long start) => Status == "seen" && ObservedAt > 0 && ObservedAt >= start
        ? (ObservedAt - start) * 1000.0 / Stopwatch.Frequency : null;
}

// Reads the interactive desktop, not the window's backing bitmap. The result is an upper bound:
// the first completed read that found this frame's two marker colors, not physical monitor latency.
internal sealed partial class DesktopFrameProbe(nint hwnd, JsonPrettyView view)
{
    private uint marker = (uint)Random.Shared.Next(1, 0xFFFFFF);
    internal (PixelPoint First, PixelPoint Second) Points { get; } = view.MeasureMarkerPoints;

    internal Task<DesktopFrame> Begin(long? revision = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Desktop frame sampling requires Windows");
        uint next = marker = (marker + 1) & 0xFFFFFF;
        view.SetMeasureMarker(next, revision);
        if (view.MeasureMarkerPoints != Points)
            return Task.FromResult(new DesktopFrame(next, 0, 0, 0, 0, "window-moved"));
        long armedAt = Stopwatch.GetTimestamp();
        return Task.Run(() => Read(next, armedAt));
    }

    internal void Release() => view.ReleaseMeasureMarker();

    private DesktopFrame Read(uint expected, long start)
    {
        long previous = start;
        double readMs = 0, pollMs = 0;
        int reads = 0;
        DesktopFrame Result(string status, long observed = 0) => new(expected, observed, readMs, pollMs, reads, status);
        uint first = ColorRef(expected), second = ColorRef(expected ^ 0xFFFFFF);
        nint dc = GetDC(0);
        if (dc == 0) return Result("desktop-unavailable");
        try
        {
            while (Stopwatch.GetElapsedTime(start).TotalSeconds < 2)
            {
                long begin = Stopwatch.GetTimestamp();
                pollMs = Math.Max(pollMs, Stopwatch.GetElapsedTime(previous, begin).TotalMilliseconds);
                if (GetForegroundWindow() != hwnd) return Result("not-foreground");
                if (!Owned(Points.First) || !Owned(Points.Second)) return Result("occluded");
                uint a = GetPixel(dc, Points.First.X, Points.First.Y);
                uint b = GetPixel(dc, Points.Second.X, Points.Second.Y);
                long end = Stopwatch.GetTimestamp();
                readMs = Math.Max(readMs, Stopwatch.GetElapsedTime(begin, end).TotalMilliseconds);
                reads++;
                if (a == 0xFFFFFFFF || b == 0xFFFFFFFF) return Result("pixel-unavailable");
                if (a == first && b == second) return Result("seen", end);
                previous = end;
                Thread.Sleep(1);
            }
            return Result("timeout");
        }
        finally { ReleaseDC(0, dc); }
    }

    private bool Owned(PixelPoint point) => GetAncestor(WindowFromPoint(new NativePoint { X = point.X, Y = point.Y }), 2) == hwnd;

    private static uint ColorRef(uint rgb) => (rgb & 0xFF00) | ((rgb & 0xFF) << 16) | (rgb >> 16);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial uint GetPixel(nint dc, int x, int y);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial nint WindowFromPoint(NativePoint point);

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint hwnd, uint flags);
}
