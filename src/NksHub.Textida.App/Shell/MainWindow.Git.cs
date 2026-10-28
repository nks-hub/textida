using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Ui;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// What the window does with the built-in Git plugin besides running its commands: the change marks in the gutter of a text tab (asked for in the background once the edits stop), a click on a
/// mark that stages the change or takes it back from the index, and the buttons of a comparison tab that stage the difference the tab is on.
/// </summary>
internal sealed partial class MainWindow
{
    private DispatcherTimer? _gitGutterTimer;
    private readonly HashSet<DocTab> _gitGutterDirty = [];
    private bool _gitGutterRunning, _gitGutterAgain;
    private StatusModel? _gitStatusSeen;

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
        foreach (var tab in _gitGutterDirty.ToList())
        {
            if (tab.Group is not { } group || group.Active != tab) continue;
            _gitGutterDirty.Remove(tab);
            if (tab.Surface is not { } surface) continue;
            if (!enabled)
            {
                surface.SetGitMarks(null);
                continue;
            }
            var marks = await _app.Plugins.GitMarksAsync(this, tab);
            if (marks is null)
            {
                if (_app.Plugins.IsBusy) ScheduleGitGutter(tab); // a command of the user was running: ask again when it is over
                continue;
            }
            if (tab.Doc.Doc.Revision == marks.Revision) surface.SetGitMarks(marks.Marks);
        }
    }

    /// <summary>A mark of the gutter was clicked: the change is staged, or, when it is staged already, taken back from the index. The plugin says in a notice how it went.</summary>
    private async Task ClickGitMarkAsync(DocTab tab, GitMark mark)
    {
        if (tab.Surface is not { } surface || tab.Doc.TextBackend is not { } backend) return;
        var text = surface.Document;
        long offset = mark.Line < text.LineCount ? backend.ToByteOffset(text.GetLineByNumber(mark.Line + 1).Offset) : tab.Doc.Doc.ByteLength;
        await _app.Plugins.RunGitAsync(this, tab, mark.Staged ? "git.unstageHunk" : "git.stageHunk", offset);
        // the plugin opens the documents of the index for a moment, which moves the tabs
        if (Active != tab) SelectTab(tab);
        FocusView(tab);
        ScheduleGitGutter(tab);
    }

    /// <summary>The Git status item of the plugin changed (a command ran or the plugin saw the repository change): the marks of the open files may have changed too.</summary>
    private void OnGitStatusChanged(PluginUiHost ui)
    {
        var status = ui.Status.TryGetValue(PluginServices.GitPluginId, out var current) ? current : null;
        if (Equals(status, _gitStatusSeen)) return;
        _gitStatusSeen = status;
        foreach (var tab in Tabs) ScheduleGitGutter(tab);
    }

    private void ReleaseGitGutter()
    {
        _gitGutterTimer?.Stop();
        _gitGutterDirty.Clear();
    }

    /// <summary>
    /// Runs a button of a comparison tab that the plugin added on the difference the tab is on. The command goes to the plugin for the document of the right side, at the byte where the difference
    /// starts there; when that went through, the plugin's refresh command follows, which opens the comparison again in the place of this tab.
    /// </summary>
    private async Task RunDiffActionAsync(DiffView view, DiffRequest request, DiffAction action, int hunk)
    {
        if (request.PluginId is not { } plugin || _app.Plugins.OpenTabOf(request.Right.Snapshot.DocumentId) is not { } target)
        {
            SetStatusMessage(Loc.Get(K.CompareActionGone));
            return;
        }
        if (target.Tab.Doc.Doc.Revision != request.Right.Snapshot.Revision || view.Session.Diff is not { } diff || hunk < 0 || hunk >= diff.Hunks.Count)
        {
            SetStatusMessage(Loc.Get(K.CompareActionStale));
            return;
        }
        var own = Tabs.FirstOrDefault(t => ReferenceEquals(t.Custom, view));
        long offset = diff.B.OffsetOf(diff.Hunks[hunk].BStart);
        bool done = await _app.Plugins.RunDiffActionAsync(target.Window, target.Tab, plugin, action.Command, offset);
        if (done && action.Refresh is { } refresh) await _app.Plugins.RunDiffActionAsync(target.Window, target.Tab, plugin, refresh, null);
        else if (own is not null && Active != own) SelectTab(own);
    }
}
