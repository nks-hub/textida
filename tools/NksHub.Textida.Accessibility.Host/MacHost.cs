using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NksHub.Textida.Accessibility.MacOS;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Accessibility.Host;

/// <summary>Editor surface drawn in a flipped NSView with a fixed-pitch grid (one cell per UTF-16 unit, tab = 4 cells).</summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacSurface : DocumentTextSurface
{
    private const double Margin = 8;
    private const int TabWidth = 4;
    private char _pendingHigh;
    public nint View { get; set; }
    public nint Window { get; set; }
    public double CellWidth { get; set; } = 8;
    public double CellHeight { get; set; } = 16;

    public MacSurface(Document doc) : base(doc)
    {
        TextChanged += _ => Repaint();
        SelectionChanged += Repaint;
    }

    public void Repaint() { if (View != 0) ObjC.Send(View, ObjC.Sel("setNeedsDisplay:"), true); }

    public override bool HasFocus => Window != 0 && ObjC.Msg(Window, "firstResponder") == View && ObjC.Msg(Window, "isKeyWindow") != 0;
    public override void Focus() { ObjC.Msg(Window, "makeFirstResponder:", View); RaiseFocusChanged(); }

    private NSRect ToScreenTopLeft(NSRect viewRect)
    {
        var inWindow = ObjC.SendRect(View, ObjC.Sel("convertRect:toView:"), viewRect, 0);
        var screen = ObjC.SendRect(Window, ObjC.Sel("convertRectToScreen:"), inWindow);
        return new(screen.X, MacAxBridge.PrimaryScreenHeight - (screen.Y + screen.Height), screen.Width, screen.Height);
    }

    public override ScreenRect ScreenBounds
    {
        get
        {
            var r = ToScreenTopLeft(ObjC.SendRect(View, ObjC.Sel("bounds")));
            return new(r.X, r.Y, r.Width, r.Height);
        }
    }

    public override TextSpan VisibleRange
    {
        get
        {
            var b = ObjC.SendRect(View, ObjC.Sel("bounds"));
            int lines = Math.Min(LineCount, Math.Max(1, (int)((b.Height - Margin) / CellHeight) + 1));
            return new(0, LineStart(lines));
        }
    }

    public int LineEnd(int line)
    {
        int end = LineStart(line + 1);
        while (end > LineStart(line) && GetText(end - 1, 1) is "\r" or "\n") end--;
        return end;
    }

    private static int Cells(string s) => s.Length + s.Count(c => c == '\t') * (TabWidth - 1);

    /// <summary>Rectangles of a range in view coordinates, one per line.</summary>
    public List<NSRect> ViewRects(int start, int length)
    {
        var result = new List<NSRect>();
        int end = start + length;
        int last = LineOfOffset(end);
        for (int line = LineOfOffset(start); line <= last; line++)
        {
            int ls = LineStart(line), le = LineEnd(line);
            if (length > 0 && ls >= end) break;
            int a = Math.Min(Math.Max(start, ls), le), b = Math.Min(end, le);
            string text = GetText(ls, le - ls);
            int c0 = Cells(text[..(a - ls)]);
            int c1 = b > a ? Cells(text[..(b - ls)]) : c0;
            if (start <= le && end >= LineStart(line + 1) && LineStart(line + 1) > le) c1++;
            result.Add(new(Margin + c0 * CellWidth, Margin + line * CellHeight, (c1 - c0) * CellWidth, CellHeight));
        }
        return result;
    }

    public override IReadOnlyList<ScreenRect> GetRangeBounds(int start, int length) =>
        ViewRects(start, length).Select(r => { var s = ToScreenTopLeft(r); return new ScreenRect(s.X, s.Y, s.Width, s.Height); }).ToList();

    public int OffsetFromViewPoint(NSPoint p)
    {
        int line = (int)Math.Floor((p.Y - Margin) / CellHeight);
        if (p.Y < Margin || line >= LineCount) return -1;
        int ls = LineStart(line), le = LineEnd(line);
        string text = GetText(ls, le - ls);
        int cell = (int)Math.Floor((p.X - Margin) / CellWidth), at = 0, i = 0;
        while (i < text.Length && at + (text[i] == '\t' ? TabWidth : 1) <= cell)
        {
            at += text[i] == '\t' ? TabWidth : 1;
            i++;
        }
        return ls + i;
    }

    public override int OffsetFromPoint(double x, double y)
    {
        var screenRect = new NSRect(x, MacAxBridge.PrimaryScreenHeight - y, 0, 0);
        var inWindow = ObjC.SendRect(Window, ObjC.Sel("convertRectFromScreen:"), screenRect);
        var local = ObjC.SendPoint(View, ObjC.Sel("convertPoint:fromView:"), new NSPoint(inWindow.X, inWindow.Y), 0);
        return OffsetFromViewPoint(local);
    }

    public override void ScrollIntoView(int start, int length, bool alignToTop) { }

    public void TypeText(string text)
    {
        foreach (char c in text)
        {
            if (char.IsHighSurrogate(c)) { _pendingHigh = c; continue; }
            string unit = char.IsLowSurrogate(c) && _pendingHigh != 0 ? $"{_pendingHigh}{c}" : c.ToString();
            _pendingHigh = '\0';
            var sel = Selection;
            ReplaceText(sel.Start, sel.Length, unit, "type");
        }
    }

    public void Backspace(bool forward)
    {
        var sel = Selection;
        if (sel.IsEmpty)
        {
            int from = forward ? CaretOffset : TextUnits.UnitStart(this, TextUnit.Character, CaretOffset - 1);
            int to = forward ? TextUnits.UnitEnd(this, TextUnit.Character, CaretOffset) : CaretOffset;
            sel = new(from, to - from);
        }
        if (sel.Length > 0) ReplaceText(sel.Start, sel.Length, "", "delete");
    }

    /// <summary>Caret keys; <paramref name="key"/> is a name so the command hook and real key codes share one path.</summary>
    public void MoveCaret(string key, bool shift)
    {
        int caret = CaretOffset, line = LineOfOffset(caret);
        int anchor = Selection.IsEmpty ? caret : caret == Selection.Start ? Selection.End : Selection.Start;
        int target;
        switch (key)
        {
            case "left": target = TextUnits.UnitStart(this, TextUnit.Character, caret - 1); break;
            case "right": target = TextUnits.UnitEnd(this, TextUnit.Character, caret); break;
            case "home": target = LineStart(line); break;
            case "end": target = LineEnd(line); break;
            case "up" or "down":
                {
                    int to = line + (key == "up" ? -1 : 1);
                    if (to < 0 || to >= LineCount) return;
                    target = Math.Min(LineStart(to) + (caret - LineStart(line)), LineEnd(to));
                    break;
                }
            default: return;
        }
        target = Math.Clamp(target, 0, Length);
        Select(shift ? anchor : target, target);
    }

}

