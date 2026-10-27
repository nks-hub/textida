using NksHub.Textida.PluginSdk;
using NksHub.Textida.Plugins.TextTools;
using NksHub.Textida.Rpc;

// Built-in text tools (statistics, hashes, block calculation, expressions, number bases): one process speaking the plugin protocol on stdin/stdout.
// Required options (no defaults, the editor decides): --chunk-bytes N, --progress-ms N.
int chunk = -1, progress = -1;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--chunk-bytes") chunk = int.Parse(args[i + 1]);
    else if (args[i] == "--progress-ms") progress = int.Parse(args[i + 1]);
}
if (chunk < 1 || progress < 0)
{
    Console.Error.WriteLine("text tools plugin: --chunk-bytes and --progress-ms are required.");
    return 2;
}

var plugin = new TextToolsPlugin(new TextToolsSettings(chunk, TimeSpan.FromMilliseconds(progress)));
return await PluginRunner.RunAsync(plugin, new RpcLimits(4 * 1024 * 1024, 8, 64, 100_000, 64 * 1024 * 1024, 64 * 1024 * 1024));
