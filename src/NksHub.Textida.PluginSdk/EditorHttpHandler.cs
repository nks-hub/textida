using System.Text.Json;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginSdk;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that hands every request to the editor (<c>net/request</c>), so an <see cref="HttpClient"/> built on it goes through the network policy
/// and proxy the user set in the editor. The plugin needs the <c>network</c> permission. Redirects come back as they are; an error of the editor (no permission, no connection,
/// timeout, an answer over its limit) is an <see cref="HttpRequestException"/> with the message of the editor.
/// </summary>
public sealed class EditorHttpHandler(JsonRpcDispatcher rpc) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("method", request.Method.Method);
            w.WriteString("url", request.RequestUri!.AbsoluteUri);
            w.WriteStartObject("headers");
            foreach (var (name, values) in request.Headers) w.WriteString(name, string.Join(", ", values));
            if (request.Content is not null) foreach (var (name, values) in request.Content.Headers) w.WriteString(name, string.Join(", ", values));
            w.WriteEndObject();
            if (body is not null) w.WriteBase64String("body", body);
            w.WriteEndObject();
        }
        JsonElement answer;
        try { answer = await rpc.RequestAsync(PluginNetwork.Request, stream.ToArray(), null, cancellationToken).ConfigureAwait(false); }
        catch (RpcException e) { throw new HttpRequestException(e.Message, e); }

        var response = new HttpResponseMessage((System.Net.HttpStatusCode)answer.GetProperty("status").GetInt32())
        {
            RequestMessage = request,
            Content = new ByteArrayContent(answer.GetProperty("body").GetBytesFromBase64()),
        };
        if (answer.TryGetProperty("contentType", out var type) && type.GetString() is { } t) response.Content.Headers.TryAddWithoutValidation("Content-Type", t);
        if (answer.TryGetProperty("location", out var location) && location.GetString() is { } l) response.Headers.TryAddWithoutValidation("Location", l);
        return response;
    }
}
