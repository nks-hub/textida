using NksHub.Textida.App.Services;
using NksHub.Textida.Core.Markdown;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The safety rules of the preview (P6.2.7). The renderer is the editor's own: it draws controls and has no script engine and no web view, so script cannot run. P6.2.7 chose it because WebView2 and
/// WKWebView need a runtime that may be missing and their behaviour under NativeAOT is not proven; that choice stays open until the proofs on Windows 10 22H2, Windows 11 and macOS 14 are made, and no web view is added before.
/// Pictures: <c>data:</c> addresses and the bytes a plugin attached (<c>preview:name</c>) are read at once; a relative path inside the folder of the document is read from disk, but only for the file the user
/// has open; a web address is contacted only after the user allowed its host, through the proxy of the network policy, without following redirects. Every source is limited in bytes and pixels and must
/// start like a PNG, JPEG, GIF, BMP or WebP picture; SVG is never drawn. Links: only web and mail addresses can be opened, and only after the user confirmed; <c>file:</c>, <c>javascript:</c>, relative
/// and every other target are refused. HTML and the raw HTML in Markdown never run: tags outside a small subset are shown as the text they are.
/// </summary>
internal static class PreviewPolicy
{
    private static readonly string[] ImageTypes = ["png", "jpeg", "gif", "bmp", "webp"];

    /// <summary>The scheme with which text refers to a picture a plugin attached.</summary>
    public const string AttachedScheme = "preview:";

    /// <summary>Widest an image is decoded and drawn, in pixels.</summary>
    public const int MaxImageWidth = 800;

    /// <summary>The bytes of a <c>data:image/…;base64,…</c> URI of a known raster type that fits <see cref="PluginLimitsPlaceholders.PreviewImageMaxBytes"/>; null for anything else.</summary>
    public static byte[]? ImageBytes(string target)
    {
        const string head = "data:image/";
        target = target.Trim();
        if (!target.StartsWith(head, StringComparison.OrdinalIgnoreCase)) return null;
        int comma = target.IndexOf(',');
        if (comma < 0) return null;
        var meta = target[head.Length..comma].Split(';');
        if (meta.Length != 2 || !ImageTypes.Contains(meta[0].ToLowerInvariant()) || !meta[1].Equals("base64", StringComparison.OrdinalIgnoreCase)) return null;
        var data = target.AsSpan(comma + 1);
        if (data.Length / 4L * 3 > PluginLimitsPlaceholders.PreviewImageMaxBytes) return null;
        var bytes = new byte[data.Length / 4 * 3 + 3];
        return Convert.TryFromBase64Chars(data, bytes, out int written) && written <= PluginLimitsPlaceholders.PreviewImageMaxBytes ? bytes[..written] : null;
    }

    /// <summary>The name of the picture a plugin attached that <c>preview:name</c> refers to; null for any other target.</summary>
    public static string? AttachedName(string target) =>
        target.Trim() is var t && t.StartsWith(AttachedScheme, StringComparison.OrdinalIgnoreCase) && t.Length > AttachedScheme.Length ? t[AttachedScheme.Length..] : null;

    /// <summary>
    /// The full path of a picture named by a relative path, or null. Only a path that is relative to <paramref name="baseDirectory"/> and stays inside it is taken: no scheme or drive (so no
    /// <c>file:</c> address and no alternate data stream), no network or rooted path, and no <c>..</c> that leaves the folder. A query or fragment is dropped.
    /// </summary>
    public static string? LocalImagePath(string? baseDirectory, string target)
    {
        if (string.IsNullOrEmpty(baseDirectory)) return null;
        target = target.Trim();
        int cut = target.IndexOfAny(['?', '#']);
        if (cut >= 0) target = target[..cut];
        try { target = Uri.UnescapeDataString(target); }
        catch (UriFormatException) { return null; }
        if (target.Length == 0 || target.Contains(':') || target[0] is '/' or '\\' || target.Any(char.IsControl)) return null;
        string root = Path.GetFullPath(baseDirectory), full = Path.GetFullPath(Path.Combine(root, target));
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison) ? full : null;
    }

    /// <summary>The web address of a picture (http or https, no user name); null for anything else. Nothing is contacted until the user allowed its host.</summary>
    public static Uri? RemoteImage(string target) =>
        Uri.TryCreate(target.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.Host.Length > 0 ? uri : null;

    /// <summary>The key under which the user's consent for a web address is kept: its host, and its port when it is not the default.</summary>
    public static string ConsentKey(Uri uri) => uri.Authority.ToLowerInvariant();

    /// <summary>Whether the bytes are a raster picture of a kind the preview draws whose size in pixels is known and within the limit.</summary>
    public static bool Drawable(ReadOnlySpan<byte> bytes) =>
        RasterImage.FormatName(bytes) is not (null or "ICO") // an icon is a picture of the viewer, not of a preview
        && RasterImage.Size(bytes) is { } size && size.Width > 0 && size.Height > 0 && (long)size.Width * size.Height <= PluginLimitsPlaceholders.PreviewImageMaxPixels;

    /// <summary>The address of a link the user may be asked to open (http, https or mailto); null when the policy never opens it.</summary>
    public static Uri? LinkTarget(string target) =>
        Uri.TryCreate(target.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto" ? uri : null;
}
