using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Headless;
using NksHub.Textida.Setup;
using NksHub.Textida.Setup.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
[assembly: SupportedOSPlatform("windows")]

namespace NksHub.Textida.Setup.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<SetupApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

internal static class Repo
{
    public static string Root { get; } = Find();

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NksHub.Textida.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("NksHub.Textida.sln not found above " + AppContext.BaseDirectory);
    }
}

/// <summary>What the setup did outside its window, instead of doing it: programs started, setups scheduled for removal, processes waited for.</summary>
internal sealed class Recorder
{
    public List<(string Exe, string Dir)> Launched { get; } = [];
    public List<(string Program, string Dir)> Removed { get; } = [];
    public List<int> Waited { get; } = [];
}
