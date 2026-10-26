using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NksHub.Textida.Accessibility.Windows;

/// <summary>
/// A zero-size child window that exists only to carry the UIA root object of an editor: a UI framework that draws its own text
/// view (Avalonia) hosts it as a native control, and UI Automation reaches the editor element through the window handle.
/// The bridge is created on the first <c>WM_GETOBJECT</c>, so an editor nobody inspects costs nothing.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class UiaProxyWindow : IDisposable
{
    private const string ClassName = "TextidaUiaProxy";
    private const uint WsChild = 0x40000000, WsVisible = 0x10000000;
    private const uint WmSetFocus = 0x0007, WmNcDestroy = 0x0082, WmNcHitTest = 0x0084;
    private static readonly Dictionary<nint, UiaProxyWindow> Windows = [];
    private static bool _registered;

    private readonly Func<WindowsUiaBridge> _createBridge;
    private WindowsUiaBridge? _bridge;
    private bool _destroyed;

    private UiaProxyWindow(nint handle, Func<WindowsUiaBridge> createBridge)
    {
        Handle = handle;
        _createBridge = createBridge;
    }

    public nint Handle { get; }

    /// <summary>Raised when UI Automation asked the window to take the keyboard focus; the focus itself goes back to the top-level window, which owns the real input.</summary>
    public event Action? FocusRequested;

    /// <summary>Creates the window under <paramref name="parent"/>; <paramref name="createBridge"/> gets the new window handle and runs on the first inspection.</summary>
    public static UiaProxyWindow Create(nint parent, Func<nint, WindowsUiaBridge> createBridge)
    {
        nint instance = GetModuleHandleW(0);
        if (!_registered)
        {
            fixed (char* name = ClassName)
            {
                WndClassEx cls = new() { CbSize = (uint)sizeof(WndClassEx), WndProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WindowProc, Instance = instance, ClassName = name };
                if (RegisterClassExW(&cls) == 0) throw new InvalidOperationException("RegisterClassEx failed for " + ClassName);
            }
            _registered = true;
        }
        nint hwnd = CreateWindowExW(0, ClassName, "", WsChild | WsVisible, 0, 0, 0, 0, parent, 0, instance, 0);
        if (hwnd == 0) throw new InvalidOperationException("CreateWindowEx failed for " + ClassName);
        var window = new UiaProxyWindow(hwnd, () => createBridge(hwnd));
        Windows[hwnd] = window;
        return window;
    }

    public void Reparent(nint parent) => SetParent(Handle, parent);

    /// <summary>Drops the bridge so the next inspection builds a new one over the current state of the editor.</summary>
    public void ResetBridge()
    {
        _bridge?.Dispose();
        _bridge = null;
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        ResetBridge();
        Windows.Remove(Handle);
        DestroyWindow(Handle);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static nint WindowProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (Windows.TryGetValue(hwnd, out var window))
            {
                switch (msg)
                {
                    case UiaNative.WmGetObject when !window._destroyed:
                        window._bridge ??= window._createBridge();
                        if (window._bridge.TryHandleGetObject(wParam, lParam, out nint result)) return result;
                        break;
                    case WmSetFocus:
                        SetFocus(GetAncestor(hwnd, 2 /* GA_ROOT */));
                        window.FocusRequested?.Invoke();
                        return 0;
                    case WmNcHitTest:
                        return -1; // HTTRANSPARENT
                    case WmNcDestroy:
                        Windows.Remove(hwnd);
                        break;
                }
            }
        }
        catch (Exception)
        {
            // a failure to answer UIA must not take the application down; the client sees an element without patterns
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        public uint CbSize, Style;
        public nint WndProc;
        public int ClsExtra, WndExtra;
        public nint Instance, Icon, Cursor, Background;
        public char* MenuName;
        public char* ClassName;
        public nint IconSm;
    }

    [LibraryImport("kernel32.dll")] private static partial nint GetModuleHandleW(nint name);
    [LibraryImport("user32.dll")] private static partial ushort RegisterClassExW(WndClassEx* cls);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [LibraryImport("user32.dll")] private static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyWindow(nint hwnd);
    [LibraryImport("user32.dll")] private static partial nint SetParent(nint child, nint parent);
    [LibraryImport("user32.dll")] private static partial nint GetAncestor(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] private static partial nint SetFocus(nint hwnd);
}
