using System.Buffers;
using System.Text.Json;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Permissions;

/// <summary>Where secret values live (the OS credential store in the editor). Keys are always the pair plugin id and name.</summary>
public interface ISecretBackend
{
    string? Get(string pluginId, string name);
    void Set(string pluginId, string name, string value);
    bool Delete(string pluginId, string name);
}

/// <summary>
/// Secret storage API of the plugin protocol: <c>secret/get</c>, <c>secret/set</c>, <c>secret/delete</c>.
/// The plugin id comes from the connection, never from the request, so a plugin reaches its own secrets and nobody else's.
/// </summary>
public sealed class PluginSecrets(ISecretBackend backend, PluginCallLog log)
{
    public const string Get = "secret/get";
    public const string Set = "secret/set";
    public const string Delete = "secret/delete";

    public ValueTask<byte[]?> HandleAsync(string pluginId, string method, JsonElement p, CancellationToken cancellation) =>
        log.RunAsync(pluginId, method, null, () =>
        {
            cancellation.ThrowIfCancellationRequested();
            var name = NeedString(p, "name");
            if (name.Length == 0) throw new RpcException(RpcErrorCodes.InvalidParams, "Parameter 'name' must not be empty.");
            switch (method)
            {
                case Get: return ValueTask.FromResult<byte[]?>(Reply(w =>
                {
                    if (backend.Get(pluginId, name) is { } value) w.WriteString("value", value); else w.WriteNull("value");
                }));
                case Set:
                    backend.Set(pluginId, name, NeedString(p, "value"));
                    return ValueTask.FromResult<byte[]?>(Reply(w => w.WriteBoolean("stored", true)));
                case Delete:
                    var deleted = backend.Delete(pluginId, name);
                    return ValueTask.FromResult<byte[]?>(Reply(w => w.WriteBoolean("deleted", deleted)));
                default: throw new RpcException(RpcErrorCodes.MethodNotFound, $"The editor has no method '{method}'.");
            }
        });

    internal static string NeedString(JsonElement p, string name) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : throw new RpcException(RpcErrorCodes.InvalidParams, $"Parameter '{name}' must be a string.");

    internal static byte[] Reply(Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            body(w);
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }
}
