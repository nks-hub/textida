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
using NksHub.Textida.Core.Documents;
using NksHub.Textida.PluginHost;
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

    public PluginUiTests()
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
            foreach (var key in new[] { K.PluginPanelClose, K.A11yPluginPanel, K.PluginTaskCancel, K.A11yPluginTaskCancel, K.A11yPluginTask, K.A11yPluginTaskBusy, K.PluginPickWatermark, K.A11yPluginPick, K.PluginUnavailable, K.PluginProposal })
                Assert.NotEqual(key, Loc.Get(key));
        }
    }

    // ---- JSON formatter inside the app ----

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

    [AvaloniaFact]
    public async Task Preview_shows_the_change_first_and_applies_it_only_when_the_user_agrees()
    {
        var tab = _w.OpenFile(_temp.File("p.json", "[1,2]"), null);
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();

        _dialogs.Answers.Enqueue(1);
        await RunAsync("nks.edit.previewFormatJson");
        await Until(() => _dialogs.Asked.Count == 1, "the preview dialog");
        Assert.Equal("[1,2]", Utf8(tab));

        _dialogs.Answers.Enqueue(0);
        await RunAsync("nks.edit.previewFormatJson");
        await Until(() => Utf8(tab) == "[\n  1,\n  2\n]", "the applied preview");
    }
}