[SupportedOSPlatform("macos")]
internal static unsafe partial class MacHost
{
    private static MacSurface _surface = null!;

    // the C macro dispatch_get_main_queue() is the address of this exported object
    private static nint MainQueue => NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libSystem.B.dylib"), "_dispatch_main_q");
    [LibraryImport("/usr/lib/libSystem.B.dylib")]
    private static partial void dispatch_async_f(nint queue, nint context, nint work);

    public static int Run(Document doc, int exitAfterSeconds, string? commandFile)
    {
        ObjC.LoadFramework("Foundation");
        ObjC.LoadFramework("AppKit");
        _surface = new MacSurface(doc);
        nint app = ObjC.Msg(ObjC.Class("NSApplication"), "sharedApplication");
        ObjC.Send(app, ObjC.Sel("setActivationPolicy:"), 0);

        nint cls = ObjC.objc_allocateClassPair(ObjC.Class("NSView"), "TextidaEditorView", 0);
        ObjC.AddMethod(cls, "isFlipped", (delegate* unmanaged<nint, nint, byte>)&IsFlipped, "B16@0:8");
        ObjC.AddMethod(cls, "acceptsFirstResponder", (delegate* unmanaged<nint, nint, byte>)&IsFlipped, "B16@0:8");
        ObjC.AddMethod(cls, "drawRect:", (delegate* unmanaged<nint, nint, NSRect, void>)&DrawRect, "v48@0:8{CGRect={CGPoint=dd}{CGSize=dd}}16");
        ObjC.AddMethod(cls, "keyDown:", (delegate* unmanaged<nint, nint, nint, void>)&KeyDown, "v24@0:8@16");
        ObjC.AddMethod(cls, "mouseDown:", (delegate* unmanaged<nint, nint, nint, void>)&MouseDown, "v24@0:8@16");
        ObjC.AddMethod(cls, "quit:", (delegate* unmanaged<nint, nint, nint, void>)&Quit, "v24@0:8@16");
        MacAxBridge.AddAccessibilityMethods(cls);
        ObjC.objc_registerClassPair(cls);

        var frame = new NSRect(0, 0, 720, 320);
        nint view = ObjC.Send(ObjC.Msg(cls, "alloc"), ObjC.Sel("initWithFrame:"), frame);
        nint window = ObjC.Send(ObjC.Msg(ObjC.Class("NSWindow"), "alloc"), ObjC.Sel("initWithContentRect:styleMask:backing:defer:"), new NSRect(200, 200, 720, 320), 15, 2, false);
        ObjC.Send(window, ObjC.Sel("setTitle:"), ObjC.NewString("Textida accessibility probe"));
        ObjC.Send(window, ObjC.Sel("setContentView:"), view);
        _surface.View = view;
        _surface.Window = window;

        nint font = ObjC.Send(ObjC.Class("NSFont"), ObjC.Sel("userFixedPitchFontOfSize:"), 14.0);
        nint attrs = ObjC.Send(ObjC.Class("NSDictionary"), ObjC.Sel("dictionaryWithObject:forKey:"), font, ObjC.NewString("NSFont"));
        ObjC.Msg(attrs, "retain");
        _attrs = attrs;
        var cell = ObjC.SendSize(ObjC.NewString("M"), ObjC.Sel("sizeWithAttributes:"), attrs);
        _surface.CellWidth = cell.Width;
        _surface.CellHeight = Math.Ceiling(cell.Height);

        _view = view;
        MacAxBridge.Attach(view, _surface, "Textida probe editor");
        ObjC.Send(window, ObjC.Sel("makeKeyAndOrderFront:"), 0);
        ObjC.Msg(window, "makeFirstResponder:", view);
        ObjC.Send(app, ObjC.Sel("activateIgnoringOtherApps:"), true);

        if (exitAfterSeconds > 0)
            ObjC.Send(ObjC.Class("NSTimer"), ObjC.Sel("scheduledTimerWithTimeInterval:target:selector:userInfo:repeats:"), view, ObjC.Sel("quit:"), 0, false, (double)exitAfterSeconds);
        if (commandFile is not null) new Thread(() => ReadCommands(commandFile)) { IsBackground = true }.Start();

        Console.WriteLine($"READY pid={Environment.ProcessId}");
        Console.Out.Flush();
        ObjC.Msg(app, "run");
        return 0;
    }

