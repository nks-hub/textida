using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Remote;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// PR.6.4, second mode: after the user allowed something, exactly that host is contacted and no other. Everything goes to the proxy of the network policy, which is a sinkhole, so the host
/// of each request is what the editor asked its proxy for; a connect anywhere else, or a name sent to the resolver, fails the test as in the first mode.
/// </summary>
[Collection("LocState")]
[Trait("Category", "Network")]
public class NetworkConsentTests
{
    /// <summary>Everything the editor did went to the sinkhole and to the hosts the test expected, and only to them.</summary>
    private static async Task AssertOnlyAsync(NetworkRig rig, params string[] hosts)
    {
        rig.Net.AllowLoopback(rig.Net.Sinkhole.Port, "network proxy of the policy");
        var report = await rig.Net.FinishAsync();
        Assert.True(report.Forbidden.Count == 0, "Attempts outside the proxy:\n" + report);
        Assert.Equal(hosts.Order(StringComparer.Ordinal), report.Hosts);
        Assert.Equal(hosts.Order(StringComparer.Ordinal), rig.Net.Sinkhole.Targets.Distinct().Order(StringComparer.Ordinal));
    }

    [AvaloniaFact]
    public async Task A_schema_is_downloaded_from_the_host_the_user_allowed_and_from_no_other()
    {
        using var rig = new NetworkRig();
        async Task<DocTab> OpenCheckedAsync(string name, string content)
        {
            var tab = rig.Window.OpenFile(rig.ProjectFile(name, content), null);
            await rig.Until(() => rig.App.Plugins.SchemaResultOf(tab) is not null, "the first schema check of " + name); // only the tab on screen is checked
            return tab;
        }
        var allowed = await OpenCheckedAsync("data.json", "{ \"$schema\": \"https://schemas.example.test/data.json\", \"a\": 1 }\n");
        var other = await OpenCheckedAsync("config.yaml", "# yaml-language-server: $schema=https://other.example.test/config.json\nname: demo\n");
        var catalog = await OpenCheckedAsync("tsconfig.json", "{ \"compilerOptions\": {} }\n");
        Assert.Empty(rig.Net.Sinkhole.Targets);

        rig.Dialogs.Answers.Enqueue(0);
        await rig.Window.SchemaAllowDownloadAsync(allowed);

        var asked = Assert.Single(rig.Dialogs.Asked);
        Assert.Contains("schemas.example.test", asked.Message + string.Concat(asked.Details ?? []));
        Assert.True(rig.App.Plugins.Registries.IsAllowed(PackageRegistryConsent.SchemaId("schemas.example.test")));
        Assert.NotEqual("noConsent", rig.App.Plugins.SchemaResultOf(allowed)!.State);
        Assert.Equal("noConsent", rig.App.Plugins.SchemaResultOf(other)!.State);
        Assert.Equal("noConsent", rig.App.Plugins.SchemaResultOf(catalog)!.State);
        // another check of the other files, as typing in them would cause, still asks nobody
        await rig.App.Plugins.CheckSchemaAsync(rig.Window, other);
        await rig.App.Plugins.CheckSchemaAsync(rig.Window, catalog);

        await AssertOnlyAsync(rig, "schemas.example.test:443");
    }

    [AvaloniaFact]
    public async Task A_registry_is_asked_about_the_dependencies_after_the_user_allowed_it_and_no_other_registry_is()
    {
        using var rig = new NetworkRig();
        var npm = rig.Window.OpenFile(rig.ProjectFile("package.json", "{ \"name\": \"demo\", \"dependencies\": { \"left-pad\": \"1.0.0\", \"lodash\": \"4.0.0\" } }\n"), null);
        await rig.Until(() => rig.App.Plugins.SchemaResultOf(npm) is not null, "the first check of package.json");
        var composer = rig.Window.OpenFile(rig.ProjectFile("composer.json", "{ \"require\": { \"acme/lib\": \"1.0.0\" } }\n"), null);
        await rig.Until(() => rig.App.Plugins.SchemaResultOf(composer) is not null, "the first check of composer.json");

        rig.Dialogs.Answers.Enqueue(0);
        await rig.Window.PackageLookupAsync(npm, refresh: false);
        await rig.Window.PackageLookupAsync(npm, refresh: true);

        Assert.Single(rig.Dialogs.Asked);
        Assert.True(rig.App.Plugins.Registries.IsAllowed("npm"));
        Assert.False(rig.App.Plugins.Registries.IsAllowed("packagist"));
        // the check that runs after an edit asks only the registries that were allowed
        await rig.App.Plugins.CheckSchemaAsync(rig.Window, composer);

        await AssertOnlyAsync(rig, "registry.npmjs.org:443");
    }

