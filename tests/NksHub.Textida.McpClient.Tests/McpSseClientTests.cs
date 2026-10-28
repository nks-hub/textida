using NksHub.Textida.Rpc;

namespace NksHub.Textida.McpClient.Tests;

/// <summary>P6.4.3 for a server of the older HTTP+SSE transport (MCP 2024-11-05): against a local server written from the public specification, never the network.</summary>
public sealed class McpSseClientTests
{
    private static readonly McpClientOptions Options = new(
        new RpcLimits(MaxMessageBytes: 1 << 20, MaxConcurrentIncoming: 4, MaxPendingOutgoing: 8, MaxResultItems: 10_000, MaxSessionBytes: 8L << 20, MaxOutgoingQueueBytes: 8L << 20),
        HandshakeTimeout: TimeSpan.FromSeconds(20), ExitGrace: TimeSpan.FromSeconds(5), StderrTailLines: 20, ClientName: "textida-test", ClientVersion: "0.0.1",
        CallTimeout: TimeSpan.FromSeconds(20), MaxResultChars: 1000);

    private static HttpClient Http() => new(new HttpClientHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };

    private static McpRemote Remote(string url, bool legacy, string token = "good-token") =>
        new(new Uri(url), new Dictionary<string, string> { ["Authorization"] = "Bearer " + token }, [token, "Bearer " + token], legacy);

    private static Task<McpClient> StartAsync(FakeMcpSseServer server, bool legacy = true, McpClientOptions? options = null, string token = "good-token") =>
        McpClient.StartRemoteAsync(Remote(server.Url, legacy, token), options ?? Options, Http());

    [Fact]
    public async Task A_server_of_the_old_transport_is_connected_listed_called_and_its_stream_ends_with_the_client()
    {
        using var server = new FakeMcpSseServer();
        var client = await StartAsync(server);

        Assert.Equal((McpClient.SseVersion, "fake-sse", "1"), (client.ProtocolVersion, client.ServerName, client.ServerVersion));
        // the list arrives split in the middle of a line, after a comment, an id and an event of a kind the client does not know
        Assert.Equal(["echo", "slow", "close"], (await client.ListToolsAsync()).Select(t => t.Name));
        Assert.Equal(["test://notes"], (await client.ListResourcesAsync()).Select(r => r.Uri));
        Assert.Equal("page", (await client.ReadResourceAsync("test://notes")).Text);
        Assert.Equal("héllo", (await client.CallToolAsync("echo", """{"text":"héllo"}""")).Text);

        // one GET for the stream; the messages go to the address the stream named, with the key, and without a session or a version header; the notification went out before the next request
        var log = server.Log.ToArray();
        Assert.Equal("get:text/event-stream|session=|version=", log[0]);
        Assert.Equal(["protocol:2024-11-05"], log.Where(l => l.StartsWith("protocol:")));
        var posts = log.Where(l => l.StartsWith("post:")).ToArray();
        Assert.StartsWith("post:initialize:/messages?session=s1|auth=Bearer good-token|session=|version=", posts[0]);
        Assert.Equal(["initialize", "notifications/initialized", "tools/list"], posts.Take(3).Select(p => p.Split(':')[1]));
        Assert.All(posts, p => Assert.EndsWith("|session=|version=", p));
        Assert.All(posts, p => Assert.Contains(":/messages?session=s1|", p));
        Assert.DoesNotContain(log, l => l.StartsWith("probe:"));

        await client.DisposeAsync();
        await server.WaitForAsync(l => l == "sse-closed", "the stream to be closed with the client");
    }

