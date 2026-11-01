using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using NksHub.Textida.Core.Http;

namespace NksHub.Textida.Remote;

/// <summary>The answer to a request: status, headers and the body as text (cut at the limit).</summary>
public sealed record HttpAnswer(int Status, string Reason, string Version, IReadOnlyList<(string Name, string Value)> Headers, string Body, bool Truncated, string? ContentType, TimeSpan Elapsed)
{
    /// <summary>The answer as the text of a tab: the status line, the headers, a blank line and the body.</summary>
    public string Format(string newLine = "\n")
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/").Append(Version).Append(' ').Append(Status).Append(' ').Append(Reason).Append(newLine);
        foreach (var (name, value) in Headers) sb.Append(name).Append(": ").Append(value).Append(newLine);
        sb.Append(newLine).Append(Body);
        return sb.ToString();
    }

    /// <summary>The id of the language mode the body is best shown in: json, xml, html, or plain text.</summary>
    public string LanguageId => ContentType switch
    {
        { } t when t.Contains("json", StringComparison.OrdinalIgnoreCase) => "json",
        { } t when t.Contains("xml", StringComparison.OrdinalIgnoreCase) => "xml",
        { } t when t.Contains("html", StringComparison.OrdinalIgnoreCase) => "html",
        _ => "plaintext",
    };
}

/// <summary>Sends one request of a <c>.http</c> file with the <see cref="HttpClient"/> it is given (the client of the network policy), without scripts and without keeping cookies.</summary>
public static class HttpSender
{
    /// <summary>Sends <paramref name="request"/> (variables already replaced). Throws <see cref="HttpRequestException"/> or <see cref="OperationCanceledException"/> as <see cref="HttpClient"/> does, and <see cref="UriFormatException"/> for a URL that is not absolute.</summary>
    public static async Task<HttpAnswer> SendAsync(HttpClient client, HttpRequest request, int maxBodyBytes, CancellationToken ct)
    {
        var uri = new Uri(request.Url, UriKind.Absolute);
        if (uri.Scheme is not ("http" or "https")) throw new UriFormatException("Only http and https addresses are sent.");
        using var message = new HttpRequestMessage(new HttpMethod(request.Method), uri);
        if (request.Body.Length > 0) message.Content = new StringContent(request.Body, new UTF8Encoding(false));
        foreach (var (name, value) in request.Headers)
        {
            if (message.Content is not null && name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
            {
                message.Content.Headers.Remove(name);
                message.Content.Headers.TryAddWithoutValidation(name, value);
            }
            else message.Headers.TryAddWithoutValidation(name, value);
        }
        var clock = Stopwatch.StartNew();
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        var headers = response.Headers.Concat(response.Content.Headers).SelectMany(h => h.Value.Select(v => (h.Key, v))).ToList();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        bool truncated = false;
        for (int read; (read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0;)
        {
            if (buffer.Length + read > maxBodyBytes) { buffer.Write(chunk, 0, (int)(maxBodyBytes - buffer.Length)); truncated = true; break; }
            buffer.Write(chunk, 0, read);
        }
        string? contentType = response.Content.Headers.ContentType?.MediaType;
        var encoding = Encoding.UTF8;
        try { if (response.Content.Headers.ContentType?.CharSet is { } charset) encoding = Encoding.GetEncoding(charset); }
        catch (ArgumentException) { /* an unknown charset: UTF-8 */ }
        return new HttpAnswer((int)response.StatusCode, response.ReasonPhrase ?? "", response.Version.ToString(), headers, encoding.GetString(buffer.ToArray()), truncated, contentType, clock.Elapsed);
    }
}
