using System.Text.Json;
using System.Text.RegularExpressions;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Documents;

/// <summary>
/// File sources (P6.2.1, P6.2.3): a plugin serves the files of a URI scheme, the editor lists, reads and stats them, caches what it read, and writes back only against the
/// revision it read. A revision is an opaque string the source chooses (an ETag, a version number); the editor only compares it.
/// </summary>
public sealed partial class DocumentApi
{
    private readonly Dictionary<string, FileSource> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly SourceCache? _cache;

    [GeneratedRegex("^[a-z][a-z0-9+.-]*$")]
    private static partial Regex SchemePattern();

    /// <summary>The scheme <c>file</c> belongs to the editor.</summary>
    private const string ReservedScheme = "file";

    /// <summary>The sources plugins offer, by scheme.</summary>
    public IReadOnlyList<FileSource> Sources
    {
        get { lock (_gate) return [.. _sources.Values.OrderBy(s => s.Scheme, StringComparer.Ordinal)]; }
    }

    /// <summary>The source that serves the scheme of <paramref name="uri"/>, or null.</summary>
    public FileSource? SourceFor(string uri)
    {
        if (UriMask.SchemeOf(uri) is not { } scheme) return null;
        lock (_gate) return _sources.GetValueOrDefault(scheme);
    }

    /// <summary>A file of the cache without asking the source (null when it is not cached).</summary>
    public SourceContent? CachedSource(string uri) => _cache?.Get(uri);

    private byte[] RegisterSource(string pluginId, JsonElement p)
    {
        var options = _options.Sources ?? throw new RpcException(RpcErrorCodes.MethodNotFound, "The editor takes no file sources.");
        var scheme = NeedString(p, "scheme");
        if (!SchemePattern().IsMatch(scheme) || scheme == ReservedScheme) throw Invalid($"'{scheme}' is not a scheme a source can serve (lower case letters, digits, '+', '-', '.'; not 'file').");
        bool enabled = !p.TryGetProperty("enabled", out var v) || v.ValueKind != JsonValueKind.False;
        lock (_gate)
        {
            if (!enabled)
            {
                if (_sources.TryGetValue(scheme, out var own) && own.PluginId == pluginId) _sources.Remove(scheme);
                return Obj(w => w.WriteBoolean("enabled", false));
            }
            if (_sources.TryGetValue(scheme, out var other) && other.PluginId != pluginId)
                throw Fail(DocumentErrorCodes.SourceSchemeTaken, $"The scheme '{scheme}' is served by another plugin.");
            if (other is null && _sources.Values.Count(s => s.PluginId == pluginId) >= options.MaxSchemesPerPlugin)
                throw Fail(DocumentErrorCodes.SourceSchemeTaken, $"A plugin may serve {options.MaxSchemesPerPlugin} schemes.", w => w.WriteNumber("limit", options.MaxSchemesPerPlugin));
            bool write = Flag(p, "write"), atomic = Flag(p, "atomicWrite");
            if (atomic && !write) throw Invalid("A source that writes atomically must also declare 'write'.");
            var name = p.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && n.GetString() is { Length: > 0 } given ? given : scheme;
            _sources[scheme] = new FileSource(pluginId, scheme, name, new SourceCapabilities(Flag(p, "list"), write, atomic));
        }
        return Obj(w => w.WriteBoolean("enabled", true));
    }

    private static bool Flag(JsonElement p, string name) => p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private void DropRevokedSources(string pluginId)
    {
        if (_options.Permissions.IsGranted(pluginId, DocumentPermissions.Source, GrantScope.Any)) return;
        lock (_gate)
        {
            foreach (var scheme in _sources.Where(s => s.Value.PluginId == pluginId).Select(s => s.Key).ToArray()) _sources.Remove(scheme);
        }
    }

    private (FileSource Source, FileSourceOptions Options) NeedSource(string uri)
    {
        var options = _options.Sources ?? throw new SourceException(SourceFailure.NoSource, "The editor takes no file sources.");
        return (SourceFor(uri) ?? throw new SourceException(SourceFailure.NoSource, $"No plugin serves '{UriMask.Mask(uri)}'."), options);
    }

    private async Task<JsonElement> AskSourceAsync(FileSource source, FileSourceOptions options, string method, byte[] request, CancellationToken cancellation)
    {
        if (!_options.Permissions.IsGranted(source.PluginId, DocumentPermissions.Source, GrantScope.Any))
            throw new SourceException(SourceFailure.Unavailable, $"The plugin of '{source.Scheme}' may not serve files.");
        try { return (await _request(source.PluginId, method, request, options.Timeout, cancellation)).Clone(); }
        catch (RpcException e) when (e.Code == DocumentErrorCodes.SourceConflict) { throw new SourceException(SourceFailure.Conflict, e.Message, CurrentRevisionOf(e)); }
        catch (RpcException e) when (e.Code == DocumentErrorCodes.SourceNotFound) { throw new SourceException(SourceFailure.NotFound, e.Message); }
        catch (RpcException e) when (e.Code == DocumentErrorCodes.SourceUnavailable) { throw new SourceException(SourceFailure.Unavailable, e.Message); }
        catch (RpcException e) { throw new SourceException(SourceFailure.Failed, e.Message); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is TimeoutException or IOException or PluginCrashedException or PluginUnavailableException or OperationCanceledException) { throw new SourceException(SourceFailure.Unavailable, e.Message); }
    }

