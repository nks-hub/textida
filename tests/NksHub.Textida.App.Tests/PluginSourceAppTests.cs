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
/// P6.2.1 and P6.2.3 in the app: a test plugin process serves a fake scheme from memory; the window opens its addresses in tabs, shows where they come from, saves against the
/// revision it read, raises the conflict dialog when the source moved on, warns before a write that is not atomic, opens the cached copy offline and masks secrets of an address.
/// </summary>
[Collection("LocState")]
public class PluginSourceAppTests : IDisposable
{
    private const string Page = "fake://wiki/page";

    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public PluginSourceAppTests()
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

    private async Task StartPluginAsync(bool atomic = true)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.PluginHost.TestPlugin.dll");
        Host.Register(new PluginSpec("p", "Test plugin", Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", [dll], ["onCommand:p"]));
        await Pump(Host.ActivateAsync("onCommand:p"));
        _app.Plugins.Permissions.Grant("p", DocumentPermissions.Source, GrantScope.All);
        var json = $"{{\"method\":\"{DocumentMethods.SourceRegister}\",\"params\":{{\"scheme\":\"fake\",\"name\":\"Fake wiki\",\"list\":true,\"write\":true,\"atomicWrite\":{(atomic ? "true" : "false")}}}}}";
        var answer = await Pump(CallPlugin("callEditor", json));
        Assert.True(answer.GetProperty("enabled").GetBoolean(), answer.GetRawText());
        await Pump(CallPlugin("sourceMode", $"{{\"atomic\":{(atomic ? "true" : "false")}}}"));
    }

    private async Task<JsonElement> CallPlugin(string method, string json) => (await Host.CallAsync("p", method, Encoding.UTF8.GetBytes(json))).Clone();

    private Task Put(string uri, string text) => Pump(CallPlugin("sourcePut", JsonSerializer.Serialize(new Dictionary<string, string> { ["uri"] = uri, ["text"] = text })));

    private async Task<string> Remote(string uri) =>
        (await Pump(CallPlugin("sourceGet", JsonSerializer.Serialize(new Dictionary<string, string> { ["uri"] = uri })))).GetProperty("text").GetString()!;

    private async Task<DocTab> OpenAsync(string uri) => (await Pump(_w.OpenSourceAsync(uri)))!;

    private static string? BarOf(DocTab tab) => (tab.Bar as InfoBar)?.Message;

    [AvaloniaFact]
    public async Task A_source_address_opens_in_a_tab_that_says_where_it_comes_from_and_a_save_with_the_matching_revision_writes()
    {
        await StartPluginAsync();
        await Put(Page, "one\n");

        var tab = await OpenAsync(Page);

        Assert.Equal("page", tab.Title);
        Assert.Equal("one\n", tab.Editor!.Document.Text);
        Assert.Null(tab.Path);
        Assert.Contains("Fake wiki", BarOf(tab));
        Assert.Contains(Page, BarOf(tab));
        Assert.Contains("r1", BarOf(tab));
        Assert.Contains(Loc.Get(K.SourceWriteAtomic), BarOf(tab)); // what the source can do is shown
        Assert.Contains(_app.Output.Read(OutputChannel.Remote), l => l.Text.Contains("r1"));

        tab.Editor.Document.Insert(0, "edited ");
        Assert.True(await Pump(_w.SaveAsync(tab)));

        Assert.Equal("edited one\n", await Remote(Page));
        Assert.False(tab.Doc.Doc.IsModified);
        Assert.Equal("r2", tab.Doc.Source!.Revision);
        Assert.Contains("r2", BarOf(tab));
        Assert.Empty(_dialogs.Asked);
    }

    [AvaloniaFact]
    public async Task A_save_after_a_remote_change_raises_the_conflict_dialog_keeps_the_local_text_and_writes_nothing()
    {
        await StartPluginAsync();
        await Put(Page, "line one\n");
        var tab = await OpenAsync(Page);
        tab.Editor!.Document.Insert(0, "mine\n");
        await Put(Page, "line one changed by a colleague\n");

        _dialogs.Answers.Enqueue(2); // Cancel
        Assert.False(await Pump(_w.SaveAsync(tab)));

        var asked = _dialogs.Asked.Single();
        Assert.Equal(Loc.Get(K.SourceConflictTitle), asked.Title);
        Assert.Equal(Loc.Get(K.SourceConflictMessage, ("title", "page"), ("name", "Fake wiki")), asked.Message);
        Assert.Equal("line one changed by a colleague\n", await Remote(Page));
        Assert.Equal("mine\nline one\n", tab.Editor.Document.Text);
        Assert.True(tab.Doc.Doc.IsModified);

        // the differences open in the compare tab and the next save replaces exactly the version that was shown
        _dialogs.Answers.Enqueue(0);
        Assert.False(await Pump(_w.SaveAsync(tab)));
        Assert.NotNull(_w.ActiveCompare);
        Assert.Equal(Loc.Get(K.SourceConflictShown), _w.StatusMessageText);
        Assert.Equal("line one changed by a colleague\n", await Remote(Page));
        Assert.True(await Pump(_w.SaveAsync(tab)));
        Assert.Equal("mine\nline one\n", await Remote(Page));
        Assert.Equal(2, _dialogs.Asked.Count);
    }

    [AvaloniaFact]
    public async Task A_source_without_an_atomic_write_warns_before_every_write_and_a_refusal_writes_nothing()
    {
        await StartPluginAsync(atomic: false);
        await Put(Page, "one");
        var tab = await OpenAsync(Page);
        Assert.Contains(Loc.Get(K.SourceWritePlain), BarOf(tab));
        tab.Editor!.Document.Insert(0, "x");

        _dialogs.Answers.Enqueue(1); // Cancel
        Assert.False(await Pump(_w.SaveAsync(tab)));
        Assert.Equal(Loc.Get(K.SourcePlainTitle), _dialogs.Asked.Single().Title);
        Assert.Equal("one", await Remote(Page));

        _dialogs.Answers.Enqueue(0); // Write anyway
        Assert.True(await Pump(_w.SaveAsync(tab)));
        Assert.Equal("xone", await Remote(Page));
        Assert.Equal(2, _dialogs.Asked.Count);
    }

    [AvaloniaFact]
    public async Task Without_a_connection_the_tab_opens_from_the_cached_copy_marked_offline()
    {
        await StartPluginAsync();
        await Put(Page, "cached text");
        await _w.CloseTabAsync(await OpenAsync(Page));
        await Pump(CallPlugin("sourceMode", "{\"offline\":true}"));

        var tab = await OpenAsync(Page);

        Assert.Equal("cached text", tab.Editor!.Document.Text);
        Assert.True(tab.Doc.Source!.FromCache);
        Assert.StartsWith("Offline", BarOf(tab));
        Assert.Contains(_app.Output.Read(OutputChannel.Remote), l => l.Text == Loc.Get(K.SourceLogCached, ("uri", Page), ("revision", "r1")));
    }

    [AvaloniaFact]
    public async Task The_secrets_of_an_address_are_masked_in_the_title_the_bar_the_recent_files_and_the_log()
    {
        await StartPluginAsync();
        const string uri = "fake://alice:hunter2@wiki/docs/secret-page?token=TOP-SECRET-123&view=1";
        await Put(uri, "text");

        var tab = await OpenAsync(uri);
        _w.SetStatusMessage(Loc.Get(K.SourceOpened, ("uri", tab.Doc.Source!.MaskedUri), ("name", "x")));

        string[] shown = [tab.Title, BarOf(tab)!, _w.StatusMessageText, .. _app.Recent.Files, .. _app.Output.Read(OutputChannel.Remote).Select(l => l.Source + " " + l.Text)];
        foreach (var text in shown)
        {
            Assert.DoesNotContain("TOP-SECRET-123", text);
            Assert.DoesNotContain("hunter2", text);
        }
        Assert.Equal("secret-page", tab.Title);
        Assert.Contains("fake://alice:***@wiki/docs/secret-page?token=***&view=1", _app.Recent.Files);
        Assert.Contains("token=***", BarOf(tab));
        // the plugin still got the whole address to read with
        Assert.Equal("text", tab.Editor!.Document.Text);
        foreach (var file in Directory.EnumerateFiles(_app.ProfileDirectory, "*", SearchOption.AllDirectories))
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
                Assert.True(bytes.AsSpan().IndexOf("TOP-SECRET-123"u8) < 0, file);
                Assert.True(bytes.AsSpan().IndexOf("hunter2"u8) < 0, file);
            }
            catch (IOException) { /* a locked journal file keeps no addresses */ }
        }
    }

    [AvaloniaFact]
    public async Task The_open_command_lists_the_sources_with_what_they_can_do_and_opens_the_typed_address()
    {
        await StartPluginAsync(atomic: false);
        await Put(Page, "listed");
        _dialogs.Prompts.Enqueue(Page);

        _w.OpenFromSource();
        await Until(() => _w.QuickResults.Count > 0, "the list of sources");
        var row = _w.QuickResults.Single(r => r.Title == "Fake wiki"); // the built-in wiki source may be listed beside it
        Assert.Equal("Fake wiki", row.Title);
        Assert.Contains(Loc.Get(K.SourceWritePlain), row.Detail);
        row.Run();
        await Until(() => _w.Tabs.Any(t => t.Doc.Source is not null), "the tab");
        Assert.Equal("listed", _w.Tabs.Single(t => t.Doc.Source is not null).Editor!.Document.Text);
    }
}
