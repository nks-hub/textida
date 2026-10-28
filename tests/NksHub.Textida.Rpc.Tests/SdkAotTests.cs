using System.Diagnostics;
using System.Runtime.InteropServices;
using NksHub.Textida.PluginHost;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>
/// P3b.5.1 and P3b.5.3: the sample plugin and the JSON plugin, both on the internal C# SDK, published with NativeAOT, answer <c>initialize</c> as real processes.
/// NativeAOT links with the toolchain of the same platform, so each test proves the RID of the machine it runs on: win-x64 on Windows, osx-arm64 or osx-x64 on a Mac of that kind.
/// </summary>
public class SdkAotTests
{
    static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "NksHub.Textida.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    /// <summary>The runtime identifier this machine can publish for: NativeAOT links with the toolchain of its own platform. Null where no built-in program is shipped.</summary>
    static string? Rid =>
        OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture == Architecture.X64 ? "win-x64"
        : OperatingSystem.IsMacOS() ? (RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64")
        : null;

    /// <summary>Publishes <paramref name="project"/> (a path below the repository root) for <see cref="Rid"/> and returns the path of the program, which must be a native one.</summary>
    static async Task<string> PublishAsync(string project, string program)
    {
        var output = T.TempDir();
        var psi = new ProcessStartInfo(T.DotnetFullPath) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        // The native linker finds the C++ toolchain through vswhere, which Visual Studio does not put on PATH.
        var installer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer");
        if (Directory.Exists(installer)) psi.Environment["PATH"] = Environment.GetEnvironmentVariable("PATH") + Path.PathSeparator + installer;
        foreach (var a in new[] { "publish", Path.Combine(RepoRoot(), project), "-c", "Release", "-r", Rid!, "-o", output, "--nologo" })
            psi.ArgumentList.Add(a);
        using (var publish = Process.Start(psi)!)
        {
            var log = publish.StandardOutput.ReadToEndAsync();
            var err = publish.StandardError.ReadToEndAsync();
            await publish.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(8));
            Assert.True(publish.ExitCode == 0, await log + await err);
        }

        var exe = Path.Combine(output, program + (OperatingSystem.IsWindows() ? ".exe" : ""));
        Assert.True(File.Exists(exe), exe);
        Assert.False(File.Exists(Path.Combine(output, program + ".dll")), "a NativeAOT publish has no managed entry assembly");
        return exe;
    }

    [Fact]
    [Trait("Category", "Aot")]
    public async Task TheSamplePluginPublishedWithNativeAotAnswersInitialize()
    {
        if (Rid is null) return;
        var exe = await PublishAsync(Path.Combine("samples", "csharp-hello", "NksHub.Textida.Samples.CsHello.csproj"), "NksHub.Textida.Samples.CsHello");

        await using var host = new PluginHost.PluginHost(PluginHostTests.Options());
        host.Register(new PluginSpec("hello", "Hello", exe, [], ["onCommand:hello"]));
        Assert.Equal(["hello"], await host.ActivateAsync("onCommand:hello"));
        Assert.Equal(PluginState.Running, host.GetState("hello"));

        var greeting = await host.CallAsync("hello", "hello/greet", T.Json("{\"name\":\"Textida\"}"));
        Assert.Equal("Hello, Textida!", greeting.GetProperty("greeting").GetString());
        var error = await Assert.ThrowsAsync<RpcException>(() => host.CallAsync("hello", "nothing"));
        Assert.Equal(RpcErrorCodes.MethodNotFound, error.Code);
    }

    [Fact]
    [Trait("Category", "Aot")]
    public async Task TheJsonPluginPublishedWithNativeAotAnswersInitializeAndFormatsAFileOfAnotherEncoding()
    {
        if (Rid is null) return;
        var exe = await PublishAsync(Path.Combine("src", "NksHub.Textida.Plugins.Json", "NksHub.Textida.Plugins.Json.csproj"), "NksHub.Textida.Plugins.Json");

        // without its options the program says so and ends, as the other built-in programs do (the publish check of the package relies on it)
        using (var bare = Process.Start(new ProcessStartInfo(exe) { RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false })!)
        {
            bare.StandardInput.Close();
            await bare.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(2, bare.ExitCode);
        }

        // the handshake is part of activating the plugin; the rest is the CP1250 file with three kinds of line ending of JsonFormatterTests, formatted by the native program
        await JsonFormatterTests.TwoSelectionsInCp1250Async(exe);
    }
}
