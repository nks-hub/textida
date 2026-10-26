using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Tests;

/// <summary>The recovery journal in the app: every document journals, restore after a crash, session restore, and honest reports when the journal cannot be written.</summary>
[Collection("LocState")]
public class JournalAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private App _app;

    public JournalAppTests() => _app = TestApp.Prepare(_temp, _dialogs);

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static string Text(DocTab t) => t.Editor!.Document.Text;

    /// <summary>The previous run ends the way a crash ends it: the journal is flushed and released, no clean-exit flag is written.</summary>
    private MainWindow Crash()
    {
        _app.SaveSession();
        _app.Journal.Flush();
        _app = TestApp.Restart(_temp, _app, _dialogs);
        return _app.CreateWindow(null);
    }

    [AvaloniaFact]
    public void Every_document_journals()
    {
        var w = _app.CreateWindow(null);
        var unnamed = w.NewDocument();
        var named = w.OpenFile(_temp.File("n.txt", "x"), null);
        Assert.True(_app.Journal.IsActive);
        Assert.NotNull(_app.Journal.Session.IdOf(unnamed.Doc.Doc));
        Assert.NotNull(_app.Journal.Session.IdOf(named.Doc.Doc));
        Assert.NotNull(unnamed.Doc.JournalId);
        Assert.True(File.Exists(_app.Journal.Session.JournalPath));
    }

    [AvaloniaFact]
    public void After_a_crash_the_tabs_groups_and_unsaved_text_come_back_and_a_bar_says_what_returned()
    {
        var w = _app.CreateWindow(null);
        var path = _temp.File("saved.txt", "disk");
        var named = w.OpenFile(path, null);
        named.Editor!.Document.Insert(4, " + edit");
        var unnamed = w.NewDocument();
        unnamed.Editor!.Document.Text = "unsaved ŘČ text";
        var clean = w.OpenFile(_temp.File("clean.txt", "untouched"), null);
        _w_Select(w, named);
        w.SplitView(); // second group shows saved.txt again
        _w_Select(w, unnamed);
        var order = w.Groups.Select(g => g.Tabs.Select(t => t.Title).ToList()).ToList();
        var activeTitle = w.Active!.Title;
        Assert.Equal(2, w.Groups.Count);

        var w2 = Crash();
        w2.RestoreFrom(_app.Journal);

        Assert.Equal(2, w2.Groups.Count);
        Assert.Equal(order, w2.Groups.Select(g => g.Tabs.Select(t => t.Title).ToList()).ToList());
        Assert.Equal(activeTitle, w2.Active!.Title);
        var restoredNamed = w2.Tabs.First(t => t.Path == path);
        Assert.Equal("disk + edit", Text(restoredNamed));
        Assert.True(restoredNamed.Doc.Doc.IsModified);
        var restoredUnnamed = w2.Tabs.Single(t => t.Path is null);
        Assert.Equal("unsaved ŘČ text", Text(restoredUnnamed));
        Assert.True(restoredUnnamed.Doc.HasUnsavedWork);
        Assert.Equal("untouched", Text(w2.Tabs.Single(t => t.Path?.EndsWith("clean.txt", StringComparison.Ordinal) == true)));
        // the two views of saved.txt are one document again
        Assert.Equal(2, w2.Tabs.Count(t => t.Path == path));
        Assert.Single(w2.Tabs.Where(t => t.Path == path).Select(t => t.Doc).Distinct());

        Assert.True(_app.Journal.CrashedLastTime);
        var bar = Assert.Single(w2.Notices, n => n.Message.Contains("neočekávaném ukončení"));
        Assert.Contains("vrátily 3 karty", bar.Message);
        Assert.Contains("neuloženými změnami: 2)", bar.Message); // the named edit and the unnamed text
        Assert.Equal("disk", File.ReadAllText(path)); // restoring never touches a user file
    }

    private static void _w_Select(MainWindow w, DocTab tab) => w.SelectTab(tab);

    [AvaloniaFact]
    public async Task A_paged_document_comes_back_after_a_crash_with_the_typed_text_and_saves()
    {
        _app.Workbench.Set("files.largeFileThreshold", System.Text.Json.Nodes.JsonValue.Create(1));
        var original = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(1, 200_000).Select(i => $"line {i}\n")));
        var path = _temp.Bytes("big.log", original);
        var w = _app.CreateWindow(null);
        var tab = w.OpenFile(path, null);
        for (int i = 0; i < 400 && !tab.Paged!.Index.Complete; i++)
        {
            Thread.Sleep(25);
            Dispatcher.UIThread.RunJobs();
        }
        tab.Paged!.Focus();
        TestApp.Type(w, "ab");
        TestApp.Press(w, Avalonia.Input.Key.Enter);
        TestApp.Type(w, "é");

        var w2 = Crash();
        w2.RestoreFrom(_app.Journal);

        var restored = w2.Tabs.Single(t => t.Path == path);
        Assert.True(restored.Doc.IsLarge);
        Assert.True(restored.Doc.Doc.IsModified);
        Assert.Equal("ab\néline 1\n", Encoding.UTF8.GetString(restored.Doc.Doc.ReadBytes(new ByteRange(0, 12))));
        Assert.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(path))); // restoring never touches a user file

        Assert.True(await w2.SaveAsync(restored));
        byte[] expected = [.. "ab\né"u8, .. original];
        Assert.True(expected.AsSpan().SequenceEqual(File.ReadAllBytes(path)));
    }

    [AvaloniaFact]
    public async Task The_restore_bar_opens_a_list_of_what_came_back()
    {
        var w = _app.CreateWindow(null);
        var t = w.NewDocument();
        t.Editor!.Document.Text = "x";
        var w2 = Crash();
        w2.RestoreFrom(_app.Journal);
        _dialogs.Answers.Enqueue(0);
        var bar = Assert.Single(w2.Notices);
        var details = bar.Buttons.Single();
        details.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(50);
        var asked = Assert.Single(_dialogs.Asked);
        Assert.Equal("Obnova po pádu", asked.Title);
        Assert.Contains(asked.Details!, d => d.Contains("obnoveno včetně neuložených změn"));
    }

    [AvaloniaFact]
    public void A_clean_exit_restores_the_session_silently()
    {
        var w = _app.CreateWindow(null);
        var a = w.OpenFile(_temp.File("a.txt", "a"), null);
        var b = w.OpenFile(_temp.File("b.txt", "b"), null);
        w.SelectTab(a);
        _app.SaveSession();
        _app.Journal.Shutdown(); // clean exit writes the flag
        foreach (var win in _app.Windows.ToList()) win.Close();
        _app = TestApp.Prepare(_temp, _dialogs);
        var w2 = _app.CreateWindow(null);
        w2.RestoreFrom(_app.Journal);

        Assert.Equal(["a.txt", "b.txt"], w2.Tabs.Select(t => t.Title));
        Assert.Equal("a.txt", w2.Active!.Title);
        Assert.False(_app.Journal.CrashedLastTime);
        Assert.Empty(w2.Notices);
    }

    [AvaloniaFact]
    public void A_first_start_has_nothing_to_restore()
    {
        var w = _app.CreateWindow(null);
        w.RestoreFrom(_app.Journal);
        Assert.Empty(w.Tabs);
        Assert.Empty(w.Notices);
    }

    [AvaloniaFact]
    public void A_file_changed_during_the_downtime_is_not_overwritten_and_is_reported()
    {
        var w = _app.CreateWindow(null);
        var path = _temp.File("conflict.txt", "original");
        var tab = w.OpenFile(path, null);
        tab.Editor!.Document.Insert(8, " mine");
        _app.Journal.Flush();
        File.WriteAllText(path, "someone else was here");

        var w2 = Crash();
        w2.RestoreFrom(_app.Journal);

        Assert.DoesNotContain(w2.Tabs, t => t.Path == path);
        Assert.Equal("someone else was here", File.ReadAllText(path));
        var bar = Assert.Single(w2.Notices);
        Assert.Contains("nepodařilo obnovit", bar.Message);
        Assert.Contains(_app.Journal.Recovery.Documents, d => d.Status == NksHub.Textida.Core.Journal.RecoveryStatus.Conflict);
    }

    [AvaloniaFact]
    public async Task Keep_in_journal_brings_the_closed_tab_back_after_a_restart()
    {
        var w = _app.CreateWindow(null);
        var tab = w.NewDocument();
        tab.Editor!.Document.Text = "keep me around";
        _dialogs.Answers.Enqueue(2); // keep in journal
        Assert.True(await w.CloseTabAsync(tab));
        Assert.Empty(w.Tabs);

        var w2 = Crash();
        w2.RestoreFrom(_app.Journal);
        Assert.Equal("keep me around", Text(Assert.Single(w2.Tabs)));
    }

    [AvaloniaFact]
    public async Task Discard_forgets_the_work()
    {
        var w = _app.CreateWindow(null);
        var tab = w.NewDocument();
        tab.Editor!.Document.Text = "throw me away";
        _dialogs.Answers.Enqueue(1);
        Assert.True(await w.CloseTabAsync(tab));

        var w2 = Crash();
        w2.RestoreFrom(_app.Journal);
        Assert.Empty(w2.Tabs);
    }

    [AvaloniaFact]
    public void The_saved_file_is_not_restored_as_unsaved_work()
    {
        var w = _app.CreateWindow(null);
        var path = _temp.File("s.txt", "a");
        var tab = w.OpenFile(path, null);
        tab.Editor!.Document.Insert(1, "b");
        Assert.True(w.SaveAsync(tab).Result);

        var w2 = Crash();
        w2.RestoreFrom(_app.Journal);
        var restored = Assert.Single(w2.Tabs);
        Assert.Equal("ab", Text(restored));
        Assert.False(restored.Doc.Doc.IsModified);
    }

    // ---- where the journal lives ----

    [AvaloniaFact]
    public void An_unwritable_primary_location_is_reported_and_the_journal_uses_the_fallback()
    {
        var jam = _temp.File("jam.txt", "a file where a folder should be");
        var fallback = Path.Combine(_temp.Path, "fallback-journal");
        _app.Workbench.Set("files.journalDirectory", Path.Combine(jam, "journal"));
        _app.Workbench.Set("files.journalFallbackDirectory", fallback);

        _app = TestApp.Restart(_temp, _app, _dialogs);
        var w = _app.CreateWindow(null);
        w.ShowJournalWarnings();

        Assert.True(_app.Journal.IsActive);
        Assert.True(_app.Journal.Location.UsedFallback);
        Assert.Equal(fallback, _app.Journal.Directory);
        var notice = Assert.Single(w.Notices);
        Assert.Contains("není dostupná", notice.Message);
        Assert.Contains(fallback, notice.Message);
        var tab = w.NewDocument();
        tab.Editor!.Document.Text = "journaled in the fallback";
        _app.Journal.Flush();
        Assert.NotEmpty(Directory.GetFiles(fallback, "session-*.njl"));
    }

    [AvaloniaFact]
    public void When_no_location_works_the_app_says_recovery_is_off_and_keeps_the_text_in_the_tab()
    {
        var jam = _temp.File("jam.txt", "x");
        _app.Workbench.Set("files.journalDirectory", Path.Combine(jam, "journal"));
        _app.Workbench.Set("files.journalFallbackDirectory", Path.Combine(jam, "fallback"));

        _app = TestApp.Restart(_temp, _app, _dialogs);
        var w = _app.CreateWindow(null);
        w.ShowJournalWarnings();

        Assert.False(_app.Journal.IsActive);
        Assert.False(_app.Journal.Location.RecoveryAvailable);
        var notice = Assert.Single(w.Notices);
        Assert.Contains("Obnova po pádu je vypnutá", notice.Message);
        Assert.Contains("jen v otevřených kartách", notice.Message);
        var tab = w.NewDocument();
        tab.Editor!.Document.Text = "still only in the tab";
        Assert.Equal("still only in the tab", Text(tab));
        w.RestoreFrom(_app.Journal);
        Assert.Single(w.Tabs);
    }

    [AvaloniaFact]
    public void Closing_a_window_without_a_working_journal_asks_about_unsaved_work()
    {
        var jam = _temp.File("jam.txt", "x");
        _app.Workbench.Set("files.journalDirectory", Path.Combine(jam, "journal"));
        _app.Workbench.Set("files.journalFallbackDirectory", Path.Combine(jam, "fallback"));
        _app = TestApp.Restart(_temp, _app, _dialogs);
        var w = _app.CreateWindow(null);
        var tab = w.NewDocument();
        tab.Editor!.Document.Text = "unsaved";
        _dialogs.Answers.Enqueue(2); // cancel

        w.Close();

        Assert.Single(_dialogs.Asked);
        Assert.Contains(w, _app.Windows); // the window stays
        _dialogs.Answers.Enqueue(1); // discard
        w.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void With_a_working_journal_closing_the_window_does_not_ask_the_work_stays_in_the_journal()
    {
        var w = _app.CreateWindow(null);
        var tab = w.NewDocument();
        tab.Editor!.Document.Text = "hot exit";
        w.Close();
        Assert.Empty(_dialogs.Asked);
        Assert.DoesNotContain(w, _app.Windows);
    }

    [AvaloniaFact]
    public void Autosave_is_off_by_default_so_a_file_never_changes_without_a_save()
    {
        var path = _temp.File("auto.txt", "a");
        var w = _app.CreateWindow(null);
        var tab = w.OpenFile(path, null);
        tab.Editor!.Document.Insert(1, "b");
        _app.Journal.Flush();
        Assert.Equal("a", File.ReadAllText(path));
        Assert.Equal("off", _app.Workbench.Settings.GetString("files.autoSave"));
    }
}
