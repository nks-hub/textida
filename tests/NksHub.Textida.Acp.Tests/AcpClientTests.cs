using NksHub.Textida.Rpc;

namespace NksHub.Textida.Acp.Tests;

/// <summary>P6.6.2 against a stand-in agent that is a real process speaking ACP on its standard input and output.</summary>
public class AcpClientTests
{
    private static readonly string FakeAgent = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.FakeAcp.dll");

    private sealed class TestHost : IAcpHost
    {
        public Dictionary<string, string> Files { get; } = [];
        public List<(string Path, string Content)> Written { get; } = [];
        public Func<AcpToolCallInfo, IReadOnlyList<AcpPermissionOption>, CancellationToken, Task<AcpPermissionChoice>> Permission { get; set; } =
            (_, _, _) => Task.FromResult(AcpPermissionChoice.Cancelled);
        public Exception? WriteFails { get; set; }
        public TaskCompletionSource PermissionAsked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<string> ReadTextFileAsync(string sessionId, string path, int? line, int? limit, CancellationToken cancellation) =>
            Files.TryGetValue(path, out var text) ? ValueTask.FromResult(text) : throw new AcpRefusedException(AcpRefusedException.NotFound, "Not shared: " + path);

        public ValueTask WriteTextFileAsync(string sessionId, string path, string content, CancellationToken cancellation)
        {
            if (WriteFails is not null) throw WriteFails;
            Written.Add((path, content));
            return ValueTask.CompletedTask;
        }

        public async ValueTask<AcpPermissionChoice> RequestPermissionAsync(string sessionId, AcpToolCallInfo toolCall, IReadOnlyList<AcpPermissionOption> options, CancellationToken cancellation)
        {
            PermissionAsked.TrySetResult();
            return await Permission(toolCall, options, cancellation);
        }
    }

