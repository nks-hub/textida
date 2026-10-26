using Avalonia;
using Avalonia.Headless;
using NksHub.Textida.App.Profile;
using NksHub.Textida.App.Startup;

[assembly: AvaloniaTestApplication(typeof(NksHub.Textida.App.Tests.TestAppBuilder))]

namespace NksHub.Textida.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

internal static class Repo
{
    public static string Root { get; } = Find();

    public static string AppSources => Path.Combine(Root, "src", "NksHub.Textida.App");

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NksHub.Textida.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("NksHub.Textida.sln not found above " + AppContext.BaseDirectory);
    }
}

/// <summary>Temporary directory removed with the object.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "textida-test-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name, string content = "")
    {
        var p = System.IO.Path.Combine(Path, name);
        System.IO.File.WriteAllText(p, content);
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

internal static class TestApp
{
    /// <summary>Points the static startup context at a throw-away profile; smoke mode keeps the app from writing settings.</summary>
    public static App Prepare(TempDir temp)
    {
        App.Startup = new StartupContext(CommandLine.Parse(["--smoke"], temp.Path), new ProfileStore(temp.Path), "test", null);
        return (App)Application.Current!;
    }
}
