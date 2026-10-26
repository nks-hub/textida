using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NksHub.Textida.Accessibility.Windows;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Accessibility.Host;

/// <summary>Editor surface drawn with GDI in one native child window; owns the document and handles keyboard input.</summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WinSurface : DocumentTextSurface
{
    private const int MarginX = 8, MarginY = 4, TabWidth = 4;
    private readonly uint _uiThread = WinNative.GetCurrentThreadId();
    private readonly nint _measureDc;
    private char _pendingHighSurrogate;
    public nint Hwnd { get; set; }
    public int LineHeight { get; }

    public WinSurface(Document doc) : base(doc)
    {
        nint font = WinNative.CreateFontW(-18, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 49, "Consolas");
        _measureDc = WinNative.CreateCompatibleDC(0);
        WinNative.SelectObject(_measureDc, font);
        TextMetric tm;
        WinNative.GetTextMetricsW(_measureDc, &tm);
        LineHeight = tm.Height;
        Font = font;
        TextChanged += _ => Repaint();
        SelectionChanged += () => { Repaint(); PlaceCaret(); };
    }

    public nint Font { get; }

    public override bool HasFocus => WinNative.GetFocus() == Hwnd;
    public override void Focus() => WinNative.SetFocus(Hwnd);
    public void NotifyFocus() => RaiseFocusChanged();

    public override void InvokeOnUiThread(Action action)
    {
        if (WinNative.GetCurrentThreadId() == _uiThread) { action(); return; }
        var handle = GCHandle.Alloc(action);
        try { WinNative.SendMessageW(Hwnd, WinNative.WmApp + 1, 0, GCHandle.ToIntPtr(handle)); }
        finally { handle.Free(); }
    }

    public override ScreenRect ScreenBounds
    {
        get
        {
            Rect r;
            WinNative.GetWindowRect(Hwnd, &r);
            return new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }
    }

    public override TextSpan VisibleRange
    {
        get
        {
            Rect r;
            WinNative.GetClientRect(Hwnd, &r);
            int lines = Math.Min(LineCount, Math.Max(1, (r.Bottom - MarginY) / LineHeight + 1));
            return new(0, LineStart(lines));
        }
    }

    private static string Display(string s) => s.Replace("\t", new string(' ', TabWidth));

    private int Measure(string text)
    {
        if (text.Length == 0) return 0;
        Size size;
        fixed (char* p = text) WinNative.GetTextExtentPoint32W(_measureDc, p, text.Length, &size);
        return size.Cx;
    }

    /// <summary>End of the visible text of a line, before its terminator.</summary>
    public int LineEnd(int line)
    {
        int end = LineStart(line + 1);
        while (end > LineStart(line) && GetText(end - 1, 1) is "\r" or "\n") end--;
        return end;
    }

    /// <summary>Rectangles of a range in client pixels, one per line.</summary>
    public List<(int X, int Y, int W, int H)> ClientRects(int start, int length)
    {
        var result = new List<(int, int, int, int)>();
        int end = start + length;
        int last = LineOfOffset(end);
        for (int line = LineOfOffset(start); line <= last; line++)
        {
            int ls = LineStart(line), le = LineEnd(line);
            if (length > 0 && ls >= end) break;
            int a = Math.Min(Math.Max(start, ls), le), b = Math.Min(end, le);
            string text = GetText(ls, le - ls);
            int x0 = Measure(Display(text[..(a - ls)]));
            int x1 = b > a ? Measure(Display(text[..(b - ls)])) : x0;
            if (start <= le && end >= LineStart(line + 1) && LineStart(line + 1) > le) x1 += Measure(" ");
            result.Add((MarginX + x0, MarginY + line * LineHeight, x1 - x0, LineHeight));
        }
        return result;
    }

    public override IReadOnlyList<ScreenRect> GetRangeBounds(int start, int length)
    {
        Point origin = default;
        WinNative.ClientToScreen(Hwnd, &origin);
        int ox = origin.X, oy = origin.Y;
        return ClientRects(start, length).Select(r => new ScreenRect(ox + r.X, oy + r.Y, r.W, r.H)).ToList();
    }

    public override int OffsetFromPoint(double x, double y)
    {
        Point pt = new() { X = (int)x, Y = (int)y };
        WinNative.ScreenToClient(Hwnd, &pt);
        int line = (pt.Y - MarginY) / LineHeight;
        if (pt.Y < MarginY || line >= LineCount) return -1;
        int ls = LineStart(line), le = LineEnd(line);
        string text = GetText(ls, le - ls);
        int i = 0;
        while (i < text.Length)
        {
            int next = TextUnits.UnitEnd(this, TextUnit.Character, ls + i) - ls;
            int mid = (Measure(Display(text[..i])) + Measure(Display(text[..Math.Min(next, text.Length)]))) / 2 + MarginX;
            if (pt.X < mid) break;
            i = next;
        }
        return ls + Math.Min(i, text.Length);
    }

    public override void ScrollIntoView(int start, int length, bool alignToTop) { }

    public void Repaint() => WinNative.InvalidateRect(Hwnd, null, true);

    public void PlaceCaret()
    {
        if (!HasFocus) return;
        var r = ClientRects(CaretOffset, 0)[0];
        WinNative.SetCaretPos(r.X, r.Y);
    }

    public void OnFocus(bool focused)
    {
        if (focused)
        {
            WinNative.CreateCaret(Hwnd, 0, 2, LineHeight);
            PlaceCaret();
            WinNative.ShowCaret(Hwnd);
        }
        else WinNative.DestroyCaret();
        RaiseFocusChanged();
    }

    public void OnChar(char c)
    {
        if (c < ' ' && c is not ('\t' or '\r')) return;
        if (char.IsHighSurrogate(c)) { _pendingHighSurrogate = c; return; }
        string text = c == '\r' ? "\r\n" : char.IsLowSurrogate(c) && _pendingHighSurrogate != 0 ? $"{_pendingHighSurrogate}{c}" : c.ToString();
        _pendingHighSurrogate = '\0';
        var sel = Selection;
        ReplaceText(sel.Start, sel.Length, text, "type");
    }

    public void OnKey(int vk)
    {
        bool shift = WinNative.GetKeyState(WinNative.VkShift) < 0, ctrl = WinNative.GetKeyState(WinNative.VkControl) < 0;
        int anchor = Selection.IsEmpty ? CaretOffset : CaretOffset == Selection.Start ? Selection.End : Selection.Start;
        int caret = CaretOffset;
        int line = LineOfOffset(caret);
        int target;
        switch (vk)
        {
            case WinNative.VkLeft: target = TextUnits.UnitStart(this, ctrl ? TextUnit.Word : TextUnit.Character, caret - 1); break;
            case WinNative.VkRight: target = TextUnits.UnitEnd(this, ctrl ? TextUnit.Word : TextUnit.Character, caret); break;
            case WinNative.VkHome: target = LineStart(line); break;
            case WinNative.VkEnd: target = LineEnd(line); break;
            case WinNative.VkUp or WinNative.VkDown:
                {
                    int to = line + (vk == WinNative.VkUp ? -1 : 1);
                    if (to < 0 || to >= LineCount) return;
                    target = Math.Min(LineStart(to) + (caret - LineStart(line)), LineEnd(to));
                    break;
                }
            case WinNative.VkBack or WinNative.VkDelete:
                {
                    var sel = Selection;
                    if (sel.IsEmpty)
                    {
                        int from = vk == WinNative.VkBack ? TextUnits.UnitStart(this, TextUnit.Character, caret - 1) : caret;
                        int to = vk == WinNative.VkBack ? caret : TextUnits.UnitEnd(this, TextUnit.Character, caret);
                        sel = new(from, to - from);
                    }
                    if (sel.Length > 0) ReplaceText(sel.Start, sel.Length, "", "delete");
                    return;
                }
            case 'A' when ctrl: Select(0, Length); return;
            case 'Z' when ctrl: Document.Undo(); return;
            case 'Y' when ctrl: Document.Redo(); return;
            default: return;
        }
        target = Math.Clamp(target, 0, Length);
        Select(shift ? anchor : target, target);
    }

    public void Paint(nint hdc)
    {
        WinNative.SetBkMode(hdc, 1);
        WinNative.SelectObject(hdc, Font);
        nint brush = WinNative.CreateSolidBrush(0xF2D7B4);
        if (!Selection.IsEmpty)
            foreach (var r in ClientRects(Selection.Start, Selection.Length))
            {
                Rect rect = new() { Left = r.X, Top = r.Y, Right = r.X + r.W, Bottom = r.Y + r.H };
                WinNative.FillRect(hdc, &rect, brush);
            }
        for (int line = 0; line < LineCount; line++)
        {
            string text = Display(GetText(LineStart(line), LineEnd(line) - LineStart(line)));
            if (text.Length == 0) continue;
            fixed (char* p = text) WinNative.TextOutW(hdc, MarginX, MarginY + line * LineHeight, p, text.Length);
        }
    }
}