    private static nint _attrs, _view;

    [UnmanagedCallersOnly] private static byte IsFlipped(nint self, nint cmd) => 1;

    [UnmanagedCallersOnly]
    private static void DrawRect(nint self, nint cmd, NSRect dirty)
    {
        ObjC.Msg(ObjC.Msg(ObjC.Class("NSColor"), "whiteColor"), "set");
        ObjC.RectFill(dirty);
        if (!_surface.Selection.IsEmpty)
        {
            ObjC.Msg(ObjC.Msg(ObjC.Class("NSColor"), "selectedTextBackgroundColor"), "set");
            foreach (var r in _surface.ViewRects(_surface.Selection.Start, _surface.Selection.Length)) ObjC.RectFill(r);
        }
        for (int line = 0; line < _surface.LineCount; line++)
        {
            string text = _surface.GetText(_surface.LineStart(line), _surface.LineEnd(line) - _surface.LineStart(line)).Replace("\t", "    ");
            if (text.Length == 0) continue;
            ObjC.Send(ObjC.NewString(text), ObjC.Sel("drawAtPoint:withAttributes:"), new NSPoint(8, 8 + line * _surface.CellHeight), _attrs);
        }
        if (_surface.HasFocus && _surface.Selection.IsEmpty)
        {
            ObjC.Msg(ObjC.Msg(ObjC.Class("NSColor"), "textColor"), "set");
            var c = _surface.ViewRects(_surface.CaretOffset, 0)[0];
            ObjC.RectFill(new(c.X, c.Y, 1.5, c.Height));
        }
    }

