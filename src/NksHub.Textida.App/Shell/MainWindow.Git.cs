using Avalonia.Controls;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// What the window does with the built-in Git plugin besides running its commands: the change marks in the gutter, the minimap and beside the scroll bar of a text tab (asked for in the background once
/// the edits stop), the peek at a change that a click on a mark opens (the old lines, buttons to revert, stage and step through the changes), the commands that step through the changes, and the
/// buttons of a comparison tab that stage the difference the tab is on. The author of the line at the caret is asked for once the caret rests and shown in the status bar.
/// </summary>
internal sealed partial class MainWindow
{
    private DispatcherTimer? _gitGutterTimer;
    private readonly HashSet<DocTab> _gitGutterDirty = [];
    private bool _gitGutterRunning, _gitGutterAgain;
    private StatusModel? _gitStatusSeen;

    /// <summary>What the marks of the gutter are shown against: the index (the default) or the last commit.</summary>
    internal GitBase GutterBase => _app.Workbench.Settings.GetString("git.gutterBase") == "head" ? GitBase.Head : GitBase.Index;

    /// <summary>Marks a tab for a new look at its Git changes; the timer asks when no edit, save or change of the repository came for the debounce time.</summary>
    private void ScheduleGitGutter(DocTab? tab)
    {
        if (tab?.Surface is null) return;
        _gitGutterDirty.Add(tab);
        if (_gitGutterTimer is null)
        {
            _gitGutterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PluginLimitsPlaceholders.GitGutterDebounceMilliseconds) };
            _gitGutterTimer.Tick += async (_, _) =>
            {
                _gitGutterTimer.Stop();
                if (_gitGutterRunning)
                {
                    _gitGutterAgain = true;
                    return;
                }
                _gitGutterRunning = true;
                try { await RefreshGitGutterAsync(); }
                catch (Exception e) when (e is IOException or InvalidOperationException or KeyNotFoundException or System.Text.Json.JsonException or FormatException or OperationCanceledException or NksHub.Textida.Rpc.RpcException)
                {
                    // a marker column that cannot be drawn must not take the editor down; the next edit asks again
                }
                finally
                {
                    _gitGutterRunning = false;
                    if (_gitGutterAgain)
                    {
                        _gitGutterAgain = false;
                        _gitGutterTimer.Start();
                    }
                }
            };
        }
        _gitGutterTimer.Stop();
        _gitGutterTimer.Start();
    }

    /// <summary>Asks Git for the marks of the tabs that are shown; a tab that is not shown waits until it is.</summary>
    private async Task RefreshGitGutterAsync()
    {
        bool enabled = _app.Workbench.Settings.GetBool("git.gutter");
        var gitBase = GutterBase;
        foreach (var tab in _gitGutterDirty.ToList())
        {
            if (tab.Group is not { } group || group.Active != tab) continue;
            _gitGutterDirty.Remove(tab);
            if (tab.Surface is not { } surface) continue;
            if (!enabled)
            {
                SetGitMarks(tab, null);
                continue;
            }
            var marks = await _app.Plugins.GitMarksAsync(this, tab, gitBase);
            if (marks is null)
            {
                if (_app.Plugins.IsBusy) ScheduleGitGutter(tab); // a command of the user was running: ask again when it is over
                continue;
            }
            if (tab.Doc.Doc.Revision == marks.Revision) SetGitMarks(tab, marks.Marks);
        }
    }

    /// <summary>The marks go to the gutter, to the minimap and to the strip beside the scroll bar of the tab at once.</summary>
    private static void SetGitMarks(DocTab tab, IReadOnlyList<GitMark>? marks)
    {
        tab.Surface?.SetGitMarks(marks);
        tab.Minimap?.SetChangeMarks(marks ?? []);
    }

    /// <summary>A mark of the gutter was clicked: the change opens under the mark, with its old lines.</summary>
    private Task ClickGitMarkAsync(DocTab tab, GitMark mark) => OpenGitPeekAsync(tab, mark, focus: false);

    /// <summary>
    /// Opens the peek at a change: asks Git for the lines the change replaced and shows them under the change. Nothing is shown when Git could not answer for the text as it is now; the status line says so.
    /// With <paramref name="focus"/> the keyboard focus goes to the peek.
    /// </summary>
    private async Task OpenGitPeekAsync(DocTab tab, GitMark mark, bool focus)
    {
        if (tab.Surface is not { } surface) return;
        var peek = await _app.Plugins.GitPeekAsync(this, tab, mark, GutterBase);
        if (peek is null || tab.Surface is null || tab.Doc.Doc.Revision != peek.Revision)
        {
            SetStatusMessage(Loc.Get(K.GitPeekStale));
            return;
        }
        var marks = surface.GitMarks;
        int index = Math.Max(0, marks.ToList().FindIndex(m => m.Line == peek.Mark.Line && m.Staged == peek.Mark.Staged));
        surface.ShowGitPeek(peek, index, marks.Count, new GitPeekActions(
            Revert: () => RevertGitChange(tab, peek),
            Stage: () => _ = StageGitMarkAsync(tab, peek.Mark),
            Previous: () => _ = StepGitChangeAsync(tab, peek.Mark, forward: false, focus: true),
            Next: () => _ = StepGitChangeAsync(tab, peek.Mark, forward: true, focus: true),
            Close: () => FocusView(tab)), focus);
        Announce(Loc.Get(K.A11yGitPeek, ("description", GitMargin.DescribeOf(peek.Mark))));
    }

    /// <summary>The change is put back to the lines it replaced, as one undo step; the peek goes away. The text now is the base text, so the marks of the change go with the next look at the file.</summary>
    private void RevertGitChange(DocTab tab, GitPeek peek)
    {
        if (tab.Surface is not { } surface) return;
        string message = surface.IsReadOnly ? Loc.Get(K.GitRevertReadOnly)
            : peek.Truncated ? Loc.Get(K.GitRevertTooLong)
            : surface.RevertGitChange(peek) ? Loc.Get(K.GitRevertDone) : Loc.Get(K.GitPeekStale);
        surface.CloseGitPeek();
        FocusView(tab);
        SetStatusMessage(message);
        ScheduleGitGutter(tab);
    }

    /// <summary>The change is put in the index, or, when it is in it already, taken back from it. The plugin says in a notice how it went.</summary>
    private async Task StageGitMarkAsync(DocTab tab, GitMark mark)
    {
        if (tab.Surface is not { } surface || tab.Doc.TextBackend is not { } backend) return;
        surface.CloseGitPeek();
        var text = surface.Document;
        long offset = mark.Line < text.LineCount ? backend.ToByteOffset(text.GetLineByNumber(mark.Line + 1).Offset) : tab.Doc.Doc.ByteLength;
        await _app.Plugins.RunGitAsync(this, tab, mark.Staged ? "git.unstageHunk" : "git.stageHunk", offset);
        // the plugin opens the documents of the index for a moment, which moves the tabs
        if (Active != tab) SelectTab(tab);
        FocusView(tab);
        ScheduleGitGutter(tab);
    }

    /// <summary>The change after (or before) <paramref name="from"/>, from the first change on when there is none after the last (and the other way round): the caret goes to its first line and its peek opens.</summary>
    private async Task StepGitChangeAsync(DocTab tab, GitMark from, bool forward, bool focus)
    {
        if (tab.Surface is not { } surface) return;
        if (GitMargin.StepFrom(surface.GitMarks, from, forward) is not { } target) return;
        GoToGitMark(surface, target);
        await OpenGitPeekAsync(tab, target, focus);
    }

    private static void GoToGitMark(EditorSurface surface, GitMark mark)
    {
        int line = Math.Clamp(mark.Line + 1, 1, surface.Document.LineCount);
        surface.CaretOffset = surface.Document.GetLineByNumber(line).Offset;
        surface.TextArea.Caret.BringCaretToView();
    }

    /// <summary>"Git: Next Change" and "Git: Previous Change": the caret goes to the next (previous) change of the file and its peek opens; after the last it goes on at the first.</summary>
    internal CommandOutcome GoToGitChange(bool forward)
    {
        if (Active is not { Surface: { } surface } tab) return CommandOutcome.NotHandled;
        var marks = surface.GitMarks;
        if (marks.Count == 0)
        {
            SetStatusMessage(Loc.Get(K.GitNoChanges));
            return CommandOutcome.Handled;
        }
        int caret = (int)surface.CaretLineColumn().Line - 1;
        if (GitMargin.StepFrom(marks, caret, forward) is { } target)
        {
            GoToGitMark(surface, target);
            _ = OpenGitPeekAsync(tab, target, focus: false);
        }
        return CommandOutcome.Handled;
    }

    /// <summary>"Git: Show Change at Caret": the peek of the change the caret is in, with the keyboard focus in it.</summary>
    internal CommandOutcome ShowGitChangeAtCaret()
    {
        if (Active is not { Surface: { } surface } tab) return CommandOutcome.NotHandled;
        if (GitMargin.MarkAt(surface.GitMarks, (int)surface.CaretLineColumn().Line - 1) is not { } mark)
        {
            SetStatusMessage(Loc.Get(surface.GitMarks.Count == 0 ? K.GitNoChanges : K.GitNoChangeAtCaret));
            return CommandOutcome.Handled;
        }
        _ = OpenGitPeekAsync(tab, mark, focus: true);
        return CommandOutcome.Handled;
    }

    /// <summary>"Git: Revert Change at Caret": the change the caret is in is put back to the lines it replaced, without the peek.</summary>
    internal CommandOutcome RevertGitChangeAtCaret()
    {
        if (Active is not { Surface: { } surface } tab) return CommandOutcome.NotHandled;
        if (GitMargin.MarkAt(surface.GitMarks, (int)surface.CaretLineColumn().Line - 1) is not { } mark)
        {
            SetStatusMessage(Loc.Get(surface.GitMarks.Count == 0 ? K.GitNoChanges : K.GitNoChangeAtCaret));
            return CommandOutcome.Handled;
        }
        _ = RevertGitMarkAsync(tab, mark);
        return CommandOutcome.Handled;
    }

    private async Task RevertGitMarkAsync(DocTab tab, GitMark mark)
    {
        var peek = await _app.Plugins.GitPeekAsync(this, tab, mark, GutterBase);
        if (peek is null || tab.Doc.Doc.Revision != peek.Revision) SetStatusMessage(Loc.Get(K.GitPeekStale));
        else RevertGitChange(tab, peek);
    }

    // ---- who changed the line at the caret ----

    private DispatcherTimer? _blameTimer;
    private GitBlame? _blame;
    private DocTab? _blameTab;
    private (DocTab Tab, int Line, long Revision) _blameAsked;

    /// <summary>The author, date and subject of the commit that last changed the line at the caret, for the status bar; null while that is not known (it is asked for once the caret rests) or the setting is off.</summary>
    private string? GitBlameStatusText(DocTab tab, int line)
    {
        if (tab.Surface is null || tab.Doc.Doc.FilePath is null || !_app.Workbench.Settings.GetBool("git.blame"))
            return null;
        long revision = tab.Doc.Doc.Revision;
        if (_blame is { } blame && ReferenceEquals(_blameTab, tab) && blame.Line == line && blame.Revision == revision)
        {
            ToolTip.SetTip(_statusLeft, BlameTip(blame));
            return BlameText(blame);
        }
        ToolTip.SetTip(_statusLeft, null);
        if (_blameAsked != (tab, line, revision)) ScheduleGitBlame(tab, line, revision);
        return null;
    }

    private void ResetGitBlame()
    {
        _blame = null;
        _blameAsked = default;
    }

    private void ScheduleGitBlame(DocTab tab, int line, long revision)
    {
        _blameAsked = (tab, line, revision);
        _blameTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PluginLimitsPlaceholders.GitBlameDebounceMilliseconds) };
        _blameTimer.Stop();
        _blameTimer.Tick -= OnBlameTimer;
        _blameTimer.Tick += OnBlameTimer;
        _blameTimer.Start();
    }

    private async void OnBlameTimer(object? sender, EventArgs e)
    {
        _blameTimer?.Stop();
        var (tab, line, revision) = _blameAsked;
        if (tab != Active || tab.Surface is null) return;
        try
        {
            var blame = await _app.Plugins.GitBlameAsync(this, tab, line);
            if (blame is null || _blameAsked != (tab, line, revision) || tab.Doc.Doc.Revision != blame.Revision) return;
            (_blame, _blameTab) = (blame, tab);
            UpdateStatus();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or KeyNotFoundException or System.Text.Json.JsonException or FormatException or OperationCanceledException or NksHub.Textida.Rpc.RpcException)
        {
            // a line of blame in the status bar is not worth a message; the next rest of the caret asks again
        }
    }

    internal static string BlameText(GitBlame blame)
    {
        if (blame.Uncommitted) return Loc.Get(K.GitBlameUncommitted);
        string subject = blame.Summary.Length > PluginLimitsPlaceholders.GitBlameSubjectChars ? blame.Summary[..PluginLimitsPlaceholders.GitBlameSubjectChars] + "…" : blame.Summary;
        return Loc.Get(K.GitBlameStatus, ("author", blame.Author), ("date", blame.Time.LocalDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)), ("subject", subject));
    }

    internal static string BlameTip(GitBlame blame) => blame.Uncommitted
        ? Loc.Get(K.GitBlameUncommitted)
        : Loc.Get(K.GitBlameTip, ("author", blame.Author), ("date", blame.Time.LocalDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)),
            ("commit", blame.Sha.Length > 8 ? blame.Sha[..8] : blame.Sha), ("subject", blame.Summary));

    /// <summary>The Git status item of the plugin changed (a command ran or the plugin saw the repository change): the marks of the open files may have changed too.</summary>
    private void OnGitStatusChanged(PluginUiHost ui)
    {
        var status = ui.Status.TryGetValue(PluginServices.GitPluginId, out var current) ? current : null;
        if (Equals(status, _gitStatusSeen)) return;
        _gitStatusSeen = status;
        ResetGitBlame(); // a commit or a checkout changes who wrote a line
        foreach (var tab in Tabs) ScheduleGitGutter(tab);
    }

    private void ReleaseGitGutter()
    {
        _gitGutterTimer?.Stop();
        _blameTimer?.Stop();
        _gitGutterDirty.Clear();
    }

    private readonly HashSet<DiffView> _diffActing = [];

    /// <summary>
    /// A side of a comparison is not as it was when the comparison was made: an open document is at another revision, or a file differs on disk. Content the plugin supplied cannot change, and a
    /// pinned side (an old revision) stays as it is; the right side, the one the command acts on, is pinned to nothing and must be the revision that was compared.
    /// </summary>
    private bool DiffSideChanged(DiffInput input, bool acted) =>
        input.File is { } file ? DiffActionPayload.FileChanged(file)
        : !input.Detached && (acted || !input.Pinned) && _app.Plugins.OpenTabOf(input.Snapshot.DocumentId) is { } open && open.Tab.Doc.Doc.Revision != input.Snapshot.Revision;

    /// <summary>
    /// Runs a button of a comparison tab that the plugin added on the difference the tab is on. Nothing is sent once a side changed since the comparison was made. The command goes to the plugin
    /// for the document of the right side, at the byte where the difference starts there, or, when that side is content the plugin supplied, without a document; the <c>diff</c> member says which
    /// difference it is (<see cref="DiffActionPayload"/>). When that went through, the plugin's refresh command follows, which opens the comparison again in the place of this tab.
    /// </summary>
    private async Task RunDiffActionAsync(DiffView view, DiffRequest request, DiffAction action, int hunk)
    {
        var target = request.Right.Detached ? null : _app.Plugins.OpenTabOf(request.Right.Snapshot.DocumentId);
        if (request.PluginId is not { } plugin || (target is null && !request.Right.Detached))
        {
            SetStatusMessage(Loc.Get(K.CompareActionGone));
            return;
        }
        if (view.Session.Diff is not { } diff || hunk < 0 || hunk >= diff.Hunks.Count || DiffSideChanged(request.Left, acted: false) || DiffSideChanged(request.Right, acted: true))
        {
            SetStatusMessage(Loc.Get(K.CompareActionStale));
            return;
        }
        if (!_diffActing.Add(view)) return;
        try
        {
            var own = Tabs.FirstOrDefault(t => ReferenceEquals(t.Custom, view));
            var detail = DiffActionPayload.Json(request, diff, hunk);
            var refreshDetail = DiffActionPayload.RefreshJson(request);
            bool done;
            if (target is { } at) done = await _app.Plugins.RunDiffActionAsync(at.Window, at.Tab, plugin, action.Command, diff.B.OffsetOf(diff.Hunks[hunk].BStart), detail);
            else done = await _app.Plugins.RunDiffActionAsync(this, plugin, action.Command, detail);
            if (done && action.Refresh is { } refresh)
            {
                if (target is { } to) await _app.Plugins.RunDiffActionAsync(to.Window, to.Tab, plugin, refresh, null, refreshDetail);
                else await _app.Plugins.RunDiffActionAsync(this, plugin, refresh, refreshDetail);
            }
            else if (own is not null && Active != own) SelectTab(own);
        }
        finally { _diffActing.Remove(view); }
    }
}
