using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Search;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Search in files (P2.5): the folder and the open documents are searched by the search worker, the matches arrive in the "Search Results" tab while it runs,
/// a double click jumps to the match, and the same tab previews Replace in Files change by change and applies the chosen ones through the change service.
/// </summary>
internal sealed partial class MainWindow
{
    private const int FilesMaxResults = 10_000;

    private DocTab? _resultsTab;
    private SearchResultsView? _resultsView;
    private SearchRun? _filesRun;
    private CancellationTokenSource? _filesCancel;
    private int _filesGeneration;
    private ChangeProposal? _plan;
    private SearchPattern? _planPattern;
    private string _planReplacement = "", _planFolder = "";

    internal DocTab? ResultsTab => _resultsTab is { Group: not null } ? _resultsTab : null;
    internal SearchResultsView? ResultsView => ResultsTab is null ? null : _resultsView;

    private void ApplyResultsTexts() => _resultsView?.ApplyTexts();

    // ---- the strip in files mode ----

    /// <summary>Opens the strip for a search over a folder and the open documents (Ctrl+Shift+F), or for Replace in Files (Ctrl+Shift+H).</summary>
    internal CommandOutcome ShowFindInFiles(bool replace)
    {
        bool wasOpen = _find.IsVisible;
        _find.Open(files: true, replace);
        if (string.IsNullOrWhiteSpace(_find.FolderBox.Text))
            _find.FolderBox.Text = Active?.Path is { } p && System.IO.Path.GetDirectoryName(p) is { Length: > 0 } dir ? dir : Environment.CurrentDirectory;
        if (!wasOpen && Active is { Custom: null } tab) SetFindText(SelectedSingleLine(tab));
        var field = replace && !string.IsNullOrEmpty(_find.FindBox.Text) ? _find.ReplaceBox : _find.FindBox;
        field.Focus();
        field.SelectAll();
        return CommandOutcome.Handled;
    }

