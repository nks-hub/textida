namespace NksHub.Textida.PluginHost.Documents;

/// <summary>What a file source can do. Reading and stat are always there. <paramref name="AtomicWrite"/> (which implies <paramref name="Write"/>): the source writes only if the file is still at the expected revision, in one step.</summary>
public sealed record SourceCapabilities(bool List, bool Write, bool AtomicWrite);

/// <summary>A plugin that serves the files of a URI scheme.</summary>
public sealed record FileSource(string PluginId, string Scheme, string Name, SourceCapabilities Capabilities);

/// <summary>The revision (an opaque string the source chose) and size of a file.</summary>
public sealed record SourceStat(string Revision, long Size);

public sealed record SourceEntry(string Uri, string Name, bool IsDirectory, long? Size);

/// <summary>A file read from a source. <paramref name="FromCache"/>: the source could not be reached and this is the copy of <paramref name="CachedAt"/>, still at the revision it was read at.</summary>
public sealed record SourceContent(byte[] Content, string Revision, bool FromCache = false, DateTimeOffset? CachedAt = null);

public enum SourceFailure
{
    /// <summary>No plugin serves the scheme of the URI.</summary>
    NoSource,
    /// <summary>The source cannot be reached (no connection, plugin down, timeout).</summary>
    Unavailable,
    NotFound,
    /// <summary>The file is not at the expected revision; nothing was written. <see cref="SourceException.CurrentRevision"/> is where it is now (null: gone).</summary>
    Conflict,
    /// <summary>The source does not write.</summary>
    ReadOnly,
    TooLarge,
    Failed,
}

public sealed class SourceException(SourceFailure kind, string message, string? currentRevision = null) : Exception(message)
{
    public SourceFailure Kind { get; } = kind;
    public string? CurrentRevision { get; } = currentRevision;
}

/// <summary>
/// Limits of the file sources. They are owner decisions, so there are no defaults. <paramref name="CacheEntries"/> and <paramref name="CacheBytes"/> bound the cache of
/// the files last read or written (memory only), least recently used out first.
/// </summary>
public sealed record FileSourceOptions(TimeSpan Timeout, long MaxBytes, int MaxEntries, int MaxSchemesPerPlugin, int CacheEntries, long CacheBytes);

/// <summary>The copies of files last read from or written to a source, kept so a tab can open without a connection. Bounded by count and by bytes; the least recently used go first.</summary>
public sealed class SourceCache(int maxEntries, long maxBytes)
{
    private sealed record Entry(byte[] Content, string Revision, DateTimeOffset At);

    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Uri, Entry Entry)>> _index = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Uri, Entry Entry)> _order = new();
    private long _bytes;

    public int Count { get { lock (_gate) return _index.Count; } }
    public long Bytes { get { lock (_gate) return _bytes; } }

    /// <summary>Keeps the copy, evicting older ones to stay within the bounds. A file larger than the byte bound is not kept (and a copy of it is dropped); returns whether it was kept.</summary>
    public bool Put(string uri, byte[] content, string revision, DateTimeOffset at)
    {
        lock (_gate)
        {
            Remove(uri);
            if (content.LongLength > maxBytes) return false;
            _index[uri] = _order.AddFirst((uri, new Entry(content, revision, at)));
            _bytes += content.LongLength;
            while (_index.Count > maxEntries || _bytes > maxBytes) Remove(_order.Last!.Value.Uri);
            return true;
        }
    }

    public SourceContent? Get(string uri)
    {
        lock (_gate)
        {
            if (!_index.TryGetValue(uri, out var node)) return null;
            _order.Remove(node);
            _order.AddFirst(node);
            return new SourceContent(node.Value.Entry.Content, node.Value.Entry.Revision, true, node.Value.Entry.At);
        }
    }

    public bool Contains(string uri)
    {
        lock (_gate) return _index.ContainsKey(uri);
    }

    public void Remove(string uri)
    {
        lock (_gate)
        {
            if (!_index.Remove(uri, out var node)) return;
            _order.Remove(node);
            _bytes -= node.Value.Entry.Content.LongLength;
        }
    }
}