    private static AcpOptions Options(TimeSpan? init = null, TimeSpan? idle = null) => new(
        new RpcLimits(MaxMessageBytes: 4 << 20, MaxConcurrentIncoming: 4, MaxPendingOutgoing: 8, MaxResultItems: 100_000, MaxSessionBytes: 16L << 20, MaxOutgoingQueueBytes: 16L << 20),
        init ?? TimeSpan.FromSeconds(30), idle ?? TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2), 10, "textida-test", "0");

    private static Task<AcpClient> Start(TestHost host, AcpOptions? options = null, params string[] agentArgs)
    {
        Assert.True(File.Exists(FakeAgent), "The stand-in agent is not next to the tests: " + FakeAgent);
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        return AcpClient.StartAsync(new AcpLaunch(dotnet, [FakeAgent, .. agentArgs], AppContext.BaseDirectory), options ?? Options(), host);
    }

    private static async Task<(AcpClient Client, string Session, List<AcpUpdate> Updates)> OpenAsync(TestHost host, AcpOptions? options = null)
    {
        var client = await Start(host, options);
        var updates = new List<AcpUpdate>();
        client.Updated += u => { lock (updates) updates.Add(u); };
        var session = await client.NewSessionAsync(AppContext.BaseDirectory);
        return (client, session, updates);
    }

    private static string Streamed(List<AcpUpdate> updates)
    {
        lock (updates) return string.Concat(updates.Where(u => u.Kind == AcpUpdateKind.AgentMessage).Select(u => u.Text));
    }

    private static async Task UntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Not reached: " + what);
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task A_turn_streams_its_chunks_in_order_and_ends_with_the_stop_reason()
    {
        var (client, session, updates) = await OpenAsync(new TestHost());
        await using var _ = client;
        Assert.Equal("fake-acp", client.Agent!.Name);
        Assert.Equal("end_turn", await client.PromptAsync(session, "hello"));
        // the notifications are delivered by their own loop; the answer to the turn may arrive first
        await UntilAsync(() => updates.Any(u => u.Kind == AcpUpdateKind.ToolCallUpdate), "the tool call update");
        Assert.Equal("Hello world", Streamed(updates));
        var call = updates.First(u => u.Kind == AcpUpdateKind.ToolCall);
        Assert.Equal(("call_1", "Looking around", "pending"), (call.ToolCallId, call.Title, call.Status));
        Assert.Equal("completed", updates.Last().Status);
        // the next turn works on the same session
        Assert.Equal("end_turn", await client.PromptAsync(session, "again"));
        await UntilAsync(() => Streamed(updates).EndsWith("echo:again"), "the second turn");
    }

    [Fact]
    public async Task Cancel_sends_session_cancel_and_the_turn_ends_cancelled()
    {
        var (client, session, updates) = await OpenAsync(new TestHost());
        await using var _ = client;
        var turn = client.PromptAsync(session, "slow");
        await UntilAsync(() => Streamed(updates).Contains("tick"), "the first chunk");
        await client.CancelAsync(session);
        Assert.Equal(AcpClient.Cancelled, await turn.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal("end_turn", await client.PromptAsync(session, "next"));
    }

    [Fact]
    public async Task Cancelling_the_token_cancels_the_turn()
    {
        var (client, session, updates) = await OpenAsync(new TestHost());
        await using var _ = client;
        using var cts = new CancellationTokenSource();
        var turn = client.PromptAsync(session, "slow", cts.Token);
        await UntilAsync(() => Streamed(updates).Contains("tick"), "the first chunk");
        cts.Cancel();
        Assert.Equal(AcpClient.Cancelled, await turn.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task A_cancelled_turn_answers_the_open_permission_request_with_cancelled()
    {
        var host = new TestHost { Permission = async (_, _, ct) => { await Task.Delay(Timeout.Infinite, ct); return AcpPermissionChoice.Cancelled; } };
        var (client, session, updates) = await OpenAsync(host);
        await using var _ = client;
        var turn = client.PromptAsync(session, "permwait");
        await host.PermissionAsked.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await client.CancelAsync(session);
        Assert.Equal(AcpClient.Cancelled, await turn.WaitAsync(TimeSpan.FromSeconds(30)));
        await UntilAsync(() => Streamed(updates).Contains("PERM:"), "the outcome the agent saw");
        Assert.Contains("\"outcome\":\"cancelled\"", Streamed(updates));
    }

    [Fact]
    public async Task A_permission_request_hands_over_the_diffs_of_the_tool_call_and_a_call_without_content_has_none()
    {
        AcpToolCallInfo? seen = null;
        var host = new TestHost { Permission = (tool, _, _) => { seen = tool; return Task.FromResult(new AcpPermissionChoice("reject-once")); } };
        var (client, session, _) = await OpenAsync(host);
        await using var _ = client;
        var path = Path.Combine(Path.GetTempPath(), "textida-acp-diff-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "old text");
        try
        {
            await client.PromptAsync(session, $"permedit {path}\nold\nnew");
            Assert.Equal("edit", seen!.Kind);
            Assert.Equal(new AcpDiff(path, "old", "new"), Assert.Single(seen.Diffs!)); // a piece of the file and its replacement, as the agent sent them

            await client.PromptAsync(session, $"permdiff {path}\nwhole\ntext");
            Assert.Equal(new AcpDiff(path, null, "whole\ntext"), Assert.Single(seen.Diffs!)); // no old text: a new file or the whole content
            Assert.Equal("old text", File.ReadAllText(path)); // rejected: the agent wrote nothing
        }
        finally { File.Delete(path); }

        await client.PromptAsync(session, "perm");
        Assert.Null(seen!.Diffs);
    }

    [Fact]
    public async Task The_agent_reads_a_file_through_the_host_and_a_refused_read_reaches_it_as_an_error()
    {
        var host = new TestHost();
        var path = Path.Combine(AppContext.BaseDirectory, "unsaved.txt");
        host.Files[path] = "text only in the editor";
        var (client, session, updates) = await OpenAsync(host);
        await using var _ = client;
        await client.PromptAsync(session, "read " + path);
        await UntilAsync(() => Streamed(updates).Contains("READ:"), "the content");
        Assert.Equal("READ:text only in the editor", Streamed(updates));
        updates.Clear();
        await client.PromptAsync(session, "read " + Path.Combine(AppContext.BaseDirectory, "not-shared.txt"));
        await UntilAsync(() => Streamed(updates).Contains("READERR:"), "the error");
        Assert.StartsWith("READERR:-32002:", Streamed(updates));
    }

    [Fact]
    public async Task A_write_goes_to_the_host_and_a_refusal_comes_back_with_its_code()
    {
        var host = new TestHost();
        var path = Path.Combine(AppContext.BaseDirectory, "out.txt");
        var (client, session, updates) = await OpenAsync(host);
        await using var _ = client;
        await client.PromptAsync(session, $"write {path}\nline 1\nline 2");
        await UntilAsync(() => Streamed(updates).Contains("WRITE-OK"), "the answer to the write");
        Assert.Equal([(path, "line 1\nline 2")], host.Written);

        updates.Clear();
        host.WriteFails = new AcpRefusedException(AcpRefusedException.Declined, "The user rejected the change.");
        await client.PromptAsync(session, $"write {path}\nx");
        await UntilAsync(() => Streamed(updates).Contains("WRITE-ERR"), "the refusal");
        Assert.Equal("WRITE-ERR:-32001:The user rejected the change.", Streamed(updates));
        Assert.Single(host.Written);
    }

    [Fact]
    public async Task The_option_the_user_chose_is_what_the_agent_gets()
    {
        var host = new TestHost { Permission = (tool, options, _) => Task.FromResult(new AcpPermissionChoice(options.First(o => o.IsReject).Id)) };
        var (client, session, updates) = await OpenAsync(host);
        await using var _ = client;
        await client.PromptAsync(session, "perm");
        await UntilAsync(() => Streamed(updates).Contains("PERM:"), "the outcome");
        Assert.Equal("PERM:{\"outcome\":\"selected\",\"optionId\":\"reject-once\"}", Streamed(updates));
    }

    [Fact]
    public async Task An_option_the_agent_did_not_offer_is_answered_as_cancelled()
    {
        var host = new TestHost { Permission = (_, _, _) => Task.FromResult(new AcpPermissionChoice("made-up")) };
        var (client, session, updates) = await OpenAsync(host);
        await using var _ = client;
        await client.PromptAsync(session, "perm");
        await UntilAsync(() => Streamed(updates).Contains("PERM:"), "the outcome");
        Assert.Equal("PERM:{\"outcome\":\"cancelled\"}", Streamed(updates));
    }

    [Fact]
    public async Task A_crash_during_a_turn_is_reported_with_the_exit_code_and_stderr_and_does_not_throw_elsewhere()
    {
        var (client, session, _) = await OpenAsync(new TestHost());
        await using var _ = client;
        var raised = new TaskCompletionSource<AcpExit>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AgentExited += exit => raised.TrySetResult(exit);
        var failure = await Assert.ThrowsAsync<AcpAgentExitedException>(() => client.PromptAsync(session, "crash"));
        Assert.Equal(3, failure.Exit.ExitCode);
        Assert.Contains("agent crashed on purpose", failure.Exit.StderrTail);
        Assert.Equal(3, (await raised.Task.WaitAsync(TimeSpan.FromSeconds(30))).ExitCode);
        await Assert.ThrowsAnyAsync<Exception>(() => client.PromptAsync(session, "hello"));
    }

    [Fact]
    public async Task An_agent_that_does_not_answer_initialize_times_out()
    {
        var failure = await Assert.ThrowsAsync<AcpTimeoutException>(() => Start(new TestHost(), Options(init: TimeSpan.FromMilliseconds(400)), "--hang-init"));
        Assert.Contains("initialize", failure.Message);
    }

    [Fact]
    public async Task A_turn_in_which_the_agent_goes_quiet_times_out()
    {
        var (client, session, _) = await OpenAsync(new TestHost(), Options(idle: TimeSpan.FromMilliseconds(400)));
        await using var _ = client;
        await Assert.ThrowsAsync<AcpTimeoutException>(() => client.PromptAsync(session, "silent"));
    }

    [Fact]
    public async Task Time_spent_waiting_for_the_user_is_not_idle_time()
    {
        var host = new TestHost
        {
            Permission = async (_, options, _) =>
            {
                await Task.Delay(1200);
                return new AcpPermissionChoice(options[0].Id);
            },
        };
        var (client, session, updates) = await OpenAsync(host, Options(idle: TimeSpan.FromMilliseconds(400)));
        await using var _ = client;
        Assert.Equal("end_turn", await client.PromptAsync(session, "perm"));
        await UntilAsync(() => Streamed(updates).Contains("allow-once"), "the outcome");
    }

    [Fact]
    public async Task Another_protocol_version_and_a_login_request_are_reported_in_words()
    {
        var version = await Assert.ThrowsAsync<AcpException>(() => Start(new TestHost(), null, "--protocol", "2"));
        Assert.Contains("version 2", version.Message);

        var client = await Start(new TestHost(), null, "--auth");
        await using var _ = client;
        var login = await Assert.ThrowsAsync<AcpException>(() => client.NewSessionAsync(AppContext.BaseDirectory));
        Assert.Contains("log in", login.Message);
        Assert.Equal(-32000, login.Code);
    }

    [Fact]
    public async Task A_program_that_does_not_exist_is_reported()
    {
        var failure = await Assert.ThrowsAsync<AcpException>(() => AcpClient.StartAsync(new AcpLaunch(Path.Combine(AppContext.BaseDirectory, "no-such-agent.exe"), [], AppContext.BaseDirectory), Options(), new TestHost()));
        Assert.NotEmpty(failure.Message);
    }

    [Fact]
    public async Task An_agent_that_is_a_batch_file_gets_every_argument_back_and_a_line_break_is_refused_before_the_start()
    {
        if (!OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("acp-batch-").FullName;
        try
        {
            string output = Path.Combine(dir, "args.txt");
            string bat = Path.Combine(dir, "agent.cmd");
            var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            File.WriteAllText(bat, $"@echo off\r\n\"{dotnet}\" \"{Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.ToolEcho.dll")}\" %* > \"{output}\"\r\n");
            string[] args = ["a&b", "%PATH%", "say \"hi\"", "c^d", "x y", "100%", "back\\"];
            // the batch file ends without speaking ACP, so the start fails after the arguments were written
            await Assert.ThrowsAnyAsync<AcpException>(() => AcpClient.StartAsync(new AcpLaunch(bat, args, dir), Options(), new TestHost()));
            Assert.Equal(args.Select(a => "[" + a + "]"), File.ReadAllLines(output));
            File.Delete(output);
            var refused = await Assert.ThrowsAsync<AcpException>(() => AcpClient.StartAsync(new AcpLaunch(bat, ["fine", "one\ntwo"], dir), Options(), new TestHost()));
            Assert.Contains("control character", refused.Message);
            Assert.False(File.Exists(output));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Programs_are_found_in_PATH_and_a_batch_file_is_found_after_a_program_of_the_same_folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "acp-find-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Assert.Contains("not found in PATH", AcpLaunch.Find("no-such-agent", folder).Problem);
            if (OperatingSystem.IsWindows())
            {
                File.WriteAllText(Path.Combine(folder, "agent.cmd"), "@echo off");
                Assert.Equal(Path.Combine(folder, "agent.cmd"), AcpLaunch.Find("agent", folder).Path);
                File.WriteAllText(Path.Combine(folder, "agent.exe"), "");
                Assert.Equal(Path.Combine(folder, "agent.exe"), AcpLaunch.Find("agent", folder).Path);
            }
            else
            {
                File.WriteAllText(Path.Combine(folder, "agent"), "");
                Assert.Equal(Path.Combine(folder, "agent"), AcpLaunch.Find("agent", folder).Path);
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Every_adapter_is_unverified_until_a_real_run_is_recorded()
    {
        Assert.Equal(["gemini", "codex", "claude", "junie", "copilot", "opencode"], AcpAdapters.All.Select(a => a.Id));
        Assert.All(AcpAdapters.All, a =>
        {
            Assert.False(a.IsVerified);
            Assert.False(a.EditorSetsMode);
        });
        Assert.Equal("opencode acp", AcpAdapters.Find("opencode")!.CommandLine);
    }

    // ---- P6.6.3 / P6.6.4: the table as data, the version probe and the boundary of the guarantee ----

    private static string Dotnet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

    [Fact]
    public void The_table_says_per_adapter_what_the_editor_sets_and_what_the_own_tools_may_do()
    {
        Assert.All(AcpAdapters.All, a =>
        {
            Assert.False(a.EditorSetsMode); // no session/set_mode is ever sent (see the test of the methods the agent saw)
            Assert.Equal(AcpReach.All, a.OwnToolsReach); // the agent is a plain process with the rights of the user: nothing stops its tools
            Assert.Matches(@"^\d+\.\d+\.\d+$", a.ListedVersion);
            Assert.StartsWith("https://github.com/", a.Repository);
            Assert.Equal(["--version"], a.VersionCheckArgs);
        });
    }

    [Fact]
    public void The_table_holds_only_program_names_and_flags_no_key_token_or_environment()
    {
        Assert.All(AcpAdapters.All, a =>
        {
            Assert.Matches("^[a-z][a-z-]*$", a.Command);
            Assert.All(a.Args, arg => Assert.Matches("^([a-z]+|--[a-z]+(=(true|false))?)$", arg));
        });
    }

    [Fact]
    public async Task The_editor_sends_no_permission_mode_and_offers_the_agent_no_terminal()
    {
        var (client, session, updates) = await OpenAsync(new TestHost());
        await using var _ = client;
        await client.PromptAsync(session, "seen");
        await UntilAsync(() => Streamed(updates).Contains("SEEN:"), "the methods");
        Assert.Equal("SEEN:initialize,session/new,session/prompt", Streamed(updates));
        updates.Clear();
        await client.PromptAsync(session, "caps");
        await UntilAsync(() => Streamed(updates).Contains("CAPS:"), "the capabilities");
        Assert.Equal("""CAPS:{"fs":{"readTextFile":true,"writeTextFile":true}}""", Streamed(updates));
    }

    [Theory]
    [InlineData("execute", true)]
    [InlineData("delete", true)]
    [InlineData("move", true)]
    [InlineData("fetch", true)]
    [InlineData("read", false)]
    [InlineData("edit", false)]
    [InlineData("search", false)]
    [InlineData("think", false)]
    [InlineData("switch_mode", false)]
    [InlineData("other", false)]
    [InlineData(null, false)]
    public void Only_the_tool_kinds_without_a_method_the_editor_serves_count_as_outside_the_editor(string? kind, bool outside) =>
        Assert.Equal(outside, AcpToolKinds.RunsOutsideEditor(kind));

    [Theory]
    [InlineData("--version", "0.1.0", null)]
    [InlineData("--version-silent", null, "no version in the output")]
    [InlineData("--version-fail", null, "exit code 4")]
    public async Task The_version_probe_takes_the_first_version_number_and_says_why_it_found_none(string flag, string? version, string? problem)
    {
        var result = await AcpVersionProbe.ReadAsync(Dotnet, [FakeAgent, flag], AppContext.BaseDirectory, TimeSpan.FromSeconds(60));
        Assert.Equal(new AcpVersionResult(version, problem), result);
    }

    [Fact]
    public async Task A_program_that_does_not_answer_is_ended_and_one_that_does_not_exist_is_reported()
    {
        var slow = await AcpVersionProbe.ReadAsync(Dotnet, [FakeAgent, "--version-hang"], AppContext.BaseDirectory, TimeSpan.FromMilliseconds(1500));
        Assert.Null(slow.Version);
        Assert.StartsWith("no answer within", slow.Problem);

        var missing = await AcpVersionProbe.ReadAsync(Path.Combine(AppContext.BaseDirectory, "no-such-agent.exe"), ["--version"], AppContext.BaseDirectory, TimeSpan.FromSeconds(10));
        Assert.Null(missing.Version);
        Assert.NotEmpty(missing.Problem!);

        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var stopped = await AcpVersionProbe.ReadAsync(Dotnet, [FakeAgent, "--version-hang"], AppContext.BaseDirectory, TimeSpan.FromSeconds(60), cancelled.Token).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Null(stopped.Version);
    }

    [Fact]
    public void A_crash_report_hides_the_secret_part_of_a_URL_in_the_last_line_of_stderr()
    {
        var message = new AcpAgentExitedException(new AcpExit(1, "login at https://user:hunter2@example.com/cb?token=abc123 failed")).Message;
        Assert.Contains("example.com", message);
        Assert.DoesNotContain("hunter2", message);
        Assert.DoesNotContain("abc123", message);
    }
}