    [UnmanagedCallersOnly]
    private static void KeyDown(nint self, nint cmd, nint theEvent)
    {
        int code = (int)(ObjC.Msg(theEvent, "keyCode") & 0xFFFF);
        nint flags = ObjC.Msg(theEvent, "modifierFlags");
        bool shift = (flags & (1 << 17)) != 0, command = (flags & (1 << 20)) != 0;
        switch (code)
        {
            case 123: _surface.MoveCaret("left", shift); return;
            case 124: _surface.MoveCaret("right", shift); return;
            case 125: _surface.MoveCaret("down", shift); return;
            case 126: _surface.MoveCaret("up", shift); return;
            case 115: _surface.MoveCaret("home", shift); return;
            case 119: _surface.MoveCaret("end", shift); return;
            case 51: _surface.Backspace(false); return;
            case 117: _surface.Backspace(true); return;
            case 0 when command: _surface.Select(0, _surface.Length); return;
            case 6 when command: _surface.Document.Undo(); return;
        }
        if (command) return;
        string typed = ObjC.ToManaged(ObjC.Msg(theEvent, "characters"));
        if (typed.Length > 0 && typed[0] >= ' ' && typed[0] != 0x7F && !(typed[0] >= 0xF700 && typed[0] <= 0xF8FF)) _surface.TypeText(typed);
        else if (typed == "\r") _surface.TypeText("\n");
    }

    [UnmanagedCallersOnly]
    private static void MouseDown(nint self, nint cmd, nint theEvent)
    {
        ObjC.Msg(_surface.Window, "makeFirstResponder:", self);
        var local = ObjC.SendPoint(self, ObjC.Sel("convertPoint:fromView:"), ObjC.SendPoint(theEvent, ObjC.Sel("locationInWindow")), 0);
        int offset = _surface.OffsetFromViewPoint(local);
        if (offset >= 0) _surface.Select(offset, offset);
    }

    [UnmanagedCallersOnly]
    private static void Quit(nint self, nint cmd, nint sender) => Terminate();

    private static void Terminate()
    {
        Console.WriteLine("FINAL " + Program.Escape(_surface.GetText(0, _surface.Length)));
        Console.Out.Flush();
        Environment.Exit(0);
    }

