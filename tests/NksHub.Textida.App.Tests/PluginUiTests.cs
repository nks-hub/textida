using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P3b.2.2 and P3b.5.3 in the app: a real test plugin process sends UI data through the real host and the window draws it; the built-in
/// JSON formatter runs as its own process against an open document.
/// </summary>
[Collection("LocState")]
public class PluginUiTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    private readonly TimeSpan _tabTimeout = PluginLimitsPlaceholders.TabTimeout;

    public PluginUiTests()
    {
        // a real plugin process on a busy machine can miss 500 ms; the timeout path has its own test in DocumentApiTests.EarlyTools
        PluginLimitsPlaceholders.TabTimeout = TimeSpan.FromSeconds(10);
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        PluginLimitsPlaceholders.TabTimeout = _tabTimeout;
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    // The test thread is the UI thread, so everything the plugin sends waits for RunJobs; these helpers keep pumping until their condition holds.
    private static async Task Until(Func<bool> condition, string what)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < 30_000, "Not reached: " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private static async Task<T> Pump<T>(Task<T> task)
    {
        await Until(() => task.IsCompleted, "a call");
        return await task;
    }

    private PluginHost.PluginHost Host => _app.Plugins.HostForTests;

    private async Task StartPluginAsync()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.PluginHost.TestPlugin.dll");
        Assert.True(File.Exists(dll), dll);
        Host.Register(new PluginSpec("p", "Test plugin", Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", [dll], ["onCommand:p"]));
        await Pump(Host.ActivateAsync("onCommand:p"));
    }

    private async Task<JsonElement> Editor(string method, string paramsJson)
    {
        var json = $"{{\"method\":\"{method}\",\"params\":{paramsJson}}}";
        return (await Host.CallAsync("p", "callEditor", Encoding.UTF8.GetBytes(json))).Clone();
    }

    private async Task<string> EventsAsync() => (await Host.CallAsync("p", "events")).GetRawText();

    private async Task WaitForEventAsync(string name)
    {
        var sw = Stopwatch.StartNew();
        while (!(await Pump(EventsAsync())).Contains(name))
        {
            Assert.True(sw.ElapsedMilliseconds < 30_000, "Not reached: the event " + name);
            await Task.Delay(20);
        }
    }

    [AvaloniaFact]
    public async Task A_panel_sent_as_data_is_drawn_as_a_tree_and_an_opened_item_reaches_the_plugin()
    {
        await StartPluginAsync();
        await Pump(Editor(UiMethods.PanelSet, "{\"panel\":\"outline\",\"title\":\"Outline\",\"kind\":\"tree\",\"reveal\":true,\"items\":[{\"id\":\"a\",\"label\":\"root\",\"detail\":\"object\",\"children\":[{\"id\":\"b\",\"label\":\"child\"}]}]}"));
        await Until(() => _w.PluginPanelOpen, "the revealed panel");

        Assert.Equal("Outline", _w.PluginPanelTitle);
        var root = Assert.IsType<TreeViewItem>(Assert.Single(_w.PluginTree.Items));
        var child = Assert.IsType<TreeViewItem>(Assert.Single(root.Items));
        Assert.Equal(("a", "b"), (root.Tag, child.Tag));
        Assert.False(_w.DocumentsPanelOpen || _w.FilePanelOpen);

        _w.PluginTree.SelectedItem = child;
        _w.ActivatePluginItem();
        await WaitForEventAsync(UiEvents.PanelActivated);

        await Pump(Editor(UiMethods.PanelRemove, "{\"panel\":\"outline\"}"));
        await Until(() => !_w.PluginPanelOpen, "the panel closing after it was removed");
        Assert.Empty(_w.PluginTree.Items);
    }

    [AvaloniaFact]
    public async Task A_plugin_has_one_status_item_and_a_task_with_progress_and_a_cancel_button()
    {
        await StartPluginAsync();
        await Pump(Editor(UiMethods.StatusSet, "{\"text\":\"JSON ok\",\"tooltip\":\"valid\"}"));
        await Pump(Editor(UiMethods.StatusSet, "{\"text\":\"JSON error\"}"));
        await Until(() => _w.PluginStatusHost is Panel { Children.Count: 1 } p && p.Children[0] is TextBlock { Text: "JSON error" }, "one status item");

        var id = (await Pump(Editor(UiMethods.TaskStart, "{\"title\":\"Indexing\",\"cancellable\":true}"))).GetProperty("task").GetString()!;
        await Until(() => ((Panel)_w.PluginStatusHost).Children.Count == 2, "the task row");
        var row = (StackPanel)((Panel)_w.PluginStatusHost).Children[1];
        var bar = row.Children.OfType<ProgressBar>().Single();
        Assert.True(bar.IsIndeterminate);

        await Pump(Editor(UiMethods.TaskProgress, $"{{\"task\":\"{id}\",\"fraction\":0.5,\"message\":\"a.json\"}}"));
        await Until(() => ((StackPanel)((Panel)_w.PluginStatusHost).Children[1]).Children.OfType<ProgressBar>().Single().Value == 0.5, "the progress");
        row = (StackPanel)((Panel)_w.PluginStatusHost).Children[1];
        Assert.Equal("Indexing: a.json", row.Children.OfType<TextBlock>().Single().Text);

        row.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForEventAsync(UiEvents.TaskCancelled);

        await Pump(Editor(UiMethods.TaskEnd, $"{{\"task\":\"{id}\"}}"));
        await Pump(Editor(UiMethods.StatusClear, "{}"));
        await Until(() => ((Panel)_w.PluginStatusHost).Children.Count == 0, "an empty plugin area");
    }

    [AvaloniaFact]
    public async Task Quick_pick_uses_the_palette_and_answers_with_the_chosen_item_or_null_when_dismissed()
    {
        await StartPluginAsync();
        var pick = Editor(UiMethods.QuickPick, "{\"title\":\"Indent\",\"items\":[{\"id\":\"two\",\"label\":\"2 spaces\"},{\"id\":\"four\",\"label\":\"4 spaces\",\"detail\":\"default\"}]}");
        await Until(() => _w.IsPaletteOpen && _w.PaletteModeNow == PaletteMode.Choice, "the quick pick");
        Assert.Equal(["2 spaces", "4 spaces"], _w.QuickResults.Select(r => r.Title));
        _w.RunPaletteSelection();
        Assert.Equal("two", (await Pump(pick)).GetProperty("id").GetString());
        Assert.False(_w.IsPaletteOpen);

        var dismissed = Editor(UiMethods.QuickPick, "{\"title\":\"Indent\",\"items\":[{\"id\":\"two\",\"label\":\"2 spaces\"}]}");
        await Until(() => _w.IsPaletteOpen, "the second quick pick");
        _w.ClosePalette();
        Assert.Equal(JsonValueKind.Null, (await Pump(dismissed)).GetProperty("id").ValueKind);
    }

    [AvaloniaFact]
    public async Task Input_and_confirm_use_the_dialogs_of_the_app_and_a_notice_becomes_a_bar()
    {
        await StartPluginAsync();
        _dialogs.Prompts.Enqueue("typed");
        Assert.Equal("typed", (await Pump(Editor(UiMethods.Input, "{\"title\":\"Name\",\"label\":\"Name:\"}"))).GetProperty("value").GetString());
        _dialogs.Prompts.Enqueue(null);
        Assert.Equal(JsonValueKind.Null, (await Pump(Editor(UiMethods.Input, "{\"title\":\"Name\",\"label\":\"Name:\"}"))).GetProperty("value").ValueKind);

        _dialogs.Answers.Enqueue(0);
        Assert.True((await Pump(Editor(UiMethods.Confirm, "{\"title\":\"Delete\",\"message\":\"Really?\"}"))).GetProperty("confirmed").GetBoolean());
        _dialogs.Answers.Enqueue(1);
        Assert.False((await Pump(Editor(UiMethods.Confirm, "{\"title\":\"Delete\",\"message\":\"Really?\"}"))).GetProperty("confirmed").GetBoolean());
        Assert.Equal(("Delete", "Really?", Loc.Get(K.DialogOk), Loc.Get(K.DialogCancel)),
            (_dialogs.Asked[0].Title, _dialogs.Asked[0].Message, _dialogs.Asked[0].Buttons[0].Label, _dialogs.Asked[0].Buttons[1].Label));

        await Pump(Editor(UiMethods.Notify, "{\"level\":\"error\",\"message\":\"Broken\"}"));
        await Until(() => _w.Notices.Any(n => n.Message == "Broken"), "the notice");
        Assert.Contains("warning", _w.Notices.Single(n => n.Message == "Broken").Classes);
    }

    [AvaloniaFact]
    public async Task When_the_plugin_dies_everything_it_showed_is_removed()
    {
        await StartPluginAsync();
        await Pump(Editor(UiMethods.StatusSet, "{\"text\":\"alive\"}"));
        await Pump(Editor(UiMethods.PanelSet, "{\"panel\":\"x\",\"title\":\"X\",\"kind\":\"list\",\"reveal\":true,\"items\":[{\"id\":\"a\",\"label\":\"A\"}]}"));
        await Until(() => _w.PluginPanelOpen, "the panel");
        await Pump(Editor(UiMethods.TaskStart, "{\"title\":\"Busy\"}"));

        var crash = Host.CallAsync("p", "crash", Encoding.UTF8.GetBytes("{\"code\":9}"));
        await Until(() => crash.IsFaulted || crash.IsCompleted, "the crash");
        await Until(() => !_w.PluginPanelOpen && ((Panel)_w.PluginStatusHost).Children.Count == 0, "the cleanup after the crash");
        Assert.Empty(_app.Plugins.Ui.Tasks);
    }

    [AvaloniaFact]
    public void The_texts_of_the_plugin_UI_exist_in_every_language()
    {
        foreach (var tag in Loc.Languages)
        {
            Loc.SetLanguage(tag);
            foreach (var key in new[] { K.PluginPanelClose, K.A11yPluginPanel, K.PluginTaskCancel, K.A11yPluginTaskCancel, K.A11yPluginTask, K.A11yPluginTaskBusy, K.PluginPickWatermark, K.A11yPluginPick, K.PluginUnavailable, K.PluginProposal, K.PluginSymbolsTitle, K.PluginSymbolsOutdated })
                Assert.NotEqual(key, Loc.Get(key));
        }
    }

    // ---- P3b.3.5: outline, reveal, snippets and Tab for early tools ----

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private async Task<DocTab> OpenForPluginAsync(string name, string content, bool expose = true)
    {
        await StartPluginAsync();
        foreach (var permission in new[] { DocumentPermissions.Read, DocumentPermissions.EditDirect })
            _app.Plugins.Permissions.Grant("p", permission, GrantScope.All);
        var tab = _w.OpenFile(_temp.File(name, content), null);
        Dispatcher.UIThread.RunJobs();
        if (expose) _app.Plugins.Documents.Expose(tab.Doc.Doc, tab.Editor!.ByteSelections);
        return tab;
    }

    private static string DocParams(DocTab tab, string rest) => $"{{\"document\":\"{tab.Doc.Doc.Id}\",\"revision\":{tab.Doc.Doc.Revision},{rest}}}";

    private ByteRange Selected(DocTab tab) => tab.Editor!.ByteSelections()!.PrimarySelection.ToRange();

    private TreeViewItem OutlineItem(string id) => _w.PluginTree.Items.OfType<TreeViewItem>().Single(i => (string?)i.Tag == id);

    private const string TwoSymbols = "\"symbols\":[{\"id\":\"a\",\"name\":\"é1\",\"kind\":\"function\",\"start\":0,\"end\":3},{\"id\":\"b\",\"name\":\"f2\",\"kind\":\"function\",\"start\":4,\"end\":6}]";

    [AvaloniaFact]
    public async Task Symbols_of_a_revision_fill_the_outline_and_an_older_answer_is_never_shown_as_current()
    {
        var tab = await OpenForPluginAsync("o.txt", "é1\nf2\nend\n");
        var firstRevision = tab.Doc.Doc.Revision;

        await Pump(Editor(DocumentMethods.SymbolsPublish, DocParams(tab, TwoSymbols)));
        await Until(() => _w.PluginPanelOpen, "the outline");
        Assert.Equal(Loc.Get(K.PluginSymbolsTitle), _w.PluginPanelTitle);
        Assert.Equal(["a", "b"], _w.PluginTree.Items.OfType<TreeViewItem>().Select(i => (string?)i.Tag));

        // the text moves on: the same outline stays, marked as outdated
        tab.Doc.Doc.Insert(0, Bytes("12"));
        await Until(() => _w.PluginPanelTitle == Loc.Get(K.PluginSymbolsOutdated), "the outdated mark");

        // the plugin answers late for the revision it read: refused, the outline is not marked current again
        var late = await Pump(Editor(DocumentMethods.SymbolsPublish, $"{{\"document\":\"{tab.Doc.Doc.Id}\",\"revision\":{firstRevision},{TwoSymbols}}}"));
        Assert.Equal(DocumentErrorCodes.RevisionConflict, late.GetProperty("error").GetInt32());
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Loc.Get(K.PluginSymbolsOutdated), _w.PluginPanelTitle);

        // a click on an outdated entry still lands on the same text, where it is now
        _w.PluginTree.SelectedItem = OutlineItem("b");
        _w.ActivatePluginItem();
        Assert.Equal(new ByteRange(6, 8), Selected(tab));
        Assert.Equal("f2", Encoding.UTF8.GetString(tab.Doc.Doc.ReadBytes(Selected(tab))));

        // an entry whose text was changed is not revealed at all
        Assert.True(tab.Doc.Doc.Replace(new ByteRange(4, 5), Bytes("Q")).Applied);
        _w.PluginTree.SelectedItem = OutlineItem("a");
        tab.Editor!.SetSelections([Sel.At(0)]);
        _w.ActivatePluginItem();
        Assert.Equal(new ByteRange(0, 0), Selected(tab));

        await Pump(Editor(DocumentMethods.SymbolsPublish, DocParams(tab, "\"symbols\":[{\"id\":\"a\",\"name\":\"zz\",\"start\":0,\"end\":2}]")));
        await Until(() => _w.PluginPanelTitle == Loc.Get(K.PluginSymbolsTitle), "the current outline");
    }

    [AvaloniaFact]
    public async Task Reveal_selects_the_same_bytes_as_a_click_in_the_outline()
    {
        var tab = await OpenForPluginAsync("r.txt", "é1\nf2\nend\n");
        await Pump(Editor(DocumentMethods.SymbolsPublish, DocParams(tab, TwoSymbols)));
        await Until(() => _w.PluginPanelOpen, "the outline");

        _w.PluginTree.SelectedItem = OutlineItem("a");
        _w.ActivatePluginItem();
        var clicked = Selected(tab);
        Assert.Equal(new ByteRange(0, 3), clicked); // "é1": two characters, three bytes

        tab.Editor!.SetSelections([Sel.At(8)]);
        Assert.Equal(new ByteRange(9, 9), Selected(tab));
        var answer = await Pump(Editor(DocumentMethods.Reveal, DocParams(tab, "\"start\":0,\"end\":3")));
        Assert.True(answer.GetProperty("revealed").GetBoolean());
        Assert.Equal(clicked, Selected(tab));

        _w.PluginTree.SelectedItem = OutlineItem("b");
        _w.ActivatePluginItem();
        clicked = Selected(tab);
        tab.Editor.SetSelections([Sel.At(0)]);
        await Pump(Editor(DocumentMethods.Reveal, DocParams(tab, "\"start\":4,\"end\":6")));
        Assert.Equal((new ByteRange(4, 6), clicked), (Selected(tab), new ByteRange(4, 6)));

        // not on a character boundary, or for an old revision: nothing moves
        tab.Editor.SetSelections([Sel.At(0)]);
        var split = await Pump(Editor(DocumentMethods.Reveal, DocParams(tab, "\"start\":1,\"end\":3")));
        Assert.Equal(DocumentErrorCodes.InvalidRange, split.GetProperty("error").GetInt32());
        var oldRevision = tab.Doc.Doc.Revision;
        tab.Doc.Doc.Insert(tab.Doc.Doc.ByteLength, Bytes("x"));
        var stale = await Pump(Editor(DocumentMethods.Reveal, $"{{\"document\":\"{tab.Doc.Doc.Id}\",\"revision\":{oldRevision},\"start\":4,\"end\":6}}"));
        Assert.Equal(DocumentErrorCodes.RevisionConflict, stale.GetProperty("error").GetInt32());
        Assert.Equal(new ByteRange(0, 0), Selected(tab));
    }

    [AvaloniaFact]
    public async Task A_snippet_puts_text_and_carets_into_all_selections_as_one_undo_step()
    {
        var tab = await OpenForPluginAsync("s.txt", "é1\né2\n");
        var surface = tab.Editor!;
        surface.SetSelections([new Sel(1, 2), new Sel(4, 5)]);
        Assert.Equal(2, surface.CaretCount);
        var doc = tab.Doc.Doc;
        var depth = doc.UndoDepth;
        var revision = doc.Revision;

        var answer = await Pump(Editor(DocumentMethods.SnippetInsert, DocParams(tab, "\"text\":\"[]\",\"carets\":[1]")));

        Assert.True(answer.GetProperty("applied").GetBoolean());
        Assert.Equal("é[]\né[]\n", Utf8(tab));
        Assert.Equal((revision + 1, depth + 1), (doc.Revision, doc.UndoDepth));
        Assert.Equal(2, surface.CaretCount);
        Assert.Equal([TextSelection.At(3), TextSelection.At(8)], surface.ByteSelections()!.Items);

        _w.Undo();
        Assert.Equal("é1\né2\n", Utf8(tab));
        Assert.Equal([new TextSelection(2, 3), new TextSelection(6, 7)], surface.ByteSelections()!.Items);
    }

    [AvaloniaFact]
    public async Task An_unhandled_Tab_indents_as_without_a_plugin_and_a_handled_one_does_not()
    {
        const string content = "ab\ncd\n";
        var baseline = _w.OpenFile(_temp.File("t0.txt", content), null);
        Dispatcher.UIThread.RunJobs();
        baseline.Editor!.TextArea.Focus();
        await RunTabAsync();
        var indented = Utf8(baseline);
        Assert.NotEqual(content, indented);

        // the document is not visible to plugins before and after Tab; the editor shows it only while it asks
        await OpenForPluginAsync("t.txt", "", expose: false);
        await Pump(Editor(DocumentMethods.TabRegister, "{\"enabled\":true}"));
        await Pump(Host.CallAsync("p", "setTab", Encoding.UTF8.GetBytes("{\"handled\":false}")));
        var tab = _w.OpenFile(_temp.File("t1.txt", content), null);
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();
        await RunTabAsync();
        Assert.Equal(indented, Utf8(tab));
        Assert.False(_app.Plugins.Documents.IsExposed(tab.Doc.Doc.Id));
        var asked = await Pump(Host.CallAsync("p", "tabRequests"));
        Assert.Equal(1, asked.GetArrayLength());
        Assert.Equal(tab.Doc.Doc.Id.ToString(), asked[0].GetProperty("document").GetString());

        await Pump(Host.CallAsync("p", "setTab", Encoding.UTF8.GetBytes("{\"handled\":true}")));
        var handled = _w.OpenFile(_temp.File("t2.txt", content), null);
        Dispatcher.UIThread.RunJobs();
        handled.Editor!.TextArea.Focus();
        await RunTabAsync();
        Assert.Equal(content, Utf8(handled));
        Assert.Equal(2, (await Pump(Host.CallAsync("p", "tabRequests"))).GetArrayLength());
    }

    private async Task RunTabAsync()
    {
        var run = _app.Workbench.Commands.ExecuteAsync("nks.edit.tab", new ShellContext(_w, forMenu: false)).AsTask();
        await Until(() => run.IsCompleted, "the Tab command");
        Assert.Equal(CommandOutcome.Handled, await run);
    }

    // ---- JSON formatter inside the app ----

    /// <summary>Starts a command whose plugin waits for the user (a proposal), so the command is not awaited to its end.</summary>
    private async Task StartAsync(string command)
    {
        var run = _app.Workbench.Commands.ExecuteAsync(command, new ShellContext(_w, forMenu: false)).AsTask();
        await Until(() => run.IsCompleted, command);
        Assert.Equal(CommandOutcome.Handled, await run);
    }

    private async Task RunAsync(string command)
    {
        var run = _app.Workbench.Commands.ExecuteAsync(command, new ShellContext(_w, forMenu: false)).AsTask();
        await Until(() => run.IsCompleted, command);
        Assert.Equal(CommandOutcome.Handled, await run);
        // the command itself returns at once; the plugin process answers later
        await Until(() => !_app.Plugins.IsBusy, "the plugin command to finish");
    }

    private static string Utf8(DocTab tab) => Encoding.UTF8.GetString(tab.Doc.Doc.ReadBytes(new ByteRange(0, tab.Doc.Doc.ByteLength)));

    [AvaloniaFact]
    public async Task Format_JSON_formats_the_selection_through_the_plugin_process_in_one_undo_step()
    {
        var tab = _w.OpenFile(_temp.File("a.json", "head ě\r\n{\"a\":[1,2],\"b\":\"č\"}\r\ntail\r\n"), null);
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();
        tab.Editor.Select(8, 19);
        Assert.Equal("{\"a\":[1,2],\"b\":\"č\"}", tab.Editor.SelectedText);

        await RunAsync("nks.edit.formatJson");

        Assert.Equal("head ě\r\n{\n  \"a\": [\n    1,\n    2\n  ],\n  \"b\": \"č\"\n}\r\ntail\r\n", Utf8(tab));
        _w.Undo();
        Assert.Equal("head ě\r\n{\"a\":[1,2],\"b\":\"č\"}\r\ntail\r\n", Utf8(tab));
    }

    [AvaloniaFact]
    public async Task A_syntax_error_in_the_document_is_reported_in_a_bar_and_leaves_it_alone()
    {
        var tab = _w.OpenFile(_temp.File("bad.json", "{\"a\":1,}"), null);
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();

        await RunAsync("nks.edit.formatJson");

        Assert.Equal("{\"a\":1,}", Utf8(tab));
        Assert.True(_w.Notices.Any(n => n.Message.Contains("JSON") && n.Message.Contains("8:")), string.Join(" | ", _w.Notices.Select(n => n.Message)));
    }

    // ---- text tools inside the app ----

    private bool HasNotice(string text) => _w.Notices.Any(n => n.Message.Contains(text, StringComparison.Ordinal));

    private static string Grouped(long n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');

    private DocTab OpenPaged(string name, byte[] content)
    {
        _app.Workbench.Set("files.largeFileThreshold", System.Text.Json.Nodes.JsonValue.Create(1));
        var tab = _w.OpenFile(_temp.Bytes(name, content), null);
        Assert.True(tab.Doc.IsLarge);
        return tab;
    }

    [AvaloniaFact]
    public async Task Statistics_of_a_paged_file_run_in_the_plugin_process_and_the_document_stays_as_it_was()
    {
        const string line = "alpha beta gamma\r\n";
        var bytes = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(line, (3 << 20) / line.Length)));
        var tab = OpenPaged("big.txt", bytes);
        long lines = bytes.Length / line.Length;

        await RunAsync("nks.tools.statistics");

        Assert.True(HasNotice($"Bajty: {Grouped(bytes.Length)}, znaky: {Grouped(bytes.Length)}, slova: {Grouped(3 * lines)}, řádky: {Grouped(lines + 1)}"), string.Join(" | ", _w.Notices.Select(n => n.Message)));
        Assert.Equal(bytes.Length, tab.Doc.Doc.ByteLength);
        await Until(() => _app.Plugins.Ui.Tasks.Count == 0, "the plugin task to end");
    }

    [AvaloniaFact]
    public async Task A_hash_of_a_paged_file_is_picked_from_the_algorithms_and_matches_the_bytes()
    {
        var bytes = new byte[3 * (1 << 20) + 17];
        new Random(5).NextBytes(bytes);
        OpenPaged("big.bin", bytes);

        var run = _app.Workbench.Commands.ExecuteAsync("nks.tools.hash", new ShellContext(_w, forMenu: false)).AsTask();
        await Until(() => _w.IsPaletteOpen && _w.PaletteModeNow == PaletteMode.Choice, "the algorithm pick");
        Assert.Equal(["MD5", "SHA-1", "SHA-256", "SHA-384", "SHA-512", "CRC-32"], _w.QuickResults.Select(r => r.Title));
        _w.RunPaletteSelection();
        await Until(() => run.IsCompleted, "the command");
        await Until(() => !_app.Plugins.IsBusy, "the plugin command to finish");

        Assert.True(HasNotice(Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(bytes))), string.Join(" | ", _w.Notices.Select(n => n.Message)));
    }

    [AvaloniaFact]
    public async Task A_selected_column_adds_up_in_the_format_of_the_ui_language_and_the_document_does_not_change()
    {
        var tab = _w.OpenFile(_temp.File("n.txt", "1 234,50\n10,25\n-3\n"), null);
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();
        tab.Editor.Select(0, 17);
        var before = Utf8(tab);

        await RunAsync("nks.tools.calculateBlock");
        Assert.True(HasNotice("Počet: 3, součet: 1 241,75, nejmenší: -3, největší: 1 234,5, průměr: 413,9166666"), string.Join(" | ", _w.Notices.Select(n => n.Message)));

        _app.SetLanguage("de");
        await RunAsync("nks.tools.calculateBlock");
        Assert.True(HasNotice("Anzahl: 3, Summe: 1.241,75"), string.Join(" | ", _w.Notices.Select(n => n.Message)));
        Assert.Equal(before, Utf8(tab));
    }

    [AvaloniaFact]
    public async Task Expressions_and_number_bases_ask_in_a_dialog_and_a_system_call_is_refused()
    {
        _w.NewDocument();
        _dialogs.Prompts.Enqueue("2*(3+4)");
        await RunAsync("nks.tools.evaluateExpression");
        Assert.True(HasNotice("2*(3+4) = 14"), string.Join(" | ", _w.Notices.Select(n => n.Message)));

        _dialogs.Prompts.Enqueue("System(\"calc\")");
        await RunAsync("nks.tools.evaluateExpression");
        Assert.True(HasNotice("Neznámý název 'System'"), string.Join(" | ", _w.Notices.Select(n => n.Message)));

        _dialogs.Prompts.Enqueue("0xFF");
        await RunAsync("nks.tools.convertBase");
        Assert.True(HasNotice("Desítkově: 255, šestnáctkově: 0xFF"), string.Join(" | ", _w.Notices.Select(n => n.Message)));
    }

    [AvaloniaFact]
    public async Task Preview_shows_the_change_first_and_applies_it_only_when_the_user_agrees()
    {
        var tab = _w.OpenFile(_temp.File("p.json", "[1,2]"), null);
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();

        await StartAsync("nks.edit.previewFormatJson");
        await Until(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true, "the change review");
        Assert.Equal("[1,2]", Utf8(tab));
        _w.ResultsView!.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => !_app.Plugins.IsBusy, "the rejected proposal");
        Assert.Equal("[1,2]", Utf8(tab));

        _w.SelectTab(tab);
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();
        await StartAsync("nks.edit.previewFormatJson");
        await Until(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true && _w.ResultsView.ApplyButton.IsVisible, "the second change review");
        _w.ApplyPlan();
        await Until(() => Utf8(tab) == "[\n  1,\n  2\n]", "the applied preview");
    }

    // ---- P8.4.1, P8.4.2, P8.7.1: diagnostics, completion and the schema check ----

    private async Task<TextBlock> StatusItemAsync(Func<string, bool> match, string what)
    {
        TextBlock? found = null;
        await Until(() => (found = ((Panel)_w.PluginStatusHost).Children.OfType<TextBlock>().FirstOrDefault(t => match(t.Text ?? ""))) is not null, what);
        return found!;
    }

    [AvaloniaFact]
    public async Task Diagnostics_are_underlined_follow_the_text_and_go_when_their_text_is_changed()
    {
        var tab = await OpenForPluginAsync("d.txt", "é1\nf2\nend\n");
        var surface = tab.Editor!;
        var published = await Pump(Editor(DocumentMethods.DiagnosticsPublish, DocParams(tab, "\"source\":\"t\",\"diagnostics\":[{\"start\":4,\"end\":6,\"severity\":\"warning\",\"message\":\"two\"}]")));
        Assert.Equal(1, published.GetProperty("accepted").GetInt32());
        var mark = Assert.Single(surface.Diagnostics);
        Assert.Equal((3, 5, DiagnosticSeverity.Warning, "two"), (mark.Start, mark.End, mark.Severity, mark.Message)); // bytes 4..6 are characters 3..5 after the two-byte "é"
        Assert.Equal(mark, surface.DiagnosticAt(4));
        Assert.Null(surface.DiagnosticAt(5));

        tab.Doc.Doc.Insert(0, Bytes("12"));
        Assert.Equal((5, 7), (surface.Diagnostics[0].Start, surface.Diagnostics[0].End));
        Assert.True(tab.Doc.Doc.Replace(new ByteRange(7, 8), Bytes("Q")).Applied);
        Assert.Empty(surface.Diagnostics);
    }

    [AvaloniaFact]
    public async Task A_plugin_offer_is_listed_with_the_words_and_applied_with_its_edit_elsewhere_as_one_undo_step_and_an_older_answer_is_not_listed()
    {
        var tab = await OpenForPluginAsync("c.txt", "é1\nf2\nfoot\n");
        await Pump(Editor(DocumentMethods.CompletionRegister, "{\"enabled\":true}"));
        var doc = tab.Doc.Doc;
        tab.Editor!.SetSelections([Sel.At(4)]); // after "f", byte 5
        string Answer(long revision) => "{\"answer\":{\"revision\":" + revision + ",\"items\":[{\"label\":\"foo\",\"detail\":\"fn\",\"start\":4,\"end\":5,\"text\":\"foo\",\"edits\":[{\"start\":0,\"end\":0,\"text\":\"use x;\\n\"}]}]}}";

        await Pump(Host.CallAsync("p", "setCompletion", Bytes(Answer(doc.Revision - 1))));
        _w.ShowCompletion();
        await Until(() => _w.IsPaletteOpen && _w.PaletteModeNow == PaletteMode.Choice, "the list with the words");
        Assert.DoesNotContain(_w.QuickResults, r => r.Title == "foo" && r.Category == "fn");
        Assert.Contains(_w.QuickResults, r => r.Title == "foot");
        _w.ClosePalette();

        await Pump(Host.CallAsync("p", "setCompletion", Bytes(Answer(doc.Revision))));
        _w.ShowCompletion();
        await Until(() => _w.IsPaletteOpen && _w.PaletteModeNow == PaletteMode.Choice, "the list with the offer");
        Assert.Equal("foo", _w.QuickResults[0].Title); // the plugin offer comes first, in the same list as the words
        Assert.Contains(_w.QuickResults, r => r.Title == "foot");
        var depth = doc.UndoDepth;
        _w.RunPaletteSelection();
        Assert.Equal("use x;\né1\nfoo2\nfoot\n", Utf8(tab));
        Assert.Equal(depth + 1, doc.UndoDepth);

        _w.Undo();
        Assert.Equal("é1\nf2\nfoot\n", Utf8(tab));
    }

    [AvaloniaFact]
    public async Task The_schema_check_underlines_an_error_in_package_json_names_the_schema_in_the_status_bar_and_completes_keys()
    {
        var tab = _w.OpenFile(_temp.File("package.json", "{ \"name\": \"demo\", \"ve\": 1, \"private\": \"yes\" }"), null);
        Dispatcher.UIThread.RunJobs();
        _app.Plugins.TrackSchema(_w, tab); // the status bar asks once the text has rested; a command that is using the document makes it ask again

        await Until(() => tab.Editor!.Diagnostics.Count > 0, "the underline");
        var mark = Assert.Single(tab.Editor!.Diagnostics);
        Assert.Equal("\"yes\"", tab.Editor.Document.GetText(mark.Start, mark.End - mark.Start));
        await StatusItemAsync(t => t == Loc.Get(K.SchemaName, ("name", "package.json")), "the schema in the status bar");

        // key completion from the schema, in the same list as the words
        tab.Editor.SetSelections([Sel.At(tab.Editor.Document.Text.IndexOf("\"ve", StringComparison.Ordinal) + 3)]);
        _w.ShowCompletion();
        await Until(() => _w.IsPaletteOpen && _w.PaletteModeNow == PaletteMode.Choice, "the list with the schema keys");
        Assert.Contains(_w.QuickResults, r => r.Title == "version");
        _w.ClosePalette();
    }

    [AvaloniaFact]
    public async Task A_json_file_without_a_schema_says_so_and_a_plain_text_tab_is_not_a_schema_file()
    {
        var tab = _w.OpenFile(_temp.File("notes.json", "{ \"a\": 1 }"), null);
        Dispatcher.UIThread.RunJobs();
        _app.Plugins.TrackSchema(_w, tab);
        await StatusItemAsync(t => t == Loc.Get(K.SchemaNone), "no schema in the status bar");
        Assert.Empty(tab.Editor!.Diagnostics);

        var plain = _w.OpenFile(_temp.File("n.txt", "x"), null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(PluginServices.IsSchemaFile(plain));
    }

    // ---- P8.5.4 and P8.5.5: language server commands (the protocol itself is tested with a fake server in Rpc.Tests) ----

    [AvaloniaFact]
    public async Task A_paged_file_never_starts_the_language_server()
    {
        var tab = OpenPaged("big.log", Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("alpha beta gamma\r\n", (3 << 20) / 18))));

        Assert.False(await _app.Plugins.RunLspAsync(_w, tab, "lsp.start"));

        Assert.Equal(PluginState.Inactive, Host.GetState(PluginServices.LspPluginId));
    }

    [AvaloniaFact]
    public async Task A_document_of_a_language_without_a_server_gets_the_notice_of_the_plugin_process()
    {
        var tab = _w.OpenFile(_temp.File("plain.txt", "some text\n"), null);
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();

        await RunAsync("nks.lsp.goToDefinition");

        Assert.True(HasNotice("Pro tento typ dokumentu není nastavený žádný jazykový server."), string.Join(" | ", _w.Notices.Select(n => n.Message)));
        Assert.Equal(PluginState.Running, Host.GetState(PluginServices.LspPluginId));
    }

    [AvaloniaFact]
    public void A_place_of_the_server_is_selected_by_its_utf16_columns_and_an_item_of_the_list_names_it()
    {
        var path = _temp.File("place.txt", "é€😀 x\nsecond é 😀 target here\n");
        _w.GoToPlace(path, 1, 12, 1, 18);

        var tab = _w.Active!;
        Assert.Equal("target", Encoding.UTF8.GetString(tab.Doc.Doc.ReadBytes(Selected(tab))));

        Assert.Equal((path, 3, 4, 5, 6), PluginServices.ParsePlace("3:4:5:6|" + path)!.Value);
        Assert.Null(PluginServices.ParsePlace("3:4|" + path));
        Assert.Null(PluginServices.ParsePlace("no bar"));
    }
}
