using System.Net;
using System.Net.Sockets;
using System.Text;
using NksHub.Textida.Core.Http;

namespace NksHub.Textida.Remote.Tests;

public class HttpSenderTests
{
    private const string Crlf = "\r\n";

    /// <summary>A server that answers one request with <paramref name="response"/> and returns what it received.</summary>
    private static (int Port, Task<string> Received) Serve(string response)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var received = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync();
            var stream = peer.GetStream();
            var buffer = new byte[8192];
            var all = new MemoryStream();
            while (true)
            {
                int read = await stream.ReadAsync(buffer);
                if (read == 0) break;
                all.Write(buffer, 0, read);
                string text = Encoding.UTF8.GetString(all.ToArray());
                int head = text.IndexOf(Crlf + Crlf, StringComparison.Ordinal);
                if (head < 0) continue;
                int length = text.Contains("Content-Length: ") ? int.Parse(text.Split("Content-Length: ")[1].Split(Crlf)[0]) : 0;
                if (all.Length - Encoding.UTF8.GetByteCount(text[..(head + 4)]) >= length) break; // the body is counted in bytes
            }
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
            listener.Stop();
            return Encoding.UTF8.GetString(all.ToArray());
        });
        return (((IPEndPoint)listener.LocalEndpoint).Port, received);
    }

    [Fact]
    public async Task A_request_goes_out_with_its_method_headers_and_body_and_the_answer_comes_back_as_text()
    {
        string body = "{\"ok\": true}";
        var (port, received) = Serve($"HTTP/1.1 201 Created{Crlf}Content-Type: application/json; charset=utf-8{Crlf}X-Id: 7{Crlf}Content-Length: {Encoding.UTF8.GetByteCount(body)}{Crlf}Connection: close{Crlf}{Crlf}{body}");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var request = new HttpRequest(null, "POST", $"http://127.0.0.1:{port}/items?x=1", [("Content-Type", "application/json"), ("X-Key", "abc")], "{\"name\": \"é\"}", 0, 0, 0);

        var answer = await HttpSender.SendAsync(client, request, 1 << 20, CancellationToken.None);

        string sent = await received;
        Assert.StartsWith("POST /items?x=1 HTTP/1.1", sent);
        Assert.Contains("X-Key: abc", sent);
        Assert.Contains("Content-Type: application/json", sent);
        Assert.EndsWith("{\"name\": \"é\"}", sent);
        Assert.Equal((201, "Created", "json"), (answer.Status, answer.Reason, answer.LanguageId));
        Assert.Equal(body, answer.Body);
        string text = answer.Format();
        Assert.StartsWith("HTTP/1.1 201 Created\n", text);
        Assert.Contains("X-Id: 7\n", text);
        Assert.EndsWith("\n\n" + body, text);
    }

    [Fact]
    public async Task A_body_over_the_limit_is_cut_and_said_so_and_only_http_addresses_are_sent()
    {
        var (port, _) = Serve($"HTTP/1.1 200 OK{Crlf}Content-Type: text/plain{Crlf}Content-Length: 10{Crlf}Connection: close{Crlf}{Crlf}0123456789");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var answer = await HttpSender.SendAsync(client, new HttpRequest(null, "GET", $"http://127.0.0.1:{port}/", [], "", 0, 0, 0), 4, CancellationToken.None);
        Assert.True(answer.Truncated);
        Assert.Equal("0123", answer.Body);

        await Assert.ThrowsAsync<UriFormatException>(() => HttpSender.SendAsync(client, new HttpRequest(null, "GET", "file:///etc/passwd", [], "", 0, 0, 0), 4, CancellationToken.None));
        await Assert.ThrowsAsync<UriFormatException>(() => HttpSender.SendAsync(client, new HttpRequest(null, "GET", "/relative", [], "", 0, 0, 0), 4, CancellationToken.None));
    }
}
