using System.Text.RegularExpressions;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Full log mode (P9.4.2): folding uninteresting lines, opening the file of a stack trace frame, showing ANSI escapes raw, and the error heatmap of the
/// minimap, which is counted in the background and replaced when the document changes.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly Dictionary<OpenDocument, CancellationTokenSource> _heatJobs = [];

    // ---- folding ----

    /// <summary>Asks which lines stay (a level and a filter) and folds each run of the others into one placeholder row; the paged view dims them instead.</summary>
    internal CommandOutcome FoldLog()
    {
        if (Active is not { Custom: null, Hex: null } tab) return CommandOutcome.NotHandled;
        _ = FoldLogAsync(tab);
        return CommandOutcome.Handled;
    }

    private async Task FoldLogAsync(DocTab tab)
    {
        var od = tab.Doc;
        _logSessions.TryGetValue(od, out var session);
        string title = Loc.Get(K.LogFoldTitle);
        string? levelText = (await _app.Dialogs.PromptAsync(this, title, Loc.Get(K.LogFoldLevelLabel), "warning"))?.Trim();
        if (levelText is null) return;
        LogLevel? least = null;
        if (levelText.Length > 0)
        {
            if (!Enum.TryParse<LogLevel>(levelText, ignoreCase: true, out var level) || !Enum.IsDefined(level))
            {
                SetStatusMessage(Loc.Get(K.LogViewBadLevel, ("level", levelText)));
                return;
            }
            least = level;
        }
        string? filterText = (await _app.Dialogs.PromptAsync(this, title, Loc.Get(K.LogViewFilterLabel), session?.Follower.Filter.Text ?? "", FilterProblem))?.Trim();
        if (filterText is null) return;
        Regex? filter = null;
        if (filterText.Length > 0)
        {
            if (FilterProblem(filterText).Length > 0)
            {
                SetStatusMessage(FilterProblem(filterText));
                return;
            }
            filter = new Regex(filterText, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        var criteria = new LogFoldCriteria(least, filter);
        if (criteria.IsEmpty)
        {
            UnfoldLog();
            return;
        }
        if (!od.IsLog) od.ChooseLanguage(LanguageModes.ById(LanguageModes.LogId));
        if (tab.Paged is { } paged)
        {
            paged.SetFold(criteria);
            SetStatusMessage(Loc.Get(K.LogFoldDimmed));
        }
        else if (tab.Surface is { } surface) await FoldEditorAsync(tab, surface, criteria);
    }

    private async Task FoldEditorAsync(DocTab tab, EditorSurface surface, LogFoldCriteria criteria)
    {
        var od = tab.Doc;
        string text = surface.Document.Text;
        long revision = od.Doc.Revision;
        var rules = od.LogLevels;
        IReadOnlyList<LogFoldRange> ranges;
        try
        {
            ranges = await Task.Run(() =>
            {
                string[] lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
                return LogFold.Ranges(lines.Length, i => lines[i], rules, criteria);
            });
        }
        catch (RegexMatchTimeoutException)
        {
            SetStatusMessage(Loc.Get(K.LogTimedOut));
            return;
        }
        if (od.Doc.Revision != revision)
        {
            SetStatusMessage(Loc.Get(K.LogChanged));
            return;
        }
        int runs = surface.Fold(ranges, count => Loc.Get(K.LogFoldPlaceholder, ("count", count)));
        SetStatusMessage(runs == 0 ? Loc.Get(K.LogFoldNothing) : Loc.Get(K.LogFolded, ("runs", runs), ("lines", ranges.Sum(r => r.Count))));
    }

    /// <summary>Shows every line again.</summary>
    internal CommandOutcome UnfoldLog()
    {
        if (Active is not { Custom: null, Hex: null } tab) return CommandOutcome.NotHandled;
        tab.Surface?.ClearFolds();
        tab.Paged?.SetFold(null);
        SetStatusMessage(Loc.Get(K.LogUnfolded));
        return CommandOutcome.Handled;
    }

    // ---- stack trace frames ----

    /// <summary>Opens the file of the stack trace frame under the caret at its line.</summary>
    internal CommandOutcome OpenStackFrame()
    {
        if (Active is not { Custom: null, Hex: null } tab) return CommandOutcome.NotHandled;
        if ((tab.Surface?.StackTraceAtCaret() ?? tab.Paged?.StackTraceAtCaret()) is { } link) _ = OpenStackLinkAsync(tab, link);
        else SetStatusMessage(Loc.Get(K.LogStackNone));
        return CommandOutcome.Handled;
    }

    /// <summary>Finds the file next to the log and in the folders of the workspace (a bare name is searched below them) and opens it at the line.</summary>
    private async Task OpenStackLinkAsync(DocTab tab, StackTraceLink link)
    {
        var folders = new List<string>();
        if (tab.Path is { } logPath && Path.GetDirectoryName(Path.GetFullPath(logPath)) is { } logFolder) folders.Add(logFolder);
        folders.AddRange(_fileTree?.Roots ?? []);
        string? resolved = await Task.Run(() => StackTraceLinks.Resolve(link, folders.Distinct(StringComparer.OrdinalIgnoreCase)));
        if (resolved is null) SetStatusMessage(Loc.Get(K.LogStackNotFound, ("path", link.Path)));
        else JumpToFile(resolved, link.Line, link.Column > 0 ? link.Column : null);
    }

    // ---- ANSI ----

    /// <summary>Switches between the colors of ANSI escape sequences and the sequences as they are in the file.</summary>
    internal CommandOutcome ToggleLogRaw()
    {
        if (Active is not { Custom: null, Hex: null } tab) return CommandOutcome.NotHandled;
        bool raw = !(tab.Surface?.AnsiRaw ?? tab.Paged?.AnsiRaw ?? false);
        if (tab.Surface is { } surface) surface.AnsiRaw = raw;
        if (tab.Paged is { } paged) paged.AnsiRaw = raw;
        SetStatusMessage(Loc.Get(raw ? K.LogAnsiRaw : K.LogAnsiColors));
        return CommandOutcome.Handled;
    }

    // ---- heatmap ----

    /// <summary>Counts the error and warning lines of a log document in the background and gives the result to the minimaps of its tabs; a count that is running is dropped.</summary>
    private void ScheduleLogHeat(OpenDocument od, int delayMilliseconds)
    {
        if (_heatJobs.Remove(od, out var old)) old.Cancel();
        if (!od.IsLog)
        {
            foreach (var view in od.Views) view.Minimap?.SetHeat(null);
            return;
        }
        var cts = _heatJobs[od] = new CancellationTokenSource();
        _ = RunLogHeatAsync(od, cts, delayMilliseconds);
    }

    private async Task RunLogHeatAsync(OpenDocument od, CancellationTokenSource cts, int delayMilliseconds)
    {
        try
        {
            if (delayMilliseconds > 0) await Task.Delay(delayMilliseconds, cts.Token);
            var snapshot = od.Doc.GetSnapshot();
            var rules = od.LogLevels;
            long first = od.Doc.BomLength;
            var limits = PluginLimitsPlaceholders.LogHeat();
            var heat = await Task.Run(() => LogHeat.Compute(snapshot, snapshot.Codec, first, rules, PluginLimitsPlaceholders.LogHeatBuckets, limits, cts.Token), cts.Token);
            if (cts.IsCancellationRequested || !_docs.Contains(od)) return;
            foreach (var view in od.Views) view.Minimap?.SetHeat(heat);
        }
        catch (Exception e) when (e is OperationCanceledException or RegexMatchTimeoutException or IOException or ObjectDisposedException) { /* the picture stays as it was */ }
        finally
        {
            if (_heatJobs.GetValueOrDefault(od) == cts) _heatJobs.Remove(od);
            cts.Dispose();
        }
    }

    private void TrackLogHeat(OpenDocument od)
    {
        od.LanguageChanged += () => ScheduleLogHeat(od, 0);
        od.LogLevelsChanged += () => ScheduleLogHeat(od, 0);
        od.Replaced += () => ScheduleLogHeat(od, 0);
        od.Changed += () =>
        {
            if (od.IsLog) ScheduleLogHeat(od, PluginLimitsPlaceholders.LogHeatDelayMilliseconds);
        };
        if (od.IsLog) ScheduleLogHeat(od, 0);
    }
}
