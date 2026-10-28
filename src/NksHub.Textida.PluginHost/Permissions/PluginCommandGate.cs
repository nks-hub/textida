using System.Text;
using System.Text.Json;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Permissions;

/// <summary>Runs a command of the plugin <paramref name="TargetPluginId"/>; <see cref="PluginHost.CallAsync"/> with <c>command/execute</c> fits.</summary>
public delegate Task<JsonElement> CommandExecutor(string targetPluginId, string command, ReadOnlyMemory<byte> argumentsJson, CancellationToken cancellation);

/// <summary>
/// <c>command/execute</c>: a plugin runs a command of another plugin. It needs <see cref="PluginPermissionNames.CommandExecute"/> scoped to the target.
/// The target runs the command in delegated mode (<see cref="PluginPermissions.BeginDelegation"/>), so the permission to run it does not
/// pass a document write on to the target: its <c>edit/apply</c> is refused while the command runs.
/// </summary>
public sealed class PluginCommandGate(PluginPermissions permissions, PluginCallLog log, CommandExecutor execute)
{
    public const string Execute = PluginServiceMethods.CommandExecute;

    public ValueTask<byte[]?> HandleAsync(string pluginId, string method, JsonElement p, CancellationToken cancellation) =>
        log.RunAsync(pluginId, method, null, async () =>
        {
            if (method != Execute) throw new RpcException(RpcErrorCodes.MethodNotFound, $"The editor has no method '{method}'.");
            var target = PluginSecrets.NeedString(p, "plugin");
            var command = PluginSecrets.NeedString(p, "command");
            using var lease = permissions.Enter(pluginId, PluginPermissionNames.CommandExecute, GrantScope.OfPlugin(target), cancellation);
            var args = p.TryGetProperty("args", out var a) ? Encoding.UTF8.GetBytes(a.GetRawText()) : default;
            try
            {
                using var delegation = permissions.BeginDelegation(target);
                var result = await execute(target, command, args, lease.Token).ConfigureAwait(false);
                return Encoding.UTF8.GetBytes(result.GetRawText());
            }
            catch (OperationCanceledException) when (lease.Revoked && !cancellation.IsCancellationRequested)
            {
                throw lease.RevokedError();
            }
        });
}
