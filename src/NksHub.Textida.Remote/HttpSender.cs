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

/// <summary>A page or file fetched with GET: its bytes (cut at the limit), the declared type and charset, and the address it came from after redirects.</summary>
public sealed record Download(int Status, byte[] Bytes, bool Truncated, string? ContentType, string? Charset, Uri FinalUri)
{
    public bool Success => Status is >= 200 and < 300;

    /// <summary>True when the first bytes hold a zero byte, which text does not (UTF-16 and UTF-32 with a byte order mark are text).</summary>
    public bool LooksBinary
    {
        get
        {
            var head = Bytes.AsSpan(0, Math.Min(Bytes.Length, 8192));
            if (head.Length >= 2 && ((head[0] == 0xFF && head[1] == 0xFE) || (head[0] == 0xFE && head[1] == 0xFF))) return false;
            return head.Contains((byte)0);
        }
    }

    /// <summary>The bytes as text: the charset of the answer when it names one, else UTF-8 (a byte order mark wins over both).</summary>
    public string Text
    {
        get
        {
            var encoding = Encoding.UTF8;
            try { if (Charset is { } name) encoding = Encoding.GetEncoding(name); }
            catch (ArgumentException) { /* an unknown charset: UTF-8 */ }
            using var reader = new StreamReader(new MemoryStream(Bytes), encoding, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
    }
}

/// <summary>Sends one request of a <c>.http</c> file with the <see cref="HttpClient"/> it is given (the client of the network policy), without scripts and without keeping cookies.</summary>
public static class HttpSender
{
    /// <summary>
    /// Fetches <paramref name="address"/> with GET and reads at most <paramref name="maxBytes"/> of the body. Nothing of the answer is run, parsed or saved. Throws as <see cref="HttpClient"/> does, and
    /// <see cref="UriFormatException"/> for an address that is not http or https.
    /// </summary>
    public static async Task<Download> DownloadAsync(HttpClient client, Uri address, int maxBytes, CancellationToken ct)
    {
        if (address.Scheme is not ("http" or "https")) throw new UriFormatException("Only http and https addresses are fetched.");
        using var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        bool truncated = false;
        for (int read; (read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0;)
        {
            if (buffer.Length + read > maxBytes) { buffer.Write(chunk, 0, (int)(maxBytes - buffer.Length)); truncated = true; break; }
            buffer.Write(chunk, 0, read);
        }
        return new Download((int)response.StatusCode, buffer.ToArray(), truncated, response.Content.Headers.ContentType?.MediaType, response.Content.Headers.ContentType?.CharSet?.Trim('"'),
            response.RequestMessage?.RequestUri ?? address);
    }

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
