using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Tests;

/// <summary>P8.4.3: a workspace edit in the change review: text edits, create, rename and delete in one preview, one tick per change, applied all or nothing.</summary>
[Collection("LocState")]
public class WorkspaceEditTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public WorkspaceEditTests()
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
