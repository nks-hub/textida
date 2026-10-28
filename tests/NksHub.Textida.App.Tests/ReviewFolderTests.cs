using System.Diagnostics;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.History;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P6.1.1 for the opened folder: a file that another program changes while no tab shows it waits for review with the content the history holds as its base; the list, the tab that takes the
/// record, a restart, the files that are left out and the writes of the editor itself.
/// </summary>
[Collection("LocState")]
public class ReviewFolderTests : IDisposable
{
    private readonly TempDir _temp = new();
    private App _app;
    private MainWindow _w;

    private const string Before = "one\ntwo\nthree\n";
    private const string After = "ONE\ntwo\nthree\nfour\n";

    public ReviewFolderTests()
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

    /// <summary>A folder with a file whose content the history holds, shown in the file panel.</summary>
    private async Task<(string Folder, string Path)> FolderWithSeenFile(string name = "a.txt", string content = Before)
    {
        string folder = Directory.CreateDirectory(Path.Combine(_temp.Path, "proj")).FullName;
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, content);
        Assert.Equal(HistoryStatus.Recorded, _app.History.Record(path, File.ReadAllBytes(path), HistorySource.Save).Status);
        Assert.True(await _w.ShowFolderAsync(folder));
        return (folder, path);
    }

    private static FolderChangeBatch Batch(params string[] files) => new(files, 0);

    [AvaloniaFact]
    public async Task A_file_changed_by_another_program_while_no_tab_shows_it_waits_for_review_with_the_seen_content_as_its_base()
    {
        var (_, path) = await FolderWithSeenFile();
        File.WriteAllText(path, After);

        await _w.ReviewFolderChangesAsync(Batch(path));

        var state = Assert.IsType<ReviewState>(_app.Reviews.Find(path));
        var pinned = Assert.Single(_app.History.List(path), r => r.Pinned);
        Assert.Equal(pinned.Id, state.BaseRevisionId);
        Assert.Equal(["review"], pinned.Pins);
        Assert.Equal(Encoding.UTF8.GetBytes(Before), _app.History.Read(path, pinned.Id));
        Assert.Equal(Loc.Plural(K.ReviewFolderWaiting, 1), _w.StatusMessageText);
        Assert.DoesNotContain(_w.Documents, d => d.Path == path); // no tab was opened for it
    }

    [AvaloniaFact]
    public async Task The_list_opens_the_file_with_its_marker_and_its_changes_and_reverting_them_ends_the_review()
    {
        var (_, path) = await FolderWithSeenFile();
        File.WriteAllText(path, After);
        await _w.ReviewFolderChangesAsync(Batch(path));

        Assert.Equal(CommandOutcomeHandled, _w.ShowFolderReviews());
        var row = Assert.Single(_w.QuickResults);
        Assert.Equal("a.txt", row.Title);
        row.Run();
        Dispatcher.UIThread.RunJobs();

        var tab = Assert.Single(_w.Tabs, t => t.Path == path); // the changes are listed in the results tab, which is the active one
        Assert.True(_w.Active!.Doc.IsResults);
        Assert.Equal("± 2", tab.Doc.ReviewBadge);
        Assert.Equal(2, _w.ResultsView!.Rows.OfType<SearchResultsView.HunkRow>().Count());

        _w.ResultsView.Rows.OfType<SearchResultsView.FileRow>().Single().Selected = false; // revert the whole file
        _w.ApplyPlan();
        await Until(() => File.ReadAllText(path) == Before && !tab.Doc.Doc.IsModified, "the base content to be saved");
        Assert.Null(_app.Reviews.Find(path));
        Assert.DoesNotContain(_app.History.List(path), r => r.Pinned);
        Assert.Empty(_app.Reviews.List());
    }

    private const NksHub.Textida.Workbench.Commands.CommandOutcome CommandOutcomeHandled = NksHub.Textida.Workbench.Commands.CommandOutcome.Handled;

    [AvaloniaFact]
    public async Task A_waiting_review_survives_a_restart_and_is_listed_again_when_the_folder_is_opened()
    {
        var (folder, path) = await FolderWithSeenFile();
        File.WriteAllText(path, After);
        await _w.ReviewFolderChangesAsync(Batch(path));

        _app = TestApp.Restart(_temp, _app);
        _w = _app.CreateWindow(null);
        Assert.True(await _w.ShowFolderAsync(folder));
        _w.ShowFolderReviews();
        Assert.Equal("a.txt", Assert.Single(_w.QuickResults).Title);

        var tab = _w.OpenFile(path, null);
        _w.UpdateLayout();
        Assert.Equal("± 2", tab.Doc.ReviewBadge);
        Assert.Contains(_app.History.List(path), r => r.Pinned);
    }

    [AvaloniaFact]
    public async Task Files_the_review_leaves_out_get_no_waiting_review()
    {
        var (folder, same) = await FolderWithSeenFile("same.txt");

        string known = Path.Combine(folder, "known.txt");
        File.WriteAllText(known, "v1\n");
        _app.History.Record(known, File.ReadAllBytes(known), HistorySource.Save);
        File.WriteAllText(known, "v2\n");
        _app.History.Record(known, File.ReadAllBytes(known), HistorySource.Save);
        File.WriteAllText(known, "v1\n"); // back to a content the history holds

        string unseen = Path.Combine(folder, "unseen.txt");
        File.WriteAllText(unseen, "never opened here\n");

        string secret = Path.Combine(folder, ".env");
        File.WriteAllText(secret, "API_KEY=abcdef123456\n");

        string binary = Path.Combine(folder, "blob.dat");
        File.WriteAllBytes(binary, Encoding.UTF8.GetBytes("text\n"));
        _app.History.Record(binary, File.ReadAllBytes(binary), HistorySource.Save);
        File.WriteAllBytes(binary, [.. Encoding.UTF8.GetBytes("text"), 0, 1, 2]);

        string open = Path.Combine(folder, "open.txt");
        File.WriteAllText(open, Before);
        _app.History.Record(open, File.ReadAllBytes(open), HistorySource.Save);
        _w.OpenFile(open, null);
        File.WriteAllText(open, After); // a tab shows it: the bar of the tab deals with it

        await _w.ReviewFolderChangesAsync(Batch(same, known, unseen, secret, binary, open, Path.Combine(folder, "gone.txt")));

        Assert.Empty(_app.Reviews.List());
        Assert.DoesNotContain(_app.History.List(same), r => r.Pinned);
    }

    [AvaloniaFact]
    public async Task A_change_during_a_write_of_the_editor_itself_is_not_a_change_from_outside()
    {
        var (_, path) = await FolderWithSeenFile();
        File.WriteAllText(path, After);

        using (_w.OwnWrite()) await _w.ReviewFolderChangesAsync(Batch(path));

        Assert.Null(_app.Reviews.Find(path));
    }

    [AvaloniaFact]
    public async Task A_second_change_keeps_the_first_base_and_does_not_start_another_review()
    {
        var (_, path) = await FolderWithSeenFile();
        File.WriteAllText(path, After);
        await _w.ReviewFolderChangesAsync(Batch(path));
        var first = _app.Reviews.Find(path)!;

        File.WriteAllText(path, After + "five\n");
        await _w.ReviewFolderChangesAsync(Batch(path));

        Assert.Equal(first, _app.Reviews.Find(path));
        Assert.Single(_app.History.List(path), r => r.Pinned);
        Assert.Single(_app.Reviews.List());
    }

    [AvaloniaFact]
    public async Task Without_an_opened_folder_and_without_waiting_files_the_list_says_so()
    {
        _w.ShowFolderReviews();
        Assert.Equal(Loc.Get(K.ReviewFolderNoFolder), _w.StatusMessageText);

        await FolderWithSeenFile();
        _w.ShowFolderReviews();
        Assert.Equal(Loc.Get(K.ReviewFolderNone), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public async Task The_watch_of_the_opened_folder_finds_a_file_that_another_program_writes()
    {
        var (_, path) = await FolderWithSeenFile();
        await Task.Delay(300); // the watcher is running

        File.WriteAllText(path, After);

        await Until(() => _app.Reviews.Find(path) is not null, "the changed file to wait for review");
        Assert.Equal(Loc.Plural(K.ReviewFolderWaiting, 1), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public void The_review_store_lists_the_records_it_can_read()
    {
        string a = _temp.File("a.txt"), b = _temp.File("b.txt");
        _app.Reviews.Save(new ReviewState(a, "20260101T000000000-00", DateTime.UtcNow));
        _app.Reviews.Save(new ReviewState(b, "20260102T000000000-00", DateTime.UtcNow));
        File.WriteAllText(Path.Combine(_app.ProfileDirectory, "reviews", "damaged.json"), "{ not json");

        Assert.Equal([a, b], _app.Reviews.List().Select(s => s.Path).Order());
        _app.Reviews.Remove(a);
        Assert.Equal([b], _app.Reviews.List().Select(s => s.Path));
    }
}
