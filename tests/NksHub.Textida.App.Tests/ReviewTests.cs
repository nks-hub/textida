using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>P6.1.1: a change another program makes to the file of an open tab, reviewed hunk by hunk; its marker, keys, pinned base and survival of a restart.</summary>
[Collection("LocState")]
public class ReviewTests : IDisposable
{
    private readonly TempDir _temp = new();
    private App _app;
    private MainWindow _w;

    private const string Before = "one\ntwo\nthree\nfour\nfive\n";
    private const string After = "ONE\ntwo\nthree\nFOUR\nfive\nsix\n";

    public ReviewTests()
    {
        _app = TestApp.Prepare(_temp);
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
            Assert.True(sw.ElapsedMilliseconds < 15_000, "Not reached: " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
    }

    private static string Text(DocTab t) => t.Editor!.Document.Text;

    /// <summary>The file changes behind the app; the app notices it as it does when its watcher fires.</summary>
    private static void ChangeOutside(DocTab tab, string path, string content)
    {
        File.WriteAllText(path, content);
        tab.Doc.Doc.CheckExternalChange();
        Dispatcher.UIThread.RunJobs();
    }

    private async Task<(DocTab Tab, string Path)> OpenAndChange()
    {
        var path = _temp.File("r.txt", Before);
        var tab = _w.OpenFile(path, null);
        _w.UpdateLayout();
        ChangeOutside(tab, path, After);
        Assert.True(await _w.StartReviewAsync(tab));
        return (tab, path);
    }

    [AvaloniaFact]
    public async Task The_bar_offers_the_review_and_the_tab_shows_the_changes_by_hunks_with_a_marker_and_a_pinned_base()
    {
        var path = _temp.File("r.txt", Before);
        var tab = _w.OpenFile(path, null);
        ChangeOutside(tab, path, After);
        var bar = Assert.IsType<InfoBar>(tab.Bar);
        Assert.Equal("Zkontrolovat změny", bar.Buttons.First().Content);
        Assert.Equal(Before, Text(tab)); // not applied silently

        Assert.True(await _w.StartReviewAsync(tab));

        Assert.Equal(After, Text(tab));
        Assert.Equal(3, _w.ResultsView!.Rows.OfType<SearchResultsView.HunkRow>().Count());
        Assert.Equal("± 3", tab.Doc.ReviewBadge);
        var pinned = Assert.Single(_app.History.List(path), r => r.Pinned);
        Assert.Equal(["review"], pinned.Pins);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(Before), _app.History.Read(path, pinned.Id));
        Assert.NotNull(_app.Reviews.Find(path));
    }

    [AvaloniaFact]
    public async Task Unticked_changes_go_back_as_one_undo_step_and_a_normal_save_and_no_other_byte_changes()
    {
        var (tab, path) = await OpenAndChange();
        int steps = tab.Doc.Doc.UndoDepth;
        var rows = _w.ResultsView!.Rows.OfType<SearchResultsView.HunkRow>().ToList();
        rows[1].Selected = false; // FOUR goes back to four

        var report = _w.ApplyPlan();

        Assert.Equal(1, report!.Applied);
        const string expected = "ONE\ntwo\nthree\nfour\nfive\nsix\n";
        Assert.Equal(expected, Text(tab));
        Assert.Equal(steps + 1, tab.Doc.Doc.UndoDepth);
        await Until(() => File.ReadAllText(path) == expected && !tab.Doc.Doc.IsModified, "the normal save");
        Assert.DoesNotContain(_app.History.List(path), r => r.Pinned);
        Assert.Null(_app.Reviews.Find(path));
        Assert.Null(tab.Doc.Review);
        Assert.Null(tab.Doc.ReviewBadge);

        _w.SelectTab(tab);
        _w.Undo();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(After, Text(tab));
    }

    [AvaloniaFact]
    public async Task Keeping_everything_leaves_the_text_and_the_file_and_ends_the_review()
    {
        var (tab, path) = await OpenAndChange();
        int steps = tab.Doc.Doc.UndoDepth;
        _w.ApplyPlan();
        Assert.Equal(After, Text(tab));
        Assert.Equal(After, File.ReadAllText(path));
        Assert.Equal(steps, tab.Doc.Doc.UndoDepth);
        Assert.Null(tab.Doc.Review);
        Assert.DoesNotContain(_app.History.List(path), r => r.Pinned);
    }

