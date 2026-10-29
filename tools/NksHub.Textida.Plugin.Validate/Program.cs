using System.Text.Json;
using System.Text.Json.Schema;
using NksHub.Textida.App.Services;
using NksHub.Textida.Plugin.Validate;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.PluginSdk;

return await Cli.RunAsync(args, Console.Out, Console.Error);

namespace NksHub.Textida.Plugin.Validate
{
    /// <summary>The command line of <c>textida-plugin-validate</c>.</summary>
    public static class Cli
    {
        private const string Usage = """
            Usage: textida-plugin-validate <plugin folder> [options]
                   textida-plugin-validate --schema

            Checks a plugin folder (nkspad-plugin.json and the program it names).
              --run                 start the plugin: check the handshake and the answer to an unknown method
              --scenario <file>     replay a recorded session against the running plugin and compare the permissions it uses (needs --run)
              --source <folder>     check the C# sources for reflection with the analyzer NKSPLG001 and the project settings for NativeAOT
              --host-api <number>   the plugin API version the manifest range must contain (default: the version of this SDK)
              --schema              print the JSON Schema of nkspad-plugin.json and end

            Exit code: 0 when there is no error, 1 when there is one, 2 for a wrong command line.
            """;

        /// <summary>Runs the program with the arguments of the command line; returns the exit code.</summary>
        public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken ct = default)
        {
            string? folder = null, scenario = null, source = null;
            var run = false;
            var hostApi = PluginApi.Version;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--schema":
                        output.WriteLine(Schema());
                        return 0;
                    case "--run":
                        run = true;
                        break;
                    case "--scenario" or "--source" or "--host-api":
                        if (i + 1 >= args.Length) return Fail(error, $"{args[i]} needs a value.");
                        var value = args[++i];
                        if (args[i - 1] == "--scenario") scenario = value;
                        else if (args[i - 1] == "--source") source = value;
                        else if (!int.TryParse(value, out hostApi) || hostApi < 1) return Fail(error, "--host-api needs a number of 1 or more.");
                        break;
                    case "-h" or "--help":
                        output.WriteLine(Usage);
                        return 0;
                    default:
                        if (args[i].StartsWith('-') || folder is not null) return Fail(error, $"Unknown argument '{args[i]}'.");
                        folder = args[i];
                        break;
                }
            }
            if (folder is null) return Fail(error, "Name the plugin folder.");

            var options = new ValidatorOptions
            {
                HostApiVersion = hostApi,
                MaxGrammarBytes = PluginLimitsPlaceholders.GrammarMaxBytes,
                MaxIconBytes = PluginLimitsPlaceholders.IconMaxBytes,
                MaxFolderBytes = PluginLimitsPlaceholders.PluginZipMaxUnpackedBytes,
                MaxEntries = PluginLimitsPlaceholders.PluginZipMaxEntries,
                PluginTimeout = TimeSpan.FromSeconds(PluginLimitsPlaceholders.ValidatorPluginSeconds),
                Run = run,
                ScenarioPath = scenario,
                SourcePath = source,
            };
            var report = await PluginValidator.ValidateAsync(folder, options, ct);
            foreach (var finding in report.Findings) output.WriteLine(finding);
            var errors = report.Of(FindingSeverity.Error).Count();
            output.WriteLine($"{errors} errors, {report.Of(FindingSeverity.Warning).Count()} warnings: {(errors == 0 ? "valid" : "not valid")}.");
            return errors == 0 ? 0 : 1;
        }

        /// <summary>The JSON Schema of <c>nkspad-plugin.json</c>, made from the types the editor reads the manifest into, so it is what the editor accepts.</summary>
        public static string Schema() =>
            JsonSchemaExporter.GetJsonSchemaAsNode(PluginManifestJson.Default.PluginManifest).ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        private static int Fail(TextWriter error, string message)
        {
            error.WriteLine(message);
            error.WriteLine(Usage);
            return 2;
        }
    }
}