    private static string? CurrentRevisionOf(RpcException e)
    {
        if (e.DataJson is not { } json) return null;
        try
        {
            using var data = JsonDocument.Parse(json);
            return data.RootElement.TryGetProperty("currentRevision", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static string RevisionOf(JsonElement answer) =>
        answer.ValueKind == JsonValueKind.Object && answer.TryGetProperty("revision", out var r) && r.ValueKind == JsonValueKind.String && r.GetString() is { Length: > 0 } text
            ? text
            : throw new SourceException(SourceFailure.Failed, "The source answered without a revision.");

    private static byte[] UriRequest(string uri, Action<Utf8JsonWriter>? more = null) => Obj(w => { w.WriteString("uri", uri); more?.Invoke(w); });

    /// <summary>The revision and size of the file, or null when it does not exist.</summary>
    public async Task<SourceStat?> SourceStatAsync(string uri, CancellationToken cancellation = default)
    {
        var (source, options) = NeedSource(uri);
        var answer = await AskSourceAsync(source, options, DocumentRequests.SourceStat, UriRequest(uri), cancellation);
        if (answer.ValueKind == JsonValueKind.Object && answer.TryGetProperty("exists", out var exists) && exists.ValueKind == JsonValueKind.False) return null;
        long size = answer.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
        return new SourceStat(RevisionOf(answer), size);
    }

    /// <summary>
    /// Reads a file with its revision and keeps a copy in the cache. When the source cannot be reached the cached copy is returned with <see cref="SourceContent.FromCache"/> set;
    /// without a copy the <see cref="SourceFailure.Unavailable"/> failure stands.
    /// </summary>
    public async Task<SourceContent> SourceReadAsync(string uri, CancellationToken cancellation = default)
    {
        var (source, options) = NeedSource(uri);
        try
        {
            var answer = await AskSourceAsync(source, options, DocumentRequests.SourceRead, UriRequest(uri), cancellation);
            string revision = RevisionOf(answer);
            byte[] content;
            try { content = ReadContent(answer, TextCodec.Utf8, options.MaxBytes); }
            catch (RpcException e) { throw new SourceException(e.Code == RpcErrorCodes.MessageTooLarge ? SourceFailure.TooLarge : SourceFailure.Failed, e.Message); }
            _cache?.Put(uri, content, revision, _time.GetUtcNow());
            return new SourceContent(content, revision);
        }
        catch (SourceException e) when (e.Kind == SourceFailure.Unavailable && _cache?.Get(uri) is { } cached)
        {
            return cached;
        }
    }

    public async Task<IReadOnlyList<SourceEntry>> SourceListAsync(string uri, CancellationToken cancellation = default)
    {
        var (source, options) = NeedSource(uri);
        if (!source.Capabilities.List) throw new SourceException(SourceFailure.Failed, $"The source '{source.Name}' does not list.");
        var answer = await AskSourceAsync(source, options, DocumentRequests.SourceList, UriRequest(uri), cancellation);
        if (answer.ValueKind != JsonValueKind.Object || !answer.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            throw new SourceException(SourceFailure.Failed, "The source answered without entries.");
        List<SourceEntry> list = [];
        foreach (var e in entries.EnumerateArray())
        {
            if (list.Count >= options.MaxEntries) break;
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("uri", out var u) || u.ValueKind != JsonValueKind.String) continue;
            string entryUri = u.GetString()!;
            string name = e.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString()! : UriMask.DisplayName(entryUri);
            bool dir = e.TryGetProperty("isDirectory", out var d) && d.ValueKind == JsonValueKind.True;
            list.Add(new SourceEntry(entryUri, name, dir, e.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var bytes) ? bytes : null));
        }
        return list;
    }

    /// <summary>
    /// Writes the file if it is still at <paramref name="expectedRevision"/> (null: it must not exist) and returns its new revision. A different revision is a
    /// <see cref="SourceFailure.Conflict"/> and nothing is written. A source that declared <see cref="SourceCapabilities.AtomicWrite"/> decides that itself in one step; any other
    /// source is asked for the revision first and written afterwards, so a change in between is not noticed (the caller warns the user about that).
    /// </summary>
    public async Task<string> SourceWriteAsync(string uri, ReadOnlyMemory<byte> content, string? expectedRevision, CancellationToken cancellation = default)
    {
        var (source, options) = NeedSource(uri);
        if (!source.Capabilities.Write) throw new SourceException(SourceFailure.ReadOnly, $"The source '{source.Name}' does not write.");
        if (content.Length > options.MaxBytes) throw new SourceException(SourceFailure.TooLarge, $"A source file holds at most {options.MaxBytes} bytes; got {content.Length}.");
        if (!source.Capabilities.AtomicWrite)
        {
            var current = (await SourceStatAsync(uri, cancellation))?.Revision;
            if (current != expectedRevision) throw new SourceException(SourceFailure.Conflict, "The file changed at the source.", current);
        }
        var bytes = content.ToArray();
        var answer = await AskSourceAsync(source, options, DocumentRequests.SourceWrite, UriRequest(uri, w =>
        {
            if (expectedRevision is null) w.WriteNull("expectedRevision"); else w.WriteString("expectedRevision", expectedRevision);
            w.WriteBase64String("bytes", bytes);
        }), cancellation);
        var revision = RevisionOf(answer);
        _cache?.Put(uri, bytes, revision, _time.GetUtcNow());
        return revision;
    }
}
