using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P3b.3.4 through the running application: the real plugin host of the app, a real test plugin process, a document of a tab. A plugin that handles events slowly gets one
/// <c>document/eventsLost</c> with the current revision instead of a backlog, and a plugin that starts again learns the revisions of the documents it may read.
/// </summary>
[Collection("LocState")]
public class PluginDocumentEventsAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public PluginDocumentEventsAppTests()
    {
        _app = TestApp.Prepare(_temp, new ScriptedDialogs());
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static async Task Until(Func<bool> condition, string what)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < 60_000, "Not reached: " + what);
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

    private async Task<DocTab> StartPluginWithSharedDocumentAsync(string text)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.PluginHost.TestPlugin.dll");
        Assert.True(File.Exists(dll), dll);
        Host.Register(new PluginSpec("p", "Test plugin", Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", [dll], ["onCommand:p"]));
        await Pump(Host.ActivateAsync("onCommand:p"));
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = text;
        _app.Plugins.Documents.Expose(tab.Doc.Doc, tab.Editor.ByteSelections);
        _app.Plugins.Permissions.Grant("p", DocumentPermissions.Read, GrantScope.All);
        await SubscribeAsync(tab);
        return tab;
    }

    private async Task SubscribeAsync(DocTab tab)
    {
        var subscribed = await Pump(Host.CallAsync("p", "callEditor", Encoding.UTF8.GetBytes($"{{\"method\":\"{DocumentMethods.Subscribe}\",\"params\":{{\"document\":\"{tab.Doc.Doc.Id}\"}}}}")));
        Assert.False(subscribed.TryGetProperty("error", out _), subscribed.GetRawText());
    }

    private async Task<List<JsonElement>> EventsAsync(string name)
    {
        var all = await Pump(Host.CallAsync("p", "events"));
        return [.. all.EnumerateArray().Where(e => e.GetProperty("name").GetString() == name).Select(e => e.Clone())];
    }

    private async Task UntilEventAsync(string name, Func<JsonElement, bool> match)
    {
        var sw = Stopwatch.StartNew();
        while (!(await EventsAsync(name)).Any(match))
        {
            Assert.True(sw.ElapsedMilliseconds < 60_000, "No event: " + name);
            await Task.Delay(20);
        }
    }

    [AvaloniaFact]
    public async Task A_plugin_that_handles_events_slowly_gets_one_eventsLost_with_the_current_revision_and_no_backlog_beyond_the_limits()
    {
        var tab = await StartPluginWithSharedDocumentAsync("x");
        // the plugin sits 3 s on its first event, so nothing is acknowledged while the edits are made
        await Pump(Host.CallAsync("p", "setEventDelay", Encoding.UTF8.GetBytes("{\"ms\":3000}")));

        int edits = PluginLimitsPlaceholders.EventQueueCapacity + PluginLimitsPlaceholders.EventWindow + 100;
        for (int i = 0; i < edits; i++) tab.Editor!.Document.Insert(0, "a");
        long current = tab.Doc.Doc.Revision;
        await Pump(Host.CallAsync("p", "setEventDelay", Encoding.UTF8.GetBytes("{\"ms\":0}")));

        await UntilEventAsync(DocumentEvents.EventsLost, _ => true);
        var lost = Assert.Single(await EventsAsync(DocumentEvents.EventsLost));
        Assert.Equal(current, lost.GetProperty("payload").GetProperty("revision").GetInt64());
        Assert.Equal(tab.Doc.Doc.Id.ToString(), lost.GetProperty("payload").GetProperty("document").GetString());

        var changed = await EventsAsync(DocumentEvents.Changed);
        // nothing is acknowledged while the edits are made: the plugin has EventWindow events, one more waits at the window and the queue holds the rest
        Assert.InRange(changed.Count, PluginLimitsPlaceholders.EventQueueCapacity, PluginLimitsPlaceholders.EventQueueCapacity + PluginLimitsPlaceholders.EventWindow + 1);
        Assert.Equal(Enumerable.Range(0, changed.Count).Select(i => current - edits + 1 + i), changed.Select(e => e.GetProperty("payload").GetProperty("revision").GetInt64()));
        Assert.DoesNotContain("\"a\"", string.Concat(changed.Select(e => e.GetRawText())));
    }

    [AvaloniaFact]
    public async Task A_plugin_that_starts_again_learns_the_current_revision_of_the_documents_it_may_read_and_still_follows_them()
    {
        var tab = await StartPluginWithSharedDocumentAsync("hello");
        var hidden = _w.NewDocument();
        hidden.Editor!.Document.Text = "other";
        _app.Plugins.Documents.Expose(hidden.Doc.Doc, hidden.Editor.ByteSelections);
        _app.Plugins.Permissions.Revoke("p", DocumentPermissions.Read, GrantScope.All);
        _app.Plugins.Permissions.Grant("p", DocumentPermissions.Read, GrantScope.OfDocument(tab.Doc.Doc.Id));
        await SubscribeAsync(tab); // taking the grant for all documents away ended the first subscription

        await Assert.ThrowsAsync<PluginCrashedException>(() => Pump(Host.CallAsync("p", "crash", Encoding.UTF8.GetBytes("{\"code\":3}"))));
        await Until(() => Host.GetState("p") == PluginState.WaitingRestart, "the plugin waits for its restart");
        tab.Editor!.Document.Insert(0, "a");
        tab.Editor.Document.Insert(0, "b");
        long current = tab.Doc.Doc.Revision;
        await Until(() => Host.GetState("p") == PluginState.Running, "the restarted plugin");

        var resync = await Pump(Host.CallAsync("p", "getResync"));
        Assert.True(resync.GetProperty("restarted").GetBoolean());
        var documents = resync.GetProperty("state").GetProperty("documents").EnumerateArray().ToList();
        var mine = Assert.Single(documents);
        Assert.Equal(tab.Doc.Doc.Id.ToString(), mine.GetProperty("id").GetString());
        Assert.Equal(current, mine.GetProperty("revision").GetInt64());
        Assert.True(mine.GetProperty("subscribed").GetBoolean());

        tab.Editor.Document.Insert(0, "c");
        await UntilEventAsync(DocumentEvents.Changed, e => e.GetProperty("payload").GetProperty("revision").GetInt64() == current + 1);
    }
}
