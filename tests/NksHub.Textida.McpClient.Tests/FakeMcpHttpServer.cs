using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace NksHub.Textida.McpClient.Tests;

/// <summary>
/// A Streamable HTTP MCP server on the loopback (protocol 2025-11-25): bearer key, a session id from <c>initialize</c>, JSON answers, one list answered as an event stream (with a comment,
/// an empty priming event and a notification before the answer), a call that never ends, and 202 for notifications. What it saw is in <see cref="Log"/>.
/// </summary>
internal sealed class FakeMcpHttpServer : IDisposable
{
    private readonly HttpListener _listener = new();

    public readonly ConcurrentQueue<string> Log = new();
    public string Token = "good-token";
    public int StatusToSend { get; set; }
    public bool Resources = true;

    public string Url { get; }

    public FakeMcpHttpServer()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Url = $"http://127.0.0.1:{port}/mcp";
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _ = Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext c;
            try { c = await _listener.GetContextAsync(); }
            catch (Exception) { return; }
            _ = Task.Run(() => HandleAsync(c));
        }
    }

    private static string Result(JsonElement id, string result) => "{\"jsonrpc\":\"2.0\",\"id\":" + id.GetRawText() + ",\"result\":" + result + "}";

    private static void Write(HttpListenerResponse response, string type, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentType = type;
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes);
    }

    private static void Json(HttpListenerResponse response, string body) => Write(response, "application/json; charset=utf-8", body);

    private async Task HandleAsync(HttpListenerContext c)
    {
        var response = c.Response;
        try
        {
            var request = c.Request;
            if (request.Headers["Authorization"] != "Bearer " + Token)
            {
                Log.Enqueue("401");
                response.StatusCode = 401;
                return;
            }
            if (request.HttpMethod == "DELETE")
            {
                Log.Enqueue("delete:" + request.Headers["Mcp-Session-Id"]);
                return;
            }
            if (StatusToSend != 0)
            {
                response.StatusCode = StatusToSend;
                return;
            }
            var accept = request.Headers["Accept"] ?? "";
            if (!accept.Contains("application/json") || !accept.Contains("text/event-stream")) Log.Enqueue("bad-accept:" + accept);
            using var doc = JsonDocument.Parse(await new StreamReader(request.InputStream, Encoding.UTF8).ReadToEndAsync());
            var root = doc.RootElement;
            var method = root.GetProperty("method").GetString()!;
            Log.Enqueue($"{method}|session={request.Headers["Mcp-Session-Id"]}|version={request.Headers["MCP-Protocol-Version"]}");
            if (!root.TryGetProperty("id", out var id))
            {
                if (method == "notifications/cancelled") Log.Enqueue("cancelled:" + root.GetProperty("params").GetProperty("requestId").GetRawText());
                response.StatusCode = 202;
                return;
            }
            var p = root.TryGetProperty("params", out var pp) ? pp : default;
            switch (method)
            {
                case "initialize":
                    response.Headers["Mcp-Session-Id"] = "session-1";
                    Json(response, Result(id, "{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{\"tools\":{}" + (Resources ? ",\"resources\":{}" : "") + "},\"serverInfo\":{\"name\":\"fake-http\",\"version\":\"3\"},\"instructions\":\"over http\"}"));
                    break;
                case "tools/list":
                    Json(response, Result(id, "{\"tools\":[{\"name\":\"echo\",\"inputSchema\":{\"type\":\"object\"}},{\"name\":\"slow\",\"inputSchema\":{\"type\":\"object\"}}]}"));
                    break;
                case "resources/list":
                    var list = Result(id, "{\"resources\":[{\"uri\":\"test://notes\",\"name\":\"notes\"},{\"uri\":\"test://big\",\"name\":\"big\"}]}");
                    Write(response, "text/event-stream", ": keep-alive\r\n\r\nid: 1\r\ndata:\r\n\r\nevent: message\r\ndata: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/message\",\"params\":{\"level\":\"info\"}}\r\n\r\nid: 2\r\ndata: " + list + "\r\n\r\n");
                    break;
                case "resources/read":
                    Json(response, Result(id, "{\"contents\":[{\"uri\":\"test://notes\",\"text\":\"page\"}]}"));
                    break;
                case "tools/call":
                    switch (p.GetProperty("name").GetString())
                    {
                        case "echo":
                            Json(response, Result(id, "{\"content\":[{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize(p.GetProperty("arguments").GetProperty("text").GetString()) + "}]}"));
                            break;
                        case "huge":
                            Json(response, Result(id, "{\"content\":[{\"type\":\"text\",\"text\":\"" + new string('z', 2 << 20) + "\"}]}"));
                            break;
                        default:
                            Log.Enqueue("slow-started:" + id.GetRawText());
                            await Task.Delay(TimeSpan.FromSeconds(20));
                            break;
                    }
                    break;
            }
        }
        catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or IOException or InvalidOperationException) { /* the client went away */ }
        finally
        {
            try { response.Close(); }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException) { }
        }
    }

    public async Task<string> WaitForAsync(Func<string, bool> match, string what)
    {
        for (int i = 0; i < 500; i++)
        {
            if (Log.FirstOrDefault(match) is { } found) return found;
            await Task.Delay(20);
        }
        throw new Xunit.Sdk.XunitException("The server did not see: " + what + "\n" + string.Join('\n', Log));
    }

    public void Dispose() => _listener.Close();
}
