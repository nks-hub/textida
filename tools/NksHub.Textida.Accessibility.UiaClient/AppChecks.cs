using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace NksHub.Textida.Accessibility.UiaClient;

/// <summary>
/// <c>app &lt;Textida.exe&gt;</c>: starts the published editor with a Czech sample file and reads it through the UIA tree of its real
/// window: element lookup, text, caret and selection from keyboard input, typing, text and selection events, and the focus
/// change between the tab strip and the editor. Exit code is the number of failed checks.
/// </summary>
internal static partial class Program
{
    private const string AppSample = "Příliš žluťoučký kůň\r\něl se úpěl ďábelské ódy\r\nthird line\r\n";

    private static int RunApp(string exe)
    {
        string dir = Path.Combine(Path.GetTempPath(), "textida-uia-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "sample.txt");
        File.WriteAllText(file, AppSample, new System.Text.UTF8Encoding(false));

        var psi = new ProcessStartInfo(exe, $"\"{file}\"") { UseShellExecute = false };
        psi.Environment["TEXTIDA_PROFILE"] = Path.Combine(dir, "profile");
        psi.Environment["TEXTIDA_CHANNEL"] = "uia-" + Environment.ProcessId;
        using var app = Process.Start(psi)!;
        try
        {
            AutomationElement? window = null;
            WaitFor(() => (window = AutomationElement.RootElement.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, app.Id))) is not null, 30000);
            if (window is null) { Console.WriteLine("FAIL the application window did not appear"); return 1; }
            AutomationElement? editor = null;
            WaitFor(() => (editor = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "textida-editor"))) is not null, 30000);
            if (editor is null) { Dump(window, 0, 4); Console.WriteLine("FAIL editor element not found in the window tree"); return 1; }
            AppChecksRun(app, window, editor);
        }
        finally
        {
            Automation.RemoveAllEventHandlers();
            if (!app.HasExited) app.Kill(true);
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
        Console.WriteLine(_failed == 0 ? "ALL PASSED" : $"{_failed} FAILED");
        return _failed;
    }

    private static void Dump(AutomationElement e, int depth, int max)
    {
        var c = e.Current;
        Console.WriteLine($"{new string(' ', depth * 2)}{c.ControlType.ProgrammaticName} id='{c.AutomationId}' name='{c.Name}' class={c.ClassName}");
        if (depth >= max) return;
        for (var child = TreeWalker.RawViewWalker.GetFirstChild(e); child is not null; child = TreeWalker.RawViewWalker.GetNextSibling(child)) Dump(child, depth + 1, max);
    }

    private static bool Same(AutomationElement? a, AutomationElement? b) => a is not null && b is not null && a.GetRuntimeId().SequenceEqual(b.GetRuntimeId());

    private static void AppChecksRun(Process app, AutomationElement window, AutomationElement editor)
    {
        if (Environment.GetEnvironmentVariable("UIA_DUMP") is not null) Dump(window, 0, 30);
        var c = editor.Current;
        var path = new List<string>();
        for (var p = TreeWalker.RawViewWalker.GetParent(editor); p is not null && !Same(p, AutomationElement.RootElement); p = TreeWalker.RawViewWalker.GetParent(p)) path.Add(p.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""));
        Check("editor found through the window tree", Same(TreeWalker.RawViewWalker.GetParent(editor) is { } par ? Ancestor(par, window) : null, window), "ancestors: " + string.Join(" < ", path));
        Check("identity", c.ControlType == ControlType.Document && c.ClassName == "TextidaEditor" && c.ProcessId == app.Id, $"ControlType={c.ControlType.ProgrammaticName} Name='{c.Name}' ClassName={c.ClassName} FrameworkId={c.FrameworkId} Hwnd={c.NativeWindowHandle}");
        Check("TextPattern available", (bool)editor.GetCurrentPropertyValue(AutomationElement.IsTextPatternAvailableProperty));
        var r = c.BoundingRectangle;
        Check("bounding rectangle", r.Width > 100 && r.Height > 50, r.ToString());

        var text = (TextPattern)editor.GetCurrentPattern(TextPattern.Pattern);
        int textEvents = 0, selectionEvents = 0;
        var focusSources = new List<string>();
        Automation.AddAutomationEventHandler(TextPattern.TextChangedEvent, editor, TreeScope.Element, (_, _) => Interlocked.Increment(ref textEvents));
        Automation.AddAutomationEventHandler(TextPattern.TextSelectionChangedEvent, editor, TreeScope.Element, (_, _) => Interlocked.Increment(ref selectionEvents));
        Automation.AddAutomationFocusChangedEventHandler((s, _) =>
        {
            try { var e = (AutomationElement)s; lock (focusSources) focusSources.Add($"{e.Current.ControlType.ProgrammaticName.Replace("ControlType.", "")}:{e.Current.AutomationId}"); }
            catch (ElementNotAvailableException) { }
        });
        Thread.Sleep(300);

        var doc = text.DocumentRange;
        string all = text.DocumentRange.GetText(-1);
        Check("DocumentRange text with Czech diacritics", all == AppSample, $"{all.Length} units: '{Show(all)}'");
        Check("GetText(maxLength)", text.DocumentRange.GetText(5) == AppSample[..5]);

        // keyboard input needs the window in front
        var hwnd = (nint)window.Current.NativeWindowHandle;
        ForceForeground(hwnd);
        editor.SetFocus();
        Check("SetFocus moves the keyboard focus to the editor", WaitFor(() => editor.Current.HasKeyboardFocus), $"HasKeyboardFocus={editor.Current.HasKeyboardFocus}, win32 focus {FocusClass()}");

        Press(0x24, ctrl: true); // Ctrl+Home
        Check("caret at the start", WaitFor(() => text.GetSelection() is { Length: 1 } s && s[0].GetText(-1) == "" && OffsetOf(text.DocumentRange, s[0]) == 0));
        int before = selectionEvents;
        for (int i = 0; i < 3; i++) Press(0x27); // Right
        Check("caret moved by keyboard, read through TextPattern", WaitFor(() => OffsetOf(text.DocumentRange, text.GetSelection()[0]) == 3 && selectionEvents - before >= 3), $"caret={OffsetOf(text.DocumentRange, text.GetSelection()[0])} events={selectionEvents - before}");
        Press(0x28); // Down
        Check("caret moved down one line", WaitFor(() => OffsetOf(text.DocumentRange, text.GetSelection()[0]) == AppSample.IndexOf('\n') + 1 + 3), $"caret={OffsetOf(text.DocumentRange, text.GetSelection()[0])}");

        // selection by keyboard (Shift+Right x4) then by UIA
        before = selectionEvents;
        for (int i = 0; i < 4; i++) Press(0x27, shift: true);
        Check("keyboard selection read through TextPattern", WaitFor(() => text.GetSelection()[0].GetText(-1) == AppSample.Substring(AppSample.IndexOf('\n') + 1 + 3, 4)), $"selection='{Show(text.GetSelection()[0].GetText(-1))}' events={selectionEvents - before}");
        var word = doc.Clone();
        word.Move(TextUnit.Word, 1);
        before = selectionEvents;
        word.Select();
        Check("Select() through UIA changes the editor selection", WaitFor(() => text.GetSelection()[0].GetText(-1) == word.GetText(-1) && selectionEvents > before), $"'{Show(word.GetText(-1))}' events={selectionEvents - before}");
        var line = text.GetSelection()[0].Clone();
        line.ExpandToEnclosingUnit(TextUnit.Line);
        Check("ExpandToEnclosingUnit(Line)", line.GetText(-1) == AppSample[..(AppSample.IndexOf('\n') + 1)], Show(line.GetText(-1)));
        var rects = text.GetSelection()[0].GetBoundingRectangles();
        Check("selection rectangle lies inside the editor", rects.Length == 1 && rects[0].Width > 5 && editor.Current.BoundingRectangle.Contains(rects[0]), rects.Length > 0 ? rects[0].ToString() : "none");
        if (rects.Length == 1)
        {
            var back = text.RangeFromPoint(new System.Windows.Point(rects[0].X + 2, rects[0].Y + rects[0].Height / 2));
            Check("RangeFromPoint on the selection", OffsetOf(text.DocumentRange, back) == OffsetOf(text.DocumentRange, text.GetSelection()[0]), $"offset {OffsetOf(text.DocumentRange, back)}");
        }
        Check("TextPattern2.GetCaretRange is the end of the selection", CaretThroughComClient(c.NativeWindowHandle, 17, out string caretDetail), caretDetail);

        // typing inserts at the caret; Czech letters go through Unicode input
        Press(0x24, ctrl: true);
        before = textEvents;
        TypeText("ŽáX");
        Check("typing is visible in TextPattern with text-changed events", WaitFor(() => text.DocumentRange.GetText(-1).StartsWith("ŽáX", StringComparison.Ordinal) && textEvents - before >= 3), $"{textEvents - before} events, text '{Show(text.DocumentRange.GetText(24))}'");
        Check("caret after typing", WaitFor(() => OffsetOf(text.DocumentRange, text.GetSelection()[0]) == 3 && text.GetSelection()[0].GetText(-1) == ""));
        Press(0x08); // Backspace
        Check("backspace removes the typed X", WaitFor(() => text.DocumentRange.GetText(-1) == "Žá" + AppSample), Show(text.DocumentRange.GetText(8)));

        // focus between the tab strip and the editor
        var tab = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ClassNameProperty, "TabStripItem"));
        Check("tab strip item present in the same tree", tab is not null, tab is null ? "none" : $"name='{tab.Current.Name}'");
        lock (focusSources) focusSources.Clear();
        tab?.SetFocus();
        Check("focus moved to the tab strip", WaitFor(() => Same(AutomationElement.FocusedElement, tab) && !editor.Current.HasKeyboardFocus), $"focused={Describe(AutomationElement.FocusedElement)}");
        editor.SetFocus();
        Check("focus returned to the editor", WaitFor(() => Same(AutomationElement.FocusedElement, editor) && editor.Current.HasKeyboardFocus), $"focused={Describe(AutomationElement.FocusedElement)}");
        string events;
        lock (focusSources) events = string.Join(", ", focusSources);
        Check("focus-changed events named the tab item and the editor", events.Contains("ListItem") && events.Contains("Document:textida-editor"), events);
        // after the focus round trip the keyboard still edits the same text
        Press(0x23, ctrl: true); // Ctrl+End
        TypeText("!");
        Check("typing after the focus round trip", WaitFor(() => text.DocumentRange.GetText(-1).EndsWith("!", StringComparison.Ordinal)), $"tail '{Show(text.DocumentRange.GetText(-1)[^12..])}', win32 focus {FocusClass()}");
    }

    private static string Describe(AutomationElement? e)
    {
        try { return e is null ? "null" : $"{e.Current.ControlType.ProgrammaticName.Replace("ControlType.", "")}:{e.Current.AutomationId}:{e.Current.Name}"; }
        catch (ElementNotAvailableException) { return "gone"; }
    }

    private static AutomationElement? Ancestor(AutomationElement from, AutomationElement target)
    {
        for (var p = from; p is not null; p = TreeWalker.RawViewWalker.GetParent(p)) if (Same(p, target)) return target;
        return null;
    }

    // ---- input ----

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ushort Vk;
        [FieldOffset(10)] public ushort Scan;
        [FieldOffset(12)] public uint Flags;
    }

    [DllImport("user32.dll")] private static extern uint SendInput(uint n, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo { public uint Size, Flags; public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret; public int L, T, R, B; }
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint hwnd, System.Text.StringBuilder sb, int max);

    private static string FocusClass()
    {
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        GetGUIThreadInfo(0, ref info);
        var sb = new System.Text.StringBuilder(128);
        GetClassNameW(info.Focus, sb, 128);
        return $"{info.Focus} {sb}";
    }

    private static void ForceForeground(nint hwnd)
    {
        uint fg = GetWindowThreadProcessId(GetForegroundWindow(), out _), me = GetCurrentThreadId();
        // an Alt tap lifts the foreground lock that a console child would otherwise hit
        SendInput(2, [Key(0x12, false), Key(0x12, true)], Marshal.SizeOf<Input>());
        AttachThreadInput(me, fg, true);
        ShowWindow(hwnd, 9);
        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);
        AttachThreadInput(me, fg, false);
        Thread.Sleep(500);
        Console.WriteLine($"foreground is the app window: {GetForegroundWindow() == hwnd}");
        Console.WriteLine($"win32 focus window: {FocusClass()}");
    }

    private static Input Key(ushort vk, bool up, bool extended = false) => new() { Type = 1, Vk = vk, Flags = (up ? 2u : 0u) | (extended ? 1u : 0u) };

    private static void Press(ushort vk, bool shift = false, bool ctrl = false)
    {
        var seq = new List<Input>();
        if (ctrl) seq.Add(Key(0x11, false));
        if (shift) seq.Add(Key(0x10, false));
        seq.Add(Key(vk, false, extended: vk is >= 0x21 and <= 0x2E));
        seq.Add(Key(vk, true, extended: vk is >= 0x21 and <= 0x2E));
        if (shift) seq.Add(Key(0x10, true));
        if (ctrl) seq.Add(Key(0x11, true));
        SendInput((uint)seq.Count, [.. seq], Marshal.SizeOf<Input>());
        Thread.Sleep(80);
    }

    private static void TypeText(string s)
    {
        foreach (char ch in s)
        {
            var seq = new[] { new Input { Type = 1, Scan = ch, Flags = 4 }, new Input { Type = 1, Scan = ch, Flags = 4 | 2 } };
            SendInput(2, seq, Marshal.SizeOf<Input>());
            Thread.Sleep(80);
        }
    }
}
