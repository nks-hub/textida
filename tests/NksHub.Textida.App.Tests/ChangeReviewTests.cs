using System.Diagnostics;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Tests;

/// <summary>P6.1: a plugin proposal in the change review: accept or reject per hunk, a stale revision, one undo step, the bytes outside the accepted hunks, a plugin that goes away meanwhile.</summary>
[Collection("LocState")]
public class ChangeReviewTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public ChangeReviewTests()
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

    private const string Original = "alpha ěšč beta gamma delta\n";

    private DocTab Open(string content = Original)
    {
        var tab = _w.OpenFile(_temp.File("t.txt", content), null);
        _w.UpdateLayout();
        Settle();
        return tab;
    }

    /// <summary>Replaces "alpha", "beta" and "delta" with upper case text.</summary>
    private static EditProposal Proposal(DocTab tab, long? revision = null)
    {
        var doc = tab.Doc.Doc;
        var bytes = doc.ReadBytes(new ByteRange(0, doc.ByteLength));
        ByteEdit Edit(string word, string text)
        {
            long start = Encoding.UTF8.GetString(bytes).IndexOf(word, StringComparison.Ordinal);
            start = Encoding.UTF8.GetByteCount(Encoding.UTF8.GetString(bytes)[..(int)start]);
            return new ByteEdit(new ByteRange(start, start + word.Length), Encoding.UTF8.GetBytes(text));
        }
        return new EditProposal("test", doc.Id, revision ?? doc.Revision, [Edit("alpha", "ALPHA"), Edit("beta", "BETA"), Edit("delta", "DELTA")], "Upper");
    }

    private Task<ProposalDecision> Review(DocTab tab, EditProposal proposal) => _w.ReviewPluginProposalAsync(proposal, tab, "p1", CancellationToken.None);

    private static string Text(DocTab t) => t.Editor!.Document.Text;
    private static int Steps(DocTab t) => t.Doc.Doc.UndoDepth - 1;

    [AvaloniaFact]
    public async Task Accepted_hunks_are_written_as_one_undo_step_and_the_rejected_one_and_every_other_byte_stay()
    {
        var tab = Open();
        var before = tab.Doc.Doc.ReadBytes(new ByteRange(0, tab.Doc.Doc.ByteLength));
        int steps = Steps(tab);

        var review = Review(tab, Proposal(tab));
        await Until(() => _w.ResultsView is { } v && v.Rows.OfType<SearchResultsView.HunkRow>().Count() == 3, "three hunks in the review");
        Assert.False(review.IsCompleted);
        Assert.Equal(Original, Text(tab));
        var hunks = _w.ResultsView!.Rows.OfType<SearchResultsView.HunkRow>().ToList();
        hunks[1].Selected = false; // beta stays

        _w.ApplyPlan();
        var decision = await review;

        Assert.Equal((ProposalOutcome.Partial, 2, 3), (decision.Outcome, decision.Accepted, decision.Total));
        Assert.Equal("ALPHA ěšč beta gamma DELTA\n", Text(tab));
        Assert.Equal(Encoding.UTF8.GetBytes("ALPHA ěšč beta gamma DELTA\n"), tab.Doc.Doc.ReadBytes(new ByteRange(0, tab.Doc.Doc.ByteLength)));
        Assert.Equal(steps + 1, Steps(tab));
        _w.SelectTab(tab);
        _w.Undo();
        Settle();
        Assert.Equal(Original, Text(tab));
        Assert.Equal(before, tab.Doc.Doc.ReadBytes(new ByteRange(0, tab.Doc.Doc.ByteLength)));
    }

    [AvaloniaFact]
    public async Task Accepting_every_hunk_answers_applied()
    {
        var tab = Open();
        var review = Review(tab, Proposal(tab));
        await Until(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true, "the review");
        _w.ApplyPlan();
        var decision = await review;
        Assert.Equal((ProposalOutcome.Applied, 3, 3), (decision.Outcome, decision.Accepted, decision.Total));
        Assert.Equal("ALPHA ěšč BETA gamma DELTA\n", Text(tab));
    }

    [AvaloniaFact]
    public async Task Rejecting_all_hunks_or_cancelling_writes_nothing()
    {
        var tab = Open();
        int steps = Steps(tab);
        var review = Review(tab, Proposal(tab));
        await Until(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true, "the review");
        foreach (var row in _w.ResultsView!.Rows.OfType<SearchResultsView.HunkRow>()) row.Selected = false;
        _w.ApplyPlan();
        Assert.Equal((ProposalOutcome.Rejected, 0, 3), Pair(await review));

        var again = Review(tab, Proposal(tab));
        await Until(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true, "the second review");
        Assert.True(_w.ResultsView!.CancelButton.IsVisible);
        _w.ResultsView.CancelButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.Equal((ProposalOutcome.Rejected, 0, 3), Pair(await again));

        Assert.Equal(Original, Text(tab));
        Assert.Equal(steps, Steps(tab));
    }

    private static (ProposalOutcome, int, int) Pair(ProposalDecision d) => (d.Outcome, d.Accepted, d.Total);

    [AvaloniaFact]
    public async Task A_proposal_for_an_older_revision_is_stale_and_writes_nothing()
    {
        var tab = Open();
        var proposal = Proposal(tab);
        tab.Editor!.Document.Insert(0, ">");
        Settle();
        string typed = Text(tab);

        var decision = await Review(tab, proposal);

        Assert.Equal(ProposalOutcome.Stale, decision.Outcome);
        Assert.Equal(typed, Text(tab));
    }

    [AvaloniaFact]
    public async Task A_document_that_changes_while_the_review_is_open_makes_the_decision_stale()
    {
        var tab = Open();
        var review = Review(tab, Proposal(tab));
        await Until(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true, "the review");
        tab.Editor!.Document.Insert(0, ">");
        Settle();
        string typed = Text(tab);

        var report = _w.ApplyPlan();
        var decision = await review;

        Assert.Equal(ProposalOutcome.Stale, decision.Outcome);
        Assert.Equal(1, report!.Conflicts);
        Assert.Equal(typed, Text(tab));
    }

    private DocTab OpenSecond()
    {
        var tab = _w.OpenFile(_temp.File("second.txt", Original), null);
        _w.UpdateLayout();
        Settle();
        return tab;
    }

    [AvaloniaFact]
    public async Task A_proposal_that_arrives_while_a_review_is_open_waits_and_the_open_review_keeps_its_ticks_and_content()
    {
        var first = Open();
        var second = OpenSecond();
        var reviewA = Review(first, Proposal(first));
        await Until(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Count() == 3, "the first review");
        var rows = _w.ResultsView!.Rows.OfType<SearchResultsView.HunkRow>().ToList();
        rows[1].Selected = false;
        var title = _w.ResultsView.TitleText;

        var reviewB = _w.ReviewPluginProposalAsync(Proposal(second), second, "p2", CancellationToken.None);
        Settle();

        Assert.False(reviewB.IsCompleted);
        Assert.Equal(title, _w.ResultsView.TitleText);
        Assert.Equal(rows, _w.ResultsView.Rows.OfType<SearchResultsView.HunkRow>().ToList());
        Assert.False(rows[1].Selected);
        Assert.Contains("1", _w.ResultsView.LimitText);

        _w.ApplyPlan();
        Assert.Equal((ProposalOutcome.Partial, 2, 3), Pair(await reviewA));
        // the next one opens by itself, with nothing waiting behind it
        await Until(() => _w.ResultsView!.Rows.OfType<SearchResultsView.HunkRow>().Any(h => h.File.Change?.DocumentId == second.Doc.Doc.Id), "the waiting proposal");
        Assert.Equal("", _w.ResultsView!.LimitText);
        Assert.Equal(Original, Text(second));
        _w.ApplyPlan();
        Assert.Equal((ProposalOutcome.Applied, 3, 3), Pair(await reviewB));
        Assert.Equal("ALPHA ěšč BETA gamma DELTA\n", Text(second));
    }

    [AvaloniaFact]
    public async Task A_replace_in_files_preview_is_not_replaced_by_a_proposal_either()
    {
        var dir = Path.Combine(_temp.Path, "tree");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "x.txt"), "needle one\nneedle two\n");
        _w.ShowFindInFiles(true);
        _w.FindBar.FolderBox.Text = dir;
        _w.FindBar.FindBox.Text = "needle";
        _w.FindBar.ReplaceBox.Text = "pin";
        await _w.RunFilesSearchAsync();
        await Until(() => !_w.ResultsView!.IsRunning && _w.ResultsView.Rows.OfType<SearchResultsView.HunkRow>().Count() == 2, "the replace preview");
        var rows = _w.ResultsView!.Rows.ToList();
        rows.OfType<SearchResultsView.HunkRow>().First().Selected = false;

        var tab = Open();
        var review = Review(tab, Proposal(tab));
        Settle();

        Assert.False(review.IsCompleted);
        Assert.Equal(rows, _w.ResultsView.Rows.ToList());
        Assert.False(rows.OfType<SearchResultsView.HunkRow>().First().Selected);

        _w.ApplyPlan();
        Assert.Equal("needle one\npin two\n", File.ReadAllText(Path.Combine(dir, "x.txt")));
        await Until(() => _w.ResultsView!.Rows.OfType<SearchResultsView.HunkRow>().Count() == 3, "the proposal after the preview");
        _w.ResultsView!.CancelButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.Equal(ProposalOutcome.Rejected, (await review).Outcome);
    }

    [AvaloniaFact]
    public async Task A_waiting_proposal_whose_document_moved_on_is_answered_as_stale()
    {
        var first = Open();
        var second = OpenSecond();
        var reviewA = Review(first, Proposal(first));
        await Until(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true, "the first review");
        var reviewB = Review(second, Proposal(second));
        second.Editor!.Document.Insert(0, ">");
        Settle();

        _w.ApplyPlan();
        await reviewA;

        Assert.Equal(ProposalOutcome.Stale, (await reviewB).Outcome);
        Assert.StartsWith(">", Text(second));
    }

    // ---- P3b.3.2: the plugin goes away (crash, restart, revoked permission) while the user decides ----

    [AvaloniaFact]
    public async Task A_proposal_whose_plugin_went_away_while_the_review_was_open_writes_nothing()
    {
        var tab = Open();
        int steps = Steps(tab);
        using var gone = new CancellationTokenSource();
        var review = _w.ReviewPluginProposalAsync(Proposal(tab), tab, "p1", gone.Token);
        await Until(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true, "the review");

        gone.Cancel();
        await Until(() => review.IsCompleted, "the answer");

        Assert.Equal((ProposalOutcome.Rejected, 0, 3), Pair(await review));
        Assert.Null(_w.ApplyPlan()); // a late click on Apply finds nothing to write
        Assert.Equal(Original, Text(tab));
        Assert.Equal(steps, Steps(tab));
    }

    [AvaloniaFact]
    public async Task A_byte_proposal_whose_plugin_went_away_while_the_inspector_asked_writes_nothing_even_when_applied()
    {
        var tab = Open();
        var doc = tab.Doc.Doc;
        int steps = Steps(tab);
        using var gone = new CancellationTokenSource();
        _dialogs.WhileAsking = gone.Cancel;
        _dialogs.Answers.Enqueue(0); // Apply, pressed as the plugin ends

        var decision = await _w.ReviewPluginProposalAsync(new EditProposal("test", doc.Id, doc.Revision, [new ByteEdit(new ByteRange(0, 1), new byte[] { 0x41 })], "Bytes", RawBytes: true), tab, "p1", gone.Token);

        Assert.Single(_dialogs.Asked);
        Assert.Equal(ProposalOutcome.Rejected, decision.Outcome);
        Assert.Equal(Original, Text(tab));
        Assert.Equal(steps, Steps(tab));
    }
}
