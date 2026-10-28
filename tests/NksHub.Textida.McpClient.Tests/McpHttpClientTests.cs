using NksHub.Textida.Rpc;

namespace NksHub.Textida.McpClient.Tests;

/// <summary>P6.4.3 for a server at an address: Streamable HTTP against a local fake, never the network.</summary>
public sealed class McpHttpClientTests
{
    private static readonly McpClientOptions Options = new(
        new RpcLimits(MaxMessageBytes: 1 << 20, MaxConcurrentIncoming: 4, MaxPendingOutgoing: 8, MaxResultItems: 10_000, MaxSessionBytes: 8L << 20, MaxOutgoingQueueBytes: 8L << 20),
        HandshakeTimeout: TimeSpan.FromSeconds(20), ExitGrace: TimeSpan.FromSeconds(5), StderrTailLines: 20, ClientName: "textida-test", ClientVersion: "0.0.1",
        CallTimeout: TimeSpan.FromSeconds(20), MaxResultChars: 1000);

    private static HttpClient Http() => new(new HttpClientHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };

    private static McpRemote Remote(string url, string token = "good-token") =>
        new(new Uri(url), new Dictionary<string, string> { ["Authorization"] = "Bearer " + token }, [token, "Bearer " + token]);

    private static Task<McpClient> StartAsync(FakeMcpHttpServer server, McpClientOptions? options = null) =>
        McpClient.StartRemoteAsync(Remote(server.Url), options ?? Options, Http());

    [Fact]
    public async Task A_server_at_an_address_is_connected_listed_called_and_its_session_is_ended()
    {
        using var server = new FakeMcpHttpServer();
        var client = await StartAsync(server);

        Assert.Equal((McpClient.LegacyVersion, "fake-http", "3", "over http"), (client.ProtocolVersion, client.ServerName, client.ServerVersion, client.Instructions));
        Assert.Equal(["echo", "slow"], (await client.ListToolsAsync()).Select(t => t.Name));
        // this list arrives as an event stream with a comment, an empty priming event and a notification ahead of the answer
        Assert.Equal(["test://notes", "test://big"], (await client.ListResourcesAsync()).Select(r => r.Uri));
        Assert.Equal("page", (await client.ReadResourceAsync("test://notes")).Text);
        Assert.Equal("héllo", (await client.CallToolAsync("echo", """{"text":"héllo"}""")).Text);

        // initialize cannot carry a session yet; everything after it names the session and the version, and the notification went out before the next request
        Assert.Equal(["initialize|session=|version=", "notifications/initialized|session=session-1|version=2025-11-25", "tools/list|session=session-1|version=2025-11-25"], server.Log.Take(3));
        Assert.DoesNotContain(server.Log, l => l.StartsWith("bad-accept:"));
        await client.DisposeAsync();
        Assert.Contains("delete:session-1", server.Log);
    }

    [Fact]
    public async Task A_key_the_server_refuses_is_reported_as_such_and_is_not_in_the_message()
    {
        using var server = new FakeMcpHttpServer { Token = "another-token" };

        var e = await Assert.ThrowsAsync<McpAuthException>(() => StartAsync(server));

        Assert.Contains("refused the key (HTTP 401)", e.Message);
        Assert.DoesNotContain("good-token", e.Message);
    }

    [Fact]
    public async Task A_key_refused_after_the_handshake_is_reported_the_same_way()
    {
        using var server = new FakeMcpHttpServer();
        await using var client = await StartAsync(server);
        server.Token = "rotated-token";

        await Assert.ThrowsAsync<McpAuthException>(() => client.ListToolsAsync());
    }

    [Fact]
    public async Task Cancelling_a_call_at_an_address_tells_the_server_and_the_client_goes_on()
    {
        using var server = new FakeMcpHttpServer();
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
    public async Task A_call_at_an_address_that_takes_too_long_ends_with_a_timeout_and_tells_the_server()
    {
        using var server = new FakeMcpHttpServer();
        await using var client = await StartAsync(server, Options with { CallTimeout = TimeSpan.FromMilliseconds(400) });

        var e = await Assert.ThrowsAsync<McpTimeoutException>(() => client.CallToolAsync("slow"));

        Assert.Contains("did not answer within 0.4 s", e.Message);
        await server.WaitForAsync(l => l.StartsWith("cancelled:"), "notifications/cancelled for the call that timed out");
        Assert.Equal("fine", (await client.CallToolAsync("echo", """{"text":"fine"}""")).Text);
    }

    [Fact]
    public async Task An_address_that_cannot_be_reached_is_a_readable_error_without_the_values_of_its_query()
    {
        var e = await Assert.ThrowsAsync<McpClientException>(() =>
            McpClient.StartRemoteAsync(new McpRemote(new Uri("http://127.0.0.1:1/mcp?key=QUERYSECRET1"), new Dictionary<string, string>(), ["QUERYSECRET1"]), Options, Http()));

        Assert.IsNotType<McpAuthException>(e);
        Assert.Contains("cannot be reached", e.Message);
        Assert.DoesNotContain("QUERYSECRET1", e.Message);
    }

    [Fact]
    public async Task A_status_the_client_does_not_expect_is_reported_with_the_number()
    {
        using var server = new FakeMcpHttpServer { StatusToSend = 500 };

        var e = await Assert.ThrowsAsync<McpClientException>(() => StartAsync(server));

        Assert.Contains("HTTP 500", e.Message);
    }

    [Fact]
    public async Task A_server_without_resources_is_not_asked_for_any()
    {
        using var server = new FakeMcpHttpServer { Resources = false };
        await using var client = await StartAsync(server);

        Assert.False(client.HasResources);
        Assert.Empty(await client.ListResourcesAsync());
        Assert.DoesNotContain(server.Log, l => l.StartsWith("resources/list"));
    }

    [Fact]
    public async Task An_answer_over_the_size_limit_is_refused_instead_of_filling_the_memory()
    {
        using var server = new FakeMcpHttpServer();
        await using var client = await StartAsync(server);

        var e = await Assert.ThrowsAsync<McpClientException>(() => client.CallToolAsync("huge"));

        Assert.Contains("more than 1048576 bytes", e.Message);
        Assert.Equal("small", (await client.CallToolAsync("echo", """{"text":"small"}""")).Text);
    }
}
