using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.Acp;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
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
