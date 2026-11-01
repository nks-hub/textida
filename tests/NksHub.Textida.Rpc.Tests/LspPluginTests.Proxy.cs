using NksHub.Textida.Plugins.Lsp;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>
/// The tool proxy with the real C# servers: the plugin asks the user once for the hosts of the NuGet feeds, remembers the answer in the consent file, and starts the server with the proxy variables of the tool proxy,
/// so that nothing the test environment names as a proxy is reached (0 connections to the canary, which is where the telemetry of Roslyn went before). Skipped where the server is not installed.
/// The restore of a package through the proxy is tried with the dotnet program itself in <c>ToolProxyRestoreTests</c> of the Remote tests: neither server restores a project by itself.
/// </summary>
public partial class LspPluginTests
{
    private const string ProjectWithPackage = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <OutputType>Library</OutputType>\n  </PropertyGroup>\n  <ItemGroup>\n    <PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />\n  </ItemGroup>\n</Project>\n";

    private static async Task<string> ServeProjectAsync(string server, bool allow)
    {
        string profile = Path.Combine(Path.GetTempPath(), "textida-proxy-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(profile);
        string consent = Path.Combine(profile, "tool-network.json");
        try
        {
            await using var rig = await Rig.CreateAsync(language: "csharp", builtInCatalog: true, startTimeoutMs: 180_000, requestTimeoutMs: 120_000, networkConsent: consent);
            rig.Ui.Confirm = _ => allow;
            File.WriteAllText(Path.Combine(rig.Folder, "demo.csproj"), ProjectWithPackage);
            var doc = rig.Add("A.cs", "using Newtonsoft.Json;\nnamespace Demo;\npublic class A { public string Run() => JsonConvert.SerializeObject(1); }\n");
            await StartedAsync(rig, doc, (server, ServerCatalog.Find(server)!));
            await Task.Delay(8000); // the server has loaded what it loads on its own: a connection it makes goes to the proxy it was given
            return File.Exists(consent) ? File.ReadAllText(consent) : "";
        }
        finally { try { Directory.Delete(profile, true); } catch (IOException) { } }
    }

    private static async Task AServerAsksOnceAndReachesNoProxyOfTheEnvironmentAsync(string server)
    {
        using var canary = new NetCanary(); // every proxy variable of this process points to it; the plugin gives its servers the variables of the tool proxy instead
        string refused = await ServeProjectAsync(server, allow: false);
        Assert.Contains(server + "|-", refused);
        Assert.DoesNotContain("nuget.org", refused);

        string allowed = await ServeProjectAsync(server, allow: true);
        Assert.Contains(server + "|api.nuget.org", allowed);

        Assert.True(canary.Connections == 0, $"{server} reached the proxy of the environment: " + string.Join(", ", canary.Targets.Distinct()));
    }

    [RealToolFact("roslyn-language-server")]
    [Trait("Category", "RealTools")]
    public Task Roslyn_is_asked_about_the_feed_once_and_reaches_no_proxy_of_the_environment() => AServerAsksOnceAndReachesNoProxyOfTheEnvironmentAsync("roslyn-language-server");

    [RealToolFact("csharp-ls")]
    [Trait("Category", "RealTools")]
    public Task Csharp_ls_is_asked_about_the_feed_once_and_reaches_no_proxy_of_the_environment() => AServerAsksOnceAndReachesNoProxyOfTheEnvironmentAsync("csharp-ls");
}
