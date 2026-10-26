using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NksHub.Textida.Accessibility.MacOS;

/// <summary>
/// Publishes an <see cref="ITextAutomationSurface"/> through NSAccessibility on an NSView subclass: role AXTextArea, AXValue,
/// AXSelectedText, AXSelectedTextRange (settable), AXNumberOfCharacters, AXVisibleCharacterRange, AXInsertionPointLineNumber,
/// the parameterized line, range, string, bounds and position attributes, and the selection and value-changed notifications.
/// Two entry points are installed: the attribute based methods (<c>accessibilityAttributeNames</c>,
/// <c>accessibilityAttributeValue:</c> and so on), which the accessibility server calls for AXUIElement requests, and the
/// method per attribute form (<c>accessibilityValue</c>, <c>accessibilitySelectedTextRange</c> and so on). Everything is a
/// plain function pointer added to the class through the Objective-C runtime, so nothing needs reflection and it runs
/// under NativeAOT. Not wired yet: attaching the methods to the NSView that Avalonia.Native creates for a window.
/// </summary>
[SupportedOSPlatform("macos")]
public static unsafe partial class MacAxBridge
{
    private static readonly ConcurrentDictionary<nint, Registration> Views = new();
    private sealed record Registration(ITextAutomationSurface Surface, string Label);

    private static readonly string[] TextAttributes =
        ["AXValue", "AXNumberOfCharacters", "AXSelectedText", "AXSelectedTextRange", "AXVisibleCharacterRange", "AXInsertionPointLineNumber"];
    private static readonly string[] ParameterizedAttributes =
        ["AXLineForIndex", "AXRangeForLine", "AXStringForRange", "AXRangeForPosition", "AXRangeForIndex", "AXBoundsForRange"];

    [LibraryImport("/System/Library/Frameworks/AppKit.framework/AppKit")]
    private static partial void NSAccessibilityPostNotification(nint element, nint notification);
    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial uint CGMainDisplayID();
    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial NSRect CGDisplayBounds(uint display);
    [LibraryImport("/usr/lib/libobjc.A.dylib")]
    private static partial nint class_getMethodImplementation(nint cls, nint selector);

    public static double PrimaryScreenHeight => CGDisplayBounds(CGMainDisplayID()).Height;

