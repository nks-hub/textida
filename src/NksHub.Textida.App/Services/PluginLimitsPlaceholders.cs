using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.App.Services;

/// <summary>
/// The one place where the app gives the plugin host its limits, timeouts and quotas. EVERY NUMBER HERE IS A PLACEHOLDER waiting for the
/// owner's decision (the host and the APIs have no defaults on purpose); none of them is a decided value.
/// </summary>
internal static class PluginLimitsPlaceholders
{
    /// <summary>Placeholder: largest JSON range the built-in formatter handles; also bounded by <see cref="DocumentApiOptions.MaxReadBytes"/>.</summary>
    public const long JsonFormatterMaxBytes = 1 << 20;

    /// <summary>Placeholder: spaces per level of the built-in formatter.</summary>
    public const int JsonFormatterIndent = 2;

    public static PluginHostOptions Host() => new()
    {
        Limits = new RpcLimits(MaxMessageBytes: 4 << 20, MaxConcurrentIncoming: 8, MaxPendingOutgoing: 64, MaxResultItems: 100_000, MaxSessionBytes: 64L << 20, MaxOutgoingQueueBytes: 64L << 20),
        Restart = new RestartPolicy(MaxCrashes: 3, Window: TimeSpan.FromMinutes(5), BaseDelay: TimeSpan.FromSeconds(1), MaxDelay: TimeSpan.FromSeconds(30)),
        ApiVersion = 1,
        HandshakeTimeout = TimeSpan.FromSeconds(10),
        PingInterval = TimeSpan.FromSeconds(5),
        PingTimeout = TimeSpan.FromSeconds(5),
        MissedPingsBeforeKill = 3,
        ShutdownGrace = TimeSpan.FromSeconds(3),
        StderrTailLines = 20,
    };

    /// <summary>Placeholder: entries kept in the plugin call log.</summary>
    public const int CallLogCapacity = 1000;

    public static DocumentApiOptions Documents(IEditProposals proposals, PluginPermissions permissions, PluginCallLog callLog) => new()
    {
        MaxReadBytes = 1 << 20,
        MaxFindMatches = 1000,
        MaxSnapshotsPerPlugin = 4,
        SnapshotLifetime = TimeSpan.FromSeconds(30),
        EventQueueCapacity = 256,
        MaxRememberedBatches = 64,
        Permissions = permissions,
        CallLog = callLog,
        Proposals = proposals,
    };

    public static UiApiOptions Ui() => new()
    {
        MaxPanelsPerPlugin = 4,
        MaxItemsPerPanel = 5000,
        MaxTreeDepth = 16,
        MaxTasksPerPlugin = 4,
        MaxTextLength = 4000,
        MaxPickItems = 500,
    };
}
