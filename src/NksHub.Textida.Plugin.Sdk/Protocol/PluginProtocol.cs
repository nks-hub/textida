using System.Text.Json;
using System.Text.Json.Serialization;

namespace NksHub.Textida.PluginHost;

/// <summary>Method names of the plugin protocol (line-framed JSON-RPC 2.0, see <see cref="NksHub.Textida.Rpc.PluginProtocol"/>): the handshake and the life cycle of a plugin process.</summary>
public static class PluginMethods
{
    /// <summary>Request, editor to plugin: the handshake. The plugin answers with an <see cref="InitializeResult"/>; the answer must name the plugin API version the editor asked for, or be an error.</summary>
    public const string Initialize = "initialize";
    /// <summary>Notification, editor to plugin: the plugin has been initialized and the editor now sends it work.</summary>
    public const string Initialized = "initialized";
    /// <summary>Request, editor to plugin: after a start or a restart, the open documents with their revisions and the events the plugin missed.</summary>
    public const string Resync = "$/resync";
    /// <summary>Request, editor to plugin: the liveness check; the plugin answers <c>true</c>.</summary>
    public const string Ping = "$/ping";
    /// <summary>Request, editor to plugin: the plugin finishes its work and answers; the editor then sends <see cref="Exit"/>.</summary>
    public const string Shutdown = "shutdown";
    /// <summary>Notification, editor to plugin: the process ends now.</summary>
    public const string Exit = "exit";
    /// <summary>Notification, editor to plugin: an event with its <c>name</c> and <c>payload</c> (a document changed, a setting changed, the user cancelled a task).</summary>
    public const string Event = "$/event";
    /// <summary>Plugin to host: the plugin waits for a user decision, so the watchdog must not count missed pings.</summary>
    public const string AwaitingUser = "$/awaitingUser";
    /// <summary>Plugin to host: <c>{"count"}</c> more <see cref="Event"/> notifications were handled. Only a plugin that declared <see cref="InitializeResult.EventAcks"/> sends it.</summary>
    public const string EventsHandled = "$/eventsHandled";
}

/// <summary>Handshake request.</summary>
/// <param name="ApiVersion">The plugin API version of the editor.</param>
/// <param name="Restarted">True when the plugin process was started again after the editor ended it or it crashed.</param>
/// <param name="HostName">Name of the editor.</param>
/// <param name="Settings">Current values of the settings the plugin declared (full key to value); absent when it declared none. Later changes arrive as the event <c>settings.changed</c>.</param>
public sealed record InitializeParams(int ApiVersion, bool Restarted, string HostName, JsonElement? Settings = null);

/// <summary>Answer to the handshake. A plugin must answer with the same <c>apiVersion</c> as the request or with an error.</summary>
/// <param name="ApiVersion">The plugin API version the plugin implements.</param>
/// <param name="Name">Name of the plugin, for the log of the editor.</param>
/// <param name="EventAcks">
/// The plugin acknowledges every handled event with <see cref="PluginMethods.EventsHandled"/>. The editor then holds document events back once a number of them (its event window)
/// are unhandled, so a slow plugin gets <c>document/eventsLost</c> instead of a backlog in its own memory.
/// </param>
/// <param name="IdleStop">
/// The plugin keeps nothing in the editor that cannot be set up again, so the editor may end its process after a while without work;
/// the next activation event starts it again with a new handshake.
/// </param>
public sealed record InitializeResult(int ApiVersion, string? Name, bool EventAcks = false, bool IdleStop = false);

/// <summary>Parameters of <see cref="PluginMethods.AwaitingUser"/>.</summary>
/// <param name="Awaiting">True while the plugin waits for the user, false when it stopped waiting.</param>
public sealed record AwaitingUserParams(bool Awaiting);

/// <summary>Source-generated JSON metadata of the handshake types (no reflection).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(InitializeParams))]
[JsonSerializable(typeof(InitializeResult))]
[JsonSerializable(typeof(AwaitingUserParams))]
public sealed partial class PluginJson : JsonSerializerContext;
