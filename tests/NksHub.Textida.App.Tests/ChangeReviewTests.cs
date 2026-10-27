using System.Diagnostics;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Tests;

/// <summary>P6.1: a plugin proposal in the change review: accept or reject per hunk, a stale revision, one undo step, the bytes outside the accepted hunks.</summary>
[Collection("LocState")]
public class ChangeReviewTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public ChangeReviewTests()
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
}
