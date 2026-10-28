using System.Text.Json;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Permissions;

/// <param name="Timeout">Longest time one request may take, answer included.</param>
/// <param name="MaxResponseBytes">Largest body of an answer; a larger one is an error. It travels base64 encoded, so it must stay well under the message limit of the connection.</param>
public sealed record PluginNetworkOptions(TimeSpan Timeout, int MaxResponseBytes);

/// <summary>
/// The network API of the plugin protocol: <c>net/request</c> sends one HTTP request for a plugin that holds <see cref="PluginPermissionNames.Network"/>. The editor makes it
/// with the handler of its network policy, read for every request, so the proxy the user set applies (its password stays in the editor) and a policy that cannot be read
/// stops the request instead of going around it. Redirects are not followed, because a header such as an API key would go on to the other host; the plugin gets the 3xx with
/// its <c>location</c>. The call log keeps the method only, never the address, a header or a body.
/// </summary>
/// <param name="handler">Builds the handler of the network policy; it throws when the policy cannot be used.</param>
public sealed class PluginNetwork(PluginPermissions permissions, Func<HttpClientHandler> handler, PluginNetworkOptions options, PluginCallLog log)
{
    public const string Request = PluginServiceMethods.NetRequest;

    private static readonly HashSet<string> Methods = ["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE"];

    /// <summary>
    /// Parameters: <c>url</c> (absolute http or https, no user name), optional <c>method</c> (GET by default), <c>headers</c> (object of strings) and <c>body</c> (base64).
    /// Result: <c>status</c>, <c>body</c> (base64), and <c>contentType</c> and <c>location</c> when the answer has them.
    /// </summary>
    public ValueTask<byte[]?> HandleAsync(string pluginId, string method, JsonElement p, CancellationToken cancellation) =>
        log.RunAsync(pluginId, method, null, async () =>
        {
            if (method != Request) throw new RpcException(RpcErrorCodes.MethodNotFound, $"The editor has no method '{method}'.");
            using var lease = permissions.Enter(pluginId, PluginPermissionNames.Network, GrantScope.All, cancellation);
            using var request = Build(p);
            HttpClientHandler h;
            try { h = handler(); }
            catch (Exception e) { throw Unavailable("The network policy of the editor cannot be used: " + e.Message); }
            h.AllowAutoRedirect = false;
            using var http = new HttpClient(h, disposeHandler: true) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(lease.Token);
            limit.CancelAfter(options.Timeout);
            try
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
                var body = await ReadAsync(response, request.RequestUri!.Host, limit.Token).ConfigureAwait(false);
                return PluginSecrets.Reply(w =>
                {
                    w.WriteNumber("status", (int)response.StatusCode);
                    if (response.Content.Headers.ContentType is { } type) w.WriteString("contentType", type.ToString());
                    if (response.Headers.Location is { } location) w.WriteString("location", location.OriginalString);
                    w.WriteBase64String("body", body);
                });
            }
            catch (OperationCanceledException) when (lease.Revoked) { throw lease.RevokedError(); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw Unavailable($"{request.RequestUri!.Host} did not answer in {(int)options.Timeout.TotalMilliseconds} ms."); }
            catch (HttpRequestException e) { throw Unavailable(e.Message); }
        });

    private static RpcException Unavailable(string message) => new(DocumentErrorCodes.NetworkUnavailable, message);

    private static RpcException Invalid(string message) => new(RpcErrorCodes.InvalidParams, message);

    private static HttpRequestMessage Build(JsonElement p)
    {
        var url = PluginSecrets.NeedString(p, "url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)
            throw Invalid("Parameter 'url' must be an absolute http or https address without a user name.");
        var verb = p.TryGetProperty("method", out var m) ? m.ValueKind == JsonValueKind.String ? m.GetString()! : "" : "GET";
        if (!Methods.Contains(verb)) throw Invalid("Parameter 'method' must be one of " + string.Join(", ", Methods) + ".");
        var request = new HttpRequestMessage(new HttpMethod(verb), uri);
        try
        {
            if (p.TryGetProperty("body", out var b))
            {
                if (b.ValueKind != JsonValueKind.String || !b.TryGetBytesFromBase64(out var bytes)) throw Invalid("Parameter 'body' must be base64.");
                request.Content = new ByteArrayContent(bytes);
            }
            if (p.TryGetProperty("headers", out var headers))
            {
                if (headers.ValueKind != JsonValueKind.Object) throw Invalid("Parameter 'headers' must be an object.");
                foreach (var header in headers.EnumerateObject())
                {
                    if (header.Value.ValueKind != JsonValueKind.String) throw Invalid($"Header '{header.Name}' must be a string.");
                    var value = header.Value.GetString()!;
                    if (!request.Headers.TryAddWithoutValidation(header.Name, value) && request.Content?.Headers.TryAddWithoutValidation(header.Name, value) != true)
                        throw Invalid($"Header '{header.Name}' cannot be set.");
                }
            }
            return request;
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private async Task<byte[]> ReadAsync(HttpResponseMessage response, string host, CancellationToken cancellation)
    {
        if (response.Content.Headers.ContentLength > options.MaxResponseBytes) throw Unavailable($"{host} answered more than {options.MaxResponseBytes} bytes.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellation).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > options.MaxResponseBytes) throw Unavailable($"{host} answered more than {options.MaxResponseBytes} bytes.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
