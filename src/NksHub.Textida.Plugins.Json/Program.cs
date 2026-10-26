using System.Text;
using System.Text.Json;
using NksHub.Textida.PluginHost;
using NksHub.Textida.Plugins.Json;
using NksHub.Textida.Rpc;

// Built-in JSON formatter: a separate process speaking the plugin protocol on stdin/stdout.
// Required options (no defaults, the editor decides): --max-bytes N, --indent N.
long maxBytes = -1;
int indent = -1;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--max-bytes") maxBytes = long.Parse(args[i + 1]);
    else if (args[i] == "--indent") indent = int.Parse(args[i + 1]);
}
if (maxBytes < 1 || indent < 0)
{
    Console.Error.WriteLine("json plugin: --max-bytes and --indent are required.");
    return 2;
}

var handler = new Handler();
var limits = new RpcLimits(4 * 1024 * 1024, 8, 64, 100_000, 64 * 1024 * 1024, 64 * 1024 * 1024);
await using var rpc = new JsonRpcDispatcher(Console.OpenStandardInput(), Console.OpenStandardOutput(), RpcProtocols.Plugin, limits, handler);
handler.Command = new FormatCommand(rpc, new FormatSettings(maxBytes, indent));
await rpc.Completion;
return 0;

sealed class Handler : IRpcHandler
{
    public FormatCommand Command = null!;

    public async ValueTask<byte[]?> HandleRequestAsync(string method, JsonElement p, CancellationToken ct)
    {
        switch (method)
        {
            case PluginMethods.Initialize:
                return JsonSerializer.SerializeToUtf8Bytes(new InitializeResult(1, "json-formatter"), PluginJson.Default.InitializeResult);
            case PluginMethods.Resync:
            case PluginMethods.Shutdown:
            case PluginMethods.Ping:
                return Encoding.UTF8.GetBytes("true");
            case "command/execute":
                if (p.GetProperty("command").GetString() is not (FormatCommand.Format or FormatCommand.Preview))
                    throw new RpcException(RpcErrorCodes.InvalidParams, "Unknown command.");
                return Encoding.UTF8.GetBytes($"{{\"status\":\"{await Command.RunAsync(p)}\"}}");
            default:
                throw new RpcException(RpcErrorCodes.MethodNotFound, $"Unknown method '{method}'.");
        }
    }

    public ValueTask HandleNotificationAsync(string method, JsonElement p)
    {
        if (method == PluginMethods.Exit) Environment.Exit(0);
        return ValueTask.CompletedTask;
    }
}