    /// <param name="viaDialog">The proxy is saved through File, Remote Files, Network Proxy; otherwise the policy file is written by something else and only its timestamp tells.</param>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_proxy_the_user_sets_while_the_editor_runs_applies_to_the_next_schema_download_at_once(bool viaDialog)
    {
        using var rig = new NetworkRig(proxy: false);
        var port = rig.Net.Sinkhole.Port;
        rig.Net.AllowLoopback(port, "network proxy of the policy");
        // consent from an earlier session; .invalid never resolves, so even a download that goes around the proxy fetches nothing
        rig.App.Plugins.Registries.Allow(PackageRegistryConsent.SchemaId("first.example.invalid"));
        rig.App.Plugins.Registries.Allow(PackageRegistryConsent.SchemaId("second.example.invalid"));
        var first = rig.Window.OpenFile(rig.ProjectFile("first.json", "{ \"$schema\": \"https://first.example.invalid/a.json\" }\n"), null);
        await rig.Until(() => rig.App.Plugins.SchemaResultOf(first) is not null, "the first check, made while there is no proxy");

        if (viaDialog)
        {
            rig.Dialogs.Answers.Enqueue(1); // HTTP
            foreach (var answer in new[] { "127.0.0.1", port.ToString(), "" }) rig.Dialogs.Prompts.Enqueue(answer); // host, port, no user
            rig.Window.SetNetworkProxy();
            await rig.Until(() => File.Exists(rig.App.Remote.NetworkPolicyPath) && File.ReadAllText(rig.App.Remote.NetworkPolicyPath).Contains(port.ToString()), "the proxy to be saved");
            await rig.Settle(100);
        }
        else NetworkPolicy.Save(rig.App.Remote.NetworkPolicyPath, rig.Net.Sinkhole.Settings, null, rig.Secrets);
        var since = NetworkHarness.Mark();

        var second = rig.Window.OpenFile(rig.ProjectFile("second.json", "{ \"$schema\": \"https://second.example.invalid/b.json\" }\n"), null);
        await rig.Until(() => rig.App.Plugins.SchemaResultOf(second) is not null, "the check made after the proxy was set");

        var report = await rig.Net.FinishAsync(since);
        Assert.True(report.Forbidden.Count == 0, "The download went around the proxy the user had just set:\n" + report);
        Assert.Equal(["second.example.invalid:443"], report.Hosts);
    }

    private static string NameOf(Control c) => AutomationProperties.GetName(c) ?? "";

