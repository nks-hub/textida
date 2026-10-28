using System.Text.Json;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>UI contributions through a real plugin process: the test plugin sends the requests, a recording stand-in for the editor draws.</summary>
public class UiApiTests
{
    internal sealed class RecordingUi : IPluginUi
    {
        public readonly List<PanelModel> Panels = [];
        public readonly List<string> Removed = [];
        public readonly List<(string Panel, string? Item)> Currents = [];
        public readonly List<StatusModel?> Statuses = [];
        public readonly List<(NoticeLevel Level, string Message)> Notices = [];
        public readonly List<TaskModel> Started = [];
        public readonly List<(string Task, TaskProgressModel Progress)> Progress = [];
        public readonly List<string> Ended = [];
        public readonly List<string> GonePlugins = [];
        public Func<QuickPickRequest, string?> Pick = _ => null;
        public Func<InputRequest, string?> Input = _ => null;
        public Func<ConfirmRequest, bool> Confirm = _ => false;
        public readonly List<QuickPickRequest> Picks = [];

        public void SetPanel(string pluginId, PanelModel panel) { lock (Panels) Panels.Add(panel); }
        public void RemovePanel(string pluginId, string panelId) { lock (Removed) Removed.Add(panelId); }
        public void SetCurrentItem(string pluginId, string panelId, string? itemId) { lock (Currents) Currents.Add((panelId, itemId)); }
        public void SetStatus(string pluginId, StatusModel? status) { lock (Statuses) Statuses.Add(status); }
        public Task<string?> PickAsync(string pluginId, QuickPickRequest request, CancellationToken ct) { Picks.Add(request); return Task.FromResult(Pick(request)); }
        public Task<string?> InputAsync(string pluginId, InputRequest request, CancellationToken ct) => Task.FromResult(Input(request));
        public Task<bool> ConfirmAsync(string pluginId, ConfirmRequest request, CancellationToken ct) => Task.FromResult(Confirm(request));
        public void Notify(string pluginId, NoticeLevel level, string message) { lock (Notices) Notices.Add((level, message)); }
        public void StartTask(string pluginId, TaskModel task) { lock (Started) Started.Add(task); }
        public void UpdateTask(string pluginId, string taskId, TaskProgressModel progress) { lock (Progress) Progress.Add((taskId, progress)); }
        public void EndTask(string pluginId, string taskId) { lock (Ended) Ended.Add(taskId); }
        public void RemovePlugin(string pluginId) { lock (GonePlugins) GonePlugins.Add(pluginId); }
    }

    sealed class Rig : IAsyncDisposable
    {
        public required PluginHost.PluginHost Host { get; init; }
        public required UiApi Api { get; init; }
        public required RecordingUi Ui { get; init; }

        public static async Task<Rig> CreateAsync(TimeSpan? cancelGrace = null)
        {
            var ui = new RecordingUi();
            PluginHost.PluginHost? host = null;
            var api = new UiApi(new UiApiOptions
            {
                MaxPanelsPerPlugin = 2, MaxItemsPerPanel = 6, MaxTreeDepth = 3, MaxTasksPerPlugin = 2, MaxTextLength = 40, MaxPickItems = 3,
                TaskCancelGrace = cancelGrace ?? TimeSpan.FromSeconds(30),
            }, ui, (plugin, name, payload) => host!.SendEventAsync(plugin, name, payload));
            host = new PluginHost.PluginHost(PluginHostTests.Options(), api.HandleAsync);
            host.Register(new PluginSpec("p", "Plugin p", T.DotnetHost, [T.PluginDll], ["onCommand:p"]));
            await host.ActivateAsync("onCommand:p");
            return new Rig { Host = host, Api = api, Ui = ui };
        }

        public async Task<JsonElement> CallAsync(string method, string paramsJson)
        {
            var json = $"{{\"method\":\"{method}\",\"params\":{paramsJson}}}";
            return (await Host.CallAsync("p", "callEditor", T.Json(json))).Clone();
        }

        public async Task ErrorAsync(string method, string paramsJson, int code)
        {
            var r = await CallAsync(method, paramsJson);
            Assert.True(r.ValueKind == JsonValueKind.Object && r.TryGetProperty("error", out var e) && e.GetInt32() == code, "Expected error " + code + ", got " + r.GetRawText());
        }

