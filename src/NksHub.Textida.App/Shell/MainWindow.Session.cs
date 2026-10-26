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
            groups.Add(new SessionGroup([.. g.Tabs.Select(CaptureTab)], g.Active?.Doc.JournalId));
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
        return new SessionTab(tab.Doc.JournalId ?? Guid.Empty, tab.Title, doc.Revision, ranges);
    }

    // ---- restoring ----

    /// <summary>Reopens the tabs, groups and order of the last session and the work the journal kept; after a crash a bar says what came back.</summary>
    internal void RestoreFrom(JournalService journal)
    {
        var recovery = journal.Recovery;
        if (recovery.Documents.Count == 0) return;
        var opened = new Dictionary<Guid, OpenDocument>();
        var byId = recovery.Documents.Where(d => d.Document is not null).ToDictionary(d => d.DocId);

        OpenDocument Open(RecoveredDocument rd)
        {
            if (opened.TryGetValue(rd.DocId, out var existing)) return existing;
            var od = OpenDocument.FromRecovered(rd);
            Adopt(od);
            return opened[rd.DocId] = od;
        }

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
                    if (sg.ActiveDocId == st.DocId && active is null) active = tab;
                }
                if (active is not null) group.Select(active);
                if (index - 1 == session.ActiveGroup) _activeGroup = group;
            }
        }
        // work the journal kept for tabs that were closed with "keep in journal" or that the session does not list
        foreach (var rd in recovery.Documents.Where(d => d.Document is not null && !opened.ContainsKey(d.DocId)))
            AddView(Open(rd), _groups[0]);
        if (!_groups.Contains(_activeGroup)) _activeGroup = _groups[0];
        OnActiveTabChanged();

        if (journal.CrashedLastTime) ShowRestoreBar(recovery);
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

    private string DescribeRecovered(RecoveredDocument rd)
    {
        string name = rd.Path is { } p ? System.IO.Path.GetFileName(p) : rd.Title ?? Loc.Get(K.TabUntitled, ("number", ""));
        string status = rd.Status switch
        {
            RecoveryStatus.Restored => Loc.Get(K.RestoreStatusRestored),
            RecoveryStatus.Clean => Loc.Get(K.RestoreStatusClean),
            RecoveryStatus.Conflict => Loc.Get(K.RestoreStatusConflict),
            RecoveryStatus.Missing => Loc.Get(K.RestoreStatusMissing),
            _ => Loc.Get(K.RestoreStatusFailed, ("reason", rd.Error ?? "")),
        };
        string when = rd.LastChange is { } t ? " · " + t.LocalDateTime.ToString("g") : "";
        return $"{name}: {status}{when}";
    }

    private void ShowRestoreBar(RecoveryResult recovery)
    {
        int back = recovery.Documents.Count(d => d.Document is not null);
        int unsaved = recovery.Documents.Count(d => d.Status == RecoveryStatus.Restored && d.PendingRecords > 0);
        int failed = recovery.Documents.Count(d => d.Document is null);
        var message = Loc.Plural(K.RestoreBar, back, ("unsaved", unsaved));
        if (failed > 0) message += " " + Loc.Plural(K.RestoreBarFailed, failed);
        var details = recovery.Documents.Select(DescribeRecovered).ToList();
        ShowNotice("restore", message, [(Loc.Get(K.RestoreDetails), () => _ = _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.RestoreTitle), message, [new DialogButton(Loc.Get(K.DialogClose), IsDefault: true, IsCancel: true)], details)))]);
    }

    // ---- warnings about the journal itself ----

    /// <summary>A journal on the fallback location, or none at all, must be said out loud: nothing may promise a recovery that cannot happen.</summary>
    internal void ShowJournalWarnings()
    {
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
        bool askFirst = !_app.Journal.IsActive && _docs.Any(d => d.HasUnsavedWork && !d.IsLarge);
        if (!askFirst)
        {
            _closeApproved = true;
            if (_app.Windows.Count == 1) _app.SaveSession();
            return;
        }
        e.Cancel = true;
        foreach (var od in _docs.Where(d => d.HasUnsavedWork && !d.IsLarge).ToList())
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
        if (_app.Windows.Count == 1) _app.SaveSession();
        Close();
    }
}
