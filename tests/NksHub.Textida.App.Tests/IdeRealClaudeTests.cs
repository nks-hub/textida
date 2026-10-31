using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Tests.RealAgents;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost.Mcp;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P6.5.1 against the real Claude Code: the interactive program (on a pseudo console, with <c>--ide</c>) connects to the bridge of a window of the editor through the lock file in the folder
/// of its own configuration, and the diff it opens waits in the change review for the user. The window is the real one, as in <see cref="IdeAppTests"/>; the other side is the program.
/// Needs <c>claude</c> in PATH and its login; two or three short prompts go to the cheapest model. The program works in a folder of its own below the temporary folder, never in a repository.
/// </summary>
[Collection("LocState")]
[Trait("Category", "RealAgents")]
public class IdeRealClaudeTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "textida-realagents", "claude-ide");

    public IdeRealClaudeTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _w.Width = 1000;
        _w.Height = 700;
        // a stable folder: the program asks once whether it may trust a folder and remembers the answer for its path
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        var off = _app.Plugins.Mcp.DisableAsync();
        for (var sw = Stopwatch.StartNew(); !off.IsCompleted && sw.ElapsedMilliseconds < 5000;)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
        try { Directory.Delete(_folder, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static Task Pump(Func<bool> done, TimeSpan timeout, string what) => Pump(done, timeout, () => what);

    private static async Task Pump(Func<bool> done, TimeSpan timeout, Func<string> what)
    {
        for (var sw = Stopwatch.StartNew(); !done();)
        {
            Assert.True(sw.Elapsed < timeout, "Not reached: " + what());
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(30);
        }
    }

    private static async Task Wait(TimeSpan time)
    {
        for (var sw = Stopwatch.StartNew(); sw.Elapsed < time;)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(30);
        }
    }

    private bool ReviewShown => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true;

    private async Task<IdeBridge> StartBridgeAsync(string fileName, string content)
    {
        _app.Plugins.Mcp.Enable();
        _dialogs.Answers.Enqueue(0);
        _w.ToggleIdeBridge();
        await _w.McpTask;
        var path = Path.Combine(_folder, fileName);
        File.WriteAllText(path, content);
        var tab = _w.OpenFile(path, null);
        _w.UpdateLayout();
        _w.SelectTab(tab);
        _w.ShareTabWithMcp();
        await _w.McpTask;
        var bridge = _app.Plugins.Mcp.IdeOf(_w) ?? throw new InvalidOperationException("The bridge did not start.");
        Assert.StartsWith(IdeBridge.DefaultLockDirectory(), bridge.LockPath, StringComparison.OrdinalIgnoreCase); // the folder the program looks in
        return bridge;
    }

    private async Task<ClaudeTerminal> StartClaudeAsync(string claude, string name, IdeBridge bridge, bool expectConnection)
    {
        var terminal = ClaudeTerminal.Start(claude, _folder, Path.Combine(_temp.Path, name + ".log"));
        await Pump(() =>
        {
            terminal.AnswerTrustQuestion();
            return bridge.Agents.Any(a => a.Name is not null) || terminal.HasExited || (!expectConnection && terminal.Screen.Contains("? for shortcuts", StringComparison.Ordinal));
        }, TimeSpan.FromSeconds(90), "the program to connect or to be ready");
        return terminal;
    }

    [AvaloniaFact]
    public async Task Claude_Code_connects_with_the_token_of_the_lock_file_and_its_diff_waits_in_the_review_for_the_decision_of_the_user()
    {
        var claude = RealTools.Require("claude");
        string version = RealTools.VersionLine(claude) ?? "";
        var bridge = await StartBridgeAsync("notes.txt", "alpha beta\ngamma\n");
        var tab = _w.Active!;
        using var terminal = await StartClaudeAsync(claude, "accept", bridge, expectConnection: true);

        // it connects, and says who it is
        Assert.Single(bridge.Agents);
        Assert.Equal("claude-code", bridge.Agents[0].Name);
        Assert.StartsWith(bridge.Agents[0].Version + " ", version.Trim() + " "); // the version the program reports is the one it introduced itself with
        await Pump(() => Equals(_w.IdeItem.Content, Loc.Get(K.IdeStatusConnected, ("agent", $"claude-code {bridge.Agents[0].Version}"))), TimeSpan.FromSeconds(10), "the status bar to name the agent");
        await Pump(() => terminal.IdeConnectionLines().Any(l => l.Contains("Successfully connected (transport: ws-ide)", StringComparison.Ordinal)), TimeSpan.FromSeconds(10), "the program to log the connection");

        // a diff: the review shows it and the program waits; nothing is written before the decision
        await Wait(TimeSpan.FromSeconds(3));
        terminal.Say("Use the Edit tool to change the word beta to delta in notes.txt. Do nothing else.");
        await Pump(() => ReviewShown, TimeSpan.FromSeconds(120), "the review of the diff");
        await Wait(TimeSpan.FromSeconds(3));
        Assert.Equal("alpha beta\ngamma\n", tab.Editor!.Document.Text);
        Assert.Equal("alpha beta\ngamma\n", File.ReadAllText(tab.Path!));
        Assert.Contains("Opened changes in Textida", terminal.Screen);
        int asked = _dialogs.Asked.Count;

        // the user applies it: the program takes that as its own question answered, writes the file itself and goes on
        _w.ApplyPlan();
        Assert.Equal("alpha delta\ngamma\n", tab.Editor.Document.Text);
        await Pump(() => File.ReadAllText(tab.Path!) == "alpha delta\ngamma\n", TimeSpan.FromSeconds(60), "the program to write the accepted change");
        Assert.Equal(asked, _dialogs.Asked.Count); // the editor asked nothing meanwhile
        Assert.False(ReviewShown);

        // a second diff the user rejects: the program does not write it
        await Wait(TimeSpan.FromSeconds(8));
        terminal.Say("Use the Edit tool to change the word gamma to omega in notes.txt. Do nothing else.");
        await Pump(() => ReviewShown, TimeSpan.FromSeconds(120), "the review of the second diff");
        foreach (var row in _w.ResultsView!.Rows.OfType<SearchResultsView.HunkRow>()) row.Selected = false;
        _w.ApplyPlan();
        await Pump(() => !ReviewShown, TimeSpan.FromSeconds(10), "the review to close");
        await Wait(TimeSpan.FromSeconds(10));
        Assert.Equal("alpha delta\ngamma\n", File.ReadAllText(tab.Path!));
        Assert.Equal("alpha delta\ngamma\n", tab.Editor.Document.Text);

        // a third diff the user answers in the terminal of the program instead: the program closes the tab of the diff (close_tab), the review goes and nothing is written
        await Wait(TimeSpan.FromSeconds(8));
        terminal.Say("Use the Edit tool to change the word delta to epsilon in notes.txt. Do nothing else.");
        await Pump(() => ReviewShown, TimeSpan.FromSeconds(120), "the review of the third diff");
        await Wait(TimeSpan.FromSeconds(2));
        terminal.Escape();
        await Pump(() => !ReviewShown, TimeSpan.FromSeconds(20), () => "the review to close after Escape in the terminal\n" + terminal.Screen);
        await Wait(TimeSpan.FromSeconds(3));
        Assert.Equal("alpha delta\ngamma\n", File.ReadAllText(tab.Path!));
        Assert.Equal("alpha delta\ngamma\n", tab.Editor.Document.Text);

        // the program wrote the accepted text to the disk itself: the file now holds what the tab holds, so the tab is the saved state and nothing was asked
        Assert.False(tab.Doc.Doc.IsModified);
        Assert.Equal(asked, _dialogs.Asked.Count);
        Assert.Equal("alpha delta\ngamma\n", File.ReadAllText(tab.Path!));

        // what the program asked of the bridge: only calls the bridge answers, no tool it does not know (that would have switched the bridge off)
        var calls = terminal.IdeToolCalls();
        Assert.Equal(3, calls.Count(c => c == "openDiff"));
        Assert.Contains("close_tab", calls);
        TestContext.Current.SendDiagnosticMessage($"Claude Code {version}: the bridge answered {string.Join(", ", calls.Distinct())}");
        Assert.All(calls, c => Assert.Contains(c, new[] { "openDiff", "close_tab", "closeAllDiffTabs", "getDiagnostics" }));
        Assert.NotNull(_app.Plugins.Mcp.IdeOf(_w));
        Assert.DoesNotContain("Failed to show diff", terminal.ReadDebugLog());
    }

    [AvaloniaFact]
    public async Task Claude_Code_shows_the_selection_of_the_shared_tab_and_an_at_mention_the_user_sends()
    {
        var claude = RealTools.Require("claude");
        var bridge = await StartBridgeAsync("notes.txt", "alpha beta\ngamma\n");
        var tab = _w.Active!;
        using var terminal = await StartClaudeAsync(claude, "selection", bridge, expectConnection: true);

        // every move of the selection of the shared tab reaches the program (the one the editor pushes at the connection is sent before the program listens for it)
        await Wait(TimeSpan.FromSeconds(4));
        tab.Editor!.Select(6, 4); // "beta"
        await Pump(() => terminal.Screen.Contains("1 line selected", StringComparison.Ordinal), TimeSpan.FromSeconds(15), () => "the program to follow the selection\n" + terminal.Screen);

        // the user hands the program the lines of the selection: it puts a reference to them into its input line
        _w.SendIdeMention();
        await _w.McpTask;
        await Pump(() => terminal.Screen.Contains("@notes.txt#L1", StringComparison.Ordinal), TimeSpan.FromSeconds(15), () => "the program to show the mention\n" + terminal.Screen);
        Assert.Empty(terminal.IdeToolCalls().Except(["closeAllDiffTabs", "getDiagnostics"])); // it asked the bridge for nothing it does not know
        Assert.NotNull(_app.Plugins.Mcp.IdeOf(_w));
    }

    [AvaloniaFact]
    public async Task Claude_Code_with_another_token_than_the_one_in_the_lock_file_is_refused()
    {
        var claude = RealTools.Require("claude");
        var bridge = await StartBridgeAsync("notes.txt", "alpha beta\n");

        // the program reads the token from the lock file; a lock file with another one is what a program that read an old file would present
        var content = JsonNode.Parse(File.ReadAllBytes(bridge.LockPath))!.AsObject();
        Assert.Equal(bridge.AuthToken, (string?)content["authToken"]);
        content["authToken"] = new string('0', bridge.AuthToken.Length);
        File.WriteAllBytes(bridge.LockPath, Encoding.UTF8.GetBytes(content.ToJsonString()));

        using var terminal = await StartClaudeAsync(claude, "refused", bridge, expectConnection: false);
        await Wait(TimeSpan.FromSeconds(10));
        Assert.Empty(bridge.Agents);
        Assert.Equal(Loc.Get(K.IdeStatusWaiting), _w.IdeItem.Content);
        Assert.DoesNotContain(terminal.IdeConnectionLines(), l => l.Contains("Successfully connected", StringComparison.Ordinal));
        Assert.Contains(terminal.IdeConnectionLines(), l => l.Contains("Expected 101 status code: 401", StringComparison.Ordinal)); // it did try, and the bridge answered 401 Unauthorized
    }
}
