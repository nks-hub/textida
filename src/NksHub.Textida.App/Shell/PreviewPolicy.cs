using NksHub.Textida.App.Services;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The safety rules of the plugin preview (P6.2.7). The renderer draws controls and has no script engine, so script cannot run. Network: nothing is fetched; an image is only taken from a
/// <c>data:</c> URI, so a remote address is never contacted. Links: only web and mail addresses can be opened, and only after the user confirmed; <c>file:</c>, <c>javascript:</c>, relative and
/// every other target are refused. Loading remote images with the user's consent per preview is not built.
/// </summary>
internal static class PreviewPolicy
{
    private static readonly string[] ImageTypes = ["png", "jpeg", "gif", "bmp", "webp"];

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

    /// <summary>The address of a link the user may be asked to open (http, https or mailto); null when the policy never opens it.</summary>
    public static Uri? LinkTarget(string target) =>
        Uri.TryCreate(target.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto" ? uri : null;
}
