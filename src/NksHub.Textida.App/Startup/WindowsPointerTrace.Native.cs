using System.Runtime.InteropServices;

namespace NksHub.Textida.App.Startup;

internal sealed partial class WindowsPointerTrace
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePointerInfo
    {
        public uint Type, Id, FrameId, Flags;
        public nint Device, Target;
        public NativePoint Pixel, Himetric, RawPixel, RawHimetric;
        public uint Time, History;
        public int InputData;
        public uint KeyStates;
        public ulong PerformanceCount;
        public uint ButtonChange;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int GetPointerInfo(uint pointerId, out NativePointerInfo info);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int GetPointerDeviceRects(nint device, out NativeRect pointerDeviceRect, out NativeRect displayRect);
}
