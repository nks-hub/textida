using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.History;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public class HistoryAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public HistoryAppTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static async Task Pump(int milliseconds)
    {
        for (var sw = Stopwatch.StartNew(); sw.ElapsedMilliseconds < milliseconds;)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private static async Task<bool> Eventually(Func<bool> condition)
    {
        for (var sw = Stopwatch.StartNew(); !condition() && sw.ElapsedMilliseconds < 10_000;) await Pump(20);
        return condition();
    }

    private const string LargeText = "a line of the large file\n";

    /// <summary>A file of 1.25 MB in the paged backend, indexed and focused, with the history set to keep files above 100 000 bytes as patches.</summary>
    private async Task<(DocTab Tab, string Path)> OpenLarge(HistoryPolicy? policy = null)
    {
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1)); // the threshold is in MiB
        _app.History.Policy = policy ?? new HistoryPolicy { PatchAboveBytes = 100_000 };
        string path = _temp.File("large.txt", string.Concat(Enumerable.Repeat(LargeText, 50_000)));
        var tab = _w.OpenFile(path, null);
        Assert.True(tab.Doc.IsLarge);
        Assert.True(await Eventually(() => tab.Paged!.Index.Complete));
        tab.Paged!.Focus();
        Dispatcher.UIThread.RunJobs();
        return (tab, path);
    }

    [AvaloniaFact]
    public async Task A_small_file_gets_its_first_revision_when_it_is_opened()
    {
        string small = _temp.File("small.txt", "one\n");
        _w.OpenFile(small, null);
        Assert.True(await Eventually(() => _app.History.List(small).Count > 0));
        Assert.Equal(HistorySource.Initial, Assert.Single(_app.History.List(small)).Source);
    }

    [AvaloniaFact]
    public async Task A_file_of_the_paged_backend_is_copied_at_its_first_edit_and_saved_as_patches()
    {
        var (tab, large) = await OpenLarge();
        await Pump(300);
        Assert.Empty(_app.History.List(large)); // a file that is only read is not copied

        TestApp.Type(_w, "x");
        Assert.True(await Eventually(() => _app.History.List(large).Count == 1));
        var initial = Assert.Single(_app.History.List(large));
        Assert.Equal(HistorySource.Initial, initial.Source);
        Assert.False(initial.IsPatch);
        Assert.Equal(LargeText.Length * 50_000L, initial.Size); // the file as it was opened, not with the typed character

        Assert.True(await _w.SaveAsync(tab));
        Assert.True(await Eventually(() => _app.History.List(large).Count == 2));
        var saved = _app.History.List(large)[0];
        Assert.True(saved.IsPatch);
        Assert.Equal(initial.Id, saved.BaseId);
        Assert.True(saved.PatchBytes < 10_000, $"{saved.PatchBytes} bytes for one typed character in a megabyte");
        Assert.True(File.ReadAllBytes(large).AsSpan().SequenceEqual(_app.History.Read(large, saved.Id)));
    }

    [AvaloniaFact]
    public async Task A_file_above_the_size_limit_gets_no_revision_and_the_status_bar_says_so()
    {
        var (_, large) = await OpenLarge(new HistoryPolicy { MaxRevisionBytes = 100_000 });
        await Pump(500);
        Assert.Empty(_app.History.List(large));
        Assert.Equal(Loc.Get(K.HistoryTooLarge, ("size", OpenDocument.FormatSize(100_000))), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public async Task The_actions_for_a_revision_of_a_large_file_compare_in_a_tool_and_save_a_copy_but_do_not_restore_in_place()
    {
        var (tab, large) = await OpenLarge();
        byte[] original = File.ReadAllBytes(large);
        TestApp.Type(_w, "x");
        Assert.True(await Eventually(() => _app.History.List(large).Count == 1));
        Assert.True(await _w.SaveAsync(tab));
        Assert.True(await Eventually(() => _app.History.List(large).Count == 2));
        var revisions = _app.History.List(large);
        var dialogs = (ScriptedDialogs)_app.Dialogs;

        dialogs.Answers.Enqueue(0); // Compare, with no external diff tool set
        await _w.HistoryActionsAsync(tab, large, revisions[0]);
        Assert.Equal([Loc.Get(K.HistoryCompare), Loc.Get(K.HistorySaveCopy), Loc.Get(K.HistorySetLabel), Loc.Get(K.HistoryPin), Loc.Get(K.DialogCancel)],
            dialogs.Asked[0].Buttons.Select(b => b.Label));
        Assert.Equal(Loc.Get(K.HistoryCompareNeedsTool), _w.StatusMessageText);
        Assert.Null(_w.ActiveCompare);

        dialogs.Answers.Enqueue(1); // Save as a Copy of the first revision
        await _w.HistoryActionsAsync(tab, large, revisions[1]);
        var copy = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(large)!, "large (*).txt"));
        Assert.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(copy)));
        Assert.Equal(copy, _w.Active!.Path);
        Assert.Contains(Path.GetFileName(copy), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public async Task The_actions_for_a_revision_of_a_text_tab_still_restore_and_also_save_a_copy()
    {
        string path = _temp.File("notes.txt", "first\n");
        var tab = _w.OpenFile(path, null);
        Assert.True(await Eventually(() => _app.History.List(path).Count == 1));
        var dialogs = (ScriptedDialogs)_app.Dialogs;

        dialogs.Answers.Enqueue(6); // Cancel
        await _w.HistoryActionsAsync(tab, path, _app.History.List(path)[0]);

        Assert.Equal([Loc.Get(K.HistoryCompare), Loc.Get(K.HistoryRestoreFile), Loc.Get(K.HistoryRestoreSection), Loc.Get(K.HistorySaveCopy), Loc.Get(K.HistorySetLabel), Loc.Get(K.HistoryPin), Loc.Get(K.DialogCancel)],
            dialogs.Asked[0].Buttons.Select(b => b.Label));
    }

    [AvaloniaFact]
    public async Task Mark_revision_of_a_large_file_takes_the_saved_file_and_wants_unsaved_edits_saved_first()
    {
        var (tab, large) = await OpenLarge();
        var dialogs = (ScriptedDialogs)_app.Dialogs;
        TestApp.Type(_w, "x");

        dialogs.Prompts.Enqueue("Before the release");
        _w.MarkRevision();
        Assert.True(await Eventually(() => _w.StatusMessageText == Loc.Get(K.HistoryMarkSaveFirst)));

        Assert.True(await _w.SaveAsync(tab));
        Assert.True(await Eventually(() => _app.History.List(large).Count >= 2));
        dialogs.Prompts.Enqueue("Release 2");
        _w.MarkRevision();
        Assert.True(await Eventually(() => _app.History.List(large).Any(r => r.Label == "Release 2")));
    }

    [AvaloniaFact]
    public async Task Without_an_external_tool_a_revision_opens_in_the_built_in_compare_tab()
    {
        string path = _temp.File("notes.txt", "first\n");
        var tab = _w.OpenFile(path, null);
        Assert.True(await Eventually(() => _app.History.List(path).Count > 0));
        var revision = Assert.Single(_app.History.List(path));
        tab.Editor!.Document.Insert(0, "changed ");

        _w.CompareWithRevision(tab, path, revision);
        Assert.NotNull(_w.ActiveCompare);
    }
}
