using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.App.Tests;

/// <summary>The limits the product decided (snapshots, diagnostics, agent panel messages) as the real document API of the app enforces them for a plugin.</summary>
[Collection("LocState")]
public class DecidedLimitsTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public DecidedLimitsTests()
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

    private static async Task<T> Pump<T>(Task<T> task)
    {
        var sw = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            Assert.True(sw.ElapsedMilliseconds < 60_000, "A call did not finish.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        return await task;
    }

    private async Task<JsonElement> CallAsync(string plugin, string method, string json)
    {
        using var p = JsonDocument.Parse(json);
        var answer = await Pump(_app.Plugins.Documents.HandleAsync(plugin, method, p.RootElement, CancellationToken.None).AsTask());
        return JsonDocument.Parse(answer!).RootElement.Clone();
    }

    private async Task<RpcException> RefusedAsync(string plugin, string method, string json)
    {
        try { await CallAsync(plugin, method, json); }
        catch (RpcException e) { return e; }
        throw new Xunit.Sdk.XunitException("The call was not refused.");
    }

    private DocTab Exposed(string text, string plugin)
    {
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = text;
        _app.Plugins.Documents.Expose(tab.Doc.Doc, null);
        foreach (var permission in new[] { DocumentPermissions.Read, DocumentPermissions.EditPropose })
            _app.Plugins.Permissions.Grant(plugin, permission, GrantScope.All);
        return tab;
    }

    [AvaloniaFact]
    public async Task A_plugin_holds_four_snapshots_for_thirty_seconds_and_the_fifth_is_refused()
    {
        var tab = Exposed("hello\n", "snap");
        string id = tab.Doc.Doc.Id.ToString();
        var first = await CallAsync("snap", DocumentMethods.SnapshotCreate, $"{{\"document\":\"{id}\"}}");
        Assert.Equal(30_000, first.GetProperty("lifetimeMs").GetInt64());
        for (int i = 1; i < PluginLimits.SnapshotsPerPlugin; i++) await CallAsync("snap", DocumentMethods.SnapshotCreate, $"{{\"document\":\"{id}\"}}");

        var refused = await RefusedAsync("snap", DocumentMethods.SnapshotCreate, $"{{\"document\":\"{id}\"}}");

        Assert.Equal(DocumentErrorCodes.SnapshotQuotaExceeded, refused.Code);
    }

    [AvaloniaFact]
    public async Task A_snapshot_of_a_document_over_sixty_four_mebibytes_is_refused_with_the_quota_of_bytes()
    {
        Assert.Equal(64L << 20, PluginLimits.SnapshotBytesPerPlugin);
        var tab = Exposed("", "bytes");
        tab.Doc.Doc.Insert(0, new byte[(int)(PluginLimits.SnapshotBytesPerPlugin + (1 << 20))].Select(_ => (byte)'a').ToArray());
        var refused = await RefusedAsync("bytes", DocumentMethods.SnapshotCreate, $"{{\"document\":\"{tab.Doc.Doc.Id}\"}}");
        Assert.Equal(DocumentErrorCodes.SnapshotQuotaExceeded, refused.Code);
    }

    [AvaloniaFact]
    public async Task One_source_may_publish_a_thousand_diagnostics_for_a_document_and_not_one_more()
    {
        var tab = Exposed("x\n", "diag");
        string id = tab.Doc.Doc.Id.ToString();
        long revision = tab.Doc.Doc.Revision;
        string Many(int count) => "[" + string.Join(",", Enumerable.Repeat("{\"severity\":\"hint\",\"message\":\"m\",\"start\":0,\"end\":1}", count)) + "]";
        string Publish(int count) => $"{{\"document\":\"{id}\",\"revision\":{revision},\"source\":\"s\",\"diagnostics\":{Many(count)}}}";

        var accepted = await CallAsync("diag", DocumentMethods.DiagnosticsPublish, Publish(PluginLimits.DiagnosticsPerDocument));
        Assert.Equal(PluginLimits.DiagnosticsPerDocument, accepted.GetProperty("accepted").GetInt32());
        var refused = await RefusedAsync("diag", DocumentMethods.DiagnosticsPublish, Publish(PluginLimits.DiagnosticsPerDocument + 1));
        Assert.Equal(DocumentErrorCodes.DiagnosticsQuotaExceeded, refused.Code);
    }

    [AvaloniaFact]
    public async Task A_message_to_the_agent_panel_may_have_sixty_four_kibi_characters()
    {
        Exposed("x", "agent");
        _app.Plugins.Permissions.Grant("agent", DocumentPermissions.AgentPanel, GrantScope.All);
        var refused = await RefusedAsync("agent", DocumentMethods.AgentUpdate, "{\"kind\":\"message\",\"text\":\"" + new string('a', PluginLimits.AgentUpdateMaxChars + 1) + "\"}");
        Assert.Equal(RpcErrorCodes.MessageTooLarge, refused.Code);
    }

    [AvaloniaFact]
    public void The_decided_values_of_the_timeouts_waits_and_caps_are_the_ones_the_reasons_name()
    {
        Assert.Equal((30, 300, 512 << 10), (PluginLimits.AcpStartTimeoutSeconds, PluginLimits.AcpIdleTimeoutSeconds, PluginLimits.AcpMaxReadBytes));
        Assert.Equal((300, 300, 1000, 200), (PluginLimits.LspDecorationRefreshMilliseconds, PluginLimits.PreviewLiveDebounceMs, PluginLimits.CompletionTimeoutMilliseconds, PluginLimits.CompletionItemsPerPlugin));
        var acp = PluginLimits.Acp();
        Assert.Equal(TimeSpan.FromSeconds(30), acp.InitializeTimeout);
        Assert.Equal(TimeSpan.FromSeconds(300), acp.IdleTimeout);
    }
}
