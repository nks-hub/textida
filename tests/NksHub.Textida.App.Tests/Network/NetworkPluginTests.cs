using Avalonia.Headless.XUnit;
using NksHub.Textida.NetWatch;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.App.Tests;

/// <summary>PR.6.4 for plugin processes: the only way to the network is <c>net/request</c> with the network permission, and a plugin that finds another way is seen.</summary>
[Collection("LocState")]
[Trait("Category", "Network")]
public class NetworkPluginTests
{
    private const string TestPluginProcess = "dotnet:NksHub.Textida.PluginHost.TestPlugin.dll";

    [AvaloniaFact]
    public async Task A_plugin_that_opens_its_own_socket_resolver_and_HttpClient_is_caught_and_named()
    {
        using var rig = new NetworkRig();
        var call = await rig.StartTestPluginAsync();

        var outcomes = await call("ownNetwork", """{"socket":"192.0.2.1:80","dns":"telemetry.invalid","http":"http://203.0.113.9/collect"}""");
        Assert.Equal(3, outcomes.GetArrayLength());

        var report = await rig.Net.FinishAsync();
        Assert.Contains(report.Hooked, h => h.Process == TestPluginProcess);
        Assert.Contains(report.Forbidden, e => e is { Process: TestPluginProcess, Kind: NetKind.Connect, Target: "192.0.2.1:80" });
        Assert.Contains(report.Forbidden, e => e is { Process: TestPluginProcess, Kind: NetKind.Dns, Target: "telemetry.invalid" });
        Assert.Contains(report.Requests, e => e is { Process: TestPluginProcess, Target: "http://203.0.113.9:80" });
        Assert.All(report.Forbidden, e => Assert.Equal(TestPluginProcess, e.Process)); // the editor itself did nothing
        Assert.Empty(rig.Net.Sinkhole.Targets); // and none of it went through the policy
    }

    [AvaloniaFact]
    public async Task Net_request_is_refused_without_the_network_permission_and_with_it_reaches_the_host_asked_for_through_the_proxy_of_the_policy_only()
    {
        using var rig = new NetworkRig();
        rig.Net.AllowLoopback(rig.Net.Sinkhole.Port, "network proxy of the policy");
        rig.Net.Sinkhole.Respond = _ => ("text/plain", "ok"u8.ToArray());
        var call = await rig.StartTestPluginAsync();
        const string request = """{"method":"net/request","params":{"url":"http://telemetry.example.test/collect","method":"POST","body":"eA=="}}""";

        var denied = await call("callEditor", request);
        Assert.Equal(DocumentErrorCodes.PermissionDenied, denied.GetProperty("error").GetInt32());
        Assert.Empty(rig.Net.Sinkhole.Targets);

        rig.App.Plugins.Permissions.Grant("p", PluginPermissionNames.Network, GrantScope.All);
        var granted = await call("callEditor", request);
        Assert.Equal(200, granted.GetProperty("status").GetInt32());

        var report = await rig.Net.FinishAsync();
        Assert.True(report.Forbidden.Count == 0, report.ToString());
        Assert.Equal(["telemetry.example.test:80"], report.Hosts);
        Assert.Equal(["telemetry.example.test:80"], rig.Net.Sinkhole.Targets);
        Assert.Contains(report.Ipc, e => e.Detail == "network proxy of the policy");
    }
}
