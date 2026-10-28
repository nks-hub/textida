using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Markdown;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Gets the pictures of a preview under <see cref="PreviewPolicy"/>. A picture from a local file is read and decoded off the UI thread; a picture from the web is only contacted after the user allowed its
/// host (the question is asked when the user presses the button that stands in its place), through the proxy of the network policy, without redirects, within a size and a time limit. Decoded
/// pictures are kept, so drawing the preview again while the user types neither reads the disk nor asks the network again; a failed web picture is tried again after a while.
/// </summary>
internal sealed class PreviewImages(Func<HttpClientHandler> handler, Func<string, Task<bool>> askHost, Action allowedChanged)
{
    private sealed record Entry(Bitmap? Bitmap, string Stamp, long FailedAt = 0);

    private readonly HashSet<string> _allowedHosts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> _cache = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private readonly Dictionary<string, Task<Bitmap?>> _inflight = new(StringComparer.Ordinal);

    public bool IsAllowed(string consentKey) => _allowedHosts.Contains(consentKey);

    /// <summary>Allows the host (as <see cref="PreviewPolicy.ConsentKey"/> gives it) until the window closes.</summary>
    public void Allow(string consentKey) => _allowedHosts.Add(consentKey);

    /// <summary>The picture of a decoded bitmap, with the alt text as its name for a screen reader.</summary>
    public static Image Picture(Bitmap bitmap, string alt)
    {
        var image = new Image { Source = bitmap, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = PreviewPolicy.MaxImageWidth };
        AutomationProperties.SetName(image, alt);
        return image;
    }

    /// <summary>The bitmap of picture bytes the policy accepts, decoded at a bounded width so a small file cannot expand to a huge bitmap; null when it is not drawable or does not decode.</summary>
    public static Bitmap? Decode(byte[] bytes)
    {
        if (!PreviewPolicy.Drawable(bytes) || RasterImage.Size(bytes) is not { } size) return null;
        try
        {
            using var stream = new MemoryStream(bytes);
            return Bitmap.DecodeToWidth(stream, Math.Min(size.Width, PreviewPolicy.MaxImageWidth));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }

    public static TextBlock Note(string key, string alt) => new() { Text = Loc.Get(key, ("alt", alt)), TextWrapping = TextWrapping.Wrap };

    // ---- local files ----

    /// <summary>A stand-in for the picture in <paramref name="path"/> (already checked by the policy); it shows the picture when it is read, or a note when it is not.</summary>
    public Control Local(string path, string alt)
    {
        string key = "file:" + path;
        var host = new ContentControl { Content = Note(K.PreviewImageLoading, alt) };
        _cache.TryGetValue(key, out var known);
        if (known?.Bitmap is { } shown) host.Content = Picture(shown, alt);
        _ = LoadLocalAsync(host, key, path, alt, known?.Stamp);
        return host;
    }

    private async Task LoadLocalAsync(ContentControl host, string key, string path, string alt, string? knownStamp)
    {
        var (stamp, bitmap, unchanged) = await Task.Run(() => ReadLocal(path, knownStamp));
        if (unchanged) return;
        if (bitmap is null)
        {
            _cache.Remove(key);
            host.Content = Note(K.PreviewPanelImage, alt);
            return;
        }
        Remember(key, new Entry(bitmap, stamp));
        host.Content = Picture(bitmap, alt);
    }

    private static (string Stamp, Bitmap? Bitmap, bool Unchanged) ReadLocal(string path, string? knownStamp)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > PluginLimitsPlaceholders.PreviewFileImageMaxBytes) return ("", null, false);
            string stamp = $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
            if (stamp == knownStamp) return (stamp, null, true);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return (stamp, Decode(ReadAll(file, PluginLimitsPlaceholders.PreviewFileImageMaxBytes) ?? []), false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return ("", null, false);
        }
    }

    private static byte[]? ReadAll(Stream stream, int limit)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > limit) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    // ---- the web ----

    /// <summary>
    /// A stand-in for a web picture. Until the user allowed its host it is a button that asks; no connection is made. Once the host is allowed the picture is fetched and replaces the button.
    /// </summary>
    public Control Remote(Uri uri, string alt)
    {
        string key = uri.AbsoluteUri, site = PreviewPolicy.ConsentKey(uri);
        var host = new ContentControl();
        if (_cache.TryGetValue(key, out var known) && known.Bitmap is { } shown) host.Content = Picture(shown, alt);
        else if (!_allowedHosts.Contains(site)) host.Content = AskButton(site, alt);
        else if (known is not null && Environment.TickCount64 - known.FailedAt < PluginLimitsPlaceholders.PreviewRemoteImageRetryMs) host.Content = Note(K.PreviewPanelImage, alt);
        else
        {
            host.Content = Note(K.PreviewImageLoading, alt);
            _ = ShowFetchedAsync(host, key, uri, alt);
        }
        return host;
    }

    private Button AskButton(string site, string alt)
    {
        string text = Loc.Get(K.PreviewImageRemote, ("host", site), ("alt", alt));
        var button = new Button { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap } };
        button.Classes.Add("previewlink");
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => _ = AskAsync(site);
        return button;
    }

    private async Task AskAsync(string site)
    {
        if (!await askHost(site)) return;
        Allow(site);
        allowedChanged();
    }

    private async Task ShowFetchedAsync(ContentControl host, string key, Uri uri, string alt)
    {
        if (!_inflight.TryGetValue(key, out var fetch)) _inflight[key] = fetch = FetchAsync(key, uri);
        var bitmap = await fetch;
        host.Content = bitmap is null ? Note(K.PreviewPanelImage, alt) : Picture(bitmap, alt);
    }

    private async Task<Bitmap?> FetchAsync(string key, Uri uri)
    {
        var bitmap = await Task.Run(() => DownloadAsync(uri));
        _inflight.Remove(key);
        Remember(key, bitmap is null ? new Entry(null, "", Environment.TickCount64) : new Entry(bitmap, ""));
        return bitmap;
    }

    private async Task<Bitmap?> DownloadAsync(Uri uri)
    {
        try
        {
            var h = handler();
            h.AllowAutoRedirect = false;
            using var http = new HttpClient(h, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
            using var limit = new CancellationTokenSource(PluginLimitsPlaceholders.PreviewRemoteImageTimeoutMs);
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > PluginLimitsPlaceholders.PreviewFileImageMaxBytes) return null;
            await using var body = await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await body.ReadAsync(chunk, limit.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > PluginLimitsPlaceholders.PreviewFileImageMaxBytes) return null;
                buffer.Write(chunk, 0, read);
            }
            return Decode(buffer.ToArray());
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null; // refused, unreachable, too slow, a proxy that cannot be used: the preview shows the note
        }
    }

    private void Remember(string key, Entry entry)
    {
        if (!_cache.ContainsKey(key)) _order.Enqueue(key);
        _cache[key] = entry;
        while (_cache.Count > PluginLimitsPlaceholders.PreviewImageCacheEntries && _order.TryDequeue(out var oldest)) _cache.Remove(oldest);
    }
}
