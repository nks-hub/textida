using System.Diagnostics;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Live views of a growing log (P9.4.1, P9.4.3, P9.4.4): a <see cref="LogFollower"/> per document examines the bytes added to the file, keeps the lines of the
/// view's filter, and reports truncation and rotation. With following on, the document is read again when the file changes and the caret goes to the end.
/// </summary>
internal sealed partial class MainWindow
{
    private sealed class LogSession(OpenDocument doc, LogFollower follower, string? levelRules)
    {
        public OpenDocument Doc { get; } = doc;
        public LogFollower Follower { get; } = follower;
        public string? LevelRules { get; } = levelRules;
        public string? ViewName { get; init; }
        public string? Highlight { get; init; }
        public bool Tail { get; set; }
        public bool Busy { get; set; }
        public long LastConsumed { get; set; } = -1;
        public CancellationTokenSource Cts { get; } = new();
    }

    private readonly Dictionary<OpenDocument, LogSession> _logSessions = [];
    private DispatcherTimer? _logTimer;

    private void ReleaseLogFollow()
    {
        _logTimer?.Stop();
        foreach (var session in _logSessions.Values) session.Cts.Cancel();
        _logSessions.Clear();
    }

    private LogSession StartLogSession(OpenDocument od, LogFilter filter, string? levelRules, string? viewName = null, string? highlight = null)
    {
        DropLogSession(od);
        var session = new LogSession(od, new LogFollower(new FileLogSource(od.Path!), od.Doc.Codec, od.LogLevels, filter), levelRules) { ViewName = viewName, Highlight = highlight };
        _logSessions[od] = session;
        _logTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(PluginLimitsPlaceholders.LogFollowPollMilliseconds), DispatcherPriority.Background, (_, _) => LogFollowTick());
        _logTimer.Start();
        LogFollowTick();
        return session;
    }

    private void DropLogSession(OpenDocument od)
    {
        if (!_logSessions.Remove(od, out var old)) return;
        old.Cts.Cancel();
        if (_logSessions.Count == 0) _logTimer?.Stop();
    }

    private void LogFollowTick()
    {
        foreach (var session in _logSessions.Values.ToList())
        {
            if (!_docs.Contains(session.Doc)) DropLogSession(session.Doc);
            else if (!session.Busy)
            {
                session.Busy = true;
                _ = PollLogAsync(session);
            }
        }
    }

    private async Task PollLogAsync(LogSession session)
    {
        var shown = Stopwatch.StartNew();
        string path = session.Doc.Path ?? "";
        void Progress(long reached)
        {
            if (shown.ElapsedMilliseconds < 300) return;
            shown.Restart();
            long total = Math.Max(1, new FileInfo(path).Length);
            Dispatcher.UIThread.Post(() => { if (_logSessions.GetValueOrDefault(session.Doc) == session) SetStatusMessage(Loc.Get(K.LogFollowReading, ("percent", Math.Clamp(reached * 100 / total, 0, 100)))); });
        }
        LogFollowUpdate update;
        try { update = await Task.Run(() => session.Follower.Poll(session.Cts.Token, Progress)); }
        catch (OperationCanceledException) { return; }
        catch (IOException e)
        {
            SetStatusMessage(Loc.Get(K.LogFollowError, ("reason", e.Message)));
            return;
        }
        finally { session.Busy = false; }
        if (_logSessions.GetValueOrDefault(session.Doc) != session || !_docs.Contains(session.Doc)) return;

        string name = session.Doc.Title;
        bool first = session.LastConsumed < 0;
        bool grew = update.Consumed != session.LastConsumed;
        session.LastConsumed = update.Consumed;
        switch (update.Change)
        {
            case LogFollowChange.Missing:
                SetStatusMessage(Loc.Get(K.LogFollowMissing, ("name", name)));
                return;
            case LogFollowChange.Truncated or LogFollowChange.Rotated:
                SetStatusMessage(Loc.Get(update.Change == LogFollowChange.Truncated ? K.LogFollowTruncated : K.LogFollowRotated, ("name", name), ("matches", update.Invalidated)));
                if (!session.Doc.Doc.IsModified) ReloadLogDocument(session.Doc, toEnd: session.Tail);
                else SetStatusMessage(Loc.Get(K.LogFollowModified));
                return;
        }
        if (first && !session.Follower.Filter.IsEmpty)
            SetStatusMessage(Loc.Get(K.LogViewApplied, ("name", session.ViewName ?? name), ("matches", session.Follower.MatchCount)));
        if (!grew || !session.Tail) return;
        if (session.Doc.Doc.IsModified)
        {
            session.Tail = false;
            SetStatusMessage(Loc.Get(K.LogFollowModified));
        }
        else ReloadLogDocument(session.Doc, toEnd: true);
    }

    /// <summary>Reads the file of the document again (the paged backend maps it again) without asking; the caller has checked that nothing is unsaved.</summary>
    private bool ReloadLogDocument(OpenDocument od, bool toEnd)
    {
        if (od.Path is not { } path) return false;
        try
        {
            var (fresh, guess) = OpenDocument.OpenDocumentOnly(path, LargeFileBytes);
            _app.Journal.Untrack(od);
            od.Replace(fresh, guess);
            _app.Journal.Track(od);
        }
        catch (IOException e)
        {
            SetStatusMessage(Loc.Get(K.LogFollowError, ("reason", e.Message)));
            return false;
        }
        if (toEnd && od.Views.FirstOrDefault() is { } tab) ApplyOffsets(tab, od.Doc.ByteLength, od.Doc.ByteLength);
        return true;
    }

    /// <summary>Turns following on or off for the document: while on, the document is read again as the file grows and the caret stays at its end.</summary>
    internal CommandOutcome ToggleLogFollow()
    {
        if (Active is not { Custom: null, Hex: null } tab) return CommandOutcome.NotHandled;
        var od = tab.Doc;
        if (od.Path is null)
        {
            SetStatusMessage(Loc.Get(K.LogFollowNoFile));
            return CommandOutcome.Handled;
        }
        if (_logSessions.TryGetValue(od, out var session) && session.Tail)
        {
            session.Tail = false;
            if (session.Follower.Filter.IsEmpty) DropLogSession(od);
            SetStatusMessage(Loc.Get(K.LogFollowStopped, ("name", od.Title)));
            return CommandOutcome.Handled;
        }
        if (od.Doc.IsModified)
        {
            SetStatusMessage(Loc.Get(K.LogFollowModified));
            return CommandOutcome.Handled;
        }
        session ??= StartLogSession(od, LogFilter.None, null);
        session.Tail = true;
        SetStatusMessage(Loc.Get(K.LogFollowStarted, ("name", od.Title)));
        return CommandOutcome.Handled;
    }

    // ---- saved views ----

    /// <summary>Asks for the name, the filter and the levels of a view, saves it in the profile and makes it the live view of the document.</summary>
    internal CommandOutcome SaveLogView()
    {
        if (Active is not { Custom: null, Hex: null } tab) return CommandOutcome.NotHandled;
        if (tab.Doc.Path is null) SetStatusMessage(Loc.Get(K.LogFollowNoFile));
        else _ = SaveLogViewAsync(tab);
        return CommandOutcome.Handled;
    }

    private async Task SaveLogViewAsync(DocTab tab)
    {
        var od = tab.Doc;
        _logSessions.TryGetValue(od, out var session);
        var current = session?.Follower.Filter ?? LogFilter.None;
        string? name = (await _app.Dialogs.PromptAsync(this, Loc.Get(K.LogViewSaveTitle), Loc.Get(K.LogViewNameLabel), session?.ViewName ?? ""))?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        string? text = (await _app.Dialogs.PromptAsync(this, Loc.Get(K.LogViewSaveTitle), Loc.Get(K.LogViewFilterLabel), current.Text ?? "", FilterProblem))?.Trim();
        if (text is null) return;
        string? highlight = (await _app.Dialogs.PromptAsync(this, Loc.Get(K.LogViewSaveTitle), Loc.Get(K.LogViewHighlightLabel), session?.Highlight ?? "", FilterProblem))?.Trim();
        if (highlight is null) return;
        string? levelText = await _app.Dialogs.PromptAsync(this, Loc.Get(K.LogViewSaveTitle), Loc.Get(K.LogViewLevelsLabel), string.Join(", ", current.Levels.Select(l => l.ToString().ToLowerInvariant())));
        if (levelText is null) return;
        var levels = new List<LogLevel>();
        foreach (string part in levelText.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Enum.TryParse<LogLevel>(part, ignoreCase: true, out var level) || !Enum.IsDefined(level))
            {
                SetStatusMessage(Loc.Get(K.LogViewBadLevel, ("level", part)));
                return;
            }
            if (!levels.Contains(level)) levels.Add(level);
        }
        if (FilterProblem(text).Length + FilterProblem(highlight).Length > 0)
        {
            SetStatusMessage(FilterProblem(text).Length > 0 ? FilterProblem(text) : FilterProblem(highlight));
            return;
        }
        string? rules = od.LogLevelsName.Length == 0 || od.LogLevelsName == Loc.Get(K.LogLevelsDefault) ? null : od.LogLevelsName;
        var view = new LogView(name, new LogFilter(levels, text.Length == 0 ? null : text, TextIsRegex: true), rules, highlight.Length == 0 ? null : highlight, od.Path, CaptureOffsets(tab).Caret);
        try { _app.LogViews.Save(view); }
        catch (IOException e)
        {
            SetStatusMessage(Loc.Get(K.LogViewSaveFailed, ("reason", e.Message)));
            return;
        }
        ApplyLogView(tab, view, restorePosition: false);
        SetStatusMessage(Loc.Get(K.LogViewSaved, ("name", name)));
    }

    private static string FilterProblem(string text)
    {
        if (text.Trim().Length == 0) return "";
        try { _ = new Regex(text.Trim(), RegexOptions.CultureInvariant); return ""; }
        catch (ArgumentException e) { return Loc.Get(K.LogViewBadFilter, ("reason", e.Message)); }
    }

    /// <summary>Lists the saved views; the chosen one sets the level rules, the filter and the caret position of the document.</summary>
    internal CommandOutcome ShowLogViews()
    {
        if (Active is not { Custom: null, Hex: null } tab) return CommandOutcome.NotHandled;
        if (tab.Doc.Path is null)
        {
            SetStatusMessage(Loc.Get(K.LogFollowNoFile));
            return CommandOutcome.Handled;
        }
        var views = _app.LogViews.Load(out var problem);
        if (problem is not null) SetStatusMessage(Loc.Get(K.LogViewFileProblem, ("reason", problem)));
        var rows = views.Select(v => new QuickRow(v.Name, null, Describe(v), null, true, null, v.Name, () => ApplyLogView(tab, v, restorePosition: true))).ToList();
        ShowChoice(Loc.Get(K.LogViewWatermark), Loc.Get(K.A11yLogViews), rows, emptyText: Loc.Get(K.LogViewNone));
        return CommandOutcome.Handled;
    }

    private static string Describe(LogView v) =>
        string.Join(" · ", new[] { v.Filter.Levels.Count > 0 ? string.Join(", ", v.Filter.Levels.Select(l => l.ToString().ToLowerInvariant())) : null, v.Filter.Text, v.FilePath is { } p ? Path.GetFileName(p) : null }.Where(s => !string.IsNullOrEmpty(s)));

    private void ApplyLogView(DocTab tab, LogView view, bool restorePosition)
    {
        var od = tab.Doc;
        var rules = LogLevelRules.Default;
        string rulesName = Loc.Get(K.LogLevelsDefault);
        if (view.LevelRules is { } wanted)
        {
            if (_app.Tools.Load().Patterns.FirstOrDefault(p => p.Levels is not null && p.Title == wanted) is { } pattern) (rules, rulesName) = (pattern.Levels!, wanted);
            else SetStatusMessage(Loc.Get(K.LogViewRulesMissing, ("name", wanted)));
        }
        od.ChooseLanguage(LanguageModes.ById(LanguageModes.LogId));
        od.SetLogLevels(rules, rulesName);
        try { od.SetLogHighlight(view.Highlight, TimeSpan.FromMilliseconds(PluginLimitsPlaceholders.LogHighlightMatchTimeoutMilliseconds)); }
        catch (ArgumentException e) { SetStatusMessage(Loc.Get(K.LogViewBadFilter, ("reason", e.Message))); }
        try { StartLogSession(od, view.Filter, view.LevelRules, view.Name, view.Highlight); }
        catch (LogPatternException e)
        {
            SetStatusMessage(Loc.Get(K.LogViewBadFilter, ("reason", e.Message)));
            return;
        }
        if (!restorePosition) return;
        if (view.FilePath is { } saved && !string.Equals(Path.GetFullPath(saved), Path.GetFullPath(od.Path!), PathComparison))
            SetStatusMessage(Loc.Get(K.LogViewOtherFile, ("file", Path.GetFileName(saved))));
        else
        {
            long at = Math.Min(view.Position, od.Doc.ByteLength);
            RecordJump();
            ApplyOffsets(tab, at, at);
            FocusView(tab);
        }
    }

    /// <summary>Jumps to the next or previous line that the filter of the live view keeps.</summary>
    internal CommandOutcome LogViewStep(bool forward)
    {
        if (Active is not { Custom: null, Hex: null } tab) return CommandOutcome.NotHandled;
        var od = tab.Doc;
        if (!_logSessions.TryGetValue(od, out var session) || session.Follower.Filter.IsEmpty)
        {
            SetStatusMessage(Loc.Get(K.LogViewNoView));
            return CommandOutcome.Handled;
        }
        if (od.Doc.IsModified)
        {
            SetStatusMessage(Loc.Get(K.LogFollowModified));
            return CommandOutcome.Handled;
        }
        long caret = CaptureOffsets(tab).Caret;
        long? hit = forward
            ? session.Follower.Next(caret)
            : session.Follower.Previous(LogScan.LineStart(od.Doc.GetSnapshot(), od.Doc.Codec, od.Doc.BomLength, caret));
        if (hit is not { } offset)
        {
            SetStatusMessage(Loc.Get(forward ? K.LogViewNoNext : K.LogViewNoPrevious));
            return CommandOutcome.Handled;
        }
        // a match found after the document was last read lies beyond its end
        if (offset >= od.Doc.ByteLength && !ReloadLogDocument(od, toEnd: false)) return CommandOutcome.Handled;
        RecordJump();
        SelectTab(tab);
        ApplyOffsets(tab, offset, offset);
        FocusView(tab);
        SetStatusMessage(Loc.Get(K.LogViewFound));
        return CommandOutcome.Handled;
    }
}