    private async Task BrowseFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Loc.Get(K.FindFolder), AllowMultiple = false });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) _find.FolderBox.Text = path;
    }

    /// <summary>The open documents the search covers: the unnamed ones and the ones under the searched folder; a file elsewhere is not part of a search of this folder.</summary>
    private IReadOnlyList<LiveBuffer> OpenBuffers(string folder, bool editableOnly) =>
        [.. _app.Windows.SelectMany(w => w.Documents).Where(d => !d.IsResults && (!editableOnly || !d.ReadOnly) && (d.Path is null || IsUnder(folder, d.Path))).DistinctBy(d => d.Doc.Id).Select(d => LiveBuffer.Of(d.Doc, d.Title))];

    private static bool IsUnder(string folder, string path) =>
        System.IO.Path.GetRelativePath(folder, path) is var relative && !relative.StartsWith("..", StringComparison.Ordinal) && !System.IO.Path.IsPathRooted(relative);

    private Document? FindOpenDocument(Guid id) => _app.Windows.SelectMany(w => w.Documents).FirstOrDefault(d => d.Doc.Id == id)?.Doc;

    private string? BatchJournalDirectory => _app.Journal.Directory is { } dir ? System.IO.Path.Combine(dir, "batches") : null;

    // ---- running a search or a preview ----

    /// <summary>Runs the search of the strip over the folder; with a replacement field shown it plans Replace in Files instead and shows the changes for review.</summary>
    internal async Task RunFilesSearchAsync()
    {
        var pattern = FindPattern();
        string folder = (_find.FolderBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(pattern.Text)) return;
        if (RegexTools.Validate(pattern) is { } error) { SetStatusMessage(Loc.Get(K.FindInvalid, ("reason", error))); return; }
        if (folder.Length == 0 || !Directory.Exists(folder)) { SetStatusMessage(Loc.Get(K.FindFolderMissing, ("path", folder))); return; }
        folder = System.IO.Path.GetFullPath(folder);
        await StartResults(pattern, folder, _find.ReplaceShown, _find.ReplaceBox.Text ?? "");
    }

    private (DocTab Tab, SearchResultsView View) EnsureResultsTab()
    {
        if (ResultsTab is { } existing && _resultsView is { } view)
        {
            SelectTab(existing);
            return (existing, view);
        }
        view = new SearchResultsView();
        var od = OpenDocument.NewResults();
        Adopt(od);
        var tab = new DocTab(od, Options, view);
        _activeGroup.Add(tab);
        OnActiveTabChanged();
        _app.SessionChanged();
        view.CancelRequested += CancelFromView;
        view.OpenRequested += OpenResult;
        view.ApplyRequested += () => ApplyPlan();
        view.RepreviewRequested += () => { if (_planPattern is { } p) _ = StartResults(p, _planFolder, true, _planReplacement); };
        _ = od.Closed.Task.ContinueWith(_ => Dispatcher.UIThread.Post(CancelFilesSearch));
        _resultsTab = tab;
        _resultsView = view;
        return (tab, view);
    }

    internal SearchRun? FilesRun => _filesRun;

    /// <summary>The user cancelled: the tab says so at once, the worker stops in the background.</summary>
    private void CancelFromView()
    {
        if (_resultsView is not { IsRunning: true } view) return;
        CancelFilesSearch();
        view.SetRunning(false);
        view.SetHeader(view.TitleText, Loc.Get(K.ResultsCancelled));
        UpdateStatus();
    }

    private void CancelFilesSearch()
    {
        _filesRun?.Cancel();
        _filesCancel?.Cancel();
    }

    private async Task StartResults(SearchPattern pattern, string folder, bool replace, string replacement)
    {
        CancelFilesSearch();
        int generation = ++_filesGeneration;
        var (tab, view) = EnsureResultsTab();
        view.Clear();
        _plan = null;
        view.Highlight = RegexTools.Create(pattern);
        string Display(string path) => DisplayPath(folder, path);
        string title = replace ? Loc.Get(K.ResultsTitleReplace, ("query", pattern.Text), ("text", replacement)) : Loc.Get(K.ResultsTitleSearch, ("query", pattern.Text));
        view.SetHeader(title, Loc.Get(K.FindSearching));
        view.SetRunning(true);
        UpdateStatus();
        var scope = new SearchScope([folder], OpenBuffers(folder, editableOnly: replace));
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (replace)
        {
            var cts = _filesCancel = new CancellationTokenSource();
            ReplacePlan plan;
            try
            {
                plan = await ReplacePlanner.PlanAsync(pattern, replacement, scope, new SearchSettings(), ct: cts.Token);
            }
            catch (OperationCanceledException)
            {
                if (generation == _filesGeneration) FinishResults(view, Loc.Get(K.ResultsCancelled));
                return;
            }
            if (generation != _filesGeneration) return;
            _plan = plan.Proposal;
            (_planPattern, _planReplacement, _planFolder) = (pattern, replacement, folder);
            view.AddPlan(plan.Proposal, Display);
            foreach (var skipped in plan.Skipped) view.AddNote(Loc.Get(K.ResultsNotPreviewed, ("text", skipped)), warning: true);
            long hunks = plan.Proposal.HunkCount;
            view.SetApplyVisible(hunks > 0, false);
            FinishResults(view, Summary(hunks, plan.Proposal.Files.Count, sw, Loc.Get(K.ResultsProposed, ("text", replacement))));
            return;
        }

        var run = _filesRun = SearchWorker.Start(pattern, scope, new SearchSettings { MaxResults = FilesMaxResults, StopAtLimit = true });
        await foreach (var batch in run.Batches.ReadAllAsync())
        {
            if (generation != _filesGeneration) return;
            view.AddHits(batch.Hits, Display);
            view.SetHeader(title, Summary(batch.TotalMatches, -1, sw, null));
        }
        var summary = await run.Completion;
        if (generation != _filesGeneration) return;
        foreach (var e in summary.Errors) view.AddNote(e, warning: true);
        if (summary.LimitReached) view.SetLimit(Loc.Get(K.ResultsLimit, ("shown", summary.HitsDelivered)));
        string text = summary.End == SearchEnd.Cancelled ? Loc.Get(K.ResultsCancelled) : Summary(summary.TotalMatches, summary.FilesMatched, sw, null);
        FinishResults(view, text);
        view.SetHeader(title, text);
    }

    private void FinishResults(SearchResultsView view, string summary)
    {
        view.SetRunning(false);
        view.SetHeader(view.TitleText, summary);
        UpdateStatus();
    }

    private static string Summary(long matches, long files, System.Diagnostics.Stopwatch sw, string? extra)
    {
        var parts = new List<string> { Loc.Plural(K.ResultsMatches, matches) };
        if (files >= 0) parts[0] += " " + Loc.Plural(K.ResultsInFiles, files);
        if (extra is not null) parts.Add(extra);
        parts.Add(Loc.Get(K.ResultsElapsed, ("seconds", sw.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture))));
        return string.Join(" · ", parts);
    }

    private static string DisplayPath(string folder, string path)
    {
        if (!System.IO.Path.IsPathRooted(path)) return path;
        var relative = System.IO.Path.GetRelativePath(folder, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative) ? path : relative.Replace('\\', '/');
    }

    // ---- opening a result ----

    internal void OpenResult(SearchResultsView.Row row)
    {
        (string? Path, Guid? Doc, int Line, int Column, int Length) at = row switch
        {
            SearchResultsView.HitRow h => (h.Hit.Source.Path, h.Hit.Source.DocumentId, h.Hit.Line, h.Hit.Column, _resultsView?.MeasureMatch(h) ?? 0),
            SearchResultsView.HunkRow h => (h.File.Change?.Path, h.File.Change?.DocumentId, h.Hunk.Line, h.Hunk.LineBefore.Length, h.Hunk.OldText.Length),
            SearchResultsView.FileRow f => (f.Change?.Path ?? (f.Key.Length > 0 && File.Exists(f.Key) ? f.Key : null), f.Change?.DocumentId, 1, 0, 0),
            _ => default,
        };
        try
        {
            DocTab? tab = null;
            if (at.Doc is { } id && _docs.FirstOrDefault(d => d.Doc.Id == id)?.Views.FirstOrDefault() is { } live) tab = SelectTab(live);
            else if (at.Path is { } path) tab = OpenFile(path, null);
            if (tab is null) return;
            tab.GoToLine(at.Line);
            if (tab.Surface is { } s && at.Length > 0 && at.Line <= s.Document.LineCount)
            {
                var line = s.Document.GetLineByNumber(at.Line);
                if (at.Column + at.Length <= line.Length) s.SetSelections([new Sel(line.Offset + at.Column, line.Offset + at.Column + at.Length)]);
            }
            FocusView(tab);
        }
        catch (IOException e)
        {
            SetStatusMessage(e.Message);
        }
    }

    // ---- applying a replacement ----

    /// <summary>Applies the changes that are still ticked. A file that changed after the preview is left alone and reported; the others go on.</summary>
    internal ApplyReport? ApplyPlan()
    {
        if (_plan is not { } plan || _resultsView is not { } view) return null;
        var selection = new ChangeSelection();
        var byKey = plan.Files.ToDictionary(f => f.Key);
        foreach (var row in view.Rows)
        {
            if (row is SearchResultsView.FileRow { Change: { } change, Selected: false }) selection.RejectFile(change);
            else if (row is SearchResultsView.HunkRow { Selected: false } h && h.File.Change is { } owner) selection.RejectHunk(owner, h.Hunk.Id);
        }
        string? journal = BatchJournalDirectory;
        if (journal is not null) Directory.CreateDirectory(journal);
        var report = new ChangeApplier(FindOpenDocument, journal).Apply(plan, selection);

        var rows = new List<SearchResultsView.Row>();
        foreach (var outcome in report.Files)
        {
            var change = byKey[outcome.Key];
            var head = new SearchResultsView.FileRow(change.Key, DisplayPath(_planFolder, change.DisplayName), outcome.HunksApplied + outcome.HunksSkipped, change)
            {
                Status = outcome.Status switch
                {
                    ChangeStatus.Applied => Loc.Plural(K.ResultsFileApplied, outcome.HunksApplied),
                    ChangeStatus.Rejected => Loc.Get(K.ResultsFileSkipped),
                    ChangeStatus.Conflict => Loc.Get(ConflictKey(outcome.Conflict)),
                    _ => Loc.Get(K.ResultsFileFailed),
                },
                Failed = outcome.Status is ChangeStatus.Conflict or ChangeStatus.Failed,
            };
            rows.Add(head);
            if (outcome.Message is { } message && outcome.Status == ChangeStatus.Failed) rows.Add(new SearchResultsView.NoteRow(message, warning: true));
        }
        view.ReplaceRows(rows);
        view.SetApplyVisible(false, report.Conflicts > 0);
        if (report.Conflicts > 0) view.AddNote(Loc.Get(K.ResultsConflictHint), warning: false);
        if (report.BatchId is null && journal is null && report.Applied > 0) view.AddNote(Loc.Get(K.ResultsNoJournal), warning: false);
        view.SetHeader(view.TitleText, Loc.Get(K.ResultsApplied, ("applied", report.Applied), ("replacements", report.HunksApplied), ("skipped", report.Rejected), ("conflicts", report.Conflicts), ("failed", report.Failed)));
        _plan = null;
        SetStatusMessage(Loc.Plural(K.FindReplaced, report.HunksApplied));
        return report;
    }

    private static string ConflictKey(ChangeConflict conflict) => conflict switch
    {
        ChangeConflict.StaleRevision => K.ResultsConflictStale,
        ChangeConflict.DocumentUnavailable => K.ResultsConflictStale,
        ChangeConflict.ExternalChange => K.ResultsConflictExternal,
        ChangeConflict.Missing => K.ResultsConflictMissing,
        ChangeConflict.EncodingLoss => K.ResultsConflictEncoding,
        _ => K.ResultsConflictInvalid,
    };

    // ---- batches a crash interrupted ----

    /// <summary>At start: a Replace in Files that was cut off by a crash is offered for finishing or rolling back, file by file.</summary>
    internal void CheckPendingBatches()
    {
        if (BatchJournalDirectory is not { } dir) return;
        var pending = BatchRecovery.Find(dir);
        if (pending.Count == 0) return;
        void Resolve(bool finish)
        {
            int files = 0;
            foreach (var batch in pending)
            {
                var outcomes = finish ? batch.Finish() : batch.RollBack();
                files += outcomes.Count(o => o.Status == ChangeStatus.Applied);
                batch.Discard();
                batch.Dispose();
            }
            DismissNotice("batch");
            SetStatusMessage(Loc.Plural(finish ? K.BatchFinished : K.BatchRolledBack, files));
        }
        ShowNotice("batch", Loc.Plural(K.BatchPending, pending.Count),
            [(Loc.Get(K.BatchFinish), () => Resolve(true)), (Loc.Get(K.BatchRollBack), () => Resolve(false))], warning: true);
    }

    // ---- regular expression tester ----

    internal void ShowRegexTester()
    {
        var window = new RegexTesterWindow(FindPattern());
        window.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.F1) { _app.OpenHelp("search.regex"); e.Handled = true; } };
        window.Show(this);
    }
}
