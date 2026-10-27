using System.Text.Json;
using System.Text.Json.Serialization;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost;

/// <summary>Method names of the plugin protocol (line-framed JSON-RPC 2.0, see <see cref="PluginProtocol"/>).</summary>
public static class PluginMethods
{
    public const string Initialize = "initialize";
    public const string Initialized = "initialized";
    public const string Resync = "$/resync";
    public const string Ping = "$/ping";
    public const string Shutdown = "shutdown";
    public const string Exit = "exit";
    public const string Event = "$/event";
    /// <summary>Plugin to host: the plugin waits for a user decision, so the watchdog must not count missed pings.</summary>
    public const string AwaitingUser = "$/awaitingUser";
    /// <summary>Plugin to host: <c>{"count"}</c> more <see cref="Event"/> notifications were handled. Only a plugin that declared <see cref="InitializeResult.EventAcks"/> sends it.</summary>
    public const string EventsHandled = "$/eventsHandled";
}

/// <summary>Handshake request. A plugin must answer with the same <c>apiVersion</c> or an error.</summary>
/// <param name="Settings">Current values of the settings the plugin declared (full key to value); absent when it declared none. Later changes arrive as the event <c>settings.changed</c>.</param>
public sealed record InitializeParams(int ApiVersion, bool Restarted, string HostName, JsonElement? Settings = null);

/// <param name="EventAcks">
/// The plugin acknowledges every handled event with <see cref="PluginMethods.EventsHandled"/>. The editor then holds document events back once
/// <see cref="PluginHostOptions.EventWindow"/> are unhandled, so a slow plugin gets <c>document/eventsLost</c> instead of a backlog in its own memory.
/// </param>
public sealed record InitializeResult(int ApiVersion, string? Name, bool EventAcks = false);
public sealed record AwaitingUserParams(bool Awaiting);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(InitializeParams))]
[JsonSerializable(typeof(InitializeResult))]
[JsonSerializable(typeof(AwaitingUserParams))]
public sealed partial class PluginJson : JsonSerializerContext;

/// <param name="Id">Stable identifier.</param>
/// <param name="DisplayName">Name shown to the user when the plugin fails.</param>
/// <param name="Arguments">Command line arguments, passed one by one (no shell).</param>
/// <param name="ActivationEvents">Triggers such as <c>onCommand:x</c>, <c>onLanguage:json</c>, <c>onFileType:.md</c>, <c>onPanel:y</c>; the plugin starts at the first matching one.</param>
public sealed record PluginSpec(string Id, string DisplayName, string Executable, IReadOnlyList<string> Arguments, IReadOnlyList<string> ActivationEvents, string? WorkingDirectory = null);

/// <summary>Restart and disable policy. No defaults: the numbers are an owner decision.</summary>
/// <param name="MaxCrashes">Crashes inside <paramref name="Window"/> that disable the plugin.</param>
public sealed record RestartPolicy(int MaxCrashes, TimeSpan Window, TimeSpan BaseDelay, TimeSpan MaxDelay);

/// <summary>Host settings. Everything is required: quotas, timeouts and the restart policy are owner decisions, not code defaults.</summary>
public sealed record PluginHostOptions
{
    public required RpcLimits Limits { get; init; }
    public required RestartPolicy Restart { get; init; }
    public required int ApiVersion { get; init; }
    public required TimeSpan HandshakeTimeout { get; init; }
    public required TimeSpan PingInterval { get; init; }
    public required TimeSpan PingTimeout { get; init; }
    public required int MissedPingsBeforeKill { get; init; }
    public required TimeSpan ShutdownGrace { get; init; }
    public required int StderrTailLines { get; init; }
    /// <summary>Bound for a <see cref="PluginHost.CallAsync"/> that names no timeout. Null keeps such a call open until the plugin answers or ends.</summary>
    public TimeSpan? DefaultCallTimeout { get; init; }
    /// <summary>The settings of a plugin as a JSON object for its handshake; null for none.</summary>
    public Func<string, JsonElement?>? SettingsOf { get; init; }

    /// <summary>Events a plugin with <see cref="InitializeResult.EventAcks"/> may have unhandled before a paced <see cref="PluginHost.SendEventAsync"/> waits. Null never waits.</summary>
    public int? EventWindow { get; init; }
}

public enum PluginState { Inactive, Starting, Running, WaitingRestart, Disabled, Stopped }

public enum PluginEndReason { Exited, Unresponsive, ProtocolViolation, HandshakeFailed }

/// <param name="NextRestartIn">Null when the plugin is not restarted (disabled or editor shutting down).</param>
public sealed record PluginCrashInfo(string PluginId, string DisplayName, PluginEndReason Reason, int? ExitCode, string Detail, IReadOnlyList<string> StderrTail, TimeSpan? NextRestartIn);

/// <summary>The plugin died or was killed while a call was in flight. The call may or may not have taken effect; it is never repeated.</summary>
public sealed class PluginCrashedException(PluginCrashInfo info)
    : Exception($"Plugin '{info.DisplayName}' ({info.PluginId}) stopped: {info.Reason}{(info.ExitCode is { } c ? $", exit code {c}" : "")}. {info.Detail}")
{
    public PluginCrashInfo Info { get; } = info;
}

public sealed class PluginHandshakeException(string pluginId, string message) : Exception(message)
{
    public string PluginId { get; } = pluginId;
}

public sealed class PluginUnavailableException(string pluginId, PluginState state)
    : Exception($"Plugin '{pluginId}' is not running (state: {state}).")
{
    public PluginState State { get; } = state;
}

/// <summary>Supplies the state a restarted plugin needs again: open documents with revisions and pending proposals, as raw JSON.</summary>
public interface IPluginStateSource
{
    byte[] BuildState(string pluginId);

    /// <summary>The plugin took the state and runs again; what it missed while it was starting can be sent now.</summary>
    void Resumed(string pluginId) { }
}

/// <summary>Editor API reached by plugin requests. Throw <see cref="RpcException"/> for a protocol error such as a missing permission.</summary>
public delegate ValueTask<byte[]?> PluginApiCallback(string pluginId, string method, JsonElement parameters, CancellationToken cancellation);