    [AvaloniaFact]
    public async Task A_remote_picture_is_fetched_from_the_host_the_user_allowed_through_the_proxy_and_the_other_host_is_not_asked()
    {
        using var rig = new NetworkRig();
        var png = Convert.FromBase64String(PreviewSafetyTests.OnePixelPng);
        rig.Net.Sinkhole.Respond = uri => uri.Host == "images.example.test" ? ("image/png", png) : null;
        rig.Window.OpenFile(rig.ProjectFile("readme.md", "![one](http://images.example.test/a.png)\n\n![two](http://other.example.test/b.png)\n"), null);
        rig.Window.ShowLivePreview(outline: false);
        await rig.Settle(700);
        rig.Window.UpdateLayout();
        var buttons = rig.Window.PreviewBody!.GetLogicalDescendants().OfType<Button>().ToList();
        Assert.Equal(2, buttons.Count);

        rig.Dialogs.Answers.Enqueue(0);
        buttons.Single(b => NameOf(b).Contains("images.example.test", StringComparison.Ordinal)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await rig.Until(() => rig.Window.PreviewBody!.GetLogicalDescendants().OfType<Image>().Any(), "the picture");
        await rig.Settle(500);

        Assert.Single(rig.Dialogs.Asked);
        Assert.Single(rig.Window.PreviewBody!.GetLogicalDescendants().OfType<Button>());
        await AssertOnlyAsync(rig, "images.example.test:80");
    }

    [AvaloniaFact]
    public async Task With_the_update_check_on_the_signed_feed_is_asked_once_at_start_and_nothing_else()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the per-user install is Windows only");
        using var rig = new NetworkRig();
        rig.Window.UpdaterOverride = NetworkNoConsentTests.UpdaterOf(rig, "127.0.0.1:9", out var feed);
        rig.Net.Sinkhole.Respond = uri => uri.AbsolutePath == "/feed.json" ? ("application/json", System.Text.Encoding.UTF8.GetBytes(feed)) : null;
        rig.App.Workbench.Settings.SetLayer(SettingSource.User, new JsonObject { ["updates.checkOnStart"] = true });

        rig.StartLikeTheProgram();
        await rig.Window.UpdateTask;
        await rig.Settle(1000);

        Assert.Equal(["127.0.0.1:9"], rig.Net.Sinkhole.Targets);
        await AssertOnlyAsync(rig, "127.0.0.1:9");
    }

    [AvaloniaFact]
    public async Task A_remote_MCP_server_of_an_agent_is_contacted_only_after_the_user_agreed_and_only_at_its_own_address()
    {
        using var rig = new NetworkRig();
        var home = Directory.CreateDirectory(Path.Combine(rig.Temp.Path, "home")).FullName;
        File.WriteAllText(Path.Combine(home, ".claude.json"), """{ "mcpServers": { "remote": { "type": "http", "url": "https://mcp.example.test/mcp" } } }""");
        rig.App.AgentConfigHome = home;
        var window = rig.Window;
        await rig.App.Workbench.Commands.ExecuteAsync("nks.tools.mcpServers", new ShellContext(window, forMenu: false)).AsTask();
        await rig.Until(() => window.IsPaletteOpen && window.PaletteModeNow == PaletteMode.Choice && window.QuickResults.Count == 1, "the list of servers");
        rig.Dialogs.Answers.Enqueue(1); // Cancel the first time
        window.RunPaletteSelection();
        await rig.Until(() => window.IsPaletteOpen && window.PaletteModeNow == PaletteMode.Choice && window.QuickResults.Count == 5, "the actions");
        window.RunPaletteSelection(); // Try
        await rig.Until(() => rig.Dialogs.Asked.Count == 1, "the question");
        await rig.Until(() => window.IsPaletteOpen && window.PaletteModeNow == PaletteMode.Choice && window.QuickResults.Count == 1, "the list again");
        Assert.Empty(rig.Net.Sinkhole.Targets);

        rig.Dialogs.Answers.Enqueue(0); // Connect
        rig.Dialogs.Answers.Enqueue(0); // Close the result
        window.RunPaletteSelection();
        await rig.Until(() => window.IsPaletteOpen && window.PaletteModeNow == PaletteMode.Choice && window.QuickResults.Count == 5, "the actions again");
        window.RunPaletteSelection();
        await rig.Until(() => rig.Dialogs.Asked.Count == 3, "the result");
        await rig.Until(() => window.IsPaletteOpen && window.PaletteModeNow == PaletteMode.Choice && window.QuickResults.Count == 1, "the list at the end");
        window.ClosePalette();
        await rig.Until(() => window.McpTask.IsCompleted, "the list to end");

        await AssertOnlyAsync(rig, "mcp.example.test:443");
    }
}
