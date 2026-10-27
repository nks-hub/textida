using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace NksHub.Textida.Plugins.Wiki;

/// <summary>A page of the wiki with its revision token (opaque to the editor, which only compares it).</summary>
public sealed record WikiPage(string Text, string Revision);

public sealed record WikiPageInfo(string Slug, string? Title, bool IsFolder);

/// <summary>The wiki did not accept the API key (401 or 403).</summary>
public sealed class WikiAuthException(int status) : Exception($"The wiki refused the API key (HTTP {status}).");

/// <summary>The wiki answered 429: more requests per minute than its limit for the key.</summary>
public sealed class WikiRateLimitException() : Exception("The wiki rate limit was reached.");

/// <summary>The wiki cannot be reached or timed out.</summary>
public sealed class WikiUnavailableException(string message) : Exception(message);

/// <summary>The wiki answered with a status the adapter does not expect.</summary>
public sealed class WikiRequestException(int status) : Exception($"The wiki answered HTTP {status}.")
{
    public int Status { get; } = status;
}

/// <summary>What the adapter needs from the wiki. The HTTP mapping behind it is <see cref="NksIsWikiHttp"/>.</summary>
public interface IWikiApi
{
    /// <summary>The pages whose slug starts with <paramref name="prefix"/>, at most <paramref name="max"/>.</summary>
    Task<IReadOnlyList<WikiPageInfo>> ListAsync(string prefix, int max, CancellationToken cancellation);

    /// <summary>The page, or null when it does not exist.</summary>
    Task<WikiPage?> ReadAsync(string slug, CancellationToken cancellation);

    /// <summary>Writes the content of the page and returns its new revision token. The wiki cannot check a revision while writing, so the caller compares the token first.</summary>
    Task<string> WriteAsync(string slug, string text, CancellationToken cancellation);
}

/// <summary>
/// The HTTP mapping of the NKS IS wiki (OpenAPI 3.0 of the service), the only place that knows it. Base path <c>{base}/api/v1</c>, key in <c>X-Api-Key</c>
/// (the Bearer scheme of the spec belongs to its MCP endpoint only). <c>GET /wiki?page&amp;limit&amp;search</c> lists (<c>{items,pages,...}</c>), <c>GET /wiki/{slug}</c> reads
/// (page fields and <c>content</c>; 404: no page), <c>PUT /wiki/{slug}</c> updates with <c>{content}</c>. The spec has no expected-revision field and no 409, so a write is never atomic.
/// The revision token is <c>version|revisionCount|updatedAt</c>: any of them changes when someone saves. 401 or 403: the key is missing or invalid; 429: 60 requests per minute per key.
/// </summary>
public sealed class NksIsWikiHttp(HttpClient http, Uri baseUrl, string key, TimeSpan timeout) : IWikiApi
{
    private HttpRequestMessage Request(HttpMethod method, string path, byte[]? body = null)
    {
        var request = new HttpRequestMessage(method, new Uri(baseUrl.AbsoluteUri.TrimEnd('/') + "/api/v1" + path));
        request.Headers.Add("X-Api-Key", key);
        request.Headers.Accept.ParseAdd("application/json");
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }
        return request;
    }

    private async Task<(HttpStatusCode Status, JsonDocument? Body)> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(timeout);
        try
        {
            using var response = await http.SendAsync(request, limit.Token).ConfigureAwait(false);
            var bytes = await response.Content.ReadAsByteArrayAsync(limit.Token).ConfigureAwait(false);
            JsonDocument? body = null;
            if (bytes.Length > 0)
            {
                try { body = JsonDocument.Parse(bytes); }
                catch (JsonException) { /* not JSON: only the status counts */ }
            }
            return (response.StatusCode, body);
        }
        catch (HttpRequestException e) { throw new WikiUnavailableException(e.Message); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new WikiUnavailableException("The wiki did not answer in time."); }
    }

    private static void Check(HttpStatusCode status)
    {
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new WikiAuthException((int)status);
        if (status == HttpStatusCode.TooManyRequests) throw new WikiRateLimitException();
        if ((int)status is < 200 or >= 300) throw new WikiRequestException((int)status);
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null }
            : null;

    private static WikiPage? PageOf(JsonElement e) =>
        Str(e, "version") is { } version && Str(e, "revisionCount") is { } count && Str(e, "updatedAt") is { } updated && Str(e, "content") is { } content
            ? new WikiPage(content, $"{version}|{count}|{updated}")
            : null;

    public async Task<IReadOnlyList<WikiPageInfo>> ListAsync(string prefix, int max, CancellationToken cancellation)
    {
        List<WikiPageInfo> list = [];
        for (int page = 1; list.Count < max; page++)
        {
            var query = $"?page={page}&limit={max}" + (prefix.Length > 0 ? "&search=" + Uri.EscapeDataString(prefix) : "");
            var (status, body) = await SendAsync(Request(HttpMethod.Get, "/wiki" + query), cancellation);
            using (body)
            {
                Check(status);
                if (body is null || !body.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) throw new WikiRequestException((int)status);
                foreach (var item in items.EnumerateArray())
                {
                    if (Str(item, "slug") is not { } slug || !slug.StartsWith(prefix, StringComparison.Ordinal)) continue; // search is a text search, the prefix is a path
                    list.Add(new WikiPageInfo(slug, Str(item, "title"), item.TryGetProperty("isFolder", out var f) && f.ValueKind == JsonValueKind.True));
                }
                if (!int.TryParse(Str(body.RootElement, "pages"), out int pages) || page >= pages) break;
            }
        }
        return list.Take(max).ToList();
    }

    public async Task<WikiPage?> ReadAsync(string slug, CancellationToken cancellation)
    {
        var (status, body) = await SendAsync(Request(HttpMethod.Get, "/wiki/" + Escape(slug)), cancellation);
        using (body)
        {
            if (status == HttpStatusCode.NotFound) return null;
            Check(status);
            return PageOf(body?.RootElement ?? default) ?? throw new WikiRequestException((int)status);
        }
    }

    public async Task<string> WriteAsync(string slug, string text, CancellationToken cancellation)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("content", text);
            w.WriteEndObject();
        }
        var (status, body) = await SendAsync(Request(HttpMethod.Put, "/wiki/" + Escape(slug), stream.ToArray()), cancellation);
        body?.Dispose();
        if (status == HttpStatusCode.NotFound) throw new WikiRequestException((int)status);
        Check(status);
        // the answer of an update is not relied on: the new token is read back
        return (await ReadAsync(slug, cancellation))?.Revision ?? throw new WikiRequestException((int)HttpStatusCode.NotFound);
    }

    private static string Escape(string slug) => string.Join('/', slug.Split('/').Select(Uri.EscapeDataString));
}
