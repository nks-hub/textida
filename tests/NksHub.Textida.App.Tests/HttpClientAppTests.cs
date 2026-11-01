using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Http;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.6.3: a request of a .http file is sent after the user allowed the host, with the values of the chosen environment, and the answer opens in a new tab.</summary>
[Collection("LocState")]
public class HttpClientAppTests : IDisposable
{
    private const string Crlf = "\r\n";
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;

    public HttpClientAppTests() => _app = TestApp.Prepare(_temp, _dialogs);

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        _temp.Dispose();
    }

    private static (int Port, List<string> Received) Serve(int requests)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var received = new List<string>();
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < requests; i++)
            {
                using var peer = await listener.AcceptTcpClientAsync();
                var stream = peer.GetStream();
                var buffer = new byte[8192];
                int read = await stream.ReadAsync(buffer);
                lock (received) received.Add(Encoding.UTF8.GetString(buffer, 0, read));
                string body = "{\"hello\": \"world\"}";
                await stream.WriteAsync(Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK{Crlf}Content-Type: application/json{Crlf}Content-Length: {body.Length}{Crlf}Connection: close{Crlf}{Crlf}{body}"));
            }
            listener.Stop();
        });
        return (((IPEndPoint)listener.LocalEndpoint).Port, received);
    }

    private static async Task Until(Func<bool> done)
    {
        for (var clock = System.Diagnostics.Stopwatch.StartNew(); !done() && clock.ElapsedMilliseconds < 10_000;)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task A_request_asks_for_the_host_once_uses_the_private_value_and_opens_the_answer_in_a_new_tab()
    {
        var (port, received) = Serve(2);
        File.WriteAllText(Path.Combine(_temp.Path, HttpEnvironments.SharedFileName), $"{{\"dev\": {{\"base\": \"http://127.0.0.1:{port}\"}}}}");
        File.WriteAllText(Path.Combine(_temp.Path, HttpEnvironments.PrivateFileName), "{\"dev\": {\"token\": \"s3cr3t-token\"}}");
        var w = _app.CreateWindow(null);
        var tab = w.OpenFile(_temp.File("api.http", "GET {{base}}/items\nAuthorization: Bearer {{token}}\n\n###\nGET {{base}}/other\n"), null);
        var surface = tab.Editor!;

        // without an environment the variables are missing: nothing is sent and nothing is asked
        surface.CaretOffset = 0;
        w.SendHttpRequest();
        await Task.Delay(100);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(_dialogs.Asked);
        Assert.Empty(received);

        var folder = Path.GetDirectoryName(tab.Path!)!;
        System.Text.Json.Nodes.JsonObject choice = new() { [folder] = "dev" };
        File.WriteAllText(Path.Combine(_app.ProfileDirectory, "http-environments.json"), choice.ToJsonString());

        _dialogs.Answers.Enqueue(0); // allow the host
        int tabs = w.Tabs.Count();
        w.SendHttpRequest();
        await Until(() => w.Tabs.Count() > tabs);
        var asked = Assert.Single(_dialogs.Asked);
        Assert.Contains("127.0.0.1", asked.Message);
        lock (received) { Assert.Contains("GET /items HTTP/1.1", received[0]); Assert.Contains("Authorization: Bearer s3cr3t-token", received[0]); }
        var answer = w.Active!;
        Assert.Contains("HTTP/1.1 200 OK", answer.Editor!.Document.Text);
        Assert.Contains("{\"hello\": \"world\"}", answer.Editor.Document.Text);
        Assert.Equal("json", answer.Doc.Language.Id);
        Assert.True(_app.Plugins.ToolNetwork.IsAllowed(MainWindowHttpTool, "127.0.0.1"));
        Assert.DoesNotContain("s3cr3t-token", File.ReadAllText(Path.Combine(_temp.Path, HttpEnvironments.SharedFileName))); // the secret stays in the private file

        // the second request to the same host is not asked about again
        w.SelectTab(tab);
        surface.CaretOffset = surface.Document.Text.IndexOf("GET {{base}}/other", StringComparison.Ordinal) + 3;
        w.SendHttpRequest();
        await Until(() => received.Count > 1);
        Assert.Single(_dialogs.Asked);
        lock (received) Assert.Contains("GET /other HTTP/1.1", received[1]);

        // revoking the network consents takes the host back
        _app.Plugins.ToolNetwork.RevokeAll();
        Assert.False(_app.Plugins.ToolNetwork.IsAllowed(MainWindowHttpTool, "127.0.0.1"));
    }

    private const string MainWindowHttpTool = "http-client";

    [AvaloniaFact]
    public async Task A_host_the_user_does_not_allow_gets_nothing()
    {
        var (port, received) = Serve(1);
        var w = _app.CreateWindow(null);
        var tab = w.OpenFile(_temp.File("deny.http", $"GET http://127.0.0.1:{port}/x\n"), null);
        tab.Editor!.CaretOffset = 0;
        _dialogs.Answers.Enqueue(1); // cancel

        w.SendHttpRequest();
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(_dialogs.Asked);
        Assert.Empty(received);
        Assert.False(_app.Plugins.ToolNetwork.IsAllowed(MainWindowHttpTool, "127.0.0.1"));
    }

    [AvaloniaFact]
    public void A_curl_command_becomes_a_request_in_a_new_tab_and_a_text_that_is_not_curl_changes_nothing()
    {
        var w = _app.CreateWindow(null);
        var tab = w.OpenFile(_temp.File("c.txt", "curl -X POST https://a.test/x -H 'A: b' -d 'q=1'\n"), null);
        tab.Editor!.SetSelections([Sel.At(0)]);

        w.CurlToHttp();
        Assert.StartsWith("POST https://a.test/x", w.Active!.Editor!.Document.Text);
        Assert.Contains("A: b", w.Active.Editor.Document.Text);
        Assert.EndsWith("q=1" + Environment.NewLine, w.Active.Editor.Document.Text);

        var other = w.OpenFile(_temp.File("n.txt", "not a command\n"), null);
        int tabs = w.Tabs.Count();
        w.CurlToHttp();
        Assert.Equal(tabs, w.Tabs.Count());
        Assert.Same(other, w.Active);
    }
}
