using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.Acp;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Tests.RealAgents;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P6.6 against the real adapters of the table (P6.6.4): <c>claude-agent-acp</c> and <c>codex-acp</c>, installed with npm at the version the table lists, started by the editor the way the
/// Start Agent command starts them. Needs npm, the login of Claude Code (<c>CLAUDE_CONFIG_DIR</c> of this process) and of Codex; the prompts are tiny and the agents work in a folder of the test.
/// What these runs showed, for the versions in the table: both adapters start, answer, ask the user before their own tools (Claude Code; Codex asked nothing in its workspace sandbox), stop on
/// <c>session/cancel</c> and end with their process. Neither calls <c>fs/read_text_file</c> or <c>fs/write_text_file</c>: they read and write the disk with their own tools, so an unsaved tab
/// is not what they read and a write is not a proposal in the change review (the characterisation test says so; when an adapter starts to use the methods it fails and the table is to be updated).
/// </summary>
[Collection("LocState")]
[Trait("Category", "RealAgents")]
public class AcpRealAgentsTests : IDisposable
{
    private static readonly Dictionary<string, string> Packages = new()
    {
        ["claude"] = "@agentclientprotocol/claude-agent-acp",
        ["codex"] = "@agentclientprotocol/codex-acp",
    };

    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly IDisposable _cleanEnvironment = RealTools.WithoutSessionVariables();
    private string? _packageFolder;
    private AcpService Acp => _app.Plugins.Acp;