        public async Task WaitEventAsync(string name)
        {
            for (var i = 0; i < 750; i++)
            {
                var events = (await Host.CallAsync("p", "events")).EnumerateArray().Select(e => e.GetRawText());
                if (events.Any(e => e.Contains(name))) return;
                await Task.Delay(20);
            }
            throw new TimeoutException(name);
        }

        public async Task<JsonElement> LastEventAsync(string name) =>
            (await Host.CallAsync("p", "events")).EnumerateArray().Last(e => e.GetRawText().Contains(name)).Clone();

        public async ValueTask DisposeAsync() => await Host.DisposeAsync();
    }

    [Fact]
    public async Task A_tree_panel_reaches_the_editor_as_data_and_an_activated_item_reaches_the_plugin()
    {
        await using var rig = await Rig.CreateAsync();
        var set = await rig.CallAsync(UiMethods.PanelSet,
            "{\"panel\":\"outline\",\"title\":\"Outline\",\"kind\":\"tree\",\"reveal\":true,\"items\":[{\"id\":\"a\",\"label\":\"root\",\"detail\":\"object\",\"children\":[{\"id\":\"b\",\"label\":\"child\"}]}]}");
        Assert.Equal(2, set.GetProperty("items").GetInt32());
        var panel = Assert.Single(rig.Ui.Panels);
        Assert.Equal(("outline", "Outline", PanelKind.Tree, true), (panel.Id, panel.Title, panel.Kind, panel.Reveal));
        Assert.Equal("child", panel.Items[0].Children[0].Label);
        Assert.Equal("object", panel.Items[0].Detail);

        Assert.True(await rig.Api.ActivateItemAsync("p", "outline", "b"));
        await rig.WaitEventAsync(UiEvents.PanelActivated);
        var payload = (await rig.LastEventAsync(UiEvents.PanelActivated)).GetProperty("payload");
        Assert.Equal(("outline", "b"), (payload.GetProperty("panel").GetString(), payload.GetProperty("item").GetString()));

        Assert.False(await rig.Api.ActivateItemAsync("p", "outline", "missing"));
        Assert.False(await rig.Api.ActivateItemAsync("p", "nope", "a"));

        Assert.True((await rig.CallAsync(UiMethods.PanelRemove, "{\"panel\":\"outline\"}")).GetProperty("removed").GetBoolean());
        Assert.Equal(["outline"], rig.Ui.Removed);
        Assert.False(await rig.Api.ActivateItemAsync("p", "outline", "b"));
    }

