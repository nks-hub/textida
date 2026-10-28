using System.Net;
using System.Text;
using System.Text.Json;

namespace NksHub.Textida.Plugins.Json.Packages;

/// <summary>A registry did not give a usable answer (no connection, an HTTP error, too large, not what the API documents). <see cref="Status"/> is the HTTP status, or 0.</summary>
public sealed class RegistryException(string message, Exception? inner = null, int status = 0) : Exception(message, inner)
{
    public int Status { get; } = status;
    public bool NotFound => Status is 404 or 410;
}

/// <summary>The user has not allowed lookups at this registry; no request was made.</summary>
public sealed class ConsentRequiredException(string registry) : Exception($"Lookups at '{registry}' are not allowed.")
{
    public string Registry { get; } = registry;
}

/// <param name="Cached">True when the answer came from the cache of this process and no request was made for it.</param>
public sealed record RegistryAnswer(string Body, bool Cached);

/// <param name="ConfigPath">The file the editor keeps the consent in: <c>{"allowed":["npm",...],"proxy":"socks5://host:port","blocked":false}; <c>blocked</c> (a proxy the plugin cannot log in to) stops every lookup</c>. Read on every request, so a revoke takes effect at once.</param>
/// <param name="ManifestMaxBytes">Largest manifest read whole; a larger one gets no offers and no marks.</param>
/// <param name="MaxLookups">Most registry requests one check of a manifest makes; the rest of the entries are left unchecked.</param>
/// <param name="CacheMaxBytes">Most bytes of answers the cache of this process holds; the oldest answers are dropped to make room, and one larger than this is not kept. Zero means no bound.</param>
/// <param name="CacheLifetimeSeconds">How long an answer is served from the cache; an older one is asked for again. Zero means it never expires.</param>
public sealed record RegistrySettings(string ConfigPath, int TimeoutMilliseconds, long MaxResponseBytes, int PageSize, int MaxLookups, long ManifestMaxBytes, long CacheMaxBytes = 0, int CacheLifetimeSeconds = 0);

