using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NksHub.Textida.Accessibility.Host;

[StructLayout(LayoutKind.Sequential)]
internal struct Rect { public int Left, Top, Right, Bottom; }

[StructLayout(LayoutKind.Sequential)]
internal struct Point { public int X, Y; }

[StructLayout(LayoutKind.Sequential)]
internal struct Size { public int Cx, Cy; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WndClassEx
{
    public uint CbSize, Style;
    public nint WndProc;
    public int ClsExtra, WndExtra;
    public nint Instance, Icon, Cursor, Background;
    public char* MenuName;
    public char* ClassName;
    public nint IconSm;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Msg { public nint Hwnd; public uint Message; public nint WParam, LParam; public uint Time; public Point Pt; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PaintStruct
{
    public nint Hdc;
    public int Erase;
    public Rect Paint;
    public int Restore, IncUpdate;
    public fixed byte Reserved[32];
}

[StructLayout(LayoutKind.Sequential)]
internal struct TextMetric
{
    public int Height, Ascent, Descent, InternalLeading, ExternalLeading, AveCharWidth, MaxCharWidth, Weight, Overhang, AspectX, AspectY;
    public char FirstChar, LastChar, DefaultChar, BreakChar;
    public byte Italic, Underlined, StruckOut, PitchAndFamily, CharSet;
}

[SupportedOSPlatform("windows")]
internal static unsafe partial class WinNative
{
    public const uint WsOverlappedWindow = 0x00CF0000, WsChild = 0x40000000, WsVisible = 0x10000000, WsTabStop = 0x00010000, WsClipChildren = 0x02000000;
    public const uint WmDestroy = 2, WmSize = 5, WmSetFocus = 7, WmKillFocus = 8, WmPaint = 0xF, WmClose = 0x10, WmEraseBkgnd = 0x14,
        WmKeyDown = 0x100, WmChar = 0x102, WmTimer = 0x113, WmLButtonDown = 0x201, WmApp = 0x8000;
    public const int SwShow = 5, VkBack = 8, VkReturn = 13, VkShift = 0x10, VkControl = 0x11, VkEnd = 35, VkHome = 36, VkLeft = 37, VkUp = 38, VkRight = 39, VkDown = 40, VkDelete = 46;
    public const int CwUseDefault = unchecked((int)0x80000000);

    [LibraryImport("kernel32.dll")] public static partial nint GetModuleHandleW(nint name);
    [LibraryImport("kernel32.dll")] public static partial uint GetCurrentThreadId();
    [LibraryImport("user32.dll")] public static partial ushort RegisterClassExW(WndClassEx* cls);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [LibraryImport("user32.dll")] public static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool ShowWindow(nint hwnd, int cmd);
    [LibraryImport("user32.dll")] public static partial int GetMessageW(Msg* msg, nint hwnd, uint min, uint max);
    [LibraryImport("user32.dll")] public static partial int TranslateMessage(Msg* msg);
    [LibraryImport("user32.dll")] public static partial nint DispatchMessageW(Msg* msg);
    [LibraryImport("user32.dll")] public static partial void PostQuitMessage(int code);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetClientRect(nint hwnd, Rect* rect);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetWindowRect(nint hwnd, Rect* rect);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool MoveWindow(nint hwnd, int x, int y, int w, int h, [MarshalAs(UnmanagedType.Bool)] bool repaint);
    [LibraryImport("user32.dll")] public static partial nint SetFocus(nint hwnd);
    [LibraryImport("user32.dll")] public static partial nint GetFocus();
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool InvalidateRect(nint hwnd, Rect* rect, [MarshalAs(UnmanagedType.Bool)] bool erase);
    [LibraryImport("user32.dll")] public static partial nint BeginPaint(nint hwnd, PaintStruct* ps);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool EndPaint(nint hwnd, PaintStruct* ps);
    [LibraryImport("user32.dll")] public static partial nint GetDC(nint hwnd);
    [LibraryImport("user32.dll")] public static partial int ReleaseDC(nint hwnd, nint hdc);
    [LibraryImport("user32.dll")] public static partial nint LoadCursorW(nint instance, nint name);
    [LibraryImport("user32.dll")] public static partial nint SendMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
    [LibraryImport("user32.dll")] public static partial short GetKeyState(int key);
    [LibraryImport("user32.dll")] public static partial int FillRect(nint hdc, Rect* rect, nint brush);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool ClientToScreen(nint hwnd, Point* pt);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool ScreenToClient(nint hwnd, Point* pt);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool CreateCaret(nint hwnd, nint bitmap, int w, int h);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetCaretPos(int x, int y);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool ShowCaret(nint hwnd);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool DestroyCaret();
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool DestroyWindow(nint hwnd);
    [LibraryImport("user32.dll")] public static partial nuint SetTimer(nint hwnd, nuint id, uint ms, nint proc);

    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFontW(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);
    [LibraryImport("gdi32.dll")] public static partial nint CreateCompatibleDC(nint hdc);
    [LibraryImport("gdi32.dll")] public static partial nint SelectObject(nint hdc, nint obj);
    [LibraryImport("gdi32.dll")] public static partial nint CreateSolidBrush(uint color);
    [LibraryImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool TextOutW(nint hdc, int x, int y, char* text, int count);
    [LibraryImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetTextExtentPoint32W(nint hdc, char* text, int count, Size* size);
    [LibraryImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetTextMetricsW(nint hdc, TextMetric* tm);
    [LibraryImport("gdi32.dll")] public static partial int SetBkMode(nint hdc, int mode);
    [LibraryImport("gdi32.dll")] public static partial uint SetTextColor(nint hdc, uint color);
}
