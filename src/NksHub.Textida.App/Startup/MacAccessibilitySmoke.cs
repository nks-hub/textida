using System.Runtime.Versioning;
using NksHub.Textida.Accessibility.MacOS;

namespace NksHub.Textida.App.Startup;

/// <summary>
/// The macOS part of <c>--smoke</c>: reads the editor through the application's own NSAccessibility tree with the property
/// calls an AX client ends up in, but from inside the process, so it needs no Accessibility permission and no unlocked screen.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacAccessibilitySmoke
{
    private static string Text(nint element, string selector) => ObjC.ToManaged(ObjC.Msg(element, selector));

    private static string Role(nint element) => Text(element, "accessibilityRole");

    private static IEnumerable<nint> Children(nint element)
    {
        nint array = ObjC.Msg(element, "accessibilityChildren");
        if (array == 0) yield break;
        int count = (int)ObjC.Msg(array, "count");
        for (int i = 0; i < count; i++) yield return ObjC.Send(array, ObjC.Sel("objectAtIndex:"), (nint)i);
    }

    /// <summary>Depth-first search of the accessibility tree for the editor text element.</summary>
    public static nint FindEditor(nint element, int depth = 0)
    {
        if (Role(element) == "AXTextArea") return element;
        if (depth > 40) return 0;
        foreach (var child in Children(element))
            if (FindEditor(child, depth + 1) is var found and not 0) return found;
        return 0;
    }

    public static string Dump(nint root)
    {
        var sb = new System.Text.StringBuilder();
        void Walk(nint e, int depth)
        {
            if (depth > 30 || sb.Length > 12000) return;
            sb.Append(' ', depth * 2).Append(Role(e)).Append(" | ").Append(Text(e, "className")).Append(" | ").Append(Text(e, "accessibilityLabel")).Append('\n');
            foreach (var c in Children(e)) Walk(c, depth + 1);
        }
        Walk(root, 0);
        return sb.ToString();
    }

    public static string Value(nint editor) => Text(editor, "accessibilityValue");

    public static (int Location, int Length) SelectedRange(nint editor)
    {
        var range = ObjC.SendRange(editor, ObjC.Sel("accessibilitySelectedTextRange"));
        return ((int)range.Location, (int)range.Length);
    }

    public static string SelectedText(nint editor) => Text(editor, "accessibilitySelectedText");

    /// <summary>Role of the element the application reports as focused.</summary>
    public static string FocusedRole()
    {
        nint focused = ObjC.Msg(ObjC.Msg(ObjC.Class("NSApplication"), "sharedApplication"), "accessibilityFocusedUIElement");
        return focused == 0 ? "" : Role(focused);
    }
}
