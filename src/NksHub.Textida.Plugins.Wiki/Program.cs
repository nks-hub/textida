using NksHub.Textida.PluginSdk;
using NksHub.Textida.Plugins.Wiki;
using NksHub.Textida.Rpc;

// Built-in NKS IS wiki as a file source: one process speaking the plugin protocol on stdin/stdout.
// Required options (no defaults, the editor decides): --timeout-ms N, --max-bytes N, --max-entries N, --language CODE.
int timeout = -1, maxEntries = -1;
long maxBytes = -1;
string? language = null;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--timeout-ms") timeout = int.Parse(args[i + 1]);
    else if (args[i] == "--max-bytes") maxBytes = long.Parse(args[i + 1]);
    else if (args[i] == "--max-entries") maxEntries = int.Parse(args[i + 1]);
    else if (args[i] == "--language") language = args[i + 1];
}
if (timeout < 1 || maxBytes < 1 || maxEntries < 1 || string.IsNullOrEmpty(language))
{
    Console.Error.WriteLine("wiki plugin: --timeout-ms, --max-bytes, --max-entries and --language are required.");
    return 2;
}

var plugin = new WikiPlugin(new WikiSettings(TimeSpan.FromMilliseconds(timeout), maxBytes, maxEntries, language));
return await PluginRunner.RunAsync(plugin, new RpcLimits(4 * 1024 * 1024, 8, 64, 100_000, 64 * 1024 * 1024, 64 * 1024 * 1024));
