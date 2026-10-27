using System.Collections.Concurrent;
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
public sealed record RegistrySettings(string ConfigPath, int TimeoutMilliseconds, long MaxResponseBytes, int PageSize, int MaxLookups, long ManifestMaxBytes);

/// <summary>
/// The one way the editor reaches a package registry. It asks nothing without consent (read from the config file on every call, so the first line of every request is the
/// check), keeps the answers of this process in a cache that never expires by itself, tells the caller whether an answer came from it, and can drop it per registry.
/// There is no vulnerability lookup and nothing here downloads a package.
/// </summary>
public sealed class RegistryClient(RegistrySettings settings) : IDisposable
{
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);
    private HttpClient? _http;
    private string? _proxy;
    private int _requests;

    /// <summary>How many requests went out to a registry (cached answers do not count).</summary>
    public int Requests => Volatile.Read(ref _requests);

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

    /// <summary>Drops the cached answers of one registry, so the next lookup asks it again.</summary>
    public void Refresh(string registry)
    {
        foreach (var key in _cache.Keys.Where(k => k.StartsWith(registry + " ", StringComparison.Ordinal))) _cache.TryRemove(key, out _);
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
        if (_cache.TryGetValue(key, out var hit)) return new RegistryAnswer(hit, true);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(settings.TimeoutMilliseconds);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Textida");
            if (adapter.Accept is { } accept) request.Headers.Accept.ParseAdd(accept);
            Interlocked.Increment(ref _requests);
            using var response = await Http(proxy).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new RegistryException($"{url.Host} answered HTTP {(int)response.StatusCode}.", null, (int)response.StatusCode);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
            {
                if (buffer.Length + read > settings.MaxResponseBytes) throw new RegistryException($"{url.Host} answered more than {settings.MaxResponseBytes} bytes.");
                buffer.Write(chunk, 0, read);
            }
            var body = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            _cache[key] = body;
            return new RegistryAnswer(body, false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new RegistryException($"{url.Host} did not answer in {settings.TimeoutMilliseconds} ms."); }
        catch (HttpRequestException e) { throw new RegistryException($"{url.Host}: {e.Message}", e); }
    }

    public void Dispose() => _http?.Dispose();
}
