using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Markdown;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Documents;

/// <summary>
/// Controlled preview (P6.2.4): a plugin supplies text, Markdown or HTML for one revision of a document it may read, and the editor renders it in its own panel. The plugin never
/// reaches the renderer: HTML is a small subset and the rest stays text, nothing runs, a remote image is only fetched after the user allowed its host, and a link to a local file is never
/// followed. Pictures come from <c>data:</c> addresses or from the bytes the plugin attaches. A preview is only accepted for the current revision; when the document moves on the
/// panel marks it outdated until the plugin shows the new revision (the plugin learns of the move by subscribing to the document).
/// </summary>
public sealed partial class DocumentApi
{
    private readonly Dictionary<(string Plugin, Guid Document), PreviewContent> _previews = [];

    private PreviewOptions NeedPreview() => _options.Preview ?? throw new RpcException(RpcErrorCodes.MethodNotFound, "The editor takes no previews.");

    private async Task<byte[]> PreviewShowAsync(string pluginId, JsonElement p)
    {
        var options = NeedPreview();
        var id = NeedDocumentId(p);
        if (!_options.Permissions.IsGranted(pluginId, DocumentPermissions.Read, GrantScope.OfDocument(id)))
            throw PluginPermissions.Denied(DocumentPermissions.Read, "NotGranted", "The plugin does not hold the 'document.read' permission for the document it shows a preview of.");
        var e = Find(id);
        long revision = NeedLong(p, "revision");
        var format = NeedString(p, "format");
        if (format is not (PreviewFormats.Text or PreviewFormats.Markdown or PreviewFormats.Html))
            throw Invalid($"The preview format '{format}' is not rendered; use '{PreviewFormats.Text}', '{PreviewFormats.Markdown}' or '{PreviewFormats.Html}'.");
        var text = NeedString(p, "text");
        if (text.Length > options.MaxChars) throw new RpcException(RpcErrorCodes.MessageTooLarge, $"A preview holds at most {options.MaxChars} characters; got {text.Length}.");
        var images = ReadPreviewImages(p, options);
        var title = p.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        return await _owner.InvokeAsync(() =>
        {
            CheckCurrent(e.Doc, revision);
            var content = new PreviewContent(pluginId, id, revision, format, title, text, images);
            lock (_gate)
            {
                if (!_exposed.ContainsKey(id)) throw Fail(DocumentErrorCodes.UnknownDocument, "The document is not open or not available to plugins.");
                _previews[(pluginId, id)] = content;
            }
            options.Panel.Show(content);
            return Obj(w =>
            {
                w.WriteString("document", id);
                w.WriteNumber("revision", revision);
            });
        });
    }

    /// <summary>The pictures a plugin attached (<c>images</c>: name to base64 bytes); each must be a raster picture the preview can draw and all must fit the limits.</summary>
    private IReadOnlyDictionary<string, byte[]>? ReadPreviewImages(JsonElement p, PreviewOptions options)
    {
        if (!p.TryGetProperty("images", out var element) || element.ValueKind == JsonValueKind.Null) return null;
        if (element.ValueKind != JsonValueKind.Object) throw Invalid("Parameter 'images' must be an object that maps a name to the base64 bytes of a picture.");
        var images = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in element.EnumerateObject())
        {
            if (images.Count >= options.MaxImages) throw new RpcException(RpcErrorCodes.MessageTooLarge, $"A preview takes at most {options.MaxImages} pictures.");
            if (entry.Name.Length is < 1 or > 64 || !entry.Name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
                throw Invalid($"The picture name '{entry.Name}' must be 1 to 64 letters, digits, dots, hyphens or underscores.");
            if (entry.Value.ValueKind != JsonValueKind.String || !entry.Value.TryGetBytesFromBase64(out var bytes)) throw Invalid($"Picture '{entry.Name}' must be base64.");
            if (bytes.Length > options.MaxImageBytes || (total += bytes.Length) > options.MaxImagesBytes)
                throw new RpcException(RpcErrorCodes.MessageTooLarge, $"A picture of a preview holds at most {options.MaxImageBytes} bytes and all of them at most {options.MaxImagesBytes}.");
            if (!RasterImage.IsSupported(bytes)) throw Invalid($"Picture '{entry.Name}' is not a PNG, JPEG, GIF, BMP or WebP picture.");
            images[entry.Name] = bytes;
        }
        return images;
    }

    private byte[] PreviewClose(string pluginId, JsonElement p)
    {
        var id = NeedDocumentId(p);
        ClearPreviews(key => key.Plugin == pluginId && key.Document == id);
        return Obj(w => w.WriteBoolean("closed", true));
    }

    /// <summary>Tells the panel that the previews of this document are for an older revision. Called on the thread that owns the documents.</summary>
    private void MarkPreviewsOutdated(Document d, long revision)
    {
        PreviewContent[] outdated;
        lock (_gate)
        {
            if (_previews.Count == 0 || _options.Preview is null) return;
            outdated = [.. _previews.Values.Where(c => c.Document == d.Id && c.Revision < revision)];
        }
        foreach (var c in outdated) _options.Preview.Panel.Outdated(c.PluginId, c.Document, revision);
    }

    private void ClearPreviews(Func<(string Plugin, Guid Document), bool> match)
    {
        List<(string Plugin, Guid Document)> gone;
        lock (_gate)
        {
            gone = [.. _previews.Keys.Where(match)];
            foreach (var key in gone) _previews.Remove(key);
        }
        if (_options.Preview is { } options)
            foreach (var (plugin, document) in gone) options.Panel.Close(plugin, document);
    }

    /// <summary>A plugin that lost the permission to show previews, to read the document or to host an agent is taken out of the panels.</summary>
    private void DropRevokedPanels(string pluginId)
    {
        bool preview = _options.Permissions.IsGranted(pluginId, DocumentPermissions.Preview, GrantScope.Any);
        ClearPreviews(key => key.Plugin == pluginId && (!preview || !_options.Permissions.IsGranted(pluginId, DocumentPermissions.Read, GrantScope.OfDocument(key.Document))));
        if (!_options.Permissions.IsGranted(pluginId, DocumentPermissions.AgentPanel, GrantScope.Any)) _options.Agent?.Panel.Close(pluginId);
    }
}
