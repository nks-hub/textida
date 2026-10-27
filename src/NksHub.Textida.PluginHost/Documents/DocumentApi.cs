using System.Buffers;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Documents;

/// <summary>
/// Document API v1 of the plugin protocol: reading by revision, snapshots, position mapping, events and edits.
/// Plug <see cref="HandleAsync"/> in as the host's <see cref="PluginApiCallback"/> and the instance in as its <see cref="IPluginStateSource"/>.
/// Only documents handed to <see cref="Expose"/> are visible. Every read names a revision or a snapshot; a read of a revision that is not
/// the current one is a <see cref="DocumentErrorCodes.RevisionConflict"/>, never a silent read of other bytes. Positions are 64-bit byte offsets.
/// </summary>
public sealed partial class DocumentApi : IPluginStateSource, IAsyncDisposable
{
    private readonly DocumentApiOptions _options;
    private readonly IDocumentOwnerThread _owner;
    private readonly PluginEventSender _send;
    private readonly PluginRequestSender _request;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Exposed> _exposed = [];
    private readonly Dictionary<string, List<Pinned>> _snapshots = [];

    public DocumentApi(DocumentApiOptions options, IDocumentOwnerThread owner, PluginEventSender send, PluginRequestSender request, TimeProvider? time = null)
    {
        if (options.MaxReadBytes < 1 || options.MaxFindMatches < 1 || options.MaxSnapshotsPerPlugin < 1 || options.SnapshotLifetime <= TimeSpan.Zero
            || options.EventQueueCapacity < 1 || options.MaxRememberedBatches < 1 || options.MaxSymbolsPerDocument < 1 || options.MaxSymbolTextLength < 1
            || options.MaxSnippetLength < 1 || options.MaxSnippetCarets < 1 || options.TabTimeout <= TimeSpan.Zero || options.MaxSecretScanBytes < 1 || options.MaxClipboardChars < 1 || options.MaxScanBytes < 1
            || options.MaxVirtualDocumentsPerPlugin < 1 || options.MaxVirtualBytes < 1
            || options.MaxDiagnosticsPerDocument < 1 || options.MaxCompletionItems < 1 || options.MaxCompletionEdits < 0 || options.CompletionTimeout <= TimeSpan.Zero
            || options.Workspace is { MaxChanges: < 1 } or { MaxFileBytes: < 1 }
            || options.Sources is { Timeout.Ticks: <= 0 } or { MaxBytes: < 1 } or { MaxEntries: < 1 } or { MaxSchemesPerPlugin: < 1 } or { CacheEntries: < 1 } or { CacheBytes: < 1 })
            throw new ArgumentException("Every document API limit must be positive.", nameof(options));
        _options = options;
        _owner = owner;
        _send = send;
        _request = request;
        _time = time ?? TimeProvider.System;
        options.Permissions.Revoked += DropRevokedSubscriptions;
        options.Permissions.Revoked += DropRevokedSources;
        _cache = options.Sources is { } sources ? new SourceCache(sources.CacheEntries, sources.CacheBytes) : null;
    }

    /// <summary>A plugin that lost the right to read a document no longer hears about its changes.</summary>
    private void DropRevokedSubscriptions(string pluginId)
    {
        lock (_gate)
        {
            foreach (var e in _exposed.Values)
                if (e.Subscribers.Contains(pluginId) && !_options.Permissions.IsGranted(pluginId, DocumentPermissions.Read, GrantScope.OfDocument(e.Doc.Id)))
                    e.Subscribers.Remove(pluginId);
        }
    }

    private sealed class Exposed(Document document, Func<SelectionSet?>? selections)
    {
        public Document Doc { get; } = document;
        public Func<SelectionSet?>? Selections { get; } = selections;
        public HashSet<string> Subscribers { get; } = [];
        public EventHandler<DocumentChangedEventArgs>? Handler;
    }

    private sealed record Pinned(string Id, DocumentSnapshot Snap, int Bom, DateTimeOffset Expires, Lazy<IReadOnlyList<ByteRange>>? Secrets);

