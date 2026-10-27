using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P8.4.3: a workspace edit in the change review: text edits, create, rename and delete in one preview, one tick per change, applied all or nothing; the preview of a new file,
/// the tabs of renamed and deleted files, and the batch a crash cut off.
/// </summary>
[Collection("LocState")]
public class WorkspaceEditTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public WorkspaceEditTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
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

    private static void Settle()
    {
        for (int i = 0; i < 20; i++) Dispatcher.UIThread.RunJobs();
    }

    private sealed record Setup(DocTab Tab, string Edited, string Created, string Old, string Renamed, string Deleted, ChangeProposal Plan);

    private Setup Prepare()
    {
        var tab = _w.OpenFile(_temp.File("open.txt", "alpha beta\n"), null);
        _w.UpdateLayout();
        Settle();
        string edited = _temp.File("edited.txt", "one two\n"), old = _temp.File("old.txt", "renamed content"), deleted = _temp.File("deleted.txt", "bye");
        string created = Path.Combine(_temp.Path, "created.txt"), renamed = Path.Combine(_temp.Path, "renamed.txt");
        FileChange Op(string path, FileOperation operation, FileFingerprint? print = null) => new(path, path, null, null, print, TextCodec.Utf8, [], operation);
        var plan = ChangeBuilder.Proposal("Rename symbol", "plugin:test",
            ChangeBuilder.ForDocument(tab.Doc.Doc, [new TextReplacement(new ByteRange(0, 5), "ALPHA")]),
            ChangeBuilder.ForFile(edited, [new TextReplacement(new ByteRange(0, 3), "ONE")]),
            Op(created, new FileOperation(FileOperationKind.Create, Content: "new file"u8.ToArray())),
            Op(old, new FileOperation(FileOperationKind.Rename, renamed)),
            Op(deleted, new FileOperation(FileOperationKind.Delete), FileFingerprint.TryRead(deleted))) with { Atomic = true };
        return new Setup(tab, edited, created, old, renamed, deleted, plan);
    }

    private Task<ProposalDecision> Review(Setup s) => _w.ReviewWorkspaceEditAsync(s.Plan, "w1", CancellationToken.None);

    private static (ProposalOutcome, int, int) Pair(ProposalDecision d) => (d.Outcome, d.Accepted, d.Total);

    private Task ShownAsync() => Until(() => _w.ResultsView is { } v && v.Rows.OfType<SearchResultsView.FileRow>().Count() == 5, "five files in the review");

    [AvaloniaFact]
    public async Task Edit_create_rename_and_delete_pass_the_preview_and_are_applied_together()
    {
        var s = Prepare();
        int steps = s.Tab.Doc.Doc.UndoDepth;

        var review = Review(s);
        await ShownAsync();
        Assert.False(review.IsCompleted);
        var files = _w.ResultsView!.Rows.OfType<SearchResultsView.FileRow>().ToList();
        Assert.Equal(2, _w.ResultsView.Rows.OfType<SearchResultsView.HunkRow>().Count()); // the file operations have no hunks, only their own tick
        Assert.Equal([null, null, Loc.Get(K.WorkspaceCreate), Loc.Get(K.WorkspaceRename, ("name", s.Renamed)), Loc.Get(K.WorkspaceDelete)], files.Select(f => f.Status));
        Assert.False(File.Exists(s.Created));
        Assert.True(File.Exists(s.Deleted));

        var report = _w.ApplyPlan();
        var decision = await review;

        Assert.Equal((ProposalOutcome.Applied, 5, 5), Pair(decision));
        Assert.Equal(5, report!.Applied);
        Assert.Equal("ALPHA beta\n", s.Tab.Editor!.Document.Text);
        Assert.Equal(steps + 1, s.Tab.Doc.Doc.UndoDepth);
        Assert.Equal("ONE two\n", File.ReadAllText(s.Edited));
        Assert.Equal("new file", File.ReadAllText(s.Created));
        Assert.False(File.Exists(s.Old));
        Assert.Equal("renamed content", File.ReadAllText(s.Renamed));
        Assert.False(File.Exists(s.Deleted));
        Assert.Empty(Directory.GetFiles(_temp.Path, "*.tmp"));
    }

    [AvaloniaFact]
    public async Task A_change_that_went_stale_during_the_review_stops_the_whole_batch()
    {
        var s = Prepare();
        var review = Review(s);
        await ShownAsync();
        File.WriteAllText(s.Edited, "someone else\n");

        var report = _w.ApplyPlan();
        var decision = await review;

        Assert.Equal(ProposalOutcome.Stale, decision.Outcome);
        Assert.Equal(1, report!.Conflicts);
        Assert.Equal("alpha beta\n", s.Tab.Editor!.Document.Text);
        Assert.Equal("someone else\n", File.ReadAllText(s.Edited));
        Assert.False(File.Exists(s.Created));
        Assert.True(File.Exists(s.Old));
        Assert.False(File.Exists(s.Renamed));
        Assert.True(File.Exists(s.Deleted));
    }

    [AvaloniaFact]
    public async Task A_document_that_moved_on_before_the_review_makes_the_edit_stale_at_once()
    {
        var s = Prepare();
        s.Tab.Editor!.Document.Insert(0, ">");
        Settle();

        var decision = await Review(s);

        Assert.Equal((ProposalOutcome.Stale, 0, 5), Pair(decision));
        Assert.False(File.Exists(s.Created));
        Assert.True(File.Exists(s.Deleted));
    }

    [AvaloniaFact]
    public async Task A_file_operation_that_is_unticked_is_left_out_and_the_rest_is_applied()
    {
        var s = Prepare();
        var review = Review(s);
        await ShownAsync();
        _w.ResultsView!.Rows.OfType<SearchResultsView.FileRow>().First(f => f.Status == Loc.Get(K.WorkspaceDelete)).Selected = false;

        _w.ApplyPlan();
        var decision = await review;

        Assert.Equal((ProposalOutcome.Partial, 4, 5), Pair(decision));
        Assert.True(File.Exists(s.Deleted));
        Assert.True(File.Exists(s.Created));
        Assert.True(File.Exists(s.Renamed));
    }

    private static FileChange Operation(string path, FileOperation operation, FileFingerprint? print = null) => new(path, path, null, null, print, TextCodec.Utf8, [], operation);

    private Task ShownFilesAsync(int count) => Until(() => _w.ResultsView is { } v && v.Rows.OfType<SearchResultsView.FileRow>().Count() == count, $"{count} files in the review");

    /// <summary>Shows the operations in the review, ticks nothing off and applies them.</summary>
    private async Task<ProposalDecision> ReviewAndApplyAsync(params FileChange[] files)
    {
        var review = _w.ReviewWorkspaceEditAsync(ChangeBuilder.Proposal("Operations", "plugin:test", files) with { Atomic = true }, "w9", CancellationToken.None);
        await ShownFilesAsync(files.Length);
        _w.ApplyPlan();
        return await review;
    }

    private DocTab OpenDirty(string path)
    {
        var tab = _w.OpenFile(path, null);
        _w.UpdateLayout();
        Settle();
        tab.Editor!.Document.Insert(0, ">");
        Settle();
        Assert.True(tab.Doc.HasUnsavedWork);
        return tab;
    }

    [AvaloniaFact]
    public async Task The_tab_of_a_renamed_file_follows_it_and_keeps_its_unsaved_text()
    {
        string old = _temp.File("open.txt", "alpha\n"), renamed = Path.Combine(_temp.Path, "moved.txt");
        var tab = OpenDirty(old);

        var decision = await ReviewAndApplyAsync(Operation(old, new FileOperation(FileOperationKind.Rename, renamed), FileFingerprint.TryRead(old)));

        Assert.Equal(ProposalOutcome.Applied, decision.Outcome);
        Assert.Equal(renamed, tab.Path);
        Assert.Equal("moved.txt", tab.Title);
        Assert.Equal(">alpha\n", tab.Editor!.Document.Text);
        Assert.True(tab.Doc.HasUnsavedWork);
        Assert.Contains(_w.Documents, d => d.Path == renamed);
        Assert.False(File.Exists(old));
        Assert.Equal("alpha\n", File.ReadAllText(renamed));
        Assert.Empty(_dialogs.Asked);
    }

    [AvaloniaFact]
    public async Task The_tab_of_a_deleted_file_without_unsaved_work_closes_without_a_question()
    {
        string path = _temp.File("clean.txt", "alpha\n");
        var tab = _w.OpenFile(path, null);
        _w.UpdateLayout();
        Settle();

        var decision = await ReviewAndApplyAsync(Operation(path, new FileOperation(FileOperationKind.Delete), FileFingerprint.TryRead(path)));

        Assert.Equal(ProposalOutcome.Applied, decision.Outcome);
        Assert.False(File.Exists(path));
        Assert.DoesNotContain(tab, _w.Tabs);
        Assert.DoesNotContain(_w.Documents, d => d.Path == path);
        Assert.Empty(_dialogs.Asked);
    }

    [AvaloniaFact]
    public async Task The_tab_of_a_deleted_file_with_unsaved_work_asks_and_can_stay_open()
    {
        string path = _temp.File("dirty.txt", "alpha\n");
        var tab = OpenDirty(path);
        _dialogs.Answers.Enqueue(1);

        var decision = await ReviewAndApplyAsync(Operation(path, new FileOperation(FileOperationKind.Delete), FileFingerprint.TryRead(path)));

        Assert.Equal(ProposalOutcome.Applied, decision.Outcome);
        Assert.Equal(Loc.Get(K.WorkspaceDeletedTitle), Assert.Single(_dialogs.Asked).Title);
        Assert.False(File.Exists(path));
        Assert.Contains(tab, _w.Tabs);
        Assert.Equal(">alpha\n", tab.Editor!.Document.Text);
    }

    [AvaloniaFact]
    public async Task The_tab_of_a_deleted_file_with_unsaved_work_closes_when_the_user_says_so()
    {
        string path = _temp.File("dirty.txt", "alpha\n");
        var tab = OpenDirty(path);
        _dialogs.Answers.Enqueue(0);

        await ReviewAndApplyAsync(Operation(path, new FileOperation(FileOperationKind.Delete), FileFingerprint.TryRead(path)));

        Assert.Single(_dialogs.Asked);
        Assert.False(File.Exists(path));
        Assert.DoesNotContain(tab, _w.Tabs);
        Assert.DoesNotContain(_w.Documents, d => d.Path == path);
    }

    [AvaloniaFact]
    public async Task The_review_previews_the_content_of_a_new_file()
    {
        string many = Path.Combine(_temp.Path, "many.txt"), blob = Path.Combine(_temp.Path, "blob.bin"), empty = Path.Combine(_temp.Path, "empty.txt");
        var plan = ChangeBuilder.Proposal("New files", "plugin:test",
            Operation(many, new FileOperation(FileOperationKind.Create, Content: Encoding.UTF8.GetBytes(string.Join("\n", Enumerable.Range(0, 40).Select(i => "line " + i))))),
            Operation(blob, new FileOperation(FileOperationKind.Create, Content: [(byte)'n', 0, (byte)'e'])),
            Operation(empty, new FileOperation(FileOperationKind.Create, Content: []))) with { Atomic = true };

        var review = _w.ReviewWorkspaceEditAsync(plan, "w9", CancellationToken.None);
        await ShownFilesAsync(3);

        var rows = _w.ResultsView!.Rows;
        Assert.Equal([typeof(SearchResultsView.FileRow), typeof(SearchResultsView.PreviewRow)], rows.Take(2).Select(r => r.GetType()));
        var previews = rows.OfType<SearchResultsView.PreviewRow>().ToList();
        Assert.Equal(3, previews.Count);
        Assert.Equal((PluginLimitsPlaceholders.NewFilePreviewLines, 40, "line 0"), (previews[0].Preview.Lines.Count, previews[0].Preview.TotalLines, previews[0].Preview.Lines[0]));
        Assert.True(previews[1].Preview.IsBinary);
        Assert.Equal(0, previews[2].Preview.TotalLines);
        Assert.False(File.Exists(many));

        string more = Loc.Get(K.WorkspacePreviewMore, ("count", 40 - PluginLimitsPlaceholders.NewFilePreviewLines));
        await Until(() =>
        {
            _w.UpdateLayout();
            return _w.ResultsView!.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == more);
        }, "the note about the lines that are not shown");
        Assert.Contains(_w.ResultsView!.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get(K.WorkspacePreviewBinary, ("bytes", 3L)));

        _w.ResultsView.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(ProposalOutcome.Rejected, (await review).Outcome);
        Assert.False(File.Exists(many));
    }

    private sealed class Crash : Exception;

    [AvaloniaFact]
    public void A_workspace_batch_cut_off_by_a_crash_is_offered_at_the_next_start_and_rolled_back()
    {
        string journal = Path.Combine(_app.Journal.Directory!, "batches");
        string old = _temp.File("old.txt", "content"), renamed = Path.Combine(_temp.Path, "renamed.txt"), created = Path.Combine(_temp.Path, "created.txt"), gone = _temp.File("gone.txt", "bye");
        var plan = ChangeBuilder.Proposal("Operations", "plugin:test",
            Operation(created, new FileOperation(FileOperationKind.Create, Content: "new"u8.ToArray())),
            Operation(old, new FileOperation(FileOperationKind.Rename, renamed), FileFingerprint.TryRead(old)),
            Operation(gone, new FileOperation(FileOperationKind.Delete), FileFingerprint.TryRead(gone))) with { Atomic = true };
        try
        {
            new ChangeApplier(_ => null, journal) { FaultPoint = p => { if (p == "after-op:1") throw new Crash(); } }.Apply(plan);
        }
        catch (Crash) { }
        Assert.True(File.Exists(created));
        Assert.True(File.Exists(renamed));

        _w.CheckPendingBatches();
        var notice = Assert.Single(_w.Notices, n => n.Message.Contains("dávk"));
        notice.Buttons.Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Roll back

        Assert.False(File.Exists(created));
        Assert.Equal("content", File.ReadAllText(old));
        Assert.False(File.Exists(renamed));
        Assert.Equal("bye", File.ReadAllText(gone));
        Assert.Empty(_w.Notices);
        Assert.Empty(BatchRecovery.Find(journal));
    }

    [AvaloniaFact]
    public async Task Cancelling_the_review_writes_nothing()
    {
        var s = Prepare();
        var review = Review(s);
        await ShownAsync();
        _w.ResultsView!.CancelButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

        Assert.Equal((ProposalOutcome.Rejected, 0, 5), Pair(await review));
        Assert.False(File.Exists(s.Created));
        Assert.Equal("alpha beta\n", s.Tab.Editor!.Document.Text);
    }
}
