using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P6.2.4 and P6.2.5 in the app: a real test plugin process shows a controlled preview that follows the revisions of a document, hosts a conversation in the agent panel (streamed chunks,
/// tools, the end of a turn, cancelling, the permission dialog), proposes a change that goes to the change review, and every request without its permission is refused.
/// </summary>
[Collection("LocState")]
public class PluginPanelsTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public PluginPanelsTests()
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
    private PluginPermissions Permissions => _app.Plugins.Permissions;

    private async Task StartPluginAsync()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.PluginHost.TestPlugin.dll");
        Assert.True(File.Exists(dll), dll);
        Host.Register(new PluginSpec("p", "Test plugin", Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", [dll], ["onCommand:p"]));
        await Pump(Host.ActivateAsync("onCommand:p"));
    }

    private Task<JsonElement> Call(string method, string paramsJson) =>
        Host.CallAsync("p", "callEditor", Encoding.UTF8.GetBytes($"{{\"method\":\"{method}\",\"params\":{paramsJson}}}")).ContinueWith(t => t.Result.Clone());

    private async Task<JsonElement> Editor(string method, string paramsJson) => await Pump(Call(method, paramsJson));

    private static int ErrorOf(JsonElement r) => r.ValueKind == JsonValueKind.Object && r.TryGetProperty("error", out var e) ? e.GetInt32() : 0;

    private async Task<List<JsonElement>> EventsAsync(string name)
    {
        var all = await Pump(Host.CallAsync("p", "events"));
        return [.. all.EnumerateArray().Where(e => e.GetProperty("name").GetString() == name).Select(e => e.Clone())];
    }

    private async Task UntilEventAsync(string name)
    {
        var sw = Stopwatch.StartNew();
        while ((await EventsAsync(name)).Count == 0)
        {
            Assert.True(sw.ElapsedMilliseconds < 30_000, "No event: " + name);
            await Task.Delay(20);
        }
    }

    private DocTab Shared(string text)
    {
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = text;
        _app.Plugins.Documents.Expose(tab.Doc.Doc, tab.Editor.ByteSelections);
        return tab;
    }

    private static string Show(DocTab tab, string text, string format = "markdown", string title = "Doc") =>
        $"{{\"document\":\"{tab.Doc.Doc.Id}\",\"revision\":{tab.Doc.Doc.Revision},\"format\":\"{format}\",\"title\":\"{title}\",\"text\":{JsonSerializer.Serialize(text)}}}";

    private static IEnumerable<Control> Tree(Control root) => new[] { root }.Concat(root.GetLogicalDescendants().OfType<Control>());

    private static string TextOf(Control root) => string.Join("\n", Tree(root).OfType<TextBlock>().Select(t =>
        t.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(r => r.Text)) : t.Text ?? ""));

    // ---- preview ----

    [AvaloniaFact]
    public async Task A_plugin_shows_a_preview_and_it_follows_the_revisions_of_the_document()
    {
        await StartPluginAsync();
        var tab = Shared("# Notes\n");
        Permissions.Grant("p", DocumentPermissions.Read, GrantScope.All);
        Permissions.Grant("p", DocumentPermissions.Preview, GrantScope.All);
        long first = tab.Doc.Doc.Revision;

        var shown = await Editor(DocumentMethods.PreviewShow, Show(tab, "# Notes\n\nSome **bold** and `code` text.\n\n- one\n- two\n\n```\nraw  code\n```\n"));
        Assert.Equal(first, shown.GetProperty("revision").GetInt64());
        Assert.True(_w.PreviewPanelOpen);
        Assert.Equal(Loc.Get(K.PreviewPanelStatus, ("title", "Doc"), ("revision", first)), _w.PreviewPanelStatus);
        var text = TextOf(_w.PreviewBody!);
        Assert.Contains("Notes", text);
        Assert.Contains("Some bold and code text.", text);
        Assert.Contains("• one", text);
        Assert.Contains("raw  code", text);

        // the document moves on: the panel says the preview is outdated, and a late preview for the old revision is refused
        tab.Editor!.Document.Replace(0, 1, "## ");
        long second = tab.Doc.Doc.Revision;
        Assert.True(second > first);
        Assert.Equal(Loc.Get(K.PreviewPanelOutdated, ("title", "Doc"), ("revision", first), ("current", second)), _w.PreviewPanelStatus);
        var late = await Editor(DocumentMethods.PreviewShow, $"{{\"document\":\"{tab.Doc.Doc.Id}\",\"revision\":{first},\"format\":\"text\",\"text\":\"old\"}}");
        Assert.Equal(DocumentErrorCodes.RevisionConflict, ErrorOf(late));
        Assert.DoesNotContain("old", TextOf(_w.PreviewBody!).Split('\n'));

        await Editor(DocumentMethods.PreviewShow, Show(tab, "Updated for the new revision"));
        Assert.Equal(Loc.Get(K.PreviewPanelStatus, ("title", "Doc"), ("revision", second)), _w.PreviewPanelStatus);
        Assert.Contains("Updated for the new revision", TextOf(_w.PreviewBody!));

        await Editor(DocumentMethods.PreviewClose, $"{{\"document\":\"{tab.Doc.Doc.Id}\"}}");
        Assert.False(_w.PreviewPanelOpen);
        Assert.Null(_w.ShownPreview);
    }

    [AvaloniaFact]
    public async Task A_preview_loads_nothing_a_script_stays_text_and_an_image_and_a_link_are_not_followed()
    {
        await StartPluginAsync();
        var tab = Shared("x");
        Permissions.Grant("p", DocumentPermissions.Read, GrantScope.All);
        Permissions.Grant("p", DocumentPermissions.Preview, GrantScope.All);

        await Editor(DocumentMethods.PreviewShow, Show(tab, "<script>alert(1)</script>\n\n![chart](https://remote.example/a.png)\n\n[notes](file:///C:/private/notes.txt) and snake_case_name\n"));
        var body = _w.PreviewBody!;
        var text = TextOf(body);
        Assert.Contains("<script>alert(1)</script>", text);
        Assert.Contains(Loc.Get(K.PreviewPanelImage, ("alt", "chart")), text);
        Assert.Contains(Loc.Get(K.PreviewPanelLink, ("text", "notes"), ("url", "file:///C:/private/notes.txt")), text);
        Assert.Contains("snake_case_name", text);
        Assert.DoesNotContain(Tree(body), c => c is Image or Button);

        // plain text is shown as it is, Markdown marks included
        await Editor(DocumentMethods.PreviewShow, Show(tab, "# not a heading", "text"));
        Assert.Contains("# not a heading", TextOf(_w.PreviewBody!));
    }

    private const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    [AvaloniaFact]
    public void The_policy_takes_an_image_only_from_a_small_data_URI_and_a_link_only_to_the_web_or_mail()
    {
        Assert.NotNull(PreviewPolicy.ImageBytes("data:image/png;base64," + OnePixelPng));
        Assert.Null(PreviewPolicy.ImageBytes("https://remote.example/a.png"));
        Assert.Null(PreviewPolicy.ImageBytes("file:///C:/private/a.png"));
        Assert.Null(PreviewPolicy.ImageBytes("data:image/svg+xml;base64," + OnePixelPng)); // svg can carry script and references
        Assert.Null(PreviewPolicy.ImageBytes("data:image/png,rawtext"));
        Assert.Null(PreviewPolicy.ImageBytes("data:image/png;base64,@@@@"));
        Assert.Null(PreviewPolicy.ImageBytes("data:image/png;base64," + new string('A', (PluginLimitsPlaceholders.PreviewImageMaxBytes / 3 + 2) * 4)));

        Assert.NotNull(PreviewPolicy.LinkTarget("https://docs.example.org/a"));
        Assert.NotNull(PreviewPolicy.LinkTarget("mailto:dev@example.org"));
        foreach (var refused in new[] { "file:///C:/private/notes.txt", "javascript:alert(1)", "notes.md", "\\\\server\\share\\a.txt", "ms-settings:network", "data:text/html,<script>1</script>" })
            Assert.Null(PreviewPolicy.LinkTarget(refused));
    }

    [AvaloniaFact]
    public async Task A_preview_draws_a_data_image_and_a_broken_or_remote_image_stays_a_note()
    {
        await StartPluginAsync();
        var tab = Shared("x");
        Permissions.Grant("p", DocumentPermissions.Read, GrantScope.All);
        Permissions.Grant("p", DocumentPermissions.Preview, GrantScope.All);

        await Editor(DocumentMethods.PreviewShow, Show(tab, $"![dot](data:image/png;base64,{OnePixelPng})\n\n![broken](data:image/png;base64,AAAA)\n\n![far](https://remote.example/a.png)\n"));
        var body = _w.PreviewBody!;
        var images = Tree(body).OfType<Image>().ToList();
        Assert.Single(images);
        Assert.Equal("dot", Avalonia.Automation.AutomationProperties.GetName(images[0]));
        var text = TextOf(body);
        Assert.Contains(Loc.Get(K.PreviewPanelImage, ("alt", "broken")), text);
        Assert.Contains(Loc.Get(K.PreviewPanelImage, ("alt", "far")), text);
    }

    [AvaloniaFact]
    public async Task A_web_link_in_a_preview_opens_only_after_the_user_confirms()
    {
        await StartPluginAsync();
        var tab = Shared("x");
        Permissions.Grant("p", DocumentPermissions.Read, GrantScope.All);
        Permissions.Grant("p", DocumentPermissions.Preview, GrantScope.All);
        var opened = new List<Uri>();
        _app.UriOpener = uri => { opened.Add(uri); return Task.FromResult(true); };

        await Editor(DocumentMethods.PreviewShow, Show(tab, "See [the manual](https://docs.example.org/a) or [local](file:///C:/private/notes.txt)."));
        var buttons = Tree(_w.PreviewBody!).OfType<Button>().ToList();
        var link = Assert.Single(buttons);

        _dialogs.Answers.Enqueue(1);
        link.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Until(() => _dialogs.Asked.Count == 1, "the question");
        Assert.Contains("https://docs.example.org/a", _dialogs.Asked[0].Message);
        Assert.Empty(opened);

        _dialogs.Answers.Enqueue(0);
        await _w.OpenPreviewLinkAsync(new Uri("https://docs.example.org/a"));
        Assert.Equal([new Uri("https://docs.example.org/a")], opened);

        // a target the policy never opens is not even asked about
        await _w.OpenPreviewLinkAsync(new Uri("file:///C:/private/notes.txt"));
        Assert.Single(opened);
        Assert.Equal(2, _dialogs.Asked.Count);
    }

    [AvaloniaFact]
    public async Task HTML_is_not_rendered_and_a_preview_without_its_permission_is_refused()
    {
        await StartPluginAsync();
        var tab = Shared("x");
        var denied = await Editor(DocumentMethods.PreviewShow, Show(tab, "hi"));
        Assert.Equal(DocumentErrorCodes.PermissionDenied, ErrorOf(denied));
        Permissions.Grant("p", DocumentPermissions.Preview, GrantScope.All); // may show, may not read this document
        denied = await Editor(DocumentMethods.PreviewShow, Show(tab, "hi"));
        Assert.Equal(DocumentErrorCodes.PermissionDenied, ErrorOf(denied));
        Assert.False(_w.PreviewPanelOpen);

        Permissions.Grant("p", DocumentPermissions.Read, GrantScope.All);
        Assert.Equal(RpcErrorCodes.InvalidParams, ErrorOf(await Editor(DocumentMethods.PreviewShow, Show(tab, "<p>hi</p>", "html"))));
        Assert.False(_w.PreviewPanelOpen);

        await Editor(DocumentMethods.PreviewShow, Show(tab, "hi"));
        Assert.True(_w.PreviewPanelOpen);
        Permissions.Revoke("p", DocumentPermissions.Preview, GrantScope.All); // taking the permission back takes the preview away
        await Until(() => !_w.PreviewPanelOpen, "the preview to close");
    }

    // ---- agent panel ----

    [AvaloniaFact]
    public async Task A_plugin_hosts_a_conversation_streams_chunks_into_the_panel_and_gets_the_messages_and_the_cancelling()
    {
        await StartPluginAsync();
        Permissions.Grant("p", DocumentPermissions.AgentPanel, GrantScope.All);

        await Editor(DocumentMethods.AgentOpen, "{\"name\":\"Plugin agent\"}");
        Assert.True(_w.AgentPanelOpen);
        Assert.Equal(Loc.Get(K.AgentStatusReady, ("agent", "Plugin agent")), _w.AgentPanelStatus);
        Assert.Equal(DocumentErrorCodes.AgentPanelBusy, ErrorOf(await Editor(DocumentMethods.AgentOpen, "{\"name\":\"Another\"}")));

        _w.AgentInputBox.Text = "hello";
        _w.SendAgentMessage();
        Assert.True(_app.Plugins.Acp.TurnRunning);
        await Until(() => _w.AgentPanelStatus == Loc.Get(K.AgentStatusWorking, ("agent", "Plugin agent")), "the working status");
        await UntilEventAsync(DocumentEvents.AgentMessage);
        Assert.Equal("hello", (await EventsAsync(DocumentEvents.AgentMessage))[0].GetProperty("payload").GetProperty("text").GetString());

        foreach (var chunk in new[] { "Hel", "lo ", "there" }) await Editor(DocumentMethods.AgentUpdate, $"{{\"kind\":\"message\",\"text\":\"{chunk}\"}}");
        await Editor(DocumentMethods.AgentUpdate, "{\"kind\":\"tool\",\"id\":\"t1\",\"title\":\"Read the file\",\"status\":\"in_progress\"}");
        await Editor(DocumentMethods.AgentUpdate, "{\"kind\":\"toolUpdate\",\"id\":\"t1\",\"status\":\"completed\"}");
        await Until(() => _w.AgentPanelText.Contains(Loc.Get(K.AgentLineTool, ("title", "Read the file"), ("status", Loc.Get(K.AgentToolCompleted)))), "the tool line");
        var panel = _w.AgentPanelText;
        Assert.Contains(Loc.Get(K.AgentLineYou, ("text", "hello")), panel);
        Assert.Contains(Loc.Get(K.AgentLineAgent, ("agent", "Plugin agent"), ("text", "Hello there")), panel);

        await Editor(DocumentMethods.AgentUpdate, "{\"kind\":\"turnEnd\",\"reason\":\"done\"}");
        Assert.False(_app.Plugins.Acp.TurnRunning);
        await Until(() => _w.AgentPanelStatus == Loc.Get(K.AgentStatusReady, ("agent", "Plugin agent")), "the ready status");

        _w.AgentInputBox.Text = "again";
        _w.SendAgentMessage();
        Assert.True(_app.Plugins.Acp.TurnRunning);
        _w.CancelAgentTurn();
        await UntilEventAsync(DocumentEvents.AgentCancel);
        await Editor(DocumentMethods.AgentUpdate, "{\"kind\":\"turnEnd\",\"reason\":\"cancelled\"}");
        Assert.False(_app.Plugins.Acp.TurnRunning);

        _ = _w.StopAgent();
        await _w.AgentTask;
        await UntilEventAsync(DocumentEvents.AgentClosed);
        Assert.Equal("user", (await EventsAsync(DocumentEvents.AgentClosed))[0].GetProperty("payload").GetProperty("reason").GetString());
        Assert.Equal(DocumentErrorCodes.NoAgentSession, ErrorOf(await Editor(DocumentMethods.AgentUpdate, "{\"kind\":\"message\",\"text\":\"late\"}")));
    }

    [AvaloniaFact]
    public async Task A_question_of_the_plugin_uses_the_permission_dialog_and_a_cancelled_dialog_is_cancelled()
    {
        await StartPluginAsync();
        Permissions.Grant("p", DocumentPermissions.AgentPanel, GrantScope.All);
        await Editor(DocumentMethods.AgentOpen, "{\"name\":\"Plugin agent\"}");
        const string ask = "{\"tool\":\"Run the build\",\"options\":[{\"id\":\"yes\",\"name\":\"Allow once\"},{\"id\":\"no\",\"name\":\"Reject\",\"reject\":true}]}";

        _dialogs.Answers.Enqueue(0);
        Assert.Equal("yes", (await Editor(DocumentMethods.AgentPermission, ask)).GetProperty("optionId").GetString());
        var asked = Assert.Single(_dialogs.Asked);
        Assert.Contains("Run the build", asked.Message);
        Assert.Equal(["Allow once", "Reject", Loc.Get(K.DialogCancel)], asked.Buttons.Select(b => b.Label));

        _dialogs.Answers.Enqueue(2); // the Cancel button
        Assert.True((await Editor(DocumentMethods.AgentPermission, ask)).GetProperty("cancelled").GetBoolean());
    }

    [AvaloniaFact]
    public async Task A_change_the_plugin_proposes_goes_to_the_change_review_and_reaches_the_document_only_when_applied()
    {
        await StartPluginAsync();
        Permissions.Grant("p", DocumentPermissions.AgentPanel, GrantScope.All);
        Permissions.Grant("p", DocumentPermissions.Read, GrantScope.All);
        Permissions.Grant("p", DocumentPermissions.EditPropose, GrantScope.All);
        var tab = Shared("alpha\nbeta\n");
        await Editor(DocumentMethods.AgentOpen, "{\"name\":\"Plugin agent\"}");

        var call = Call(DocumentMethods.EditPropose, $"{{\"document\":\"{tab.Doc.Doc.Id}\",\"revision\":{tab.Doc.Doc.Revision},\"title\":\"From the agent\",\"edits\":[{{\"start\":0,\"end\":5,\"text\":\"ALPHA\"}}]}}");
        await Until(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true, "the review");
        Assert.Equal("alpha\nbeta\n", tab.Editor!.Document.Text);
        Assert.False(call.IsCompleted);

        _w.ApplyPlan();
        var decision = await Pump(call);
        Assert.Equal("applied", decision.GetProperty("decision").GetString());
        Assert.Equal("ALPHA\nbeta\n", tab.Editor.Document.Text);
    }

    [AvaloniaFact]
    public async Task Every_request_without_its_permission_is_refused_and_taking_it_back_ends_the_conversation()
    {
        await StartPluginAsync();
        foreach (var (method, args) in new[]
        {
            (DocumentMethods.AgentOpen, "{\"name\":\"Plugin agent\"}"),
            (DocumentMethods.AgentUpdate, "{\"kind\":\"message\",\"text\":\"x\"}"),
            (DocumentMethods.AgentPermission, "{\"tool\":\"t\",\"options\":[{\"id\":\"a\",\"name\":\"A\"}]}"),
            (DocumentMethods.AgentClose, "{}"),
        })
            Assert.Equal(DocumentErrorCodes.PermissionDenied, ErrorOf(await Editor(method, args)));
        Assert.False(_w.AgentPanelOpen);
        Assert.Empty(_dialogs.Asked);

        Permissions.Grant("p", DocumentPermissions.AgentPanel, GrantScope.All);
        await Editor(DocumentMethods.AgentOpen, "{\"name\":\"Plugin agent\"}");
        Assert.True(_app.Plugins.Acp.HasSession("p"));
        Permissions.Revoke("p", DocumentPermissions.AgentPanel, GrantScope.All);
        await Until(() => !_app.Plugins.Acp.IsRunning, "the conversation to end");
        Assert.Equal(DocumentErrorCodes.PermissionDenied, ErrorOf(await Editor(DocumentMethods.AgentUpdate, "{\"kind\":\"message\",\"text\":\"x\"}")));
    }
}
