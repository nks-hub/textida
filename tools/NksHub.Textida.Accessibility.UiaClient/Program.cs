using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace NksHub.Textida.Accessibility.UiaClient;

/// <summary>
/// Starts the published accessibility host and reads it through UI Automation from this separate process:
/// element identity, DocumentRange text, selection, caret, units, bounding rectangles, and the text, selection and focus events.
/// Exit code is the number of failed checks.
/// </summary>
internal static partial class Program
{
    private const string Sample = "Hello Textida\r\nsecond line, with words\r\nthird: \u011b\u0161\u010d\u0159 \U0001F600 end\r\n";
    private const uint WmKeyDown = 0x100, WmChar = 0x102;
    private const int VkBack = 8, VkRight = 0x27, VkLeft = 0x25, VkDown = 0x28, VkEnd = 0x23;

    private static int _failed;
    private static int _textEvents, _selectionEvents, _focusEvents;
    private static readonly List<bool> FocusProperty = [];

    [DllImport("user32.dll")] private static extern bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    private static void Check(string name, bool ok, string detail = "")
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? ": " + detail : "")}");
        if (!ok) _failed++;
    }

    private static string Show(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n");

    private static bool WaitFor(Func<bool> condition, int ms = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            if (condition()) return true;
            Thread.Sleep(20);
        }
        return condition();
    }

    private static int Main(string[] args)
    {
        if (args is ["app", var appExe]) return RunApp(appExe);
        var psi = new ProcessStartInfo(args[0], "--exit-after 90") { RedirectStandardOutput = true, UseShellExecute = false };
        using var host = Process.Start(psi)!;
        string? ready = host.StandardOutput.ReadLine();
        Console.WriteLine($"host: {ready}  (this client pid {Environment.ProcessId})");
        if (ready is null || !ready.StartsWith("READY")) { Console.WriteLine("FAIL host did not start"); return 1; }
        nint hwnd = nint.Parse(ready[(ready.IndexOf("hwnd=") + 5)..]);

        AutomationElement? editor = null;
        WaitFor(() => (editor = AutomationElement.RootElement.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, host.Id),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "textida-editor")))) is not null);
        if (editor is null) { Console.WriteLine("FAIL editor element not found through UIA"); host.Kill(); return 1; }

        RunChecks(editor, hwnd, host);

        WaitFor(() => host.HasExited, 10000);
        if (!host.HasExited) host.Kill();
        Console.WriteLine(_failed == 0 ? "ALL PASSED" : $"{_failed} FAILED");
        return _failed;
    }

    private static void RunChecks(AutomationElement editor, nint hwnd, Process host)
    {
        var c = editor.Current;
        Check("identity", c.ControlType == ControlType.Document && c.AutomationId == "textida-editor" && c.ClassName == "TextidaEditor" && c.NativeWindowHandle == hwnd,
            $"ControlType={c.ControlType.ProgrammaticName} Name='{c.Name}' ClassName={c.ClassName} FrameworkId={c.FrameworkId} NativeWindowHandle={c.NativeWindowHandle}");
        Check("TextPattern available", (bool)editor.GetCurrentPropertyValue(AutomationElement.IsTextPatternAvailableProperty));
        var r = c.BoundingRectangle;
        Check("bounding rectangle", r.Width > 100 && r.Height > 50, r.ToString());

        var text = (TextPattern)editor.GetCurrentPattern(TextPattern.Pattern);
        Check("SupportedTextSelection", text.SupportedTextSelection == SupportedTextSelection.Single, text.SupportedTextSelection.ToString());

        Automation.AddAutomationEventHandler(TextPattern.TextChangedEvent, editor, TreeScope.Element, (_, _) => Interlocked.Increment(ref _textEvents));
        Automation.AddAutomationEventHandler(TextPattern.TextSelectionChangedEvent, editor, TreeScope.Element, (_, _) => Interlocked.Increment(ref _selectionEvents));
        Automation.AddAutomationPropertyChangedEventHandler(editor, TreeScope.Element, (_, e) => { lock (FocusProperty) FocusProperty.Add((bool)e.NewValue); }, AutomationElement.HasKeyboardFocusProperty);
        Automation.AddAutomationFocusChangedEventHandler((_, _) => Interlocked.Increment(ref _focusEvents));
        Thread.Sleep(300);

        var doc = text.DocumentRange;
        string all = doc.GetText(-1);
        Check("DocumentRange text", all == Sample, $"{all.Length} units: '{Show(all)}'");
        Check("GetText(maxLength)", doc.GetText(5) == "Hello");

        var sel = text.GetSelection();
        Check("initial selection is a caret at 0", sel.Length == 1 && sel[0].GetText(-1) == "" && OffsetOf(doc, sel[0]) == 0, $"ranges={sel.Length}");

        // caret moves from the keyboard path of the host
        int before = _selectionEvents;
        for (int i = 0; i < 5; i++) PostMessageW(hwnd, WmKeyDown, VkRight, 0);
        bool moved = WaitFor(() => OffsetOf(doc, text.GetSelection()[0]) == 5 && _selectionEvents - before >= 5);
        Check("caret move events", moved, $"caret={OffsetOf(doc, text.GetSelection()[0])} selection-changed events={_selectionEvents - before}");

        // UIA Move/Expand/Select, then the selection as the host reports it
        var word = doc.Clone();
        word.Move(TextUnit.Word, 1);
        Check("Move(Word,1) from the document start", word.GetText(-1) == "Textida", $"'{Show(word.GetText(-1))}' at {OffsetOf(doc, word)}");
        before = _selectionEvents;
        word.Select();
        Check("Select() raises the selection event and sets the host selection",
            WaitFor(() => _selectionEvents > before) && text.GetSelection()[0].GetText(-1) == "Textida" && OffsetOf(doc, text.GetSelection()[0]) == 6);

        var line = text.GetSelection()[0].Clone();
        line.ExpandToEnclosingUnit(TextUnit.Line);
        Check("ExpandToEnclosingUnit(Line)", line.GetText(-1) == "Hello Textida\r\n", Show(line.GetText(-1)));
        Check("Move(Line,1)", line.Move(TextUnit.Line, 1) == 1 && line.GetText(-1) == "second line, with words\r\n", Show(line.GetText(-1)));
        line.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Line, 1);
        Check("MoveEndpointByUnit(End,Line,1)", line.GetText(-1).StartsWith("second") && line.GetText(-1).EndsWith("end\r\n"), $"{line.GetText(-1).Length} units");
        var para = doc.Clone();
        para.Move(TextUnit.Paragraph, 2);
        Check("Move(Paragraph,2)", para.GetText(-1).StartsWith("third:"), Show(para.GetText(-1)));

        var found = doc.FindText("words", false, false);
        Check("FindText", found is not null && OffsetOf(doc, found) == Sample.IndexOf("words", StringComparison.Ordinal) && found.GetText(-1) == "words");
        var emoji = doc.FindText("\U0001F600", false, false);
        var ch = emoji!.Clone();
        ch.ExpandToEnclosingUnit(TextUnit.Character);
        Check("surrogate pair is one character", ch.GetText(-1) == "\U0001F600");
        ch.Move(TextUnit.Character, 1);
        Check("Move(Character,1) after the emoji", ch.GetText(-1) == " ");
        Check("IsReadOnly attribute", (bool)doc.GetAttributeValue(TextPattern.IsReadOnlyAttribute) == false);

        // geometry
        var rects = found!.GetBoundingRectangles();
        var bounds = editor.Current.BoundingRectangle;
        bool inside = rects.Length == 1 && rects[0].Width > 20 && bounds.Contains(rects[0]);
        Check("bounding rectangle of a word", inside, rects.Length > 0 ? rects[0].ToString() : "none");
        if (rects.Length == 1)
        {
            var back = text.RangeFromPoint(new System.Windows.Point(rects[0].X + 2, rects[0].Y + rects[0].Height / 2));
            Check("RangeFromPoint", OffsetOf(doc, back) == Sample.IndexOf("words", StringComparison.Ordinal), $"offset {OffsetOf(doc, back)}");
        }
        var twoLines = doc.Clone();
        twoLines.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Line, -1);
        Check("multi-line range has one rectangle per line", twoLines.GetBoundingRectangles().Length == 2, $"{twoLines.GetBoundingRectangles().Length} rectangles");

        // TextPattern2.GetCaretRange needs the UIA3 COM client
        Check("TextPattern2.GetCaretRange", CaretThroughComClient(hwnd, 13, out string caretDetail), caretDetail);

        // typing replaces the selection ("Textida"), backspace deletes
        int textBefore = _textEvents;
        PostMessageW(hwnd, WmChar, 'X', 0);
        PostMessageW(hwnd, WmChar, 'Y', 0);
        Check("typing raises text-changed events", WaitFor(() => _textEvents - textBefore >= 2 && doc.GetText(-1).StartsWith("Hello XY\r\n")), $"{_textEvents - textBefore} events, text '{Show(doc.GetText(20))}'");
        Check("caret after typing", OffsetOf(doc, text.GetSelection()[0]) == 8 && text.GetSelection()[0].GetText(-1) == "");
        PostMessageW(hwnd, WmKeyDown, VkBack, 0);
        PostMessageW(hwnd, WmKeyDown, VkBack, 0);
        Check("backspace", WaitFor(() => doc.GetText(-1).StartsWith("Hello \r\nsecond")), Show(doc.GetText(20)));

        // focus: leave to the tab button and come back
        var button = AutomationElement.RootElement.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, host.Id),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)));
        button?.SetFocus();
        Check("focus left the editor", WaitFor(() => !editor.Current.HasKeyboardFocus && button!.Current.HasKeyboardFocus));
        int focusBefore = _focusEvents;
        editor.SetFocus();
        Check("focus returned to the editor", WaitFor(() => editor.Current.HasKeyboardFocus && _focusEvents > focusBefore), $"focus events {_focusEvents - focusBefore}");
        string props;
        lock (FocusProperty) props = string.Join(",", FocusProperty);
        Check("HasKeyboardFocus property events", props.Contains("False") && props.Contains("True"), props);

        // close the window and compare the host's own final text with what the UIA reads said
        string finalUia = doc.GetText(-1);
        var main = AutomationElement.RootElement.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, host.Id));
        ((WindowPattern)main.GetCurrentPattern(WindowPattern.Pattern)).Close();
        string? final = null;
        for (string? l; (l = host.StandardOutput.ReadLine()) is not null;) if (l.StartsWith("FINAL ")) final = l[6..];
        Check("host document equals the last UIA read", final == Escape(finalUia), $"host: {final}");
        Check("final text is the expected edit", finalUia == "Hello \r\nsecond line, with words\r\nthird: \u011b\u0161\u010d\u0159 \U0001F600 end\r\n", Show(finalUia));
        Automation.RemoveAllEventHandlers();
    }

    private static int OffsetOf(TextPatternRange doc, TextPatternRange range)
    {
        var head = doc.Clone();
        head.MoveEndpointByRange(TextPatternRangeEndpoint.End, range, TextPatternRangeEndpoint.Start);
        return head.GetText(-1).Length;
    }

    private static string Escape(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char ch in s)
            sb.Append(ch switch { '\r' => "\\r", '\n' => "\\n", '\t' => "\\t", '\\' => "\\\\", < ' ' or > '~' => $"\\u{(int)ch:x4}", _ => ch.ToString() });
        return sb.ToString();
    }

    // ---- UIA3 COM client, only for TextPattern2 which the managed wrapper does not offer ----

    private static bool CaretThroughComClient(nint hwnd, int expectedOffset, out string detail)
    {
        var uia = (IUIAutomation)new CUIAutomation();
        uia.ElementFromHandle(hwnd, out var element);
        element.GetCurrentPattern(10024, out object pattern);
        var text2 = (IUIAutomationTextPattern2)pattern;
        text2.GetCaretRange(out int active, out var caret);
        caret.GetText(-1, out string caretText);
        text2.get_DocumentRange(out var doc);
        doc.Clone(out var head);
        head.MoveEndpointByRange(1, caret, 0);
        head.GetText(-1, out string prefix);
        detail = $"isActive={active} caretText='{caretText}' caretOffset={prefix.Length}";
        return active != 0 && caretText == "" && prefix.Length == expectedOffset;
    }

    [ComImport, Guid("ff48dba4-60ef-4201-aa87-54103eef594e")] private class CUIAutomation { }

    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        void CompareElements(); void CompareRuntimeIds(); void GetRootElement();
        [PreserveSig] int ElementFromHandle(nint hwnd, out IUIAutomationElement element);
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        void SetFocus(); void GetRuntimeId(); void FindFirst(); void FindAll(); void FindFirstBuildCache(); void FindAllBuildCache(); void BuildUpdatedCache();
        void GetCurrentPropertyValue(); void GetCurrentPropertyValueEx(); void GetCachedPropertyValue(); void GetCachedPropertyValueEx();
        void GetCurrentPatternAs(); void GetCachedPatternAs();
        [PreserveSig] int GetCurrentPattern(int patternId, [MarshalAs(UnmanagedType.IUnknown)] out object pattern);
    }

    [ComImport, Guid("506a921a-fcc9-409f-b23b-37eb74106872"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextPattern2
    {
        void RangeFromPoint(); void RangeFromChild(); void GetSelection(); void GetVisibleRanges();
        [PreserveSig] int get_DocumentRange(out IUIAutomationTextRange range);
        void get_SupportedTextSelection();
        void RangeFromAnnotation();
        [PreserveSig] int GetCaretRange(out int isActive, out IUIAutomationTextRange range);
    }

    [ComImport, Guid("a543cc6a-f4ae-494b-8239-c814481187a8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRange
    {
        [PreserveSig] int Clone(out IUIAutomationTextRange clone);
        void Compare(); void CompareEndpoints(); void ExpandToEnclosingUnit(); void FindAttribute(); void FindText(); void GetAttributeValue(); void GetBoundingRectangles(); void GetEnclosingElement();
        [PreserveSig] int GetText(int maxLength, [MarshalAs(UnmanagedType.BStr)] out string text);
        void Move(); void MoveEndpointByUnit();
        [PreserveSig] int MoveEndpointByRange(int endpoint, IUIAutomationTextRange target, int targetEndpoint);
    }
}
