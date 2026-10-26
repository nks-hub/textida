using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace NksHub.Textida.Accessibility.MacOS;

[StructLayout(LayoutKind.Sequential)]
public struct NSRange(nuint location, nuint length)
{
    public nuint Location = location;
    public nuint Length = length;
}

[StructLayout(LayoutKind.Sequential)]
public struct NSPoint(double x, double y) { public double X = x, Y = y; }

[StructLayout(LayoutKind.Sequential)]
public struct NSSize(double width, double height) { public double Width = width, Height = height; }

[StructLayout(LayoutKind.Sequential)]
public struct NSRect(double x, double y, double width, double height)
{
    public double X = x, Y = y, Width = width, Height = height;
}

/// <summary>
/// Minimal Objective-C runtime access with plain P/Invoke and function pointers, so it needs no reflection and
/// no built-in COM or Objective-C interop and runs under NativeAOT. Struct returns use the arm64 convention
/// (the registers, never <c>objc_msgSend_stret</c>); the x64 Mac build must not call the NSRect returning wrappers.
/// </summary>
[SupportedOSPlatform("macos")]
public static unsafe partial class ObjC
{
    private const string Lib = "/usr/lib/libobjc.A.dylib";

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] public static partial nint objc_getClass(string name);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] public static partial nint sel_registerName(string name);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] public static partial nint objc_allocateClassPair(nint superclass, string name, nuint extraBytes);
    [LibraryImport(Lib)] public static partial void objc_registerClassPair(nint cls);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool class_addMethod(nint cls, nint selector, nint imp, string types);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial nint Send(nint self, nint sel);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial nint Send(nint self, nint sel, nint a);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial nint Send(nint self, nint sel, nint a, nint b);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial nint Send(nint self, nint sel, nint a, nint b, nint c);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial nint Send(nint self, nint sel, double a);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial nint Send(nint self, nint sel, nint a, nuint b);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial nint Send(nint self, nint sel, nint target, nint selector, nint info, [MarshalAs(UnmanagedType.U1)] bool repeats, double interval);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial nint Send(nint self, nint sel, NSRect frame, nuint style, nuint backing, [MarshalAs(UnmanagedType.U1)] bool deferred);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial nint Send(nint self, nint sel, NSRect frame);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial nint Send(nint self, nint sel, NSPoint point, nint attrs);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial nint Send(nint self, nint sel, NSPoint point);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial nint Send(nint self, nint sel, NSRect rect, nint attrs);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial nint Send(nint self, nint sel, [MarshalAs(UnmanagedType.U1)] bool value);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial nint Send(nint self, nint sel, NSRange range);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial nint Send(nint self, nint sel, NSRect rect, nint a, nint b);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial NSSize SendSize(nint self, nint sel, nint attrs);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial NSRect SendRect(nint self, nint sel);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial NSRect SendRect(nint self, nint sel, NSRect rect, nint other);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial NSRect SendRect(nint self, nint sel, NSRect rect);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial NSPoint SendPoint(nint self, nint sel);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial NSRange SendRange(nint self, nint sel);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial NSRange SendRange(nint self, nint sel, nint index);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial NSRange SendRange(nint self, nint sel, NSPoint point);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial NSRect SendRect(nint self, nint sel, NSRange range);
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")] public static partial NSPoint SendPoint(nint self, nint sel, NSPoint point, nint view);

    [LibraryImport("/System/Library/Frameworks/AppKit.framework/AppKit", EntryPoint = "NSRectFill")] public static partial void RectFill(NSRect rect);

    /// <summary>Loads a framework so that its classes are registered with the runtime before <see cref="Class"/> looks them up.</summary>
    public static void LoadFramework(string name) => NativeLibrary.Load($"/System/Library/Frameworks/{name}.framework/{name}");

    public static nint Class(string name) => objc_getClass(name);
    public static nint Sel(string name) => sel_registerName(name);

    public static nint Msg(nint self, string selector) => Send(self, Sel(selector));
    public static nint Msg(nint self, string selector, nint a) => Send(self, Sel(selector), a);

    public static nint NewString(string s)
    {
        fixed (char* p = s) return Send(Class("NSString"), Sel("stringWithCharacters:length:"), (nint)p, (nuint)s.Length);
    }

    public static string ToManaged(nint nsString)
    {
        if (nsString == 0) return "";
        int len = (int)(nuint)Send(nsString, Sel("length"));
        if (len == 0) return "";
        var buffer = new char[len];
        fixed (char* p = buffer) Send(nsString, Sel("getCharacters:range:"), (nint)p, (nint)0, (nint)len);
        return new string(buffer);
    }

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] public static partial nint objc_getProtocol(string name);
    [LibraryImport(Lib)][return: MarshalAs(UnmanagedType.U1)] public static partial bool class_addProtocol(nint cls, nint protocol);

    /// <summary>Declares that a class conforms to a protocol known to the runtime; AppKit derives the attribute list of an accessible view from the protocols it adopts.</summary>
    public static void AddProtocol(nint cls, string name)
    {
        nint protocol = objc_getProtocol(name);
        if (protocol == 0) throw new InvalidOperationException($"The protocol {name} is not registered; load its framework first.");
        class_addProtocol(cls, protocol);
    }

    /// <summary>Adds an instance method; the function pointer must be an [UnmanagedCallersOnly] method.</summary>
    public static void AddMethod(nint cls, string selector, void* imp, string typeEncoding)
    {
        if (!class_addMethod(cls, Sel(selector), (nint)imp, typeEncoding))
            throw new InvalidOperationException($"The class already implements {selector}.");
    }
}
