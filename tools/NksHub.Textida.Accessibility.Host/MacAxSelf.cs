using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NksHub.Textida.Accessibility.MacOS;

namespace NksHub.Textida.Accessibility.Host;

/// <summary>
/// Reads the host's own accessibility tree with the AXUIElement client API from a worker thread (the main thread has to
/// answer the request, so asking from it would deadlock). macOS may refuse this without the Accessibility permission;
/// the result then says so with the AXError code.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class MacAxSelf
{
    private const string AppServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [LibraryImport(AppServices)] private static partial nint AXUIElementCreateApplication(int pid);
    [LibraryImport(AppServices)] private static partial int AXUIElementCopyAttributeValue(nint element, nint attribute, out nint value);
    [LibraryImport(AppServices)] [return: MarshalAs(UnmanagedType.U1)] private static partial bool AXIsProcessTrusted();
    [LibraryImport(AppServices)] [return: MarshalAs(UnmanagedType.U1)] private static partial bool AXValueGetValue(nint value, int type, NSRange* range);
    [LibraryImport(CoreFoundation)] private static partial nint CFStringCreateWithCharacters(nint allocator, char* chars, nint length);
    [LibraryImport(CoreFoundation)] private static partial nint CFArrayGetCount(nint array);
    [LibraryImport(CoreFoundation)] private static partial nint CFArrayGetValueAtIndex(nint array, nint index);
    [LibraryImport(CoreFoundation)] private static partial nint CFGetTypeID(nint cf);
    [LibraryImport(CoreFoundation)] private static partial nint CFArrayGetTypeID();

    private static nint Cf(string s) { fixed (char* p = s) return CFStringCreateWithCharacters(0, p, s.Length); }

    private static int Copy(nint element, string attribute, out nint value) => AXUIElementCopyAttributeValue(element, Cf(attribute), out value);

    private static nint FindTextArea(nint element, int depth)
    {
        if (depth > 8) return 0;
        if (Copy(element, "AXRole", out nint role) == 0 && ObjC.ToManaged(role) == "AXTextArea") return element;
        foreach (var attr in new[] { "AXWindows", "AXChildren" })
        {
            if (Copy(element, attr, out nint kids) != 0 || kids == 0 || CFGetTypeID(kids) != CFArrayGetTypeID()) continue;
            for (nint i = 0; i < CFArrayGetCount(kids); i++)
                if (FindTextArea(CFArrayGetValueAtIndex(kids, i), depth + 1) is var found and not 0) return found;
        }
        return 0;
    }

    private static void Dump(nint element, int depth)
    {
        if (depth > 6) return;
        string role = Copy(element, "AXRole", out nint r) == 0 ? ObjC.ToManaged(r) : "?";
        string title = Copy(element, "AXDescription", out nint d) == 0 && d != 0 ? ObjC.ToManaged(d) : "";
        Console.WriteLine($"AXSELF {new string(' ', depth * 2)}{role} '{title}'");
        foreach (var attr in new[] { "AXWindows", "AXChildren" })
        {
            if (Copy(element, attr, out nint kids) != 0 || kids == 0 || CFGetTypeID(kids) != CFArrayGetTypeID()) continue;
            for (nint i = 0; i < CFArrayGetCount(kids); i++) Dump(CFArrayGetValueAtIndex(kids, i), depth + 1);
        }
    }

    public static void Run()
    {
        var thread = new Thread(() =>
        {
            Console.WriteLine($"AXSELF AXIsProcessTrusted={AXIsProcessTrusted()}");
            nint app = AXUIElementCreateApplication(Environment.ProcessId);
            int err = Copy(app, "AXRole", out nint appRole);
            Console.WriteLine($"AXSELF application AXRole error={err} value={(err == 0 ? ObjC.ToManaged(appRole) : "-")}");
            if (err == 0)
            {
                foreach (var attr in new[] { "AXWindows", "AXMainWindow", "AXFocusedWindow", "AXChildren" })
                {
                    int e = Copy(app, attr, out nint v);
                    Console.WriteLine($"AXSELF app {attr}: error={e} count={(e == 0 && v != 0 && CFGetTypeID(v) == CFArrayGetTypeID() ? CFArrayGetCount(v) : -1)}");
                }
                Dump(app, 0);
                nint area = FindTextArea(app, 0);
                Console.WriteLine($"AXSELF text area found={area != 0}");
                if (area != 0 && Copy(area, "AXValue", out nint value) == 0) Console.WriteLine("AXSELF AXValue=" + Program.Escape(ObjC.ToManaged(value)));
                NSRange r;
                if (area != 0 && Copy(area, "AXSelectedTextRange", out nint rv) == 0 && AXValueGetValue(rv, 4, &r)) Console.WriteLine($"AXSELF AXSelectedTextRange={r.Location},{r.Length}");
            }
            Console.Out.Flush();
        });
        thread.Start();
    }
}
