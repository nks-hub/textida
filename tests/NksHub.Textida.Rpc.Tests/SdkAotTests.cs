using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NksHub.Textida.PluginHost;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P3b.5.1: the sample plugin on the internal C# SDK, published with NativeAOT, answers <c>initialize</c> as a real process.</summary>
public class SdkAotTests
{
    static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "NksHub.Textida.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Fact]
    [Trait("Category", "Aot")]
    public async Task TheSamplePluginPublishedWithNativeAotAnswersInitialize()
    {
        // NativeAOT links with the platform toolchain of the same RID, so only the RID of this machine can be proven here (win-x64).
        if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64) return;

        var output = T.TempDir();
        var psi = new ProcessStartInfo(T.DotnetFullPath) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        // The native linker finds the C++ toolchain through vswhere, which Visual Studio does not put on PATH.
        var installer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer");
        if (Directory.Exists(installer)) psi.Environment["PATH"] = Environment.GetEnvironmentVariable("PATH") + Path.PathSeparator + installer;
        foreach (var a in new[] { "publish", Path.Combine(RepoRoot(), "samples", "csharp-hello", "NksHub.Textida.Samples.CsHello.csproj"), "-c", "Release", "-r", "win-x64", "-o", output, "--nologo" })
            psi.ArgumentList.Add(a);
        using (var publish = Process.Start(psi)!)
        {
            var log = publish.StandardOutput.ReadToEndAsync();
            var err = publish.StandardError.ReadToEndAsync();
            await publish.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(8));
            Assert.True(publish.ExitCode == 0, await log + await err);
        }

        var exe = Path.Combine(output, "NksHub.Textida.Samples.CsHello.exe");
        Assert.True(File.Exists(exe), exe);
        Assert.False(File.Exists(Path.Combine(output, "NksHub.Textida.Samples.CsHello.dll")), "a NativeAOT publish has no managed entry assembly");

        await using var host = new PluginHost.PluginHost(PluginHostTests.Options());
        host.Register(new PluginSpec("hello", "Hello", exe, [], ["onCommand:hello"]));
        Assert.Equal(["hello"], await host.ActivateAsync("onCommand:hello"));
        Assert.Equal(PluginState.Running, host.GetState("hello"));

        var greeting = await host.CallAsync("hello", "hello/greet", T.Json("{\"name\":\"Textida\"}"));
        Assert.Equal("Hello, Textida!", greeting.GetProperty("greeting").GetString());
        var error = await Assert.ThrowsAsync<RpcException>(() => host.CallAsync("hello", "nothing"));
        Assert.Equal(RpcErrorCodes.MethodNotFound, error.Code);
    }
}
