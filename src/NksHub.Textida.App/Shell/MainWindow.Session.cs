using Avalonia.Controls;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Journal;

namespace NksHub.Textida.App.Shell;

internal sealed partial class MainWindow
{
    private bool _journalBrokenShown;

    // ---- what the journal stores about this window ----

    /// <summary>Groups, tab order, active tabs and the primary selection of every tab.</summary>
    internal SessionModel CaptureSession()
    {
        var groups = new List<SessionGroup>();
        int activeGroup = 0;
        foreach (var g in _groups.Where(g => g.Tabs.Count > 0))
        {
            if (g == _activeGroup) activeGroup = groups.Count;
            groups.Add(new SessionGroup([.. g.Tabs.Where(t => !t.Doc.IsResults && !t.Doc.IsVirtual).Select(CaptureTab)], g.Active?.Doc.JournalId));
        }
        return new SessionModel(groups, activeGroup);
    }

    private static SessionTab CaptureTab(DocTab tab)
    {
        var doc = tab.Doc.Doc;
        var ranges = new List<ByteRange>();
        if (tab.Surface is { } surface)
        {
            var sel = surface.GetSelections()[0];
            var text = surface.Document;
            long Byte(int offset)
            {
                var loc = text.GetLocation(offset);
                return doc.TryGetOffset(new LinePosition(loc.Line - 1, loc.Column - 1), out long b) ? b : -1;
            }
            long a = Byte(sel.Start), z = Byte(sel.End);
            if (a >= 0 && z >= a) ranges.Add(new ByteRange(a, z));
        }
        // an unnamed document keeps its number as the title, so "Untitled 3" comes back as 3 in any language
        return new SessionTab(tab.Doc.JournalId ?? Guid.Empty, tab.Path is null ? tab.Doc.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) : tab.Title, doc.Revision, ranges, tab.Doc.Bookmarks.Offsets, tab.Doc.HexBookmarks.Capture());
    }

    // ---- restoring ----

    /// <summary>Reopens the tabs, groups and order of the last session and the work the journal kept; after a crash a bar says what came back.</summary>
    internal void RestoreFrom(JournalService journal)
    {
        var recovery = journal.Recovery;
        if (recovery.Documents.Count == 0) return;
        var opened = new Dictionary<Guid, OpenDocument>();
        var byId = recovery.Documents.Where(d => d.Document is not null).ToDictionary(d => d.DocId);

        var numbers = recovery.Session?.AllTabs.Where(t => int.TryParse(t.Title, out _)).GroupBy(t => t.DocId).ToDictionary(g => g.Key, g => int.Parse(g.First().Title!)) ?? [];

        OpenDocument Open(RecoveredDocument rd)
        {
            if (opened.TryGetValue(rd.DocId, out var existing)) return existing;
            var od = OpenDocument.FromRecovered(rd, numbers.TryGetValue(rd.DocId, out var n) ? n : null);
            Adopt(od);
            return opened[rd.DocId] = od;
        }

        TabGroup? wantActive = null;
        if (recovery.Session is { } session)
        {
            int index = 0;
            foreach (var sg in session.Groups.Where(g => g.Tabs.Any(t => byId.ContainsKey(t.DocId))))
            {
                var group = index++ == 0 ? _groups[0] : AddGroup();
                DocTab? active = null;
                foreach (var st in sg.Tabs)
                {
                    if (!byId.TryGetValue(st.DocId, out var rd)) continue;
                    var tab = AddView(Open(rd), group);
                    ApplySelection(tab, st);
                    if (st.Bookmarks is { Count: > 0 } marks && !tab.Doc.Bookmarks.HasAny) tab.Doc.Bookmarks.Restore(marks, tab.Doc.Doc.Revision);
                    if (st.HexBookmarks is { Count: > 0 } regions && tab.Doc.HexBookmarks.Count == 0) tab.Doc.HexBookmarks.Restore(regions, tab.Doc.Doc.Revision);
                    if (sg.ActiveDocId == st.DocId && active is null) active = tab;
                }
                if (active is not null) group.Select(active);
                if (index - 1 == session.ActiveGroup) wantActive = group;
            }
        }
        // work the journal kept for tabs that were closed with "keep in journal" or that the session does not list
        foreach (var rd in recovery.Documents.Where(d => d.Document is not null && !opened.ContainsKey(d.DocId)))
            AddView(Open(rd), _groups[0]);
        _activeGroup = wantActive ?? (_groups.Contains(_activeGroup) ? _activeGroup : _groups[0]);
        OnActiveTabChanged();

        if (journal.CrashedLastTime) ShowRestoreBar(recovery);
        foreach (var rd in journal.Pending) ShowPendingBar(rd);
    }

    private static void ApplySelection(DocTab tab, SessionTab saved)
    {
        if (tab.Surface is not { } surface || saved.Selections.Count == 0) return;
        var doc = tab.Doc.Doc;
        var text = surface.Document;
        int Offset(long b) => doc.TryGetPosition(b, out var p) ? text.GetOffset((int)p.Line + 1, (int)p.Column + 1) : -1;
        int start = Offset(saved.Selections[0].Start), end = Offset(saved.Selections[0].End);
        if (start >= 0 && end >= start) surface.SetSelections([new Sel(start, end)]);
    }

    private static string NameOfRecovered(RecoveredDocument rd) =>
        rd.Path is { } p ? System.IO.Path.GetFileName(p) : rd.Title ?? Loc.Get(K.TabUntitled, ("number", ""));

    private static string StatusOfRecovered(RecoveredDocument rd) => rd.Status switch
    {
        RecoveryStatus.Restored => Loc.Get(K.RestoreStatusRestored),
        RecoveryStatus.Clean => Loc.Get(K.RestoreStatusClean),
        RecoveryStatus.Conflict => Loc.Get(K.RestoreStatusConflict),
        RecoveryStatus.Missing => Loc.Get(K.RestoreStatusMissing),
        RecoveryStatus.Pending => Loc.Get(K.RestoreStatusPending, ("size", OpenDocument.FormatSize(rd.PendingBytes))),
        _ => Loc.Get(K.RestoreStatusFailed, ("reason", rd.Error ?? "")),
    };

    private string DescribeRecovered(RecoveredDocument rd)
    {
        string when = rd.LastChange is { } t ? " · " + t.LocalDateTime.ToString("g") : "";
        return $"{NameOfRecovered(rd)}: {StatusOfRecovered(rd)}{when}";
    }

    private void ShowRestoreBar(RecoveryResult recovery)
    {
        int back = recovery.Documents.Count(d => d.Document is not null);
        int unsaved = recovery.Documents.Count(d => d.Status == RecoveryStatus.Restored && d.PendingRecords > 0);
        int failed = recovery.Documents.Count(d => d.Document is null && d.Status != RecoveryStatus.Pending);
        if (back == 0 && failed == 0) return; // only large changes wait: their own bars say it
        var message = Loc.Plural(K.RestoreBar, back, ("unsaved", unsaved));
        if (failed > 0) message += " " + Loc.Plural(K.RestoreBarFailed, failed);
        var details = recovery.Documents.Select(DescribeRecovered).ToList();
        ShowNotice("restore", message, [(Loc.Get(K.RestoreDetails), () => _ = _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.RestoreTitle), message, [new DialogButton(Loc.Get(K.DialogClose), IsDefault: true, IsCancel: true)], details, "recovery")))]);
    }

    // ---- large changes that wait for a decision ----

    private static string PendingKey(RecoveredDocument rd) => "recover:" + rd.DocId.ToString("N");

    /// <summary>A document whose changes were too large to replay at start waits behind a bar: restore them (the journal is read in the background) or discard them.</summary>
    private void ShowPendingBar(RecoveredDocument rd)
    {
        string when = rd.LastChange is { } t ? t.LocalDateTime.ToString("g") : "";
        ShowNotice(PendingKey(rd), Loc.Get(K.RecoverLargeBar, ("name", NameOfRecovered(rd)), ("size", OpenDocument.FormatSize(rd.PendingBytes)), ("when", when)),
            [(Loc.Get(K.RecoverLargeRestore), () => _ = RecoverLargeAsync(rd)), (Loc.Get(K.RecoverLargeDiscard), () => _ = DiscardLargeAsync(rd))], warning: true);
    }

    /// <summary>Replays the large changes off the UI thread and opens the document like any restored one; the file of the document is not touched.</summary>
    internal async Task RecoverLargeAsync(RecoveredDocument pending)
    {
        string name = NameOfRecovered(pending);
        DismissNotice(PendingKey(pending));
        SetStatusMessage(Loc.Get(K.RecoverLargeRunning, ("name", name), ("size", OpenDocument.FormatSize(pending.PendingBytes))));
        RecoveredDocument back;
        try { back = await _app.Journal.RecoverPendingAsync(pending); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ObjectDisposedException or NotSupportedException)
        {
            ShowNotice(PendingKey(pending), Loc.Get(K.RecoverLargeFailed, ("name", name), ("status", e.Message)), warning: true);
            return;
        }
        if (back.Document is null)
        {
            ShowNotice(PendingKey(pending), Loc.Get(K.RecoverLargeFailed, ("name", name), ("status", StatusOfRecovered(back))), warning: true);
            return;
        }
        var od = OpenDocument.FromRecovered(back);
        Adopt(od);
        SelectTab(AddView(od, _activeGroup));
        SetStatusMessage(Loc.Get(K.RecoverLargeDone, ("name", name)));
    }

    /// <summary>Asks once more, because nothing brings the changes back, and then removes them from the journal.</summary>
    internal async Task DiscardLargeAsync(RecoveredDocument pending)
    {
        string name = NameOfRecovered(pending);
        var ask = new ChoiceRequest(Loc.Get(K.RecoverLargeDiscardTitle),
            Loc.Get(K.RecoverLargeDiscardMessage, ("name", name), ("size", OpenDocument.FormatSize(pending.PendingBytes))),
            [new DialogButton(Loc.Get(K.RecoverLargeDiscard)), new DialogButton(Loc.Get(K.DialogCancel), IsDefault: true, IsCancel: true)], HelpTopic: "recovery");
        if (await _app.Dialogs.ChooseAsync(this, ask) != 0) return;
        DismissNotice(PendingKey(pending));
        await _app.Journal.DiscardPendingAsync(pending.DocId);
        SetStatusMessage(Loc.Get(K.RecoverLargeDiscarded, ("name", name)));
    }

    // ---- warnings about the journal itself ----

    /// <summary>A journal on the fallback location, or none at all, must be said out loud: nothing may promise a recovery that cannot happen.</summary>
    internal void ShowJournalWarnings()
    {
        CheckPendingBatches();
        var location = _app.Journal.Location;
        if (!location.RecoveryAvailable)
            ShowNotice("journal", Loc.Get(K.JournalOff, ("primary", location.PrimaryProblem ?? ""), ("fallback", location.FallbackProblem ?? "")), warning: true);
        else if (location.UsedFallback)
            ShowNotice("journal", Loc.Get(K.JournalFallback, ("primary", location.PrimaryProblem ?? ""), ("directory", location.Directory ?? "")), warning: true);
    }

    private void CheckJournalHealth()
    {
        if (_journalBrokenShown || !_app.Journal.Location.RecoveryAvailable || _app.Journal.IsActive) return;
        _journalBrokenShown = true;
        ShowNotice("journal", Loc.Get(K.JournalBroken, ("reason", _app.Journal.Session.Fault?.Message ?? "")), warning: true);
    }

    // ---- closing the window ----

    /// <summary>
    /// With a working journal unsaved work survives the close (hot exit), so nothing is asked. Without one every document
    /// with unsaved changes asks, because the tab is the only place the text lives.
    /// </summary>
    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApproved) return;
        var running = _docs.Select(d => d.BackgroundSave).Where(t => t is { IsCompleted: false }).Select(t => Task.WhenAny(t!)).ToList();
        if (running.Count > 0)
        {
            // a save writes from the documents, which close with the window: wait for it, then go through the close again
            e.Cancel = true;
            SetStatusMessage(Loc.Get(K.StatusSaveWaiting));
            await Task.WhenAll(running);
            Close();
            return;
        }
        bool askFirst = !_app.Journal.IsActive && _docs.Any(d => d.HasUnsavedWork);
        if (!askFirst)
        {
            _closeApproved = true;
            if (_app.Windows.Count == 1) { UpdateActiveProject(); _app.SaveSession(); }
            return;
        }
        e.Cancel = true;
        foreach (var od in _docs.Where(d => d.HasUnsavedWork).ToList())
        {
            var tab = od.Views[0];
            SelectTab(tab);
            switch (await AskUnsavedAsync(od))
            {
                case UnsavedAnswer.Save: if (!await SaveAsync(tab)) return; break;
                case UnsavedAnswer.Discard: break;
                default: return;
            }
        }
        _closeApproved = true;
        if (_app.Windows.Count == 1) { UpdateActiveProject(); _app.SaveSession(); }
        Close();
    }
}
