using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.App.Tests;

/// <summary>New, open, save, save as, close, reload, external changes, compare: the file life of a document.</summary>
[Collection("LocState")]
public class FileFlowTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    static FileFlowTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public FileFlowTests()
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

    private static string Text(DocTab t) => t.Editor!.Document.Text;

    private DocTab NewWith(string text)
    {
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = text;
        return tab;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Button BarButton(DocTab tab, string label) => ((InfoBar)tab.Bar!).Buttons.Single(b => (string?)b.Content == label);

    // ---- save and save as ----

    [AvaloniaFact]
    public async Task Save_of_a_new_document_asks_for_path_and_encoding_and_writes_exact_bytes()
    {
        var tab = NewWith("ahoj čau");
        var path = Path.Combine(_temp.Path, "new.html");
        _dialogs.SaveAs.Enqueue(new SaveAsChoice(path, TextCodec.Cp1250));

        Assert.True(await _w.SaveAsync(tab));

        Assert.Equal(Encoding.GetEncoding(1250).GetBytes("ahoj čau"), File.ReadAllBytes(path));
        var asked = Assert.Single(_dialogs.SaveAsAsked);
        Assert.Contains(asked.Targets, t => t.Codec == TextCodec.Utf8 && t.Lost == 0);
        Assert.Contains(asked.Targets, t => t.Codec == TextCodec.Cp1250 && t.Lost == 0);
        Assert.Contains(asked.Targets, t => t.Codec == TextCodec.Cp1252 && t.Lost > 0);
        Assert.Equal(path, tab.Path);
        Assert.Equal("new.html", tab.Title);
        Assert.False(tab.Doc.Doc.IsModified);
        Assert.Equal("html", tab.Doc.Language.Id);
        Assert.Equal(TextCodec.Cp1250, tab.Doc.Doc.Codec);
    }

    [AvaloniaFact]
    public async Task Cancelled_save_as_writes_nothing()
    {
        var tab = NewWith("x");
        _dialogs.SaveAs.Enqueue(null);
        Assert.False(await _w.SaveAsync(tab));
        Assert.Null(tab.Path);
        Assert.True(tab.Doc.Doc.IsModified);
    }

    [AvaloniaFact]
    public async Task Save_as_to_an_encoding_that_loses_characters_shows_the_loss_dialog()
    {
        var tab = NewWith("ř č 😀");
        var path = Path.Combine(_temp.Path, "loss.txt");
        _dialogs.SaveAs.Enqueue(new SaveAsChoice(path, TextCodec.Cp1252));
        _dialogs.Answers.Enqueue(1); // cancel in the loss dialog

        Assert.False(await _w.SaveAsync(tab));

        Assert.False(File.Exists(path));
        var loss = Assert.Single(_dialogs.Asked);
        Assert.Equal(Loc.Get(K.LossTitle), loss.Title);
        Assert.NotNull(loss.Details);
        Assert.Contains(loss.Details!, d => d.Contains('ř') && d.Contains("řádek 1") && d.Contains("bajt"));
        Assert.Contains(loss.Details!, d => d.Contains('č'));
        Assert.Contains(loss.Details!, d => d.Contains("😀"));
        Assert.Equal(Loc.Get(K.LossSaveReplaced), loss.Buttons[0].Label);

        _dialogs.SaveAs.Enqueue(new SaveAsChoice(path, TextCodec.Cp1252));
        _dialogs.Answers.Enqueue(0); // explicit replacement
        Assert.True(await _w.SaveAsync(tab));
        Assert.Equal(Encoding.ASCII.GetBytes("? ? ?"), File.ReadAllBytes(path));
    }

    [AvaloniaFact]
    public async Task Typed_character_the_code_page_cannot_hold_is_confirmed_before_saving()
    {
        var path = _temp.Bytes("cp1252.txt", [0x61, 0x62]);
        var tab = _w.OpenFile(path, null);
        tab.Doc.Doc.ReinterpretAs(TextCodec.Cp1252);
        tab.Editor!.Document.Insert(1, "č");
        _dialogs.Answers.Enqueue(1);

        Assert.False(await _w.SaveAsync(tab));
        Assert.Equal(new byte[] { 0x61, 0x62 }, File.ReadAllBytes(path));
        Assert.Contains("č", _dialogs.Asked[0].Details![0]);

        _dialogs.Answers.Enqueue(0);
        Assert.True(await _w.SaveAsync(tab));
        Assert.Equal(new byte[] { 0x61, 0x3F, 0x62 }, File.ReadAllBytes(path));
    }

    [AvaloniaFact]
    public async Task Save_failure_keeps_the_content_in_the_tab_and_says_why()
    {
        var tab = NewWith("keep me");
        _dialogs.SaveAs.Enqueue(new SaveAsChoice(Path.Combine(_temp.Path, "missing-folder", "f.txt"), TextCodec.Utf8));
        Assert.False(await _w.SaveAsync(tab));
        Assert.True(tab.Doc.HasUnsavedWork);
        Assert.Equal("keep me", Text(tab));
        Assert.Contains("Uložení se nepovedlo", _w.StatusLeftText);
    }

    [AvaloniaFact]
    public async Task Save_makes_a_backup_of_the_file_it_replaces()
    {
        var path = _temp.File("b.txt", "old");
        var tab = _w.OpenFile(path, null);
        tab.Editor!.Document.Insert(3, "er");
        Assert.True(await _w.SaveAsync(tab));
        Assert.Equal("older", File.ReadAllText(path));
        var backups = Directory.GetFiles(Path.Combine(_app.ProfileDirectory, "backups"), "*.bak", SearchOption.AllDirectories);
        Assert.Equal("old", File.ReadAllText(Assert.Single(backups)));
    }

    [AvaloniaFact]
    public async Task Save_all_saves_every_named_modified_document()
    {
        var a = _w.OpenFile(_temp.File("a.txt", "a"), null);
        var b = _w.OpenFile(_temp.File("b.txt", "b"), null);
        a.Editor!.Document.Insert(1, "1");
        b.Editor!.Document.Insert(1, "2");
        Assert.True(await _w.SaveAllAsync());
        Assert.Equal("a1", File.ReadAllText(a.Path!));
        Assert.Equal("b2", File.ReadAllText(b.Path!));
    }

    // ---- close ----

    [AvaloniaFact]
    public async Task Closing_a_modified_tab_offers_save_discard_keep_in_journal_or_cancel()
    {
        var path = _temp.File("c.txt", "c");
        var tab = _w.OpenFile(path, null);
        tab.Editor!.Document.Insert(1, "!");

        _dialogs.Answers.Enqueue(3); // cancel
        Assert.False(await _w.CloseTabAsync(tab));
        Assert.Single(_w.Tabs);
        var asked = _dialogs.Asked[0];
        Assert.Equal(["Uložit", "Neukládat", "Nechat v žurnálu", "Zrušit"], asked.Buttons.Select(b => b.Label));

        _dialogs.Answers.Enqueue(0); // save
        Assert.True(await _w.CloseTabAsync(tab));
        Assert.Empty(_w.Tabs);
        Assert.Equal("c!", File.ReadAllText(path));
    }

    [AvaloniaFact]
    public async Task Discarding_closes_without_writing()
    {
        var path = _temp.File("d.txt", "d");
        var tab = _w.OpenFile(path, null);
        tab.Editor!.Document.Insert(1, "!");
        _dialogs.Answers.Enqueue(1);
        Assert.True(await _w.CloseTabAsync(tab));
        Assert.Equal("d", File.ReadAllText(path));
        Assert.Empty(_w.Tabs);
    }

    [AvaloniaFact]
    public async Task A_new_untouched_document_closes_without_a_question()
    {
        var tab = _w.NewDocument();
        Assert.True(await _w.CloseTabAsync(tab));
        Assert.Empty(_dialogs.Asked);
    }

    [AvaloniaFact]
    public async Task Without_a_working_journal_the_keep_in_journal_choice_is_not_offered()
    {
        var jam = _temp.File("jam.txt", "x");
        _app.Workbench.Set("files.journalDirectory", Path.Combine(jam, "journal"));
        _app.Workbench.Set("files.journalFallbackDirectory", Path.Combine(jam, "fallback"));
        var app = TestApp.Restart(_temp, _app, _dialogs);
        var w = app.CreateWindow(null);
        var tab = w.NewDocument();
        tab.Editor!.Document.Text = "unsaved";
        Assert.False(app.Journal.IsActive);
        _dialogs.Answers.Enqueue(2);
        Assert.False(await w.CloseTabAsync(tab));
        Assert.Equal(["Uložit", "Neukládat", "Zrušit"], _dialogs.Asked[^1].Buttons.Select(b => b.Label));
        w.CloseTab(tab);
    }

    // ---- reload ----

    [AvaloniaFact]
    public async Task Reload_takes_the_text_from_disk()
    {
        var path = _temp.File("r.txt", "one");
        var tab = _w.OpenFile(path, null);
        File.WriteAllText(path, "two");
        Assert.True(await _w.ReloadAsync(tab));
        Assert.Equal("two", Text(tab));
        Assert.False(tab.Doc.Doc.IsModified);
        Assert.Empty(_dialogs.Asked);
    }

    [AvaloniaFact]
    public async Task Reload_of_a_modified_tab_asks_first_and_cancel_keeps_the_changes()
    {
        var path = _temp.File("r2.txt", "one");
        var tab = _w.OpenFile(path, null);
        tab.Editor!.Document.Insert(3, "!");
        File.WriteAllText(path, "disk");

        _dialogs.Answers.Enqueue(1);
        Assert.False(await _w.ReloadAsync(tab));
        Assert.Equal("one!", Text(tab));

        _dialogs.Answers.Enqueue(0);
        Assert.True(await _w.ReloadAsync(tab));
        Assert.Equal("disk", Text(tab));
    }

    // ---- external change ----

    [AvaloniaFact]
    public void External_change_shows_a_non_modal_bar_with_reload_keep_and_compare()
    {
        var path = _temp.File("e.txt", "mine");
        var tab = _w.OpenFile(path, null);
        File.WriteAllText(path, "theirs");
        tab.Doc.Doc.CheckExternalChange();
        Dispatcher.UIThread.RunJobs();

        var bar = Assert.IsType<InfoBar>(tab.Bar);
        Assert.Equal(["Znovu načíst", "Ponechat moje", "Porovnat"], bar.Buttons.Select(b => (string?)b.Content));
        Assert.Equal("mine", Text(tab)); // never replaced silently
        Click(BarButton(tab, "Znovu načíst"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("theirs", Text(tab));
        Assert.Null(tab.Bar);
    }

    [AvaloniaFact]
    public async Task Keep_mine_leaves_the_tab_and_the_next_save_ends_in_a_conflict_not_an_overwrite()
    {
        var path = _temp.File("k.txt", "mine");
        var tab = _w.OpenFile(path, null);
        tab.Editor!.Document.Insert(4, "!");
        File.WriteAllText(path, "theirs");
        tab.Doc.Doc.CheckExternalChange();
        Dispatcher.UIThread.RunJobs();
        Click(BarButton(tab, "Ponechat moje"));
        Assert.Null(tab.Bar);
        Assert.Equal("mine!", Text(tab));

        _dialogs.Answers.Enqueue(2); // cancel the conflict dialog
        Assert.False(await _w.SaveAsync(tab));
        Assert.Equal("theirs", File.ReadAllText(path));
        Assert.Equal(Loc.Get(K.ConflictTitle), _dialogs.Asked[^1].Title);
        Assert.Equal(["Přepsat soubor", "Porovnat", "Zrušit"], _dialogs.Asked[^1].Buttons.Select(b => b.Label));

        _dialogs.Answers.Enqueue(0); // explicit overwrite
        Assert.True(await _w.SaveAsync(tab));
        Assert.Equal("mine!", File.ReadAllText(path));
    }

    [AvaloniaFact]
    public async Task The_file_watcher_raises_the_bar_without_any_call_from_the_test()
    {
        var path = _temp.File("w.txt", "mine");
        var tab = _w.OpenFile(path, null);
        await Task.Delay(300);
        File.WriteAllText(path, "changed by another program");
        for (int i = 0; i < 80 && tab.Bar is null; i++)
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.IsType<InfoBar>(tab.Bar);
    }

    [AvaloniaFact]
    public void A_deleted_file_offers_keep_or_close()
    {
        var path = _temp.File("gone.txt", "x");
        var tab = _w.OpenFile(path, null);
        File.Delete(path);
        tab.Doc.Doc.CheckExternalChange();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Ponechat", "Zavřít kartu"], ((InfoBar)tab.Bar!).Buttons.Select(b => (string?)b.Content));
    }

    // ---- compare ----

    [AvaloniaFact]
    public void Compare_without_a_tool_says_what_is_missing()
    {
        var tab = _w.OpenFile(_temp.File("cmp.txt", "x"), null);
        Assert.Null(_w.CompareWithDisk(tab));
        var notice = Assert.Single(_w.Notices);
        Assert.Contains("tools.diffTool", notice.Message);
    }

    [AvaloniaFact]
    public void Compare_of_an_unnamed_tab_says_there_is_no_file()
    {
        var tab = NewWith("x");
        Assert.Null(_w.CompareWithDisk(tab));
        Assert.Contains("není s čím porovnávat", _w.StatusLeftText);
    }

    [AvaloniaFact]
    public async Task Compare_hands_two_files_to_the_tool_and_deletes_the_temporary_one_afterwards()
    {
        var path = _temp.File("cmp2.txt", "on disk");
        var tab = _w.OpenFile(path, null);
        tab.Editor!.Document.Insert(7, " + unsaved");
        var seen = Path.Combine(_temp.Path, "seen-right.txt");
        string tool, arguments;
        if (OperatingSystem.IsWindows())
        {
            tool = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            arguments = $"/c copy /y \"{{right}}\" \"{seen}\" >nul & copy /y \"{{left}}\" \"{seen}.left\" >nul";
        }
        else
        {
            tool = "/bin/sh";
            arguments = $"-c 'cp \"{{right}}\" \"{seen}\"; cp \"{{left}}\" \"{seen}.left\"'";
        }
        _app.Workbench.Set("tools.diffTool", tool);
        _app.Workbench.Set("tools.diffArguments", arguments);

        var started = _w.CompareWithDisk(tab);

        Assert.NotNull(started);
        Assert.True(started!.Ok, started.Problem);
        await started.Completion.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal("on disk + unsaved", File.ReadAllText(seen));
        Assert.Equal("on disk", File.ReadAllText(seen + ".left"));
        Assert.False(File.Exists(started.TempFile!));
        Assert.False(Directory.Exists(Path.GetDirectoryName(started.TempFile!)!));
    }

    [AvaloniaFact]
    public void Compare_reports_a_tool_that_does_not_start()
    {
        var tab = _w.OpenFile(_temp.File("cmp3.txt", "x"), null);
        _app.Workbench.Set("tools.diffTool", Path.Combine(_temp.Path, "no-such-tool.exe"));
        var started = _w.CompareWithDisk(tab);
        Assert.False(started!.Ok);
        Assert.Contains("Porovnávač se nespustil", _w.StatusLeftText);
    }

    // ---- open ----

    [AvaloniaFact]
    public async Task Open_dialog_files_open_in_tabs_and_a_second_open_of_the_same_file_selects_it()
    {
        var a = _temp.File("o1.txt", "1");
        var b = _temp.File("o2.txt", "2");
        _dialogs.Picked.AddRange([a, b]);
        await _w.OpenWithDialogAsync();
        Assert.Equal(2, _w.Tabs.Count());
        _w.OpenFile(a, null);
        Assert.Equal(2, _w.Tabs.Count());
        Assert.Equal(a, _w.Active!.Path);
    }

    [AvaloniaFact]
    public void Unreadable_file_reports_an_error_with_the_path()
    {
        var ex = Assert.Throws<IOException>(() => _w.OpenFile(Path.Combine(_temp.Path, "nope.txt"), null));
        Assert.Contains("nope.txt", ex.Message);
    }

    [AvaloniaFact]
    public void File_above_the_switch_size_opens_paged_and_editable_with_a_note_and_a_badge()
    {
        _app.Workbench.Set("files.largeFileThreshold", System.Text.Json.Nodes.JsonValue.Create(1));
        var lines = new StringBuilder();
        for (int i = 1; i <= 150_000; i++) lines.Append("line ").Append(i).Append('\n');
        var path = _temp.Bytes("huge.log", Encoding.ASCII.GetBytes(lines.ToString()));

        var tab = _w.OpenFile(path, null);

        Assert.True(tab.Doc.IsLarge);
        Assert.Null(tab.Editor);
        Assert.NotNull(tab.Paged);
        Assert.Contains("stránkovaný režim", _w.StatusLeftText);
        Assert.Contains("MiB", tab.Doc.SizeBadge);
        Assert.False(tab.Doc.Doc.IsModified);
        // the text commands of the AvaloniaEdit surface stay off, undo and redo work on every document
        var ctx = new ShellContext(_w, forMenu: true);
        Assert.False(_app.Workbench.Commands.TryGet("nks.edit.deleteLine", out var cmd) && cmd.IsEnabled(ctx));
        Assert.True(_app.Workbench.Commands.TryGet("nks.edit.undo", out var undo) && undo.IsEnabled(ctx));
        tab.GoToLine(100_000);
        Assert.True(tab.CaretPosition().Line >= 1);
    }

    [AvaloniaFact]
    public void Large_file_in_an_encoding_the_paged_backend_cannot_hold_says_so()
    {
        _app.Workbench.Set("files.largeFileThreshold", System.Text.Json.Nodes.JsonValue.Create(1));
        var content = Encoding.Unicode.GetBytes(new string('a', 700_000));
        var path = _temp.Bytes("wide.txt", [.. Encoding.Unicode.GetPreamble(), .. content]);
        var ex = Assert.Throws<IOException>(() => _w.OpenFile(path, null));
        Assert.Contains("UTF-16", ex.Message);
    }
}
