using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.Acp;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P6.6 through the running window and a stand-in agent that is a real process speaking ACP on its standard input and output: the conversation in the panel, the agent reading
/// the unsaved text of a tab the user shared (and nothing else), its writes as hunks in the change review, the permission questions as dialogs, cancel and a crash.
/// The agents of the table of P6.6.4 are not started here: no real agent was run.
/// </summary>
[Collection("LocState")]
public class AcpAppTests : IDisposable
{
    private static readonly string FakeAgent = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.FakeAcp.dll");

    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private AcpService Acp => _app.Plugins.Acp;

    public AcpAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _w.Width = 1000;
        _w.Height = 700;
    }

    public void Dispose()
    {
        try { Acp.StopAsync().Wait(TimeSpan.FromSeconds(10)); } catch (Exception e) when (e is AggregateException or TimeoutException) { }
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
        await Until(() => _w.AgentTask.IsCompleted, "the agent command to finish");
        await _w.AgentTask;
    }

    private async Task<bool> StartAsync(params string[] agentArgs)
    {
        Assert.True(File.Exists(FakeAgent), "The stand-in agent is not next to the tests: " + FakeAgent);
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var start = Acp.StartAsync(_w, new AcpLaunch(dotnet, [FakeAgent, .. agentArgs], _temp.Path), "Fake agent");
        await Until(() => start.IsCompleted, "the agent to start");
        return await start;
    }

    private async Task ShareAsync(DocTab tab)
    {
        _w.SelectTab(tab);
        await RunAsync("nks.agent.shareTab");
    }

    /// <summary>Types the message the way the user does and waits for the end of the turn.</summary>
    private async Task SayAsync(string text)
    {
        _w.AgentInputBox.Text = text;
        _w.SendAgentMessage();
        await Until(() => _w.AgentTask.IsCompleted, "the turn to end");
        await _w.AgentTask;
        Settle();
    }

    private static string Panel(MainWindow w) => w.AgentPanelText;

    private async Task UntilPanelHas(string text) => await Until(() => Panel(_w).Contains(text), "the panel to show: " + text);

    // ---- the conversation ----

    [AvaloniaFact]
    public async Task The_panel_is_closed_until_asked_for_and_shows_the_conversation_of_a_turn_as_it_streams()
    {
        Assert.False(_w.AgentPanelOpen);
        await RunAsync("nks.agent.panel");
        Assert.True(_w.AgentPanelOpen);
        Assert.Equal(Loc.Get(K.AgentStatusNone), _w.AgentPanelStatus);

        Assert.True(await StartAsync());
        Assert.Equal(AgentState.Ready, Acp.State);
        await Until(() => _w.AgentPanelStatus == Loc.Get(K.AgentStatusReady, ("agent", "Fake agent")), "the status");
        await SayAsync("hello");
        await UntilPanelHas("Hello world");
        var text = Panel(_w);
        Assert.Contains(Loc.Get(K.AgentLineYou, ("text", "hello")), text);
        Assert.Contains(Loc.Get(K.AgentLineAgent, ("agent", "Fake agent"), ("text", "Hello world")), text);
        await UntilPanelHas(Loc.Get(K.AgentLineTool, ("title", "Looking around"), ("status", Loc.Get(K.AgentToolCompleted))));
        Assert.Contains(Loc.Get(K.AgentLineNote, ("text", Loc.Get(K.AgentTurnDone, ("reason", "end_turn")))), text);
        Assert.Equal(AgentState.Ready, Acp.State);
        Assert.Equal(string.Empty, _w.AgentInputBox.Text);
    }

    [AvaloniaFact]
    public async Task Cancel_ends_a_running_turn_and_the_panel_says_it_was_cancelled()
    {
        await RunAsync("nks.agent.panel");
        Assert.True(await StartAsync());
        _w.AgentInputBox.Text = "slow";
        _w.SendAgentMessage();
        await UntilPanelHas("tick");
        Assert.True(_w.AgentCancelButton.IsEnabled);
        Assert.Equal(AgentState.Working, Acp.State);
        await RunAsync("nks.agent.cancel");
        await Until(() => _w.AgentTask.IsCompleted, "the turn to end");
        await UntilPanelHas(Loc.Get(K.AgentLineNote, ("text", Loc.Get(K.AgentTurnDone, ("reason", "cancelled")))));
        Assert.Equal(AgentState.Ready, Acp.State);
        await SayAsync("again");
        await UntilPanelHas("echo:again");
    }

    [AvaloniaFact]
    public async Task A_second_message_while_a_turn_runs_is_refused_and_a_message_without_an_agent_too()
    {
        _w.AgentInputBox.Text = "hello";
        _w.SendAgentMessage();
        Assert.Equal(Loc.Get(K.AgentShareNeedAgent), _w.StatusMessageText);
        Assert.True(await StartAsync());
        _w.AgentInputBox.Text = "slow";
        _w.SendAgentMessage();
        await Until(() => Acp.TurnRunning, "the turn");
        _w.AgentInputBox.Text = "more";
        _w.SendAgentMessage();
        Assert.Equal(Loc.Get(K.AgentBusy), _w.StatusMessageText);
        Assert.Equal("more", _w.AgentInputBox.Text);
        Acp.CancelTurn();
        await Until(() => !Acp.TurnRunning, "the end of the turn");
    }

    // ---- fs/read_text_file ----

    [AvaloniaFact]
    public async Task The_agent_reads_the_unsaved_text_of_a_shared_tab_and_nothing_that_was_not_shared()
    {
        await RunAsync("nks.agent.panel");
        var shared = Open("shared.txt", "alpha\nbeta\ngamma\n");
        var other = Open("other.txt", "never shared\n");
        shared.Editor!.Document.Text = "unsaved alpha\nbeta\ngamma\n";
        Settle();
        Assert.Equal("alpha\nbeta\ngamma\n", File.ReadAllText(shared.Path!));
        var onlyOnDisk = _temp.File("disk.txt", "not open at all\n");

        // sharing needs a running agent
        _w.SelectTab(shared);
        await RunAsync("nks.agent.shareTab");
        Assert.False(Acp.IsShared(shared));
        Assert.Equal(Loc.Get(K.AgentShareNeedAgent), _w.StatusMessageText);

        Assert.True(await StartAsync());
        await ShareAsync(shared);
        Assert.True(Acp.IsShared(shared));
        Assert.False(Acp.IsShared(other));

        await SayAsync("read " + shared.Path);
        await UntilPanelHas("READ:unsaved alpha");
        Assert.DoesNotContain("never shared", Panel(_w));

        await SayAsync("read " + other.Path);
        await UntilPanelHas("READERR:-32002:");
        await SayAsync("read " + onlyOnDisk);
        await Until(() => Panel(_w).Split("READERR:-32002:").Length == 3, "the second refusal");
        Assert.DoesNotContain("not open at all", Panel(_w));
        Assert.DoesNotContain("never shared", Panel(_w));

        // the user takes the tab back: the next read is refused
        await ShareAsync(shared);
        Assert.False(Acp.IsShared(shared));
        await SayAsync("read " + shared.Path);
        await Until(() => Panel(_w).Split("READERR:-32002:").Length == 4, "the refusal after withdrawing");
        Assert.Equal(1, Acp.Boundary().Reads); // only the read the editor served is counted
    }

    // ---- fs/write_text_file ----

    [AvaloniaFact]
    public async Task A_write_is_shown_as_hunks_and_reaches_the_document_only_when_the_user_applies_it()
    {
        await RunAsync("nks.agent.panel");
        var tab = Open("shared.txt", "alpha\nbeta\ngamma");
        Assert.True(await StartAsync());
        await ShareAsync(tab);

        _w.AgentInputBox.Text = $"write {tab.Path}\nalpha\ndelta\ngamma";
        _w.SendAgentMessage();
        await Until(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true, "the review");
        // nothing is written while the user looks at it
        Assert.Equal("alpha\nbeta\ngamma", tab.Editor!.Document.Text);
        Assert.Equal("alpha\nbeta\ngamma", File.ReadAllText(tab.Path!));
        Assert.DoesNotContain("WRITE-OK", Panel(_w));
        var hunk = Assert.Single(_w.ResultsView!.Rows.OfType<SearchResultsView.HunkRow>());
        Assert.True(Acp.TurnRunning);

        _w.ApplyPlan();
        await UntilPanelHas("WRITE-OK");
        await Until(() => _w.AgentTask.IsCompleted, "the turn");
        Assert.Equal("alpha\ndelta\ngamma", tab.Editor.Document.Text);
        Assert.Equal("alpha\nbeta\ngamma", File.ReadAllText(tab.Path!)); // the document is changed, not saved
        Assert.Equal(new AgentBoundary(0, 1, 1, 0, 0), Acp.Boundary());
        _ = hunk;

        // one undo step takes it back, the way an applied change always does
        _w.SelectTab(tab);
        _w.Undo();
        Settle();
        Assert.Equal("alpha\nbeta\ngamma", tab.Editor.Document.Text);
    }

    [AvaloniaFact]
    public async Task A_rejected_write_changes_nothing_and_the_agent_is_told()
    {
        await RunAsync("nks.agent.panel");
        var tab = Open("shared.txt", "alpha\nbeta\n");
        Assert.True(await StartAsync());
        await ShareAsync(tab);
        _w.AgentInputBox.Text = $"write {tab.Path}\nsomething else\n";
        _w.SendAgentMessage();
        await Until(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true, "the review");
        foreach (var row in _w.ResultsView!.Rows.OfType<SearchResultsView.HunkRow>()) row.Selected = false;
        _w.ApplyPlan();
        await UntilPanelHas("WRITE-ERR:-32001:The user rejected the change.");
        Assert.Equal("alpha\nbeta\n", tab.Editor!.Document.Text);
        Assert.Equal("alpha\nbeta\n", File.ReadAllText(tab.Path!));
        Assert.Equal(new AgentBoundary(0, 1, 0, 0, 0), Acp.Boundary()); // decided, not applied
    }

    [AvaloniaFact]
    public async Task A_write_to_a_file_that_is_not_shared_is_refused_without_asking_the_user()
    {
        await RunAsync("nks.agent.panel");
        var shared = Open("shared.txt", "one\n");
        var other = Open("other.txt", "two\n");
        Assert.True(await StartAsync());
        await ShareAsync(shared);
        await SayAsync($"write {other.Path}\nhijacked\n");
        await UntilPanelHas("WRITE-ERR:-32002:");
        Assert.Null(_w.ResultsView);
        Assert.Equal("two\n", other.Editor!.Document.Text);
        Assert.Equal("two\n", File.ReadAllText(other.Path!));

        // a path that exists only on disk is not created or changed either
        var missing = Path.Combine(_temp.Path, "created-by-agent.txt");
        await SayAsync($"write {missing}\nnew\n");
        await Until(() => Panel(_w).Split("WRITE-ERR:-32002:").Length == 3, "the second refusal");
        Assert.False(File.Exists(missing));
    }

    [AvaloniaFact]
    public async Task The_same_content_is_not_a_change_and_a_secrets_file_is_never_written()
    {
        await RunAsync("nks.agent.panel");
        var tab = Open("same.txt", "unchanged");
        var secrets = Open(".env", "KEY=value\n");
        Assert.True(await StartAsync());
        await ShareAsync(tab);
        await ShareAsync(secrets);
        await SayAsync($"write {tab.Path}\nunchanged");
        await UntilPanelHas("WRITE-OK");
        Assert.Null(_w.ResultsView);

        await SayAsync($"write {secrets.Path}\nKEY=stolen\n");
        await UntilPanelHas("WRITE-ERR:-32001:");
        Assert.Equal("KEY=value\n", secrets.Editor!.Document.Text);

        // the values stay hidden from the agent
        await SayAsync("read " + secrets.Path);
        await UntilPanelHas("READ:");
        Assert.DoesNotContain("value", Panel(_w).Split("READ:")[1].Split('\n')[0]);
    }

    // ---- session/request_permission ----

    [AvaloniaFact]
    public async Task A_permission_question_is_a_dialog_with_the_choices_of_the_agent_and_the_answer_goes_back()
    {
        await RunAsync("nks.agent.panel");
        Assert.True(await StartAsync());
        _dialogs.Answers.Enqueue(0); // Allow once
        await SayAsync("perm");
        await UntilPanelHas("PERM:{\"outcome\":\"selected\",\"optionId\":\"allow-once\"}");
        var asked = Assert.Single(_dialogs.Asked);
        Assert.Equal(Loc.Get(K.AgentPermissionTitle, ("agent", "Fake agent")), asked.Title);
        Assert.Contains("Run the tests", asked.Message);
        Assert.Null(asked.Details); // an ordinary tool with no "always" choice has nothing to add
        Assert.Equal(["Allow once", "Reject", Loc.Get(K.DialogCancel)], asked.Buttons.Select(b => b.Label));
        // the safe answer is the default; the cancel button is the cancel button
        Assert.Equal([false, true, false], asked.Buttons.Select(b => b.IsDefault));
        Assert.Equal([false, false, true], asked.Buttons.Select(b => b.IsCancel));

        _dialogs.Answers.Enqueue(1); // Reject
        await SayAsync("perm");
        await Until(() => Panel(_w).Contains("\"optionId\":\"reject-once\""), "the rejection");
    }

    [AvaloniaTheory]
    [InlineData(2)] // the cancel button
    [InlineData(-1)] // the window closed without an answer
    public async Task Cancelling_or_closing_the_permission_dialog_answers_cancelled(int answer)
    {
        await RunAsync("nks.agent.panel");
        Assert.True(await StartAsync());
        _dialogs.Answers.Enqueue(answer);
        await SayAsync("perm");
        await UntilPanelHas("PERM:{\"outcome\":\"cancelled\"}");
    }

    // ---- ending ----

    [AvaloniaFact]
    public async Task A_crash_is_reported_in_the_panel_the_shares_are_taken_back_and_the_editor_goes_on()
    {
        await RunAsync("nks.agent.panel");
        var tab = Open("shared.txt", "text\n");
        Assert.True(await StartAsync());
        await ShareAsync(tab);
        await SayAsync("crash");
        await Until(() => Acp.State == AgentState.Ended, "the crash to be noticed");
        await UntilPanelHas(Loc.Get(K.AgentLineProblem, ("text", Loc.Get(K.AgentEnded, ("reason", new AcpAgentExitedException(new AcpExit(3, "agent crashed on purpose")).Message)))));
        await Until(() => !Acp.IsShared(tab), "the share to be taken back");
        Assert.Equal(Loc.Get(K.AgentStatusEnded, ("agent", "Fake agent")), _w.AgentPanelStatus);
        Assert.False(_w.AgentSendButton.IsEnabled);
        Assert.Equal("text\n", tab.Editor!.Document.Text);

        // a new agent can be started afterwards
        Assert.True(await StartAsync());
        await SayAsync("hello");
        await UntilPanelHas("Hello world");
    }

    [AvaloniaFact]
    public async Task An_agent_that_cannot_start_is_reported_and_stopping_ends_the_process_and_takes_the_shares_back()
    {
        await RunAsync("nks.agent.panel");
        Assert.False(await StartAsync("--protocol", "2"));
        Assert.Equal(AgentState.Ended, Acp.State);
        await UntilPanelHas(Loc.Get(K.AgentLineProblem, ("text", Loc.Get(K.AgentStartFailed, ("agent", "Fake agent"), ("reason", "The agent speaks ACP version 2; this client speaks version 1.")))));

        var tab = Open("shared.txt", "text\n");
        Assert.True(await StartAsync());
        await ShareAsync(tab);
        await RunAsync("nks.agent.stop");
        Assert.Equal(AgentState.None, Acp.State);
        Assert.False(Acp.IsShared(tab));
        await UntilPanelHas(Loc.Get(K.AgentLineNote, ("text", Loc.Get(K.AgentStopped))));
        Assert.Equal(Loc.Get(K.AgentStatusNone), _w.AgentPanelStatus);
    }

    [AvaloniaFact]
    public async Task Closing_the_window_the_agent_was_started_from_stops_it()
    {
        Assert.True(await StartAsync());
        _w.Close();
        await Until(() => Acp.State == AgentState.None, "the agent to be stopped");
    }

    [AvaloniaFact]
    public async Task A_tab_without_a_file_cannot_be_shared_and_the_adapters_are_listed_as_not_verified()
    {
        Assert.True(await StartAsync());
        var untitled = _w.NewDocument();
        Settle();
        await ShareAsync(untitled);
        Assert.False(Acp.IsShared(untitled));
        Assert.Equal(Loc.Get(K.AgentShareUnavailable), _w.StatusMessageText);
        Assert.All(AcpAdapters.All, a => Assert.False(a.IsVerified));
    }

    // ---- P6.6.3 / P6.6.4: adapters, versions and the boundary of the guarantee ----

    private static string Dotnet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

    private static AcpAdapter FakeAdapter(string versionFlag = "--version", string? verified = null) =>
        new("fake", "Fake agent", Dotnet, [FakeAgent], "0.1.0", "https://example.invalid/fake", verified, VersionArgs: [FakeAgent, versionFlag]);

    private async Task<bool> StartAdapterAsync(AcpAdapter adapter)
    {
        var start = Acp.StartAsync(_w, new AcpLaunch(Dotnet, adapter.Args, _temp.Path), adapter.Name, adapter);
        await Until(() => start.IsCompleted, "the agent to start");
        return await start;
    }

    private bool HasNote(string text) => Acp.Lines().Any(l => l.Role == AgentRole.Note && l.Text == text);

    private static string Counts(int reads, int proposals, int applied, int commands, int other) =>
        Loc.Get(K.AgentBoundaryCounts, ("reads", reads), ("proposals", proposals), ("applied", applied), ("commands", commands), ("other", other));

    private static string AllReach() => string.Join(", ", Loc.Get(K.AgentReachFiles), Loc.Get(K.AgentReachPrograms), Loc.Get(K.AgentReachNetwork));

    private async Task UntilBoundaryHas(string text) => await Until(() => _w.AgentBoundaryNowText.Contains(text), "the boundary to show: " + text);

    private Task OpenChoiceAsync(int rows, string what) =>
        Until(() => _w.IsPaletteOpen && _w.PaletteModeNow == PaletteMode.Choice && _w.QuickResults.Count == rows, what);

    [AvaloniaFact]
    public async Task The_version_of_the_program_is_noted_next_to_the_table_and_a_program_without_one_only_gets_a_note()
    {
        Assert.True(await StartAdapterAsync(FakeAdapter()));
        Assert.Equal(AgentState.Ready, Acp.State);
        Assert.True(HasNote(Loc.Get(K.AgentVersionUnverified, ("agent", "Fake agent"), ("version", "0.1.0"), ("listed", "0.1.0"))));
        await Acp.StopAsync();

        Assert.True(await StartAdapterAsync(FakeAdapter(verified: "0.1.0")));
        Assert.True(HasNote(Loc.Get(K.AgentVersionVerified, ("agent", "Fake agent"), ("version", "0.1.0"), ("listed", "0.1.0"))));
        await Acp.StopAsync();

        Assert.True(await StartAdapterAsync(FakeAdapter("--version-silent")));
        Assert.Equal(AgentState.Ready, Acp.State);
        Assert.True(HasNote(Loc.Get(K.AgentVersionUnknown, ("agent", "Fake agent"), ("reason", "no version in the output"))));
    }

    [AvaloniaFact]
    public async Task Stopping_while_the_version_is_read_ends_the_start_without_an_agent()
    {
        var adapter = FakeAdapter("--version-hang");
        var start = Acp.StartAsync(_w, new AcpLaunch(Dotnet, adapter.Args, _temp.Path), adapter.Name, adapter);
        Assert.Equal(AgentState.Starting, Acp.State);
        await Acp.StopAsync();
        await Until(() => start.IsCompleted, "the start to end", seconds: 10); // the check itself has a longer limit, so only the stop can end it this soon
        Assert.False(await start);
        Assert.Equal(AgentState.None, Acp.State);
        Assert.False(Acp.IsRunning);
    }

    [AvaloniaFact]
    public async Task The_panel_marks_tools_that_run_outside_the_editor_and_counts_what_went_through_it()
    {
        await RunAsync("nks.agent.panel");
        var tab = Open("shared.txt", "alpha\nbeta\ngamma");
        Assert.True(await StartAdapterAsync(FakeAdapter()));
        await UntilBoundaryHas(Loc.Get(K.AgentBoundaryMode, ("agent", "Fake agent"), ("reach", AllReach())));
        await UntilBoundaryHas(Counts(0, 0, 0, 0, 0));
        await ShareAsync(tab);

        await SayAsync("outside");
        await UntilPanelHas("OUTSIDE-DONE");
        var done = Loc.Get(K.AgentToolCompleted);
        await UntilPanelHas(Loc.Get(K.AgentLineToolOutside, ("title", "Run the tests"), ("status", done)));
        Assert.Contains(Loc.Get(K.AgentLineToolOutside, ("title", "Run the build"), ("status", done)), Panel(_w));
        Assert.Contains(Loc.Get(K.AgentLineToolOutside, ("title", "Delete the cache"), ("status", done)), Panel(_w));
        // a read by the agent's own tool may or may not have gone through the editor, so it is not marked
        Assert.Contains(Loc.Get(K.AgentLineTool, ("title", "Look at a file"), ("status", done)), Panel(_w));
        await UntilBoundaryHas(Counts(0, 0, 0, 2, 1));

        await SayAsync("read " + tab.Path);
        await UntilPanelHas("READ:alpha");
        await UntilBoundaryHas(Counts(1, 0, 0, 2, 1));

        // a new agent starts with new counts, and with no agent the panel shows no boundary of a session
        await Acp.StopAsync();
        await Until(() => _w.AgentBoundaryNowText.Length == 0, "the boundary of the session to go");
        Assert.True(await StartAdapterAsync(FakeAdapter()));
        Assert.Equal(new AgentBoundary(0, 0, 0, 0, 0), Acp.Boundary());
    }

    [AvaloniaFact]
    public async Task A_permission_question_for_a_tool_outside_the_editor_says_so_and_flags_an_always_choice()
    {
        await RunAsync("nks.agent.panel");
        Assert.True(await StartAsync());
        _dialogs.Answers.Enqueue(1); // Reject
        await SayAsync("permexec");
        await Until(() => Panel(_w).Contains("\"optionId\":\"reject-once\""), "the rejection");
        var asked = Assert.Single(_dialogs.Asked);
        Assert.Contains("Run rm", asked.Message);
        Assert.Equal([Loc.Get(K.AgentPermissionOutside), Loc.Get(K.AgentPermissionAlways)], asked.Details);
        Assert.Equal(["Allow once", "Reject", "Always allow", Loc.Get(K.DialogCancel)], asked.Buttons.Select(b => b.Label));
        Assert.Equal([false, true, false, false], asked.Buttons.Select(b => b.IsDefault));
    }

    [AvaloniaFact]
    public async Task Start_Agent_says_where_each_program_is_found_and_a_missing_one_gets_a_clear_message_instead_of_a_start()
    {
        Open("a.txt", "x"); // the agent works in the folder of the open file
        var missing = new AcpAdapter("gone", "Gone agent", "textida-no-such-agent-xyz", ["--acp"], "1.2.3", "https://example.invalid/gone");
        _w.AgentAdapters = [FakeAdapter(), missing];
        await _app.Workbench.Commands.ExecuteAsync("nks.agent.start", new ShellContext(_w, forMenu: false));
        await OpenChoiceAsync(2, "the list");
        Assert.Equal(["Fake agent", "Gone agent"], _w.QuickResults.Select(r => r.Title));
        Assert.Equal($"{FakeAdapter().CommandLine} · {Loc.Get(K.AgentStartFound)} · {Loc.Get(K.AgentStartUnverified)}", _w.QuickResults[0].Detail);
        Assert.Equal($"{missing.CommandLine} · {Loc.Get(K.AgentStartMissingInPath)}", _w.QuickResults[1].Detail);

        _dialogs.Answers.Enqueue(0); // OK
        _w.MovePalette(1);
        _w.RunPaletteSelection();
        await Until(() => _w.AgentTask.IsCompleted, "the command to end");
        var asked = Assert.Single(_dialogs.Asked);
        Assert.Equal(Loc.Get(K.AgentStartTitle, ("agent", "Gone agent")), asked.Title);
        Assert.Contains("textida-no-such-agent-xyz", asked.Message);
        Assert.Contains("was not found in PATH", asked.Message);
        Assert.Contains("https://example.invalid/gone", asked.Message);
        Assert.Equal(AgentState.None, Acp.State);
    }

    [AvaloniaFact]
    public async Task Start_Agent_asks_with_the_resolved_program_and_the_boundary_of_that_agent_and_starts_it_only_after_a_yes()
    {
        var tab = Open("a.txt", "x");
        var adapter = FakeAdapter();
        _w.AgentAdapters = [adapter];
        await _app.Workbench.Commands.ExecuteAsync("nks.agent.start", new ShellContext(_w, forMenu: false));
        await OpenChoiceAsync(1, "the list");
        _dialogs.Answers.Enqueue(1); // Cancel
        _w.RunPaletteSelection();
        await Until(() => _w.AgentTask.IsCompleted, "the command to end");
        Assert.Equal(AgentState.None, Acp.State);

        await _app.Workbench.Commands.ExecuteAsync("nks.agent.start", new ShellContext(_w, forMenu: false));
        await OpenChoiceAsync(1, "the list again");
        _dialogs.Answers.Enqueue(0); // Start
        _w.RunPaletteSelection();
        await Until(() => _w.AgentTask.IsCompleted, "the start to end");
        await _w.AgentTask;
        Assert.Equal(AgentState.Ready, Acp.State);

        Assert.Equal(2, _dialogs.Asked.Count);
        var asked = _dialogs.Asked[1];
        var program = AcpLaunch.Find(adapter.Command).Path!;
        var folder = Path.GetDirectoryName(tab.Path)!;
        Assert.Equal(Loc.Get(K.AgentStartMessage, ("command", new ToolInvocation("x", program, adapter.Args, folder, null, "").CommandLine), ("folder", folder)), asked.Message);
        Assert.Equal([Loc.Get(K.AgentStartReads), Loc.Get(K.AgentStartWrites), Loc.Get(K.AgentStartOwn, ("agent", "Fake agent"), ("reach", AllReach())), Loc.Get(K.AgentStartMode, ("agent", "Fake agent"))], asked.Details);
        Assert.True(_w.AgentPanelOpen);
        Assert.True(HasNote(Loc.Get(K.AgentVersionUnverified, ("agent", "Fake agent"), ("version", "0.1.0"), ("listed", "0.1.0"))));
        await UntilBoundaryHas(Loc.Get(K.AgentBoundaryMode, ("agent", "Fake agent"), ("reach", AllReach())));
    }
}

public class AcpServiceTests
{
    [Fact]
    public void Lines_and_limit_of_a_read_cut_the_text_at_line_ends()
    {
        const string text = "one\r\ntwo\nthree\nfour";
        Assert.Equal(text, AcpService.Slice(text, null, null));
        Assert.Equal("two\nthree\n", AcpService.Slice(text, 2, 2));
        Assert.Equal("three\nfour", AcpService.Slice(text, 3, null));
        Assert.Equal("one\r\n", AcpService.Slice(text, null, 1));
        Assert.Equal("", AcpService.Slice(text, 9, 1));
        Assert.Equal("", AcpService.Slice(text, 1, 0));
    }
}