    public AcpRealAgentsTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _w.Width = 1000;
        _w.Height = 700;
    }

    public void Dispose()
    {
        try { Acp.StopAsync().Wait(TimeSpan.FromSeconds(15)); } catch (Exception e) when (e is AggregateException or TimeoutException) { }
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
        _cleanEnvironment.Dispose();
        if (_packageFolder is null) return;
        var installation = Path.GetDirectoryName(Path.GetDirectoryName(_packageFolder))!;
        var leftovers = RealTools.RunningBelow(installation);
        for (var sw = Stopwatch.StartNew(); leftovers.Count > 0 && sw.Elapsed < TimeSpan.FromSeconds(10); leftovers = RealTools.RunningBelow(installation)) Thread.Sleep(200);
        Assert.Empty(leftovers); // no program of the adapter outlives the test
    }

    private static async Task Pump(Func<bool> done, TimeSpan timeout, string what)
    {
        for (var sw = Stopwatch.StartNew(); !done();)
        {
            Assert.True(sw.Elapsed < timeout, "Not reached: " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }

    private static async Task Wait(TimeSpan time)
    {
        for (var sw = Stopwatch.StartNew(); sw.Elapsed < time;)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }

    private static async Task Pump(Task task, TimeSpan timeout, string what)
    {
        await Pump(() => task.IsCompleted, timeout, what);
        await task;
    }

    private static async Task<T> Pump<T>(Task<T> task, TimeSpan timeout, string what)
    {
        await Pump(() => task.IsCompleted, timeout, what);
        return await task;
    }

    /// <summary>The adapter of the table, installed at the version the table lists; the command is found in the folder of that installation, as the Start Agent command finds it in PATH.</summary>
    private async Task<(AcpAdapter Adapter, AcpLaunch Launch)> InstallAsync(string id)
    {
        var adapter = AcpAdapters.Find(id)!;
        _packageFolder = await RealTools.InstallNpmPackageAsync(Packages[id], adapter.ListedVersion);
        var (program, problem) = AcpLaunch.Find(adapter.Command, _packageFolder);
        Assert.True(program is not null, problem);
        if (id == "claude") RealTools.Require("claude"); // the adapter runs the agent with the login of Claude Code
        if (id == "codex") RealTools.Require("codex");
        return (adapter, new AcpLaunch(program!, adapter.Args, _temp.Path));
    }

    private async Task StartAsync(string id)
    {
        var (adapter, launch) = await InstallAsync(id);
        Assert.True(await Pump(Acp.StartAsync(_w, launch, adapter.Name, adapter), TimeSpan.FromSeconds(120), "the agent to start"), string.Join("\n", Acp.Lines().Select(l => l.Text)));
        Assert.Equal(AgentState.Ready, Acp.State);
    }

    /// <summary>Answers every question of the agent with the button whose label starts with <paramref name="label"/>, and remembers the questions.</summary>
    private List<ChoiceRequest> Answer(string label) => Answer(_ => label);

    /// <summary>Answers each question with the button whose label starts with what <paramref name="pick"/> says for it; a label that is not offered answers with Cancel, the last button.</summary>
    private List<ChoiceRequest> Answer(Func<ChoiceRequest, string> pick)
    {
        var questions = new List<ChoiceRequest>();
        _dialogs.WhileAsking = () =>
        {
            var question = _dialogs.Asked[^1];
            questions.Add(question);
            var label = pick(question);
            int index = question.Buttons.ToList().FindIndex(b => b.Label.StartsWith(label, StringComparison.OrdinalIgnoreCase));
            _dialogs.Answers.Enqueue(index < 0 ? question.Buttons.Count - 1 : index);
        };
        return questions;
    }

    /// <summary>The question is about reading a file (the title of the tool of the agent starts with Read).</summary>
    private static bool IsRead(ChoiceRequest question) => question.Message.Contains(": Read ", StringComparison.Ordinal);

    private async Task<string> SayAsync(string text, int seconds = 180)
    {
        int before = Acp.Lines().Count;
        Assert.True(await Pump(Acp.SendAsync(text), TimeSpan.FromSeconds(seconds), "the turn to end"));
        Assert.Equal(AgentState.Ready, Acp.State);
        return string.Join("\n", Acp.Lines().Skip(before).Select(l => l.Text));
    }

    private DocTab OpenShared(string name, string saved, string? unsaved = null)
    {
        var tab = _w.OpenFile(_temp.File(name, saved), null);
        _w.UpdateLayout();
        if (unsaved is not null) tab.Editor!.Document.Text = unsaved;
        _w.SelectTab(tab);
        Acp.Share(_w, tab);
        return tab;
    }

    private static bool Alive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
    }

    [AvaloniaTheory]
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task The_adapter_starts_answers_a_prompt_and_is_gone_with_its_process_when_stopped(string id)
    {
        await StartAsync(id);
        var adapter = AcpAdapters.Find(id)!;
        Assert.Equal(adapter.ListedVersion, Acp.AgentInfo?.Version); // the table pins the version that was run
        Answer("Yes");
        var said = await SayAsync("Reply with the single word PONG and nothing else.");
        Assert.Contains("PONG", said);
        Assert.Contains(Loc.Get(K.AgentTurnDone, ("reason", "end_turn")), said);

        int pid = Acp.AgentProcessId!.Value;
        await Pump(Acp.StopAsync(), TimeSpan.FromSeconds(30), "the agent to stop");
        Assert.Equal(AgentState.None, Acp.State);
        await Pump(() => !Alive(pid), TimeSpan.FromSeconds(10), "the agent process to end");
    }

    [AvaloniaTheory]
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task The_adapter_reads_and_writes_the_disk_with_its_own_tools_and_neither_the_unsaved_text_nor_the_review_is_involved(string id)
    {
        if (id == "claude") AskBeforeFileTools(); // before the agent starts: it reads the settings of the folder then
        await StartAsync(id);
        var questions = Answer("Yes");
        var tab = OpenShared("shared.txt", "alpha\nbeta\ngamma\n", "unsaved alpha\nbeta\ngamma\n");
        string path = tab.Path!;

        var said = await SayAsync($"Read the file {path}, then change its first line to the word CHANGED and keep the other lines. Use your file tools.");

        // the adapter did not ask the editor for the file: the read was of the disk (the text of the tab was not seen), and the write went to the disk without a proposal
        var boundary = Acp.Boundary();
        Assert.Equal(0, boundary.Reads);
        Assert.Equal(0, boundary.Proposals);
        Assert.StartsWith("CHANGED\nbeta\ngamma", File.ReadAllText(path).Replace("\r\n", "\n"));
        Assert.Equal("unsaved alpha\nbeta\ngamma\n", tab.Editor!.Document.Text); // the tab is untouched
        Assert.False(_w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true);
        Assert.DoesNotContain("unsaved", said, StringComparison.OrdinalIgnoreCase);
        TestContext.Current.SendDiagnosticMessage($"{id}: {questions.Count} question(s) asked, boundary {boundary}");
    }

    /// <summary>The agent takes its permission mode from the settings of Claude Code; a settings file in the folder of the agent makes it ask before it reads or writes.</summary>
    private void AskBeforeFileTools()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Path, ".claude"));
        File.WriteAllText(Path.Combine(_temp.Path, ".claude", "settings.json"), "{\"permissions\":{\"defaultMode\":\"default\",\"ask\":[\"Write\",\"Edit\",\"Read\"]}}");
    }

    [AvaloniaFact]
    public async Task Claude_asks_before_it_writes_and_nothing_is_written_when_the_user_says_no_or_cancels()
    {
        AskBeforeFileTools(); // before the agent starts: it reads the settings of the folder then
        await StartAsync("claude");
        var tab = OpenShared("shared.txt", "alpha\nbeta\n");
        string path = tab.Path!;

        var no = Answer(q => IsRead(q) ? "Yes" : "No"); // reading is allowed, writing is not
        var said = await SayAsync($"Write the file {path} so that its whole content is the single word REFUSED. Use your file writing tool.");
        Assert.Contains(no, q => !IsRead(q) && q.Message.Contains("Write", StringComparison.Ordinal)); // the question names the tool
        Assert.Equal("alpha\nbeta\n", File.ReadAllText(path).Replace("\r\n", "\n"));

        var cancel = Answer(q => IsRead(q) ? "Yes" : Loc.Get(K.DialogCancel)); // the dialog is closed without an answer
        await SayAsync($"Write the file {path} so that its whole content is the single word CANCELLED. Use your file writing tool.");
        Assert.Contains(cancel, q => !IsRead(q));
        Assert.Equal("alpha\nbeta\n", File.ReadAllText(path).Replace("\r\n", "\n"));
        Assert.NotEmpty(said);
    }

    [AvaloniaTheory]
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task Cancel_stops_a_turn_that_runs_a_long_command(string id)
    {
        await StartAsync(id);
        Answer("Yes");
        var turn = Acp.SendAsync("Run a shell command that waits for 90 seconds (sleep, or Start-Sleep), and when it ends reply done. Do nothing else.");
        await Pump(() => Acp.Lines().Any(l => l.Role == AgentRole.Tool), TimeSpan.FromSeconds(120), "the agent to start the command");
        await Wait(TimeSpan.FromSeconds(2)); // let the command run a moment
        Assert.True(Acp.TurnRunning);
        var cancelled = Stopwatch.StartNew();
        Acp.CancelTurn();
        await Pump(turn, TimeSpan.FromSeconds(30), "the turn to end after the cancel");
        Assert.True(cancelled.Elapsed < TimeSpan.FromSeconds(30));
        Assert.Contains(Acp.Lines(), l => l.Text == Loc.Get(K.AgentTurnDone, ("reason", AcpClient.Cancelled)));
        Assert.Equal(AgentState.Ready, Acp.State);
    }

    [AvaloniaTheory]
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task An_agent_that_is_killed_is_reported_in_the_panel_and_the_editor_goes_on(string id)
    {
        await StartAsync(id);
        int pid = Acp.AgentProcessId!.Value;
        using (var process = Process.GetProcessById(pid)) process.Kill(entireProcessTree: true);

        await Pump(() => Acp.State == AgentState.Ended, TimeSpan.FromSeconds(30), "the end of the agent to be noticed");
        Assert.Contains(Acp.Lines(), l => l.Role == AgentRole.Problem);
        var tab = _w.OpenFile(_temp.File("after.txt", "still works\n"), null); // the window is alive
        Assert.Equal("still works\n", tab.Editor!.Document.Text);
        _w.AgentInputBox.Text = "hello";
        _w.SendAgentMessage();
        Assert.Equal(Loc.Get(K.AgentShareNeedAgent), _w.StatusMessageText); // a message to an ended agent is refused with the reason
    }

    [AvaloniaTheory]
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task An_adapter_that_does_not_answer_initialize_in_time_ends_as_a_timeout_and_leaves_no_process(string id)
    {
        var (_, launch) = await InstallAsync(id);
        var options = PluginLimitsPlaceholders.Acp() with { InitializeTimeout = TimeSpan.FromMilliseconds(1) };
        var failed = await Assert.ThrowsAsync<AcpTimeoutException>(() => Pump(AcpClient.StartAsync(launch, options, Acp), TimeSpan.FromSeconds(60), "the start to fail"));
        Assert.Contains("initialize", failed.Message);
    }
}
