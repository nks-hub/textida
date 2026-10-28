using System.Reflection;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Plugin.Testing;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugin.Tests.Contracts;

/// <summary>
/// P8b.2.2, interface <b>ui</b> (panels, status, prompts, notices, tasks). The UI API has no document revision and needs no permission: it is limited by quotas. So the revision scenario
/// is the nearest thing the API has, a handle that is out of date (a task that ended), and the permission scenario shows that no grant is needed and what refuses a plugin instead (its quota).
/// </summary>
public class UiContractTests
{
    [Fact]
    public async Task Positive_a_plugin_without_any_grant_shows_a_panel_a_notice_a_status_and_asks_the_user()
    {
        await using var editor = await T.RawEditor();
        editor.Ui.Pick = request => request.Items[1].Id;
        editor.Ui.Input = request => request.Value + "!";
        editor.Ui.Confirm = request => request.Message == "Sure?";
        var plugin = editor.Raw!;

        var set = await plugin.RequestAsync(UiMethods.PanelSet, """{"panel":"outline","title":"Outline","kind":"tree","items":[{"id":"a","label":"root","children":[{"id":"b","label":"child"}]}]}""");
        await plugin.RequestAsync(UiMethods.Notify, """{"level":"warning","message":"Careful"}""");
        await plugin.RequestAsync(UiMethods.StatusSet, """{"text":"JSON ok"}""");
        var pick = await plugin.RequestAsync(UiMethods.QuickPick, """{"title":"Indent","items":[{"id":"2","label":"2"},{"id":"4","label":"4"}]}""");
        var input = await plugin.RequestAsync(UiMethods.Input, """{"title":"Name","label":"Name:","value":"seed"}""");
        var confirm = await plugin.RequestAsync(UiMethods.Confirm, """{"title":"Q","message":"Sure?"}""");

        Assert.Equal(2, set.GetProperty("items").GetInt32());
        Assert.Equal("child", editor.Ui.Panels.Single().Items[0].Children[0].Label);
        Assert.Equal([(NoticeLevel.Warning, "Careful")], editor.Ui.Notices);
        Assert.Equal("JSON ok", editor.Ui.Statuses.Single()!.Text);
        Assert.Equal(("4", "seed!", true), (pick.GetProperty("id").GetString(), input.GetProperty("value").GetString(), confirm.GetProperty("confirmed").GetBoolean()));
    }

    [Fact]
    public async Task Positive_a_task_reports_progress_and_the_cancel_of_the_user_reaches_the_plugin_as_an_event()
    {
        await using var editor = await T.RawEditor();
        var plugin = editor.Raw!;
        var id = (await plugin.RequestAsync(UiMethods.TaskStart, """{"title":"Indexing","cancellable":true}""")).GetProperty("task").GetString()!;

        await plugin.RequestAsync(UiMethods.TaskProgress, $$"""{"task":"{{id}}","fraction":0.5,"message":"half"}""");
        Assert.True(await editor.UiApi.CancelTaskAsync(editor.PluginId, id));
        var cancelled = await plugin.WaitForEventAsync(UiEvents.TaskCancelled);
        var ended = await plugin.RequestAsync(UiMethods.TaskEnd, $$"""{"task":"{{id}}"}""");

        Assert.Equal([("Indexing", true)], editor.Ui.TasksStarted.Select(t => (t.Title, t.Cancellable)));
        Assert.Equal([0.5], editor.Ui.Progress.Select(p => p.Progress.Fraction));
        Assert.Contains(id, cancelled.PayloadJson);
        Assert.True(ended.GetProperty("ended").GetBoolean());
        Assert.Equal([id], editor.Ui.TasksEnded);
    }

    [Fact]
    public async Task Positive_the_sdk_task_helper_starts_reports_and_ends_a_task_on_the_real_ui_api()
    {
        await using var editor = await TestEditor.StartAsync(new TaskPlugin());

        var reply = await editor.ExecuteCommandAsync("task.run");

        Assert.Equal("done", reply.GetProperty("status").GetString());
        Assert.Equal([("Work", true)], editor.Ui.TasksStarted.Select(t => (t.Title, t.Cancellable)));
        Assert.Equal([0.25], editor.Ui.Progress.Select(p => p.Progress.Fraction));
        Assert.Single(editor.Ui.TasksEnded);
    }

    [Fact]
    public async Task Revision_conflict_there_is_no_document_revision_in_the_ui_api_the_stale_thing_is_a_task_handle()
    {
        await using var editor = await T.RawEditor();
        var plugin = editor.Raw!;
        var id = (await plugin.RequestAsync(UiMethods.TaskStart, """{"title":"Once"}""")).GetProperty("task").GetString()!;
        await plugin.RequestAsync(UiMethods.TaskEnd, $$"""{"task":"{{id}}"}""");

        var progress = await T.ErrorOf(() => plugin.RequestAsync(UiMethods.TaskProgress, $$"""{"task":"{{id}}","fraction":0.9}"""));

        Assert.Equal(UiErrorCodes.UnknownTask, progress.Code);
        Assert.False((await plugin.RequestAsync(UiMethods.TaskEnd, $$"""{"task":"{{id}}"}""")).GetProperty("ended").GetBoolean());
    }

    [Fact]
    public async Task Permission_the_ui_api_needs_none_and_what_refuses_a_plugin_is_its_quota()
    {
        await using var editor = await T.RawEditor(new TestEditorOptions { TuneUi = o => o with { MaxPanelsPerPlugin = 1 } });
        var plugin = editor.Raw!;
        foreach (var method in Methods(typeof(UiMethods))) Assert.Null(PluginMethodPermissions.For(method));

        await plugin.RequestAsync(UiMethods.PanelSet, """{"panel":"one","title":"One","kind":"list","items":[]}""");
        var second = await T.ErrorOf(() => plugin.RequestAsync(UiMethods.PanelSet, """{"panel":"two","title":"Two","kind":"list","items":[]}"""));

        Assert.Equal(UiErrorCodes.QuotaExceeded, second.Code);
        Assert.DoesNotContain(editor.Calls, c => c.ErrorCode == DocumentErrorCodes.PermissionDenied);
    }

    private static IEnumerable<string> Methods(Type constants) =>
        constants.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!);

    private sealed class TaskPlugin : PluginBase
    {
        public TaskPlugin() : base("task", PluginApi.Version) =>
            OnCommand("task.run", async (_, ct) =>
            {
                await using var task = await StartTaskAsync("Work", ct);
                await task.ReportAsync(0.25, "a quarter");
                return new CommandResult("done");
            });
    }
}