    /// <summary>Adds the accessibility methods to a class that derives from NSView. Call once per class, before instances exist.</summary>
    public static void AddAccessibilityMethods(nint nsViewSubclass)
    {
        ObjC.AddProtocol(nsViewSubclass, "NSAccessibilityNavigableStaticText");

        ObjC.AddMethod(nsViewSubclass, "accessibilityAttributeNames", (delegate* unmanaged<nint, nint, nint>)&AttributeNames, "@16@0:8");
        ObjC.AddMethod(nsViewSubclass, "accessibilityParameterizedAttributeNames", (delegate* unmanaged<nint, nint, nint>)&ParameterizedAttributeNames, "@16@0:8");
        ObjC.AddMethod(nsViewSubclass, "accessibilityAttributeValue:", (delegate* unmanaged<nint, nint, nint, nint>)&AttributeValue, "@24@0:8@16");
        ObjC.AddMethod(nsViewSubclass, "accessibilityAttributeValue:forParameter:", (delegate* unmanaged<nint, nint, nint, nint, nint>)&ParameterizedValue, "@32@0:8@16@24");
        ObjC.AddMethod(nsViewSubclass, "accessibilityIsAttributeSettable:", (delegate* unmanaged<nint, nint, nint, byte>)&IsAttributeSettable, "B24@0:8@16");
        ObjC.AddMethod(nsViewSubclass, "accessibilitySetValue:forAttribute:", (delegate* unmanaged<nint, nint, nint, nint, void>)&SetAttributeValue, "v32@0:8@16@24");
        ObjC.AddMethod(nsViewSubclass, "accessibilityIsIgnored", (delegate* unmanaged<nint, nint, byte>)&IsIgnored, "B16@0:8");

        ObjC.AddMethod(nsViewSubclass, "isAccessibilityElement", (delegate* unmanaged<nint, nint, byte>)&IsAccessibilityElement, "B16@0:8");
        ObjC.AddMethod(nsViewSubclass, "accessibilityRole", (delegate* unmanaged<nint, nint, nint>)&Role, "@16@0:8");
        ObjC.AddMethod(nsViewSubclass, "accessibilityLabel", (delegate* unmanaged<nint, nint, nint>)&Label, "@16@0:8");
        ObjC.AddMethod(nsViewSubclass, "accessibilityValue", (delegate* unmanaged<nint, nint, nint>)&Value, "@16@0:8");
        ObjC.AddMethod(nsViewSubclass, "accessibilityNumberOfCharacters", (delegate* unmanaged<nint, nint, nint>)&NumberOfCharacters, "q16@0:8");
        ObjC.AddMethod(nsViewSubclass, "accessibilitySelectedText", (delegate* unmanaged<nint, nint, nint>)&SelectedText, "@16@0:8");
        ObjC.AddMethod(nsViewSubclass, "accessibilitySelectedTextRange", (delegate* unmanaged<nint, nint, NSRange>)&SelectedTextRange, "{_NSRange=QQ}16@0:8");
        ObjC.AddMethod(nsViewSubclass, "setAccessibilitySelectedTextRange:", (delegate* unmanaged<nint, nint, NSRange, void>)&SetSelectedTextRange, "v32@0:8{_NSRange=QQ}16");
        ObjC.AddMethod(nsViewSubclass, "accessibilityVisibleCharacterRange", (delegate* unmanaged<nint, nint, NSRange>)&VisibleCharacterRange, "{_NSRange=QQ}16@0:8");
        ObjC.AddMethod(nsViewSubclass, "accessibilityInsertionPointLineNumber", (delegate* unmanaged<nint, nint, nint>)&InsertionPointLineNumber, "q16@0:8");
        ObjC.AddMethod(nsViewSubclass, "accessibilityLineForIndex:", (delegate* unmanaged<nint, nint, nint, nint>)&LineForIndex, "q24@0:8q16");
        ObjC.AddMethod(nsViewSubclass, "accessibilityRangeForLine:", (delegate* unmanaged<nint, nint, nint, NSRange>)&RangeForLine, "{_NSRange=QQ}24@0:8q16");
        ObjC.AddMethod(nsViewSubclass, "accessibilityRangeForIndex:", (delegate* unmanaged<nint, nint, nint, NSRange>)&RangeForIndex, "{_NSRange=QQ}24@0:8Q16");
        ObjC.AddMethod(nsViewSubclass, "accessibilityStringForRange:", (delegate* unmanaged<nint, nint, NSRange, nint>)&StringForRange, "@32@0:8{_NSRange=QQ}16");
        ObjC.AddMethod(nsViewSubclass, "accessibilityFrameForRange:", (delegate* unmanaged<nint, nint, NSRange, NSRect>)&FrameForRange, "{CGRect={CGPoint=dd}{CGSize=dd}}32@0:8{_NSRange=QQ}16");
        ObjC.AddMethod(nsViewSubclass, "accessibilityRangeForPosition:", (delegate* unmanaged<nint, nint, NSPoint, NSRange>)&RangeForPosition, "{_NSRange=QQ}32@0:8{CGPoint=dd}16");
    }

    /// <summary>Connects an instance of a class prepared with <see cref="AddAccessibilityMethods"/> to its surface and starts posting notifications.</summary>
    public static void Attach(nint view, ITextAutomationSurface surface, string label)
    {
        Views[view] = new Registration(surface, label);
        surface.TextChanged += _ => Post(view, "AXValueChanged");
        surface.SelectionChanged += () => Post(view, "AXSelectedTextChanged");
    }

    private static void Post(nint view, string notification) =>
        NSAccessibilityPostNotification(view, ObjC.NewString(notification));

    private static ITextAutomationSurface? Surface(nint self) => Views.TryGetValue(self, out var r) ? r.Surface : null;

    private static NSRange ToRange(TextSpan s) => new((nuint)s.Start, (nuint)s.Length);

    private static TextSpan Clamp(ITextAutomationSurface s, NSRange r)
    {
        int start = (int)Math.Min((nuint)s.Length, r.Location);
        return new(start, (int)Math.Min((nuint)(s.Length - start), r.Length));
    }

    // ---- shared logic, used by both entry points ----

    private static NSRange LineRange(ITextAutomationSurface s, nint line)
    {
        if (line < 0 || line >= s.LineCount) return default;
        int start = s.LineStart((int)line);
        return new((nuint)start, (nuint)(s.LineStart((int)line + 1) - start));
    }

    private static NSRange CharacterRange(ITextAutomationSurface s, nint index)
    {
        if (index < 0 || index >= s.Length) return default;
        int start = TextUnits.UnitStart(s, TextUnit.Character, (int)index);
        return new((nuint)start, (nuint)(TextUnits.UnitEnd(s, TextUnit.Character, start) - start));
    }

