using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.McpClient.Tests;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P6.4.3 through the window: the editor connects to a server of an agent's list (a real test server started as a program, and a Streamable HTTP fake on the loopback), lists its tools and
/// resources, reads a resource into a read-only tab and calls a tool only after the user has seen the call and said yes. Results are cut at the limit, a slow server ends the call with a
/// message, a stopped one is reported, and every key goes through the credential store: a missing one is asked for (hidden), a refused one is dropped and asked for again.
/// </summary>
[Collection("LocState")]
public class McpClientAppTests : IDisposable
{
    private const string Secret = "s3cr3t-value-xyz";
    private const string KeyVariable = "TEXTIDA_TEST_MCP_KEY";
    private const string AskedVariable = "TEXTIDA_TEST_ASKED_VALUE";
    private const string KeyReference = "${" + KeyVariable + "}";
    private const string AskedReference = "${" + AskedVariable + "}";
    private static readonly string EchoDll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.ToolEcho.dll");

    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly FakeMcpHttpServer _server = new();
    private readonly FakeCredentials _store = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly string _home;
    private readonly string _marker;

    public McpClientAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _app.Credentials = _store;
        _w = _app.CreateWindow(null);
        _w.Width = 1000;
        _w.Height = 700;
        _home = Path.Combine(_temp.Path, "home");
        _marker = Path.Combine(_temp.Path, "started.marker");
        _app.AgentConfigHome = _home;
        var dotnet = JsonSerializer.Serialize(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet");
        var program = $"[{JsonSerializer.Serialize(EchoDll)}, \"--mcp\", \"modern\"]";
        Directory.CreateDirectory(_home);
        File.WriteAllText(Path.Combine(_home, ".claude.json"), $$"""
            { "mcpServers": {
              "fake": { "command": {{dotnet}}, "args": {{program}}, "env": { "TEST_SECRET": "{{Secret}}", "TEST_MARKER": {{JsonSerializer.Serialize(_marker)}} } },
              "remote": { "type": "http", "url": {{JsonSerializer.Serialize(_server.Url)}}, "headers": { "Authorization": "Bearer {{KeyReference}}" } },
              "asking": { "command": {{dotnet}}, "args": {{program}}, "env": { "TEST_SECRET": "{{AskedReference}}", "TEST_MARKER": {{JsonSerializer.Serialize(_marker)}} } }
            } }
            """);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(KeyVariable, null);
        _server.Dispose();
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static async Task Until(Func<bool> condition, string what, int seconds = 60)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < seconds * 1000, "Not reached: " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private Task WaitForRowsAsync(string what, int rows) =>
        Until(() => _w.IsPaletteOpen && _w.PaletteModeNow == PaletteMode.Choice && _w.QuickResults.Count == rows, what);

    /// <summary>Opens the list of servers, takes the server at <paramref name="server"/> and its action Connect; the answers of the dialogs are scripted by the caller.</summary>
    private async Task ConnectAsync(int server)
    {
        await _app.Workbench.Commands.ExecuteAsync("nks.tools.mcpServers", new ShellContext(_w, forMenu: false)).AsTask();
        await WaitForRowsAsync("the servers", 3);
        _w.MovePalette(server);
        _w.RunPaletteSelection();
        await WaitForRowsAsync("the actions", 5);
        _w.MovePalette(1); // Connect
        _w.RunPaletteSelection();
    }

    /// <summary>Dismisses the list of tools, which brings the list of servers back, and that one too.</summary>
    private async Task FinishAsync()
    {
        _w.ClosePalette();
        await WaitForRowsAsync("the servers again", 3);
        _w.ClosePalette();
        await Until(() => _w.McpTask.IsCompleted, "the list to end");
    }

    private static string HashOf(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private IEnumerable<string> TasksOutput() => _app.Output.Read(OutputChannel.Tasks).Select(l => l.Text);

    [AvaloniaFact]
    public async Task Connect_lists_the_tools_and_resources_of_a_real_server_and_a_resource_opens_in_a_read_only_tab()
    {
        _dialogs.Answers.Enqueue(0); // Start
        await ConnectAsync(0);
        await WaitForRowsAsync("tools and resources", 8);

        Assert.Equal(["echo", "slow", "crash", "env", "secret", "Notes", "blob", "big"], _w.QuickResults.Select(r => r.Title));
        Assert.Equal(Loc.Get(K.McpClientToolRow, ("text", "Returns text")), _w.QuickResults[0].Detail);
        Assert.Equal(Loc.Get(K.McpClientResourceRow, ("text", "test://blob")), _w.QuickResults[6].Detail);
        var start = _dialogs.Asked.Single();
        Assert.Equal(Loc.Get(K.McpAgentsTryTitle, ("name", "fake")), start.Title);
        Assert.Equal(Loc.Get(K.McpClientStartMessage), start.Message); // the program stays up while the list is used
        Assert.Contains("TEST_SECRET=***", start.Details!);

        _w.MovePalette(5); // Notes
        _w.RunPaletteSelection();
        await WaitForRowsAsync("the list again", 8);

        var tab = _w.Active!;
        Assert.Equal("first notes\nsecond part", tab.Editor!.Document.Text);
        Assert.Equal(Loc.Get(K.McpClientResourceTab, ("server", "fake"), ("resource", "Notes")), tab.Title);
        Assert.False(tab.Doc.Doc.VirtualEditable);
        Assert.Null(tab.Path);
        Assert.Equal(Loc.Get(K.McpClientResourceOk, ("resource", "Notes"), ("length", 23)), _w.StatusMessageText);
        Assert.True(File.Exists(_marker));
        await FinishAsync();
    }

    [AvaloniaFact]
    public async Task A_binary_resource_is_shown_as_a_line_that_names_its_size()
    {
        _dialogs.Answers.Enqueue(0);
        await ConnectAsync(0);
        await WaitForRowsAsync("tools and resources", 8);
        _w.MovePalette(6); // blob
        _w.RunPaletteSelection();
        await WaitForRowsAsync("the list again", 8);

        Assert.Equal("[binary data, application/octet-stream, about 12 bytes]", _w.Active!.Editor!.Document.Text);
        await FinishAsync();
    }

    [AvaloniaFact]
    public async Task A_tool_is_not_called_until_the_user_has_seen_the_call_and_said_yes()
    {
        _dialogs.Answers.Enqueue(0); // Start
        _dialogs.Answers.Enqueue(1); // the call: Cancel
        _dialogs.Prompts.Enqueue("""{"text":"never sent"}""");
        await ConnectAsync(0);
        await WaitForRowsAsync("tools and resources", 8);
        var tabs = _w.Tabs.Count();

        _w.RunPaletteSelection(); // echo
        await Until(() => _dialogs.Asked.Count == 2, "the question about the call");
        await WaitForRowsAsync("the list again", 8);

        var asked = _dialogs.Asked[1];
        Assert.Equal(Loc.Get(K.McpClientCallTitle, ("tool", "echo"), ("server", "fake")), asked.Title);
        Assert.Equal(Loc.Get(K.McpClientCallMessage, ("server", "fake"), ("tool", "echo"), ("note", Loc.Get(K.McpClientCallReadOnly))), asked.Message);
        Assert.Equal([Loc.Get(K.McpClientCallDetailArgs, ("arguments", """{"text":"never sent"}"""))], asked.Details);
        Assert.True(asked.Buttons[1] is { IsDefault: true, IsCancel: true }); // Enter does not call
        Assert.Equal(tabs, _w.Tabs.Count());
        Assert.DoesNotContain(TasksOutput(), t => t == Loc.Get(K.McpClientLogCall, ("tool", "echo"), ("chars", 24)));
        await FinishAsync();
    }

    [AvaloniaFact]
    public async Task A_tool_that_does_not_say_it_only_reads_is_marked_so_in_the_question()
    {
        _dialogs.Answers.Enqueue(0);
        _dialogs.Answers.Enqueue(1);
        _dialogs.Prompts.Enqueue("{}");
        await ConnectAsync(0);
        await WaitForRowsAsync("tools and resources", 8);
        _w.MovePalette(1); // slow
        _w.RunPaletteSelection();
        await Until(() => _dialogs.Asked.Count == 2, "the question about the call");
        await WaitForRowsAsync("the list again", 8);

        Assert.Contains(Loc.Get(K.McpClientCallMayChange), _dialogs.Asked[1].Message);
        await FinishAsync();
    }

    [AvaloniaFact]
    public async Task A_tool_that_is_allowed_is_called_and_its_answer_opens_in_a_new_tab_without_the_arguments_in_the_log()
    {
        const string arguments = """{"text":"hello from the tool"}""";
        _dialogs.Answers.Enqueue(0); // Start
        _dialogs.Answers.Enqueue(0); // Call
        _dialogs.Prompts.Enqueue(arguments);
        await ConnectAsync(0);
        await WaitForRowsAsync("tools and resources", 8);

        _w.RunPaletteSelection(); // echo
        await WaitForRowsAsync("the list again", 8);

        Assert.Equal("hello from the tool", _w.Active!.Editor!.Document.Text);
        Assert.Equal(Loc.Get(K.McpClientResultTab, ("server", "fake"), ("tool", "echo")), _w.Active.Title);
        Assert.Equal(Loc.Get(K.McpClientResultOk, ("tool", "echo"), ("length", 19)), _w.StatusMessageText);
        Assert.Equal([Loc.Get(K.McpClientArgsSchema, ("schema", """{"type":"object"}"""))], _dialogs.Previews); // the schema is shown while the arguments are typed
        var lines = TasksOutput().ToList();
        Assert.Contains(Loc.Get(K.McpClientLogCall, ("tool", "echo"), ("chars", arguments.Length)), lines);
        Assert.DoesNotContain(lines, l => l.Contains("hello from the tool"));
        await FinishAsync();
    }

    [AvaloniaFact]
    public async Task Arguments_that_are_not_a_json_object_are_refused_before_anything_is_asked_or_sent()
    {
        _dialogs.Answers.Enqueue(0);
        _dialogs.Prompts.Enqueue("[1,2]");
        await ConnectAsync(0);
        await WaitForRowsAsync("tools and resources", 8);

        _w.RunPaletteSelection(); // echo
        await WaitForRowsAsync("the list again", 8);

        Assert.Equal(Loc.Get(K.McpClientArgsInvalid), _w.StatusMessageText);
        Assert.Equal([Loc.Get(K.McpClientArgsInvalid)], _dialogs.Previews);
        Assert.Single(_dialogs.Asked); // only the start was asked
        await FinishAsync();
    }

    [AvaloniaFact]
    public async Task An_answer_longer_than_the_limit_is_cut_and_the_message_says_how_long_it_was()
    {
        _app.McpClientOptions = _app.McpClientOptions with { MaxResultChars = 10 };
        _dialogs.Answers.Enqueue(0);
        _dialogs.Answers.Enqueue(0);
        _dialogs.Prompts.Enqueue("""{"text":"0123456789abcdef"}""");
        await ConnectAsync(0);
        await WaitForRowsAsync("tools and resources", 8);

        _w.RunPaletteSelection(); // echo
        await WaitForRowsAsync("the list again", 8);

        Assert.Equal("0123456789", _w.Active!.Editor!.Document.Text);
        Assert.Equal(Loc.Get(K.McpClientResultOk, ("tool", "echo"), ("length", 16)) + " " + Loc.Get(K.McpClientCut, ("limit", 10), ("length", 16)), _w.StatusMessageText);
        await FinishAsync();
    }

    [AvaloniaFact]
    public async Task A_resource_longer_than_the_limit_is_cut_too()
    {
        _app.McpClientOptions = _app.McpClientOptions with { MaxResultChars = 100 };
        _dialogs.Answers.Enqueue(0);
        await ConnectAsync(0);
        await WaitForRowsAsync("tools and resources", 8);
        _w.MovePalette(7); // big
        _w.RunPaletteSelection();
        await WaitForRowsAsync("the list again", 8);

        Assert.Equal(new string('r', 100), _w.Active!.Editor!.Document.Text);
        Assert.EndsWith(Loc.Get(K.McpClientCut, ("limit", 100), ("length", 5000)), _w.StatusMessageText);
        await FinishAsync();
    }

    [AvaloniaFact]
    public async Task A_server_that_does_not_answer_ends_the_call_with_a_message_and_the_list_stays_usable()
    {
        _app.McpClientOptions = _app.McpClientOptions with { CallTimeout = TimeSpan.FromSeconds(1) };
        _dialogs.Answers.Enqueue(0);
        _dialogs.Answers.Enqueue(0);
        _dialogs.Prompts.Enqueue("{}");
        await ConnectAsync(0);
        await WaitForRowsAsync("tools and resources", 8);
        var tabs = _w.Tabs.Count();

        _w.MovePalette(1); // slow
        _w.RunPaletteSelection();
        await Until(() => _w.StatusMessageText == Loc.Get(K.McpClientFailed, ("name", "slow"), ("reason", Loc.Get(K.McpAgentsTimeout, ("seconds", "1")))), "the timeout message");
        await WaitForRowsAsync("the list again", 8);

        Assert.Equal(tabs, _w.Tabs.Count());
        await FinishAsync();
    }

    [AvaloniaFact]
    public async Task A_server_that_stops_during_a_call_is_reported_with_its_exit_code_and_the_list_closes()
    {
        _dialogs.Answers.Enqueue(0);
        _dialogs.Answers.Enqueue(0);
        _dialogs.Prompts.Enqueue("{}");
        await ConnectAsync(0);
        await WaitForRowsAsync("tools and resources", 8);

        _w.MovePalette(2); // crash
        _w.RunPaletteSelection();
        await WaitForRowsAsync("the servers again", 3);

        Assert.Contains("exit code 7", _w.StatusMessageText);
        Assert.Contains("boom", _w.StatusMessageText);
        _w.ClosePalette();
        await Until(() => _w.McpTask.IsCompleted, "the list to end");
        Assert.Contains(TasksOutput(), t => t.Contains("exit code 7"));
    }

    // ---- keys ----

    [AvaloniaFact]
    public async Task A_variable_nobody_knows_is_asked_hidden_and_kept_in_the_credential_store_and_in_no_file()
    {
        const string typed = "typed-secret-value-42";
        var file = Path.Combine(_home, ".claude.json");
        var before = HashOf(file);
        _dialogs.Secrets.Enqueue(typed);
        _dialogs.Answers.Enqueue(0); // Start
        await ConnectAsync(2);
        await WaitForRowsAsync("tools and resources", 8);

        Assert.Equal([Loc.Get(K.McpClientAskLabel, ("server", "asking"), ("name", AskedVariable))], _dialogs.SecretsAsked);
        Assert.Equal(typed, _store[MainWindow.McpEnvCredentialPrefix + AskedVariable]);
        var start = _dialogs.Asked.Single();
        Assert.Contains("TEST_SECRET=***", start.Details!);
        Assert.DoesNotContain(typed, string.Join('\n', start.Details!) + start.Message + start.Title);
        Assert.DoesNotContain(TasksOutput(), t => t.Contains(typed));
        Assert.Equal(before, HashOf(file));
        Assert.DoesNotContain(typed, File.ReadAllText(file));
        foreach (var path in Directory.EnumerateFiles(_temp.Path, "*", SearchOption.AllDirectories).Where(p => !p.EndsWith(".marker", StringComparison.Ordinal)))
        {
            try { Assert.DoesNotContain(typed, File.ReadAllText(path)); }
            catch (IOException) { /* a locked journal file keeps no keys */ }
        }
        await FinishAsync();

        // the next connection finds it in the store and asks nothing
        _dialogs.Answers.Enqueue(0);
        await ConnectAsync(2);
        await WaitForRowsAsync("tools and resources again", 8);
        Assert.Single(_dialogs.SecretsAsked);
        await FinishAsync();
    }

    [AvaloniaFact]
    public async Task When_the_user_gives_no_value_nothing_is_started()
    {
        await ConnectAsync(2);
        await WaitForRowsAsync("the servers again", 3);

        Assert.Equal(Loc.Get(K.McpClientAskNone, ("name", AskedVariable)), _w.StatusMessageText);
        Assert.Empty(_dialogs.Asked);
        Assert.False(File.Exists(_marker));
        Assert.Empty(_store);
        _w.ClosePalette();
        await Until(() => _w.McpTask.IsCompleted, "the list to end");
    }

    [AvaloniaFact]
    public async Task A_server_at_an_address_is_connected_with_the_key_from_the_store_and_a_tool_is_called_there()
    {
        _store[MainWindow.McpEnvCredentialPrefix + KeyVariable] = "good-token";
        _dialogs.Answers.Enqueue(0); // Connect
        _dialogs.Answers.Enqueue(0); // Call
        _dialogs.Prompts.Enqueue("""{"text":"over http"}""");
        await ConnectAsync(1);
        await WaitForRowsAsync("tools and resources", 4);

        Assert.Equal(["echo", "slow", "notes", "big"], _w.QuickResults.Select(r => r.Title));
        var start = _dialogs.Asked.Single();
        Assert.Equal(Loc.Get(K.McpClientConnectTitle, ("name", "remote")), start.Title);
        Assert.Equal(Loc.Get(K.McpClientConnectMessage), start.Message);
        Assert.Equal([Loc.Get(K.McpClientDetailUrl, ("url", _server.Url)), "Authorization: ***"], start.Details);
        Assert.Equal(Loc.Get(K.McpClientConnectButton), start.Buttons[0].Label);

        _w.RunPaletteSelection(); // echo
        await WaitForRowsAsync("the list again", 4);
        Assert.Equal("over http", _w.Active!.Editor!.Document.Text);
        Assert.DoesNotContain(_server.Log, l => l == "401");
        Assert.DoesNotContain("good-token", string.Join('\n', TasksOutput()) + _w.StatusMessageText + string.Join('\n', start.Details!));
        await FinishAsync();
        Assert.Contains("delete:session-1", _server.Log); // the session ended with the list
    }

    [AvaloniaFact]
    public async Task A_key_the_server_refuses_is_dropped_from_the_store_and_asked_for_again_without_a_second_question_about_starting()
    {
        _store[MainWindow.McpEnvCredentialPrefix + KeyVariable] = "stale-key";
        _dialogs.Secrets.Enqueue("good-token");
        _dialogs.Answers.Enqueue(0);
        await ConnectAsync(1);
        await WaitForRowsAsync("tools and resources", 4);

        Assert.Equal("401", _server.Log.First()); // the stale key was tried first
        Assert.Equal([Loc.Get(K.McpClientAskLabel, ("server", "remote"), ("name", KeyVariable))], _dialogs.SecretsAsked);
        Assert.Equal("good-token", _store[MainWindow.McpEnvCredentialPrefix + KeyVariable]); // the new key replaced it
        Assert.Single(_dialogs.Asked);
        await FinishAsync();
    }

    [AvaloniaFact]
    public async Task A_key_that_is_refused_again_ends_the_connection_with_a_message_and_nothing_stays_stored()
    {
        _store[MainWindow.McpEnvCredentialPrefix + KeyVariable] = "stale-key";
        _dialogs.Secrets.Enqueue("also-wrong");
        _dialogs.Answers.Enqueue(0); // Connect
        _dialogs.Answers.Enqueue(0); // Close
        await ConnectAsync(1);
        await Until(() => _dialogs.Asked.Count == 2, "the failure");
        await WaitForRowsAsync("the servers again", 3);

        Assert.Contains("refused the key (HTTP 401)", _dialogs.Asked[1].Message);
        Assert.DoesNotContain(MainWindow.McpEnvCredentialPrefix + KeyVariable, _store.Keys);
        Assert.Single(_dialogs.SecretsAsked); // asked once, not in a loop
        Assert.DoesNotContain("also-wrong", _dialogs.Asked[1].Message);
        _w.ClosePalette();
        await Until(() => _w.McpTask.IsCompleted, "the list to end");
    }

    [AvaloniaFact]
    public async Task A_refused_key_that_comes_from_the_environment_is_not_ours_to_drop_and_the_message_says_where_to_change_it()
    {
        Environment.SetEnvironmentVariable(KeyVariable, "wrong-env-key");
        _dialogs.Answers.Enqueue(0);
        _dialogs.Answers.Enqueue(0);
        await ConnectAsync(1);
        await Until(() => _dialogs.Asked.Count == 2, "the failure");
        await WaitForRowsAsync("the servers again", 3);

        Assert.Contains(Loc.Get(K.McpClientAuthKept, ("name", "remote")), _dialogs.Asked[1].Message);
        Assert.Empty(_dialogs.SecretsAsked);
        Assert.Equal("wrong-env-key", Environment.GetEnvironmentVariable(KeyVariable));
        _w.ClosePalette();
        await Until(() => _w.McpTask.IsCompleted, "the list to end");
    }

    private sealed class FakeCredentials : Dictionary<string, string>, NksHub.Textida.Core.Secrets.ICredentialStore
    {
        public string? Get(string key) => TryGetValue(key, out var v) ? v : null;
        public void Set(string key, string value) => this[key] = value;
        public bool Delete(string key) => Remove(key);
    }
}
