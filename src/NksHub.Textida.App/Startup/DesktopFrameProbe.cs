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
        nint dc = GetDC(0);
        if (dc == 0) return Result("desktop-unavailable");
        nint memory = 0, bitmap = 0, previousBitmap = 0;
        try
        {
            int width = Points.Second.X - Points.First.X + 1;
            if (width is < 2 or > 256 || Points.Second.Y != Points.First.Y) return Result("unsupported-scale");
            memory = CreateCompatibleDC(dc);
            if (memory == 0) return Result("capture-unavailable");
            var info = new BitmapInfo { Size = 40, Width = width, Height = -1, Planes = 1, BitCount = 32 };
            bitmap = CreateDIBSection(dc, in info, 0, out nint bits, 0, 0);
            if (bitmap == 0 || bits == 0) return Result("capture-unavailable");
            previousBitmap = SelectObject(memory, bitmap);
            if (previousBitmap == 0 || previousBitmap == -1) return Result("capture-unavailable");
            while (Stopwatch.GetElapsedTime(start).TotalSeconds < 2)
            {
                long begin = Stopwatch.GetTimestamp();
                pollMs = Math.Max(pollMs, Stopwatch.GetElapsedTime(previous, begin).TotalMilliseconds);
                if (GetForegroundWindow() != hwnd) return Result("not-foreground");
                if (!Owned(Points.First) || !Owned(Points.Second)) return Result("occluded");
                if (!BitBlt(memory, 0, 0, width, 1, dc, Points.First.X, Points.First.Y, 0x40CC0020) || !GdiFlush())
                    return Result("capture-failed");
                uint a = (uint)Marshal.ReadInt32(bits) & 0xFFFFFF;
                uint b = (uint)Marshal.ReadInt32(bits, (width - 1) * 4) & 0xFFFFFF;
                long end = Stopwatch.GetTimestamp();
                readMs = Math.Max(readMs, Stopwatch.GetElapsedTime(begin, end).TotalMilliseconds);
                reads++;
                if (a == expected && b == (expected ^ 0xFFFFFF)) return Result("seen", end);
                previous = end;
                Thread.Sleep(1);
            }
            return Result("timeout");
        }
        finally
        {
            if (previousBitmap != 0 && previousBitmap != -1) SelectObject(memory, previousBitmap);
            if (bitmap != 0) DeleteObject(bitmap);
            if (memory != 0) DeleteDC(memory);
            ReleaseDC(0, dc);
        }
    }

    private bool Owned(PixelPoint point) => GetAncestor(WindowFromPoint(new NativePoint { X = point.X, Y = point.Y }), 2) == hwnd;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint dc, in BitmapInfo info, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint bitmap);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint handle);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GdiFlush();

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial nint WindowFromPoint(NativePoint point);

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint hwnd, uint flags);
}
