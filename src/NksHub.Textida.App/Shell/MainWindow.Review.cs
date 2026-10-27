using System.Security.Cryptography;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.History;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Review of changes another program made to the file of an open tab (P6.1.1). The tab takes the new content; the content from before is a revision of the local
/// history, pinned for as long as the review waits. The changes are listed in the Search Results tab, the ticked ones stay and the others are reverted to the
/// earlier content as one undo step, followed by a normal save. The record of a waiting review is kept in the profile, so it survives a restart.
/// </summary>
internal sealed partial class MainWindow
{
    private DocTab? _reviewTab;
    private ChangeProposal? _reviewRevert;

    private static bool ReviewTarget(DocTab tab) =>
        tab.Surface is not null && tab.Doc is { Path: { } path, IsLarge: false } od && !od.Doc.IsModified && !SecretValues.IsSecretFile(path);

    private bool TryPin(string path, string id)
    {
        try
        {
            _app.History.Pin(path, id, ExternalReview.PinOwner);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or HistoryFormatException or KeyNotFoundException or ArgumentException) { return false; }
    }

    /// <summary>
    /// Starts the review of the change on disk: keeps the content the tab shows as the base (pinned in the local history), lets the tab take the new content and lists the
    /// changes. False when nothing was started (the tab has unsaved edits, a secrets file, no text view, or the base could not be kept).
    /// </summary>
    internal async Task<bool> StartReviewAsync(DocTab tab)
    {
        var od = tab.Doc;
        if (!ReviewTarget(tab)) return false;
        string path = od.Path!;
        bool created = false;
        if (od.Review is null)
        {
            var doc = od.Doc;
            var baseBytes = doc.ReadBytes(new ByteRange(0, doc.ByteLength));
            string sha = Convert.ToHexString(SHA256.HashData(baseBytes));
            var revision = _app.History.List(path).FirstOrDefault(r => string.Equals(r.Sha256, sha, StringComparison.OrdinalIgnoreCase)) ?? _app.History.Record(path, baseBytes, HistorySource.Manual).Revision;
            var state = revision is null || !TryPin(path, revision.Id) ? null : new ReviewState(path, revision.Id, DateTime.UtcNow);
            try { if (state is not null) _app.Reviews.Save(state); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { state = null; }
            if (state is null)
            {
                if (revision is not null) TryUnpin(path, revision.Id);
                SetStatusMessage(Loc.Get(K.ReviewNoBase));
                return false;
            }
            od.Review = state;
            created = true;
        }
        if (!await ReloadAsync(tab))
        {
            if (created) EndReview(od);
            return false;
        }
        return ShowReview(tab);
    }

    private void TryUnpin(string path, string id)
    {
        try { _app.History.Unpin(path, id, ExternalReview.PinOwner); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or HistoryFormatException or KeyNotFoundException or ArgumentException) { /* the pin goes with the revision */ }
    }

    /// <summary>The review is over: the base is no longer pinned and its record is gone.</summary>
    private void EndReview(OpenDocument od)
    {
        if (od.Review is { } state)
        {
            TryUnpin(state.Path, state.BaseRevisionId);
            _app.Reviews.Remove(state.Path);
        }
        od.Review = null;
        od.ReviewCount = 0;
        RefreshHeaders();
    }

    private byte[]? ReadBase(ReviewState state)
    {
        try { return _app.History.Read(state.Path, state.BaseRevisionId); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or HistoryFormatException or KeyNotFoundException or ArgumentException) { return null; }
    }

    /// <summary>A document opened (or restored) whose file has a review waiting from an earlier run: the tab shows the marker again.</summary>
    private void AttachReview(OpenDocument od)
    {
        if (od.Path is not { } path || od.IsLarge || _app.Reviews.Find(path) is not { } state) return;
        var revert = ReadBase(state) is { } baseBytes ? ExternalReview.BuildRevert(od.Doc, baseBytes) : null;
        if (revert is null)
        {
            TryUnpin(path, state.BaseRevisionId);
            _app.Reviews.Remove(path);
            return;
        }
        od.Review = state;
        od.ReviewCount = revert.Hunks.Count;
    }

    /// <summary>Lists the changes of the waiting review of the tab in the review tab. False when there is nothing to list or another review is open.</summary>
    private bool ShowReview(DocTab tab)
    {
        var od = tab.Doc;
        if (od.Review is not { } state) return false;
        if (ReadBase(state) is not { } baseBytes)
        {
            EndReview(od);
            SetStatusMessage(Loc.Get(K.ReviewNoBase));
            return false;
        }
        var revert = ExternalReview.BuildRevert(od.Doc, baseBytes);
        if (revert is null)
        {
            EndReview(od);
            SetStatusMessage(Loc.Get(K.ReviewSame));
            return false;
        }
        od.ReviewCount = revert.Hunks.Count;
        RefreshHeaders();
        if (ReviewOpen)
        {
            SetStatusMessage(Loc.Get(K.ReviewBusy));
            return false;
        }

        CancelFilesSearch();
        ++_filesGeneration;
        var (_, view) = EnsureResultsTab();
        view.Clear();
        view.SetRunning(false);
        view.Highlight = null;
        string title = Loc.Get(K.ReviewTitle, ("title", tab.Title));
        var shown = ChangeBuilder.Proposal(title, "review", ExternalReview.AsChange(revert));
        _plan = shown;
        _reviewRevert = ChangeBuilder.Proposal(title, "review", revert);
        _reviewTab = tab;
        _planPattern = null;
        _planFolder = System.IO.Path.GetDirectoryName(state.Path) ?? "";
        view.AddPlan(shown, _ => tab.Title);
        view.SetHeader(title, Loc.Get(K.ReviewHint));
        view.SetApplyVisible(true, false);
        view.SetCancelVisible(true);
        ShowQueueNote();
        UpdateStatus();
        return true;
    }

    private void DropReview()
    {
        if (_reviewTab is null) return;
        _reviewTab = null;
        _reviewRevert = null;
        _plan = null;
    }

    /// <summary>
    /// Keeps the ticked changes and reverts the others to the earlier content: one undo step, the bytes outside the reverted changes untouched, then a normal save
    /// (which checks that the file is still the one that was reviewed). A text that changed during the review shows the changes again from the new text.
    /// </summary>
    private ApplyReport? ApplyReview()
    {
        if (_reviewTab is not { } tab || _reviewRevert is not { } revert || _resultsView is not { } view) return null;
        var od = tab.Doc;
        var file = revert.Files[0];
        var selection = new ChangeSelection();
        bool fileKept = view.Rows.OfType<SearchResultsView.FileRow>().FirstOrDefault()?.Selected ?? true;
        int kept = 0;
        foreach (var row in view.Rows.OfType<SearchResultsView.HunkRow>())
        {
            if (!fileKept || !row.Selected) continue;
            selection.RejectHunk(file, row.Hunk.Id);
            kept++;
        }
        bool wasClean = !od.Doc.IsModified;
        var report = new ChangeApplier(FindOpenDocument).Apply(revert, selection);
        var outcome = report.Files[0];
        if (outcome.NeedsRepreview)
        {
            DropReview();
            ShowReview(tab);
            SetStatusMessage(Loc.Get(K.ReviewStale));
            return report;
        }
        if (outcome.Status is ChangeStatus.Conflict or ChangeStatus.Failed)
        {
            SetStatusMessage(Loc.Get(outcome.Status == ChangeStatus.Conflict ? ConflictKey(outcome.Conflict) : K.ResultsFileFailed));
            return report;
        }

        int reverted = outcome.HunksApplied;
        DropReview();
        EndReview(od);
        view.Clear();
        view.SetRunning(false);
        string done = Loc.Get(K.ReviewDone, ("kept", file.Hunks.Count - reverted), ("reverted", reverted));
        view.SetHeader(view.TitleText, done);
        SetStatusMessage(done);
        if (reverted > 0 && wasClean) _ = SaveReviewedAsync(tab);
        DrainProposals();
        return report;
    }

    private async Task SaveReviewedAsync(DocTab tab)
    {
        if (!await SaveAsync(tab)) SetStatusMessage(Loc.Get(K.ReviewNotSaved, ("reason", Loc.Get(K.ResultsFileFailed))));
    }

    /// <summary>Lists the waiting changes of the active tab again (also after a restart).</summary>
    internal CommandOutcome OpenReview()
    {
        if (Active is not { Surface: not null } tab) return CommandOutcome.NotHandled;
        if (tab.Doc.Review is null) SetStatusMessage(Loc.Get(K.ReviewNone));
        else ShowReview(tab);
        return CommandOutcome.Handled;
    }

    /// <summary>Moves the caret to the next or previous change from outside that still waits for review, wrapping around.</summary>
    internal CommandOutcome ReviewStep(bool next)
    {
        if (Active is not { Surface: { } surface } tab) return CommandOutcome.NotHandled;
        if (tab.Doc.Review is not { } state || ReadBase(state) is not { } baseBytes)
        {
            SetStatusMessage(Loc.Get(K.ReviewNone));
            return CommandOutcome.Handled;
        }
        if (ExternalReview.BuildRevert(tab.Doc.Doc, baseBytes) is not { } revert)
        {
            SetStatusMessage(Loc.Get(K.ReviewSame));
            return CommandOutcome.Handled;
        }
        var lines = revert.Hunks.Select(h => h.Line).Order().ToList();
        int caret = surface.CaretLineColumn().Line;
        int line = next ? lines.FirstOrDefault(l => l > caret, lines[0]) : lines.LastOrDefault(l => l < caret, lines[^1]);
        tab.GoToLine(line);
        return CommandOutcome.Handled;
    }
}
