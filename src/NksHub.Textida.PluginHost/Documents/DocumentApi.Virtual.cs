using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Documents;

public sealed partial class DocumentApi
{
    private sealed record Provided(string PluginId, string Key, string? Uri, bool Editable);

    /// <summary>Virtual documents by document id, with the plugin that provides each, the key that plugin chose for it and what it said about it. Guarded by <c>_gate</c>.</summary>
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
    private byte[] ReadContent(JsonElement p, TextCodec codec, long? limit = null)
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
        long max = limit ?? _options.MaxVirtualBytes;
        if (content.LongLength > max)
            throw new RpcException(RpcErrorCodes.MessageTooLarge, $"The content holds at most {max} bytes; got {content.LongLength}.");
        return content;
    }

    private static byte[] VirtualAnswer(Document d, Provided provided, bool created) => Obj(w =>
    {
        w.WriteString("document", d.Id);
        w.WriteString("key", provided.Key);
        if (provided.Uri is { } uri) w.WriteString("uri", uri); else w.WriteNull("uri");
        w.WriteBoolean("editable", provided.Editable);
        w.WriteNumber("revision", d.Revision);
        w.WriteNumber("byteLength", d.ByteLength);
        w.WriteBoolean("created", created);
    });

    /// <summary>
    /// Opens a document the plugin supplies. The <c>key</c> is the identity the plugin chose: opening the same key again returns the open document
    /// (the content in the request is then ignored; <see cref="DocumentMethods.VirtualUpdate"/> changes it). The plugin may read what it provided without a further grant.
    /// An <c>editable</c> document takes the edits of the user: the plugin hears them as events without subscribing, and a save is sent to it (<see cref="VirtualSaveAsync"/>).
    /// </summary>
    private async Task<byte[]> VirtualOpenAsync(string pluginId, JsonElement p)
    {
        var uri = p.TryGetProperty("uri", out var given) && given.ValueKind != JsonValueKind.Null ? NeedString(p, "uri") : null;
        if (uri is not null && (UriMask.SchemeOf(uri) is not { } scheme || scheme == ReservedScheme))
            throw Invalid("'uri' must start with a scheme other than 'file', as in 'scheme://name'.");
        var key = uri is not null && !p.TryGetProperty("key", out _) ? uri : NeedString(p, "key");
        var name = NeedString(p, "name");
        if (key.Length == 0 || string.IsNullOrWhiteSpace(name)) throw Invalid("'key' and 'name' must not be empty.");
        bool editable = Flag(p, "editable");
        if (editable && _options.VirtualSaveTimeout is null) throw new RpcException(RpcErrorCodes.MethodNotFound, "The editor takes no editable virtual documents.");
        var title = OptionalString(p, "title");
        var codec = TextCodec.Utf8;
        if (p.TryGetProperty("encoding", out var enc) && enc.ValueKind == JsonValueKind.String)
            codec = TextCodec.ByName(enc.GetString()!) ?? throw Invalid($"The encoding '{enc.GetString()}' is not known.");
        var content = ReadContent(p, codec);
        return await _owner.InvokeAsync(() =>
        {
            KeyValuePair<Guid, Provided> existing;
            int count;
            lock (_gate)
            {
                existing = _provided.FirstOrDefault(x => x.Value.PluginId == pluginId && x.Value.Key == key);
                count = _provided.Count(x => x.Value.PluginId == pluginId);
            }
            if (existing.Value is not null && Find(existing.Key) is { } open) return VirtualAnswer(open.Doc, existing.Value, created: false);
            if (count >= _options.MaxVirtualDocumentsPerPlugin)
                throw Fail(DocumentErrorCodes.VirtualQuotaExceeded, $"A plugin may keep {_options.MaxVirtualDocumentsPerPlugin} virtual documents open; close one first.",
                    w => w.WriteNumber("limit", _options.MaxVirtualDocumentsPerPlugin));
            var provided = new Provided(pluginId, key, uri, editable);
            var doc = _options.Tools.OpenVirtual(pluginId, name, title, content, codec, new VirtualInfo(key, uri, editable));
            lock (_gate) _provided[doc.Id] = provided;
            Expose(doc);
            _options.Permissions.Grant(pluginId, DocumentPermissions.Read, GrantScope.OfDocument(doc.Id));
            if (editable) lock (_gate) _exposed[doc.Id].Subscribers.Add(pluginId);
            return VirtualAnswer(doc, provided, created: true);
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

    /// <summary>The plugin closes a virtual document it provided. The tab goes without asking, whatever the user typed into it.</summary>
    private async Task<byte[]> VirtualCloseAsync(string pluginId, JsonElement p)
    {
        var e = FindProvided(pluginId, NeedDocumentId(p));
        return await _owner.InvokeAsync(() =>
        {
            CloseProvided(e.Doc);
            return Obj(w => w.WriteBoolean("closed", true));
        });
    }

    /// <summary>Closes the tab of a virtual document and withdraws the document (closing the tab already does, when there is one). Call on the thread that owns the document.</summary>
    private void CloseProvided(Document doc)
    {
        _options.Tools.CloseVirtual(doc.Id);
        Withdraw(doc);
    }

    /// <summary>
    /// A plugin ended (crashed, was turned off, the editor is closing): the virtual documents it provided close, because nobody is left to save them or to bring them up to date,
    /// and the snapshots it pinned are released. Call on the thread that owns the documents.
    /// </summary>
    public void PluginEnded(string pluginId)
    {
        ReleaseSnapshots(pluginId);
        List<Exposed> mine;
        lock (_gate) mine = [.. _provided.Where(x => x.Value.PluginId == pluginId && _exposed.ContainsKey(x.Key)).Select(x => _exposed[x.Key])];
        foreach (var e in mine) CloseProvided(e.Doc);
    }

    /// <summary>
    /// Sends the content of an editable virtual document to the plugin that provides it, which keeps it (<see cref="DocumentRequests.VirtualSave"/>). Nothing is written anywhere else.
    /// Throws <see cref="SourceException"/>: <see cref="SourceFailure.ReadOnly"/> for a document that is not editable, <see cref="SourceFailure.NoSource"/> for one that is not virtual or no longer provided,
    /// <see cref="SourceFailure.Unavailable"/> when the plugin cannot take it now, <see cref="SourceFailure.Failed"/> when it refused.
    /// </summary>
    public async Task VirtualSaveAsync(Guid document, ReadOnlyMemory<byte> content, long revision, CancellationToken cancellation = default)
    {
        Provided? provided;
        lock (_gate) _provided.TryGetValue(document, out provided);
        if (provided is null || _options.VirtualSaveTimeout is not { } timeout) throw new SourceException(SourceFailure.NoSource, "The document is not a virtual document that a plugin provides.");
        if (!provided.Editable) throw new SourceException(SourceFailure.ReadOnly, "The virtual document is read only.");
        if (content.Length > _options.MaxVirtualBytes) throw new SourceException(SourceFailure.TooLarge, $"A virtual document holds at most {_options.MaxVirtualBytes} bytes; got {content.Length}.");
        if (!_options.Permissions.IsGranted(provided.PluginId, DocumentPermissions.Virtual, GrantScope.Any))
            throw new SourceException(SourceFailure.Unavailable, "The plugin may not provide virtual documents.");
        var bytes = content.ToArray();
        var request = Obj(w =>
        {
            w.WriteString("document", document);
            w.WriteString("key", provided.Key);
            if (provided.Uri is { } uri) w.WriteString("uri", uri); else w.WriteNull("uri");
            w.WriteNumber("revision", revision);
            w.WriteBase64String("bytes", bytes);
        });
        try { await _request(provided.PluginId, DocumentRequests.VirtualSave, request, timeout, cancellation); }
        catch (RpcException e) { throw new SourceException(SourceFailure.Failed, e.Message); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is TimeoutException or IOException or PluginCrashedException or PluginUnavailableException or OperationCanceledException) { throw new SourceException(SourceFailure.Unavailable, e.Message); }
    }
}
