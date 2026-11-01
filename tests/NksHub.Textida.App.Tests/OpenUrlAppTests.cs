using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia.Headless.XUnit;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.2.1: an address is downloaded into a new tab as text, after the user allowed the host, and nothing of it is run.</summary>
[Collection("LocState")]
public class OpenUrlAppTests : IDisposable
{
    private const string Crlf = "\r\n";
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;

    public OpenUrlAppTests() => _app = TestApp.Prepare(_temp, _dialogs);

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        _temp.Dispose();
    }

    private static int Serve(params (string Status, string Type, byte[] Body)[] answers)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _ = Task.Run(async () =>
        {
            foreach (var (status, type, body) in answers)
            {
                using var peer = await listener.AcceptTcpClientAsync();
                var stream = peer.GetStream();
                await stream.ReadAsync(new byte[8192]);
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}{Crlf}Content-Type: {type}{Crlf}Content-Length: {body.Length}{Crlf}Connection: close{Crlf}{Crlf}"));
                await stream.WriteAsync(body);
            }
            listener.Stop();
        });
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [AvaloniaFact]
    public async Task Text_opens_in_a_new_tab_after_the_host_is_allowed_and_html_stays_source()
    {
        int port = Serve(("200 OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes("<script>alert(1)</script><p>čau</p>")), ("200 OK", "application/json", "{\"a\": 1}"u8.ToArray()));
        var w = _app.CreateWindow(null);
        int tabs = w.Tabs.Count();

        _dialogs.Answers.Enqueue(0); // allow the host
        await w.OpenUrlAsync($"http://127.0.0.1:{port}/page");

        Assert.Equal(tabs + 1, w.Tabs.Count());
        var tab = w.Active!;
        Assert.Null(tab.Path);
        Assert.Equal("<script>alert(1)</script><p>čau</p>", tab.Editor!.Document.Text);
        Assert.Equal("html", tab.Doc.Language.Id);
        Assert.Contains("127.0.0.1", Assert.Single(_dialogs.Asked).Message);

        await w.OpenUrlAsync($"http://127.0.0.1:{port}/data.json"); // the host is known now: no question, the extension decides the mode
        Assert.Single(_dialogs.Asked);
        Assert.Equal("json", w.Active!.Doc.Language.Id);
    }

    [AvaloniaFact]
    public async Task An_error_status_binary_content_and_a_refused_host_open_nothing_and_say_why()
    {
        int port = Serve(("404 Not Found", "text/plain", "gone"u8.ToArray()), ("200 OK", "application/octet-stream", [1, 2, 0, 3, 4]));
        var w = _app.CreateWindow(null);
        int tabs = w.Tabs.Count();

        _dialogs.Answers.Enqueue(1); // refuse the host
        await w.OpenUrlAsync($"http://127.0.0.1:{port}/a");
        Assert.Equal(tabs, w.Tabs.Count());

        _dialogs.Answers.Enqueue(0);
        await w.OpenUrlAsync($"http://127.0.0.1:{port}/a");
        Assert.Equal(tabs, w.Tabs.Count());
        Assert.Contains("404", w.StatusMessageText);

        await w.OpenUrlAsync($"http://127.0.0.1:{port}/b");
        Assert.Equal(tabs, w.Tabs.Count());
        Assert.Contains("application/octet-stream", w.StatusMessageText);

        await w.OpenUrlAsync("ftp://example.test/file");
        Assert.Equal(tabs, w.Tabs.Count());
    }
}