    private static NSRect Frame(ITextAutomationSurface s, NSRange range)
    {
        var span = Clamp(s, range);
        var rects = s.GetRangeBounds(span.Start, span.Length);
        if (rects.Count == 0) return default;
        double l = rects.Min(r => r.X), t = rects.Min(r => r.Y), right = rects.Max(r => r.X + r.Width), b = rects.Max(r => r.Y + r.Height);
        // The surface reports top-left screen coordinates; AppKit wants the origin at the bottom left of the primary display.
        return new(l, PrimaryScreenHeight - b, right - l, b - t);
    }

    private static NSRange PositionRange(ITextAutomationSurface s, NSPoint point)
    {
        int offset = s.OffsetFromPoint(point.X, PrimaryScreenHeight - point.Y);
        return offset < 0 ? default : CharacterRange(s, offset);
    }

    private static nint Number(nint value) => ObjC.Send(ObjC.Class("NSNumber"), ObjC.Sel("numberWithInteger:"), value);
    private static nint Boxed(NSRange range) => ObjC.Send(ObjC.Class("NSValue"), ObjC.Sel("valueWithRange:"), range);
    private static nint Boxed(NSRect rect) => ObjC.Send(ObjC.Class("NSValue"), ObjC.Sel("valueWithRect:"), rect);

    private static nint Super(nint self, nint cmd) =>
        ((delegate* unmanaged<nint, nint, nint>)class_getMethodImplementation(ObjC.Class("NSView"), cmd))(self, cmd);

    private static nint Super(nint self, nint cmd, nint a) =>
        ((delegate* unmanaged<nint, nint, nint, nint>)class_getMethodImplementation(ObjC.Class("NSView"), cmd))(self, cmd, a);

    private static nint Super(nint self, nint cmd, nint a, nint b) =>
        ((delegate* unmanaged<nint, nint, nint, nint, nint>)class_getMethodImplementation(ObjC.Class("NSView"), cmd))(self, cmd, a, b);

    // ---- attribute based entry points ----

    [UnmanagedCallersOnly]
    private static nint AttributeNames(nint self, nint cmd)
    {
        nint names = ObjC.Send(ObjC.Class("NSMutableArray"), ObjC.Sel("arrayWithArray:"), Super(self, cmd));
        foreach (var a in TextAttributes) ObjC.Send(names, ObjC.Sel("addObject:"), ObjC.NewString(a));
        return names;
    }

    [UnmanagedCallersOnly]
    private static nint ParameterizedAttributeNames(nint self, nint cmd)
    {
        nint names = ObjC.Msg(ObjC.Class("NSMutableArray"), "array");
        foreach (var a in ParameterizedAttributes) ObjC.Send(names, ObjC.Sel("addObject:"), ObjC.NewString(a));
        return names;
    }

    [UnmanagedCallersOnly]
    private static nint AttributeValue(nint self, nint cmd, nint attribute)
    {
        if (Surface(self) is not { } s) return Super(self, cmd, attribute);
        switch (ObjC.ToManaged(attribute))
        {
            case "AXRole": return ObjC.NewString("AXTextArea");
            case "AXDescription": return ObjC.NewString(Views[self].Label);
            case "AXValue": return ObjC.NewString(s.GetText(0, s.Length));
            case "AXNumberOfCharacters": return Number(s.Length);
            case "AXSelectedText": return ObjC.NewString(s.GetText(s.Selection.Start, s.Selection.Length));
            case "AXSelectedTextRange": return Boxed(ToRange(s.Selection));
            case "AXVisibleCharacterRange": return Boxed(ToRange(s.VisibleRange));
            case "AXInsertionPointLineNumber": return Number(s.LineOfOffset(s.CaretOffset));
            default: return Super(self, cmd, attribute);
        }
    }