    /// <summary>
    /// Calls the accessibility methods of the view the way the accessibility server does, by selector through the Objective-C
    /// runtime, so the registered IMPs (including the NSRange, NSPoint and NSRect struct arguments and returns) are checked on
    /// this CPU. It runs in the host and needs no accessibility permission, but it is not an AXUIElement client: no
    /// AppKit attribute mapping or IPC is exercised.
    /// </summary>
    private static void SelfTest()
    {
        int failed = 0;
        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? ": " + detail : "")}");
            if (!ok) failed++;
        }
        string Str(nint o) => ObjC.ToManaged(o);
        nint V(string sel) => ObjC.Msg(_view, sel);
        string sample = Program.SampleText;

        nint names = V("accessibilityAttributeNames");
        int count = (int)ObjC.Msg(names, "count");
        var list = new List<string>();
        for (int i = 0; i < count; i++) list.Add(Str(ObjC.Send(names, ObjC.Sel("objectAtIndex:"), i)));
        Console.WriteLine($"INFO AppKit accessibilityAttributeNames without an AX client ({count}): {string.Join(" ", list)}");

        _surface.SetSelection(6, 7);
        Check("isAccessibilityElement", V("isAccessibilityElement") != 0);
        Check("accessibilityRole", Str(V("accessibilityRole")) == "AXTextArea");
        Check("accessibilityLabel", Str(V("accessibilityLabel")) == "Textida probe editor");
        Check("accessibilityValue", Str(V("accessibilityValue")) == sample);
        Check("accessibilitySelectedText", Str(V("accessibilitySelectedText")) == "Textida", Str(V("accessibilitySelectedText")));
        var sel = ObjC.SendRange(_view, ObjC.Sel("accessibilitySelectedTextRange"));
        Check("accessibilitySelectedTextRange (NSRange return)", sel.Location == 6 && sel.Length == 7, $"{sel.Location},{sel.Length}");
        Check("accessibilityNumberOfCharacters", V("accessibilityNumberOfCharacters") == sample.Length);
        var vis = ObjC.SendRange(_view, ObjC.Sel("accessibilityVisibleCharacterRange"));
        Check("accessibilityVisibleCharacterRange", vis.Location == 0 && (int)vis.Length == sample.Length, $"{vis.Location},{vis.Length}");
        Check("accessibilityInsertionPointLineNumber", V("accessibilityInsertionPointLineNumber") == 0);
        Check("accessibilityLineForIndex:", ObjC.Msg(_view, "accessibilityLineForIndex:", 20) == 1);
        var line = ObjC.SendRange(_view, ObjC.Sel("accessibilityRangeForLine:"), 1);
        Check("accessibilityRangeForLine:", line.Location == 15 && line.Length == 25, $"{line.Location},{line.Length}");
        Check("accessibilityStringForRange: (NSRange argument)", Str(ObjC.Send(_view, ObjC.Sel("accessibilityStringForRange:"), new NSRange(0, 5))) == "Hello");
        var emoji = ObjC.SendRange(_view, ObjC.Sel("accessibilityRangeForIndex:"), sample.IndexOf("😀", StringComparison.Ordinal));
        Check("accessibilityRangeForIndex: covers the surrogate pair", emoji.Length == 2, $"{emoji.Location},{emoji.Length}");

        var rect = ObjC.SendRect(_view, ObjC.Sel("accessibilityFrameForRange:"), new NSRange(7, 1));
        var window = ObjC.SendRect(_surface.Window, ObjC.Sel("frame"));
        bool inside = rect.Width > 3 && rect.Height > 8 && rect.X >= window.X && rect.X + rect.Width <= window.X + window.Width && rect.Y >= window.Y && rect.Y + rect.Height <= window.Y + window.Height;
        Check("accessibilityFrameForRange: (NSRect return, bottom-left screen origin) inside the window", inside,
            $"range ({rect.X:F0},{rect.Y:F0},{rect.Width:F0},{rect.Height:F0}) window ({window.X:F0},{window.Y:F0},{window.Width:F0},{window.Height:F0})");
        var pos = ObjC.SendRange(_view, ObjC.Sel("accessibilityRangeForPosition:"), new NSPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
        Check("accessibilityRangeForPosition: (NSPoint argument)", pos.Location == 7 && pos.Length == 1, $"{pos.Location},{pos.Length}");

        ObjC.Send(_view, ObjC.Sel("setAccessibilitySelectedTextRange:"), new NSRange(0, 5));
        Check("setAccessibilitySelectedTextRange:", _surface.Selection == new TextSpan(0, 5));

        // attribute based entry points, the ones the accessibility server calls for AXUIElement requests
        nint Attr(string name) => ObjC.Send(_view, ObjC.Sel("accessibilityAttributeValue:"), ObjC.NewString(name));
        nint Param(string name, nint value) => ObjC.Send(_view, ObjC.Sel("accessibilityAttributeValue:forParameter:"), ObjC.NewString(name), value);
        nint Int(int v) => ObjC.Send(ObjC.Class("NSNumber"), ObjC.Sel("numberWithInteger:"), v);
        nint Range(int loc, int len) => ObjC.Send(ObjC.Class("NSValue"), ObjC.Sel("valueWithRange:"), new NSRange((nuint)loc, (nuint)len));
        bool Has(nint array, string name) => (ObjC.Msg(array, "containsObject:", ObjC.NewString(name)) & 0xFF) != 0;
        _surface.SetSelection(6, 7);
        nint attrNames = V("accessibilityAttributeNames");
        Check("accessibilityAttributeNames lists the text attributes", new[] { "AXValue", "AXSelectedTextRange", "AXNumberOfCharacters", "AXSelectedText", "AXVisibleCharacterRange", "AXInsertionPointLineNumber" }.All(n => Has(attrNames, n)) && Has(attrNames, "AXRole"));
        nint paramNames = V("accessibilityParameterizedAttributeNames");
        Check("accessibilityParameterizedAttributeNames", new[] { "AXLineForIndex", "AXRangeForLine", "AXStringForRange", "AXRangeForPosition", "AXRangeForIndex", "AXBoundsForRange" }.All(n => Has(paramNames, n)));
        Check("attribute AXRole / AXDescription", Str(Attr("AXRole")) == "AXTextArea" && Str(Attr("AXDescription")) == "Textida probe editor");
        Check("attribute AXValue", Str(Attr("AXValue")) == sample);
        Check("attribute AXSelectedText", Str(Attr("AXSelectedText")) == "Textida");
        var lsel = ObjC.SendRange(Attr("AXSelectedTextRange"), ObjC.Sel("rangeValue"));
        Check("attribute AXSelectedTextRange", lsel.Location == 6 && lsel.Length == 7, $"{lsel.Location},{lsel.Length}");
        Check("attribute AXNumberOfCharacters", ObjC.Msg(Attr("AXNumberOfCharacters"), "integerValue") == sample.Length);
        Check("attribute AXInsertionPointLineNumber", ObjC.Msg(Attr("AXInsertionPointLineNumber"), "integerValue") == 0);
        Check("parameterized AXLineForIndex", ObjC.Msg(Param("AXLineForIndex", Int(20)), "integerValue") == 1);
        var lline = ObjC.SendRange(Param("AXRangeForLine", Int(1)), ObjC.Sel("rangeValue"));
        Check("parameterized AXRangeForLine", lline.Location == 15 && lline.Length == 25, $"{lline.Location},{lline.Length}");
        Check("parameterized AXStringForRange", Str(Param("AXStringForRange", Range(0, 5))) == "Hello");
        var lemoji = ObjC.SendRange(Param("AXRangeForIndex", Int(sample.IndexOf("😀", StringComparison.Ordinal))), ObjC.Sel("rangeValue"));
        Check("parameterized AXRangeForIndex", lemoji.Length == 2);
        var lrect = ObjC.SendRect(Param("AXBoundsForRange", Range(7, 1)), ObjC.Sel("rectValue"));
        Check("parameterized AXBoundsForRange", lrect.Width > 3 && lrect.Height > 8 && lrect.X >= window.X && lrect.X + lrect.Width <= window.X + window.Width, $"({lrect.X:F0},{lrect.Y:F0},{lrect.Width:F0},{lrect.Height:F0})");
        var lpos = ObjC.SendRange(Param("AXRangeForPosition", ObjC.Send(ObjC.Class("NSValue"), ObjC.Sel("valueWithPoint:"), new NSPoint(lrect.X + lrect.Width / 2, lrect.Y + lrect.Height / 2))), ObjC.Sel("rangeValue"));
        Check("parameterized AXRangeForPosition", lpos.Location == 7, $"{lpos.Location},{lpos.Length}");
        Check("AXSelectedTextRange is settable, AXValue is not", ObjC.Send(_view, ObjC.Sel("accessibilityIsAttributeSettable:"), ObjC.NewString("AXSelectedTextRange")) != 0 && (ObjC.Send(_view, ObjC.Sel("accessibilityIsAttributeSettable:"), ObjC.NewString("AXValue")) & 0xFF) == 0);
        ObjC.Send(_view, ObjC.Sel("accessibilitySetValue:forAttribute:"), Range(2, 3), ObjC.NewString("AXSelectedTextRange"));
        Check("accessibilitySetValue:forAttribute: AXSelectedTextRange", _surface.Selection == new TextSpan(2, 3));

        int events = 0;
        _surface.TextChanged += _ => events++;
        _surface.SetSelection(6, 7);
        _surface.TypeText("XY");
        Check("typing through the surface updates accessibilityValue", Str(V("accessibilityValue")).StartsWith("Hello XY\r\nsecond") && events == 2);
        Console.WriteLine(failed == 0 ? "SELFTEST PASSED" : $"SELFTEST {failed} FAILED");
        Console.Out.Flush();
    }

    // ---- command hook: lines "key <name> [shift]", "type <text>", "back", "select <start> <length>", "quit" run on the main thread ----

    private static void ReadCommands(string path)
    {
        while (true)
        {
            using var reader = new StreamReader(path);
            for (string? line; (line = reader.ReadLine()) is not null;)
            {
                var handle = GCHandle.Alloc(line);
                dispatch_async_f(MainQueue, GCHandle.ToIntPtr(handle), (nint)(delegate* unmanaged<nint, void>)&RunCommand);
            }
        }
    }

    [UnmanagedCallersOnly]
    private static void RunCommand(nint context)
    {
        var handle = GCHandle.FromIntPtr(context);
        string line = (string)handle.Target!;
        handle.Free();
        try
        {
            string[] p = line.Split(' ', 2);
            switch (p[0])
            {
                case "key": _surface.MoveCaret(p[1].Split(' ')[0], p[1].EndsWith("shift")); break;
                case "type": _surface.TypeText(p[1].Replace("\\n", "\n")); break;
                case "back": _surface.Backspace(false); break;
                case "select": var a = p[1].Split(' '); _surface.SetSelection(int.Parse(a[0]), int.Parse(a[1])); break;
                case "selftest": SelfTest(); break;
                case "axself":
                    Console.WriteLine($"AXSELF window visible={ObjC.Msg(_surface.Window, "isVisible")} number={ObjC.Msg(_surface.Window, "windowNumber")} key={ObjC.Msg(_surface.Window, "isKeyWindow")} appActive={ObjC.Msg(ObjC.Msg(ObjC.Class("NSApplication"), "sharedApplication"), "isActive")} policy={ObjC.Msg(ObjC.Msg(ObjC.Class("NSApplication"), "sharedApplication"), "activationPolicy")}");
                    MacAxSelf.Run();
                    break;
                case "quit": Terminate(); break;
            }
        }
        catch (Exception e) { Console.Error.WriteLine("command failed: " + e.Message); }
    }
}
