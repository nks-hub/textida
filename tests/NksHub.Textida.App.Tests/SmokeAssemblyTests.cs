using NksHub.Textida.Core.Dotnet;

namespace NksHub.Textida.App.Tests;

/// <summary>The assembly the --smoke check opens is in the program as a resource and decompiles to the type the check looks for.</summary>
public class SmokeAssemblyTests
{
    [Fact]
    public void The_embedded_smoke_assembly_decompiles_to_the_known_type()
    {
        using var resource = typeof(App).Assembly.GetManifestResourceStream("smoke/SmokeAssembly.dll");
        Assert.NotNull(resource);
        string path = Path.Combine(Path.GetTempPath(), "smoke-" + Guid.NewGuid().ToString("N")[..8] + ".dll");
        try
        {
            using (var file = File.Create(path)) resource.CopyTo(file);

            var result = AssemblyDecompiler.Decompile(path);

            Assert.Contains("class SmokeAssemblyMarker", result.Text);
            Assert.Contains("left * right", result.Text);
        }
        finally { File.Delete(path); }
    }
}
