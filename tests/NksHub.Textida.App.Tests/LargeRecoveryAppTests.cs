using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Journal;

namespace NksHub.Textida.App.Tests;

/// <summary>P7.1.4: changes too large to replay at start wait behind a bar; the user restores them (read in the background) or discards them.</summary>
[Collection("LocState")]
public class LargeRecoveryAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private App _app;

    public LargeRecoveryAppTests()
    {
        JournalService.AskBeforeRecoverBytes = 4096;
        _app = TestApp.Prepare(_temp, _dialogs);
    }

    public void Dispose()
    {
        JournalService.AskBeforeRecoverBytes = JournalPlaceholders.AskBeforeRecoverBytes;
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static readonly string Typed = new('x', 10_000);

    /// <summary>A named file with 10 000 characters typed in front of it (more than the threshold of this test), then the end the way a crash leaves it.</summary>
    private string EditAndCrash(out MainWindow restarted)
    {
        var w = _app.CreateWindow(null);
        string path = _temp.File("big.txt", "start\n");
        var tab = w.OpenFile(path, null);
        tab.Editor!.Document.Insert(0, Typed);
        _app.SaveSession();
        _app.Journal.Flush();
        _app = TestApp.Restart(_temp, _app, _dialogs);
        restarted = _app.CreateWindow(null);
        restarted.RestoreFrom(_app.Journal);
        return path;
    }

    private static InfoBar PendingBar(MainWindow w) => Assert.Single(w.Notices, n => n.Message.Contains("big.txt", StringComparison.Ordinal));

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task Eventually(Func<bool> condition)
    {
        for (int i = 0; i < 400 && !condition(); i++)
        {
            await Task.Delay(25);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(condition());
    }

    [AvaloniaFact]
    public async Task Large_changes_wait_behind_a_bar_and_a_click_restores_them_without_touching_the_file()
    {
        string path = EditAndCrash(out var w);

        var pending = Assert.Single(_app.Journal.Pending);
        Assert.Equal(RecoveryStatus.Pending, pending.Status);
        Assert.Empty(w.Tabs); // nothing was replayed at start
        var bar = PendingBar(w);
        Assert.Single(w.Notices); // the restore bar says nothing about documents that wait
        Assert.Equal(2, bar.Buttons.Count());

        Click(bar.Buttons.First());
        await Eventually(() => w.Tabs.Any(t => t.Path == path));

        var tab = w.Tabs.Single(t => t.Path == path);
        Assert.Equal(Typed + "start\n", tab.Editor!.Document.Text);
        Assert.True(tab.Doc.Doc.IsModified);
        Assert.NotNull(_app.Journal.Session.IdOf(tab.Doc.Doc));
        Assert.Empty(_app.Journal.Pending);
        Assert.Empty(w.Notices);
        Assert.Equal("start\n", File.ReadAllText(path));
    }

    [AvaloniaFact]
    public async Task Discarding_asks_once_more_and_removes_the_changes_from_the_journal()
    {
        string path = EditAndCrash(out var w);
        var bar = PendingBar(w);

        _dialogs.Answers.Enqueue(1); // Cancel keeps everything
        Click(bar.Buttons.Last());
        await Task.Delay(100);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(_dialogs.Asked);
        Assert.Equal("Zahodit neuložené změny?", _dialogs.Asked[0].Title);
        Assert.Single(_app.Journal.Pending);
        PendingBar(w);

        _dialogs.Answers.Enqueue(0);
        Click(bar.Buttons.Last());
        await Eventually(() => _app.Journal.Pending.Count == 0);
        Assert.Equal(2, _dialogs.Asked.Count);
        Assert.Empty(w.Notices);
        Assert.Empty(w.Tabs);
        Assert.Equal("start\n", File.ReadAllText(path));

        var again = Crash();
        again.RestoreFrom(_app.Journal);
        Assert.Empty(_app.Journal.Recovery.Documents);
        Assert.Empty(again.Tabs);
    }

    [AvaloniaFact]
    public void A_bar_that_is_closed_asks_again_at_the_next_start()
    {
        EditAndCrash(out var w);
        var bar = PendingBar(w);
        w.DismissNotice("recover:" + Assert.Single(_app.Journal.Pending).DocId.ToString("N"));
        Assert.Empty(w.Notices);
        Assert.NotNull(bar);

        var again = Crash();
        again.RestoreFrom(_app.Journal);
        Assert.Single(_app.Journal.Pending);
        PendingBar(again);
    }

    [AvaloniaFact]
    public void Small_changes_come_back_at_start_as_before()
    {
        var w = _app.CreateWindow(null);
        string path = _temp.File("small.txt", "start\n");
        w.OpenFile(path, null).Editor!.Document.Insert(0, "tiny ");

        var again = Crash();
        again.RestoreFrom(_app.Journal);
        Assert.Empty(_app.Journal.Pending);
        Assert.Equal("tiny start\n", again.Tabs.Single(t => t.Path == path).Editor!.Document.Text);
    }

    private MainWindow Crash()
    {
        _app.SaveSession();
        _app.Journal.Flush();
        _app = TestApp.Restart(_temp, _app, _dialogs);
        return _app.CreateWindow(null);
    }
}
