using System.Diagnostics;
using NksHub.Textida.Remote;

namespace NksHub.Textida.Remote.Tests;

/// <summary>
/// The NuGet restore of a real project through the tool proxy, with the dotnet program itself: it takes the proxy from the environment the proxy gives it, is refused before the user allowed the feed hosts and
/// works after. The packages go to a folder of the test, so that the restore needs the network.
/// </summary>
public sealed class ToolProxyRestoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "textida-restore-" + Guid.NewGuid().ToString("N")[..8]);

    public ToolProxyRestoreTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private async Task<(int ExitCode, string Output)> RestoreAsync(ToolProxy proxy, string project)
    {
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", ["restore", project, "--no-cache", "--force", "-v", "m"])
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _folder,
        };
        info.Environment["NUGET_PACKAGES"] = Path.Combine(_folder, "packages");
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        proxy.Apply(info, "dotnet");
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(240_000)) { process.Kill(true); return (-1, "timeout"); }
        return (process.ExitCode, await output + await error);
    }

    [Fact]
    [Trait("Category", "RealTools")]
    public async Task A_restore_through_the_proxy_is_refused_before_the_user_allowed_the_feed_hosts_and_works_after()
    {
        string project = Path.Combine(_folder, "demo.csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n  <ItemGroup>\n    <PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />\n  </ItemGroup>\n</Project>\n");
        File.WriteAllText(Path.Combine(_folder, "nuget.config"), "<configuration><packageSources><clear /><add key=\"nuget\" value=\"https://api.nuget.org/v3/index.json\" /></packageSources></configuration>\n");
        using var proxy = new ToolProxy();
        var consent = new ToolNetworkConsent(Path.Combine(_folder, "tool-network.json"));
        proxy.Consent = consent;
        proxy.Register("dotnet", ["api.nuget.org", "globalcdn.nuget.org"]);

        var refused = await RestoreAsync(proxy, project);
        Assert.NotEqual(0, refused.ExitCode);
        Assert.Contains("api.nuget.org:443", proxy.Refused);
        Assert.False(Directory.Exists(Path.Combine(_folder, "packages", "newtonsoft.json"))); // nothing was downloaded

        consent.Allow("dotnet", ["api.nuget.org", "globalcdn.nuget.org"]);
        var allowed = await RestoreAsync(proxy, project);
        Assert.True(allowed.ExitCode == 0, allowed.Output);
        Assert.Contains("Newtonsoft.Json", File.ReadAllText(Path.Combine(_folder, "obj", "project.assets.json")));
    }
}