    /// <param name="Secrets">The byte ranges to hide from this plugin; null when the document has none or the plugin may see them.</param>
    private sealed record Target(DocumentSnapshot Snap, int Bom, IReadOnlyList<ByteRange>? Secrets);

    /// <summary>The secrets of a revision of a secrets file (found on first use), or null for any other document. Call on the thread that owns the document.</summary>
    private Lazy<IReadOnlyList<ByteRange>>? SecretsOf(Document d, DocumentSnapshot snap, int bom) =>
        SecretValues.IsSecretFile(d.SecretsPath)
            ? new(() => snap.ByteLength > _options.MaxSecretScanBytes ? [new ByteRange(bom, snap.ByteLength)] : SecretValues.ByteRanges(snap, bom))
            : null;

    private IReadOnlyList<ByteRange>? Hidden(string pluginId, Guid document, Lazy<IReadOnlyList<ByteRange>>? secrets) =>
        secrets is null || _options.Permissions.IsGranted(pluginId, DocumentPermissions.SecretsReveal, GrantScope.OfDocument(document)) ? null : secrets.Value;

    /// <summary>Makes a document visible to plugins. Call on the thread that owns the document.</summary>
    public void Expose(Document document, Func<SelectionSet?>? selections = null)
    {
        var e = new Exposed(document, selections);
        e.Handler = (_, args) => OnChanged(e, args.Change);
        lock (_gate)
        {
            if (!_exposed.TryAdd(document.Id, e)) throw new ArgumentException("The document is already exposed.", nameof(document));
        }
        document.Changed += e.Handler;
    }

    /// <summary>Stops showing a document; its subscribers get <see cref="DocumentEvents.Withdrawn"/>. Call on the thread that owns the document.</summary>
    public void Withdraw(Document document)
    {
        Exposed? e;
        string[] subscribers;
        lock (_gate)
        {
            if (!_exposed.Remove(document.Id, out e)) return;
            subscribers = [.. e.Subscribers];
        }
        document.Changed -= e.Handler;
        ForgetVirtual(document.Id);
        ClearSymbols(key => key.Document == document.Id);
        ClearDiagnostics(key => key.Document == document.Id);
        var payload = Obj(w => w.WriteString("document", document.Id));
        foreach (var plugin in subscribers) Enqueue(plugin, document.Id, DocumentEvents.Withdrawn, payload);
    }

    /// <summary>The permission a method needs; null for the calls that only give something back (unsubscribe, release a snapshot).</summary>
    private static string? Required(string method) => method switch
    {
        DocumentMethods.Unsubscribe or DocumentMethods.SnapshotRelease => null,
        DocumentMethods.EditApply or DocumentMethods.SnippetInsert => DocumentPermissions.EditDirect,
        DocumentMethods.EditPropose or DocumentMethods.WorkspaceEdit => DocumentPermissions.EditPropose,
        DocumentMethods.ClipboardWrite => DocumentPermissions.ClipboardWrite,
        DocumentMethods.VirtualOpen or DocumentMethods.VirtualUpdate => DocumentPermissions.Virtual,
        DocumentMethods.SourceRegister => DocumentPermissions.Source,
        _ => DocumentPermissions.Read,
    };

    /// <summary>The document a request is about: the one it names, or the one its snapshot was taken from. <see cref="GrantScope.Any"/> when there is none.</summary>
    private (GrantScope Scope, Guid? Document) ScopeOf(string pluginId, JsonElement p)
    {
        if (p.ValueKind != JsonValueKind.Object) return (GrantScope.Any, null);
        if (p.TryGetProperty("document", out var d) && d.ValueKind == JsonValueKind.String && Guid.TryParse(d.GetString(), out var id)) return (GrantScope.OfDocument(id), id);
        if (p.TryGetProperty("snapshot", out var s) && s.ValueKind == JsonValueKind.String)
        {
            lock (_gate)
            {
                if (_snapshots.TryGetValue(pluginId, out var list) && list.Find(x => x.Id == s.GetString()) is { } pin)
                    return (GrantScope.OfDocument(pin.Snap.DocumentId), pin.Snap.DocumentId);
            }
        }
        return (GrantScope.Any, null);
    }

