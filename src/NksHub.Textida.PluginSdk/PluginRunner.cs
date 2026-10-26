using System.Text.Json;
using NksHub.Textida.PluginHost;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginSdk;

/// <summary>Connects a <see cref="PluginBase"/> to the editor over the plugin protocol (line-framed JSON-RPC on stdin and stdout).</summary>
public static class PluginRunner
{
    /// <summary>Serves the plugin until the editor sends <c>exit</c> or closes the connection. Returns the process exit code.</summary>
    /// <param name="limits">Message and queue limits of the connection; they are an owner decision, so there is no default.</param>
    /// <param name="input">Defaults to standard input.</param>
    /// <param name="output">Defaults to standard output.</param>
    public static async Task<int> RunAsync(PluginBase plugin, RpcLimits limits, Stream? input = null, Stream? output = null)
    {
        var handler = new Handler(plugin);
        await using var rpc = new JsonRpcDispatcher(input ?? Console.OpenStandardInput(), output ?? Console.OpenStandardOutput(), RpcProtocols.Plugin, limits, handler);
        plugin.Rpc = rpc;
        await Task.WhenAny(rpc.Completion, handler.Exited.Task).ConfigureAwait(false);
        return 0;
    }

    private sealed class Handler(PluginBase plugin) : IRpcHandler
    {
        public readonly TaskCompletionSource Exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<byte[]?> HandleRequestAsync(string method, JsonElement parameters, CancellationToken cancellation) =>
            plugin.HandleAsync(method, parameters, cancellation);

        public ValueTask HandleNotificationAsync(string method, JsonElement parameters)
        {
            if (method == PluginMethods.Exit) Exited.TrySetResult();
            return plugin.HandleNotificationAsync(method, parameters);
        }
    }
}
