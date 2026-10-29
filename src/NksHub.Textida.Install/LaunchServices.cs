using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NksHub.Textida.Install;

/// <summary>The default application of a file extension, as Launch Services keeps it. A seam, so that the integration is tested without changing the Launch Services database of the machine.</summary>
public interface ILaunchServices
{
    /// <summary>The identifier of the bundle that opens files of the extension (with the dot, <c>.md</c>), or null when none does.</summary>
    string? DefaultHandlerOf(string extension);

    /// <summary>Makes the bundle the default application for the extension; throws <see cref="IOException"/> with the reason when Launch Services refuses.</summary>
    void SetDefaultHandler(string extension, string bundleId);
}

/// <summary>
/// Launch Services of this Mac, through <c>LSSetDefaultRoleHandlerForContentType</c> and <c>LSCopyDefaultRoleHandlerForContentType</c>: the extension is turned into its content type (a dynamic one when no
/// application declares it), and the handler is set for every role. Both functions are marked deprecated by Apple and keep working; no other call sets a default application without the user.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed partial class SystemLaunchServices : ILaunchServices
{
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
    private const uint Utf8 = 0x08000100; // kCFStringEncodingUTF8
    private const uint AllRoles = 0xFFFFFFFF; // kLSRolesAll

    [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint CFStringCreateWithCString(nint allocator, string text, uint encoding);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint value);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFStringGetLength(nint text);

    [LibraryImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFStringGetCString(nint text, Span<byte> buffer, nint bufferSize, uint encoding);

    [LibraryImport(CoreServices)]
    private static partial nint UTTypeCreatePreferredIdentifierForTag(nint tagClass, nint tag, nint conformingTo);

    [LibraryImport(CoreServices)]
    private static partial int LSSetDefaultRoleHandlerForContentType(nint contentType, uint roles, nint handlerBundleId);

    [LibraryImport(CoreServices)]
    private static partial nint LSCopyDefaultRoleHandlerForContentType(nint contentType, uint roles);

    private static nint Text(string text) => CFStringCreateWithCString(0, text, Utf8);

    private static string? Managed(nint text)
    {
        if (text == 0) return null;
        var buffer = new byte[(int)CFStringGetLength(text) * 4 + 1];
        return CFStringGetCString(text, buffer, buffer.Length, Utf8) ? System.Text.Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0)) : null;
    }

    /// <summary>The content type of the extension; the caller releases it.</summary>
    private static nint ContentTypeOf(string extension)
    {
        nint filenameExtension = Text("public.filename-extension"), tag = Text(extension.TrimStart('.'));
        try { return UTTypeCreatePreferredIdentifierForTag(filenameExtension, tag, 0); }
        finally
        {
            CFRelease(filenameExtension);
            CFRelease(tag);
        }
    }

    public string? DefaultHandlerOf(string extension)
    {
        nint type = ContentTypeOf(extension);
        if (type == 0) return null;
        try
        {
            nint handler = LSCopyDefaultRoleHandlerForContentType(type, AllRoles);
            try { return Managed(handler); }
            finally { if (handler != 0) CFRelease(handler); }
        }
        finally { CFRelease(type); }
    }

    public void SetDefaultHandler(string extension, string bundleId)
    {
        nint type = ContentTypeOf(extension);
        if (type == 0) throw new IOException($"Launch Services has no content type for {extension}.");
        nint bundle = Text(bundleId);
        try
        {
            int status = LSSetDefaultRoleHandlerForContentType(type, AllRoles, bundle);
            if (status != 0) throw new IOException($"Launch Services refused {extension} (error {status}).");
        }
        finally
        {
            CFRelease(bundle);
            CFRelease(type);
        }
    }
}
