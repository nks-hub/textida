using NksHub.Textida.PluginSdk;
using NksHub.Textida.Plugins.WebData;
using NksHub.Textida.Rpc;

// Built-in colour conversion and key paths of YAML and JSON: one process speaking the plugin protocol on stdin/stdout.
// Required options (no defaults, the editor decides): --chunk-bytes N, --max-bytes N, --max-symbols N, --max-symbol-chars N.
int chunk = -1, maxBytes = -1, maxSymbols = -1, maxSymbolChars = -1;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--chunk-bytes") chunk = int.Parse(args[i + 1]);
    else if (args[i] == "--max-bytes") maxBytes = int.Parse(args[i + 1]);
    else if (args[i] == "--max-symbols") maxSymbols = int.Parse(args[i + 1]);
    else if (args[i] == "--max-symbol-chars") maxSymbolChars = int.Parse(args[i + 1]);
}
if (chunk < 1 || maxBytes < 1 || maxSymbols < 1 || maxSymbolChars < 1)
{
    Console.Error.WriteLine("web data plugin: --chunk-bytes, --max-bytes, --max-symbols and --max-symbol-chars are required.");
    return 2;
}

var plugin = new WebDataPlugin(new WebDataSettings(chunk, maxBytes, maxSymbols, maxSymbolChars));
return await PluginRunner.RunAsync(plugin, new RpcLimits(4 * 1024 * 1024, 8, 64, 100_000, 64 * 1024 * 1024, 64 * 1024 * 1024));