    [AvaloniaFact]
    public async Task Reverting_all_by_unticking_the_file_restores_the_base_bytes()
    {
        var (tab, path) = await OpenAndChange();
        _w.ResultsView!.Rows.OfType<SearchResultsView.FileRow>().Single().Selected = false;
        _w.ApplyPlan();
        await Until(() => File.ReadAllText(path) == Before, "the saved base");
        Assert.Equal(Before, Text(tab));
    }

    [AvaloniaFact]
    public async Task Typing_during_the_review_shows_the_changes_again_and_nothing_is_written_blindly()
    {
        var (tab, path) = await OpenAndChange();
        tab.Editor!.Document.Insert(0, ">");
        Dispatcher.UIThread.RunJobs();
        string typed = Text(tab);
        _w.ResultsView!.Rows.OfType<SearchResultsView.HunkRow>().First().Selected = false;

        _w.ApplyPlan();

        Assert.Equal(typed, Text(tab));
        Assert.NotNull(tab.Doc.Review);
        Assert.Equal(After, File.ReadAllText(path));
        Assert.NotEmpty(_w.ResultsView!.Rows.OfType<SearchResultsView.HunkRow>());
    }

    [AvaloniaFact]
    public async Task The_review_waits_across_a_restart_and_lists_the_same_changes_again()
    {
        var (_, path) = await OpenAndChange();

        _app = TestApp.Restart(_temp, _app);
        _w = _app.CreateWindow(null);
        var tab = _w.OpenFile(path, null);
        _w.UpdateLayout();

        Assert.Equal("± 3", tab.Doc.ReviewBadge);
        Assert.Contains(_app.History.List(path), r => r.Pinned);
        _w.OpenReview();
        Assert.Equal(3, _w.ResultsView!.Rows.OfType<SearchResultsView.HunkRow>().Count());
        _w.ResultsView.Rows.OfType<SearchResultsView.HunkRow>().Skip(2).Single().Selected = false;
        _w.ApplyPlan();
        await Until(() => File.ReadAllText(path) == "ONE\ntwo\nthree\nFOUR\nfive\n" && !tab.Doc.Doc.IsModified, "the saved result");
    }

    [AvaloniaFact]
    public async Task The_keys_move_between_the_changes_and_wrap_around()
    {
        var (tab, _) = await OpenAndChange();
        int Line() => tab.Editor!.CaretLineColumn().Line;
        _w.SelectTab(tab);
        tab.GoToLine(1);
        _w.ReviewStep(next: true);
        Assert.Equal(4, Line());
        _w.ReviewStep(next: true);
        Assert.Equal(6, Line());
        _w.ReviewStep(next: true);
        Assert.Equal(1, Line());
        _w.ReviewStep(next: false);
        Assert.Equal(6, Line());
    }

    [AvaloniaFact]
    public async Task With_auto_keep_on_the_tab_takes_the_change_without_a_review()
    {
        _app.Workbench.Set("review.autoKeep", JsonValue.Create(true));
        var path = _temp.File("a.txt", Before);
        var tab = _w.OpenFile(path, null);
        ChangeOutside(tab, path, After);
        await Until(() => Text(tab) == After, "the tab to take the change");
        Assert.Null(tab.Doc.Review);
        Assert.Null(tab.Bar);
    }

    [AvaloniaFact]
    public void A_tab_with_unsaved_edits_keeps_the_old_bar_without_a_review()
    {
        var path = _temp.File("d.txt", Before);
        var tab = _w.OpenFile(path, null);
        tab.Editor!.Document.Insert(0, "x");
        ChangeOutside(tab, path, After);
        var bar = Assert.IsType<InfoBar>(tab.Bar);
        Assert.Equal(["Znovu načíst", "Ponechat moje", "Porovnat"], bar.Buttons.Select(b => (string?)b.Content));
    }

    [AvaloniaFact]
    public async Task The_base_cannot_be_deleted_by_retention_while_the_review_waits()
    {
        var (_, path) = await OpenAndChange();
        _app.History.Policy = new NksHub.Textida.Core.History.HistoryPolicy { MaxRevisionsPerFile = 1 };
        for (int i = 0; i < 3; i++) _app.History.Record(path, System.Text.Encoding.UTF8.GetBytes("filler " + i), NksHub.Textida.Core.History.HistorySource.Manual);
        _app.History.Prune(path);
        var pinned = Assert.Single(_app.History.List(path), r => r.Pinned);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(Before), _app.History.Read(path, pinned.Id));
    }
}