/// <summary>
/// The one way the editor reaches a package registry (and the address of a schema). It asks nothing without consent (read from the config file on every call, so the first line of every request is the
/// check), keeps the answers of this process in a bounded cache that expires, tells the caller whether an answer came from it, and can drop it per registry.
/// There is no vulnerability lookup and nothing here downloads a package.
/// </summary>
/// <param name="clock">The time source of the cache lifetime; the system clock when omitted.</param>
public sealed class RegistryClient(RegistrySettings settings, TimeProvider? clock = null) : IDisposable
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Dictionary<string, (string Body, long Stored, long Order)> _cache = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private long _cacheBytes;
    private long _order;
    private HttpClient? _http;
    private string? _proxy;
    private int _requests;

    /// <summary>How many requests went out to a registry (cached answers do not count).</summary>
    public int Requests => Volatile.Read(ref _requests);

    /// <summary>How many bytes of answers the cache holds now.</summary>
    public long CachedBytes
    {
        get { lock (_gate) return _cacheBytes; }
    }

    private (bool Allowed, string? Proxy) Config(string registry)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(settings.ConfigPath, Encoding.UTF8));
            bool blocked = doc.RootElement.TryGetProperty("blocked", out var b) && b.ValueKind == JsonValueKind.True;
            bool allowed = !blocked && doc.RootElement.TryGetProperty("allowed", out var list) && list.ValueKind == JsonValueKind.Array && list.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == registry);
            string? proxy = doc.RootElement.TryGetProperty("proxy", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            return (allowed, proxy);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return (false, null); }
    }

    /// <summary>Whether the user allowed requests under this consent id now (a registry id, or <c>schemas:host</c>).</summary>
    public bool IsAllowed(string registry) => Config(registry).Allowed;

    /// <summary>Drops the cached answers of one registry, so the next lookup asks it again.</summary>
    public void Refresh(string registry)
    {
        lock (_gate)
        {
            foreach (var key in _cache.Keys.Where(k => k.StartsWith(registry + " ", StringComparison.Ordinal)).ToList()) Remove(key);
        }
    }

    private void Remove(string key)
    {
        if (_cache.Remove(key, out var old)) _cacheBytes -= Encoding.UTF8.GetByteCount(old.Body);
    }

    private bool TryGetCached(string key, out string body)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var hit))
            {
                if (settings.CacheLifetimeSeconds <= 0 || _clock.GetElapsedTime(hit.Stored) < TimeSpan.FromSeconds(settings.CacheLifetimeSeconds))
                {
                    body = hit.Body;
                    return true;
                }
                Remove(key);
            }
        }
        body = "";
        return false;
    }

    private void Store(string key, string body)
    {
        long size = Encoding.UTF8.GetByteCount(body);
        if (settings.CacheMaxBytes > 0 && size > settings.CacheMaxBytes) return;
        lock (_gate)
        {
            Remove(key);
            while (settings.CacheMaxBytes > 0 && _cacheBytes + size > settings.CacheMaxBytes && _cache.Count > 0)
                Remove(_cache.MinBy(x => x.Value.Order).Key);
            _cache[key] = (body, _clock.GetTimestamp(), _order++);
            _cacheBytes += size;
        }
    }

    private HttpClient Http(string? proxy)
    {
        if (_http is not null && proxy == _proxy) return _http;
        _http?.Dispose();
        var handler = new SocketsHttpHandler { UseProxy = proxy is not null, Proxy = proxy is null ? null : new WebProxy(new Uri(proxy), false) };
        _proxy = proxy;
        return _http = new HttpClient(handler, true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <exception cref="ConsentRequiredException">The registry is not allowed; nothing was sent and the cache of it is dropped.</exception>
    /// <exception cref="RegistryException">The request failed or the answer is not usable.</exception>
    public async Task<RegistryAnswer> GetAsync(RegistryAdapter adapter, Uri url, CancellationToken ct = default)
    {
        var (allowed, proxy) = Config(adapter.Id);
        if (!allowed)
        {
            Refresh(adapter.Id);
            throw new ConsentRequiredException(adapter.Id);
        }
        string key = adapter.Id + " " + url;
        if (TryGetCached(key, out var hit)) return new RegistryAnswer(hit, true);
        var body = await SendAsync(proxy, adapter.Accept, url, settings.MaxResponseBytes, ct);
        Store(key, body);
        return new RegistryAnswer(body, false);
    }

    /// <summary>
    /// One request for a caller that keeps its own copy of the answer (the schema cache on disk): the same consent check, proxy, time limit and size limit as <see cref="GetAsync"/>, but no cache of this process.
    /// </summary>
    /// <param name="registry">The consent id the request is made under.</param>
    /// <param name="maxBytes">Largest body that is read; a larger answer is a <see cref="RegistryException"/>.</param>
    /// <exception cref="ConsentRequiredException">The id is not allowed; nothing was sent.</exception>
    /// <exception cref="RegistryException">The request failed or the answer is too large.</exception>
    public async Task<string> FetchAsync(string registry, string? accept, Uri url, long maxBytes, CancellationToken ct = default)
    {
        var (allowed, proxy) = Config(registry);
        if (!allowed) throw new ConsentRequiredException(registry);
        return await SendAsync(proxy, accept, url, maxBytes, ct);
    }

    private async Task<string> SendAsync(string? proxy, string? accept, Uri url, long maxBytes, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(settings.TimeoutMilliseconds);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Textida");
            if (accept is not null) request.Headers.Accept.ParseAdd(accept);
            Interlocked.Increment(ref _requests);
            using var response = await Http(proxy).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new RegistryException($"{url.Host} answered HTTP {(int)response.StatusCode}.", null, (int)response.StatusCode);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
            {
                if (buffer.Length + read > maxBytes) throw new RegistryException($"{url.Host} answered more than {maxBytes} bytes.");
                buffer.Write(chunk, 0, read);
            }
            return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new RegistryException($"{url.Host} did not answer in {settings.TimeoutMilliseconds} ms."); }
        catch (HttpRequestException e) { throw new RegistryException($"{url.Host}: {e.Message}", e); }
    }

    public void Dispose() => _http?.Dispose();
}