    [Fact]
    public async Task Panel_data_that_breaks_a_limit_or_a_rule_is_refused_and_nothing_is_drawn()
    {
        await using var rig = await Rig.CreateAsync();
        const string Header = "\"title\":\"T\",";
        await rig.ErrorAsync(UiMethods.PanelSet, "{\"panel\":\"x\"," + Header + "\"kind\":\"list\",\"items\":[{\"id\":\"a\",\"label\":\"A\",\"children\":[{\"id\":\"b\",\"label\":\"B\"}]}]}", RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(UiMethods.PanelSet, "{\"panel\":\"x\"," + Header + "\"kind\":\"list\",\"items\":[{\"id\":\"a\",\"label\":\"A\"},{\"id\":\"a\",\"label\":\"B\"}]}", RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(UiMethods.PanelSet, "{\"panel\":\"x\"," + Header + "\"kind\":\"grid\",\"items\":[]}", RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(UiMethods.PanelSet, "{\"panel\":\"x\",\"title\":\"" + new string('t', 41) + "\",\"kind\":\"list\",\"items\":[]}", RpcErrorCodes.InvalidParams);
        var seven = string.Join(",", Enumerable.Range(0, 7).Select(i => $"{{\"id\":\"i{i}\",\"label\":\"L\"}}"));
        await rig.ErrorAsync(UiMethods.PanelSet, "{\"panel\":\"x\"," + Header + "\"kind\":\"list\",\"items\":[" + seven + "]}", UiErrorCodes.QuotaExceeded);
        await rig.ErrorAsync(UiMethods.PanelSet, "{\"panel\":\"x\"," + Header + "\"kind\":\"tree\",\"items\":[{\"id\":\"1\",\"label\":\"a\",\"children\":[{\"id\":\"2\",\"label\":\"b\",\"children\":[{\"id\":\"3\",\"label\":\"c\",\"children\":[{\"id\":\"4\",\"label\":\"d\"}]}]}]}]}", RpcErrorCodes.InvalidParams);
        Assert.Empty(rig.Ui.Panels);

        await rig.CallAsync(UiMethods.PanelSet, "{\"panel\":\"one\"," + Header + "\"kind\":\"list\",\"items\":[]}");
        await rig.CallAsync(UiMethods.PanelSet, "{\"panel\":\"two\"," + Header + "\"kind\":\"list\",\"items\":[]}");
        await rig.ErrorAsync(UiMethods.PanelSet, "{\"panel\":\"three\"," + Header + "\"kind\":\"list\",\"items\":[]}", UiErrorCodes.QuotaExceeded);
        await rig.CallAsync(UiMethods.PanelSet, "{\"panel\":\"one\"," + Header + "\"kind\":\"list\",\"items\":[]}"); // replacing is not a new panel
    }

    [Fact]
    public async Task A_panel_names_its_current_item_and_moves_the_mark_without_sending_the_items_again()
    {
        await using var rig = await Rig.CreateAsync();
        const string Items = "\"items\":[{\"id\":\"a\",\"label\":\"A\",\"children\":[{\"id\":\"b\",\"label\":\"B\"}]},{\"id\":\"c\",\"label\":\"C\"}]";
        await rig.CallAsync(UiMethods.PanelSet, "{\"panel\":\"outline\",\"title\":\"T\",\"kind\":\"tree\"," + Items + "}");
        await rig.CallAsync(UiMethods.PanelSet, "{\"panel\":\"outline\",\"title\":\"T\",\"kind\":\"tree\",\"current\":\"b\"," + Items + "}");
        Assert.Equal([null, "b"], rig.Ui.Panels.Select(p => p.Current));

        // the mark moves, and goes, with the panel staying as it is
        Assert.True((await rig.CallAsync(UiMethods.PanelCurrent, "{\"panel\":\"outline\",\"current\":\"c\"}")).GetProperty("ok").GetBoolean());
        await rig.CallAsync(UiMethods.PanelCurrent, "{\"panel\":\"outline\",\"current\":null}");
        await rig.CallAsync(UiMethods.PanelCurrent, "{\"panel\":\"outline\"}");
        Assert.Equal([("outline", "c"), ("outline", null), ("outline", null)], rig.Ui.Currents);
        Assert.Equal(2, rig.Ui.Panels.Count);

        // an item or a panel the plugin does not have is refused, and nothing is drawn
        await rig.ErrorAsync(UiMethods.PanelCurrent, "{\"panel\":\"outline\",\"current\":\"missing\"}", RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(UiMethods.PanelCurrent, "{\"panel\":\"nope\",\"current\":\"a\"}", RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(UiMethods.PanelSet, "{\"panel\":\"x\",\"title\":\"T\",\"kind\":\"list\",\"current\":\"z\",\"items\":[{\"id\":\"a\",\"label\":\"A\"}]}", RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(UiMethods.PanelCurrent, "{\"panel\":\"outline\",\"current\":7}", RpcErrorCodes.InvalidParams);
        Assert.Equal(3, rig.Ui.Currents.Count);
        Assert.Equal(2, rig.Ui.Panels.Count);

        // a panel that was removed has no current item to move
        await rig.CallAsync(UiMethods.PanelRemove, "{\"panel\":\"outline\"}");
        await rig.ErrorAsync(UiMethods.PanelCurrent, "{\"panel\":\"outline\",\"current\":\"a\"}", RpcErrorCodes.InvalidParams);
    }

    [Fact]
    public async Task A_plugin_has_one_status_item_that_it_sets_and_clears()
    {
        await using var rig = await Rig.CreateAsync();
        await rig.CallAsync(UiMethods.StatusSet, "{\"text\":\"JSON ok\",\"tooltip\":\"valid\"}");
        await rig.CallAsync(UiMethods.StatusSet, "{\"text\":\"JSON error\"}");
        await rig.CallAsync(UiMethods.StatusClear, "{}");
        Assert.Equal(["JSON ok", "JSON error", null], rig.Ui.Statuses.Select(s => s?.Text));
        Assert.Equal("valid", rig.Ui.Statuses[0]!.Tooltip);
    }

    [Fact]
    public async Task Quick_pick_input_and_confirm_return_what_the_user_decided_or_null()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Ui.Pick = r => r.Items[1].Id;
        var pick = await rig.CallAsync(UiMethods.QuickPick, "{\"title\":\"Indent\",\"items\":[{\"id\":\"2\",\"label\":\"2 spaces\"},{\"id\":\"4\",\"label\":\"4 spaces\",\"detail\":\"default\"}]}");
        Assert.Equal("4", pick.GetProperty("id").GetString());
        Assert.Equal("default", rig.Ui.Picks[0].Items[1].Detail);

        rig.Ui.Pick = _ => null;
        Assert.Equal(JsonValueKind.Null, (await rig.CallAsync(UiMethods.QuickPick, "{\"title\":\"x\",\"items\":[]}")).GetProperty("id").ValueKind);
        await rig.ErrorAsync(UiMethods.QuickPick, "{\"title\":\"x\",\"items\":[{\"id\":\"1\",\"label\":\"a\"},{\"id\":\"2\",\"label\":\"b\"},{\"id\":\"3\",\"label\":\"c\"},{\"id\":\"4\",\"label\":\"d\"}]}", UiErrorCodes.QuotaExceeded);
        await rig.ErrorAsync(UiMethods.QuickPick, "{\"title\":\"x\",\"items\":[{\"id\":\"1\",\"label\":\"a\"},{\"id\":\"1\",\"label\":\"b\"}]}", RpcErrorCodes.InvalidParams);

        rig.Ui.Input = r => r.Value + "!";
        Assert.Equal("seed!", (await rig.CallAsync(UiMethods.Input, "{\"title\":\"Name\",\"label\":\"Name:\",\"value\":\"seed\"}")).GetProperty("value").GetString());
        rig.Ui.Input = _ => null;
        Assert.Equal(JsonValueKind.Null, (await rig.CallAsync(UiMethods.Input, "{\"title\":\"Name\",\"label\":\"Name:\"}")).GetProperty("value").ValueKind);

        rig.Ui.Confirm = r => r.Message == "Really?";
        Assert.True((await rig.CallAsync(UiMethods.Confirm, "{\"title\":\"Q\",\"message\":\"Really?\"}")).GetProperty("confirmed").GetBoolean());
        Assert.False((await rig.CallAsync(UiMethods.Confirm, "{\"title\":\"Q\",\"message\":\"Sure?\"}")).GetProperty("confirmed").GetBoolean());
    }

    [Fact]
    public async Task Notifications_carry_a_level_and_an_unknown_level_is_refused()
    {
        await using var rig = await Rig.CreateAsync();
        await rig.CallAsync(UiMethods.Notify, "{\"level\":\"error\",\"message\":\"Broken\"}");
        await rig.CallAsync(UiMethods.Notify, "{\"message\":\"Plain\"}");
        await rig.ErrorAsync(UiMethods.Notify, "{\"level\":\"loud\",\"message\":\"x\"}", RpcErrorCodes.InvalidParams);
        Assert.Equal([(NoticeLevel.Error, "Broken"), (NoticeLevel.Info, "Plain")], rig.Ui.Notices);
    }

    [Fact]
    public async Task A_task_reports_progress_and_hears_about_cancel_once_and_only_when_it_allows_it()
    {
        await using var rig = await Rig.CreateAsync();
        var id = (await rig.CallAsync(UiMethods.TaskStart, "{\"title\":\"Indexing\",\"cancellable\":true}")).GetProperty("task").GetString()!;
        var fixedId = (await rig.CallAsync(UiMethods.TaskStart, "{\"title\":\"Saving\"}")).GetProperty("task").GetString()!;
        Assert.Equal([("Indexing", true), ("Saving", false)], rig.Ui.Started.Select(t => (t.Title, t.Cancellable)));
        await rig.ErrorAsync(UiMethods.TaskStart, "{\"title\":\"Third\"}", UiErrorCodes.QuotaExceeded);

        await rig.CallAsync(UiMethods.TaskProgress, $"{{\"task\":\"{id}\",\"fraction\":0.25,\"message\":\"a.json\"}}");
        await rig.CallAsync(UiMethods.TaskProgress, $"{{\"task\":\"{id}\"}}");
        await rig.ErrorAsync(UiMethods.TaskProgress, $"{{\"task\":\"{id}\",\"fraction\":1.5}}", RpcErrorCodes.InvalidParams);
        Assert.Equal([0.25, null], rig.Ui.Progress.Select(p => p.Progress.Fraction));
        await rig.ErrorAsync(UiMethods.TaskProgress, "{\"task\":\"unknown\"}", UiErrorCodes.UnknownTask);

        Assert.False(await rig.Api.CancelTaskAsync("p", fixedId));
        Assert.True(await rig.Api.CancelTaskAsync("p", id));
        Assert.False(await rig.Api.CancelTaskAsync("p", id));
        await rig.WaitEventAsync(UiEvents.TaskCancelled);
        Assert.Equal(id, (await rig.LastEventAsync(UiEvents.TaskCancelled)).GetProperty("payload").GetProperty("task").GetString());

        Assert.True((await rig.CallAsync(UiMethods.TaskEnd, $"{{\"task\":\"{id}\"}}")).GetProperty("ended").GetBoolean());
        Assert.False((await rig.CallAsync(UiMethods.TaskEnd, $"{{\"task\":\"{id}\"}}")).GetProperty("ended").GetBoolean());
        Assert.Equal([id], rig.Ui.Ended);
        await rig.CallAsync(UiMethods.TaskStart, "{\"title\":\"Room again\"}");
    }

    [Fact]
    public async Task A_cancelled_task_the_plugin_does_not_end_is_ended_by_the_editor_and_frees_its_slot()
    {
        await using var rig = await Rig.CreateAsync(cancelGrace: TimeSpan.FromMilliseconds(200));
        var id = (await rig.CallAsync(UiMethods.TaskStart, "{\"title\":\"Stuck\",\"cancellable\":true}")).GetProperty("task").GetString()!;
        await rig.CallAsync(UiMethods.TaskStart, "{\"title\":\"Other\"}");
        await rig.ErrorAsync(UiMethods.TaskStart, "{\"title\":\"Third\"}", UiErrorCodes.QuotaExceeded);

        // the test plugin only records the cancel event and never ends the task
        Assert.True(await rig.Api.CancelTaskAsync("p", id));
        await rig.WaitEventAsync(UiEvents.TaskCancelled);
        for (int i = 0; i < 500 && !rig.Ui.Ended.Contains(id); i++) await Task.Delay(10);

        Assert.Equal([id], rig.Ui.Ended);
        await rig.ErrorAsync(UiMethods.TaskProgress, $"{{\"task\":\"{id}\",\"fraction\":0.5}}", UiErrorCodes.UnknownTask);
        Assert.False((await rig.CallAsync(UiMethods.TaskEnd, $"{{\"task\":\"{id}\"}}")).GetProperty("ended").GetBoolean());
        await rig.CallAsync(UiMethods.TaskStart, "{\"title\":\"Room again\"}");
    }

    [Fact]
    public async Task A_task_the_plugin_ends_after_cancel_is_not_ended_a_second_time()
    {
        // the end has to reach the editor within the grace; a round trip to a plugin process on a busy machine takes longer than a few hundred milliseconds
        await using var rig = await Rig.CreateAsync(cancelGrace: TimeSpan.FromSeconds(3));
        var id = (await rig.CallAsync(UiMethods.TaskStart, "{\"title\":\"Polite\",\"cancellable\":true}")).GetProperty("task").GetString()!;
        Assert.True(await rig.Api.CancelTaskAsync("p", id));
        Assert.True((await rig.CallAsync(UiMethods.TaskEnd, $"{{\"task\":\"{id}\"}}")).GetProperty("ended").GetBoolean());
        await Task.Delay(3500);
        Assert.Equal([id], rig.Ui.Ended);
    }

    [Fact]
    public void A_cancel_grace_of_zero_is_refused()
    {
        var options = new UiApiOptions
        {
            MaxPanelsPerPlugin = 1, MaxItemsPerPanel = 1, MaxTreeDepth = 1, MaxTasksPerPlugin = 1, MaxTextLength = 1, MaxPickItems = 1, TaskCancelGrace = TimeSpan.Zero,
        };
        Assert.Throws<ArgumentException>(() => new UiApi(options, new RecordingUi(), (_, _, _) => Task.FromResult(true)));
    }

    [Fact]
    public async Task When_the_plugin_ends_everything_it_contributed_is_dropped()
    {
        await using var rig = await Rig.CreateAsync();
        var id = (await rig.CallAsync(UiMethods.TaskStart, "{\"title\":\"x\",\"cancellable\":true}")).GetProperty("task").GetString()!;
        await rig.CallAsync(UiMethods.PanelSet, "{\"panel\":\"x\",\"title\":\"T\",\"kind\":\"list\",\"items\":[{\"id\":\"a\",\"label\":\"A\"}]}");
        rig.Api.PluginEnded("p");
        Assert.Equal(["p"], rig.Ui.GonePlugins);
        Assert.False(await rig.Api.CancelTaskAsync("p", id));
        Assert.False(await rig.Api.ActivateItemAsync("p", "x", "a"));
    }
}
