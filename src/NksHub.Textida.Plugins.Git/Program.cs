using NksHub.Textida.PluginSdk;
using NksHub.Textida.Plugins.Git;
using NksHub.Textida.Rpc;

// Built-in Git: one process speaking the plugin protocol on stdin/stdout.
// Required options (no defaults, the editor decides): --timeout-ms N, --max-bytes N, --max-entries N, --chunk-bytes N.
// Optional: --watch-debounce-ms N, the quiet time after a change in the repository before the status is read again; without it the repository is not watched.
int timeout = -1, maxEntries = -1, chunk = -1, watch = 0;
long maxBytes = -1;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--timeout-ms") timeout = int.Parse(args[i + 1]);
    else if (args[i] == "--max-bytes") maxBytes = long.Parse(args[i + 1]);
    else if (args[i] == "--max-entries") maxEntries = int.Parse(args[i + 1]);
    else if (args[i] == "--chunk-bytes") chunk = int.Parse(args[i + 1]);
    else if (args[i] == "--watch-debounce-ms") watch = int.Parse(args[i + 1]);
}
if (timeout < 1 || maxBytes < 1 || maxEntries < 1 || chunk < 1)
{
    Console.Error.WriteLine("git plugin: --timeout-ms, --max-bytes, --max-entries and --chunk-bytes are required.");
    return 2;
}

var plugin = new GitPlugin(new GitSettings(timeout, maxBytes, maxEntries, chunk, watch));
return await PluginRunner.RunAsync(plugin, new RpcLimits(4 * 1024 * 1024, 8, 64, 100_000, 64 * 1024 * 1024, 64 * 1024 * 1024));
