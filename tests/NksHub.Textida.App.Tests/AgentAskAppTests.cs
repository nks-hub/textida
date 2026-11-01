using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.Acp;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.2.5 with the stand-in agent of the ACP tests: the question about a selected range, what is sent, and that a change outside the range is refused while one inside goes to the change review.</summary>
[Collection("LocState")]
public class AgentAskAppTests : IDisposable
{
    private static readonly string FakeAgent = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.FakeAcp.dll");

    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private AcpService Acp => _app.Plugins.Acp;

    public AgentAskAppTests()
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

    private async Task<DocTab> OpenAndStartAsync(string content)
    {
        var tab = _w.OpenFile(_temp.File("sample.txt", content), null);
        _w.UpdateLayout();
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var start = Acp.StartAsync(_w, new AcpLaunch(dotnet, [FakeAgent], _temp.Path), "Fake agent");
        await Until(() => start.IsCompleted, "the agent to start");
        Assert.True(await start);
        return tab;
    }

    [AvaloniaFact]
    public async Task Without_an_agent_or_a_selection_the_command_says_so_and_sends_nothing()
    {
        var tab = _w.OpenFile(_temp.File("sample.txt", "one\ntwo\nthree\n"), null);
        tab.Editor!.Select(4, 3);
        await _w.AskAgentAboutSelectionAsync();
        Assert.Equal(Loc.Get(K.AgentAskNeedAgent), _w.StatusMessageText);
        Assert.Empty(_dialogs.Asked);

        var started = await OpenAndStartAsync("one\ntwo\nthree\n");
        started.Editor!.Select(0, 0);
        await _w.AskAgentAboutSelectionAsync();
        Assert.Equal(Loc.Get(K.AgentAskNoSelection), _w.StatusMessageText);
        Assert.Empty(_dialogs.Asked);
    }

    [AvaloniaFact]
    public async Task The_user_sees_what_is_sent_and_the_agent_gets_the_range_the_task_and_the_path()
    {
        var tab = await OpenAndStartAsync("one\ntwo\nthree\nfour\n");
        tab.Editor!.Select(4, 9); // "two\nthree"
        _dialogs.Prompts.Enqueue("make it shout");
        _dialogs.Answers.Enqueue(0); // send
        await _w.AskAgentAboutSelectionAsync();

        var confirm = Assert.Single(_dialogs.Asked);
        Assert.Contains("Fake agent", confirm.Message);
        Assert.Contains(confirm.Details!, d => d.Contains("2") && d.Contains("3") && d.Contains("9"));
        Assert.True(Acp.IsShared(tab));
        await Until(() => _w.AgentPanelText.Contains("echo:The user asks about lines 2-3 of the file " + tab.Path), "the message of the agent");
        string panel = _w.AgentPanelText;
        Assert.Contains("Task: make it shout", panel);
        Assert.Contains("two", panel);
        Assert.Contains("three", panel);
        Assert.DoesNotContain("one", panel.Split("echo:")[1]); // the line outside the range is not in the message
        Assert.Equal("one\ntwo\nthree\nfour\n", tab.Editor.Document.Text);
    }

    [AvaloniaFact]
    public async Task A_cancelled_confirmation_sends_nothing()
    {
        var tab = await OpenAndStartAsync("one\ntwo\n");
        tab.Editor!.Select(0, 3);
        _dialogs.Prompts.Enqueue("task");
        _dialogs.Answers.Enqueue(1);
        await _w.AskAgentAboutSelectionAsync();
        Assert.False(Acp.IsShared(tab));
        Assert.DoesNotContain("echo:", _w.AgentPanelText);
    }

    [AvaloniaFact]
    public async Task A_change_inside_the_range_goes_to_the_review_and_one_outside_it_is_refused()
    {
        var tab = await OpenAndStartAsync("alpha\nbeta\ngamma");
        _w.ToggleAgentPanel(true);
        string text = tab.Editor!.Document.Text;

        // outside the range: the first line is changed too
        var outside = Acp.AskRangeAsync(_w, tab, text, 6, 10, $"write {tab.Path}\nALPHA\nbeta\ngamma");
        await Until(() => outside.IsCompleted, "the turn");
        Assert.True(await outside);
        await Until(() => _w.AgentPanelText.Contains("WRITE-ERR:-32001:The question was about a range"), "the refused write to appear in the conversation");
        Assert.Contains("WRITE-ERR:-32001:The question was about a range", _w.AgentPanelText);
        Assert.Contains(Loc.Get(K.AgentAskOutside), _w.AgentPanelText);
        Assert.Null(_w.ResultsView);
        Assert.Equal(text, tab.Editor.Document.Text);

        // inside the range: shown as a hunk, applied only when the user applies it
        var inside = Acp.AskRangeAsync(_w, tab, text, 6, 10, $"write {tab.Path}\nalpha\ndelta\ngamma");
        await Until(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true, "the review");
        Assert.Equal(text, tab.Editor.Document.Text);
        _w.ApplyPlan();
        await Until(() => inside.IsCompleted, "the turn");
        await Until(() => _w.AgentPanelText.Contains("WRITE-OK"), "the accepted write to appear in the conversation");
        Assert.Contains("WRITE-OK", _w.AgentPanelText);
        Assert.Equal("alpha\ndelta\ngamma", tab.Editor.Document.Text);

        // after the turn the limit is gone: a later write of the whole file is not held to the old range
        var later = Acp.AskRangeAsync(_w, tab, tab.Editor.Document.Text, 0, 5, "echo");
        await Until(() => later.IsCompleted, "the turn");
        Assert.True(await later);
    }
}
