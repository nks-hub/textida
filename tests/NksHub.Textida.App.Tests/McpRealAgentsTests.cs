using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Tests.RealAgents;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P6.3.4: the MCP server of a running window against the real clients. The window is the real one, as in <see cref="McpAppTests"/>; the other side is Claude Code in print mode (the server is registered with
/// <c>--mcp-config</c>) or Codex (the server is registered in the <c>config.toml</c> of a temporary <c>CODEX_HOME</c>). Both start the real <c>textida-mcp</c> program. The checks: a client the user has not
/// allowed gets nothing, the shared tabs are listed, an unsaved tab is read, a prepared change waits in the review and writes nothing until the user decides, a tab that is not shared stays invisible,
/// <c>.env</c> arrives masked, and taking the permission back stops further reads. The tools each client really called are written to the test output with its version.
/// </summary>
[Collection("LocState")]
[Trait("Category", "RealAgents")]
public class McpRealAgentsTests : IDisposable
{
    private static readonly string BridgeDll = Path.Combine(AppContext.BaseDirectory, "mcp", "textida-mcp.dll");
    private static readonly string Dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "textida-realagents", "mcp-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<string> _asked = [];

    public McpRealAgentsTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _w.Width = 1000;
        _w.Height = 700;
        Directory.CreateDirectory(_folder);
        _app.Plugins.Mcp.ClientAsked += _asked.Add;
    }

    public void Dispose()
    {
        try { _app.Plugins.Mcp.DisableAsync().Wait(TimeSpan.FromSeconds(5)); } catch (Exception e) when (e is AggregateException or TimeoutException) { }
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
        try { Directory.Delete(_folder, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static async Task Until(Func<bool> condition, string what, int seconds = 30)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < seconds * 1000, "Not reached: " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }

    private async Task RunAsync(string command)
    {
        var run = _app.Workbench.Commands.ExecuteAsync(command, new ShellContext(_w, forMenu: false)).AsTask();
        await Until(() => run.IsCompleted, command);
        Assert.Equal(CommandOutcome.Handled, await run);
        await Until(() => _w.McpTask.IsCompleted, "the MCP command to finish");
        await _w.McpTask;
    }

    private DocTab Open(string name, string content)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, content);
        var tab = _w.OpenFile(path, null);
        _w.UpdateLayout();
        for (int i = 0; i < 20; i++) Dispatcher.UIThread.RunJobs();
        return tab;
    }

    private async Task ShareAsync(DocTab tab)
    {
        _w.SelectTab(tab);
        await RunAsync("nks.tools.mcpShareTab");
    }

    private static string Tail(string output) => output.Length <= 1500 ? output : "..." + output[^1500..];

    private bool ReviewShown => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true;

    /// <summary>Runs a client program while the window keeps working (the preview of a proposal and the questions of the server need its thread).</summary>
    private async Task<(int ExitCode, string Output, bool TimedOut)> WhileRunning(Task<(int ExitCode, string Output, bool TimedOut)> client)
    {
        while (!client.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        return await client;
    }

    /// <summary>The tools of this server that the JSON output of a client says it called: a <c>tool_use</c> of Claude Code, an <c>mcp_tool_call</c> of Codex. The list of available tools is not a call.</summary>
    private static List<string> ToolsCalled(string output) =>
        [.. Regex.Matches(output, "\"type\":\"tool_use\",\"id\":\"[^\"]+\",\"name\":\"mcp__textida__(editor_[a-z_]+)\"|\"server\":\"textida\",\"tool\":\"(editor_[a-z_]+)\"")
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Distinct().Order()];

    private async Task<(DocTab Shared, DocTab Env, DocTab Other)> PrepareTabsAsync()
    {
        _dialogs.Answers.Enqueue(0);
        await RunAsync("nks.tools.mcpServer");
        Assert.True(_app.Plugins.Mcp.Enabled);
        var shared = Open("shared.txt", "alpha beta gamma\n");
        var other = Open("other.txt", "never shared secret-plan\n");
        var env = Open(".env", "API_KEY=hunter2hunter2\nHOST=example\n");
        shared.Editor!.Document.Text = "unsaved alpha beta gamma\n"; // only in the editor, not on disk
        for (int i = 0; i < 20; i++) Dispatcher.UIThread.RunJobs();
        await ShareAsync(shared);
        _dialogs.Answers.Enqueue(0); // the values of the env tab stay hidden
        await ShareAsync(env);
        return (shared, env, other);
    }

    private const string ListPrompt = "Call the tool editor_list_documents of the textida MCP server and quote its answer verbatim. Use no other tool. If the tool does not exist, say so in one sentence and stop.";
    private const string ReadPrompt = "Call the tool editor_list_documents of the textida MCP server, then editor_read_range for every document it lists, and quote the text of each document verbatim with its title. Use no other tool. If the tools do not exist, say so in one sentence and stop.";

    // ---- Claude Code ----

    private async Task<(int ExitCode, string Output, bool TimedOut)> ClaudeAsync(string claude, string prompt)
    {
        string config = Path.Combine(_temp.Path, "mcp.json");
        File.WriteAllText(config, System.Text.Json.JsonSerializer.Serialize(new
        {
            mcpServers = new { textida = new { command = Dotnet, args = new[] { BridgeDll }, env = new Dictionary<string, string> { ["TEXTIDA_CHANNEL"] = App.Startup.Channel } } },
        }));
        // a run of the program that hangs after its first model turn (it happens now and then, with no tools in the first run) is not the subject of the test: it is repeated once
        (int ExitCode, string Output, bool TimedOut) run = default;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            run = await WhileRunning(RealTools.RunAsync(claude, ["-p", "--output-format", "stream-json", "--verbose", "--mcp-config", config, "--strict-mcp-config", "--tools", "",
                "--allowedTools", "mcp__textida", "--permission-mode", "default", "--max-turns", "6", "--model", "haiku", "--no-session-persistence", "--max-budget-usd", "0.5", prompt], _folder, TimeSpan.FromMinutes(2)));
            if (!run.TimedOut) break;
        }
        return run;
    }

    [AvaloniaFact]
    public async Task Claude_Code_sees_nothing_until_the_user_allows_it_then_lists_reads_and_prepares_a_change_that_waits_and_loses_access_when_the_permission_is_taken_back()
    {
        var claude = RealTools.Require("claude");
        string version = RealTools.VersionLine(claude) ?? "";
        var (shared, env, other) = await PrepareTabsAsync();
        var called = new SortedSet<string>();

        // a client the user has not decided about: it connects, the editor asks the user, and the client sees no tool
        var first = await ClaudeAsync(claude, ListPrompt);
        Assert.False(first.TimedOut, Tail(first.Output));
        string name = Assert.Single(_asked);
        Assert.Contains("claude", name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shared.txt", first.Output);
        Assert.Empty(ToolsCalled(first.Output));
        Assert.DoesNotContain(name, _app.Plugins.Mcp.Clients.Decisions.Keys);

        // the user allows it: the shared tabs are listed, the unsaved text is read, the tab that is not shared is invisible and the values of .env are masked
        _app.Plugins.Mcp.ClientDecided(name, true);
        var second = await ClaudeAsync(claude, ReadPrompt);
        Assert.False(second.TimedOut, Tail(second.Output));
        Assert.Contains("shared.txt", second.Output);
        Assert.Contains("unsaved alpha beta gamma", second.Output);
        Assert.Contains(".env", second.Output);
        Assert.DoesNotContain("other.txt", second.Output.Replace(ReadPrompt, ""));
        Assert.DoesNotContain("secret-plan", second.Output);
        Assert.DoesNotContain("hunter2hunter2", second.Output);
        called.UnionWith(ToolsCalled(second.Output));
        Assert.Contains("editor_list_documents", called);
        Assert.Contains("editor_read_range", called);

        // a prepared change is shown in the review and nothing is written before the user decides
        var third = await ClaudeAsync(claude, $"Call the tool editor_list_documents of the textida MCP server, take the document titled shared.txt and its revision, then call editor_prepare_changes to replace the word beta with the word delta in it. Use no other tool.");
        Assert.False(third.TimedOut, Tail(third.Output));
        called.UnionWith(ToolsCalled(third.Output));
        Assert.Contains("editor_prepare_changes", called);
        await Until(() => ReviewShown, "the review of the prepared change");
        Assert.Equal("unsaved alpha beta gamma\n", shared.Editor!.Document.Text);
        Assert.Equal("alpha beta gamma\n", File.ReadAllText(shared.Path!));
        _w.ApplyPlan();
        await Until(() => shared.Editor.Document.Text == "unsaved alpha delta gamma\n", "the change to be applied after the decision of the user");
        Assert.Equal("alpha beta gamma\n", File.ReadAllText(shared.Path!)); // applied to the tab, not written to the file

        // the user takes the permission back: the client is cut off and the next run reads nothing
        _app.Plugins.Mcp.ClientDecided(name, false);
        var fourth = await ClaudeAsync(claude, ReadPrompt);
        Assert.DoesNotContain("unsaved alpha delta gamma", fourth.Output);
        Assert.DoesNotContain("shared.txt", fourth.Output);
        TestContext.Current.SendDiagnosticMessage($"{version}: client name '{name}', tools called: {string.Join(", ", called)}");
    }

    // ---- Codex ----

    private string CodexHome(string codexRealHome)
    {
        string home = Path.Combine(_temp.Path, "codex-home");
        Directory.CreateDirectory(home);
        // the login of the user is copied into the temporary home; nothing is written back
        string auth = Path.Combine(codexRealHome, "auth.json");
        if (File.Exists(auth)) File.Copy(auth, Path.Combine(home, "auth.json"), true);
        string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        File.WriteAllText(Path.Combine(home, "config.toml"),
            $"[mcp_servers.textida]\ncommand = \"{Esc(Dotnet)}\"\nargs = [\"{Esc(BridgeDll)}\"]\nenv = {{ TEXTIDA_CHANNEL = \"{Esc(App.Startup.Channel)}\" }}\n");
        return home;
    }

    private async Task<(int ExitCode, string Output, bool TimedOut)> CodexAsync(string codex, string home, string prompt)
    {
        var info = RealTools.StartInfo(codex, ["exec", "--skip-git-repo-check", "--ephemeral", "--ignore-rules", "--json", "-s", "read-only", "-c", "model_reasoning_effort=low", "-C", _folder, prompt], _folder);
        info.Environment["CODEX_HOME"] = home;
        using var process = Process.Start(info)!;
        process.StandardInput.Close();
        var output = new StringBuilder();
        var pumps = new[]
        {
            Task.Run(async () => { string? l; while ((l = await process.StandardOutput.ReadLineAsync()) is not null) lock (output) output.AppendLine(l); }),
            Task.Run(async () => { string? l; while ((l = await process.StandardError.ReadLineAsync()) is not null) lock (output) output.AppendLine(l); }),
        };
        var sw = Stopwatch.StartNew();
        while (!process.HasExited && sw.Elapsed < TimeSpan.FromMinutes(5))
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        bool timedOut = !process.HasExited;
        if (timedOut) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
        await Task.WhenAll(pumps);
        return (timedOut ? -1 : process.ExitCode, output.ToString(), timedOut);
    }

    [AvaloniaFact]
    public async Task Codex_sees_nothing_until_the_user_allows_it_then_lists_and_reads_the_shared_tabs_and_loses_access_when_the_permission_is_taken_back()
    {
        var codex = RealTools.Require("codex");
        string version = RealTools.VersionLine(codex) ?? "";
        string home = CodexHome(Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")));
        var (shared, env, other) = await PrepareTabsAsync();

        var first = await CodexAsync(codex, home, ListPrompt);
        Assert.SkipWhen(first.Output.Contains("usage limit", StringComparison.OrdinalIgnoreCase), "the account of Codex has reached its usage limit; the test needs a working login");
        Assert.False(first.TimedOut, Tail(first.Output));
        string name = Assert.Single(_asked);
        Assert.Contains("codex", name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shared.txt", first.Output);

        _app.Plugins.Mcp.ClientDecided(name, true);
        var second = await CodexAsync(codex, home, ReadPrompt);
        Assert.False(second.TimedOut, Tail(second.Output));
        Assert.Contains("shared.txt", second.Output);
        Assert.Contains("unsaved alpha beta gamma", second.Output);
        Assert.DoesNotContain("secret-plan", second.Output);
        Assert.DoesNotContain("hunter2hunter2", second.Output);
        var called = ToolsCalled(second.Output);
        Assert.Contains("editor_list_documents", called);

        var third = await CodexAsync(codex, home, "Call the tool editor_list_documents of the textida MCP server, take the document titled shared.txt and its revision, then call editor_prepare_changes to replace the word beta with the word delta in it. Use no other tool.");
        Assert.False(third.TimedOut, Tail(third.Output));
        called = [.. called.Union(ToolsCalled(third.Output)).Order()];
        await Until(() => ReviewShown, "the review of the prepared change");
        Assert.Equal("alpha beta gamma\n", File.ReadAllText(shared.Path!));
        Assert.Equal("unsaved alpha beta gamma\n", shared.Editor!.Document.Text);

        _app.Plugins.Mcp.ClientDecided(name, false);
        var fourth = await CodexAsync(codex, home, ReadPrompt);
        Assert.DoesNotContain("unsaved alpha", fourth.Output);
        TestContext.Current.SendDiagnosticMessage($"{version}: client name '{name}', tools called: {string.Join(", ", called)}");
    }
}
