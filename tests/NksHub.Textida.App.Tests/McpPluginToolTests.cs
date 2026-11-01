using System.Diagnostics;
using System.IO.Pipelines;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.PluginHost.Mcp;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// PZ.10.1: a command of an installed plugin offered as an MCP tool. The plugin is the test plugin process, installed with the consent of the user; the tool shows only to a client the user allowed and only while the
/// plugin holds the <c>mcp.tool</c> permission, and a call reaches the command with the arguments the assistant gave (and the document, when the user shared it).
/// </summary>
[Collection("LocState")]
public class McpPluginToolTests : IDisposable
{
    private const string Meta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"probe-client\"}}";
    private const string Plugin = "acme.assist";

    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public McpPluginToolTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static async Task Until(Func<bool> condition, string what)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < 30_000, "Not reached: " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private static async Task<T> Pump<T>(Task<T> task)
    {
        await Until(() => task.IsCompleted, "a call");
        return await task;
    }

    private string Zip(string[] permissions, string tools)
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        var executable = host ?? Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.PluginHost.TestPlugin" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        string args = host is null ? "[]" : JsonSerializer.Serialize(new[] { Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.PluginHost.TestPlugin.dll") });
        var path = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N") + ".zip");
        using (var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry(PluginManifest.FileName).Open()))
            writer.Write("{\"schemaVersion\":1,\"id\":\"" + Plugin + "\",\"name\":\"Acme assist\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1},"
                + "\"permissions\":[" + string.Join(",", permissions.Select(p => "\"" + p + "\"")) + "],"
                + "\"main\":{\"executable\":" + JsonSerializer.Serialize(executable) + ",\"args\":" + args + "},"
                + "\"contributes\":{\"commands\":[{\"id\":\"acme.assist.count\",\"title\":\"Count\"}]" + tools + "}}");
        return path;
    }

    private const string CountTool = ",\"mcpTools\":[{\"id\":\"count\",\"title\":\"Count words\",\"description\":\"Counts the words of the text given.\",\"command\":\"acme.assist.count\",\"readOnly\":true,"
        + "\"inputSchema\":{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\"}},\"required\":[\"text\"],\"additionalProperties\":false}}]";

    /// <summary>A client that speaks to the server of the app over two pipes, as a line of JSON per message.</summary>
    private sealed class Client : IAsyncDisposable
    {
        private readonly Pipe _toServer = new(), _fromServer = new();
        private readonly StreamWriter _write;
        private readonly StreamReader _read;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serving;
        private int _next;

        public Client(McpServer server)
        {
            _write = new StreamWriter(_toServer.Writer.AsStream(), new UTF8Encoding(false)) { AutoFlush = true };
            _read = new StreamReader(_fromServer.Reader.AsStream(), Encoding.UTF8);
            _serving = server.ServeAsync(_toServer.Reader.AsStream(), _fromServer.Writer.AsStream(), _stop.Token);
        }

        public async Task<JsonElement> RequestAsync(string method, string paramsJson)
        {
            await _write.WriteLineAsync($"{{\"jsonrpc\":\"2.0\",\"id\":{++_next},\"method\":\"{method}\",\"params\":{paramsJson}}}");
            return JsonDocument.Parse((await _read.ReadLineAsync())!).RootElement.Clone();
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            try { await _serving.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception e) when (e is TimeoutException or OperationCanceledException) { }
        }
    }

    private static IEnumerable<string> NamesOf(JsonElement reply) => reply.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()!);

    [AvaloniaFact]
    public async Task A_manifest_with_tools_but_without_the_permission_or_the_command_is_refused()
    {
        _dialogs.Answers.Enqueue(0);
        Assert.False(await Pump(_w.InstallPluginAsync(Zip([], CountTool))));
        Assert.Contains(Assert.Single(_dialogs.Asked).Details!, d => d.Contains("mcpTools need the permission", StringComparison.Ordinal));

        _dialogs.Answers.Enqueue(0);
        Assert.False(await Pump(_w.InstallPluginAsync(Zip([DocumentPermissions.McpTool], CountTool.Replace("\"command\":\"acme.assist.count\"", "\"command\":\"acme.assist.other\"")))));
        Assert.Contains(_dialogs.Asked[1].Details!, d => d.Contains("is not a command of this plugin", StringComparison.Ordinal));
        Assert.Empty(_app.Plugins.Manager.Installed());
    }

    [AvaloniaFact]
    public async Task The_tool_is_offered_to_an_allowed_client_while_the_permission_stands_and_a_call_reaches_the_command_with_the_arguments()
    {
        _dialogs.Answers.Enqueue(0);
        Assert.True(await Pump(_w.InstallPluginAsync(Zip([DocumentPermissions.McpTool, DocumentPermissions.Read], CountTool))));
        Assert.True(_app.Plugins.Permissions.IsGranted(Plugin, DocumentPermissions.McpTool, GrantScope.All));
        var mcp = _app.Plugins.Mcp;

        await using var client = new Client(mcp.Server);
        // a client nobody decided about sees nothing, and a call is refused
        var unknown = await Pump(client.RequestAsync("tools/list", "{" + Meta + "}"));
        Assert.DoesNotContain("plugin_count", NamesOf(unknown));
        var refused = await Pump(client.RequestAsync("tools/call", "{" + Meta + ",\"name\":\"plugin_count\",\"arguments\":{\"text\":\"a b\"}}"));
        Assert.True(refused.GetProperty("result").GetProperty("isError").GetBoolean());

        mcp.ClientDecided("probe-client", true);
        var listed = await Pump(client.RequestAsync("tools/list", "{" + Meta + "}"));
        Assert.Contains("plugin_count", NamesOf(listed));
        var tool = listed.GetProperty("result").GetProperty("tools").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "plugin_count");
        Assert.True(tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
        var properties = tool.GetProperty("inputSchema").GetProperty("properties");
        Assert.True(properties.TryGetProperty("text", out _) && properties.TryGetProperty("document", out _));

        // a call without a document runs the command in the plugin process with the arguments
        var called = await Pump(client.RequestAsync("tools/call", "{" + Meta + ",\"name\":\"plugin_count\",\"arguments\":{\"text\":\"one two three\"}}"));
        Assert.False(called.GetProperty("result").GetProperty("isError").GetBoolean(), called.GetRawText());
        var seen = await Pump(CommandsOfPluginAsync());
        var run = seen.EnumerateArray().Last().GetProperty("params");
        Assert.Equal(("acme.assist.count", "one two three"), (run.GetProperty("command").GetString(), run.GetProperty("arguments").GetProperty("text").GetString()));
        Assert.False(run.TryGetProperty("document", out _));

        // a document that is not shared is refused; one that is, is the document of the command
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = "hello";
        string id = tab.Doc.Doc.Id.ToString();
        var notShared = await Pump(client.RequestAsync("tools/call", "{" + Meta + ",\"name\":\"plugin_count\",\"arguments\":{\"text\":\"x\",\"document\":\"" + id + "\"}}"));
        Assert.True(notShared.GetProperty("result").GetProperty("isError").GetBoolean());
        mcp.Share(_w, tab, revealSecrets: false);
        var shared = await Pump(client.RequestAsync("tools/call", "{" + Meta + ",\"name\":\"plugin_count\",\"arguments\":{\"text\":\"x\",\"document\":\"" + id + "\"}}"));
        Assert.False(shared.GetProperty("result").GetProperty("isError").GetBoolean(), shared.GetRawText());
        run = (await Pump(CommandsOfPluginAsync())).EnumerateArray().Last().GetProperty("params");
        Assert.Equal((id, "x"), (run.GetProperty("document").GetString(), run.GetProperty("arguments").GetProperty("text").GetString()));
        Assert.False(run.GetProperty("arguments").TryGetProperty("document", out _)); // the plugin sees the document in the request, not among the arguments

        // the user takes the permission back: the tool is gone from the list and a call is refused
        _app.Plugins.Permissions.Revoke(Plugin, DocumentPermissions.McpTool, GrantScope.All);
        Assert.DoesNotContain("plugin_count", NamesOf(await Pump(client.RequestAsync("tools/list", "{" + Meta + "}"))));
        var gone = await Pump(client.RequestAsync("tools/call", "{" + Meta + ",\"name\":\"plugin_count\",\"arguments\":{\"text\":\"x\"}}"));
        Assert.True(gone.TryGetProperty("error", out _));

        // a client the user does not allow (any more) sees nothing either
        _app.Plugins.Permissions.Grant(Plugin, DocumentPermissions.McpTool, GrantScope.All);
        Assert.Contains("plugin_count", NamesOf(await Pump(client.RequestAsync("tools/list", "{" + Meta + "}"))));
        mcp.ClientDecided("probe-client", false);
    }

    private async Task<JsonElement> CommandsOfPluginAsync() =>
        (await _app.Plugins.HostForTests.CallAsync(Plugin, "commands", Encoding.UTF8.GetBytes("{}"))).Clone();
}
