using NksHub.Textida.McpClient;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.McpClient.Tests;

/// <summary>P6.4.3 against a real MCP server process (the test server of ToolEcho): both protocol eras, paging, a call, a cancel that reaches the server, and a server that dies.</summary>
public sealed class McpClientTests
{
    private static readonly string EchoDll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.ToolEcho.dll");
    private static string DotNet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

    private static readonly McpClientOptions Options = new(
        new RpcLimits(MaxMessageBytes: 1 << 20, MaxConcurrentIncoming: 4, MaxPendingOutgoing: 8, MaxResultItems: 10_000, MaxSessionBytes: 8L << 20, MaxOutgoingQueueBytes: 8L << 20),
        HandshakeTimeout: TimeSpan.FromSeconds(30), ExitGrace: TimeSpan.FromSeconds(5), StderrTailLines: 20, ClientName: "textida-test", ClientVersion: "0.0.1",
        CallTimeout: TimeSpan.FromSeconds(30), MaxResultChars: 1000);

    [Fact]
    public async Task A_server_that_is_a_batch_file_gets_every_argument_back_and_a_line_break_is_refused_before_the_start()
    {
        if (!OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("mcp-batch-").FullName;
        try
        {
            string output = Path.Combine(dir, "args.txt");
            string bat = Path.Combine(dir, "server.cmd");
            File.WriteAllText(bat, $"@echo off\r\n\"{DotNet}\" \"{EchoDll}\" %* > \"{output}\"\r\n");
            string[] args = ["a&b", "%PATH%", "say \"hi\"", "c^d", "x y", "100%", "back\\"];
            // the batch file ends without speaking MCP, so the start fails after the arguments were written
            await Assert.ThrowsAnyAsync<McpClientException>(() => McpClient.StartAsync(new McpLaunch(bat, args, dir, new Dictionary<string, string>(), []), Options));
            Assert.Equal(args.Select(a => "[" + a + "]"), File.ReadAllLines(output));
            File.Delete(output);
            var refused = await Assert.ThrowsAsync<McpClientException>(() => McpClient.StartAsync(new McpLaunch(bat, ["fine", "one\ntwo"], dir, new Dictionary<string, string>(), []), Options));
            Assert.Contains("control character", refused.Message);
            Assert.False(File.Exists(output));
        }
        finally { Directory.Delete(dir, true); }
    }

    private sealed class Log
    {
        private readonly List<string> _lines = [];
        public void Add(string line) { lock (_lines) _lines.Add(line); }
        public string[] Lines { get { lock (_lines) return [.. _lines]; } }

        public async Task<string> WaitForAsync(Func<string, bool> match, string what)
        {
            for (int i = 0; i < 500; i++)
            {
                if (Lines.FirstOrDefault(match) is { } found) return found;
                await Task.Delay(20);
            }
            throw new Xunit.Sdk.XunitException("Not seen on the standard error of the server: " + what + "\n" + string.Join('\n', Lines));
        }
    }

    private static Task<McpClient> StartAsync(string mode, Log log, Dictionary<string, string>? env = null, string[]? secrets = null) =>
        McpClient.StartAsync(new McpLaunch(DotNet, [EchoDll, "--mcp", mode], AppContext.BaseDirectory, env ?? [], secrets ?? []), Options, log.Add);

    [Fact]
    public async Task Connects_in_the_current_era_lists_all_pages_and_calls_a_tool()
    {
        var log = new Log();
        await using var client = await StartAsync("modern", log);
        Assert.Equal(McpClient.ModernVersion, client.ProtocolVersion);
        Assert.Equal(("fake", "2"), (client.ServerName, client.ServerVersion));

        var tools = await client.ListToolsAsync();
        Assert.Equal(["echo", "slow", "crash", "env", "secret"], tools.Select(t => t.Name));
        var echo = tools[0];
        Assert.Equal(("Echo", "Returns text", true), (echo.Title, echo.Description, echo.ReadOnly));
        Assert.Contains("\"object\"", echo.InputSchemaJson);
        await log.WaitForAsync(l => l == "era:modern", "tools/list carrying the version in _meta");

        var result = await client.CallToolAsync("echo", """{"text":"héllo"}""");
        Assert.Equal((false, "héllo"), (result.IsError, result.Text));
    }

    [Fact]
    public async Task A_server_that_only_knows_initialize_gets_the_handshake_of_2025_11_25()
    {
        var log = new Log();
        await using var client = await StartAsync("legacy", log);
        Assert.Equal(McpClient.LegacyVersion, client.ProtocolVersion);
        Assert.Equal(("fake", "1", "fake instructions"), (client.ServerName, client.ServerVersion, client.Instructions));
        await log.WaitForAsync(l => l == "notified:notifications/initialized", "notifications/initialized");
        Assert.Equal(5, (await client.ListToolsAsync()).Count);
        await log.WaitForAsync(l => l == "era:legacy", "tools/list without _meta");
        Assert.Equal("x", (await client.CallToolAsync("echo", """{"text":"x"}""")).Text);
    }

    [Fact]
    public async Task A_server_of_another_version_is_refused_with_the_versions_the_client_speaks()
    {
        var e = await Assert.ThrowsAsync<McpClientException>(() => StartAsync("old", new Log()));
        Assert.Contains("2024-11-05", e.Message);
        Assert.Contains(McpClient.ModernVersion, e.Message);
    }

    [Fact]
    public async Task A_tool_error_is_an_answer_and_an_unknown_tool_is_a_protocol_error()
    {
        await using var client = await StartAsync("modern", new Log());
        var e = await Assert.ThrowsAsync<RpcException>(() => client.CallToolAsync("nope"));
        Assert.Equal(RpcErrorCodes.InvalidParams, e.Code);
        Assert.Equal("(unset)", (await client.CallToolAsync("env", """{"name":"NO_SUCH_VARIABLE_XYZ"}""")).Text);
    }

    [Theory]
    [InlineData("modern")]
    [InlineData("legacy")]
    public async Task Resources_are_listed_and_read_and_a_binary_part_is_named_by_its_size(string mode)
    {
        await using var client = await StartAsync(mode, new Log());
        Assert.True(client.HasResources);

        var resources = await client.ListResourcesAsync();
        Assert.Equal(["test://notes", "test://blob", "test://big"], resources.Select(r => r.Uri));
        Assert.Equal(("notes", "Notes", "text/plain"), (resources[0].Name, resources[0].Title, resources[0].MimeType));

        var notes = await client.ReadResourceAsync("test://notes");
        Assert.Equal(("first notes\nsecond part", "text/plain", false), (notes.Text, notes.MimeType, notes.Truncated));
        var blob = await client.ReadResourceAsync("test://blob");
        Assert.Equal("[binary data, application/octet-stream, about 12 bytes]", blob.Text);
        var missing = await Assert.ThrowsAsync<RpcException>(() => client.ReadResourceAsync("test://nope"));
        Assert.Equal(-32002, missing.Code);
    }

    [Fact]
    public async Task A_result_and_a_resource_longer_than_the_limit_are_cut_and_say_how_long_they_were()
    {
        await using var client = await StartAsync("modern", new Log());

        var result = await client.CallToolAsync("big", """{"length":5000,"unit":"y"}""");
        Assert.Equal((1000, 5000, true), (result.Text.Length, result.FullLength, result.Truncated));
        Assert.Equal(new string('y', 1000), result.Text);
        var resource = await client.ReadResourceAsync("test://big");
        Assert.Equal((1000, 5000, true), (resource.Text.Length, resource.FullLength, resource.Truncated));

        var exact = await client.CallToolAsync("big", """{"length":1000,"unit":"y"}""");
        Assert.False(exact.Truncated); // exactly the limit is not a cut
    }

    [Fact]
    public async Task A_cut_never_splits_the_two_halves_of_a_character()
    {
        await using var client = await StartAsync("modern", new Log());

        // "x" and 600 characters of two UTF-16 units each: unit 999 is the first half of a pair
        var result = await client.CallToolAsync("big", """{"prefix":"x","length":600,"unit":"😀"}""");

        Assert.Equal(999, result.Text.Length);
        Assert.Equal(1201, result.FullLength);
        Assert.False(char.IsHighSurrogate(result.Text[^1]));
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task A_call_that_takes_too_long_ends_with_a_timeout_tells_the_server_and_the_client_goes_on()
    {
        var log = new Log();
        await using var client = await McpClient.StartAsync(new McpLaunch(DotNet, [EchoDll, "--mcp", "modern"], AppContext.BaseDirectory, new Dictionary<string, string>(), []),
            Options with { CallTimeout = TimeSpan.FromMilliseconds(500) }, log.Add);

        var e = await Assert.ThrowsAsync<McpTimeoutException>(() => client.CallToolAsync("slow"));

        Assert.Contains("did not answer within 0.5 s", e.Message);
        var started = await log.WaitForAsync(l => l.StartsWith("slow-started:"), "the call to arrive");
        await log.WaitForAsync(l => l == "cancelled:" + started["slow-started:".Length..], "notifications/cancelled for the call that timed out");
        Assert.Equal("still here", (await client.CallToolAsync("echo", """{"text":"still here"}""")).Text);
    }

    [Theory]
    [InlineData("modern")]
    [InlineData("legacy")]
    public async Task Cancelling_a_call_tells_the_server_with_notifications_cancelled(string mode)
    {
        var log = new Log();
        await using var client = await StartAsync(mode, log);
        using var cts = new CancellationTokenSource();
        var call = client.CallToolAsync("slow", ct: cts.Token);
        var started = await log.WaitForAsync(l => l.StartsWith("slow-started:"), "the call to arrive");
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        // the id the server saw is the id the cancel names
        await log.WaitForAsync(l => l == "cancelled:" + started["slow-started:".Length..], "notifications/cancelled for that request");
        Assert.Equal("still alive", (await client.CallToolAsync("echo", """{"text":"still alive"}""")).Text);
    }

    [Fact]
    public async Task A_server_that_dies_is_reported_with_its_exit_code_and_stderr_and_the_caller_goes_on()
    {
        var log = new Log();
        var client = await StartAsync("modern", log);
        var exits = new List<McpServerExit>();
        client.ServerExited += exits.Add;
        var pending = client.CallToolAsync("slow");
        await log.WaitForAsync(l => l.StartsWith("slow-started:"), "the pending call to arrive");

        var crash = await Assert.ThrowsAsync<McpServerExitedException>(() => client.CallToolAsync("crash"));
        Assert.Equal(7, crash.Exit.ExitCode);
        Assert.Contains("boom", crash.Exit.StderrTail);
        Assert.Contains("exit code 7", crash.Message);
        Assert.Contains("boom", crash.Message);
        await Assert.ThrowsAsync<McpServerExitedException>(() => pending); // the call in flight learns it too
        await Assert.ThrowsAsync<McpServerExitedException>(() => client.ListToolsAsync()); // and so does the next one
        Assert.Equal(7, (await client.Exited).ExitCode);
        await Task.Delay(100);
        Assert.Single(exits);
        await client.DisposeAsync(); // does not throw after a crash
    }

    [Fact]
    public async Task A_program_that_is_not_a_server_ends_the_handshake_with_its_exit_code()
    {
        var e = await Assert.ThrowsAsync<McpServerExitedException>(() =>
            McpClient.StartAsync(new McpLaunch(DotNet, [EchoDll, "plain", "--exit", "3"], AppContext.BaseDirectory, new Dictionary<string, string>(), []), Options));
        Assert.Equal(3, e.Exit.ExitCode);
    }

    [Fact]
    public async Task A_program_that_cannot_start_is_a_readable_error()
    {
        var e = await Assert.ThrowsAsync<McpClientException>(() =>
            McpClient.StartAsync(new McpLaunch(Path.Combine(AppContext.BaseDirectory, "no-such-file.exe"), [], AppContext.BaseDirectory, new Dictionary<string, string>(), []), Options));
        Assert.NotEmpty(e.Message);
    }

    [Fact]
    public async Task Environment_values_reach_the_server_and_never_the_log()
    {
        const string secret = "s3cr3t-value-xyz";
        var log = new Log();
        await using var client = await StartAsync("modern", log, new() { ["TEST_SECRET"] = secret }, [secret]);
        Assert.Equal(secret, (await client.CallToolAsync("env", """{"name":"TEST_SECRET"}""")).Text); // the server has it
        await client.CallToolAsync("secret");
        var line = await log.WaitForAsync(l => l.StartsWith("token is"), "the line that holds the secret");
        Assert.Equal("token is ***", line);
        Assert.DoesNotContain(log.Lines, l => l.Contains(secret));
        Assert.DoesNotContain(secret, (await Assert.ThrowsAsync<McpServerExitedException>(() => client.CallToolAsync("crash"))).Message);
    }

    /// <summary>The official reference server (<c>@modelcontextprotocol/server-everything</c>), started the way an agent's configuration starts it: <c>npx -y package</c>.</summary>
    [Fact]
    [Trait("Category", "RealClient")]
    public async Task The_official_reference_server_lists_its_tools_answers_a_call_and_honours_a_cancel()
    {
        var npx = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).SelectMany(d => new[] { "npx.cmd", "npx" }.Select(f => Path.Combine(d, f))).FirstOrDefault(File.Exists);
        if (npx is null) return; // nothing to test without Node.js
        var entry = new McpServerEntry("everything", McpAgent.ClaudeCode, McpTransport.Stdio, "npx", ["-y", "@modelcontextprotocol/server-everything"], new Dictionary<string, string>(), null, new Dictionary<string, string>());
        var launch = McpConfig.BuildLaunch(entry, _ => null, AppContext.BaseDirectory);
        var log = new Log();
        await using var client = await McpClient.StartAsync(launch, Options with { HandshakeTimeout = TimeSpan.FromMinutes(3) }, log.Add);
        Console.WriteLine($"reference server: {client.ServerName} {client.ServerVersion}, protocol {client.ProtocolVersion}");

        var tools = await client.ListToolsAsync();
        Assert.Contains(tools, t => t.Name == "echo");
        var echo = await client.CallToolAsync("echo", """{"message":"hello from textida"}""");
        Assert.False(echo.IsError);
        Assert.Contains("hello from textida", echo.Text);

        using var cts = new CancellationTokenSource();
        var slow = client.CallToolAsync("trigger-long-running-operation", """{"duration":60,"steps":60}""", cts.Token);
        await Task.Delay(500);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slow);
        Assert.Contains("hello again", (await client.CallToolAsync("echo", """{"message":"hello again"}""")).Text); // the server is still there
    }

    [Fact]
    public async Task Disposing_ends_the_server_through_its_input()
    {
        var client = await StartAsync("modern", new Log());
        var exit = client.Exited;
        await client.DisposeAsync();
        Assert.True(exit.IsCompleted);
        Assert.Equal(0, (await exit).ExitCode);
    }
}
