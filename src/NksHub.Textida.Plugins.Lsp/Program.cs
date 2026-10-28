using NksHub.Textida.PluginSdk;
using NksHub.Textida.Plugins.Lsp;
using NksHub.Textida.Rpc;

// Built-in language server client: one process speaking the plugin protocol on stdin/stdout; the servers it starts speak LSP on their own stdin/stdout.
// Required options (no defaults, the editor decides): --max-bytes N, --start-timeout-ms N, --request-timeout-ms N, --stop-timeout-ms N, --max-locations N,
// --stderr-lines N, --max-message-chars N, --max-diagnostics N. Optional: --catalog FILE (JSON list of servers instead of the built-in ones), --idle-stop-ms N (stop a server that long after its last document closed; 0 or absent: never),
// --root-search-levels N (folders searched upwards for the project root of a document, the folder of the file included; absent: 1, the folder of the file).
// Restart after a crash (all four together, or none: a server that ends unexpectedly is then only started again by the next command): --restart-max-attempts N, --restart-base-delay-ms N (doubles with each attempt),
// --restart-max-delay-ms N, --restart-stable-ms N (a server that ran this long before it ended gets the count of attempts back).
// One server runs per language, project root and server configuration; the root is the nearest folder with a marker file of the language, otherwise the nearest with .git or .textida.
// A command may carry "lspServer": the launch of a server a plugin manifest declares; it is used for the language of the command instead of the catalog.
long maxBytes = -1;
int startMs = -1, requestMs = -1, stopMs = -1, maxLocations = -1, stderrLines = -1, messageChars = -1, maxDiagnostics = -1;
string? catalogFile = null;
int idleStopMs = 0, rootSearchLevels = 1, restartAttempts = 0, restartBaseMs = 0, restartMaxMs = 0, restartStableMs = 0;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--max-bytes") maxBytes = long.Parse(args[i + 1]);
    else if (args[i] == "--start-timeout-ms") startMs = int.Parse(args[i + 1]);
    else if (args[i] == "--request-timeout-ms") requestMs = int.Parse(args[i + 1]);
    else if (args[i] == "--stop-timeout-ms") stopMs = int.Parse(args[i + 1]);
    else if (args[i] == "--max-locations") maxLocations = int.Parse(args[i + 1]);
    else if (args[i] == "--stderr-lines") stderrLines = int.Parse(args[i + 1]);
    else if (args[i] == "--max-message-chars") messageChars = int.Parse(args[i + 1]);
    else if (args[i] == "--max-diagnostics") maxDiagnostics = int.Parse(args[i + 1]);
    else if (args[i] == "--catalog") catalogFile = args[i + 1];
    else if (args[i] == "--idle-stop-ms") idleStopMs = int.Parse(args[i + 1]);
    else if (args[i] == "--root-search-levels") rootSearchLevels = int.Parse(args[i + 1]);
    else if (args[i] == "--restart-max-attempts") restartAttempts = int.Parse(args[i + 1]);
    else if (args[i] == "--restart-base-delay-ms") restartBaseMs = int.Parse(args[i + 1]);
    else if (args[i] == "--restart-max-delay-ms") restartMaxMs = int.Parse(args[i + 1]);
    else if (args[i] == "--restart-stable-ms") restartStableMs = int.Parse(args[i + 1]);
}
if (maxBytes < 1 || startMs < 1 || requestMs < 1 || stopMs < 1 || maxLocations < 1 || stderrLines < 1 || messageChars < 2 || maxDiagnostics < 1)
{
    Console.Error.WriteLine("lsp plugin: --max-bytes, --start-timeout-ms, --request-timeout-ms, --stop-timeout-ms, --max-locations, --stderr-lines, --max-message-chars and --max-diagnostics are required.");
    return 2;
}
if (restartAttempts > 0 && (restartBaseMs < 1 || restartMaxMs < restartBaseMs || restartStableMs < 1))
{
    Console.Error.WriteLine("lsp plugin: --restart-max-attempts needs --restart-base-delay-ms, --restart-max-delay-ms (not below the base delay) and --restart-stable-ms.");
    return 2;
}

var catalog = catalogFile is null ? ServerCatalog.BuiltIn : ServerCatalog.FromJson(File.ReadAllText(catalogFile));
var plugin = new LspPlugin(new LspSettings(maxBytes, startMs, requestMs, stopMs, maxLocations, stderrLines, messageChars, maxDiagnostics, idleStopMs, rootSearchLevels,
    restartAttempts, restartBaseMs, restartMaxMs, restartStableMs), catalog);
var code = await PluginRunner.RunAsync(plugin, new RpcLimits(4 * 1024 * 1024, 8, 64, 100_000, 64 * 1024 * 1024, 64 * 1024 * 1024));
await plugin.StopAllAsync();
return code;
