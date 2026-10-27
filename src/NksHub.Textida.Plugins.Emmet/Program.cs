using NksHub.Textida.PluginSdk;
using NksHub.Textida.Plugins.Emmet;
using NksHub.Textida.Rpc;

// Built-in Emmet: one process speaking the plugin protocol on stdin/stdout.
// Required options (no defaults, the editor decides): --context-bytes N, --max-chars N, --max-carets N, --preview-chars N.
int context = -1, maxChars = -1, maxCarets = -1, previewChars = -1;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--context-bytes") context = int.Parse(args[i + 1]);
    else if (args[i] == "--max-chars") maxChars = int.Parse(args[i + 1]);
    else if (args[i] == "--max-carets") maxCarets = int.Parse(args[i + 1]);
    else if (args[i] == "--preview-chars") previewChars = int.Parse(args[i + 1]);
}
if (context < 1 || maxChars < 1 || maxCarets < 1 || previewChars < 1)
{
    Console.Error.WriteLine("emmet plugin: --context-bytes, --max-chars, --max-carets and --preview-chars are required.");
    return 2;
}

var plugin = new EmmetPlugin(new EmmetSettings(context, maxChars, maxCarets, previewChars));
return await PluginRunner.RunAsync(plugin, new RpcLimits(4 * 1024 * 1024, 8, 64, 100_000, 64 * 1024 * 1024, 64 * 1024 * 1024));
