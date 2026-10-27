using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NksHub.Textida.Plugins.Wiki;

/// <summary>A page of the wiki with the revision it was read at (an opaque string; the editor only compares it).</summary>
public sealed record WikiPage(string Text, string Revision);

public sealed record WikiPageInfo(string Slug, string? Title);

/// <summary>The page is not at the expected revision (<see cref="CurrentRevision"/> null: it is gone).</summary>
public sealed class WikiConflictException(string? currentRevision) : Exception("The page is not at the expected revision.")
{
    public string? CurrentRevision { get; } = currentRevision;
}

/// <summary>The wiki did not accept the token (401 or 403).</summary>
public sealed class WikiAuthException(int status) : Exception($"The wiki refused the token (HTTP {status}).");

/// <summary>The wiki cannot be reached or timed out.</summary>
public sealed class WikiUnavailableException(string message) : Exception(message);

/// <summary>The wiki answered with a status the adapter does not expect.</summary>
public sealed class WikiRequestException(int status) : Exception($"The wiki answered HTTP {status}.")
{
    public int Status { get; } = status;
}

/// <summary>What the adapter needs from the wiki. The HTTP shape behind it is <see cref="AssumedWikiHttp"/>.</summary>
public interface IWikiApi
{
    Task<IReadOnlyList<WikiPageInfo>> ListAsync(string prefix, CancellationToken cancellation);

    /// <summary>The page, or null when it does not exist.</summary>
    Task<WikiPage?> ReadAsync(string slug, CancellationToken cancellation);

    /// <summary>Writes the page and returns its new revision. <paramref name="expectedRevision"/> null creates a page that must not exist yet.</summary>
    Task<string> WriteAsync(string slug, string text, string? expectedRevision, CancellationToken cancellation);
}

/// <summary>
/// PLACEHOLDER for the HTTP mapping of the NKS IS wiki. Nothing in the repository or the plan describes the endpoints, the auth header or the revision field of the wiki
/// (P6.4.4: the current state of the service is "not verified"), so this is an ASSUMED shape, used by the tests through a local fake server and to be replaced
/// once the owner of the wiki documents the real one:
/// <list type="bullet">
/// <item><c>GET {base}/pages?prefix=P</c> answers <c>{"pages":[{"slug","title"?}]}</c>.</item>
/// <item><c>GET {base}/pages/{slug}</c> answers <c>{"content","revision"}</c> (404: no such page); there is no separate stat call, a stat reads the page.</item>
/// <item><c>PUT {base}/pages/{slug}</c> with <c>{"content","expectedRevision"}</c> answers <c>{"revision"}</c>; 409 with <c>{"currentRevision"}</c> means another revision.</item>
/// <item>The token goes in <c>Authorization: Bearer</c>; <c>revision</c> may be a string or a number.</item>
/// </list>
/// Whether the wiki honours <c>expectedRevision</c> in one step is not known, which is why the plugin does not declare an atomic write.
/// </summary>
public sealed class AssumedWikiHttp(HttpClient http, Uri baseUrl, string token, TimeSpan timeout) : IWikiApi
{
    private HttpRequestMessage Request(HttpMethod method, string path, byte[]? body = null)
    {
        var request = new HttpRequestMessage(method, new Uri(baseUrl.AbsoluteUri.TrimEnd('/') + path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
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
        if ((int)status is < 200 or >= 300) throw new WikiRequestException((int)status);
    }

    private static string? RevisionOf(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var r)
            ? r.ValueKind switch { JsonValueKind.String => r.GetString(), JsonValueKind.Number => r.GetRawText(), _ => null }
            : null;

    public async Task<IReadOnlyList<WikiPageInfo>> ListAsync(string prefix, CancellationToken cancellation)
    {
        var (status, body) = await SendAsync(Request(HttpMethod.Get, "/pages?prefix=" + Uri.EscapeDataString(prefix)), cancellation);
        using (body)
        {
            Check(status);
            if (body is null || !body.RootElement.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array) throw new WikiRequestException((int)status);
            List<WikiPageInfo> list = [];
            foreach (var page in pages.EnumerateArray())
            {
                if (page.ValueKind != JsonValueKind.Object || !page.TryGetProperty("slug", out var slug) || slug.ValueKind != JsonValueKind.String) continue;
                list.Add(new WikiPageInfo(slug.GetString()!, page.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null));
            }
            return list;
        }
    }

    public async Task<WikiPage?> ReadAsync(string slug, CancellationToken cancellation)
    {
        var (status, body) = await SendAsync(Request(HttpMethod.Get, "/pages/" + Escape(slug)), cancellation);
        using (body)
        {
            if (status == HttpStatusCode.NotFound) return null;
            Check(status);
            var root = body?.RootElement ?? default;
            if (RevisionOf(root, "revision") is not { Length: > 0 } revision || !root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
                throw new WikiRequestException((int)status);
            return new WikiPage(content.GetString()!, revision);
        }
    }

    public async Task<string> WriteAsync(string slug, string text, string? expectedRevision, CancellationToken cancellation)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("content", text);
            if (expectedRevision is null) w.WriteNull("expectedRevision"); else w.WriteString("expectedRevision", expectedRevision);
            w.WriteEndObject();
        }
        var (status, body) = await SendAsync(Request(HttpMethod.Put, "/pages/" + Escape(slug), stream.ToArray()), cancellation);
        using (body)
        {
            if (status == HttpStatusCode.Conflict) throw new WikiConflictException(RevisionOf(body?.RootElement ?? default, "currentRevision"));
            Check(status);
            return RevisionOf(body?.RootElement ?? default, "revision") is { Length: > 0 } revision ? revision : throw new WikiRequestException((int)status);
        }
    }

    private static string Escape(string slug) => string.Join('/', slug.Split('/').Select(Uri.EscapeDataString));
}