    public async ValueTask<byte[]?> HandleAsync(string pluginId, string method, JsonElement p, CancellationToken cancellation)
    {
        var (scope, document) = ScopeOf(pluginId, p);
        return await _options.CallLog.RunAsync(pluginId, method, document, async () =>
        {
            if (Required(method) is not { } permission) return await DispatchAsync(pluginId, method, p, cancellation, null);
            using var lease = _options.Permissions.Enter(pluginId, permission, scope, cancellation);
            try
            {
                var work = DispatchAsync(pluginId, method, p, lease.Token, lease);
                // An edit that already started is not abandoned: the caller must learn that it happened.
                return method is DocumentMethods.EditApply or DocumentMethods.SnippetInsert ? await work : await work.WaitAsync(lease.Token);
            }
            catch (OperationCanceledException) when (lease.Revoked && !cancellation.IsCancellationRequested)
            {
                throw lease.RevokedError();
            }
        });
    }

    private async Task<byte[]?> DispatchAsync(string pluginId, string method, JsonElement p, CancellationToken cancellation, PermissionLease? lease)
    {
        switch (method)
        {
            case DocumentMethods.List: return await ListAsync(pluginId);
            case DocumentMethods.Info: return await InfoAsync(p);
            case DocumentMethods.Subscribe: return await SubscribeAsync(pluginId, p, add: true);
            case DocumentMethods.Unsubscribe: return await SubscribeAsync(pluginId, p, add: false);
            case DocumentMethods.ReadBytes: return await ReadBytesAsync(pluginId, p);
            case DocumentMethods.ReadText: return await ReadTextAsync(pluginId, p);
            case DocumentMethods.Find: return await FindAsync(pluginId, p, cancellation);
            case DocumentMethods.Selections: return await SelectionsAsync(p);
            case DocumentMethods.OffsetToPosition: return await OffsetToPositionAsync(p);
            case DocumentMethods.PositionToOffset: return await PositionToOffsetAsync(p);
            case DocumentMethods.MapRanges: return await MapRangesAsync(p);
            case DocumentMethods.SnapshotCreate: return await SnapshotCreateAsync(pluginId, p);
            case DocumentMethods.SnapshotRelease: return SnapshotRelease(pluginId, p);
            case DocumentMethods.EditApply: return await ApplyAsync(pluginId, p, lease!);
            case DocumentMethods.EditPropose: return await ProposeAsync(pluginId, p, cancellation);
            case DocumentMethods.SymbolsPublish: return await PublishSymbolsAsync(pluginId, p);
            case DocumentMethods.Reveal: return await RevealAsync(p);
            case DocumentMethods.SnippetInsert: return await InsertSnippetAsync(pluginId, p, lease!);
            case DocumentMethods.TabRegister: return RegisterTab(pluginId, p);
            case DocumentMethods.TableSelection: return await TableSelectionAsync(pluginId, p);
            case DocumentMethods.ClipboardWrite: return await ClipboardWriteAsync(p);
            case DocumentMethods.Facts: return await FactsAsync(pluginId, p, cancellation);
            case DocumentMethods.FindBytes: return await FindBytesAsync(pluginId, p, cancellation);
            case DocumentMethods.FindLog: return await FindLogAsync(pluginId, p, cancellation);
            case DocumentMethods.SymbolsList: return await SymbolsListAsync(p);
            case DocumentMethods.DiffCompute: return await DiffComputeAsync(pluginId, p, cancellation);
            case DocumentMethods.DiffOpen: return await DiffOpenAsync(pluginId, p, cancellation);
            case DocumentMethods.DiffStage: return await DiffStageAsync(pluginId, p, cancellation);
            case DocumentMethods.VirtualOpen: return await VirtualOpenAsync(pluginId, p);
            case DocumentMethods.VirtualUpdate: return await VirtualUpdateAsync(pluginId, p);
            case DocumentMethods.DiagnosticsPublish: return await PublishDiagnosticsAsync(pluginId, p);
            case DocumentMethods.DiagnosticsList: return await DiagnosticsListAsync(pluginId, p);
            case DocumentMethods.CompletionRegister: return RegisterCompletion(pluginId, p);
            case DocumentMethods.WorkspaceEdit: return await WorkspaceEditAsync(pluginId, p, cancellation);
            case DocumentMethods.SourceRegister: return RegisterSource(pluginId, p);
            default: throw new RpcException(RpcErrorCodes.MethodNotFound, $"The editor has no method '{method}'.");
        }
    }

