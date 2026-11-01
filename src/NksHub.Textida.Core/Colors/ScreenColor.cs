using System.Numerics;
using System.Runtime.InteropServices;

namespace NksHub.Textida.Core.Colors;

/// <summary>Whether the colour of a pixel of the screen can be read here, and if not, why.</summary>
public enum ScreenColorSupport
{
    /// <summary>The platform gives the pixel under the pointer.</summary>
    Available,
    /// <summary>macOS: the program has no permission to record the screen (System Settings, Privacy and Security, Screen Recording). Asking once shows the system dialog.</summary>
    NeedsPermission,
    /// <summary>No way to read the screen: a Wayland session without X11 access, or a system without the call.</summary>
    Unavailable,
}

/// <summary>
/// The colour of the pixel under the pointer, read from the screen (PZ.4.1). The platform calls, checked one by one:
/// Windows <c>GetCursorPos</c> and <c>GetPixel</c> of the screen DC (no permission; the process is per-monitor DPI aware, so both use physical pixels);
/// macOS <c>CGEventGetLocation</c> and <c>CGDisplayCreateImageForRect</c> of the display under the pointer, which since macOS 10.15 returns the screen only when the program has the Screen Recording permission (<c>CGPreflightScreenCaptureAccess</c>,
/// <c>CGRequestScreenCaptureAccess</c>); the pixel is taken as the display gives it, without a conversion of the colour space;
/// Linux with X11 (or XWayland) <c>XQueryPointer</c> and <c>XGetImage</c> of the root window: a native Wayland window is not visible there and reads as black, and a session with Wayland only has no X display, so the call reports it unavailable.
/// Only the pixel is read; no screenshot is kept or saved.
/// </summary>
public static class ScreenColor
{
    public static ScreenColorSupport Support(out string reason)
    {
        reason = "";
        if (OperatingSystem.IsWindows()) return ScreenColorSupport.Available;
        if (OperatingSystem.IsMacOS())
        {
            try { return MacOs.Preflight() ? ScreenColorSupport.Available : ScreenColorSupport.NeedsPermission; }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { reason = e.Message; return ScreenColorSupport.Unavailable; }
        }
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            reason = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) ? "no display" : "Wayland without X11";
            return ScreenColorSupport.Unavailable;
        }
        return ScreenColorSupport.Available;
    }

    /// <summary>macOS: shows the system dialog that asks for the Screen Recording permission (once per program); true when it is granted already. Elsewhere true.</summary>
    public static bool RequestPermission()
    {
        if (!OperatingSystem.IsMacOS()) return true;
        try { return MacOs.Request(); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    /// <summary>The pixel under the pointer; false when it cannot be read (no permission, no display, the pointer outside every screen).</summary>
    public static bool TryRead(out Rgba color, out int x, out int y)
    {
        color = default;
        x = y = 0;
        try
        {
            if (OperatingSystem.IsWindows()) return Windows.Read(out color, out x, out y);
            if (OperatingSystem.IsMacOS()) return MacOs.Read(out color, out x, out y);
            return X11.Read(out color, out x, out y);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or SEHException or AccessViolationException) { return false; }
    }

    /// <summary>The colour of a pixel value of an X image by the masks of its channels (the value is 8 bits wide per channel after scaling).</summary>
    public static Rgba FromMasks(ulong pixel, ulong redMask, ulong greenMask, ulong blueMask)
    {
        static byte Channel(ulong pixel, ulong mask)
        {
            if (mask == 0) return 0;
            int shift = BitOperations.TrailingZeroCount(mask), bits = BitOperations.PopCount(mask);
            ulong value = (pixel & mask) >> shift, max = (1UL << bits) - 1;
            return (byte)(value * 255 / max);
        }
        return new Rgba(Channel(pixel, redMask), Channel(pixel, greenMask), Channel(pixel, blueMask));
    }

    private static class Windows
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Point { public int X, Y; }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr dc, int x, int y);

        public static bool Read(out Rgba color, out int x, out int y)
        {
            color = default;
            x = y = 0;
            if (!GetCursorPos(out var point)) return false;
            (x, y) = (point.X, point.Y);
            var dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return false;
            try
            {
                uint pixel = GetPixel(dc, point.X, point.Y);
                if (pixel == 0xFFFFFFFF) return false; // CLR_INVALID: the point is outside the screen
                color = new Rgba((byte)pixel, (byte)(pixel >> 8), (byte)(pixel >> 16));
                return true;
            }
            finally { ReleaseDC(IntPtr.Zero, dc); }
        }
    }

    private static class MacOs
    {
        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        [StructLayout(LayoutKind.Sequential)]
        private struct CgPoint { public double X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct CgRect { public double X, Y, Width, Height; }

        [DllImport(CoreGraphics)] private static extern byte CGPreflightScreenCaptureAccess();
        [DllImport(CoreGraphics)] private static extern byte CGRequestScreenCaptureAccess();
        [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreate(IntPtr source);
        [DllImport(CoreGraphics)] private static extern CgPoint CGEventGetLocation(IntPtr evt);
        [DllImport(CoreGraphics)] private static extern int CGGetDisplaysWithPoint(CgPoint point, uint max, out uint display, out uint count);
        [DllImport(CoreGraphics)] private static extern IntPtr CGDisplayCreateImageForRect(uint display, CgRect rect);
        [DllImport(CoreGraphics)] private static extern IntPtr CGImageGetDataProvider(IntPtr image);
        [DllImport(CoreGraphics)] private static extern IntPtr CGDataProviderCopyData(IntPtr provider);
        [DllImport(CoreGraphics)] private static extern nuint CGImageGetBytesPerRow(IntPtr image);
        [DllImport(CoreGraphics)] private static extern nuint CGImageGetBitsPerPixel(IntPtr image);
        [DllImport(CoreGraphics)] private static extern uint CGImageGetBitmapInfo(IntPtr image);
        [DllImport(CoreGraphics)] private static extern void CGImageRelease(IntPtr image);
        [DllImport(CoreFoundation)] private static extern IntPtr CFDataGetBytePtr(IntPtr data);
        [DllImport(CoreFoundation)] private static extern nint CFDataGetLength(IntPtr data);
        [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr obj);

        public static bool Preflight() => CGPreflightScreenCaptureAccess() != 0;
        public static bool Request() => CGRequestScreenCaptureAccess() != 0;

        public static unsafe bool Read(out Rgba color, out int x, out int y)
        {
            color = default;
            x = y = 0;
            var evt = CGEventCreate(IntPtr.Zero);
            if (evt == IntPtr.Zero) return false;
            CgPoint point;
            try { point = CGEventGetLocation(evt); }
            finally { CFRelease(evt); }
            (x, y) = ((int)point.X, (int)point.Y);
            if (CGGetDisplaysWithPoint(point, 1, out uint display, out uint count) != 0 || count == 0) return false;
            var image = CGDisplayCreateImageForRect(display, new CgRect { X = point.X, Y = point.Y, Width = 1, Height = 1 }); // in the points of the global space; the image has the pixels of the display
            if (image == IntPtr.Zero) return false;
            try
            {
                if (CGImageGetBitsPerPixel(image) != 32) return false;
                var data = CGDataProviderCopyData(CGImageGetDataProvider(image));
                if (data == IntPtr.Zero) return false;
                try
                {
                    if (CFDataGetLength(data) < 4) return false;
                    byte* p = (byte*)CFDataGetBytePtr(data);
                    // the display gives 32 bits with the byte order little (kCGBitmapByteOrder32Little, 0x2000) and the alpha first: the bytes are B, G, R, A; in the big order they are A, R, G, B
                    bool little = (CGImageGetBitmapInfo(image) & 0x7000) == 0x2000;
                    color = little ? new Rgba(p[2], p[1], p[0]) : new Rgba(p[1], p[2], p[3]);
                    return true;
                }
                finally { CFRelease(data); }
            }
            finally { CGImageRelease(image); }
        }
    }

    private static class X11
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct XImage
        {
            public int Width, Height, XOffset, Format;
            public IntPtr Data;
            public int ByteOrder, BitmapUnit, BitmapBitOrder, BitmapPad, Depth, BytesPerLine, BitsPerPixel;
            public nuint RedMask, GreenMask, BlueMask;
        }

        [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport("libX11.so.6")] private static extern int XDefaultScreen(IntPtr display);
        [DllImport("libX11.so.6")] private static extern IntPtr XRootWindow(IntPtr display, int screen);
        [DllImport("libX11.so.6")] private static extern int XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int rootX, out int rootY, out int winX, out int winY, out uint mask);
        [DllImport("libX11.so.6")] private static extern IntPtr XGetImage(IntPtr display, IntPtr drawable, int x, int y, uint width, uint height, nuint planeMask, int format);
        [DllImport("libX11.so.6")] private static extern int XDestroyImage(IntPtr image);
        [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);

        public static unsafe bool Read(out Rgba color, out int x, out int y)
        {
            color = default;
            x = y = 0;
            var display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return false;
            try
            {
                var root = XRootWindow(display, XDefaultScreen(display));
                if (XQueryPointer(display, root, out _, out _, out x, out y, out _, out _, out _) == 0) return false;
                var image = XGetImage(display, root, x, y, 1, 1, nuint.MaxValue, 2); // ZPixmap
                if (image == IntPtr.Zero) return false;
                try
                {
                    var info = Marshal.PtrToStructure<XImage>(image);
                    if (info.BitsPerPixel != 32 && info.BitsPerPixel != 24) return false;
                    uint pixel = info.ByteOrder == 0 ? *(uint*)info.Data & (info.BitsPerPixel == 32 ? 0xFFFFFFFFu : 0xFFFFFFu) : System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(*(uint*)info.Data);
                    color = FromMasks(pixel, info.RedMask, info.GreenMask, info.BlueMask);
                    return true;
                }
                finally { XDestroyImage(image); }
            }
            finally { XCloseDisplay(display); }
        }
    }
}