    [Fact]
    public async Task An_address_without_a_type_falls_back_to_the_old_transport_when_the_server_refuses_the_first_post()
    {
        using var server = new FakeMcpSseServer();
        await using var client = await StartAsync(server, legacy: false);

        Assert.Equal(McpClient.SseVersion, client.ProtocolVersion);
        Assert.Equal(["echo", "slow", "close"], (await client.ListToolsAsync()).Select(t => t.Name));
        Assert.Equal("again", (await client.CallToolAsync("echo", """{"text":"again"}""")).Text);
        // the probe came first, then the stream: both with the key, and the same HttpClient served the two attempts
        var log = server.Log.ToArray();
        Assert.Equal("probe:initialize:405", log[0]);
        Assert.Equal("get:text/event-stream|session=|version=", log[1]);
        Assert.Contains("protocol:2024-11-05", log);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_key_the_server_refuses_is_reported_as_such_and_is_not_tried_again_on_the_old_transport(bool legacy)
    {
        using var server = new FakeMcpSseServer { Token = "another-token" };

        var e = await Assert.ThrowsAsync<McpAuthException>(() => StartAsync(server, legacy));

        Assert.Contains("refused the key (HTTP 401)", e.Message);
        Assert.DoesNotContain("good-token", e.Message);
        Assert.Equal(["401"], server.Log.ToArray()); // one refusal, whichever attempt was the first: a refused key is no reason to try the other transport
    }

    [Fact]
    public async Task An_address_for_messages_on_another_host_is_refused_and_no_key_is_sent_there()
    {
        using var server = new FakeMcpSseServer();
        server.Endpoint = $"http://localhost:{server.Port}/messages"; // the same server, but not the host of the stream

        var e = await Assert.ThrowsAsync<McpClientException>(() => StartAsync(server));

        Assert.Contains("not on its own host", e.Message);
        Assert.DoesNotContain(server.Log, l => l.StartsWith("post:"));
    }

    [Fact]
    public async Task An_absolute_address_on_the_host_of_the_stream_is_used()
    {
        using var server = new FakeMcpSseServer();
        server.Endpoint = $"http://127.0.0.1:{server.Port}/messages?session=abs";
        await using var client = await StartAsync(server);

        Assert.Equal("ok", (await client.CallToolAsync("echo", """{"text":"ok"}""")).Text);
        Assert.Contains(server.Log, l => l.StartsWith("post:tools/call:/messages?session=abs|"));
    }

    [Fact]
    public async Task Cancelling_a_call_over_the_old_transport_tells_the_server_and_the_client_goes_on()
    {
        using var server = new FakeMcpSseServer();
        await using var client = await StartAsync(server);
        using var cts = new CancellationTokenSource();
        var call = client.CallToolAsync("slow", ct: cts.Token);
        var started = await server.WaitForAsync(l => l.StartsWith("slow-started:"), "the call to arrive");

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        await server.WaitForAsync(l => l == "cancelled:" + started["slow-started:".Length..], "notifications/cancelled for that request");
        Assert.Equal("again", (await client.CallToolAsync("echo", """{"text":"again"}""")).Text);
    }

    [Fact]
    public async Task A_call_over_the_old_transport_that_takes_too_long_ends_with_a_timeout_and_tells_the_server()
    {
        using var server = new FakeMcpSseServer();
        await using var client = await StartAsync(server, options: Options with { CallTimeout = TimeSpan.FromMilliseconds(400) });

        var e = await Assert.ThrowsAsync<McpTimeoutException>(() => client.CallToolAsync("slow"));

        Assert.Contains("did not answer within 0.4 s", e.Message);
        await server.WaitForAsync(l => l.StartsWith("cancelled:"), "notifications/cancelled for the call that timed out");
        Assert.Equal("fine", (await client.CallToolAsync("echo", """{"text":"fine"}""")).Text);
    }

    [Fact]
    public async Task A_server_that_closes_the_event_stream_ends_the_waiting_call_and_is_reported_without_a_hang()
    {
        using var server = new FakeMcpSseServer();
        await using var client = await StartAsync(server);
        var ended = new TaskCompletionSource<McpServerExit>();
        client.ServerExited += exit => ended.TrySetResult(exit);

        var e = await Assert.ThrowsAsync<McpServerExitedException>(() => client.CallToolAsync("close"));

        Assert.StartsWith("The MCP server ended", e.Message);
        Assert.Null((await ended.Task.WaitAsync(TimeSpan.FromSeconds(10))).ExitCode);
        await Assert.ThrowsAsync<McpServerExitedException>(() => client.ListToolsAsync()); // nothing is waiting for an answer that cannot come
    }

    [Fact]
    public async Task A_stream_address_that_is_gone_is_a_readable_error_without_the_values_of_its_query()
    {
        var e = await Assert.ThrowsAsync<McpClientException>(() =>
            McpClient.StartRemoteAsync(new McpRemote(new Uri("http://127.0.0.1:1/sse?key=QUERYSECRET1"), new Dictionary<string, string>(), ["QUERYSECRET1"], LegacySse: true), Options, Http()));

        Assert.IsNotType<McpAuthException>(e);
        Assert.Contains("cannot be reached", e.Message);
        Assert.DoesNotContain("QUERYSECRET1", e.Message);
    }
}