    // ---- json helpers ----

    internal static byte[] Obj(Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            body(w);
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    internal static RpcException Invalid(string message) => new(RpcErrorCodes.InvalidParams, message);

    internal static RpcException Fail(int code, string message, Action<Utf8JsonWriter>? data = null) =>
        new(code, message) { DataJson = data is null ? null : Encoding.UTF8.GetString(Obj(data)) };

    private static RpcException Conflict(string reason, long currentRevision, int? index = null) =>
        Fail(DocumentErrorCodes.RevisionConflict, $"Revision conflict ({reason}); the current revision is {currentRevision}.", w =>
        {
            w.WriteString("reason", reason);
            w.WriteNumber("currentRevision", currentRevision);
            if (index is { } i) w.WriteNumber("index", i);
        });

    internal static JsonElement Need(JsonElement p, string name) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var v) ? v : throw Invalid($"Missing parameter '{name}'.");

    internal static long NeedLong(JsonElement p, string name) =>
        Need(p, name) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt64(out var n) ? n : throw Invalid($"Parameter '{name}' must be an integer.");

    internal static string NeedString(JsonElement p, string name) =>
        Need(p, name) is { ValueKind: JsonValueKind.String } v ? v.GetString()! : throw Invalid($"Parameter '{name}' must be a string.");

    private static Guid NeedDocumentId(JsonElement p) =>
        Guid.TryParse(NeedString(p, "document"), out var id) ? id : throw Invalid("Parameter 'document' must be a document id.");

    private Exposed Find(Guid id)
    {
        lock (_gate)
        {
            return _exposed.TryGetValue(id, out var e) ? e : throw Fail(DocumentErrorCodes.UnknownDocument, "The document is not open or not available to plugins.");
        }
    }

    /// <summary>A document an edit may reach: a virtual document is a view of a plugin's data and takes no edit, from a plugin or from an assistant.</summary>
    private Exposed FindWritable(Guid id)
    {
        var e = Find(id);
        if (e.Doc.IsVirtual)
            throw Fail(DocumentErrorCodes.ReadOnlyDocument, "The document is a virtual document and is read only.", w => w.WriteString("reason", "Virtual"));
        return e;
    }

    private static void CheckCurrent(Document d, long revision)
    {
        if (d.Revision != revision) throw Conflict(revision > d.Revision ? "FutureRevision" : "Stale", d.Revision);
    }

    private static void WriteInfo(Utf8JsonWriter w, Document d)
    {
        var (lines, complete) = d.LineIndexState;
        w.WriteString("id", d.Id);
        w.WriteNumber("revision", d.Revision);
        w.WriteNumber("byteLength", d.ByteLength);
        w.WriteString("codec", d.Codec.Name);
        w.WriteNumber("bomLength", d.BomLength);
        if (d.FilePath is { } path) w.WriteString("path", path); else w.WriteNull("path");
        w.WriteStartObject("lineIndex");
        w.WriteNumber("lines", lines);
        w.WriteBoolean("complete", complete);
        w.WriteEndObject();
    }

    // ---- list, info, subscriptions ----

    private async Task<byte[]> ListAsync(string pluginId)
    {
        List<Exposed> all;
        lock (_gate) all = [.. _exposed.Values.Where(x => _options.Permissions.IsGranted(pluginId, DocumentPermissions.Read, GrantScope.OfDocument(x.Doc.Id)))];
        return await _owner.InvokeAsync(() =>
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var w = new Utf8JsonWriter(buffer))
            {
                w.WriteStartArray();
                foreach (var e in all)
                {
                    w.WriteStartObject();
                    WriteInfo(w, e.Doc);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            return buffer.WrittenSpan.ToArray();
        });
    }

