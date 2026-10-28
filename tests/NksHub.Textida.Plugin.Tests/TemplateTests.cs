using System.Diagnostics;
using System.Runtime.InteropServices;
using NksHub.Textida.Plugin.Validate;

namespace NksHub.Textida.Plugin.Tests;

/// <summary>
/// P8b.2.1: the packages of the SDK are made locally (nothing is published), the template makes a plugin from them, and that plugin builds for the three runtime identifiers, passes its own tests on the
/// test host, is published as a NativeAOT program and passes the validator while it runs. It needs the network to restore the test packages (xunit) and several minutes, so it is a category of its own.
/// A NativeAOT program is linked on the system it runs on, so only the identifier of the machine is published here: win-x64 on Windows, osx-arm64 or osx-x64 on a Mac of that kind;
/// the other two are built (managed code and launcher) but not published.
/// </summary>
[Trait("Category", "Packaging")]
public class TemplateTests
{
    private static readonly string[] Rids = ["win-x64", "osx-arm64", "osx-x64"];

    private static string? HostRid =>
        OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture == Architecture.X64 ? "win-x64"
        : OperatingSystem.IsMacOS() ? (RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64")
        : null;

    private static async Task<string> DotnetAsync(string workingDirectory, TimeSpan timeout, params string[] arguments)
    {
        var psi = new ProcessStartInfo(T.DotnetFullPath) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);
        // The native linker finds the C++ toolchain through vswhere, which Visual Studio does not put on PATH.
        var installer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer");
        if (Directory.Exists(installer)) psi.Environment["PATH"] = Environment.GetEnvironmentVariable("PATH") + Path.PathSeparator + installer;
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en"; // the output is read below
        psi.Environment["VSLANG"] = "1033";
        psi.Environment["PreferredUILang"] = "en-US";
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(timeout);
        var text = await output + await error;
        Assert.True(process.ExitCode == 0, $"dotnet {string.Join(' ', arguments)} ended with {process.ExitCode}:\n{text}");
        return text;
    }

    [Fact]
    public async Task A_plugin_made_from_the_template_builds_for_the_three_rids_passes_its_tests_and_passes_the_validator_as_a_native_program()
    {
        var repo = T.RepoRoot();
        var work = T.TempDir();
        var feed = Path.Combine(work, "feed");
        var hive = Path.Combine(work, "hive");
        var generated = Path.Combine(work, "generated");
        var version = "0.1.0-test." + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        try
        {
            // 1. the packages, made locally with a version nobody has used
            foreach (var project in new[]
            {
                Path.Combine("src", "NksHub.Textida.Plugin.Sdk", "NksHub.Textida.Plugin.Sdk.csproj"),
                Path.Combine("src", "NksHub.Textida.Plugin.TestHost", "NksHub.Textida.Plugin.TestHost.csproj"),
                Path.Combine("src", "NksHub.Textida.Plugin.Templates", "NksHub.Textida.Plugin.Templates.csproj"),
            })
                await DotnetAsync(repo, TimeSpan.FromMinutes(10), "pack", Path.Combine(repo, project), "-c", "Release", $"-p:Version={version}", "-o", feed, "--nologo");

            // 2. the template, in a template hive of its own so the machine's list of templates stays as it is
            await DotnetAsync(work, TimeSpan.FromMinutes(2), "new", "install", Path.Combine(feed, $"NksHub.Textida.Plugin.Templates.{version}.nupkg"), "--debug:custom-hive", hive);
            await DotnetAsync(work, TimeSpan.FromMinutes(2), "new", "nkspad-plugin", "-n", "Probe.Plugin", "-o", generated, "--pluginId", "probe.plugin", "--sdkVersion", version, "--debug:custom-hive", hive);
            Assert.True(File.Exists(Path.Combine(generated, "Probe.Plugin", "nkspad-plugin.json")));
            Assert.Contains("\"id\": \"probe.plugin\"", File.ReadAllText(Path.Combine(generated, "Probe.Plugin", "nkspad-plugin.json")));

            // 3. restore from the local packages and nuget.org, into a package folder of its own
            File.WriteAllText(Path.Combine(generated, "nuget.config"), $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <config><add key="globalPackagesFolder" value="{Path.Combine(work, "packages")}" /></config>
                  <packageSources>
                    <clear />
                    <add key="local" value="{feed}" />
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                  </packageSources>
                </configuration>
                """);
            var solution = Path.Combine(generated, "Probe.Plugin.slnx");
            var plugin = Path.Combine(generated, "Probe.Plugin", "Probe.Plugin.csproj");

            await DotnetAsync(generated, TimeSpan.FromMinutes(10), "build", solution, "--nologo");

            // 4. the tests of the generated plugin run on the test host
            var tests = await DotnetAsync(generated, TimeSpan.FromMinutes(10), "test", solution, "--no-build", "--nologo");
            Assert.Matches(@"Passed:s+4", tests);

            // 5. the plugin builds for each runtime identifier
            foreach (var rid in Rids) await DotnetAsync(generated, TimeSpan.FromMinutes(10), "build", plugin, "-r", rid, "--nologo");

            // 6. published as a native program for this machine, it is what the validator calls valid when it runs
            if (HostRid is not { } host) return;
            await DotnetAsync(generated, TimeSpan.FromMinutes(15), "publish", plugin, "-c", "Release", "-r", host, "--nologo");
            var published = Path.Combine(generated, "Probe.Plugin", "bin", "Release", "net10.0", host, "publish");
            var program = OperatingSystem.IsWindows() ? "Probe.Plugin.exe" : "Probe.Plugin";
            Assert.True(File.Exists(Path.Combine(published, program)), "the published program");
            Assert.True(File.Exists(Path.Combine(published, "nkspad-plugin.json")), "the manifest travels with the program");
            var manifest = Path.Combine(published, "nkspad-plugin.json");
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"./Probe.Plugin\"", $"\"./{program}\""));

            var output = new StringWriter();
            var exit = await Cli.RunAsync([published, "--run", "--scenario", Path.Combine(generated, "Probe.Plugin.Tests", "recordings", "shout.json"), "--source", Path.Combine(generated, "Probe.Plugin")], output, output);

            Assert.True(exit == 0, output.ToString());
            Assert.DoesNotContain("[warning]", output.ToString());
            Assert.Contains("The recorded session (8 steps) ran as recorded.", output.ToString());
        }
        finally
        {
            try { Directory.Delete(work, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void The_template_files_name_the_command_of_the_plugin_the_same_way_everywhere()
    {
        var root = Path.Combine(T.RepoRoot(), "src", "NksHub.Textida.Plugin.Templates", "templates", "nkspad-plugin");
        var files = new[] { "NkspadPlugin/nkspad-plugin.json", "NkspadPlugin/ExamplePlugin.cs", "NkspadPlugin.Tests/recordings/shout.json" };

        foreach (var file in files) Assert.Contains("example.nkspad-plugin.shout", File.ReadAllText(Path.Combine(root, file)));
        Assert.Contains("\"shortName\": \"nkspad-plugin\"", File.ReadAllText(Path.Combine(root, ".template.config", "template.json")));
        Assert.Contains("\"min\": 1, \"max\": 1", File.ReadAllText(Path.Combine(root, "NkspadPlugin", "nkspad-plugin.json"))); // the range holds PluginApi.Version
        Assert.Equal(1, PluginSdk.PluginApi.Version);
    }
}
