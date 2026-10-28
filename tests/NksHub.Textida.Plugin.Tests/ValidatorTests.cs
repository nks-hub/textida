using System.Text.Json;
using NksHub.Textida.Plugin.Validate;

namespace NksHub.Textida.Plugin.Tests;

/// <summary>P8b.2.2: textida-plugin-validate checks a plugin folder: the manifest, the range of the plugin API, the permissions declared and used, the size, the program and the sources.</summary>
public class ValidatorTests
{
    private static ValidatorOptions Options(Func<ValidatorOptions, ValidatorOptions>? tune = null)
    {
        var options = new ValidatorOptions { MaxGrammarBytes = 1 << 20, MaxFolderBytes = 64L << 20, MaxEntries = 500, PluginTimeout = TimeSpan.FromSeconds(60) };
        return tune?.Invoke(options) ?? options;
    }

    private const string Script = "plugin.sh";

    private static string DefaultMain => $$"""{ "executable": "./{{Script}}", "activationEvents": ["onStartup"] }""";

    private static string Manifest(string api = """{ "min": 1, "max": 1 }""", string permissions = "[]", string? main = null) => $$"""
        {
          "schemaVersion": 1, "id": "example.probe", "name": "Probe", "version": "1.0.0",
          "api": {{api}},
          "permissions": {{permissions}},
          "main": {{main ?? DefaultMain}}
        }
        """;

