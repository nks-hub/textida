using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Search;
using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Find and replace in the active document (P2.4): the strip above the status bar, incremental search on the search worker over the live revision,
/// next and previous match, replace one, all or in the selection through the change service (one undo step), go to line, percent or byte and go to the
/// matching bracket.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>Matches kept for stepping through; more are counted but not listed.</summary>
    private const int FindMaxHits = 100_000;

    private readonly FindBar _find = new();
    private readonly DispatcherTimer _findDebounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _findProgress = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private SearchRun? _findRun;
    private Task _findCollect = Task.CompletedTask;
    private int _findGeneration;
    private List<SearchHit> _hits = [];
    private long _hitsRevision = -1, _hitsTotal, _findRunRevision = -1;
    private Guid _hitsDocument;
    private bool _findBusy, _findSeeding;
    private int _findOrigin;
    private Anchor? _scopeStart, _scopeEnd;
    private OpenDocument? _findSubscribed;
    private string _findSignature = "";

    internal FindBar FindBar => _find;
    internal IReadOnlyList<SearchHit> FindHits => _hits;
    internal bool FindIsRunning => FindBusy;
    internal SearchRun? FindRun => _findRun;

    /// <summary>A search of the active document runs: the bar shows its progress and the Cancel button meanwhile.</summary>
    private bool FindBusy
    {
        get => _findBusy;
        set
        {
            _findBusy = value;
            if (value) _findProgress.Start();
            else _findProgress.Stop();
            _find.SetRunning(value);
        }
    }

    /// <summary>The matches of the document's current text are known, or the running search collects them.</summary>
    private bool FindCoversText(Document doc) => _hitsDocument == doc.Id && (FindBusy ? _findRunRevision : _hitsRevision) == doc.Revision;

    private Control BuildFindBar()
    {
        _find.CloseRequested += HideFind;
        _find.QueryChanged += OnFindQueryChanged;
        // through the registry, so a macro being recorded records Enter in the bar like F3, and Replace and Replace All
        _find.NextRequested += () => _ = _app.Workbench.Commands.ExecuteAsync("nks.search.findNext", new ShellContext(this, forMenu: true));
        _find.PreviousRequested += () => _ = _app.Workbench.Commands.ExecuteAsync("nks.search.findPrevious", new ShellContext(this, forMenu: true));
        _find.ReplaceOneRequested += () => _ = _app.Workbench.Commands.ExecuteAsync("nks.search.replaceNext", new ShellContext(this, forMenu: true));
        _find.ReplaceAllRequested += () => _ = _app.Workbench.Commands.ExecuteAsync("nks.search.replaceAll", new ShellContext(this, forMenu: true));
        _find.GoRequested += () => _ = RunFilesSearchAsync();
        _find.TryRequested += ShowRegexTester;
        _find.ListRequested += () => _ = ListFindResultsAsync();
        _find.StopRequested += StopFind;
        _findProgress.Tick += (_, _) =>
        {
            if (_findRun is { Progress: { Total: > 0 } p } && FindBusy) _find.ShowCount(Loc.Get(K.FindProgress, ("percent", p.Done * 100 / p.Total)));
        };
        _find.BrowseRequested += () => _ = BrowseFolderAsync();
        _findDebounce.Tick += (_, _) =>
        {
            _findDebounce.Stop();
            if (_find.IsVisible && !_find.FilesMode && Active is { Custom: null } tab && !FindCoversText(tab.Doc.Doc)) _ = StartFindSearch(jump: false);
        };
        return _find;
    }

    private void ApplyFindTexts()
    {
        _find.ApplyTexts();
        ApplyResultsTexts();
    }

    private SearchPattern FindPattern() => new(_find.FindBox.Text ?? "", _find.RegexToggle.IsChecked == true, _find.CaseToggle.IsChecked == true, _find.WordToggle.IsChecked == true);

    /// <summary>What the strip asks for; the change event of a field that only repeats the search already started (a seeded text) is ignored.</summary>
    private string FindSignature() => $"{_find.FindBox.Text}|{_find.RegexToggle.IsChecked}{_find.CaseToggle.IsChecked}{_find.WordToggle.IsChecked}{_find.SelectionToggle.IsChecked}";

    // ---- opening and closing ----

    /// <summary>Opens the strip for the active document (Ctrl+F; with <paramref name="replace"/> Ctrl+H). A single-line selection becomes the query when the strip was closed.</summary>
    internal CommandOutcome ShowFind(bool replace)
    {
        if (Active is not { Custom: null } tab) return CommandOutcome.NotHandled;
        if (tab.Hex is { } hex)
        {
            _ = replace ? ReplaceBytesAsync(hex) : FindBytesAsync(hex);
            return CommandOutcome.Handled;
        }
        bool wasOpen = _find.IsVisible;
        _find.Open(files: false, replace);
        string? seed = wasOpen ? null : SelectedSingleLine(tab);
        _findOrigin = SelectionTextStart(tab);
        SetFindText(seed);
        var field = replace && !string.IsNullOrEmpty(_find.FindBox.Text) ? _find.ReplaceBox : _find.FindBox;
        field.Focus();
        field.SelectAll();
        _ = StartFindSearch(jump: true);
        return CommandOutcome.Handled;
    }

    private void SetFindText(string? text)
    {
        if (text is null) return;
        _findSeeding = true;
        try { _find.FindBox.Text = text; }
        finally { _findSeeding = false; }
    }

    private static string? SelectedSingleLine(DocTab tab)
    {
        if (tab.Surface is not { HasSelection: true } s) return null;
        var sel = s.GetSelections()[0];
        if (sel.IsEmpty || sel.Length > 200) return null;
        string text = s.Document.GetText(sel.Start, sel.Length);
        return text.Contains('\n') || text.Contains('\r') ? null : text;
    }

    private static int SelectionTextStart(DocTab tab) => tab.Surface is { } s ? s.GetSelections()[0].Start : 0;

    internal void HideFind()
    {
        if (!_find.IsVisible) return;
        CancelFind();
        ShowFindMarks(null, null);
        DropScope();
        _find.Close();
        if (Active is { } tab) FocusView(tab);
    }

    /// <summary>Called when the active tab changed: follows the document with the search and forgets the selection scope of the old one.</summary>
    private void FindActiveTabChanged()
    {
        var od = Active?.Doc is { IsResults: false } d ? d : null;
        if (ReferenceEquals(_findSubscribed, od)) return;
        if (_findSubscribed is { } old) old.Changed -= OnFindDocChanged;
        _findSubscribed = od;
        if (od is not null) od.Changed += OnFindDocChanged;
        DropScope();
        // the results tab keeps the search of the document it lists, so coming back does not search a large file again
        if (_find.IsVisible && !_find.FilesMode && Active?.Doc is not { IsResults: true } && (od is null || !FindCoversText(od.Doc))) _ = StartFindSearch(jump: false);
    }

    private void CancelFind()
    {
        _findGeneration++;
        _findRun?.Cancel();
        FindBusy = false;
    }

    /// <summary>Cancel on the bar: the count says so at once and the worker stops in the background; the next step or listing searches again.</summary>
    private void StopFind()
    {
        if (!FindBusy) return;
        CancelFind();
        _find.ShowCount(Loc.Get(K.ResultsCancelled));
    }

    /// <summary>The last view of a document closed: a search of it that still runs has no one to report to.</summary>
    private void FindDocumentClosed(Document doc)
    {
        if (!FindBusy || doc.Id != _hitsDocument) return;
        CancelFind();
        _find.ShowCount("");
    }

    private void OnFindDocChanged()
    {
        if (!_find.IsVisible || _find.FilesMode) return;
        _findDebounce.Stop();
        _findDebounce.Start();
    }

    private void OnFindQueryChanged()
    {
        if (_findSeeding || _find.FilesMode || FindSignature() == _findSignature) return;
        if (Active is { Custom: null } tab) _findOrigin = SelectionTextStart(tab);
        _ = StartFindSearch(jump: true);
    }

    // ---- scope: the selection a search or replacement is limited to ----

    private void UpdateScope(DocTab tab)
    {
        if (_find.SelectionToggle.IsChecked != true) { DropScope(); return; }
        if (_scopeStart is { IsValid: true } && _scopeEnd is { IsValid: true }) return;
        var sel = tab.Surface is { } s ? ToBytes(s, s.GetSelections()) : [];
        if (sel.Count == 0 || sel[0].Length == 0)
        {
            _find.SelectionToggle.IsChecked = false;
            SetStatusMessage(Loc.Get(K.FindNoScope));
            return;
        }
        var doc = tab.Doc.Doc;
        _scopeStart = doc.CreateAnchor(sel[0].Start, AnchorGravity.Left);
        _scopeEnd = doc.CreateAnchor(sel[0].End, AnchorGravity.Right);
    }

    private void DropScope()
    {
        _scopeStart = _scopeEnd = null;
        if (_find.SelectionToggle.IsChecked == true) _find.SelectionToggle.IsChecked = false;
    }

    private bool InScope(ByteRange r) => _scopeStart is not { IsValid: true } a || _scopeEnd is not { IsValid: true } b || (r.Start >= a.Offset && r.End <= b.Offset);

    // ---- incremental search ----

    /// <summary>
    /// Starts a search of the active document in its current revision and cancels the one before. With <paramref name="jump"/> the first match at or after the
    /// place the search began is selected as soon as it is known. The task ends when the search ends or is replaced by a newer one.
    /// </summary>
    private Task StartFindSearch(bool jump)
    {
        _findSignature = FindSignature();
        _findRun?.Cancel();
        int generation = ++_findGeneration;
        _hits = [];
        _hitsRevision = -1;
        _hitsTotal = 0;
        _findIndex = -1;
        FindBusy = false;
        ShowFindMarks(null, null);
        var pattern = FindPattern();
        if (string.IsNullOrEmpty(pattern.Text) || Active is not { Custom: null } tab)
        {
            _find.ShowCount("");
            return Task.CompletedTask;
        }
        if (RegexTools.Validate(pattern) is { } error)
        {
            _find.ShowCount(Loc.Get(K.FindInvalid, ("reason", error)), error: true);
            return Task.CompletedTask;
        }
        UpdateScope(tab);
        var doc = tab.Doc.Doc;
        var buffer = LiveBuffer.Of(doc);
        _hitsDocument = doc.Id;
        _findRunRevision = buffer.Snapshot.Revision;
        var run = SearchWorker.Start(pattern, new SearchScope([], [buffer]), new SearchSettings { MaxResults = FindMaxHits, StopAtLimit = false, Parallelism = Math.Min(4, Environment.ProcessorCount) });
        _findRun = run;
        FindBusy = true;
        _find.ShowCount(Loc.Get(K.FindSearching));
        return _findCollect = CollectFindAsync(run, generation, tab, buffer.Snapshot.Revision, jump);
    }

    private async Task CollectFindAsync(SearchRun run, int generation, DocTab tab, long revision, bool jump)
    {
        var hits = new List<SearchHit>();
        bool jumped = !jump;
        await foreach (var batch in run.Batches.ReadAllAsync())
        {
            if (generation != _findGeneration) return;
            hits.AddRange(batch.Hits.Where(h => InScope(h.Range)));
            _hits = hits;
            if (!jumped && StepFrom(tab, hits, forward: true, _findOrigin, wrap: false)) jumped = true;
        }
        var summary = await run.Completion;
        if (generation != _findGeneration) return;
        FindBusy = false;
        _hits = hits;
        _hitsRevision = revision;
        _hitsTotal = _scopeStart is null ? summary.TotalMatches : hits.Count;
        ShowFindMarks(tab, hits);
        if (!jumped) StepFrom(tab, hits, forward: true, _findOrigin, wrap: true);
        ShowFindCount(summary.Errors.Count > 0);
    }

    private void ShowFindCount(bool tooSlow = false)
    {
        if (tooSlow) { _find.ShowCount(Loc.Get(K.FindTooSlow), error: true); return; }
        if (string.IsNullOrEmpty(_find.FindBox.Text)) { _find.ShowCount(""); return; }
        long total = Math.Max(_hitsTotal, _hits.Count);
        if (!FindBusy && total > _hits.Count) { _find.ShowCount(Loc.Get(K.FindLimit, ("total", total), ("listed", _hits.Count))); return; }
        _find.ShowCount(total == 0 ? Loc.Get(K.FindNone) : _findIndex >= 0 ? Loc.Get(K.FindCount, ("current", _findIndex + 1), ("total", total)) : Loc.Get(K.FindTotal, ("total", total)));
    }

    private int _findIndex = -1;

    /// <summary>
    /// Lists the matches of the active document in the Search Results tab. A search of its current text that still runs is awaited, not started again
    /// (in a large file that would begin from the first byte); otherwise the document is searched first when the known matches are not of its current text.
    /// </summary>
    private async Task ListFindResultsAsync()
    {
        if (Active is not { Custom: null } tab || string.IsNullOrEmpty(_find.FindBox.Text)) return;
        var doc = tab.Doc.Doc;
        await (FindCoversText(doc) ? _findCollect : StartFindSearch(jump: false));
        if (FindBusy || Active != tab || _hitsRevision != doc.Revision || _hitsDocument != doc.Id || _hits.Count == 0) return;
        var (hits, total, pattern) = (_hits, _hitsTotal, FindPattern());
        var (_, view) = EnsureResultsTab();
        view.Clear();
        view.Highlight = RegexTools.Create(pattern);
        string title = Loc.Get(K.ResultsTitleSearch, ("query", pattern.Text));
        view.AddHits(hits, path => path);
        if (total > hits.Count) view.SetLimit(Loc.Get(K.ResultsListLimit, ("shown", hits.Count), ("total", total)));
        view.SetHeader(title, "");
        FinishResults(view, Loc.Plural(K.ResultsMatches, total));
    }

    // ---- stepping ----

    /// <summary>Selects the next or previous match from the selection of the active document, searching again first when the listed matches are not of its current text.</summary>
    internal async Task FindStepAsync(bool forward)
    {
        if (Active is not { Custom: null } tab) return;
        if (tab.Hex is { } hex)
        {
            await HexFindStepAsync(hex, forward);
            return;
        }
        if (!_find.IsVisible || string.IsNullOrEmpty(_find.FindBox.Text))
        {
            ShowFind(false);
            return;
        }
        var doc = tab.Doc.Doc;
        // a search put into the bar without a search of its own (a macro step) is not what the listed matches are of
        if (!FindCoversText(doc) || FindSignature() != _findSignature) await StartFindSearch(jump: false);
        else if (FindBusy)
        {
            // waiting for the running search instead of starting over matters in a large file; when it selects its first match meanwhile, that is the step asked for
            bool selected = _findIndex >= 0;
            await _findCollect;
            if (!selected && _findIndex >= 0) return;
        }
        if (FindBusy || Active != tab || _hitsRevision != doc.Revision) return;
        _findOrigin = SelectionTextStart(tab);
        if (!StepFrom(tab, _hits, forward, TextStepOrigin(tab, forward), wrap: true)) ShowFindCount();
    }

    private static int TextStepOrigin(DocTab tab, bool forward) =>
        tab.Surface is { } s ? (forward ? s.GetSelections()[0].End : s.GetSelections()[0].Start) : 0;

    /// <summary>
    /// Selects the first match after (forward) or the last before (backward) the text offset <paramref name="origin"/>; with <paramref name="wrap"/> it continues
    /// from the other end. False when nothing was selected.
    /// </summary>
    private bool StepFrom(DocTab tab, List<SearchHit> hits, bool forward, int origin, bool wrap)
    {
        if (hits.Count == 0) return false;
        if (tab.Surface is null)
        {
            // a paged view has no selection to step from: walk the list
            int next = _findIndex < 0 ? (forward ? 0 : hits.Count - 1) : forward ? _findIndex + 1 : _findIndex - 1;
            bool past = next < 0 || next >= hits.Count;
            if (past && !wrap) return false;
            if (past) next = forward ? 0 : hits.Count - 1;
            SelectHit(tab, hits[next], next);
            if (past) SetStatusMessage(Loc.Get(K.FindWrapped));
            return true;
        }
        var surface = tab.Surface!;
        var bytes = ToBytes(surface, [Sel.At(Math.Clamp(origin, 0, surface.Document.TextLength))]);
        long limit = bytes.Count > 0 ? bytes[0].Start : 0;
        int index = forward ? hits.FindIndex(h => h.Range.Start >= limit) : hits.FindLastIndex(h => h.Range.End <= limit);
        bool wrapped = false;
        if (index < 0)
        {
            if (!wrap) return false;
            index = forward ? 0 : hits.Count - 1;
            wrapped = true;
        }
        SelectHit(tab, hits[index], index);
        if (wrapped) SetStatusMessage(Loc.Get(K.FindWrapped));
        return true;
    }

    private void SelectHit(DocTab tab, SearchHit hit, int index)
    {
        _findIndex = index;
        if (tab.Surface is { } s)
        {
            var doc = tab.Doc.Doc;
            var text = s.Document;
            int a = ToTextOffset(doc, text, hit.Range.Start), b = ToTextOffset(doc, text, hit.Range.End);
            if (a >= 0 && b >= a) s.SetSelections([new Sel(a, b)]);
        }
        else if (tab.Paged is { } paged) ShowPagedHit(tab, paged, hit);
        else tab.GoToLine(hit.Line);
        if (_hitsRevision == tab.Doc.Doc.Revision || !FindBusy) ShowFindCount();
        else _find.ShowCount(Loc.Get(K.FindCount, ("current", index + 1), ("total", Math.Max(_hitsTotal, _hits.Count))));
    }

    /// <summary>
    /// Selects a match in a paged view by its bytes mapped to the current revision, so that a match beyond the indexed part is reached too;
    /// by its line number when the range no longer maps.
    /// </summary>
    private static void ShowPagedHit(DocTab tab, PagedTextView paged, SearchHit hit)
    {
        if (hit.Source.DocumentId == tab.Doc.Doc.Id && hit.Source.Revision is { } revision && tab.Doc.Doc.MapRange(hit.Range, revision) is { Ok: true, Range: var range })
        {
            paged.GoToOffset(range.Start);
            paged.SelectBytes(range.Start, range.End);
        }
        else tab.GoToLine(hit.Line);
    }

    /// <summary>Ctrl+F3: finds the next occurrence of the word under the caret or of the selection.</summary>
    internal CommandOutcome FindWord()
    {
        if (Active is not { Custom: null } tab) return CommandOutcome.NotHandled;
        string? word = SelectedSingleLine(tab) ?? WordAtCaret(tab);
        if (word is null) return CommandOutcome.Handled;
        _find.Open(files: false, _find.ReplaceShown);
        SetFindText(_find.RegexToggle.IsChecked == true ? Regex.Escape(word) : word);
        _ = FindStepAsync(forward: true);
        return CommandOutcome.Handled;
    }

    /// <summary>The offsets of the word under the caret in a text tab, or null when the caret is not in a word.</summary>
    private static (int Start, int End)? WordRangeAtCaret(DocTab tab)
    {
        if (tab.Surface is not { } s) return null;
        var d = s.Document;
        int caret = s.CaretOffset;
        static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
        int start = caret, end = caret;
        while (start > 0 && IsWord(d.GetCharAt(start - 1))) start--;
        while (end < d.TextLength && IsWord(d.GetCharAt(end))) end++;
        return end == start ? null : (start, end);
    }

    /// <summary>The word under the caret; the word is selected so that the next search starts behind it.</summary>
    private static string? WordAtCaret(DocTab tab)
    {
        if (WordRangeAtCaret(tab) is not { } word || tab.Surface is not { } s) return null;
        s.SetSelections([new Sel(word.Start, word.End)]);
        return s.Document.GetText(word.Start, word.End - word.Start);
    }

    // ---- replacing ----

    /// <summary>
    /// Replaces the selected match (or the next one) or every match, in the whole document or in the selection scope. The change is planned for the revision
    /// seen now and applied only when the document is still at it; when it moved on meanwhile the plan is made again.
    /// </summary>
    private async Task ReplaceAsync(bool all)
    {
        if (Active is not { Surface: { } surface } tab || tab.Doc.ReadOnly) return;
        var pattern = FindPattern();
        if (string.IsNullOrEmpty(pattern.Text) || RegexTools.Validate(pattern) is not null) return;
        UpdateScope(tab);
        string replacement = _find.ReplaceBox.Text ?? "";
        var doc = tab.Doc.Doc;
        var applier = new ChangeApplier(id => id == doc.Id ? doc : null);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            ReplacePlan plan;
            try { plan = await ReplacePlanner.PlanAsync(pattern, replacement, new SearchScope([], [LiveBuffer.Of(doc)])); }
            catch (Exception e) when (e is IOException or NotSupportedException or ArgumentException) { SetStatusMessage(e.Message); return; }
            if (Active != tab) return;
            if (plan.Proposal.Files.FirstOrDefault() is not { } file || file.Hunks.Count == 0)
            {
                SetStatusMessage(Loc.Get(K.FindNone));
                return;
            }

            var selection = new ChangeSelection();
            Hunk? only = null;
            if (all)
            {
                foreach (var h in file.Hunks.Where(h => !InScope(h.Range))) selection.RejectHunk(file, h.Id);
            }
            else
            {
                only = PickHunk(surface, file);
                if (only is null) return;
                foreach (var h in file.Hunks.Where(h => h.Id != only.Id)) selection.RejectHunk(file, h.Id);
            }
            int caret = only is null ? -1 : ToTextOffset(doc, surface.Document, only.Range.Start);
            ApplyReport report;
            using (doc.BeginUndoGroup()) report = applier.Apply(plan.Proposal, selection);
            var outcome = report.Files[0];
            if (outcome.NeedsRepreview) continue;
            if (outcome.Status != ChangeStatus.Applied)
            {
                SetStatusMessage(Loc.Get(K.FindNothingReplaced));
                return;
            }
            if (only is not null && caret >= 0) surface.SetSelections([Sel.At(caret + only.NewText.Length)]);
            SetStatusMessage(Loc.Plural(K.FindReplaced, outcome.HunksApplied));
            _findOrigin = SelectionTextStart(tab);
            if (only is not null) await FindStepAsync(forward: true);
            else await StartFindSearch(jump: false);
            return;
        }
        SetStatusMessage(Loc.Get(K.FindNothingReplaced));
    }

    /// <summary>The change for the selected match, else the first one after the selection (or the caret) inside the scope, else the first one.</summary>
    private Hunk? PickHunk(EditorSurface surface, FileChange file)
    {
        var sel = ToBytes(surface, surface.GetSelections());
        long start = sel.Count > 0 ? sel[0].Start : 0, end = sel.Count > 0 ? sel[0].End : 0;
        var candidates = file.Hunks.Where(h => h.CanApply && InScope(h.Range)).ToList();
        return candidates.FirstOrDefault(h => h.Range.Start == start && h.Range.End == end)
            ?? candidates.FirstOrDefault(h => h.Range.Start >= end)
            ?? candidates.FirstOrDefault();
    }

    // ---- go to ----

    /// <summary>Jumps to a line (and column), a percentage of the file or a byte offset; the reason is said when the text is not a target or the offset is not indexed yet.</summary>
    internal void GoTo(string text)
    {
        if (Active is not { Custom: null } tab || !GoToTarget.TryParse(text, out var target))
        {
            SetStatusMessage(Loc.Get(K.GoToLineInvalid, ("text", text)));
            return;
        }
        RecordJump();
        var doc = tab.Doc.Doc;
        switch (target.Kind)
        {
            case GoToKind.Line when tab.Surface is { } s:
                {
                    int line = (int)Math.Min(target.Value, s.Document.LineCount);
                    s.GoToLine(line);
                    var l = s.Document.GetLineByNumber(line);
                    s.SetSelections([Sel.At(l.Offset + (int)Math.Min(target.Column - 1, l.Length))]);
                    break;
                }
            case GoToKind.Line:
                if (!tab.GoToLine((int)Math.Min(target.Value, int.MaxValue), target.Column)) SetStatusMessage(Loc.Get(K.GoToNotIndexed));
                break;
            default:
                GoToByte(tab, target.Kind == GoToKind.Percent ? (long)(doc.ByteLength * (decimal)target.Value / 100m) : target.Value);
                break;
        }
    }

    private void GoToByte(DocTab tab, long offset)
    {
        var doc = tab.Doc.Doc;
        offset = Math.Clamp(offset, 0, doc.ByteLength);
        while (offset > 0 && !doc.IsCharBoundary(offset)) offset--;
        if (tab.Paged is { } paged)
        {
            paged.GoToOffset(offset);
            return;
        }
        if (!doc.TryGetPosition(offset, out var position))
        {
            SetStatusMessage(Loc.Get(K.GoToNotIndexed));
            return;
        }
        if (tab.Surface is { } s)
        {
            int at = ToTextOffset(doc, s.Document, offset);
            if (at >= 0) s.SetSelections([Sel.At(at)]);
        }
        else tab.GoToLine((int)Math.Min(position.Line + 1, int.MaxValue));
    }

    // ---- matching bracket ----

    /// <summary>Moves the caret to the partner of the bracket at or before it; brackets in strings and comments are not counted when the language has a grammar.</summary>
    internal CommandOutcome GoToBracket()
    {
        if (ActiveEditor is not { } surface || Active is not { } tab) return CommandOutcome.NotHandled;
        var text = surface.Document;
        var caret = text.GetLocation(surface.CaretOffset);
        var lines = LinesOf(text);
        var tokenizer = TokenizerOf(tab);
        var pair = LanguageModes.GrammarId(tab.Doc) == "latte" ? TagPairMatcher.FindPair(lines, tokenizer, caret.Line - 1, caret.Column - 1) : null;
        pair ??= BracketMatcher.FindPair(lines, tokenizer, caret.Line - 1, caret.Column - 1);
        if (pair is not { } at)
        {
            SetStatusMessage(Loc.Get(K.BracketNone));
            return CommandOutcome.Handled;
        }
        surface.SetSelections([Sel.At(text.GetOffset(at.Line + 1, at.Column + 1))]);
        return CommandOutcome.Handled;
    }

    private static List<string> LinesOf(TextDocument text)
    {
        var lines = new List<string>(text.LineCount);
        foreach (var l in text.Lines) lines.Add(text.GetText(l.Offset, l.Length));
        return lines;
    }

    /// <summary>Tokenizer of the tab's grammar: the chosen language first, then the file name; null for plain text.</summary>
    private static Tokenizer? TokenizerOf(DocTab tab)
    {
        var registry = LanguageModes.Grammars;
        var grammar = (LanguageModes.GrammarId(tab.Doc) is { } id ? registry.Find(id) : null)
            ?? (tab.Path is { } p ? registry.ForFile(System.IO.Path.GetFileName(p)) : null);
        return grammar is null ? null : new Tokenizer(grammar);
    }

    // ---- language edits ----

    /// <summary>Comments the lines of every selection out, or back in, with the markers of the language at each line; one undo step.</summary>
    internal CommandOutcome ToggleComment()
    {
        if (Active is not { Surface: { IsReadOnly: false } } tab) return CommandOutcome.NotHandled;
        var tokenizer = TokenizerOf(tab);
        return RunEdit((s, sels) =>
        {
            var d = s.Document;
            var ranges = sels.Select(sel =>
            {
                var first = d.GetLineByOffset(sel.Start);
                var last = d.GetLineByOffset(sel.End);
                if (!sel.IsEmpty && sel.End == last.Offset && last != first) last = last.PreviousLine;
                return (first.LineNumber - 1, last.LineNumber - 1);
            });
            var edits = CommentToggle.Toggle(LinesOf(d), tokenizer, ranges);
            if (edits.Count == 0) return sels;
            TextAnchor Anchor(int offset, AnchorMovementType movement)
            {
                var anchor = d.CreateAnchor(offset);
                anchor.MovementType = movement;
                anchor.SurviveDeletion = true;
                return anchor;
            }
            // a selection keeps the markers inside it, a caret moves past an inserted marker
            var anchors = sels.Select(sel => (Start: Anchor(sel.Start, sel.IsEmpty ? AnchorMovementType.AfterInsertion : AnchorMovementType.BeforeInsertion),
                End: Anchor(sel.End, AnchorMovementType.AfterInsertion))).ToList();
            using (d.RunUpdate())
                foreach (var e in edits.Reverse())
                    d.Replace(d.GetLineByNumber(e.Line + 1).Offset + e.Column, e.Length, e.Text);
            return anchors.Select(a => new Sel(a.Start.Offset, a.End.Offset)).ToList();
        });
    }

    /// <summary>Grows every selection one step: word, bracket content, the brackets, the next pair out, whole lines, all text.</summary>
    internal CommandOutcome ExpandSelection()
    {
        if (Active is not { Surface: { } surface } tab) return CommandOutcome.NotHandled;
        var d = surface.Document;
        var lines = LinesOf(d);
        var tokenizer = TokenizerOf(tab);
        TextPosition At(int offset) => d.GetLocation(offset) is var l ? new TextPosition(l.Line - 1, l.Column - 1) : default;
        int OffsetOf(TextPosition p) => d.GetOffset(p.Line + 1, p.Column + 1);
        var grown = surface.GetSelections().Select(sel =>
        {
            var (a, b) = SelectionGrowth.Expand(lines, tokenizer, At(sel.Start), At(sel.End));
            return new Sel(OffsetOf(a), OffsetOf(b));
        }).ToList();
        surface.SetSelections(grown);
        return CommandOutcome.Handled;
    }
}
