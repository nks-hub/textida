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
