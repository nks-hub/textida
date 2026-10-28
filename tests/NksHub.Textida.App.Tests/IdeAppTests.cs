using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost.Mcp;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P6.5.1 through the running window: the user switches the /ide bridge of a window on after being told what the agent gets, the lock file and the status bar item follow it, every window
/// has its own bridge, a diff the agent opens goes through the change review, and a tab that is not shared or a secrets file never reaches the agent. The agent is a fake client over loopback;
/// the lock file goes to a folder of the test, never to the one of the user.
/// </summary>
[Collection("LocState")]
public class IdeAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly string _lockDirectory;
    private readonly List<Agent> _agents = [];

    public IdeAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _lockDirectory = Path.Combine(_temp.Path, "ide");
        _app.Plugins.Mcp.IdeLockDirectory = _lockDirectory;
        _w = _app.CreateWindow(null);
        _w.Width = 1000;
        _w.Height = 700;
    }

    public void Dispose()
    {
        foreach (var agent in _agents) agent.Dispose();
        var off = _app.Plugins.Mcp.DisableAsync(); // its bridges close on the UI thread, which this method is on
        for (var sw = Stopwatch.StartNew(); !off.IsCompleted && sw.ElapsedMilliseconds < 5000;)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
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

    /// <summary>Lets the UI thread work while a request the agent made is being answered (the review of a diff needs it).</summary>
    private static async Task<T> Pump<T>(Task<T> task, string what)
    {
        await Until(() => task.IsCompleted, what);
        return await task;
    }

    private static void Settle()
    {
        for (int i = 0; i < 20; i++) Dispatcher.UIThread.RunJobs();
    }

    private async Task Quiet(int milliseconds = 300) => await Pump(Task.Delay(milliseconds).ContinueWith(_ => true), "a quiet moment");

    private DocTab Open(MainWindow w, string name, string content)
    {
        var tab = w.OpenFile(_temp.File(name, content), null);
        w.UpdateLayout();
        Settle();
        return tab;
    }

    private async Task RunAsync(MainWindow w, string command)
    {
        var run = _app.Workbench.Commands.ExecuteAsync(command, new ShellContext(w, forMenu: false)).AsTask();
        await Until(() => run.IsCompleted, command);
        Assert.Equal(CommandOutcome.Handled, await run);
        await Until(() => w.McpTask.IsCompleted, "the MCP command to finish");
        await w.McpTask;
    }

    private async Task SwitchOnServerAsync()
    {
        _dialogs.Answers.Enqueue(0);
        await RunAsync(_w, "nks.tools.mcpServer");
        Assert.True(_app.Plugins.Mcp.Enabled);
    }

    /// <summary>The window's own method, not the command: a command runs on the window that was activated last, which is not the one asked for once a second window exists.</summary>
    private async Task SwitchOnBridgeAsync(MainWindow w)
    {
        _dialogs.Answers.Enqueue(0);
        w.ToggleIdeBridge();
        await Until(() => w.McpTask.IsCompleted, "the bridge command to finish");
        await w.McpTask;
        Assert.NotNull(_app.Plugins.Mcp.IdeOf(w));
    }

    private async Task ShareAsync(MainWindow w, DocTab tab)
    {
        w.SelectTab(tab);
        w.ShareTabWithMcp();
        await Until(() => w.McpTask.IsCompleted, "the share to finish");
        await w.McpTask;
        Assert.True(_app.Plugins.Mcp.IsShared(tab));
    }

    private string[] LockFiles() => Directory.Exists(_lockDirectory) ? Directory.GetFiles(_lockDirectory, "*.lock") : [];

    private async Task<Agent> ConnectAsync(MainWindow w, string name = "fake-agent", string version = "1", string? token = null, bool initialize = true)
    {
        var bridge = _app.Plugins.Mcp.IdeOf(w)!;
        using var lockContent = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(_lockDirectory, bridge.Port + ".lock")));
        var agent = await Pump(Agent.ConnectAsync(bridge.Port, token ?? lockContent.RootElement.GetProperty("authToken").GetString()!), "the agent to connect");
        _agents.Add(agent);
        if (initialize) await Pump(agent.InitializeAsync(name, version), "the handshake");
        return agent;
    }

    private static string DiffArgs(string path, string contents, string tab) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { ["old_file_path"] = path, ["new_file_path"] = path, ["new_file_contents"] = contents, ["tab_name"] = tab });

    private bool ReviewShown => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true;

    /// <summary>A fake agent: reads the token from the lock file, connects with it and speaks JSON-RPC over the WebSocket.</summary>
    private sealed class Agent : IDisposable
    {
        private readonly ClientWebSocket _socket = new();
        private int _nextId;

        public List<JsonElement> Notifications { get; } = [];

        public static async Task<Agent> ConnectAsync(int port, string token)
        {
            var agent = new Agent();
            agent._socket.Options.SetRequestHeader(IdeBridge.AuthorizationHeader, token);
            agent._socket.Options.AddSubProtocol("mcp");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await agent._socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), timeout.Token); }
            catch
            {
                agent.Dispose();
                throw;
            }
            return agent;
        }

        private async Task SendAsync(string json) => await _socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

        private async Task<JsonElement> ReceiveAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var buffer = new byte[64 * 1024];
            var message = new MemoryStream();
            ValueWebSocketReceiveResult result;
            do
            {
                result = await _socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);
            return JsonDocument.Parse(message.ToArray()).RootElement.Clone();
        }

        public async Task<JsonElement> RequestAsync(string method, string paramsJson)
        {
            var id = ++_nextId;
            await SendAsync($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"{method}\",\"params\":{paramsJson}}}");
            while (true)
            {
                var message = await ReceiveAsync();
                if (message.TryGetProperty("id", out var got) && got.GetInt32() == id) return message;
                Notifications.Add(message);
            }
        }

        public async Task<bool> InitializeAsync(string name, string version)
        {
            var reply = await RequestAsync("initialize", $"{{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{{}},\"clientInfo\":{{\"name\":\"{name}\",\"version\":\"{version}\"}}}}");
            Assert.False(reply.TryGetProperty("error", out _), reply.GetRawText());
            await SendAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
            return true;
        }

        public async Task<(bool IsError, string Text)> ToolTextAsync(string name, string argumentsJson = "{}")
        {
            var reply = await RequestAsync("tools/call", $"{{\"name\":\"{name}\",\"arguments\":{argumentsJson}}}");
            Assert.False(reply.TryGetProperty("error", out _), reply.GetRawText());
            var result = reply.GetProperty("result");
            return (result.GetProperty("isError").GetBoolean(), result.GetProperty("content")[0].GetProperty("text").GetString()!);
        }

        public async Task<JsonElement> NextNotificationAsync()
        {
            if (Notifications.Count > 0)
            {
                var first = Notifications[0];
                Notifications.RemoveAt(0);
                return first;
            }
            return await ReceiveAsync();
        }

        public void Dispose() => _socket.Dispose();
    }

    // ---- switching on and off ----

    [AvaloniaFact]
    public async Task The_bridge_needs_the_server_and_the_users_yes_and_its_lock_file_and_status_item_follow_the_switch()
    {
        await RunAsync(_w, "nks.tools.ideBridge"); // the MCP server is off: nothing is opened and nobody is asked
        Assert.Equal(Loc.Get(K.McpShareNeedServer), _w.StatusMessageText);
        Assert.Empty(_dialogs.Asked);
        Assert.Null(_app.Plugins.Mcp.IdeOf(_w));
        Assert.Empty(LockFiles());

        await SwitchOnServerAsync();
        _dialogs.Answers.Enqueue(1); // cancel
        await RunAsync(_w, "nks.tools.ideBridge");
        Assert.Null(_app.Plugins.Mcp.IdeOf(_w));
        Assert.Empty(LockFiles());
        Assert.Contains(_lockDirectory, _dialogs.Asked[^1].Message); // the user is told where the lock file goes
        Assert.False(_w.IdeItem.IsVisible);

        await SwitchOnBridgeAsync(_w);
        var bridge = _app.Plugins.Mcp.IdeOf(_w)!;
        var lockFile = Assert.Single(LockFiles());
        Assert.Equal(Path.Combine(_lockDirectory, bridge.Port + ".lock"), lockFile);
        using (var content = JsonDocument.Parse(File.ReadAllBytes(lockFile)))
        {
            Assert.Equal(Environment.ProcessId, content.RootElement.GetProperty("pid").GetInt32());
            Assert.Equal("Textida", content.RootElement.GetProperty("ideName").GetString());
            Assert.Equal(bridge.AuthToken, content.RootElement.GetProperty("authToken").GetString());
        }
        Assert.Equal(Loc.Get(K.IdeOnStatus, ("port", bridge.Port)), _w.StatusMessageText);
        Assert.True(_w.IdeItem.IsVisible);
        Assert.Equal(Loc.Get(K.IdeStatusWaiting), _w.IdeItem.Content);

        int asked = _dialogs.Asked.Count;
        await RunAsync(_w, "nks.tools.ideBridge"); // the same command closes it, without a question
        Assert.Equal(asked, _dialogs.Asked.Count);
        Assert.Null(_app.Plugins.Mcp.IdeOf(_w));
        Assert.Empty(LockFiles());
        Assert.False(_w.IdeItem.IsVisible);
        Assert.Equal(Loc.Get(K.IdeOffStatus), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public async Task A_connected_agent_is_named_in_the_status_bar_and_both_the_command_and_a_click_cut_it_off()
    {
        _w.DisconnectIdeAgent();
        Assert.Equal(Loc.Get(K.IdeNotOn), _w.StatusMessageText);
        await SwitchOnServerAsync();
        await SwitchOnBridgeAsync(_w);
        _w.DisconnectIdeAgent();
        Assert.Equal(Loc.Get(K.IdeNoAgent), _w.StatusMessageText);

        var agent = await ConnectAsync(_w, "fake-agent", "2.5");
        string connected = Loc.Get(K.IdeStatusConnected, ("agent", "fake-agent 2.5"));
        await Until(() => Equals(_w.IdeItem.Content, connected), "the status item to name the agent");
        await RunAsync(_w, "nks.tools.ideDisconnect");
        Assert.Equal(Loc.Get(K.IdeDisconnected, ("agent", "fake-agent 2.5")), _w.StatusMessageText);
        await Until(() => Equals(_w.IdeItem.Content, Loc.Get(K.IdeStatusWaiting)), "the status item to wait again");
        await Assert.ThrowsAnyAsync<Exception>(() => Pump(agent.RequestAsync("ping", "{}"), "a request after the cut"));

        var again = await ConnectAsync(_w, "fake-agent", "2.5"); // the bridge is still open for the next /ide
        await Until(() => Equals(_w.IdeItem.Content, connected), "the agent to connect again");
        _w.IdeItem.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Equals(_w.IdeItem.Content, Loc.Get(K.IdeStatusWaiting)), "a click on the item to cut the agent off");
        await Assert.ThrowsAnyAsync<Exception>(() => Pump(again.RequestAsync("ping", "{}"), "a request after the click"));
    }

    [AvaloniaFact]
    public async Task Every_window_has_its_own_bridge_and_serves_only_its_own_tabs_and_closing_the_window_closes_its_bridge()
    {
        await SwitchOnServerAsync();
        var second = _app.CreateWindow(null);
        second.Width = 1000;
        second.Height = 700;
        await SwitchOnBridgeAsync(_w);
        await SwitchOnBridgeAsync(second);
        var first = _app.Plugins.Mcp.IdeOf(_w)!;
        var other = _app.Plugins.Mcp.IdeOf(second)!;
        Assert.NotEqual(first.Port, other.Port);
        Assert.NotEqual(first.AuthToken, other.AuthToken);
        Assert.Equal(2, LockFiles().Length);

        var tabOne = Open(_w, "one.txt", "in the first window\n");
        var tabTwo = Open(second, "two.txt", "in the second window\n");
        await ShareAsync(_w, tabOne);
        await ShareAsync(second, tabTwo);

        var agentOne = await ConnectAsync(_w);
        var agentTwo = await ConnectAsync(second);
        string Ids(string json) => string.Join(",", JsonDocument.Parse(json).RootElement.GetProperty("documents").EnumerateArray().Select(d => d.GetProperty("id").GetString()));
        Assert.Equal(tabOne.Doc.Doc.Id.ToString(), Ids((await Pump(agentOne.ToolTextAsync("editor_list_documents"), "list of the first window")).Text));
        Assert.Equal(tabTwo.Doc.Doc.Id.ToString(), Ids((await Pump(agentTwo.ToolTextAsync("editor_list_documents"), "list of the second window")).Text));
        var crossed = await Pump(agentOne.ToolTextAsync("editor_read_range", $"{{\"document\":\"{tabTwo.Doc.Doc.Id}\"}}"), "a read across windows");
        Assert.True(crossed.IsError);
        Assert.DoesNotContain("in the second window", crossed.Text);
        await Assert.ThrowsAsync<WebSocketException>(() => Pump(Agent.ConnectAsync(other.Port, first.AuthToken), "a connection with the token of the other window"));

        string otherLock = other.LockPath;
        second.Close();
        await Until(() => _app.Plugins.Mcp.IdeOf(second) is null && !File.Exists(otherLock), "the bridge of the closed window to go");
        Assert.True(File.Exists(first.LockPath));
        Assert.Equal(tabOne.Doc.Doc.Id.ToString(), Ids((await Pump(agentOne.ToolTextAsync("editor_list_documents"), "list after the other window closed")).Text));
    }

    [AvaloniaFact]
    public async Task Switching_the_server_off_closes_the_bridge_and_cuts_the_agent_off()
    {
        await SwitchOnServerAsync();
        await SwitchOnBridgeAsync(_w);
        var agent = await ConnectAsync(_w);
        Assert.Single(LockFiles());

        await RunAsync(_w, "nks.tools.mcpServer");
        await Until(() => !LockFiles().Any() && _app.Plugins.Mcp.IdeOf(_w) is null, "the bridge to close with the server");
        Assert.False(_w.IdeItem.IsVisible);
        await Assert.ThrowsAnyAsync<Exception>(() => Pump(agent.RequestAsync("ping", "{}"), "a request after the server went off"));
    }

    // ---- what the agent gets ----

    [AvaloniaFact]
    public async Task The_agent_hears_the_selection_of_a_shared_tab_only_and_has_no_active_editor_once_the_user_is_in_another_tab()
    {
        await SwitchOnServerAsync();
        await SwitchOnBridgeAsync(_w);
        var shared = Open(_w, "shared.txt", "alpha beta gamma\n");
        var hidden = Open(_w, "hidden.txt", "secret words\n");
        await ShareAsync(_w, shared);
        shared.Editor!.Select(6, 4);
        Settle();

        var agent = await ConnectAsync(_w);
        var pushed = await Pump(agent.NextNotificationAsync(), "the selection pushed on connect");
        Assert.Equal(IdeBridge.SelectionMethod, pushed.GetProperty("method").GetString());
        Assert.Equal("beta", pushed.GetProperty("params").GetProperty("text").GetString());
        Assert.Equal(shared.Path, pushed.GetProperty("params").GetProperty("filePath").GetString());

        shared.Editor.Select(0, 5);
        var moved = await Pump(agent.NextNotificationAsync(), "the selection of the user");
        Assert.Equal("alpha", moved.GetProperty("params").GetProperty("text").GetString());

        _w.SelectTab(hidden); // not shared: nothing about it is sent, and the active editor is gone
        Settle();
        hidden.Editor!.Select(0, 6);
        Settle();
        var current = JsonDocument.Parse((await Pump(agent.ToolTextAsync("getCurrentSelection"), "the current selection")).Text).RootElement;
        Assert.False(current.GetProperty("success").GetBoolean());
        var latest = JsonDocument.Parse((await Pump(agent.ToolTextAsync("getLatestSelection"), "the latest selection")).Text).RootElement;
        Assert.Equal("alpha", latest.GetProperty("text").GetString()); // the last shared one
        await Quiet();
        var rest = await Pump(agent.ToolTextAsync("editor_list_documents"), "one more answer to collect what was sent meanwhile");
        Assert.DoesNotContain("secret", rest.Text);
        Assert.DoesNotContain(agent.Notifications, n => n.GetRawText().Contains("secret"));
        Assert.Empty(agent.Notifications);
    }

    [AvaloniaFact]
    public async Task Hidden_values_of_an_env_tab_are_not_in_what_the_bridge_tells_the_agent()
    {
        await SwitchOnServerAsync();
        await SwitchOnBridgeAsync(_w);
        var env = Open(_w, ".env", "API_KEY=hunter2\nHOST=example\n");
        _dialogs.Answers.Enqueue(0); // keep the values hidden
        await ShareAsync(_w, env);
        env.Editor!.Select(0, 15);
        Settle();

        var agent = await ConnectAsync(_w);
        var pushed = await Pump(agent.NextNotificationAsync(), "the selection pushed on connect");
        env.Editor.Select(0, 25);
        var moved = await Pump(agent.NextNotificationAsync(), "the selection of the user");
        var answers = new[]
        {
            pushed.GetRawText(), moved.GetRawText(),
            (await Pump(agent.ToolTextAsync("getCurrentSelection"), "the current selection")).Text,
            (await Pump(agent.ToolTextAsync("getLatestSelection"), "the latest selection")).Text,
            (await Pump(agent.ToolTextAsync("getDiagnostics"), "diagnostics")).Text,
        };
        foreach (var answer in answers) Assert.DoesNotContain("hunter2", answer);
        Assert.Contains("API_KEY=", moved.GetRawText());
    }

    // ---- openDiff ----

    [AvaloniaFact]
    public async Task A_diff_the_agent_opens_goes_through_the_change_review_and_the_agent_hears_what_the_user_decided()
    {
        await SwitchOnServerAsync();
        await SwitchOnBridgeAsync(_w);
        var tab = Open(_w, "diff.txt", "alpha beta\n");
        await ShareAsync(_w, tab);
        var agent = await ConnectAsync(_w);

        var applied = agent.ToolTextAsync("openDiff", DiffArgs(tab.Path!, "alpha delta\n", "Rename beta"));
        await Until(() => ReviewShown, "the review");
        Assert.False(applied.IsCompleted); // the agent waits for the user
        Assert.Equal("alpha beta\n", tab.Editor!.Document.Text); // nothing is written before the decision
        _w.ApplyPlan();
        Assert.Equal((false, "FILE_SAVED"), await Pump(applied, "the answer after Apply"));
        Assert.Equal("alpha delta\n", tab.Editor.Document.Text);
        Assert.Equal("alpha beta\n", File.ReadAllText(tab.Path!)); // the bridge never saves
        _w.SelectTab(tab); // the review lives in the Search Results tab
        await _app.Workbench.Commands.ExecuteAsync("nks.edit.undo", new ShellContext(_w, forMenu: true)); // one undo step brings the text back
        Assert.Equal("alpha beta\n", tab.Editor.Document.Text);

        var rejected = agent.ToolTextAsync("openDiff", DiffArgs(tab.Path!, "alpha omega\n", "Rename again"));
        await Until(() => ReviewShown, "the second review");
        foreach (var row in _w.ResultsView!.Rows.OfType<SearchResultsView.HunkRow>()) row.Selected = false;
        _w.ApplyPlan();
        Assert.Equal((false, "DIFF_REJECTED"), await Pump(rejected, "the answer after the rejection"));
        Assert.Equal("alpha beta\n", tab.Editor.Document.Text);

        var same = await Pump(agent.ToolTextAsync("openDiff", DiffArgs(tab.Path!, "alpha beta\n", "No change")), "a diff that changes nothing");
        Assert.Equal((false, "FILE_SAVED"), same);
        Assert.False(ReviewShown);
    }

    [AvaloniaFact]
    public async Task A_diff_the_user_applies_only_in_part_is_an_error_the_agent_can_read_and_only_that_part_is_written()
    {
        await SwitchOnServerAsync();
        await SwitchOnBridgeAsync(_w);
        var tab = Open(_w, "parts.txt", "one\ntwo\nthree\nfour\nfive\nsix\nseven\n");
        await ShareAsync(_w, tab);
        var agent = await ConnectAsync(_w);

        var call = agent.ToolTextAsync("openDiff", DiffArgs(tab.Path!, "ONE\ntwo\nthree\nfour\nfive\nsix\nSEVEN\n", "Two places"));
        await Until(() => ReviewShown, "the review");
        var rows = _w.ResultsView!.Rows.OfType<SearchResultsView.HunkRow>().ToList();
        Assert.Equal(2, rows.Count);
        rows[0].Selected = false;
        _w.ApplyPlan();
        var (isError, text) = await Pump(call, "the answer after a partial Apply");
        Assert.True(isError);
        Assert.Contains("only some of the changes", text);
        Assert.Equal("one\ntwo\nthree\nfour\nfive\nsix\nSEVEN\n", tab.Editor!.Document.Text);
    }

    [AvaloniaFact]
    public async Task A_diff_for_a_tab_that_is_not_shared_a_file_that_is_not_open_or_a_secrets_file_is_refused_before_anything_is_shown()
    {
        await SwitchOnServerAsync();
        await SwitchOnBridgeAsync(_w);
        var hidden = Open(_w, "hidden.txt", "not shared\n");
        var env = Open(_w, ".env", "API_KEY=hunter2\n");
        _dialogs.Answers.Enqueue(0); // keep the values hidden
        await ShareAsync(_w, env);
        var agent = await ConnectAsync(_w);

        var unshared = await Pump(agent.ToolTextAsync("openDiff", DiffArgs(hidden.Path!, "pwned\n", "t1")), "a diff of a tab that is not shared");
        Assert.True(unshared.IsError);
        Assert.Contains("has not shared it", unshared.Text);
        var unknown = await Pump(agent.ToolTextAsync("openDiff", DiffArgs(Path.Combine(_temp.Path, "nowhere.txt"), "pwned\n", "t2")), "a diff of a file that is not open");
        Assert.True(unknown.IsError);
        var secrets = await Pump(agent.ToolTextAsync("openDiff", DiffArgs(env.Path!, "API_KEY=pwned\n", "t3")), "a diff of a secrets file");
        Assert.True(secrets.IsError);
        Assert.Contains("secret", secrets.Text);
        Assert.DoesNotContain("hunter2", secrets.Text);

        Assert.False(ReviewShown);
        Assert.Equal("not shared\n", hidden.Editor!.Document.Text);
        Assert.Equal("API_KEY=hunter2\n", env.Editor!.Document.Text);
        Assert.Equal("not shared\n", File.ReadAllText(hidden.Path!));
    }

    [AvaloniaFact]
    public async Task Closing_the_tab_of_the_diff_from_another_connection_ends_the_review_as_rejected()
    {
        await SwitchOnServerAsync();
        await SwitchOnBridgeAsync(_w);
        var tab = Open(_w, "closing.txt", "alpha beta\n");
        await ShareAsync(_w, tab);
        var waiting = await ConnectAsync(_w, "waiting-agent");
        var closer = await ConnectAsync(_w, "closing-agent");

        var call = waiting.ToolTextAsync("openDiff", DiffArgs(tab.Path!, "alpha delta\n", "Tab A"));
        await Until(() => ReviewShown, "the review");
        Assert.Equal((false, "TAB_CLOSED"), await Pump(closer.ToolTextAsync("close_tab", "{\"tab_name\":\"Tab A\"}"), "close_tab"));
        Assert.Equal((false, "DIFF_REJECTED"), await Pump(call, "the answer after the tab was closed"));
        await Until(() => !ReviewShown, "the review to close");
        Assert.Equal("alpha beta\n", tab.Editor!.Document.Text);
    }

    // ---- an agent that changed ----

    [AvaloniaFact]
    public async Task An_agent_that_calls_a_tool_nobody_documents_switches_the_bridge_of_its_window_off_and_the_user_is_told()
    {
        await SwitchOnServerAsync();
        var second = _app.CreateWindow(null);
        await SwitchOnBridgeAsync(_w);
        await SwitchOnBridgeAsync(second);
        var agent = await ConnectAsync(_w);
        Assert.Equal(2, LockFiles().Length);

        // the bridge goes off as soon as it sees the call, so the agent gets either the error or a closed connection
        var closed = await Record.ExceptionAsync(async () => Assert.True((await Pump(agent.RequestAsync("tools/call", "{\"name\":\"someNewTool\",\"arguments\":{}}"), "the call of an unknown tool")).TryGetProperty("error", out _)));
        if (closed is not null) Assert.IsType<WebSocketException>(closed);
        await Until(() => _app.Plugins.Mcp.IdeOf(_w) is null && !_w.IdeItem.IsVisible && LockFiles().Length == 1, "the bridge of the window to go off and take its lock file");
        await Until(() => _w.Notices.Any(n => n.Message.Contains("someNewTool", StringComparison.Ordinal)), "the notice");
        Assert.NotNull(_app.Plugins.Mcp.IdeOf(second)); // the other window is not affected
        Assert.True(second.IdeItem.IsVisible);
        Assert.DoesNotContain(second.Notices, n => n.Message.Contains("someNewTool", StringComparison.Ordinal));
    }
}
