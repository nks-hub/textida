using NksHub.Textida.PluginSdk;
using NksHub.Textida.Plugins.CodeExplorer;
using NksHub.Textida.Rpc;

// Built-in Code Explorer: one process speaking the plugin protocol on stdin/stdout.
// Required options (no defaults, the editor decides): --chunk-bytes N, --max-bytes N, --max-symbols N, --max-symbol-chars N, --match-timeout-ms N.
int chunk = -1, maxBytes = -1, maxSymbols = -1, maxSymbolChars = -1, matchTimeout = -1;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--chunk-bytes") chunk = int.Parse(args[i + 1]);
    else if (args[i] == "--max-bytes") maxBytes = int.Parse(args[i + 1]);
    else if (args[i] == "--max-symbols") maxSymbols = int.Parse(args[i + 1]);
    else if (args[i] == "--max-symbol-chars") maxSymbolChars = int.Parse(args[i + 1]);
    else if (args[i] == "--match-timeout-ms") matchTimeout = int.Parse(args[i + 1]);
}
if (chunk < 1 || maxBytes < 1 || maxSymbols < 1 || maxSymbolChars < 1 || matchTimeout < 1)
{
    Console.Error.WriteLine("code explorer plugin: --chunk-bytes, --max-bytes, --max-symbols, --max-symbol-chars and --match-timeout-ms are required.");
    return 2;
}

var plugin = new CodeExplorerPlugin(new CodeExplorerSettings(chunk, maxBytes, maxSymbols, maxSymbolChars, matchTimeout));
return await PluginRunner.RunAsync(plugin, new RpcLimits(4 * 1024 * 1024, 8, 64, 100_000, 64 * 1024 * 1024, 64 * 1024 * 1024));
