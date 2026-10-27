using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Journal;

namespace NksHub.Textida.App.Shell;

internal sealed partial class MainWindow
{
    public static StringComparison PathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private long LargeFileBytes => _app.Workbench.Settings.GetInt("files.largeFileThreshold") << 20;

    // ---- opening ----

    public DocTab NewDocument()
    {
        var od = OpenDocument.New(Options.NewDocumentCodec);
        Adopt(od);
        return AddView(od, _activeGroup);
    }

    /// <summary>Opens a file in this window or activates its tab. Throws <see cref="IOException"/> with a localised message when it cannot be read.</summary>
    public DocTab OpenFile(string path, int? line)
    {
        path = Path.GetFullPath(path);
        var tab = Tabs.FirstOrDefault(t => string.Equals(t.Path, path, PathComparison));
        _app.Recent.Add(path);
        if (tab is null)
        {
            var od = OpenDocument.Open(path, LargeFileBytes);
            Adopt(od);
            tab = AddView(od, _activeGroup);
            tab = OfferHex(tab);
        }
        else SelectTab(tab);
        if (line is { } l) tab.GoToLine(l);
        FocusView(tab);
        return tab;
    }

    /// <summary>Wraps documents the journal brought back and shows them in the groups the saved session names.</summary>
    internal DocTab AddRecovered(OpenDocument od, TabGroup group)
    {
        Adopt(od);
        return AddView(od, group);
    }

    private void Adopt(OpenDocument od)
    {
        _docs.Add(od);
        od.Changed += () => OnDocChanged(od);
        od.Replaced += () => OnDocReplaced(od);
        od.ExternalChangeDetected += change => OnExternalChange(od, change);
        od.HistoryProblem += result => SetStatusMessage(result.NoSpace ? Loc.Get(K.HistoryNoSpace) : Loc.Get(K.HistoryFailed, ("reason", result.Problem ?? "")));
        AcquireLock(od);
        if (od.Path is { } localPath) od.RemoteCopy ??= _app.Remote.Copies.FindByLocalPath(localPath);
        _app.Journal.Track(od);
        od.EnsureWatching();
        AttachReview(od);
        TrackEdits(od);
        DetectIndentation(od);
        ConfigureAutosave(od);
    }

    /// <summary>With <c>editor.detectIndentation</c> on, the indentation of a file wins over the configured one for that file.</summary>
    private void DetectIndentation(OpenDocument od)
    {
        if (od.TextBackend is not { } text || od.Path is null || !_app.Workbench.Settings.GetBool("editor.detectIndentation")) return;
        int length = Math.Min(text.TextDocument.TextLength, 64 * 1024);
        od.IndentOverride = IndentDetector.Detect(text.TextDocument.GetText(0, length), EditorOptions.From(_app.Workbench.Settings, od.Language.Id).Indent);
    }

    /// <summary>With <c>files.autoSave</c> on, a named document is written to its own file now and then; off by default, and never the journal's job.</summary>
    private void ConfigureAutosave(OpenDocument od)
    {
        bool on = _app.Workbench.Settings.GetString("files.autoSave") == "afterDelay" && od.Path is not null && !od.IsLarge && !od.LockedReadOnly;
        if (!on)
        {
            od.Autosave?.Dispose();
            od.Autosave = null;
            return;
        }
        od.Autosave ??= new AutosaveService(od.Doc, new AutosaveOptions { Enabled = true, Interval = OpenDocument.AutosaveInterval }, _app.Backups, a => Dispatcher.UIThread.Post(a));
    }

