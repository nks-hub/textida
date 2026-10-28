using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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

    private static bool IsBold(TreeViewItem node) => ((TextBlock)((StackPanel)node.Header!).Children[0]).FontWeight == Avalonia.Media.FontWeight.Bold;

    [AvaloniaFact]
    public async Task The_current_item_of_a_panel_is_drawn_in_bold_named_so_and_moves_without_the_tree_being_built_again()
    {
        Loc.SetLanguage("en"); // the name is spoken in the language of the editor, Czech by default; Dispose puts it back
        await StartPluginAsync();
        await Pump(Editor(UiMethods.PanelSet, "{\"panel\":\"outline\",\"title\":\"Outline\",\"kind\":\"tree\",\"reveal\":true,\"current\":\"b\",\"items\":[{\"id\":\"a\",\"label\":\"first\",\"detail\":\"class\",\"children\":[{\"id\":\"b\",\"label\":\"second\"}]},{\"id\":\"c\",\"label\":\"third\"}]}"));
        await Until(() => _w.PluginPanelOpen, "the revealed panel");
        var a = OutlineItem("a");
        var b = a.Items.OfType<TreeViewItem>().Single();
        var c = OutlineItem("c");
        Assert.Equal([false, true, false], new[] { IsBold(a), IsBold(b), IsBold(c) });
        Assert.Equal(("second, current item", "first, class", "third"), (Avalonia.Automation.AutomationProperties.GetName(b), Avalonia.Automation.AutomationProperties.GetName(a), Avalonia.Automation.AutomationProperties.GetName(c)));

        // the mark is no selection: the item the user walked to stays selected, and the nodes stay the very same ones
        _w.PluginTree.SelectedItem = a;
        await Pump(Editor(UiMethods.PanelCurrent, "{\"panel\":\"outline\",\"current\":\"c\"}"));
        await Until(() => IsBold(c), "the mark moving to the last item");
        Assert.Equal([false, false, true], new[] { IsBold(a), IsBold(b), IsBold(c) });
        Assert.Equal("second", Avalonia.Automation.AutomationProperties.GetName(b));
        Assert.Same(a, _w.PluginTree.SelectedItem);
        Assert.Same(a, OutlineItem("a"));
        Assert.Same(c, OutlineItem("c"));

        await Pump(Editor(UiMethods.PanelCurrent, "{\"panel\":\"outline\",\"current\":null}"));
        await Until(() => !IsBold(c), "the mark going");
        Assert.DoesNotContain(true, new[] { IsBold(a), IsBold(b), IsBold(c) });

        // new items without a current item draw none
        await Pump(Editor(UiMethods.PanelSet, "{\"panel\":\"outline\",\"title\":\"Outline\",\"kind\":\"tree\",\"items\":[{\"id\":\"a\",\"label\":\"first\"},{\"id\":\"c\",\"label\":\"third\"}]}"));
        await Until(() => !ReferenceEquals(OutlineItem("c"), c), "the new items");
        Assert.False(IsBold(OutlineItem("a")) || IsBold(OutlineItem("c")));
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

    private const string TwoItems = "\"items\":[{\"id\":\"a\",\"label\":\"first\"},{\"id\":\"b\",\"label\":\"second\"}]";

    [AvaloniaFact]
    public async Task A_panel_opens_from_the_palette_with_the_keyboard_on_its_first_item_and_Esc_in_it_closes_it()
    {
        _w.Show();
        await StartPluginAsync();
        await Pump(Editor(UiMethods.PanelSet, "{\"panel\":\"todo\",\"title\":\"Todo\",\"kind\":\"list\"," + TwoItems + "}"));
        await Until(() => _app.Plugins.Ui.Panels.Count == 1, "the panel data");
        Assert.False(_w.PluginPanelOpen);

        await _app.Workbench.Commands.ExecuteAsync("nks.view.pluginPanels", new ShellContext(_w, forMenu: false));
        Assert.True(_w.IsPaletteOpen && _w.PaletteModeNow == PaletteMode.Choice);
        var row = Assert.Single(_w.QuickResults);
        Assert.Equal(("Todo", "p"), (row.Title, row.Category));
        _w.RunPaletteSelection();
        await Until(() => _w.PluginPanelOpen && _w.PluginTree.SelectedItem is TreeViewItem { Tag: "a" } && _w.PluginTree.IsKeyboardFocusWithin, "the keyboard on the first item");
        Assert.Equal("Todo", _w.PluginPanelTitle);
        Assert.Equal("view.pluginPanels", _w.HelpContextTopic());

        TestApp.Press(_w, Avalonia.Input.Key.Escape);
        Assert.False(_w.PluginPanelOpen);
    }

    [AvaloniaFact]
    public async Task The_panel_list_says_so_when_no_plugin_shows_a_panel()
    {
        await _app.Workbench.Commands.ExecuteAsync("nks.view.pluginPanels", new ShellContext(_w, forMenu: false));
        Assert.Empty(_w.QuickResults);
        Assert.Equal(Loc.Get(K.PluginPanelsEmpty), _w.PaletteEmptyText);
    }

    [AvaloniaFact]
    public async Task New_items_of_the_open_panel_keep_the_selected_item_and_a_status_change_leaves_the_tree_alone()
    {
        await StartPluginAsync();
        await Pump(Editor(UiMethods.PanelSet, "{\"panel\":\"todo\",\"title\":\"Todo\",\"kind\":\"list\",\"reveal\":true," + TwoItems + "}"));
        await Until(() => _w.PluginPanelOpen, "the panel");
        var second = _w.PluginTree.Items.OfType<TreeViewItem>().Single(i => (string?)i.Tag == "b");
        _w.PluginTree.SelectedItem = second;

        await Pump(Editor(UiMethods.StatusSet, "{\"text\":\"busy\"}"));
        await Until(() => ((Panel)_w.PluginStatusHost).Children.Count == 1, "the status item");
        Assert.Same(second, _w.PluginTree.SelectedItem);

        await Pump(Editor(UiMethods.PanelSet, "{\"panel\":\"todo\",\"title\":\"Todo\",\"kind\":\"list\",\"items\":[{\"id\":\"new\",\"label\":\"zero\"},{\"id\":\"a\",\"label\":\"first\"},{\"id\":\"b\",\"label\":\"second\"}]}"));
        await Until(() => _w.PluginTree.Items.Count == 3, "the new items");
        Assert.Equal("b", (_w.PluginTree.SelectedItem as TreeViewItem)?.Tag);
    }

    [AvaloniaFact]
    public async Task Esc_in_the_panel_returns_the_keyboard_to_the_text_while_Esc_in_the_text_leaves_the_panel_open()
    {
        _w.Show();
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = "text";
        await StartPluginAsync();
        await Pump(Editor(UiMethods.PanelSet, "{\"panel\":\"todo\",\"title\":\"Todo\",\"kind\":\"list\",\"reveal\":true," + TwoItems + "}"));
        await Until(() => _w.PluginPanelOpen && _w.PluginTree.IsKeyboardFocusWithin, "the keyboard in the panel");

        TestApp.Press(_w, Avalonia.Input.Key.Escape);
        await Until(() => !_w.PluginPanelOpen && tab.Editor.IsKeyboardFocusWithin, "the keyboard back in the text");

        _w.ShowPluginPanel("p", "todo");
        await Until(() => _w.PluginPanelOpen && _w.PluginTree.IsKeyboardFocusWithin, "the keyboard in the panel again");
        tab.Editor.TextArea.Focus();
        await Until(() => tab.Editor.IsKeyboardFocusWithin, "the keyboard in the text");
        TestApp.Press(_w, Avalonia.Input.Key.Escape);
        Assert.True(_w.PluginPanelOpen);
    }

    [AvaloniaFact]
    public async Task The_arrows_move_through_the_panel_and_Enter_sends_the_chosen_item_to_the_plugin()
    {
        _w.Show();
        await StartPluginAsync();
        await Pump(Editor(UiMethods.PanelSet, "{\"panel\":\"todo\",\"title\":\"Todo\",\"kind\":\"list\",\"reveal\":true," + TwoItems + "}"));
        await Until(() => _w.PluginPanelOpen && _w.PluginTree.SelectedItem is TreeViewItem { Tag: "a" } && _w.PluginTree.IsKeyboardFocusWithin, "the first item with the keyboard");

        TestApp.Press(_w, Avalonia.Input.Key.Down);
        Assert.Equal("b", (_w.PluginTree.SelectedItem as TreeViewItem)?.Tag);
        TestApp.Press(_w, Avalonia.Input.Key.Enter);

        var sw = Stopwatch.StartNew();
        while (true)
        {
            var all = await Pump(Host.CallAsync("p", "events"));
            var activated = all.EnumerateArray().Where(e => e.GetProperty("name").GetString() == UiEvents.PanelActivated).ToList();
            if (activated.Count > 0)
            {
                var payload = Assert.Single(activated).GetProperty("payload");
                Assert.Equal(("todo", "b"), (payload.GetProperty("panel").GetString(), payload.GetProperty("item").GetString()));
                break;
            }
            Assert.True(sw.ElapsedMilliseconds < 30_000, "Not reached: the activated item");
            await Task.Delay(20);
        }
    }

    [AvaloniaFact]
    public async Task F1_in_the_open_panel_opens_the_help_topic_of_plugin_panels()
    {
        _w.Show();
        await StartPluginAsync();
        await Pump(Editor(UiMethods.PanelSet, "{\"panel\":\"todo\",\"title\":\"Todo\",\"kind\":\"list\",\"reveal\":true," + TwoItems + "}"));
        await Until(() => _w.PluginPanelOpen && _w.PluginTree.IsKeyboardFocusWithin, "the keyboard in the panel");

        TestApp.Press(_w, Avalonia.Input.Key.F1);
        Assert.Equal("view.pluginPanels", _app.HelpView!.CurrentTopic);
        _app.HelpView.Close();
    }

    [AvaloniaFact]
    public async Task The_panel_its_items_and_the_status_item_carry_names_for_a_screen_reader_and_the_command_is_in_the_view_menu_and_the_palette()
    {
        await StartPluginAsync();
        await Pump(Editor(UiMethods.PanelSet, "{\"panel\":\"todo\",\"title\":\"Todo\",\"kind\":\"tree\",\"reveal\":true,\"items\":[{\"id\":\"a\",\"label\":\"first\",\"detail\":\"open\",\"children\":[{\"id\":\"b\",\"label\":\"second\"}]}]}"));
        await Until(() => _w.PluginPanelOpen, "the panel");
        await Pump(Editor(UiMethods.StatusSet, "{\"text\":\"JSON ok\",\"tooltip\":\"valid\"}"));
        await Until(() => ((Panel)_w.PluginStatusHost).Children.Count == 1, "the status item");

        var name = Loc.Get(K.A11yPluginPanel, ("title", "Todo"));
        Assert.Equal(name, Avalonia.Automation.AutomationProperties.GetName(_w.PluginTree));
        var first = Assert.IsType<TreeViewItem>(Assert.Single(_w.PluginTree.Items));
        Assert.Equal("first, open", Avalonia.Automation.AutomationProperties.GetName(first));
        Assert.Equal("second", Avalonia.Automation.AutomationProperties.GetName(Assert.IsType<TreeViewItem>(Assert.Single(first.Items))));
        var status = Assert.IsType<TextBlock>(Assert.Single(((Panel)_w.PluginStatusHost).Children));
        Assert.Equal("JSON ok", Avalonia.Automation.AutomationProperties.GetName(status));

        // the next report changes the same item, it is not replaced by another one
        await Pump(Editor(UiMethods.StatusSet, "{\"text\":\"JSON error\"}"));
        await Until(() => status.Text == "JSON error", "the new status text");
        Assert.Same(status, Assert.Single(((Panel)_w.PluginStatusHost).Children));
        Assert.Equal("JSON error", Avalonia.Automation.AutomationProperties.GetName(status));

        var inMenu = _w.MenuModel.Single(g => g.Label == "Zobrazit").Items.AllCommands().Select(c => c.CommandId);
        Assert.Contains("nks.view.pluginPanels", inMenu);
        _w.ShowPalette();
        _w.SetPaletteQuery(_app.Workbench.Strings.Get(_app.Workbench.Commands.TryGet("nks.view.pluginPanels", out var command) ? command.TitleKey : ""));
        Assert.Contains(_w.PaletteResults, r => r.CommandId == "nks.view.pluginPanels" && r.Enabled);
    }

    [AvaloniaFact]
    public async Task A_task_row_is_kept_while_progress_comes_and_says_it_cancels_once_Cancel_was_pressed()
    {
        await StartPluginAsync();
        var id = (await Pump(Editor(UiMethods.TaskStart, "{\"title\":\"Indexing\",\"cancellable\":true}"))).GetProperty("task").GetString()!;
        await Until(() => ((Panel)_w.PluginStatusHost).Children.Count == 1, "the task row");
        var row = (StackPanel)((Panel)_w.PluginStatusHost).Children[0];
        var cancel = row.Children.OfType<Button>().Single();

        // a row rebuilt on every report would drop a click that lands between two of them
        await Pump(Editor(UiMethods.TaskProgress, $"{{\"task\":\"{id}\",\"fraction\":0.5}}"));
        await Until(() => row.Children.OfType<ProgressBar>().Single().Value == 0.5, "the progress in the same row");
        Assert.Same(row, ((Panel)_w.PluginStatusHost).Children[0]);

        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForEventAsync(UiEvents.TaskCancelled);
        await Until(() => !cancel.IsVisible, "the row saying it cancels");
        Assert.Equal(Loc.Get(K.PluginTaskCancelling, ("title", "Indexing")), row.Children.OfType<TextBlock>().Single().Text);
        Assert.True(_app.Plugins.Ui.Tasks.Single().Cancelling);

        await Pump(Editor(UiMethods.TaskEnd, $"{{\"task\":\"{id}\"}}"));
        await Until(() => ((Panel)_w.PluginStatusHost).Children.Count == 0, "the row gone after the end");
    }

    [AvaloniaFact]
    public async Task A_crash_is_shown_with_the_name_of_the_plugin_and_its_last_error_output_goes_to_the_output_window()
    {
        await StartPluginAsync();
        var crash = Host.CallAsync("p", "crash", Encoding.UTF8.GetBytes("{\"code\":9}"));
        await Until(() => crash.IsCompleted, "the crash");
        await Assert.ThrowsAsync<PluginCrashedException>(() => crash);

        await Until(() => _w.Notices.Any(n => n.Message.Contains("Test plugin")), "the notice of the crash");
        var notice = _w.Notices.Single(n => n.Message.Contains("Test plugin"));
        Assert.Contains("warning", notice.Classes);
        // the first crash waits the base delay of the restart placeholder, one second
        Assert.Equal(Loc.Get(K.PluginCrashRestart, ("name", "Test plugin"), ("reason", Loc.Get(K.PluginCrashExited)), ("seconds", 1)), notice.Message);
        Assert.Contains(_app.Output.Read(OutputChannel.Tasks), l => l.Source == "Test plugin" && l.Text.Contains("asked to crash"));

        var show = ((Grid)notice.Child!).Children.OfType<StackPanel>().Single().Children.OfType<Button>().Single();
        Assert.Equal(Loc.Get(K.PluginCrashLog), show.Content);
        show.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(_w.OutputPanelOpen);

        await Until(() => Host.GetState("p") == PluginState.Running, "the restart");
    }

    [AvaloniaFact]
    public void A_frozen_plugin_and_one_turned_off_after_repeated_crashes_get_their_own_words()
    {
        var frozen = PluginServices.CrashMessage(new PluginCrashInfo("p", "Test plugin", PluginEndReason.Unresponsive, null, "", [], TimeSpan.FromMilliseconds(2500)));
        Assert.Equal(Loc.Get(K.PluginCrashRestart, ("name", "Test plugin"), ("reason", Loc.Get(K.PluginCrashUnresponsive)), ("seconds", 3)), frozen);
        var off = PluginServices.CrashMessage(new PluginCrashInfo("p", "Test plugin", PluginEndReason.Exited, 1, "", [], null));
        Assert.Equal(Loc.Get(K.PluginCrashDisabled, ("name", "Test plugin"), ("reason", Loc.Get(K.PluginCrashExited))), off);
    }

    [AvaloniaFact]
    public async Task After_repeated_crashes_the_bar_says_the_plugin_is_turned_off_and_the_host_log_is_in_the_output_window()
    {
        await StartPluginAsync();
        for (int i = 0; i < 3; i++)
        {
            await Until(() => Host.GetState("p") == PluginState.Running, "the plugin running again");
            var crash = Host.CallAsync("p", "crash", Encoding.UTF8.GetBytes("{\"code\":9}"));
            await Until(() => crash.IsCompleted, "the crash");
            await Assert.ThrowsAsync<PluginCrashedException>(() => crash);
        }
        await Until(() => Host.GetState("p") == PluginState.Disabled, "the plugin turned off");

        string off = Loc.Get(K.PluginCrashDisabled, ("name", "Test plugin"), ("reason", Loc.Get(K.PluginCrashExited)));
        await Until(() => _w.Notices.Any(n => n.Message == off), "the notice that the plugin is turned off");
        var log = _app.Output.Read(OutputChannel.Tasks).Where(l => l.Source == Loc.Get(K.PluginLogSource)).Select(l => l.Text).ToList();
        Assert.Equal(2, log.Count(l => l.Contains("Restart in")));
        Assert.Single(log, l => l.Contains("Disabled."));
        Assert.All(log, l => Assert.Contains("'p' crashed", l));
    }

    [AvaloniaFact]
    public void The_texts_of_the_plugin_UI_exist_in_every_language()
    {
        foreach (var tag in Loc.Languages)
        {
            Loc.SetLanguage(tag);
            foreach (var key in new[] { K.PluginPanelClose, K.A11yPluginPanel, K.PluginTaskCancel, K.A11yPluginTaskCancel, K.A11yPluginTask, K.A11yPluginTaskBusy, K.PluginPickWatermark, K.A11yPluginPick, K.PluginUnavailable, K.PluginProposal, K.PluginSymbolsTitle, K.PluginSymbolsOutdated,
                K.PluginCrashRestart, K.PluginCrashDisabled, K.PluginCrashExited, K.PluginCrashUnresponsive, K.PluginCrashProtocol, K.PluginCrashStart, K.PluginCrashLog, K.PluginLogSource,
                K.PluginTaskCancelling, K.PluginPanelsWatermark, K.A11yPluginPanels, K.PluginPanelsEmpty })
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

        // the selection holds no line break, so the lines the formatter breaks take the line ending of the file (all three are CRLF)
        Assert.Equal("head ě\r\n{\r\n  \"a\": [\r\n    1,\r\n    2\r\n  ],\r\n  \"b\": \"č\"\r\n}\r\ntail\r\n", Utf8(tab));
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
    public async Task Remove_commas_sort_keys_and_minify_run_through_the_plugin_process_each_as_one_undo_step()
    {
        const string original = "{\"b\": 1,\r\n \"a\": [1, 2,],\r\n}";
        var tab = _w.OpenFile(_temp.File("m.json", original), null);
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();

        await RunAsync("nks.edit.fixJsonCommas");
        const string repaired = "{\"b\": 1,\r\n \"a\": [1, 2]\r\n}";
        Assert.Equal(repaired, Utf8(tab));

        await RunAsync("nks.edit.sortJsonKeys");
        const string sorted = "{\r\n  \"a\": [\r\n    1,\r\n    2\r\n  ],\r\n  \"b\": 1\r\n}";
        Assert.Equal(sorted, Utf8(tab));

        await RunAsync("nks.edit.minifyJson");
        Assert.Equal("{\"a\":[1,2],\"b\":1}", Utf8(tab));

        _w.Undo();
        Assert.Equal(sorted, Utf8(tab));
        _w.Undo();
        Assert.Equal(repaired, Utf8(tab));
        _w.Undo();
        Assert.Equal(original, Utf8(tab));
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
    public async Task A_hash_of_a_paged_file_is_picked_from_the_algorithms_and_shown_in_the_dialog_of_the_hex_view()
    {
        var bytes = new byte[3 * (1 << 20) + 17];
        new Random(5).NextBytes(bytes);
        OpenPaged("big.bin", bytes);
        _dialogs.Answers.Enqueue(2); // Close

        var run = _app.Workbench.Commands.ExecuteAsync("nks.tools.hash", new ShellContext(_w, forMenu: false)).AsTask();
        await Until(() => _w.IsPaletteOpen && _w.PaletteModeNow == PaletteMode.Choice, "the algorithm pick");
        // the same list as the checksum dialog of the hex view; the first row is not MD5, so the test narrows the list to it
        Assert.Equal(NksHub.Textida.Core.Hex.Checksums.All.Select(a => a.Name), _w.QuickResults.Select(r => r.Title));
        Assert.Contains(_w.QuickResults, r => r.Title == "MD5");
        _w.SetPaletteQuery("MD5");
        Assert.Equal(["MD5"], _w.QuickResults.Select(r => r.Title));
        _w.RunPaletteSelection();
        await Until(() => run.IsCompleted, "the command");
        await Until(() => !_app.Plugins.IsBusy, "the plugin command to finish");

        // the result is in the dialog of the hex view for its checksums, not in a notice of its own
        string md5 = Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(bytes));
        var asked = Assert.Single(_dialogs.Asked);
        Assert.Equal([md5], asked.Details);
        Assert.Equal(Loc.Get(K.HexSumTitle), asked.Title);
        Assert.Equal([Loc.Get(K.HexSumCopy), Loc.Get(K.HexSumCompare), Loc.Get(K.HexChangesClose)], asked.Buttons.Select(b => b.Label));
        Assert.Equal(Loc.Get(K.HexSumResult, ("name", "MD5"), ("scope", Loc.Get(K.HexSumScopeFile, ("bytes", bytes.Length)))), asked.Message);
        Assert.False(HasNotice(md5), string.Join(" | ", _w.Notices.Select(n => n.Message)));
    }

    [AvaloniaFact]
    public async Task The_hash_dialog_of_the_text_tools_copies_every_part_and_compares_a_pasted_value_with_each_one()
    {
        var tab = _w.OpenFile(_temp.File("parts.txt", "abcdef"), null);
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();
        tab.Editor.SetSelections([new Sel(0, 2), new Sel(3, 5)]);
        string Sha1(string text) => Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(text)));
        _dialogs.Answers.Enqueue(1); // Compare
        _dialogs.Prompts.Enqueue(Sha1("de").ToUpperInvariant());
        _dialogs.Answers.Enqueue(0); // Copy

        var run = _app.Workbench.Commands.ExecuteAsync("nks.tools.hash", new ShellContext(_w, forMenu: false)).AsTask();
        await Until(() => _w.IsPaletteOpen && _w.PaletteModeNow == PaletteMode.Choice, "the algorithm pick");
        _w.SetPaletteQuery("SHA-1");
        Assert.Equal(["SHA-1"], _w.QuickResults.Select(r => r.Title));
        _w.RunPaletteSelection();
        await Until(() => run.IsCompleted, "the command");

        Assert.Equal(2, _dialogs.Asked.Count);
        Assert.Equal([Sha1("ab"), Sha1("de")], _dialogs.Asked[0].Details);
        Assert.Contains(Loc.Get(K.HexSumScopeParts, ("count", 2), ("bytes", 4)), _dialogs.Asked[0].Message);
        // the second time the dialog says, for each part, whether the pasted value is its hash
        Assert.EndsWith(Loc.Get(K.HexSumPart, ("number", 1), ("result", Loc.Get(K.HexSumDiffers))) + "\n" + Loc.Get(K.HexSumPart, ("number", 2), ("result", Loc.Get(K.HexSumMatch))), _dialogs.Asked[1].Message);
        Assert.Equal(Sha1("ab") + "\n" + Sha1("de"), await _w.Clipboard!.TryGetTextAsync());
        Assert.Equal(Loc.Get(K.HexSumCopied, ("name", "SHA-1")), _w.StatusMessageText);
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
    public async Task Insert_expression_writes_the_result_behind_the_selection_or_the_line_in_the_plain_form_and_each_is_one_undo_step()
    {
        const string original = "žluťoučký: 10/4\r\n1000*1.5+2\r\nbez výrazu\r\n";
        var tab = _w.OpenFile(_temp.File("calc.txt", original), null);
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();

        // the selection is the expression; the characters before it are two bytes each, the result goes behind the selection and not in front of the line break
        tab.Editor.Select(original.IndexOf("10/4", StringComparison.Ordinal), 4);
        await RunAsync("nks.tools.insertExpression");
        Assert.Equal("žluťoučký: 10/4 = 2.5\r\n1000*1.5+2\r\nbez výrazu\r\n", Utf8(tab));
        Assert.True(HasNotice("10/4 = 2,5"), string.Join(" | ", _w.Notices.Select(n => n.Message))); // the notice follows the language of the interface, the document does not

        // no selection: the line of the caret is the expression, and a thousands separator or a decimal comma never reaches the file
        tab.Editor.Select(tab.Editor.Text.IndexOf("1000*", StringComparison.Ordinal) + 3, 0);
        await RunAsync("nks.tools.insertExpression");
        Assert.Equal("žluťoučký: 10/4 = 2.5\r\n1000*1.5+2 = 1502\r\nbez výrazu\r\n", Utf8(tab));
        Assert.Equal(2, tab.Doc.Doc.UndoDepth);

        _w.Undo();
        Assert.Equal("žluťoučký: 10/4 = 2.5\r\n1000*1.5+2\r\nbez výrazu\r\n", Utf8(tab));
        _w.Undo();
        Assert.Equal(original, Utf8(tab));
    }

    [AvaloniaFact]
    public async Task Insert_expression_changes_nothing_when_the_text_is_no_expression_or_calls_the_system()
    {
        const string original = "2 $ 3\r\nSystem(\"calc\")\r\n\r\n";
        var tab = _w.OpenFile(_temp.File("nope.txt", original), null);
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();

        tab.Editor.Select(2, 0);
        await RunAsync("nks.tools.insertExpression");
        Assert.True(HasNotice("Chyba ve výrazu na pozici"), string.Join(" | ", _w.Notices.Select(n => n.Message)));

        tab.Editor.Select(original.IndexOf("System", StringComparison.Ordinal) + 2, 0);
        await RunAsync("nks.tools.insertExpression");
        Assert.True(HasNotice("Neznámý název 'System'"), string.Join(" | ", _w.Notices.Select(n => n.Message)));

        tab.Editor.Select(original.Length, 0); // the empty line behind the last break
        await RunAsync("nks.tools.insertExpression");
        Assert.True(HasNotice("Označte výraz, nebo dejte kurzor na řádek s výrazem."), string.Join(" | ", _w.Notices.Select(n => n.Message)));

        Assert.Equal(original, Utf8(tab));
        Assert.Equal(0, tab.Doc.Doc.UndoDepth);
    }

    [AvaloniaFact]
    public async Task Insert_expression_is_off_in_the_large_file_mode_and_the_document_stays_as_it_was()
    {
        // longer than the threshold of the paged surface, which the test sets to 1 MiB
        string original = "first line\n2^10\nlast\n" + string.Concat(Enumerable.Repeat("a line that only makes the file larger than the threshold\n", 25_000));
        var tab = OpenPaged("expr.txt", Encoding.UTF8.GetBytes(original));
        await Until(() => tab.Paged!.Index.Complete, "the line index");
        Assert.True(tab.GoToLine(2, 3));

        var run = _app.Workbench.Commands.ExecuteAsync("nks.tools.insertExpression", new ShellContext(_w, forMenu: false)).AsTask();
        await Until(() => run.IsCompleted, "the command");

        Assert.Equal(CommandOutcome.Disabled, await run);
        Assert.Equal(original, Utf8(tab));
        Assert.Equal(0, tab.Doc.Doc.UndoDepth);
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

    private sealed record Shot(byte[] Data, int Stride);

    private Shot Capture()
    {
        _w.UpdateLayout();
        for (int i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        var frame = _w.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        var data = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, data, 0, data.Length);
        return new Shot(data, buffer.RowBytes);
    }

    /// <summary>The window area of one text line of the editor, over the whole width of the text.</summary>
    private Avalonia.Rect LineBand(DocTab tab, int line)
    {
        var view = tab.Editor!.TextArea.TextView;
        view.EnsureVisualLines();
        var visual = view.VisualLines[line];
        var origin = view.TranslatePoint(default, _w)!.Value;
        return new Avalonia.Rect(origin.X, origin.Y + visual.VisualTop - view.ScrollOffset.Y, view.Bounds.Width, visual.Height);
    }

    /// <summary>Pixels that are clearly red; text, caret and background of both themes are grey. The frame is RGBA or BGRA depending on the platform: either order counts.</summary>
    private static int Reddish(Shot shot, Avalonia.Rect area)
    {
        int count = 0;
        for (int y = (int)area.Top; y < (int)area.Bottom; y++)
            for (int x = (int)area.Left; x < (int)area.Right; x++)
            {
                int at = y * shot.Stride + x * 4;
                if (at + 3 >= shot.Data.Length) continue;
                int first = shot.Data[at], green = shot.Data[at + 1], third = shot.Data[at + 2];
                if ((first - green >= 40 && first - third >= 40) || (third - green >= 40 && third - first >= 40)) count++;
            }
        return count;
    }

    [AvaloniaFact]
    public async Task A_published_diagnostic_is_drawn_as_a_wave_under_its_text_and_the_wave_goes_with_the_text()
    {
        var tab = await OpenForPluginAsync("wave.txt", "alpha beta gamma\nsecond line\nthird line\n");
        var plain = Capture();
        var (first, second) = (LineBand(tab, 0), LineBand(tab, 1));
        Assert.Equal(0, Reddish(plain, first));

        await Pump(Editor(DocumentMethods.DiagnosticsPublish, DocParams(tab, "\"source\":\"t\",\"diagnostics\":[{\"start\":6,\"end\":10,\"severity\":\"error\",\"message\":\"bad\"}]")));
        var drawn = Capture();

        Assert.Single(tab.Editor!.Diagnostics);
        Assert.True(Reddish(drawn, first) >= 6, "the wave under the word");
        Assert.Equal(0, Reddish(drawn, second));

        Assert.True(tab.Doc.Doc.Replace(new ByteRange(7, 8), Bytes("E")).Applied);
        var edited = Capture();

        Assert.Empty(tab.Editor.Diagnostics);
        Assert.Equal(0, Reddish(edited, first));
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

    [AvaloniaFact]
    public async Task A_file_the_user_mapped_to_a_schema_is_checked_at_once_and_the_rules_reach_the_plugin_through_their_file()
    {
        var schema = _temp.File("thing.schema.json", """{ "type": "object", "properties": { "n": { "type": "integer" } } }""");
        var tab = _w.OpenFile(_temp.File("app.cfg", "{ \"n\": \"x\" }"), null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(PluginServices.IsSchemaFile(tab)); // .cfg is not a configuration extension the editor knows

        var rules = $"*.cfg={schema} as json";
        Assert.Null(_app.Workbench.Set("schema.mappings", System.Text.Json.Nodes.JsonValue.Create(rules)));
        Assert.True(PluginServices.IsSchemaFile(tab));
        Assert.Equal(rules, File.ReadAllText(Path.Combine(_app.ProfileDirectory, "schema-mappings.txt")));

        await Until(() => tab.Editor!.Diagnostics.Count > 0, "the underline from the schema the rule names");
        var mark = Assert.Single(tab.Editor!.Diagnostics);
        Assert.Equal("\"x\"", tab.Editor.Document.GetText(mark.Start, mark.End - mark.Start));
        _app.Plugins.TrackSchema(_w, tab);
        await StatusItemAsync(t => t == Loc.Get(K.SchemaName, ("name", "thing.schema.json")), "the schema of the rule in the status bar");

        Assert.Null(_app.Workbench.Set("schema.mappings", System.Text.Json.Nodes.JsonValue.Create("")));
        Assert.False(PluginServices.IsSchemaFile(tab)); // without the rule the extension is unknown again
    }

    /// <summary>A server on the loopback address that answers one path with a body and counts what it was asked; the schemas of the tests come from here, never from a real host.</summary>
    private sealed class SchemaServer : IDisposable
    {
        private readonly System.Net.HttpListener _listener = new();
        public List<string> Requests { get; } = [];
        public string Url { get; }

        public SchemaServer(string path, string body)
        {
            int port;
            using (var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0))
            {
                probe.Start();
                port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
            }
            Url = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(Url + "/");
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    System.Net.HttpListenerContext context;
                    try { context = await _listener.GetContextAsync(); }
                    catch (Exception e) when (e is System.Net.HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }
                    lock (Requests) Requests.Add(context.Request.Url!.AbsolutePath);
                    var bytes = Encoding.UTF8.GetBytes(context.Request.Url.AbsolutePath == path ? body : "{}");
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                }
            });
        }

        public int Count
        {
            get { lock (Requests) return Requests.Count; }
        }

        public void Dispose() => _listener.Close();
    }

    [AvaloniaFact]
    public async Task A_schema_at_an_address_waits_for_consent_then_underlines_completes_and_describes_from_the_download()
    {
        using var server = new SchemaServer("/schemas/thing.json", """{ "type": "object", "properties": { "n": { "type": "integer", "description": "A number." } } }""");
        var tab = _w.OpenFile(_temp.File("thing.json", $"{{ \"$schema\": \"{server.Url}/schemas/thing.json\", \"n\": \"x\" }}"), null);
        Dispatcher.UIThread.RunJobs();
        _app.Plugins.TrackSchema(_w, tab);
        await StatusItemAsync(t => t == Loc.Get(K.SchemaNoConsent, ("name", "thing.json")), "the status bar says the download is not allowed");
        Assert.Empty(tab.Editor!.Diagnostics);
        Assert.Equal(0, server.Count);

        _dialogs.Answers.Enqueue(1); // refused
        var refused = _w.SchemaAllowDownloadAsync(tab);
        await Until(() => refused.IsCompleted, "the refused consent");
        Assert.Contains(Assert.Single(_dialogs.Asked).Details!, d => d.Contains("127.0.0.1", StringComparison.Ordinal));
        Assert.False(_app.Plugins.Registries.IsAllowed(PackageRegistryConsent.SchemaId("127.0.0.1")));
        Assert.Equal(0, server.Count);

        _dialogs.Answers.Enqueue(0); // agreed
        var agreed = _w.SchemaAllowDownloadAsync(tab);
        await Until(() => agreed.IsCompleted, "the agreed consent");
        Assert.True(_app.Plugins.Registries.IsAllowed(PackageRegistryConsent.SchemaId("127.0.0.1")));
        await Until(() => tab.Editor!.Diagnostics.Count > 0, "the underline from the downloaded schema");
        var mark = Assert.Single(tab.Editor!.Diagnostics);
        Assert.Equal("\"x\"", tab.Editor.Document.GetText(mark.Start, mark.End - mark.Start));
        await StatusItemAsync(t => t == Loc.Get(K.SchemaName, ("name", "thing.json")), "the schema in the status bar");
        Assert.Equal(1, server.Count);

        Loc.SetLanguage("en"); // the hover is written in the language of the editor, which is Czech by default; Dispose puts it back
        var hover = await Pump(tab.Editor.HoverProvider!(tab.Editor.Document.Text.IndexOf("\"n\"", StringComparison.Ordinal) + 2));
        Assert.Equal("A number.\n\nType: integer", hover!.Value.Text);
        Assert.Equal("\"n\"", tab.Editor.Document.GetText(hover.Value.Start, hover.Value.End - hover.Value.Start));
        Assert.Equal(1, server.Count); // the description came from the copy, not from the host

        _w.PackageRevoke(); // forbids every download: the file is checked again without the schema
        await Until(() => tab.Editor!.Diagnostics.Count == 0, "the underline gone after the revoke");
        await StatusItemAsync(t => t == Loc.Get(K.SchemaNoConsent, ("name", "thing.json")), "the status bar after the revoke");
        Assert.Equal(1, server.Count);
    }

    [AvaloniaFact]
    public async Task A_schema_file_on_disk_is_used_without_asking_for_consent()
    {
        _temp.File("thing.schema.json", """{ "type": "object", "properties": { "n": { "type": "integer" } } }""");
        var tab = _w.OpenFile(_temp.File("thing.json", "{ \"$schema\": \"thing.schema.json\", \"n\": \"x\" }"), null);
        Dispatcher.UIThread.RunJobs();
        _app.Plugins.TrackSchema(_w, tab);

        await Until(() => tab.Editor!.Diagnostics.Count > 0, "the underline from the schema file");
        await StatusItemAsync(t => t == Loc.Get(K.SchemaName, ("name", "thing.schema.json")), "the schema file in the status bar");
        Assert.Empty(_dialogs.Asked);
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
