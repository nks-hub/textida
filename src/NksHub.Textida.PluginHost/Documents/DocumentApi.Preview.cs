using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Documents;

/// <summary>
/// Controlled preview (P6.2.4): a plugin supplies text or Markdown for one revision of a document it may read, and the editor renders it in its own panel. The plugin never
/// reaches the renderer: there is no HTML, no script, no remote image and no local file link. A preview is only accepted for the current revision; when the document moves on the
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
        if (format is not (PreviewFormats.Text or PreviewFormats.Markdown))
            throw Invalid($"The preview format '{format}' is not rendered; use '{PreviewFormats.Text}' or '{PreviewFormats.Markdown}'.");
        var text = NeedString(p, "text");
        if (text.Length > options.MaxChars) throw new RpcException(RpcErrorCodes.MessageTooLarge, $"A preview holds at most {options.MaxChars} characters; got {text.Length}.");
        var title = p.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        return await _owner.InvokeAsync(() =>
        {
            CheckCurrent(e.Doc, revision);
            var content = new PreviewContent(pluginId, id, revision, format, title, text);
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
