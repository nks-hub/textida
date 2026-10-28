using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P9.5.2: a named session over a file too large to keep as text. The unsaved work of such a tab is kept as the edits of its file; the session is saved, the app is started
/// again on the same profile and the session is opened, and what comes back has to be the document that was there, with the file on disk as it was.
/// </summary>
[Collection("LocState")]
public class SessionLargeFileTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private App _app;
    private MainWindow _w;

    public SessionLargeFileTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static async Task WaitFor(Func<bool> condition, string what, int milliseconds = 30_000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < milliseconds, "timed out waiting for " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }

    private void Restart()
    {
        _app = TestApp.Restart(_temp, _app, _dialogs);
        _w = _app.CreateWindow(null);
    }

    private static string LogText(int lines) => string.Concat(Enumerable.Range(0, lines).Select(i => $"2026-10-27 12:00:00 INFO request number {i} handled ěščř\n"));

    /// <summary>Index into the text of the character at the one-based <paramref name="line"/> and <paramref name="column"/>.</summary>
    private static int IndexOf(string text, int line, int column)
    {
        int at = 0;
        for (int i = 1; i < line; i++) at = text.IndexOf('\n', at) + 1;
        return at + column - 1;
    }

    private static string ReadAll(DocTab tab) => System.Text.Encoding.UTF8.GetString(tab.Doc.Doc.ReadBytes(new ByteRange(0, tab.Doc.Doc.ByteLength)));

    private async Task<DocTab> OpenLargeAsync(string path)
    {
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        var tab = _w.OpenFile(path, null);
        Assert.True(tab.Doc.IsLarge);
        await WaitFor(() => tab.Paged!.Index.Complete, "the line index");
        tab.Paged!.Focus();
        return tab;
    }

    [AvaloniaFact]
    public async Task A_named_session_brings_back_the_unsaved_edits_and_the_caret_of_a_large_file_after_a_restart_and_the_file_on_disk_is_not_touched()
    {
        string original = LogText(40_000);
        string path = _temp.File("big.log", original);
        var tab = await OpenLargeAsync(path);

        // three edits far apart, none of them moves a line: a typed word with Czech letters, the Delete key, a typed word
        Assert.True(tab.GoToLine(100, 1));
        TestApp.Type(_w, "první ");
        Assert.True(tab.GoToLine(30_000, 5));
        TestApp.Press(_w, Key.Delete);
        Assert.True(tab.GoToLine(20_000, 12));
        TestApp.Type(_w, "EDITED ");
        string expected = original.Remove(IndexOf(original, 30_000, 5), 1).Insert(IndexOf(original, 20_000, 12), "EDITED ").Insert(IndexOf(original, 100, 1), "první ");
        Assert.Equal(expected, ReadAll(tab));
        Assert.True(tab.Doc.HasUnsavedWork);
        Assert.Equal((20_000L, 19L), tab.CaretPosition());

        Assert.True(_w.SaveNamedSession("big"));
        Assert.Equal(original, File.ReadAllText(path));
        _w.CloseTab(tab);

        Restart();
        var session = _app.Workspace.Sessions().Single();
        Assert.Empty(_w.OpenNamedSession(session));
        var restored = _w.Tabs.Single();

        Assert.True(restored.Doc.IsLarge);
        Assert.True(restored.Doc.HasUnsavedWork);
        Assert.Equal(expected, ReadAll(restored));
        Assert.Equal(original, File.ReadAllText(path));
        await WaitFor(() => restored.Paged!.Index.Complete, "the line index");
        Assert.Equal((20_000L, 19L), restored.CaretPosition());

        // what came back is a normal unsaved document: it saves, and one undo takes the whole restore back
        Assert.Equal(1, restored.Doc.Doc.UndoDepth);
        Assert.True(await _w.SaveAsync(restored));
        Assert.Equal(expected, File.ReadAllText(path));
    }

    [AvaloniaFact]
    public async Task A_named_session_does_not_put_unsaved_edits_on_a_large_file_that_changed_on_disk_and_says_so()
    {
        string path = _temp.File("big.log", LogText(40_000));
        var tab = await OpenLargeAsync(path);
        Assert.True(tab.GoToLine(500, 1));
        TestApp.Type(_w, "mine ");
        Assert.True(_w.SaveNamedSession("big"));
        _w.CloseTab(tab);

        string changed = LogText(40_000).Replace("number 7 ", "number 7 CHANGED ", StringComparison.Ordinal);
        File.WriteAllText(path, changed);
        var missing = _w.OpenNamedSession(_app.Workspace.Sessions().Single());

        var restored = _w.Tabs.Single();
        Assert.Equal(changed, ReadAll(restored));
        Assert.False(restored.Doc.HasUnsavedWork);
        Assert.Empty(missing);
        Assert.Contains(_w.Notices, n => n.Message == Loc.Get(K.SessionEditsNotRestored, ("name", "big"), ("files", "big.log")));
    }

    [AvaloniaFact]
    public async Task A_named_session_opened_while_the_file_is_open_with_changes_of_its_own_does_not_apply_its_edits_a_second_time()
    {
        string path = _temp.File("big.log", LogText(40_000));
        var tab = await OpenLargeAsync(path);
        Assert.True(tab.GoToLine(100, 1));
        TestApp.Type(_w, "mine ");
        Assert.True(_w.SaveNamedSession("big"));
        string before = ReadAll(tab);

        Assert.Empty(_w.OpenNamedSession(_app.Workspace.Sessions().Single()));

        Assert.Equal(before, ReadAll(tab));
        Assert.Equal(1, tab.Doc.Doc.UndoDepth);
        Assert.Contains(_w.Notices, n => n.Message == Loc.Get(K.SessionEditsNotRestored, ("name", "big"), ("files", "big.log")));
    }

    [AvaloniaFact]
    public async Task A_named_session_after_an_in_place_save_keeps_exactly_what_is_not_in_the_file_yet()
    {
        string original = LogText(40_000);
        string path = _temp.File("big.log", original);
        var tab = await OpenLargeAsync(path);
        var doc = tab.Doc.Doc;
        Assert.True(doc.TryGetOffset(new LinePosition(299, 0), out long at));

        // four bytes over four bytes: the save writes them into the file where they are
        Assert.True(doc.Replace(new ByteRange(at, at + 4), "ABCD"u8.ToArray()).Applied);
        Assert.Equal(SaveMode.InPlace, doc.PlanSave().Mode);
        Assert.True(await _w.SaveAsync(tab));
        string written = original.Remove(IndexOf(original, 300, 1), 4).Insert(IndexOf(original, 300, 1), "ABCD");
        Assert.Equal(written, File.ReadAllText(path));

        // an edit that changes the length, taken back: the document is what the file is, nothing is unsaved
        Assert.True(doc.Insert(0, "NEW "u8.ToArray()).Applied);
        Assert.NotNull(doc.Undo());
        Assert.False(doc.IsModified);
        Assert.True(_w.SaveNamedSession("as-saved"));

        // the saved edit taken back too: the file has ABCD, the document has what was there before
        Assert.NotNull(doc.Undo());
        Assert.Equal(original, ReadAll(tab));
        Assert.True(doc.IsModified);
        Assert.True(_w.SaveNamedSession("undone"));
        _w.CloseTab(tab);

        Assert.Empty(_w.OpenNamedSession(_app.Workspace.FindSession("as-saved")!));
        var first = _w.Tabs.Single();
        Assert.Equal(written, ReadAll(first));
        Assert.False(first.Doc.HasUnsavedWork);
        _w.CloseTab(first);

        Assert.Empty(_w.OpenNamedSession(_app.Workspace.FindSession("undone")!));
        var second = _w.Tabs.Single();
        Assert.Equal(original, ReadAll(second));
        Assert.True(second.Doc.HasUnsavedWork);
        Assert.Equal(written, File.ReadAllText(path));
    }

    [AvaloniaFact]
    public async Task A_named_session_says_which_large_file_could_not_keep_its_unsaved_changes()
    {
        string path = _temp.File("big.log", LogText(40_000));
        var tab = await OpenLargeAsync(path);
        Assert.True(tab.Doc.Doc.Insert(0, Enumerable.Repeat((byte)'x', PluginLimitsPlaceholders.SessionLargeEditsMaxBytes + 1).ToArray()).Applied);

        Assert.True(_w.SaveNamedSession("big"));

        Assert.Contains(_w.Notices, n => n.Message == Loc.Get(K.SessionLargeLeftOut, ("name", "big"), ("files", "big.log")));
        var kept = Assert.Single(Assert.Single(_app.Workspace.Sessions().Single().Groups).Tabs);
        Assert.Null(kept.Edits);
        Assert.Equal(path, kept.Path);
    }
}
