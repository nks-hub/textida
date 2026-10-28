using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace NksHub.Textida.McpClient.Tests;

/// <summary>
/// A server of the older HTTP+SSE transport (MCP 2024-11-05) on the loopback, written from the public specification: <c>GET /sse</c> opens an event stream whose first event, <c>endpoint</c>,
/// names the address for messages; the client POSTs there, gets 202 and finds the answer as a <c>message</c> event on the stream. A POST to the stream address itself is refused with 405, as
/// a server that does not know Streamable HTTP does. The stream carries comments, ids, CRLF and an unknown event; the list of tools arrives split in the middle of a line. What the server saw is in <see cref="Log"/>.
/// </summary>
internal sealed class FakeMcpSseServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly object _gate = new();
    private HttpListenerResponse? _stream;

    public readonly ConcurrentQueue<string> Log = new();
    public string Token = "good-token";
    /// <summary>What the <c>endpoint</c> event says; a relative address by default.</summary>
    public string Endpoint = "/messages?session=s1";
    public int Port { get; }
    public string Url { get; }

    public FakeMcpSseServer()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        Port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Url = $"http://127.0.0.1:{Port}/sse";
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
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

    private async Task HandleAsync(HttpListenerContext c)
    {
        var response = c.Response;
        bool keepOpen = false;
        try
        {
            var request = c.Request;
            if (request.Headers["Authorization"] != "Bearer " + Token)
            {
                Log.Enqueue("401");
                response.StatusCode = 401;
                return;
            }
            if (request.HttpMethod == "GET" && request.Url!.AbsolutePath == "/sse")
            {
                Log.Enqueue($"get:{request.Headers["Accept"]}|session={request.Headers["Mcp-Session-Id"]}|version={request.Headers["MCP-Protocol-Version"]}");
                response.ContentType = "text/event-stream";
                response.SendChunked = true;
                response.Headers["Cache-Control"] = "no-cache";
                lock (_gate) _stream = response;
                keepOpen = true;
                Write(": welcome\r\n\r\nevent: endpoint\r\nid: 0\r\ndata: " + Endpoint + "\r\n\r\nevent: ping\r\ndata: not json\r\n\r\n");
                await KeepAliveAsync(response);
                return;
            }
            if (request.HttpMethod == "POST" && request.Url!.AbsolutePath == "/sse")
            {
                using var probe = JsonDocument.Parse(await new StreamReader(request.InputStream, Encoding.UTF8).ReadToEndAsync());
                Log.Enqueue($"probe:{probe.RootElement.GetProperty("method").GetString()}:405");
                response.StatusCode = 405;
                return;
            }
            if (request.HttpMethod != "POST" || request.Url!.AbsolutePath != "/messages")
            {
                response.StatusCode = 404;
                return;
            }
            using var doc = JsonDocument.Parse(await new StreamReader(request.InputStream, Encoding.UTF8).ReadToEndAsync());
            var root = doc.RootElement;
            var method = root.GetProperty("method").GetString()!;
            Log.Enqueue($"post:{method}:{request.Url.PathAndQuery}|auth={request.Headers["Authorization"]}|session={request.Headers["Mcp-Session-Id"]}|version={request.Headers["MCP-Protocol-Version"]}");
            response.StatusCode = 202;
            response.ContentType = "text/plain";
            response.OutputStream.Write("Accepted"u8);
            if (!root.TryGetProperty("id", out var id))
            {
                if (method == "notifications/cancelled") Log.Enqueue("cancelled:" + root.GetProperty("params").GetProperty("requestId").GetRawText());
                return;
            }
            var p = root.TryGetProperty("params", out var pp) ? pp : default;
            switch (method)
            {
                case "initialize":
                    Log.Enqueue("protocol:" + p.GetProperty("protocolVersion").GetString());
                    Message(Result(id, "{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{\"tools\":{}" + ",\"resources\":{}" + "},\"serverInfo\":{\"name\":\"fake-sse\",\"version\":\"1\"}}"));
                    break;
                case "tools/list":
                    var tools = Result(id, "{\"tools\":[{\"name\":\"echo\",\"inputSchema\":{\"type\":\"object\"}},{\"name\":\"slow\",\"inputSchema\":{\"type\":\"object\"}},{\"name\":\"close\",\"inputSchema\":{\"type\":\"object\"}}]}");
                    Write("event: message\r\ndata: " + tools[..20]);
                    await Task.Delay(40);
                    Write(tools[20..] + "\r\n\r\n");
                    break;
                case "resources/list":
                    Message(Result(id, "{\"resources\":[{\"uri\":\"test://notes\",\"name\":\"notes\"}]}"));
                    break;
                case "resources/read":
                    Message(Result(id, "{\"contents\":[{\"uri\":\"test://notes\",\"text\":\"page\"}]}"));
                    break;
                case "tools/call":
                    switch (p.GetProperty("name").GetString())
                    {
                        case "echo":
                            Message(Result(id, "{\"content\":[{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize(p.GetProperty("arguments").GetProperty("text").GetString()) + "}]}"));
                            break;
                        case "close":
                            Log.Enqueue("closing-stream");
                            CloseStream();
                            break;
                        default:
                            Log.Enqueue("slow-started:" + id.GetRawText());
                            break;
                    }
                    break;
            }
        }
        catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or IOException or InvalidOperationException) { /* the client went away */ }
        finally
        {
            if (!keepOpen)
            {
                try { response.Close(); }
                catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException) { }
            }
        }
    }

    /// <summary>A comment every 50 ms keeps the stream busy so that a client that went away is noticed by a failing write.</summary>
    private async Task KeepAliveAsync(HttpListenerResponse stream)
    {
        Log.Enqueue("sse-open");
        while (true)
        {
            await Task.Delay(50);
            lock (_gate)
            {
                if (!ReferenceEquals(_stream, stream)) return; // closed on purpose
                try
                {
                    var bytes = ": keep-alive\r\n\r\n"u8.ToArray();
                    stream.OutputStream.Write(bytes);
                    stream.OutputStream.Flush();
                }
                catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or IOException or InvalidOperationException)
                {
                    Log.Enqueue("sse-closed");
                    return;
                }
            }
        }
    }

    private void Write(string text)
    {
        lock (_gate)
        {
            if (_stream is null) return;
            _stream.OutputStream.Write(Encoding.UTF8.GetBytes(text));
            _stream.OutputStream.Flush();
        }
    }

    private void Message(string json) => Write("event: message\r\ndata: " + json + "\r\n\r\n");

    private void CloseStream()
    {
        lock (_gate)
        {
            _stream?.Close();
            _stream = null;
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