[SupportedOSPlatform("windows")]
internal static unsafe class WinHost
{
    private static nint _main, _editor, _button;
    private const int ButtonHeight = 28;
    private static WinSurface _surface = null!;
    private static WindowsUiaBridge _bridge = null!;

    public static int Run(Document doc, int exitAfterSeconds)
    {
        var instance = WinNative.GetModuleHandleW(0);
        _surface = new WinSurface(doc);
        Register("TextidaProbeMain", (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&MainProc, instance, 6 /* COLOR_WINDOW + 1 */);
        Register("TextidaProbeEditor", (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&EditorProc, instance, 0);

        _main = WinNative.CreateWindowExW(0, "TextidaProbeMain", "Textida accessibility probe", WinNative.WsOverlappedWindow, 200, 200, 820, 360, 0, 0, instance, 0);
        _button = WinNative.CreateWindowExW(0, "BUTTON", "Tab: notes.txt", WinNative.WsChild | WinNative.WsVisible | WinNative.WsTabStop, 0, 0, 800, ButtonHeight, _main, 0, instance, 0);
        _editor = WinNative.CreateWindowExW(0, "TextidaProbeEditor", "", WinNative.WsChild | WinNative.WsVisible | WinNative.WsTabStop,  0, ButtonHeight, 800, 300 - ButtonHeight, _main, 0, instance, 0);
        _surface.Hwnd = _editor;
        _bridge = new WindowsUiaBridge(_surface, _editor, new UiaEditorInfo("Textida probe editor", "textida-editor"));
        WinNative.ShowWindow(_main, WinNative.SwShow);
        WinNative.SetFocus(_editor);
        if (exitAfterSeconds > 0) WinNative.SetTimer(_main, 1, (uint)exitAfterSeconds * 1000, 0);

        Console.WriteLine($"READY pid={Environment.ProcessId} hwnd={_editor}");
        Console.Out.Flush();

        Msg msg;
        while (WinNative.GetMessageW(&msg, 0, 0, 0) > 0)
        {
            WinNative.TranslateMessage(&msg);
            WinNative.DispatchMessageW(&msg);
        }
        _bridge.Dispose();
        return 0;
    }

    private static void Register(string name, nint proc, nint instance, nint background)
    {
        fixed (char* n = name)
        {
            WndClassEx cls = new()
            {
                CbSize = (uint)sizeof(WndClassEx), WndProc = proc, Instance = instance, ClassName = n,
                Cursor = WinNative.LoadCursorW(0, 32513 /* IDC_IBEAM */), Background = background,
            };
            if (WinNative.RegisterClassExW(&cls) == 0) throw new InvalidOperationException("RegisterClassEx failed for " + name);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint MainProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WinNative.WmSize:
                int w = (short)(lParam & 0xFFFF), h = (short)((lParam >> 16) & 0xFFFF);
                WinNative.MoveWindow(_button, 0, 0, w, ButtonHeight, true);
                WinNative.MoveWindow(_editor, 0, ButtonHeight, w, Math.Max(0, h - ButtonHeight), true);
                return 0;
            case WinNative.WmSetFocus:
                WinNative.SetFocus(_editor);
                return 0;
            case WinNative.WmTimer:
                WinNative.DestroyWindow(hwnd);
                return 0;
            case WinNative.WmDestroy:
                Console.WriteLine("FINAL " + Program.Escape(_surface.GetText(0, _surface.Length)));
                Console.Out.Flush();
                WinNative.PostQuitMessage(0);
                return 0;
        }
        return WinNative.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint EditorProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case WinNative.WmApp + 1:
                    ((Action)GCHandle.FromIntPtr(lParam).Target!)();
                    return 0;
                case 0x003D when _bridge is not null && _bridge.TryHandleGetObject(wParam, lParam, out nint result):
                    return result;
                case WinNative.WmSetFocus:
                    _surface.OnFocus(true);
                    return 0;
                case WinNative.WmKillFocus:
                    _surface.OnFocus(false);
                    return 0;
                case WinNative.WmChar:
                    _surface.OnChar((char)wParam);
                    return 0;
                case WinNative.WmKeyDown:
                    _surface.OnKey((int)wParam);
                    return 0;
                case WinNative.WmLButtonDown:
                    {
                        WinNative.SetFocus(hwnd);
                        Point pt = new() { X = (short)(lParam & 0xFFFF), Y = (short)((lParam >> 16) & 0xFFFF) };
                        WinNative.ClientToScreen(hwnd, &pt);
                        int offset = _surface.OffsetFromPoint(pt.X, pt.Y);
                        if (offset >= 0) _surface.Select(offset, offset);
                        return 0;
                    }
                case WinNative.WmEraseBkgnd:
                    return 1;
                case WinNative.WmPaint:
                    {
                        PaintStruct ps;
                        nint hdc = WinNative.BeginPaint(hwnd, &ps);
                        Rect client;
                        WinNative.GetClientRect(hwnd, &client);
                        WinNative.FillRect(hdc, &client, WinNative.CreateSolidBrush(0xFFFFFF));
                        _surface.Paint(hdc);
                        WinNative.EndPaint(hwnd, &ps);
                        return 0;
                    }
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("EditorProc: " + e);
        }
        return WinNative.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

}
