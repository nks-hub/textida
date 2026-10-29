using NksHub.Textida.PluginSdk;
using NksHub.Textida.Plugins.Git;
using NksHub.Textida.Rpc;

// Built-in Git: one process speaking the plugin protocol on stdin/stdout.
// Required options (no defaults, the editor decides): --timeout-ms N, --max-bytes N, --max-entries N, --chunk-bytes N.
// Optional: --watch-debounce-ms N, the quiet time after a change in the repository before the status is read again; without it the repository is not watched.
// Optional: --max-tree-entries N, the entries the state of the files of a folder (for the file tree) is answered with; without it --max-entries.
// Optional: --peek-max-lines N, the old lines of one change the peek at it is answered with; without it the answer holds none.
// Optional: --network-timeout-ms N, the time after which fetch, pull and push are ended (without it they run until done or cancelled by the user).
// Optional: --log-entries N, the commits of the log panel; --message-history N, the messages of earlier commits the commit panel offers; without them --max-entries and none.
// Optional: --max-pick-items N and --max-text-length N, the limits of the editor for a question with choices and for one text; without them --max-entries and no cut.
int timeout = -1, maxEntries = -1, chunk = -1, watch = 0, treeEntries = 0, peekLines = 0, networkTimeout = 0, logEntries = 0, messageHistory = 0, pickItems = 0, textLength = 0;
long maxBytes = -1;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--timeout-ms") timeout = int.Parse(args[i + 1]);
    else if (args[i] == "--max-bytes") maxBytes = long.Parse(args[i + 1]);
    else if (args[i] == "--max-entries") maxEntries = int.Parse(args[i + 1]);
    else if (args[i] == "--chunk-bytes") chunk = int.Parse(args[i + 1]);
    else if (args[i] == "--watch-debounce-ms") watch = int.Parse(args[i + 1]);
    else if (args[i] == "--max-tree-entries") treeEntries = int.Parse(args[i + 1]);
    else if (args[i] == "--peek-max-lines") peekLines = int.Parse(args[i + 1]);
    else if (args[i] == "--network-timeout-ms") networkTimeout = int.Parse(args[i + 1]);
    else if (args[i] == "--log-entries") logEntries = int.Parse(args[i + 1]);
    else if (args[i] == "--message-history") messageHistory = int.Parse(args[i + 1]);
    else if (args[i] == "--max-pick-items") pickItems = int.Parse(args[i + 1]);
    else if (args[i] == "--max-text-length") textLength = int.Parse(args[i + 1]);
}
if (timeout < 1 || maxBytes < 1 || maxEntries < 1 || chunk < 1)
{
    Console.Error.WriteLine("git plugin: --timeout-ms, --max-bytes, --max-entries and --chunk-bytes are required.");
    return 2;
}

var plugin = new GitPlugin(new GitSettings(timeout, maxBytes, maxEntries, chunk, watch, treeEntries, peekLines, networkTimeout, logEntries, messageHistory, pickItems, textLength));
return await PluginRunner.RunAsync(plugin, new RpcLimits(4 * 1024 * 1024, 8, 64, 100_000, 64 * 1024 * 1024, 64 * 1024 * 1024));
