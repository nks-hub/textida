using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P6.4.2 through the window: one list of the servers in the files of the agents (fixtures in a temp folder, never the real ones), Try that shows the exact command with hidden
/// variables and starts nothing until the user agrees, and a conversion that opens a snippet in a new tab and writes to no file.
/// </summary>
[Collection("LocState")]
public class McpAgentsAppTests : IDisposable
{
    private const string Secret = "s3cr3t-value-xyz";
    private static readonly string EchoDll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.ToolEcho.dll");

    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly string _home;
    private readonly string _marker;

    public McpAgentsAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _w.Width = 1000;
        _w.Height = 700;
        _home = Path.Combine(_temp.Path, "home");
        _marker = Path.Combine(_temp.Path, "started.marker");
        _app.AgentConfigHome = _home;
        var dotnet = JsonSerializer.Serialize(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet");
        Directory.CreateDirectory(_home);
        File.WriteAllText(Path.Combine(_home, ".claude.json"), $$"""
            { "mcpServers": {
              "fake": { "command": {{dotnet}}, "args": [{{JsonSerializer.Serialize(EchoDll)}}, "--mcp", "modern"], "env": { "TEST_SECRET": "{{Secret}}", "TEST_MARKER": {{JsonSerializer.Serialize(_marker)}} } },
              "broken": { "command": {{dotnet}}, "args": [{{JsonSerializer.Serialize(EchoDll)}}, "--both"] },
              "remote": { "type": "http", "url": "https://example.com/mcp?key={{Secret}}" }
            } }
            """);
    }