    private DocTab AddView(OpenDocument od, TabGroup group, int? index = null)
    {
        var tab = new DocTab(od, OptionsFor(od));
        if (tab.Surface is { } surface)
        {
            surface.TextArea.Caret.PositionChanged += (_, _) => { if (tab == Active) UpdateStatus(); };
            surface.TextArea.SelectionChanged += (_, _) => { if (tab == Active) UpdateStatus(); };
            surface.CaretsChanged += () => { if (tab == Active) UpdateStatus(); };
            surface.TextTyped += _app.Workbench.Commands.RecordText;
            AutomationProperties.SetName(surface, Loc.Get(K.A11yEditor));
            surface.Watermark = od.Path is null ? Loc.Get(K.EditorPlaceholder) : "";
        }
        else if (tab.Hex is { } hex) WireHex(tab, hex);
        else
        {
            tab.Paged!.ViewChanged += () => { if (tab == Active) UpdateStatus(); };
            tab.Paged.Refused += SetStatusMessage;
            tab.Paged.Copied += RememberCopy;
            AutomationProperties.SetName(tab.Paged, Loc.Get(K.A11yEditor));
        }
        if (tab.Syntax is { } syntax) syntax.PendingChanged += () => { if (tab == Active) UpdateStatus(); };
        AttachEditorMenu(tab);
        group.Add(tab, index);
        _activeGroup = group;
        if (od.Lock is not null || od.IsVirtual) ApplyLock(od);
        if (od.RemoteCopy is not null && !od.LockedReadOnly) ShowRemoteBarOffline(tab);
        OnActiveTabChanged();
        _app.SessionChanged();
        return tab;
    }

    // ---- state changes of a document ----

    private void OnDocChanged(OpenDocument od)
    {
        long t = Core.TypingTrace.Begin();
        RefreshHeaders();
        Core.TypingTrace.End("win.refreshHeaders", t);
        if (Active?.Doc == od) { UpdateStatus(); t = Core.TypingTrace.Begin(); RefreshTitle(); Core.TypingTrace.End("win.refreshTitle", t); }
        t = Core.TypingTrace.Begin();
        od.EnsureWatching();
        if (od.Autosave is null) ConfigureAutosave(od);
        Core.TypingTrace.End("win.watchAutosave", t);
    }

    /// <summary>The document under the tabs was swapped (reload). A view of the other kind (text or paged) is rebuilt in place.</summary>
    private void OnDocReplaced(OpenDocument od)
    {
        foreach (var tab in od.Views.ToList())
        {
            var group = tab.Group!;
            int index = group.Tabs.ToList().IndexOf(tab);
            bool wasActive = Active == tab;
            tab.ShowBar(null);
            tab.Table?.Rebind();
            if (tab.Surface is { } s && !od.IsLarge)
            {
                s.BindDocument();
                s.ApplyOptions(OptionsFor(tab));
                continue;
            }
            group.Remove(tab);
            od.Views.Remove(tab);
            tab.Close();
            var fresh = AddViewAt(od, group, index);
            if (wasActive) SelectTab(fresh);
        }
        od.EnsureWatching();
        TrackEdits(od);
        od.Autosave?.Dispose();
        od.Autosave = null;
        ConfigureAutosave(od);
        UpdateStatus();
    }

    private DocTab AddViewAt(OpenDocument od, TabGroup group, int index) => AddView(od, group, index);

    // ---- closing ----

    /// <summary>Closes without asking and without keeping anything; for tests and for <c>--wait</c> callers that end the session themselves.</summary>
    public void CloseTab(DocTab tab) => RemoveTab(tab, untrack: true);

    /// <summary>Closes a tab. The last view of a document with unsaved changes asks: save, discard, or leave the work in the journal.</summary>
    public async Task<bool> CloseTabAsync(DocTab tab)
    {
        var od = tab.Doc;
        bool keep = false;
        if (od.Views.Count == 1 && od.HasUnsavedWork)
        {
            switch (await AskUnsavedAsync(od))
            {
                case UnsavedAnswer.Save:
                    if (!await SaveAsync(tab)) return false;
                    break;
                case UnsavedAnswer.Discard: break;
                case UnsavedAnswer.KeepInJournal: keep = true; break;
                default: return false;
            }
        }
        RemoveTab(tab, untrack: !keep);
        return true;
    }