    private async Task<byte[]> InfoAsync(JsonElement p)
    {
        var e = Find(NeedDocumentId(p));
        return await _owner.InvokeAsync(() => Obj(w => WriteInfo(w, e.Doc)));
    }

    private async Task<byte[]> SubscribeAsync(string pluginId, JsonElement p, bool add)
    {
        var e = Find(NeedDocumentId(p));
        lock (_gate)
        {
            if (add) e.Subscribers.Add(pluginId); else e.Subscribers.Remove(pluginId);
        }
        var revision = await _owner.InvokeAsync(() => e.Doc.Revision);
        return Obj(w => w.WriteNumber("revision", revision));
    }

    // ---- reading ----

    private async Task<Target> ResolveAsync(string pluginId, JsonElement p)
    {
        if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("snapshot", out var s))
        {
            var id = s.ValueKind == JsonValueKind.String ? s.GetString()! : throw Invalid("Parameter 'snapshot' must be a string.");
            lock (_gate)
            {
                PurgeExpired(pluginId);
                // the lifetime counts from the last read, so a long scan of one snapshot keeps it alive while a forgotten one still expires
                if (_snapshots.TryGetValue(pluginId, out var list) && list.FindIndex(x => x.Id == id) is var at and >= 0)
                {
                    var pin = list[at] = list[at] with { Expires = _time.GetUtcNow() + _options.SnapshotLifetime };
                    return new Target(pin.Snap, pin.Bom, Hidden(pluginId, pin.Snap.DocumentId, pin.Secrets));
                }
            }
            throw Fail(DocumentErrorCodes.SnapshotExpired, "The snapshot has expired or was released.");
        }
        var e = Find(NeedDocumentId(p));
        var revision = NeedLong(p, "revision");
        return await _owner.InvokeAsync(() =>
        {
            CheckCurrent(e.Doc, revision);
            var snap = e.Doc.GetSnapshot();
            return new Target(snap, e.Doc.BomLength, Hidden(pluginId, e.Doc.Id, SecretsOf(e.Doc, snap, e.Doc.BomLength)));
        });
    }

    private ByteRange ReadRange(Target t, JsonElement p, bool limit)
    {
        long start = NeedLong(p, "start"), end = NeedLong(p, "end");
        if (start < 0 || end < start) throw Fail(DocumentErrorCodes.InvalidRange, $"The range [{start}, {end}) is not valid.");
        if (limit && end - start > _options.MaxReadBytes) throw new RpcException(RpcErrorCodes.MessageTooLarge, $"A read returns at most {_options.MaxReadBytes} bytes; asked for {end - start}.");
        if (end > t.Snap.ByteLength) throw Fail(DocumentErrorCodes.InvalidRange, $"The range [{start}, {end}) is outside the document ({t.Snap.ByteLength} bytes).");
        return new ByteRange(start, end);
    }

    private static T Guarded<T>(DocumentSnapshot snap, Func<T> read)
    {
        try { return read(); }
        catch (SnapshotInvalidatedException) { throw Conflict("SourceChanged", snap.Revision); }
    }

    private async Task<byte[]> ReadBytesAsync(string pluginId, JsonElement p)
    {
        var t = await ResolveAsync(pluginId, p);
        var range = ReadRange(t, p, limit: true);
        var bytes = Guarded(t.Snap, () => t.Snap.ReadBytes(range));
        if (t.Secrets is not null) SecretValues.MaskBytes(bytes, range.Start, t.Secrets, t.Snap.Codec);
        return Obj(w =>
        {
            w.WriteNumber("revision", t.Snap.Revision);
            w.WriteNumber("start", range.Start);
            w.WriteNumber("end", range.End);
            w.WriteBase64String("bytes", bytes);
        });
    }

    private static bool IsBoundary(Target t, long offset)
    {
        var snap = t.Snap;
        var codec = snap.Codec;
        if (offset < t.Bom || offset > snap.ByteLength) return false;
        if (offset == t.Bom || offset == snap.ByteLength) return true;
        long start = Math.Max(t.Bom, offset - 4);
        start -= (start - t.Bom) % codec.Alignment;
        long end = Math.Min(snap.ByteLength, offset + 4);
        var window = new byte[end - start];
        snap.Read(start, window);
        return codec.IsBoundary(window, (int)(offset - start));
    }

    /// <summary>
    /// Text of a range between character boundaries. Bytes that are not valid in the encoding are decoded as U+FFFD and their
    /// indexes in <c>text</c> are listed in <c>invalid</c>, so a replacement character is never mistaken for one in the file; the bytes come from readBytes.
    /// </summary>
    private async Task<byte[]> ReadTextAsync(string pluginId, JsonElement p)
    {
        var t = await ResolveAsync(pluginId, p);
        var range = ReadRange(t, p, limit: true);
        // with "snap" the range grows to the next character boundaries instead of being refused; the answer names the range actually read
        bool snap = p.TryGetProperty("snap", out var snapFlag) && snapFlag.ValueKind == JsonValueKind.True;
        var bytes = Guarded(t.Snap, () =>
        {
            if (snap)
            {
                long start = range.Start, end = range.End;
                while (start > t.Bom && !IsBoundary(t, start)) start--;
                while (end < t.Snap.ByteLength && !IsBoundary(t, end)) end++;
                range = new ByteRange(start, end);
            }
            if (!IsBoundary(t, range.Start) || !IsBoundary(t, range.End)) throw Fail(DocumentErrorCodes.InvalidRange, $"The range {range} does not fall on character boundaries.");
            return t.Snap.ReadBytes(range);
        });
        if (t.Secrets is not null) SecretValues.MaskBytes(bytes, range.Start, t.Secrets, t.Snap.Codec);
        var decoded = t.Snap.Codec.WithBom(false).Decode(bytes);
        return Obj(w =>
        {
            w.WriteNumber("revision", t.Snap.Revision);
            w.WriteNumber("start", range.Start);
            w.WriteNumber("end", range.End);
            w.WriteString("text", decoded.Text);
            w.WriteStartArray("invalid");
            if (decoded.InvalidCount > 0)
                for (int i = 0; i < decoded.Text.Length; i++)
                    if (decoded.IsInvalid(i)) w.WriteNumberValue(i);
            w.WriteEndArray();
        });
    }

    /// <summary>Literal, case-sensitive search for text encoded the way the document is; matches are byte ranges in the named revision.</summary>
    private async Task<byte[]> FindAsync(string pluginId, JsonElement p, CancellationToken ct)
    {
        var t = await ResolveAsync(pluginId, p);
        var snap = t.Snap;
        var text = NeedString(p, "text");
        if (text.Length == 0) throw Invalid("Parameter 'text' must not be empty.");
        byte[] needle;
        try { needle = snap.Codec.Encode(text); }
        catch (UnrepresentableCharactersException) { throw Invalid("The text cannot be written in the encoding of the document."); }

        long from = p.TryGetProperty("start", out _) ? NeedLong(p, "start") : t.Bom;
        long to = p.TryGetProperty("end", out _) ? NeedLong(p, "end") : snap.ByteLength;
        if (from < 0 || to < from || to > snap.ByteLength) throw Fail(DocumentErrorCodes.InvalidRange, $"The range [{from}, {to}) is outside the document ({snap.ByteLength} bytes).");

        var matches = new List<long>();
        bool truncated = false;
        var buffer = new byte[Math.Max(1 << 16, needle.Length * 2)];
        int alignment = snap.Codec.Alignment;
        Guarded(snap, () =>
        {
            for (long pos = from; pos + needle.Length <= to && !truncated;)
            {
                ct.ThrowIfCancellationRequested();
                int want = (int)Math.Min(buffer.Length, to - pos);
                int got = snap.Read(pos, buffer.AsSpan(0, want));
                if (got < needle.Length) break;
                var span = buffer.AsSpan(0, got);
                for (int at = 0; ;)
                {
                    int i = span[at..].IndexOf(needle);
                    if (i < 0) break;
                    long absolute = pos + at + i;
                    at += i + 1;
                    if (absolute < t.Bom || (absolute - t.Bom) % alignment != 0) continue;
                    if (t.Secrets is not null && SecretValues.Overlaps(t.Secrets, absolute, absolute + needle.Length)) continue; // a match that touches a hidden value would tell what it is
                    if (matches.Count == _options.MaxFindMatches) { truncated = true; break; }
                    matches.Add(absolute);
                }
                pos += got - (needle.Length - 1);
            }
            return 0;
        });
        return Obj(w =>
        {
            w.WriteNumber("revision", snap.Revision);
            w.WriteStartArray("matches");
            foreach (var m in matches)
            {
                w.WriteStartObject();
                w.WriteNumber("start", m);
                w.WriteNumber("end", m + needle.Length);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteBoolean("truncated", truncated);
        });
    }

    private async Task<byte[]> SelectionsAsync(JsonElement p)
    {
        var e = Find(NeedDocumentId(p));
        var (revision, set) = await _owner.InvokeAsync(() => (e.Doc.Revision, e.Selections?.Invoke()));
        return Obj(w =>
        {
            w.WriteNumber("revision", revision);
            w.WriteNumber("primary", set?.Primary ?? -1);
            w.WriteStartArray("selections");
            foreach (var s in set?.Items ?? [])
            {
                w.WriteStartObject();
                w.WriteNumber("anchor", s.Anchor);
                w.WriteNumber("caret", s.Caret);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });
    }

    // ---- positions ----

    private static RpcException PositionUnknown(Document d)
    {
        var (lines, complete) = d.LineIndexState;
        return Fail(DocumentErrorCodes.PositionUnknown,
            complete ? "The offset is not a text position." : "The line index does not cover this position yet.",
            w => { w.WriteNumber("lines", lines); w.WriteBoolean("complete", complete); });
    }

    private async Task<byte[]> OffsetToPositionAsync(JsonElement p)
    {
        var e = Find(NeedDocumentId(p));
        long revision = NeedLong(p, "revision"), offset = NeedLong(p, "offset");
        return await _owner.InvokeAsync(() =>
        {
            var d = e.Doc;
            CheckCurrent(d, revision);
            if (offset < 0 || offset > d.ByteLength) throw Fail(DocumentErrorCodes.InvalidRange, $"The offset {offset} is outside the document.");
            if (!d.TryGetPosition(offset, out var pos)) throw PositionUnknown(d);
            return Obj(w =>
            {
                w.WriteNumber("revision", revision);
                w.WriteNumber("line", pos.Line);
                w.WriteNumber("column", pos.Column);
            });
        });
    }

    private async Task<byte[]> PositionToOffsetAsync(JsonElement p)
    {
        var e = Find(NeedDocumentId(p));
        long revision = NeedLong(p, "revision"), line = NeedLong(p, "line"), column = NeedLong(p, "column");
        if (line < 0 || column < 0) throw Invalid("Line and column must not be negative.");
        return await _owner.InvokeAsync(() =>
        {
            var d = e.Doc;
            CheckCurrent(d, revision);
            if (!d.TryGetOffset(new LinePosition(line, column), out var offset)) throw PositionUnknown(d);
            return Obj(w =>
            {
                w.WriteNumber("revision", revision);
                w.WriteNumber("offset", offset);
            });
        });
    }

    /// <summary>
    /// Maps ranges of an older revision to the current one through the change log. One range that a later change touched, or a log
    /// that no longer reaches back, fails the whole call with a conflict that names the range; no position is guessed.
    /// </summary>
    private async Task<byte[]> MapRangesAsync(JsonElement p)
    {
        var e = Find(NeedDocumentId(p));
        long from = NeedLong(p, "fromRevision");
        var items = Need(p, "ranges");
        if (items.ValueKind != JsonValueKind.Array) throw Invalid("Parameter 'ranges' must be an array.");
        List<ByteRange> ranges = [];
        foreach (var item in items.EnumerateArray())
        {
            long start = NeedLong(item, "start"), end = NeedLong(item, "end");
            if (start < 0 || end < start) throw Invalid($"The range [{start}, {end}) is not valid.");
            ranges.Add(new ByteRange(start, end));
        }
        return await _owner.InvokeAsync(() =>
        {
            var d = e.Doc;
            if (from > d.Revision || from < 0) throw Conflict("FutureRevision", d.Revision);
            if (from < d.OldestMappableRevision) throw Conflict("LogTruncated", d.Revision);
            var mapped = new List<ByteRange>(ranges.Count);
            for (int i = 0; i < ranges.Count; i++)
            {
                var m = d.MapRange(ranges[i], from);
                if (!m.Ok) throw Conflict(m.Conflict.ToString(), d.Revision, i);
                mapped.Add(m.Range);
            }
            return Obj(w =>
            {
                w.WriteNumber("revision", d.Revision);
                w.WriteStartArray("ranges");
                foreach (var m in mapped)
                {
                    w.WriteStartObject();
                    w.WriteNumber("start", m.Start);
                    w.WriteNumber("end", m.End);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            });
        });
    }

    // ---- snapshots ----

    private void PurgeExpired(string pluginId)
    {
        if (_snapshots.TryGetValue(pluginId, out var list)) list.RemoveAll(s => s.Expires <= _time.GetUtcNow());
    }

    private async Task<byte[]> SnapshotCreateAsync(string pluginId, JsonElement p)
    {
        var e = Find(NeedDocumentId(p));
        var (snap, bom, secrets) = await _owner.InvokeAsync(() =>
        {
            var s = e.Doc.GetSnapshot();
            return (s, e.Doc.BomLength, SecretsOf(e.Doc, s, e.Doc.BomLength));
        });
        var pin = new Pinned(Guid.NewGuid().ToString("N"), snap, bom, _time.GetUtcNow() + _options.SnapshotLifetime, secrets);
        lock (_gate)
        {
            PurgeExpired(pluginId);
            if (!_snapshots.TryGetValue(pluginId, out var list)) _snapshots[pluginId] = list = [];
            if (list.Count >= _options.MaxSnapshotsPerPlugin)
                throw Fail(DocumentErrorCodes.SnapshotQuotaExceeded, $"A plugin may hold {_options.MaxSnapshotsPerPlugin} snapshots; release one or wait for one to expire.",
                    w => w.WriteNumber("limit", _options.MaxSnapshotsPerPlugin));
            list.Add(pin);
        }
        return Obj(w =>
        {
            w.WriteString("snapshot", pin.Id);
            w.WriteString("document", snap.DocumentId);
            w.WriteNumber("revision", snap.Revision);
            w.WriteNumber("byteLength", snap.ByteLength);
            w.WriteNumber("lifetimeMs", (long)_options.SnapshotLifetime.TotalMilliseconds);
        });
    }

    private byte[] SnapshotRelease(string pluginId, JsonElement p)
    {
        var id = NeedString(p, "snapshot");
        bool released;
        lock (_gate) released = _snapshots.TryGetValue(pluginId, out var list) && list.RemoveAll(s => s.Id == id) > 0;
        return Obj(w => w.WriteBoolean("released", released));
    }

    /// <summary>
    /// The state a (re)started plugin needs: the revisions of the documents it subscribed to. It also drops what the old process left behind
    /// (queued events and snapshots), because the state returned here replaces them.
    /// </summary>
    public byte[] BuildState(string pluginId)
    {
        ClearOutbox(pluginId);
        ClearSymbols(key => key.Plugin == pluginId);
        ClearDiagnostics(key => key.Plugin == pluginId);
        lock (_gate)
        {
            _snapshots.Remove(pluginId);
            _tabProviders.Remove(pluginId);
            _completionProviders.Remove(pluginId);
            var buffer = new ArrayBufferWriter<byte>();
            using (var w = new Utf8JsonWriter(buffer))
            {
                w.WriteStartObject();
                w.WriteStartArray("documents");
                foreach (var e in _exposed.Values.Where(x => x.Subscribers.Contains(pluginId)))
                {
                    w.WriteStartObject();
                    w.WriteString("id", e.Doc.Id);
                    w.WriteNumber("revision", e.Doc.Revision);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteStartArray("proposals");
                w.WriteEndArray();
                w.WriteEndObject();
            }
            return buffer.WrittenSpan.ToArray();
        }
    }
}