    public void Dispose()
    {
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

    private Task OpenListAsync() => _app.Workbench.Commands.ExecuteAsync("nks.tools.mcpServers", new ShellContext(_w, forMenu: false)).AsTask();

    private async Task WaitForChoiceAsync(string what, int rows)
    {
        await Until(() => _w.IsPaletteOpen && _w.PaletteModeNow == PaletteMode.Choice && _w.QuickResults.Count == rows, what);
    }

    private static string HashOf(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    [AvaloniaFact]
    public async Task The_list_shows_every_server_with_its_agent_and_never_a_secret()
    {
        await OpenListAsync();
        await WaitForChoiceAsync("the list", 3);
        Assert.Equal(["fake", "broken", "remote"], _w.QuickResults.Select(r => r.Title));
        Assert.All(_w.QuickResults, r => Assert.Contains("Claude Code, .claude.json: ", r.Detail));
        Assert.DoesNotContain(_w.QuickResults, r => (r.Detail ?? "").Contains(Secret));
        Assert.Equal("Claude Code, .claude.json: https://example.com/mcp?key=***", _w.QuickResults[2].Detail);
        _w.ClosePalette();
        await Until(() => _w.McpTask.IsCompleted, "the list to end");
        Assert.Empty(_dialogs.Asked);
        Assert.False(File.Exists(_marker));
    }

    [AvaloniaFact]
    public async Task Try_shows_the_exact_command_with_hidden_values_and_starts_nothing_without_a_yes()
    {
        await OpenListAsync();
        await WaitForChoiceAsync("the list", 3);
        _dialogs.Answers.Enqueue(1); // Cancel
        _w.RunPaletteSelection(); // fake
        await WaitForChoiceAsync("the actions", 4);
        Assert.Equal(Loc.Get(K.McpAgentsTry), _w.QuickResults[0].Title);
        _w.RunPaletteSelection(); // Try
        await Until(() => _dialogs.Asked.Count == 1, "the question");
        await WaitForChoiceAsync("the list again", 3);

        var asked = _dialogs.Asked[0];
        Assert.Contains(EchoDll, asked.Details!.First(d => d.StartsWith(Loc.Get(K.McpAgentsDetailCommand, ("command", "")), StringComparison.Ordinal)));
        Assert.Contains("TEST_SECRET=***", asked.Details);
        Assert.DoesNotContain(asked.Details, d => d.Contains(Secret));
        Assert.DoesNotContain(Secret, asked.Message + asked.Title);
        await Task.Delay(500);
        Assert.False(File.Exists(_marker), "the program must not run before the user said yes");
        Assert.Empty(_app.Output.Read(OutputChannel.Tasks));
        _w.ClosePalette();
        await Until(() => _w.McpTask.IsCompleted, "the list to end");
    }

    [AvaloniaFact]
    public async Task Try_after_a_yes_counts_the_tools_of_a_real_server_and_the_output_never_holds_the_secret()
    {
        await OpenListAsync();
        await WaitForChoiceAsync("the list", 3);
        _dialogs.Answers.Enqueue(0); // Start
        _dialogs.Answers.Enqueue(0); // Close
        _w.RunPaletteSelection();
        await WaitForChoiceAsync("the actions", 4);
        _w.RunPaletteSelection();
        await Until(() => _dialogs.Asked.Count == 2, "the result");
        await WaitForChoiceAsync("the list again", 3);

        var result = _dialogs.Asked[1];
        Assert.Equal(Loc.Get(K.McpAgentsResultOk, ("name", "fake"), ("count", "5"), ("version", "2026-07-28")), result.Message);
        Assert.Equal(["echo", "slow", "crash", "env", "secret"], result.Details);
        Assert.True(File.Exists(_marker));
        var lines = _app.Output.Read(OutputChannel.Tasks);
        Assert.Contains(lines, l => l.Text == "era:modern"); // what the server wrote to its standard error
        Assert.DoesNotContain(lines, l => l.Text.Contains(Secret) || l.Source.Contains(Secret));
        _w.ClosePalette();
        await Until(() => _w.McpTask.IsCompleted, "the list to end");
    }

    [AvaloniaFact]
    public async Task Try_reports_the_stderr_of_a_program_that_is_not_a_server()
    {
        await OpenListAsync();
        await WaitForChoiceAsync("the list", 3);
        _dialogs.Answers.Enqueue(0);
        _dialogs.Answers.Enqueue(0);
        _w.MovePalette(1); // broken
        _w.RunPaletteSelection();
        await WaitForChoiceAsync("the actions", 4);
        _w.RunPaletteSelection();
        await Until(() => _dialogs.Asked.Count == 2, "the result");

        var result = _dialogs.Asked[1];
        Assert.Contains("exit code 0", result.Message);
        Assert.Contains("to-err", result.Message);
        Assert.Contains("to-err", result.Details!);
        Assert.Contains(_app.Output.Read(OutputChannel.Tasks), l => l.Text == "to-err");
        await WaitForChoiceAsync("the list again", 3);
        _w.ClosePalette();
        await Until(() => _w.McpTask.IsCompleted, "the list to end");
    }

    [AvaloniaFact]
    public async Task A_server_reached_by_an_address_is_listed_but_not_started()
    {
        await OpenListAsync();
        await WaitForChoiceAsync("the list", 3);
        _w.MovePalette(2); // remote
        _w.RunPaletteSelection();
        await WaitForChoiceAsync("the actions", 4);
        _w.RunPaletteSelection();
        await WaitForChoiceAsync("the list again", 3);
        Assert.Equal(Loc.Get(K.McpAgentsRemote, ("name", "remote")), _w.StatusMessageText);
        Assert.Empty(_dialogs.Asked);
        _w.ClosePalette();
        await Until(() => _w.McpTask.IsCompleted, "the list to end");
    }

    [AvaloniaFact]
    public async Task Converting_opens_a_checked_snippet_in_a_new_tab_with_hidden_values_and_writes_to_no_file()
    {
        var file = Path.Combine(_home, ".claude.json");
        var before = (HashOf(file), File.GetLastWriteTimeUtc(file));
        await OpenListAsync();
        await WaitForChoiceAsync("the list", 3);
        _w.RunPaletteSelection(); // fake
        await WaitForChoiceAsync("the actions", 4);
        Assert.Equal(Loc.Get(K.McpAgentsConvert, ("agent", "Codex")), _w.QuickResults[2].Title);
        _w.MovePalette(2);
        _w.RunPaletteSelection(); // Convert to Codex
        await Until(() => _w.McpTask.IsCompleted, "the conversion");

        var text = _w.Active!.Editor!.Document.Text;
        Assert.Contains("[mcp_servers.fake]", text);
        Assert.Contains("[mcp_servers.fake.env]", text);
        Assert.Contains("TEST_SECRET = \"***\"", text);
        Assert.DoesNotContain(Secret, text);
        Assert.Empty(NksHub.Textida.McpClient.McpConfig.Check(NksHub.Textida.McpClient.McpAgent.Codex, text));
        Assert.Equal(Loc.Get(K.McpAgentsConverted, ("agent", "Codex"), ("file", "~/.codex/config.toml")), _w.StatusMessageText);
        Assert.Equal(before, (HashOf(file), File.GetLastWriteTimeUtc(file)));
        Assert.Equal([".claude.json"], Directory.EnumerateFileSystemEntries(_home).Select(Path.GetFileName));
        Assert.Empty(_dialogs.Asked);
    }

    [AvaloniaFact]
    public async Task A_variable_that_only_the_credential_store_knows_is_used_and_stays_hidden()
    {
        // ${...} references are filled from the environment, then from the Credential Manager entry textida:mcp:env:NAME
        File.WriteAllText(Path.Combine(_home, ".claude.json"), $$"""
            { "mcpServers": { "viaStore": { "command": {{JsonSerializer.Serialize(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")}},
              "args": [{{JsonSerializer.Serialize(EchoDll)}}, "--mcp", "modern"], "env": { "TEST_SECRET": "${TEXTIDA_TEST_STORE_ONLY}", "TEST_MARKER": {{JsonSerializer.Serialize(_marker)}} } } } }
            """);
        var store = new FakeCredentials { [MainWindow.McpEnvCredentialPrefix + "TEXTIDA_TEST_STORE_ONLY"] = Secret };
        _app.Credentials = store;
        await OpenListAsync();
        await WaitForChoiceAsync("the list", 1);
        _dialogs.Answers.Enqueue(0);
        _dialogs.Answers.Enqueue(0);
        _w.RunPaletteSelection();
        await WaitForChoiceAsync("the actions", 4);
        _w.RunPaletteSelection();
        await Until(() => _dialogs.Asked.Count == 2, "the result");
        Assert.Contains("TEST_SECRET=***", _dialogs.Asked[0].Details!); // the value came from the store and is shown hidden, as the start will use it
        Assert.DoesNotContain(_dialogs.Asked.SelectMany(a => a.Details ?? []).Append(_dialogs.Asked[1].Message), s => s.Contains(Secret));
        Assert.Equal(Loc.Get(K.McpAgentsResultOk, ("name", "viaStore"), ("count", "5"), ("version", "2026-07-28")), _dialogs.Asked[1].Message);
        await WaitForChoiceAsync("the list again", 1);
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
