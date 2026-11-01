using System.Diagnostics;
using System.Text.Json;
using NksHub.Textida.Plugin.Validate;

namespace NksHub.Textida.Plugin.Tests;

/// <summary>
/// PZ.8.2: the SDKs for JavaScript/TypeScript and Python. The types they ship are generated from the types of the C# SDK and compared here; each sample plugin is checked by the validator of the
/// plugin author (textida-plugin-validate) with the plugin running as a real program (Node.js, Python) and a recorded session over a document.
/// </summary>
public class SdkTypesTests
{
    private static string Sdk(params string[] parts) => Path.Combine([T.RepoRoot(), "sdk", .. parts]);

    private static void Check(string file, string expected)
    {
        if (Environment.GetEnvironmentVariable("TEXTIDA_UPDATE_SDK") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, expected);
        }
        Assert.True(File.Exists(file), $"{file} is missing; run the tests with TEXTIDA_UPDATE_SDK=1 to write it.");
        Assert.Equal(expected.ReplaceLineEndings("\n"), File.ReadAllText(file).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void The_schema_and_the_typescript_and_python_types_are_the_ones_made_from_the_types_of_the_csharp_sdk()
    {
        Check(Sdk("api.schema.json"), SdkTypes.SchemaText());
        Check(Sdk("js", "api.d.ts"), SdkTypes.TypeScript());
        Check(Sdk("python", "textida_plugin", "api.py"), SdkTypes.Python());
    }

    [Fact]
    public void The_generated_types_name_the_members_of_the_calls_the_sdks_make()
    {
        string ts = SdkTypes.TypeScript(), py = SdkTypes.Python();
        Assert.Contains("export interface ReadTextParams {\n  document: string;\n  revision: number;\n  start: number;\n  end: number;\n}", ts);
        Assert.Contains("export interface RangeEdit {", ts);
        Assert.Contains("  edits: RangeEdit[];", ts);
        Assert.Contains("  path: string | null;", ts);
        Assert.Contains("\"edit/propose\": { params: ProposeParams; result: null };", ts);
        Assert.Contains("class ReadTextParams(TypedDict):\n    document: str\n    revision: int\n    start: int\n    end: int\n", py);
        Assert.Contains("    edits: list[RangeEdit]", py);
        Assert.Contains("    path: str | None", py);
        Assert.True(py.IndexOf("class RangeEdit", StringComparison.Ordinal) < py.IndexOf("class ApplyParams", StringComparison.Ordinal), "a class comes before the classes that use it");
        using var schema = JsonDocument.Parse(SdkTypes.SchemaText());
        Assert.Equal("#/$defs/ReadTextParams", schema.RootElement.GetProperty("methods").GetProperty("document/readText").GetProperty("params").GetString());
    }

    private const string ShoutSession = """
        {
          "documents": [{ "name": "a.txt", "text": "héllo world\nsecond line\n", "selections": [[0, 6]] }],
          "steps": [
            { "kind": "command", "name": "COMMAND", "document": "a.txt", "status": "applied" },
            { "kind": "expectText", "document": "a.txt", "text": "HÉLLO world\nsecond line\n" }
          ]
        }
        """;

    private static string Program(string name)
    {
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (folder.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) continue; // the stubs of the Store, not programs
            foreach (string candidate in OperatingSystem.IsWindows() ? new[] { Path.Combine(folder, name + ".exe") } : new[] { Path.Combine(folder, name) })
                if (File.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException(name + " is not on the PATH of this machine.");
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (string dir in Directory.GetDirectories(from)) CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    private static async Task<ValidationReport> ValidateAsync(string sample, string command, string executable, Action<string> vendor)
    {
        string folder = T.TempDir();
        CopyFolder(Path.Combine(T.RepoRoot(), "samples", "plugins", sample), folder);
        string manifest = Path.Combine(folder, "nkspad-plugin.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"/path/to/" + (sample.StartsWith("js") ? "node" : "python") + "\"", JsonSerializer.Serialize(executable)));
        vendor(folder);
        string scenario = Path.Combine(T.TempDir(), "session.json");
        File.WriteAllText(scenario, ShoutSession.Replace("COMMAND", command));
        var options = new ValidatorOptions { MaxGrammarBytes = 1 << 20, MaxFolderBytes = 64L << 20, MaxEntries = 500, PluginTimeout = TimeSpan.FromSeconds(60), Run = true, ScenarioPath = scenario };
        return await PluginValidator.ValidateAsync(folder, options);
    }

    private static bool Has(ValidationReport report, FindingSeverity severity, string check, string text) =>
        report.Findings.Any(f => f.Severity == severity && f.Check == check && f.Message.Contains(text, StringComparison.Ordinal));

    [Fact]
    public async Task The_javascript_sample_passes_the_validator_with_the_plugin_running_in_node_and_a_recorded_session()
    {
        string node = Program("node");
        var report = await ValidateAsync("js-hello", "example.js-hello.shout", node, folder => CopyFolder(Sdk("js"), Path.Combine(folder, "node_modules", "textida-plugin")));

        Assert.True(report.Passed, string.Join("\n", report.Findings));
        Assert.True(Has(report, FindingSeverity.Info, "handshake", "plugin API version 1"));
        Assert.True(Has(report, FindingSeverity.Info, "rpc", "method-not-found"));
        Assert.True(Has(report, FindingSeverity.Info, "scenario", "ran as recorded"));
        Assert.True(Has(report, FindingSeverity.Info, "permissions", "used 'document.edit.direct'") && Has(report, FindingSeverity.Info, "permissions", "used 'document.read'"));
    }

    [Fact]
    public async Task The_python_sample_passes_the_validator_with_the_plugin_running_in_python_and_a_recorded_session()
    {
        string python = Environment.GetEnvironmentVariable("TEXTIDA_PYTHON") ?? Program(OperatingSystem.IsWindows() ? "python" : "python3");
        var report = await ValidateAsync("python-sdk-hello", "example.python-sdk-hello.shout", python, folder => CopyFolder(Sdk("python", "textida_plugin"), Path.Combine(folder, "textida_plugin")));

        Assert.True(report.Passed, string.Join("\n", report.Findings));
        Assert.True(Has(report, FindingSeverity.Info, "handshake", "plugin API version 1"));
        Assert.True(Has(report, FindingSeverity.Info, "rpc", "method-not-found"));
        Assert.True(Has(report, FindingSeverity.Info, "scenario", "ran as recorded"));
        Assert.True(Has(report, FindingSeverity.Info, "permissions", "used 'document.edit.direct'") && Has(report, FindingSeverity.Info, "permissions", "used 'document.read'"));
    }

    [Fact]
    public async Task A_sample_that_asks_for_less_than_it_uses_is_refused_by_the_validator()
    {
        string node = Program("node");
        string folder = T.TempDir();
        CopyFolder(Path.Combine(T.RepoRoot(), "samples", "plugins", "js-hello"), folder);
        CopyFolder(Sdk("js"), Path.Combine(folder, "node_modules", "textida-plugin"));
        string manifest = Path.Combine(folder, "nkspad-plugin.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"/path/to/node\"", JsonSerializer.Serialize(node)).Replace("[\"document.read\", \"document.edit.direct\"]", "[\"document.read\"]"));
        string scenario = Path.Combine(T.TempDir(), "session.json");
        File.WriteAllText(scenario, ShoutSession.Replace("COMMAND", "example.js-hello.shout"));

        var report = await PluginValidator.ValidateAsync(folder, new ValidatorOptions { MaxGrammarBytes = 1 << 20, MaxFolderBytes = 64L << 20, MaxEntries = 500, PluginTimeout = TimeSpan.FromSeconds(60), Run = true, ScenarioPath = scenario });

        Assert.False(report.Passed);
        Assert.True(Has(report, FindingSeverity.Error, "permissions", "needs 'document.edit.direct'"));
    }
}
