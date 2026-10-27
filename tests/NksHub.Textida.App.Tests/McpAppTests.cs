using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Startup;
using NksHub.Textida.App.Text;
using NksHub.Textida.McpClient;
using TextidaMcpClient = NksHub.Textida.McpClient.McpClient;
using NksHub.Textida.PluginHost.Mcp;
using NksHub.Textida.Rpc;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P6.3.1 through the running window and the real <c>textida-mcp</c> program: the user switches the server on after being told what the assistant gets, shares tabs one by one,
/// and decides on every proposal in the change preview. The client is a process that speaks MCP on its standard input and output.
/// </summary>
[Collection("LocState")]
public class McpAppTests : IDisposable
{
    private const string Meta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{}}";
    private static readonly string BridgeDll = Path.Combine(AppContext.BaseDirectory, "mcp", "textida-mcp.dll");

    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly List<McpClient> _clients = [];

    public McpAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _w.Width = 1000;
        _w.Height = 700;
    }

    public void Dispose()
    {
        foreach (var client in _clients) client.Dispose();
        try { _app.Plugins.Mcp.DisableAsync().Wait(TimeSpan.FromSeconds(5)); } catch (Exception e) when (e is AggregateException or TimeoutException) { }
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static async Task Until(Func<bool> condition, string what, int seconds = 30)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < seconds * 1000, "Not reached: " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    /// <summary>Lets the UI thread work while a request the client made is being answered (the preview of a proposal needs it).</summary>
    private static async Task<T> Pump<T>(Task<T> task, string what)
    {
        await Until(() => task.IsCompleted, what);
        return await task;
    }

    private static void Settle()
    {
        for (int i = 0; i < 20; i++) Dispatcher.UIThread.RunJobs();
    }

    private DocTab Open(string name, string content)
    {
        var tab = _w.OpenFile(_temp.File(name, content), null);
        _w.UpdateLayout();
        Settle();
        return tab;
    }

    private async Task RunAsync(string command)
    {
        var run = _app.Workbench.Commands.ExecuteAsync(command, new ShellContext(_w, forMenu: false)).AsTask();
        await Until(() => run.IsCompleted, command);
        Assert.Equal(CommandOutcome.Handled, await run);
        await Until(() => _w.McpTask.IsCompleted, "the MCP command to finish");
        await _w.McpTask;
    }

    private async Task SwitchOnAsync()
    {
        _dialogs.Answers.Enqueue(0);
        await RunAsync("nks.tools.mcpServer");
        Assert.True(_app.Plugins.Mcp.Enabled);
    }

    private async Task ShareAsync(DocTab tab)
    {
        _w.SelectTab(tab);
        await RunAsync("nks.tools.mcpShareTab");
    }

    /// <summary>A client process: the real program, started the way an MCP client starts it.</summary>
    private sealed class McpClient : IDisposable
    {
        private readonly Process _process;
        private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pending = [];
        private int _nextId;

        public McpClient()
        {
            Assert.True(File.Exists(BridgeDll), "The textida-mcp program is not next to the tests: " + BridgeDll);
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false),
            };
            start.ArgumentList.Add(BridgeDll);
            start.Environment["TEXTIDA_CHANNEL"] = App.Startup.Channel;
            _process = Process.Start(start)!;
            Errors = _process.StandardError.ReadToEndAsync();
            _ = ReadAsync();
        }

        public Task<string> Errors { get; }
        public bool HasExited => _process.HasExited;
        public int ExitCode => _process.ExitCode;
        public Task WaitForExitAsync() => _process.WaitForExitAsync();

        private async Task ReadAsync()
        {
            while (await _process.StandardOutput.ReadLineAsync() is { } line)
            {
                var message = JsonDocument.Parse(line).RootElement.Clone();
                TaskCompletionSource<JsonElement>? waiting = null;
                lock (_pending)
                {
                    if (message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number) _pending.Remove(id.GetInt32(), out waiting);
                }
                waiting?.SetResult(message);
            }
            lock (_pending) foreach (var waiting in _pending.Values) waiting.TrySetException(new IOException("The program closed its output."));
        }

        public async Task<JsonElement> RequestAsync(string method, string paramsJson)
        {
            int id = Interlocked.Increment(ref _nextId);
            var waiting = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pending) _pending[id] = waiting;
            await _process.StandardInput.WriteLineAsync($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"{method}\",\"params\":{paramsJson}}}");
            await _process.StandardInput.FlushAsync();
            return await waiting.Task;
        }

        public async Task<(bool IsError, string Text)> ToolAsync(string name, string argumentsJson = "{}")
        {
            var reply = await RequestAsync("tools/call", $"{{{Meta},\"name\":\"{name}\",\"arguments\":{argumentsJson}}}");
            Assert.True(reply.TryGetProperty("result", out var result), reply.GetRawText());
            return (result.GetProperty("isError").GetBoolean(), result.GetProperty("content")[0].GetProperty("text").GetString()!);
        }

        public void Dispose()
        {
            try { _process.StandardInput.Close(); } catch (IOException) { }
            if (!_process.WaitForExit(5000)) _process.Kill(true);
            _process.Dispose();
        }
    }

    private McpClient NewClient()
    {
        var client = new McpClient();
        _clients.Add(client);
        return client;
    }

    // ---- switching on ----

    [AvaloniaFact]
    public async Task The_server_is_off_until_the_user_has_been_told_what_the_assistant_gets()
    {
        Assert.False(_app.Plugins.Mcp.Enabled);
        _dialogs.Answers.Enqueue(1); // cancel
        await RunAsync("nks.tools.mcpServer");
        Assert.False(_app.Plugins.Mcp.Enabled);
        var asked = Assert.Single(_dialogs.Asked);
        Assert.Contains(McpService.BridgeProgram, asked.Message);
        Assert.Equal(McpServer.AllTools.Count, asked.Details!.Count);
        Assert.Contains(asked.Details, d => d.Contains(MainWindow.McpToolLabel("editor_prepare_changes")));

        // without the server a client finds nobody
        var client = NewClient();
        await client.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(1, client.ExitCode);
        Assert.Contains("not running or its MCP server is switched off", await client.Errors);

        _dialogs.Answers.Enqueue(0);
        await RunAsync("nks.tools.mcpServer");
        Assert.True(_app.Plugins.Mcp.Enabled);
        await RunAsync("nks.tools.mcpServer"); // the same command switches it off, without a question
        Assert.False(_app.Plugins.Mcp.Enabled);
        Assert.Equal(2, _dialogs.Asked.Count);
    }

    [AvaloniaFact]
    public async Task A_tab_can_only_be_shared_while_the_server_is_on()
    {
        var tab = Open("a.txt", "text\n");
        await ShareAsync(tab);
        Assert.False(_app.Plugins.Mcp.IsShared(tab));
        await SwitchOnAsync();
        await ShareAsync(tab);
        Assert.True(_app.Plugins.Mcp.IsShared(tab));
        await ShareAsync(tab); // the same command takes it back
        Assert.False(_app.Plugins.Mcp.IsShared(tab));
        Assert.False(_app.Plugins.Documents.IsExposed(tab.Doc.Doc.Id));
    }

    // ---- the whole way ----

    [AvaloniaFact]
    public async Task The_assistant_reads_an_unsaved_tab_proposes_a_change_and_the_user_applies_it_while_other_tabs_stay_invisible()
    {
        await SwitchOnAsync();
        var shared = Open("shared.txt", "alpha beta gamma\n");
        var other = Open("other.txt", "never shared\n");
        shared.Editor!.Document.Text = "unsaved alpha beta gamma\n"; // only in the editor, not on disk
        Settle();
        Assert.Equal("alpha beta gamma\n", File.ReadAllText(shared.Path!));
        await ShareAsync(shared);

        var client = NewClient();
        var discover = await Pump(client.RequestAsync("server/discover", $"{{{Meta}}}"), "server/discover");
        Assert.Equal("complete", discover.GetProperty("result").GetProperty("resultType").GetString());
        var tools = await Pump(client.RequestAsync("tools/list", $"{{{Meta}}}"), "tools/list");
        Assert.Equal(McpServer.AllTools.Count, tools.GetProperty("result").GetProperty("tools").GetArrayLength());

        var listed = JsonDocument.Parse((await Pump(client.ToolAsync("editor_list_documents"), "list")).Text).RootElement;
        var only = Assert.Single(listed.GetProperty("documents").EnumerateArray());
        Assert.Equal(shared.Doc.Doc.Id.ToString(), only.GetProperty("id").GetString());
        Assert.Equal("shared.txt", only.GetProperty("title").GetString());
        var id = shared.Doc.Doc.Id;
        var otherRead = await Pump(client.ToolAsync("editor_read_range", $"{{\"document\":\"{other.Doc.Doc.Id}\"}}"), "read of a tab that is not shared");
        Assert.True(otherRead.IsError);
        Assert.DoesNotContain("never shared", otherRead.Text);

        var read = JsonDocument.Parse((await Pump(client.ToolAsync("editor_read_range", $"{{\"document\":\"{id}\"}}"), "read")).Text).RootElement;
        Assert.Equal("unsaved alpha beta gamma\n", read.GetProperty("text").GetString());
        long revision = read.GetProperty("revision").GetInt64();

        // a proposal for an older revision is refused and the user is not asked
        var stale = await Pump(client.ToolAsync("editor_prepare_changes", $"{{\"document\":\"{id}\",\"revision\":{revision - 1},\"edits\":[{{\"start\":0,\"end\":1,\"text\":\"X\"}}]}}"), "stale proposal");
        Assert.True(stale.IsError);
        Assert.Contains("-32020", stale.Text);
        Assert.Empty(_dialogs.Asked.Skip(1));

        // a text proposal goes to the change review by hunks, the same one the proposals of plugins use
        var proposed = await Pump(client.ToolAsync("editor_prepare_changes", $"{{\"document\":\"{id}\",\"revision\":{revision},\"title\":\"Rename\",\"edits\":[{{\"find\":\"beta\",\"text\":\"delta\"}}]}}"), "proposal");
        Assert.False(proposed.IsError, proposed.Text);
        var proposal = JsonDocument.Parse(proposed.Text).RootElement.GetProperty("proposal").GetString()!;
        Assert.Equal(ProposalStatus.Pending, _app.Plugins.Mcp.Ledger.Get(proposal)!.Status);
        await Until(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true, "the review");
        _w.ApplyPlan();
        await Until(() => _app.Plugins.Mcp.Ledger.Get(proposal)!.Status != ProposalStatus.Pending, "the user's decision");
        Assert.Equal(ProposalStatus.Applied, _app.Plugins.Mcp.Ledger.Get(proposal)!.Status);
        Assert.Equal("unsaved alpha delta gamma\n", shared.Editor.Document.Text);
        var status = JsonDocument.Parse((await Pump(client.ToolAsync("editor_get_change_status", $"{{\"proposal\":\"{proposal}\"}}"), "status")).Text).RootElement;
        Assert.Equal("applied", status.GetProperty("status").GetString());

        // a proposal the user rejects changes nothing
        var again = JsonDocument.Parse((await Pump(client.ToolAsync("editor_prepare_changes", $"{{\"document\":\"{id}\",\"revision\":{status.GetProperty("appliedRevision").GetInt64()},\"edits\":[{{\"find\":\"gamma\",\"text\":\"omega\"}}]}}"), "second proposal")).Text).RootElement.GetProperty("proposal").GetString()!;
        await Until(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true, "the second review");
        foreach (var row in _w.ResultsView!.Rows.OfType<SearchResultsView.HunkRow>()) row.Selected = false;
        _w.ApplyPlan();
        await Until(() => _app.Plugins.Mcp.Ledger.Get(again)!.Status != ProposalStatus.Pending, "the second decision");
        Assert.Equal(ProposalStatus.Rejected, _app.Plugins.Mcp.Ledger.Get(again)!.Status);
        Assert.Equal("unsaved alpha delta gamma\n", shared.Editor.Document.Text);

        // the user takes the tab back: the next read is refused
        await ShareAsync(shared);
        var after = await Pump(client.ToolAsync("editor_read_range", $"{{\"document\":\"{id}\"}}"), "read after withdrawing");
        Assert.True(after.IsError);

        // switching the server off ends the connection
        await RunAsync("nks.tools.mcpServer");
        await Pump(client.WaitForExitAsync().ContinueWith(_ => true), "the program to end");
        Assert.True(client.HasExited);
    }

    [AvaloniaFact]
    public async Task Hex_changes_are_proposed_as_bytes_and_the_user_sees_them_before_and_after()
    {
        await SwitchOnAsync();
        var tab = _w.OpenFile(_temp.Bytes("data.bin", Enumerable.Range(0, 300).Select(i => (byte)(i % 251)).ToArray()), null);
        _w.UpdateLayout();
        Settle();
        await ShareAsync(tab);
        Assert.True(_app.Plugins.Mcp.IsShared(tab));
        var client = NewClient();
        var id = tab.Doc.Doc.Id;
        var read = JsonDocument.Parse((await Pump(client.ToolAsync("editor_read_bytes", $"{{\"document\":\"{id}\",\"start\":10,\"length\":256}}"), "read bytes")).Text).RootElement;
        Assert.Equal(512, read.GetProperty("data").GetString()!.Length);
        long revision = read.GetProperty("revision").GetInt64();
        _dialogs.Answers.Enqueue(0);
        var proposed = await Pump(client.ToolAsync("editor_prepare_byte_changes", $"{{\"document\":\"{id}\",\"revision\":{revision},\"edits\":[{{\"offset\":100,\"bytes\":\"DEADBEEF\"}}]}}"), "byte proposal");
        Assert.False(proposed.IsError, proposed.Text);
        var proposal = JsonDocument.Parse(proposed.Text).RootElement.GetProperty("proposal").GetString()!;
        await Until(() => _app.Plugins.Mcp.Ledger.Get(proposal)!.Status != ProposalStatus.Pending, "the decision");
        Assert.Equal(ProposalStatus.Applied, _app.Plugins.Mcp.Ledger.Get(proposal)!.Status);
        var preview = _dialogs.Asked[^1];
        Assert.Contains("DE AD BE EF", string.Join("\n", preview.Details!));
        Assert.Equal([0xDE, 0xAD, 0xBE, 0xEF], tab.Doc.Doc.ReadBytes(new NksHub.Textida.Core.Documents.ByteRange(100, 104)));
    }

    [AvaloniaFact]
    public async Task Values_of_an_env_tab_stay_hidden_unless_the_user_allows_them_for_that_tab()
    {
        await SwitchOnAsync();
        var env = Open(".env", "API_KEY=hunter2\nHOST=example\n");
        var client = NewClient();
        var id = env.Doc.Doc.Id;

        _dialogs.Answers.Enqueue(0); // keep hidden
        await ShareAsync(env);
        Assert.Contains(_dialogs.Asked, d => d.Title == Loc.Get(K.McpSecretsTitle));
        var hidden = await Pump(client.ToolAsync("editor_read_range", $"{{\"document\":\"{id}\"}}"), "hidden read");
        Assert.DoesNotContain("hunter2", hidden.Text);
        Assert.Contains("API_KEY=*******", hidden.Text);

        await ShareAsync(env); // take it back
        _dialogs.Answers.Enqueue(1); // show the values
        await ShareAsync(env);
        var shown = await Pump(client.ToolAsync("editor_read_range", $"{{\"document\":\"{id}\"}}"), "revealed read");
        Assert.Contains("hunter2", shown.Text);
    }

    [AvaloniaFact]
    public async Task Closing_a_shared_tab_or_switching_the_server_off_takes_the_grants_back()
    {
        await SwitchOnAsync();
        var closing = Open("closing.txt", "x\n");
        var staying = Open("staying.txt", "y\n");
        await ShareAsync(closing);
        await ShareAsync(staying);
        var permissions = _app.Plugins.Permissions;
        Assert.Equal(4, permissions.GrantsOf(McpServer.ClientId).Count); // read and propose for each

        var closedId = closing.Doc.Doc.Id;
        _w.CloseTab(closing);
        Settle();
        Assert.DoesNotContain(permissions.GrantsOf(McpServer.ClientId), g => g.Scope.Id == closedId.ToString("N"));
        Assert.False(_app.Plugins.Documents.IsExposed(closedId));
        Assert.Equal(2, permissions.GrantsOf(McpServer.ClientId).Count);

        await RunAsync("nks.tools.mcpServer");
        Assert.Empty(permissions.GrantsOf(McpServer.ClientId));
        Assert.False(_app.Plugins.Documents.IsExposed(staying.Doc.Doc.Id));
    }

    [AvaloniaFact]
    public async Task Taking_the_tab_back_or_switching_the_server_off_withdraws_a_proposal_that_still_waits_for_the_user()
    {
        await SwitchOnAsync();
        var tab = Open("waiting.txt", "alpha beta\n");
        await ShareAsync(tab);
        var client = NewClient();
        var id = tab.Doc.Doc.Id;
        async Task<string> ProposeAsync()
        {
            var reply = await Pump(client.ToolAsync("editor_prepare_changes", $"{{\"document\":\"{id}\",\"revision\":{tab.Doc.Doc.Revision},\"edits\":[{{\"find\":\"beta\",\"text\":\"delta\"}}]}}"), "proposal");
            Assert.False(reply.IsError, reply.Text);
            await Until(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true, "the review");
            return JsonDocument.Parse(reply.Text).RootElement.GetProperty("proposal").GetString()!;
        }

        var first = await ProposeAsync();
        Assert.Equal(ProposalStatus.Pending, _app.Plugins.Mcp.Ledger.Get(first)!.Status);
        await ShareAsync(tab); // the user takes the tab back while the review is open
        await Until(() => _app.Plugins.Mcp.Ledger.Get(first)!.Status != ProposalStatus.Pending, "the withdrawal");
        Assert.Equal(ProposalStatus.Rejected, _app.Plugins.Mcp.Ledger.Get(first)!.Status);
        Assert.Equal("alpha beta\n", tab.Editor!.Document.Text);

        await ShareAsync(tab);
        var second = await ProposeAsync();
        await RunAsync("nks.tools.mcpServer"); // switching the server off withdraws it as well
        await Until(() => _app.Plugins.Mcp.Ledger.Get(second)!.Status != ProposalStatus.Pending, "the withdrawal at switch off");
        Assert.Equal(ProposalStatus.Rejected, _app.Plugins.Mcp.Ledger.Get(second)!.Status);
        Assert.Equal("alpha beta\n", tab.Editor.Document.Text);
    }

    [AvaloniaFact]
    public async Task The_assistant_asks_where_the_cursor_is_and_gets_the_selected_text_of_the_shared_tab()
    {
        await SwitchOnAsync();
        var tab = Open("cursor.txt", "alpha beta gamma\n");
        tab.Editor!.Select(6, 4);
        Settle();
        await ShareAsync(tab);
        var client = NewClient();
        var reply = await Pump(client.ToolAsync("editor_get_selection", $"{{\"document\":\"{tab.Doc.Doc.Id}\"}}"), "selection");
        Assert.False(reply.IsError, reply.Text);
        var selection = JsonDocument.Parse(reply.Text).RootElement.GetProperty("selections")[0];
        Assert.Equal("beta", selection.GetProperty("text").GetString());
        Assert.Equal((6, 10), (selection.GetProperty("start").GetInt32(), selection.GetProperty("end").GetInt32()));
    }

    [AvaloniaFact]
    public async Task Sharing_the_selection_opens_a_read_only_excerpt_and_the_rest_of_the_tab_stays_hidden()
    {
        await SwitchOnAsync();
        var tab = Open("whole.txt", "alpha beta gamma\n");
        _w.SelectTab(tab);
        tab.Editor!.Select(3, 0);
        await RunAsync("nks.tools.mcpShareSelection"); // nothing selected: nothing is opened
        Assert.DoesNotContain(_w.Documents, d => d.Doc.IsVirtual);

        tab.Editor.Select(6, 4);
        Settle();
        await RunAsync("nks.tools.mcpShareSelection");
        var excerpt = Assert.Single(_w.Documents, d => d.Doc.IsVirtual);
        Assert.Equal("beta", excerpt.Doc.ReadText(new NksHub.Textida.Core.Documents.ByteRange(0, excerpt.Doc.ByteLength)));
        Assert.Contains("whole.txt", excerpt.Title);
        Assert.True(_app.Plugins.Mcp.IsSharedDocument(excerpt.Doc.Id));
        Assert.False(_app.Plugins.Mcp.IsShared(tab));

        var client = NewClient();
        var listed = JsonDocument.Parse((await Pump(client.ToolAsync("editor_list_documents"), "list")).Text).RootElement;
        Assert.Equal(excerpt.Doc.Id.ToString(), Assert.Single(listed.GetProperty("documents").EnumerateArray()).GetProperty("id").GetString());
        Assert.Contains("beta", (await Pump(client.ToolAsync("editor_read_range", $"{{\"document\":\"{excerpt.Doc.Id}\"}}"), "the excerpt")).Text);
        Assert.True((await Pump(client.ToolAsync("editor_read_range", $"{{\"document\":\"{tab.Doc.Doc.Id}\"}}"), "the whole tab")).IsError);
        var edit = await Pump(client.ToolAsync("editor_prepare_changes", $"{{\"document\":\"{excerpt.Doc.Id}\",\"revision\":{excerpt.Doc.Revision},\"edits\":[{{\"start\":0,\"end\":1,\"text\":\"B\"}}]}}"), "a proposal for the excerpt");
        Assert.True(edit.IsError);
        Assert.Equal("alpha beta gamma\n", tab.Editor.Document.Text);
    }

    [AvaloniaFact]
    public async Task Tools_can_be_switched_off_one_by_one()
    {
        await SwitchOnAsync();
        var client = NewClient();
        var before = await Pump(client.RequestAsync("tools/list", $"{{{Meta}}}"), "list");
        Assert.Equal(McpServer.AllTools.Count, before.GetProperty("result").GetProperty("tools").GetArrayLength());
        _app.Plugins.Mcp.Server.SetEnabled("editor_read_bytes", false);
        var after = await Pump(client.RequestAsync("tools/list", $"{{{Meta}}}"), "list again");
        Assert.DoesNotContain("editor_read_bytes", after.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        _app.Plugins.Mcp.Server.SetEnabled("editor_read_bytes", true);
    }

    [AvaloniaFact]
    public async Task The_tool_picker_lists_every_tool_with_its_state_and_choosing_one_switches_it()
    {
        var server = _app.Plugins.Mcp.Server;
        await _app.Workbench.Commands.ExecuteAsync("nks.tools.mcpTools", new ShellContext(_w, forMenu: false));
        await Until(() => _w.IsPaletteOpen && _w.PaletteModeNow == PaletteMode.Choice, "the tool list");
        Assert.Equal(McpServer.AllTools.Select(t => MainWindow.McpToolLabel(t.Name)), _w.QuickResults.Select(r => r.Title));
        Assert.All(_w.QuickResults, r => Assert.Equal(Loc.Get(K.McpToolsOn), r.Detail));

        _w.RunPaletteSelection(); // the first tool
        await Until(() => !server.IsEnabled("editor_list_documents"), "the tool to be switched off");
        await Until(() => _w.IsPaletteOpen && _w.PaletteModeNow == PaletteMode.Choice && _w.QuickResults[0].Detail == Loc.Get(K.McpToolsOff), "the list to show the new state");
        _w.RunPaletteSelection(); // and back
        await Until(() => server.IsEnabled("editor_list_documents"), "the tool to be switched on");
        await Until(() => _w.IsPaletteOpen && _w.PaletteModeNow == PaletteMode.Choice, "the list again");
        _w.ClosePalette();
        await Until(() => _w.McpTask.IsCompleted, "the picker to end");
    }

    // ---- the editor's own client (P6.4.3) against the real textida-mcp program and the running editor ----

    private static readonly McpClientOptions ClientOptions = new(
        new RpcLimits(MaxMessageBytes: 4 << 20, MaxConcurrentIncoming: 4, MaxPendingOutgoing: 8, MaxResultItems: 100_000, MaxSessionBytes: 16L << 20, MaxOutgoingQueueBytes: 16L << 20),
        HandshakeTimeout: TimeSpan.FromSeconds(60), ExitGrace: TimeSpan.FromSeconds(10), StderrTailLines: 20, ClientName: "textida-test", ClientVersion: "0.0.1");

    private static McpLaunch BridgeLaunch() => new(
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", [BridgeDll], AppContext.BaseDirectory,
        new Dictionary<string, string> { ["TEXTIDA_CHANNEL"] = App.Startup.Channel }, []);

    [AvaloniaFact]
    public async Task The_editors_own_client_reads_a_shared_tab_through_the_real_server_and_reports_the_server_going_away()
    {
        await SwitchOnAsync();
        var tab = Open("client.txt", "on disk\n");
        tab.Editor!.Document.Text = "unsaved for the client\n";
        Settle();
        await ShareAsync(tab);

        var client = await Pump(TextidaMcpClient.StartAsync(BridgeLaunch(), ClientOptions), "the client to connect");
        await using var _ = client;
        Assert.Equal(TextidaMcpClient.ModernVersion, client.ProtocolVersion);
        Assert.Equal("textida", client.ServerName);

        var tools = await Pump(client.ListToolsAsync(), "tools/list");
        Assert.Equal(McpServer.AllTools.Select(t => t.Name), tools.Select(t => t.Name));
        var listed = JsonDocument.Parse((await Pump(client.CallToolAsync("editor_list_documents"), "list")).Text).RootElement;
        var id = Assert.Single(listed.GetProperty("documents").EnumerateArray()).GetProperty("id").GetString();
        var read = await Pump(client.CallToolAsync("editor_read_range", $"{{\"document\":\"{id}\"}}"), "read");
        Assert.False(read.IsError);
        Assert.Contains("unsaved for the client", read.Text);

        var ended = new TaskCompletionSource<McpServerExit>();
        client.ServerExited += e => ended.TrySetResult(e);
        await _app.Plugins.Mcp.DisableAsync(); // the user switches the server off: the program ends and the client says so
        var exit = await Pump(ended.Task, "the client to notice the end");
        Assert.NotNull(exit.ExitCode);
        await Assert.ThrowsAsync<McpServerExitedException>(() => Pump(client.ListToolsAsync(), "a call after the end"));
    }

    [AvaloniaFact]
    public async Task The_editors_own_client_reports_the_stderr_of_the_program_when_the_server_is_off()
    {
        var e = await Assert.ThrowsAsync<McpServerExitedException>(() => Pump(TextidaMcpClient.StartAsync(BridgeLaunch(), ClientOptions), "the failed connect"));
        Assert.Equal(1, e.Exit.ExitCode);
        Assert.Contains("not running or its MCP server is switched off", e.Exit.StderrTail);
    }

    // ---- a client written by someone else ----

    private static string? Npx() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .SelectMany(d => new[] { "npx.cmd", "npx" }.Select(f => Path.Combine(d, f))).FirstOrDefault(File.Exists);

    /// <summary>The official MCP Inspector (CLI mode) as the client: it starts the program like any MCP client does and speaks whichever protocol era both sides agree on.</summary>
    private async Task<(int Exit, string Output, string Error)> InspectorAsync(string npx, params string[] arguments)
    {
        var start = new ProcessStartInfo(npx) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var a in new[] { "-y", "@modelcontextprotocol/inspector", "--cli", "-e", "TEXTIDA_CHANNEL=" + App.Startup.Channel }) start.ArgumentList.Add(a);
        foreach (var a in arguments) start.ArgumentList.Add(a);
        start.ArgumentList.Add("--"); // the options that take several values would swallow the program otherwise
        start.ArgumentList.Add(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet");
        start.ArgumentList.Add(BridgeDll);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await Until(() => process.HasExited && output.IsCompleted && error.IsCompleted, "the inspector to finish", seconds: 180);
        return (process.ExitCode, await output, await error);
    }

    [AvaloniaFact]
    [Trait("Category", "RealClient")]
    public async Task The_official_inspector_lists_the_tools_and_reads_a_shared_tab()
    {
        var npx = Npx();
        Assert.SkipWhen(npx is null, "Node.js (npx) is not installed here.");
        await SwitchOnAsync();
        var tab = Open("note.txt", "text for the inspector\n");
        tab.Editor!.Document.Text = "unsaved text for the inspector\n";
        Settle();
        await ShareAsync(tab);

        var list = await InspectorAsync(npx!, "--method", "tools/list");
        Assert.True(list.Exit == 0, list.Error + list.Output);
        var tools = JsonDocument.Parse(list.Output).RootElement.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Equal(McpServer.AllTools.Select(t => t.Name), tools);

        var read = await InspectorAsync(npx!, "--method", "tools/call", "--tool-name", "editor_read_range", "--tool-arg", "document=" + tab.Doc.Doc.Id);
        Assert.True(read.Exit == 0, read.Error + read.Output);
        var result = JsonDocument.Parse(read.Output).RootElement;
        Assert.False(result.TryGetProperty("isError", out var isError) && isError.GetBoolean(), read.Output);
        Assert.Contains("unsaved text for the inspector", result.GetProperty("content")[0].GetProperty("text").GetString());
    }
}