    private void RemoveTab(DocTab tab, bool untrack)
    {
        var od = tab.Doc;
        var group = tab.Group;
        if (group is null) return;
        group.Remove(tab);
        od.Views.Remove(tab);
        tab.Close();
        tab.ShowBar(null);
        if (od.Views.Count == 0)
        {
            ForgetDocument(od);
            if (untrack) _app.Journal.Untrack(od);
            _docs.Remove(od);
            od.Closed.TrySetResult();
            _app.Plugins.Forget(od.Doc);
            od.Dispose();
        }
        RemoveGroupIfEmpty(group);
        if (!_groups.Contains(_activeGroup)) _activeGroup = _groups[0];
        OnActiveTabChanged();
        _app.SessionChanged();
    }

    public async Task<bool> CloseOthersAsync(DocTab keep)
    {
        foreach (var tab in keep.Group!.Tabs.Where(t => t != keep).ToList())
            if (!await CloseTabAsync(tab)) return false;
        return true;
    }

    // ---- groups and split ----

    /// <summary>Shows a second view of the active document in the group to the right; both views share revision and undo.</summary>
    public DocTab? SplitView()
    {
        if (Active is not { } tab) return null;
        int at = _groups.IndexOf(tab.Group!);
        var target = at + 1 < _groups.Count ? _groups[at + 1] : AddGroup(at + 1);
        return AddView(tab.Doc, target);
    }

    public void MoveToOtherGroup()
    {
        if (Active is not { } tab) return;
        int at = _groups.IndexOf(tab.Group!);
        TabGroup target;
        if (_groups.Count == 1) { if (tab.Group!.Tabs.Count < 2) return; target = AddGroup(1); }
        else target = _groups[(at + 1) % _groups.Count];
        MoveTab(tab, target, target.Tabs.Count);
    }

    public void MoveTab(DocTab tab, TabGroup target, int index)
    {
        var source = tab.Group!;
        if (source == target)
        {
            int from = source.Tabs.ToList().IndexOf(tab);
            if (index > from) index--;
            source.Remove(tab);
            target.Add(tab, index);
        }
        else
        {
            source.Remove(tab);
            target.Add(tab, index);
            RemoveGroupIfEmpty(source);
        }
        _activeGroup = target;
        SelectTab(tab);
        _app.SessionChanged();
    }

    private IReadOnlyList<(string Label, Action Run)> TabContextActions(DocTab tab)
    {
        var actions = new List<(string, Action)> { (Loc.Get(K.TabMenuClose), () => _ = CloseTabAsync(tab)) };
        if (tab.Group!.Tabs.Count > 1) actions.Add((Loc.Get(K.TabMenuCloseOthers), () => _ = CloseOthersAsync(tab)));
        actions.Add((Loc.Get(K.TabMenuSplit), () => { SelectTab(tab); SplitView(); }));
        if (_groups.Count > 1 || tab.Group.Tabs.Count > 1) actions.Add((Loc.Get(K.TabMenuMove), () => { SelectTab(tab); MoveToOtherGroup(); }));
        if (tab.Path is { } path) actions.Add((Loc.Get(K.TabMenuCopyPath), () => _ = Clipboard?.SetTextAsync(path)));
        return actions;
    }

    // ---- files dropped from the system ----

    private DragEventArgs? _dropHandled;

    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is not { Length: > 0 }) return;
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnWindowDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is not { Length: > 0 } files || _dropHandled == e) return;
        _dropHandled = e;
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        var group = (e.Source as Visual)?.FindAncestorOfType<TabGroup>() ?? _activeGroup;
        OpenDropped(group, paths);
        e.Handled = true;
    }

    internal void OpenDropped(TabGroup group, IReadOnlyList<string> paths)
    {
        _activeGroup = group;
        foreach (var path in paths)
        {
            try { OpenFile(path, null); }
            catch (IOException ex) { SetStatusMessage(ex.Message); }
        }
    }

    internal async Task OpenWithDialogAsync()
    {
        foreach (var path in await _app.Dialogs.PickFilesAsync(this))
        {
            try { OpenFile(path, null); }
            catch (IOException e) { SetStatusMessage(e.Message); }
        }
    }
}