    [UnmanagedCallersOnly]
    private static nint ParameterizedValue(nint self, nint cmd, nint attribute, nint parameter)
    {
        if (Surface(self) is not { } s) return 0;
        switch (ObjC.ToManaged(attribute))
        {
            case "AXLineForIndex": return Number(s.LineOfOffset((int)Math.Clamp(ObjC.Msg(parameter, "integerValue"), 0, s.Length)));
            case "AXRangeForLine": return Boxed(LineRange(s, ObjC.Msg(parameter, "integerValue")));
            case "AXStringForRange":
                var span = Clamp(s, ObjC.SendRange(parameter, ObjC.Sel("rangeValue")));
                return ObjC.NewString(s.GetText(span.Start, span.Length));
            case "AXRangeForPosition": return Boxed(PositionRange(s, ObjC.SendPoint(parameter, ObjC.Sel("pointValue"))));
            case "AXRangeForIndex": return Boxed(CharacterRange(s, ObjC.Msg(parameter, "integerValue")));
            case "AXBoundsForRange": return Boxed(Frame(s, ObjC.SendRange(parameter, ObjC.Sel("rangeValue"))));
            default: return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static byte IsAttributeSettable(nint self, nint cmd, nint attribute) =>
        ObjC.ToManaged(attribute) == "AXSelectedTextRange" && Surface(self) is not null ? (byte)1 : (byte)Super(self, cmd, attribute);

    [UnmanagedCallersOnly]
    private static void SetAttributeValue(nint self, nint cmd, nint value, nint attribute)
    {
        if (ObjC.ToManaged(attribute) == "AXSelectedTextRange" && Surface(self) is { } s)
        {
            var span = Clamp(s, ObjC.SendRange(value, ObjC.Sel("rangeValue")));
            s.SetSelection(span.Start, span.Length);
        }
        else Super(self, cmd, value, attribute);
    }

    [UnmanagedCallersOnly] private static byte IsIgnored(nint self, nint cmd) => 0;

    // ---- method per attribute entry points ----

    [UnmanagedCallersOnly] private static byte IsAccessibilityElement(nint self, nint cmd) => 1;
    [UnmanagedCallersOnly] private static nint Role(nint self, nint cmd) => ObjC.NewString("AXTextArea");
    [UnmanagedCallersOnly] private static nint Label(nint self, nint cmd) => ObjC.NewString(Views.TryGetValue(self, out var r) ? r.Label : "");

    [UnmanagedCallersOnly]
    private static nint Value(nint self, nint cmd) =>
        Surface(self) is { } s ? ObjC.NewString(s.GetText(0, s.Length)) : 0;

    [UnmanagedCallersOnly]
    private static nint NumberOfCharacters(nint self, nint cmd) => Surface(self)?.Length ?? 0;

    [UnmanagedCallersOnly]
    private static nint SelectedText(nint self, nint cmd) =>
        Surface(self) is { } s ? ObjC.NewString(s.GetText(s.Selection.Start, s.Selection.Length)) : 0;

    [UnmanagedCallersOnly]
    private static NSRange SelectedTextRange(nint self, nint cmd) =>
        Surface(self) is { } s ? ToRange(s.Selection) : default;

    [UnmanagedCallersOnly]
    private static void SetSelectedTextRange(nint self, nint cmd, NSRange range)
    {
        if (Surface(self) is not { } s) return;
        var span = Clamp(s, range);
        s.SetSelection(span.Start, span.Length);
    }

    [UnmanagedCallersOnly]
    private static NSRange VisibleCharacterRange(nint self, nint cmd) =>
        Surface(self) is { } s ? ToRange(s.VisibleRange) : default;

    [UnmanagedCallersOnly]
    private static nint InsertionPointLineNumber(nint self, nint cmd) =>
        Surface(self) is { } s ? s.LineOfOffset(s.CaretOffset) : 0;

    [UnmanagedCallersOnly]
    private static nint LineForIndex(nint self, nint cmd, nint index) =>
        Surface(self) is { } s ? s.LineOfOffset((int)Math.Clamp(index, 0, s.Length)) : 0;

    [UnmanagedCallersOnly]
    private static NSRange RangeForLine(nint self, nint cmd, nint line) =>
        Surface(self) is { } s ? LineRange(s, line) : default;

    [UnmanagedCallersOnly]
    private static NSRange RangeForIndex(nint self, nint cmd, nint index) =>
        Surface(self) is { } s ? CharacterRange(s, index) : default;

    [UnmanagedCallersOnly]
    private static nint StringForRange(nint self, nint cmd, NSRange range)
    {
        if (Surface(self) is not { } s) return 0;
        var span = Clamp(s, range);
        return ObjC.NewString(s.GetText(span.Start, span.Length));
    }

    [UnmanagedCallersOnly]
    private static NSRect FrameForRange(nint self, nint cmd, NSRange range) =>
        Surface(self) is { } s ? Frame(s, range) : default;

    [UnmanagedCallersOnly]
    private static NSRange RangeForPosition(nint self, nint cmd, NSPoint point) =>
        Surface(self) is { } s ? PositionRange(s, point) : default;
}
