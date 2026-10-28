using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginSdk;

/// <summary>
/// Requests and notifications to the editor with parameters and results as types. The type information is source generated (<see cref="JsonTypeInfo{T}"/>),
/// so a plugin that uses these carries no reflection and can be published with NativeAOT; there is no overload that takes a type without it.
/// </summary>
public static class TypedRpc
{
    /// <summary>Sends a request and reads the result as <typeparamref name="TResult"/>. A result of <c>null</c> is a protocol error of the editor.</summary>
    public static async Task<TResult> RequestAsync<TParams, TResult>(this JsonRpcDispatcher rpc, string method, TParams parameters, JsonTypeInfo<TParams> parametersInfo,
        JsonTypeInfo<TResult> resultInfo, CancellationToken cancellation = default)
    {
        var result = await rpc.RequestAsync(method, JsonSerializer.SerializeToUtf8Bytes(parameters, parametersInfo), null, cancellation).ConfigureAwait(false);
        try { return result.Deserialize(resultInfo) ?? throw new JsonException("The result is null."); }
        catch (JsonException e) { throw new RpcException(RpcErrorCodes.InternalError, $"The editor answered '{method}' with a result the plugin cannot read: {e.Message}"); }
    }

    /// <summary>Sends a request and returns the result as it came, for a call whose answer the plugin does not read.</summary>
    public static Task<JsonElement> RequestAsync<TParams>(this JsonRpcDispatcher rpc, string method, TParams parameters, JsonTypeInfo<TParams> parametersInfo, CancellationToken cancellation = default) =>
        rpc.RequestAsync(method, JsonSerializer.SerializeToUtf8Bytes(parameters, parametersInfo), null, cancellation);

    public static Task NotifyAsync<TParams>(this JsonRpcDispatcher rpc, string method, TParams parameters, JsonTypeInfo<TParams> parametersInfo, CancellationToken cancellation = default) =>
        rpc.NotifyAsync(method, JsonSerializer.SerializeToUtf8Bytes(parameters, parametersInfo), cancellation);
}