    private static string Folder(string manifest, params (string Name, string Content)[] files)
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, "nkspad-plugin.json"), manifest);
        if (!files.Any(f => f.Name == Script)) File.WriteAllText(Path.Combine(dir, Script), "#!/bin/sh\n");
        foreach (var (name, content) in files)
        {
            var path = Path.Combine(dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        return dir;
    }

    private static bool Has(ValidationReport report, FindingSeverity severity, string check, string text) =>
        report.Findings.Any(f => f.Severity == severity && f.Check == check && f.Message.Contains(text, StringComparison.Ordinal));

    [Fact]
    public async Task A_plugin_that_declares_the_permissions_it_uses_is_valid_and_each_check_says_what_it_found()
    {
        var folder = Folder(Manifest(permissions: """["document.read"]"""), (Script, "#!/bin/sh\n# calls document/readText\n"));

        var report = await PluginValidator.ValidateAsync(folder, Options());

        Assert.True(report.Passed, string.Join("\n", report.Findings));
        Assert.True(Has(report, FindingSeverity.Info, "api", "plugin API 1 to 1"));
        Assert.True(Has(report, FindingSeverity.Info, "manifest", "example.probe"));
        Assert.True(Has(report, FindingSeverity.Info, "size", "within the limits"));
        Assert.True(Has(report, FindingSeverity.Info, "permissions", "'document.read' is declared"));
        Assert.True(Has(report, FindingSeverity.Info, "aot", "script"));
        Assert.Empty(report.Of(FindingSeverity.Warning));
    }

    [Theory]
    [InlineData("""{ "min": 0, "max": 1 }""")]
    [InlineData("""{ "min": 2, "max": 1 }""")]
    [InlineData("""{ "min": 1, "max": 0 }""")]
    [InlineData("""{ "min": 2, "max": 3 }""")]
    [InlineData("""{ "min": -1, "max": -1 }""")]
    [InlineData("{}")]
    public async Task A_manifest_with_an_invalid_api_range_or_one_without_the_api_of_the_sdk_is_refused(string api)
    {
        var report = await PluginValidator.ValidateAsync(Folder(Manifest(api)), Options());

        Assert.False(report.Passed);
        Assert.Contains(report.Findings, f => f is { Severity: FindingSeverity.Error, Check: "api" });
    }

    [Theory]
    [InlineData("""{ "min": 1, "max": 1 }""")]
    [InlineData("""{ "min": 1, "max": 5 }""")]
    public async Task A_range_that_contains_the_api_of_the_sdk_is_accepted(string api)
    {
        var report = await PluginValidator.ValidateAsync(Folder(Manifest(api)), Options());

        Assert.True(report.Passed, string.Join("\n", report.Findings));
    }

    [Fact]
    public async Task The_api_the_range_has_to_contain_can_be_named_and_a_range_without_it_is_refused()
    {
        var report = await PluginValidator.ValidateAsync(Folder(Manifest("""{ "min": 1, "max": 1 }""")), Options(o => o with { HostApiVersion = 2 }));

        Assert.True(Has(report, FindingSeverity.Error, "api", "provides 2"));
    }

    [Fact]
    public async Task A_manifest_that_is_not_json_or_has_a_member_the_editor_does_not_know_is_refused_whole()
    {
        var unknown = await PluginValidator.ValidateAsync(Folder(Manifest().Replace("\"name\"", "\"nmae\": 1, \"name\"")), Options());
        var broken = await PluginValidator.ValidateAsync(Folder("{ not json"), Options());
        var missing = await PluginValidator.ValidateAsync(T.TempDir(), Options());
        var nowhere = await PluginValidator.ValidateAsync(Path.Combine(T.TempDir(), "nothing"), Options());

        Assert.All(new[] { unknown, broken, missing, nowhere }, report => Assert.Contains(report.Findings, f => f is { Severity: FindingSeverity.Error, Check: "manifest" }));
    }

    [Fact]
    public async Task A_permission_that_does_not_exist_or_is_listed_twice_is_an_error()
    {
        var report = await PluginValidator.ValidateAsync(Folder(Manifest(permissions: """["document.read", "document.read", "bogus"]""")), Options());

        Assert.True(Has(report, FindingSeverity.Error, "permissions", "Unknown permission 'bogus'"));
        Assert.True(Has(report, FindingSeverity.Error, "permissions", "listed twice"));
    }

    [Fact]
    public async Task A_method_the_plugin_calls_without_the_permission_is_an_error_and_a_permission_it_never_needs_is_a_warning()
    {
        var script = "#!/bin/sh\n# edit/apply net/request document/readText command/execute ui/notify secret/get\n";
        var folder = Folder(Manifest(permissions: """["document.read", "document.preview"]"""), (Script, script));

        var report = await PluginValidator.ValidateAsync(folder, Options());

        Assert.False(report.Passed);
        Assert.True(Has(report, FindingSeverity.Error, "permissions", "edit/apply") && Has(report, FindingSeverity.Error, "permissions", "'document.edit.direct'"));
        Assert.True(Has(report, FindingSeverity.Error, "permissions", "net/request") && Has(report, FindingSeverity.Error, "permissions", "'network'"));
        Assert.True(Has(report, FindingSeverity.Warning, "permissions", "'document.preview'"));
        Assert.True(Has(report, FindingSeverity.Info, "permissions", "'command.execute'")); // not declarable: the user grants it for the target plugin
        Assert.DoesNotContain(report.Findings, f => f.Check == "permissions" && f.Message.Contains("ui/notify") && f.Severity != FindingSeverity.Info);
    }

    [Fact]
    public async Task The_names_of_the_methods_are_found_in_a_program_too_in_utf8_and_in_utf16_and_across_the_end_of_a_chunk()
    {
        var folder = Folder(Manifest(permissions: """["document.read"]""", main: """{ "executable": "./program.bin", "activationEvents": ["onStartup"] }"""));
        var bytes = new byte[(1 << 20) + 64];
        System.Text.Encoding.Unicode.GetBytes("document/readText").CopyTo(bytes, (1 << 20) - 10); // cut by the end of the first chunk
        System.Text.Encoding.UTF8.GetBytes("edit/apply").CopyTo(bytes, 100);
        // a NativeAOT program for macOS: the last byte of a string literal (the zero high byte of its last character) belongs to what follows it
        var net = System.Text.Encoding.Unicode.GetBytes("net/request");
        net.CopyTo(bytes, 5000);
        bytes[5000 + net.Length - 1] = 0x59;
        File.WriteAllBytes(Path.Combine(folder, "program.bin"), bytes);

        var report = await PluginValidator.ValidateAsync(folder, Options());

        Assert.True(Has(report, FindingSeverity.Info, "permissions", "'document.read' is declared"));
        Assert.True(Has(report, FindingSeverity.Error, "permissions", "edit/apply"));
        Assert.True(Has(report, FindingSeverity.Error, "permissions", "net/request"));
    }

    [Fact]
    public async Task A_folder_over_the_limits_of_the_editor_is_refused()
    {
        var folder = Folder(Manifest(), ("a.txt", new string('a', 100)), ("b.txt", "b"));

        var bytes = await PluginValidator.ValidateAsync(folder, Options(o => o with { MaxFolderBytes = 50 }));
        var entries = await PluginValidator.ValidateAsync(folder, Options(o => o with { MaxEntries = 2 }));

        Assert.True(Has(bytes, FindingSeverity.Error, "size", "bytes; a plugin may hold 50"));
        Assert.True(Has(entries, FindingSeverity.Error, "size", "files; a plugin may hold 2"));
    }

    [Fact]
    public async Task A_managed_program_is_a_warning_because_a_distributed_plugin_is_native()
    {
        var folder = Folder(Manifest(main: """{ "executable": "./program.exe", "activationEvents": ["onStartup"] }"""));
        File.Copy(typeof(PluginValidator).Assembly.Location, Path.Combine(folder, "program.exe")); // a managed assembly

        var report = await PluginValidator.ValidateAsync(folder, Options());

        Assert.True(Has(report, FindingSeverity.Warning, "aot", "managed assembly"));
    }

    [Fact]
    public async Task A_program_with_a_runtime_configuration_beside_it_is_a_framework_dependent_program()
    {
        var folder = Folder(Manifest(main: """{ "executable": "./program", "activationEvents": ["onStartup"] }"""), ("program", "x"), ("program.runtimeconfig.json", "{}"));

        var report = await PluginValidator.ValidateAsync(folder, Options());

        Assert.True(Has(report, FindingSeverity.Warning, "aot", "framework-dependent"));
    }

    [Fact]
    public async Task A_native_program_without_metadata_is_the_shape_of_a_nativeaot_plugin()
    {
        if (!OperatingSystem.IsWindows()) return; // the check reads Windows executables; the other systems have the runtime configuration check
        var folder = Folder(Manifest(main: """{ "executable": "./program.exe", "activationEvents": ["onStartup"] }"""));
        File.Copy(T.DotnetFullPath, Path.Combine(folder, "program.exe")); // the dotnet host is native code

        var report = await PluginValidator.ValidateAsync(folder, Options());

        Assert.True(Has(report, FindingSeverity.Info, "aot", "native code with no .NET metadata"));
        Assert.Empty(report.Of(FindingSeverity.Warning).Where(f => f.Check == "aot"));
    }

    private const string GoodProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><PublishAot>true</PublishAot></PropertyGroup>
          <ItemGroup><PackageReference Include="NksHub.Textida.Plugin.Sdk" Version="0.1.0" /></ItemGroup>
        </Project>
        """;

    [Fact]
    public async Task Sources_that_use_reflection_fail_with_the_place_of_each_use_and_clean_sources_pass()
    {
        var folder = Folder(Manifest());
        Directory.CreateDirectory(Path.Combine(folder, "src"));
        File.WriteAllText(Path.Combine(folder, "src", "Plugin.csproj"), GoodProject);
        File.WriteAllText(Path.Combine(folder, "src", "Plugin.cs"), "public class Plugin\n{\n    public object? Find() => typeof(Plugin).GetMethod(\"Find\");\n}\n");

        var reflective = await PluginValidator.ValidateAsync(folder, Options(o => o with { SourcePath = Path.Combine(folder, "src") }));

        Assert.False(reflective.Passed);
        Assert.True(Has(reflective, FindingSeverity.Error, "aot", "Plugin.cs(3)") && Has(reflective, FindingSeverity.Error, "aot", "Type.GetMethod"));

        File.WriteAllText(Path.Combine(folder, "src", "Plugin.cs"), "public class Plugin\n{\n    public int Find() => 1;\n}\n");
        var clean = await PluginValidator.ValidateAsync(folder, Options(o => o with { SourcePath = Path.Combine(folder, "src") }));

        Assert.True(clean.Passed, string.Join("\n", clean.Findings));
        Assert.True(Has(clean, FindingSeverity.Info, "source", "found no reflection"));
    }

    [Fact]
    public async Task A_project_that_leaves_out_the_analyzer_or_switches_the_trimming_warnings_off_is_refused()
    {
        var folder = Folder(Manifest());
        var src = Path.Combine(folder, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "Plugin.cs"), "public class Plugin { }\n");

        File.WriteAllText(Path.Combine(src, "Plugin.csproj"), GoodProject.Replace("<ItemGroup><PackageReference Include=\"NksHub.Textida.Plugin.Sdk\" Version=\"0.1.0\" /></ItemGroup>", ""));
        Assert.True(Has(await PluginValidator.ValidateAsync(folder, Options(o => o with { SourcePath = src })), FindingSeverity.Error, "aot", "does not run the analyzer"));

        File.WriteAllText(Path.Combine(src, "Plugin.csproj"), GoodProject.Replace("<PublishAot>true</PublishAot>", "<PublishAot>true</PublishAot><NoWarn>NKSPLG001;CS1591</NoWarn>"));
        Assert.True(Has(await PluginValidator.ValidateAsync(folder, Options(o => o with { SourcePath = src })), FindingSeverity.Error, "aot", "switches off NKSPLG001"));

        File.WriteAllText(Path.Combine(src, "Plugin.csproj"), GoodProject.Replace("<PublishAot>true</PublishAot>", "<PublishAot>false</PublishAot>"));
        Assert.True(Has(await PluginValidator.ValidateAsync(folder, Options(o => o with { SourcePath = src })), FindingSeverity.Warning, "aot", "does not set PublishAot"));
    }

    private static string SampleFolder(string permissions)
    {
        // the sample plugin (samples/csharp-hello) as it was built for the tests, started through the dotnet host with the full path a manifest needs
        string Json(string text) => JsonSerializer.Serialize(text);
        var main = $$"""{ "executable": {{Json(T.DotnetFullPath)}}, "args": [{{Json(T.SampleDll)}}], "activationEvents": ["onStartup"] }""";
        var manifest = Manifest(permissions: permissions, main: main).Replace("\"permissions\"", "\"contributes\": { \"commands\": [{ \"id\": \"example.probe.shout\", \"title\": \"Shout\" }] },\n  \"permissions\"");
        return Folder(manifest);
    }

    private const string ShoutSession = """
        {
          "documents": [{ "name": "a.txt", "text": "hello world\n", "selections": [[0, 5]] }],
          "steps": [
            { "kind": "command", "name": "example.csharp-hello.shout", "document": "a.txt", "status": "applied" },
            { "kind": "expectText", "document": "a.txt", "text": "HELLO world\n" }
          ]
        }
        """;

    [Fact]
    public async Task A_running_plugin_answers_the_handshake_and_refuses_an_unknown_method_and_a_recorded_session_runs()
    {
        var folder = SampleFolder("""["document.read", "document.edit.direct"]""");
        var scenario = Path.Combine(folder, "session.json");
        File.WriteAllText(scenario, ShoutSession);

        var report = await PluginValidator.ValidateAsync(folder, Options(o => o with { Run = true, ScenarioPath = scenario }));

        Assert.True(report.Passed, string.Join("\n", report.Findings));
        Assert.True(Has(report, FindingSeverity.Info, "handshake", "plugin API version 1"));
        Assert.True(Has(report, FindingSeverity.Info, "rpc", "method-not-found"));
        Assert.True(Has(report, FindingSeverity.Info, "scenario", "ran as recorded"));
        Assert.True(Has(report, FindingSeverity.Info, "permissions", "used 'document.edit.direct'") && Has(report, FindingSeverity.Info, "permissions", "used 'document.read'"));
        Assert.True(Has(report, FindingSeverity.Warning, "aot", "through dotnet"));
    }

    [Fact]
    public async Task A_session_shows_a_permission_the_plugin_uses_and_the_manifest_does_not_declare()
    {
        var folder = SampleFolder("""["document.read"]""");
        var scenario = Path.Combine(folder, "session.json");
        File.WriteAllText(scenario, ShoutSession);

        var report = await PluginValidator.ValidateAsync(folder, Options(o => o with { Run = true, ScenarioPath = scenario }));

        Assert.False(report.Passed);
        Assert.True(Has(report, FindingSeverity.Error, "permissions", "needs 'document.edit.direct'"));
        Assert.Contains(report.Findings, f => f is { Severity: FindingSeverity.Error, Check: "scenario" });
    }

    [Fact]
    public async Task A_plugin_that_ends_before_the_handshake_is_a_handshake_error_with_what_the_editor_saw()
    {
        var main = $$"""{ "executable": {{JsonSerializer.Serialize(T.DotnetFullPath)}}, "args": ["--version"], "activationEvents": ["onStartup"] }""";
        var folder = Folder(Manifest(main: main));

        var report = await PluginValidator.ValidateAsync(folder, Options(o => o with { Run = true }));

        Assert.False(report.Passed);
        Assert.Contains(report.Findings, f => f is { Severity: FindingSeverity.Error, Check: "handshake" });
    }

    [Fact]
    public async Task A_scenario_without_a_run_is_refused_and_a_plugin_that_cannot_be_loaded_is_not_started()
    {
        var withoutRun = await PluginValidator.ValidateAsync(Folder(Manifest()), Options(o => o with { ScenarioPath = "session.json" }));
        var invalid = await PluginValidator.ValidateAsync(Folder(Manifest("{}")), Options(o => o with { Run = true }));

        Assert.True(Has(withoutRun, FindingSeverity.Error, "scenario", "add the option"));
        Assert.True(Has(invalid, FindingSeverity.Error, "handshake", "not started"));
    }

    [Fact]
    public async Task The_command_line_ends_with_the_code_0_for_a_valid_plugin_1_for_an_invalid_one_and_2_for_a_wrong_line()
    {
        var good = new StringWriter();
        var bad = new StringWriter();
        var wrong = new StringWriter();

        var valid = await Cli.RunAsync([Folder(Manifest())], good, wrong);
        var invalid = await Cli.RunAsync([Folder(Manifest("{}"))], bad, wrong);
        var usage = await Cli.RunAsync(["--nothing"], good, wrong);
        var noFolder = await Cli.RunAsync([], good, wrong);

        Assert.Equal((0, 1, 2, 2), (valid, invalid, usage, noFolder));
        Assert.Contains("0 errors", good.ToString());
        Assert.Contains("[error] api:", bad.ToString());
        Assert.Contains("Usage:", wrong.ToString());
    }

    [Fact]
    public void The_json_schema_in_the_sdk_package_is_the_one_made_from_the_types_the_editor_reads()
    {
        var file = File.ReadAllText(Path.Combine(T.RepoRoot(), "src", "NksHub.Textida.Plugin.Sdk", "schemas", "nkspad-plugin.schema.json"));

        Assert.Equal(Normalize(Cli.Schema()), Normalize(file));
        using var schema = JsonDocument.Parse(file);
        Assert.Equal("integer", schema.RootElement.GetProperty("properties").GetProperty("api").GetProperty("properties").GetProperty("min").GetProperty("type").GetString());
    }

    [Fact]
    public async Task The_sample_plugin_of_the_sdk_has_a_manifest_that_passes_everything_but_the_path_of_its_program()
    {
        var folder = T.TempDir();
        File.Copy(Path.Combine(T.RepoRoot(), "samples", "csharp-hello", "nkspad-plugin.json"), Path.Combine(folder, "nkspad-plugin.json"));

        var report = await PluginValidator.ValidateAsync(folder, Options());

        // the sample names a program that is not on this machine; every other rule holds
        Assert.Equal(["main.executable '/path/to/NksHub.Textida.Samples.CsHello' does not exist."], report.Of(FindingSeverity.Error).Select(f => f.Message));
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd();
}
