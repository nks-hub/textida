using NksHub.Textida.PluginSdk;
using NksHub.Textida.Plugins.Formatters;
using NksHub.Textida.Rpc;

// Built-in formatters: one process speaking the plugin protocol on stdin/stdout.
// Required options (no defaults, the editor decides): --max-bytes N, --sql-indent N, --sql-case upper|lower|preserve, --tidy-timeout-ms N.
// Repeated: --tidy-arg ARGUMENT (the arguments Tidy is started with, in order).
long maxBytes = -1;
int indent = -1, tidyTimeout = -1;
KeywordCase? keywordCase = null;
var tidyArguments = new List<string>();
for (var i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--max-bytes": maxBytes = long.Parse(args[i + 1]); break;
        case "--sql-indent": indent = int.Parse(args[i + 1]); break;
        case "--sql-case": keywordCase = Enum.TryParse<KeywordCase>(args[i + 1], ignoreCase: true, out var parsed) ? parsed : null; break;
        case "--tidy-timeout-ms": tidyTimeout = int.Parse(args[i + 1]); break;
        case "--tidy-arg": tidyArguments.Add(args[i + 1]); break;
    }
}
if (maxBytes < 1 || indent < 0 || keywordCase is null || tidyTimeout < 1)
{
    Console.Error.WriteLine("formatters plugin: --max-bytes, --sql-indent, --sql-case and --tidy-timeout-ms are required.");
    return 2;
}

var plugin = new FormattersPlugin(new FormattersSettings(maxBytes, new SqlFormatOptions(keywordCase.Value, indent), tidyArguments, TimeSpan.FromMilliseconds(tidyTimeout)));
return await PluginRunner.RunAsync(plugin, new RpcLimits(4 * 1024 * 1024, 8, 64, 100_000, 64 * 1024 * 1024, 64 * 1024 * 1024));
