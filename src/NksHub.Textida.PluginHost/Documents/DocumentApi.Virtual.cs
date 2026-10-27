using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Documents;

public sealed partial class DocumentApi
{
    private sealed record Provided(string PluginId, string Key);

    /// <summary>Virtual documents by document id, with the plugin that provides each and the key that plugin chose for it. Guarded by <c>_gate</c>.</summary>
    private readonly Dictionary<Guid, Provided> _provided = [];

    /// <summary>A virtual document is gone (its tab closed or it was withdrawn): the key is free again and its provider no longer reads it.</summary>
    private void ForgetVirtual(Guid id)
    {
        Provided? gone;
        lock (_gate)
        {
            if (!_provided.Remove(id, out gone)) return;
        }
        _options.Permissions.Revoke(gone.PluginId, DocumentPermissions.Read, GrantScope.OfDocument(id));
    }

    /// <summary>The content of a request: <c>text</c> written in the codec (with its byte order mark) or <c>bytes</c> (base64) as they are; neither is an empty document.</summary>
    private byte[] ReadContent(JsonElement p, TextCodec codec)
    {
        bool hasText = p.TryGetProperty("text", out var text), hasBytes = p.TryGetProperty("bytes", out var bytes);
        if (hasText && hasBytes) throw Invalid("The content is either 'text' or 'bytes', not both.");
        byte[] content = [];
        try
        {
            if (hasText) content = [.. codec.Bom, .. (text.ValueKind == JsonValueKind.String ? codec.Encode(text.GetString()!) : throw Invalid("'text' must be a string."))];
            else if (hasBytes) content = bytes.ValueKind == JsonValueKind.String ? Convert.FromBase64String(bytes.GetString()!) : throw Invalid("'bytes' must be a base64 string.");
        }
        catch (UnrepresentableCharactersException) { throw Invalid("The text cannot be written in the encoding of the document."); }
        catch (FormatException) { throw Invalid("'bytes' is not valid base64."); }
        if (content.LongLength > _options.MaxVirtualBytes)
            throw new RpcException(RpcErrorCodes.MessageTooLarge, $"A virtual document holds at most {_options.MaxVirtualBytes} bytes; got {content.LongLength}.");
        return content;
    }

    private static byte[] VirtualAnswer(Document d, string key, bool created) => Obj(w =>
    {
        w.WriteString("document", d.Id);
        w.WriteString("key", key);
        w.WriteNumber("revision", d.Revision);
        w.WriteNumber("byteLength", d.ByteLength);
        w.WriteBoolean("created", created);
    });

    /// <summary>
    /// Opens a read only document the plugin supplies. The <c>key</c> is the identity the plugin chose: opening the same key again returns the open document
    /// (the content in the request is then ignored; <see cref="DocumentMethods.VirtualUpdate"/> changes it). The plugin may read what it provided without a further grant.
    /// </summary>
    private async Task<byte[]> VirtualOpenAsync(string pluginId, JsonElement p)
    {
        var key = NeedString(p, "key");
        var name = NeedString(p, "name");
        if (key.Length == 0 || string.IsNullOrWhiteSpace(name)) throw Invalid("'key' and 'name' must not be empty.");
        var title = p.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        var codec = TextCodec.Utf8;
        if (p.TryGetProperty("encoding", out var enc) && enc.ValueKind == JsonValueKind.String)
            codec = TextCodec.ByName(enc.GetString()!) ?? throw Invalid($"The encoding '{enc.GetString()}' is not known.");
        var content = ReadContent(p, codec);
        return await _owner.InvokeAsync(() =>
        {
            Guid existing;
            int count;
            lock (_gate)
            {
                existing = _provided.FirstOrDefault(x => x.Value == new Provided(pluginId, key)).Key;
                count = _provided.Count(x => x.Value.PluginId == pluginId);
            }
            if (existing != Guid.Empty && Find(existing) is { } open) return VirtualAnswer(open.Doc, key, created: false);
            if (count >= _options.MaxVirtualDocumentsPerPlugin)
                throw Fail(DocumentErrorCodes.VirtualQuotaExceeded, $"A plugin may keep {_options.MaxVirtualDocumentsPerPlugin} virtual documents open; close one first.",
                    w => w.WriteNumber("limit", _options.MaxVirtualDocumentsPerPlugin));
            var doc = _options.Tools.OpenVirtual(pluginId, name, title, content, codec);
            lock (_gate) _provided[doc.Id] = new Provided(pluginId, key);
            Expose(doc);
            _options.Permissions.Grant(pluginId, DocumentPermissions.Read, GrantScope.OfDocument(doc.Id));
            return VirtualAnswer(doc, key, created: true);
        });
    }

    private Exposed FindProvided(string pluginId, Guid id)
    {
        var e = Find(id);
        bool own;
        lock (_gate) own = _provided.TryGetValue(id, out var provided) && provided.PluginId == pluginId;
        return own ? e : throw Fail(DocumentErrorCodes.NotProvider, "The document is not a virtual document this plugin provided.");
    }

    /// <summary>Replaces the content of a virtual document the plugin provided. With <c>baseRevision</c> it is refused as a conflict when the document is at another revision.</summary>
    private async Task<byte[]> VirtualUpdateAsync(string pluginId, JsonElement p)
    {
        var e = FindProvided(pluginId, NeedDocumentId(p));
        long? expected = p.TryGetProperty("baseRevision", out _) ? NeedLong(p, "baseRevision") : null;
        var content = ReadContent(p, e.Doc.Codec);
        return await _owner.InvokeAsync(() =>
        {
            var d = e.Doc;
            if (expected is { } revision) CheckCurrent(d, revision);
            var result = d.ApplyVirtual([new ByteEdit(new ByteRange(0, d.ByteLength), content)], null, "plugin:" + pluginId);
            if (!result.Applied) throw EditFailure(result.Conflict, result.Revision);
            return Obj(w =>
            {
                w.WriteString("document", d.Id);
                w.WriteNumber("revision", d.Revision);
                w.WriteNumber("byteLength", d.ByteLength);
            });
        });
    }
}
