using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using NksHub.Textida.PluginHost;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginSdk;

/// <summary>Handles one request of the editor; returns the result as raw JSON, or null for a null result.</summary>
public delegate ValueTask<byte[]?> RequestHandler(JsonElement parameters, CancellationToken cancellation);

/// <summary>
/// Base of a plugin written in C#. Register methods with <see cref="On(string, RequestHandler)"/> or the typed overload and start it with <see cref="PluginRunner.RunAsync"/>.
/// The handshake, state resync, ping, shutdown and exit are handled here. The typed overload takes source-generated
/// <see cref="JsonTypeInfo{T}"/>, so a plugin built on it carries no reflection and can be published with NativeAOT.
/// </summary>
/// <param name="name">Name reported in the handshake.</param>
/// <param name="apiVersion">Plugin API version the plugin implements; the editor refuses a plugin whose version differs from its own.</param>
public abstract class PluginBase(string name, int apiVersion)
{
    private readonly Dictionary<string, RequestHandler> _methods = [];

    public string Name { get; } = name;
    public int ApiVersion { get; } = apiVersion;

    /// <summary>The connection to the editor, for requests and notifications to it. Set before the first request is handled.</summary>
    public JsonRpcDispatcher Rpc { get; internal set; } = null!;

    protected void On(string method, RequestHandler handler) => _methods.Add(method, handler);

    protected void On<TRequest, TResult>(string method, JsonTypeInfo<TRequest> request, JsonTypeInfo<TResult> result, Func<TRequest, CancellationToken, ValueTask<TResult>> handler) =>
        On(method, async (p, ct) =>
        {
            TRequest value;
            try { value = p.Deserialize(request) ?? throw new JsonException("The parameters are null."); }
            catch (JsonException e) { throw new RpcException(RpcErrorCodes.InvalidParams, e.Message); }
            return JsonSerializer.SerializeToUtf8Bytes(await handler(value, ct).ConfigureAwait(false), result);
        });

    /// <summary>The state after a start or restart, as the editor sends it (open documents with revisions, lost events).</summary>
    protected virtual ValueTask OnResyncAsync(JsonElement state) => ValueTask.CompletedTask;

    /// <summary>An editor event (<c>$/event</c> notification) with its name and payload.</summary>
    protected virtual ValueTask OnEventAsync(JsonElement eventPayload) => ValueTask.CompletedTask;

    internal async ValueTask<byte[]?> HandleAsync(string method, JsonElement p, CancellationToken ct)
    {
        switch (method)
        {
            case PluginMethods.Initialize:
                return JsonSerializer.SerializeToUtf8Bytes(new InitializeResult(ApiVersion, Name, EventAcks: true), PluginJson.Default.InitializeResult);
            case PluginMethods.Resync:
                await OnResyncAsync(p).ConfigureAwait(false);
                return null;
            case PluginMethods.Ping:
            case PluginMethods.Shutdown:
                return "true"u8.ToArray();
            default:
                if (!_methods.TryGetValue(method, out var handler)) throw new RpcException(RpcErrorCodes.MethodNotFound, $"The plugin has no method '{method}'.");
                return await handler(p, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Events are handled one after another and each is acknowledged, so the editor holds the next ones back while <see cref="OnEventAsync"/> is slow.</summary>
    internal async ValueTask HandleNotificationAsync(string method, JsonElement p)
    {
        if (method != PluginMethods.Event) return;
        try { await OnEventAsync(p).ConfigureAwait(false); }
        // an event that failed is still handled; without the acknowledgement the editor would wait for it for good
        finally { await Rpc.NotifyAsync(PluginMethods.EventsHandled, HandledOne).ConfigureAwait(false); }
    }

    private static readonly byte[] HandledOne = "{\"count\":1}"u8.ToArray();
}
