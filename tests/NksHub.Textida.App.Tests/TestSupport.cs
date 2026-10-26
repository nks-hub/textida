using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using NksHub.Textida.App.Profile;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Startup;
using NksHub.Textida.Core.Encodings;

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

    public string Bytes(string name, byte[] content)
    {
        var p = System.IO.Path.Combine(Path, name);
        System.IO.File.WriteAllBytes(p, content);
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>Answers dialogs from a script instead of opening windows. Unscripted questions fail the test.</summary>
internal sealed class ScriptedDialogs : IDialogs
{
    public Queue<int> Answers { get; } = new();
    public List<ChoiceRequest> Asked { get; } = [];
    public Queue<SaveAsChoice?> SaveAs { get; } = new();
    public List<SaveAsRequest> SaveAsAsked { get; } = [];
    public Queue<string?> Prompts { get; } = new();
    public List<string> Picked { get; } = [];

    public Task<int> ChooseAsync(Window owner, ChoiceRequest request)
    {
        Asked.Add(request);
        if (Answers.Count == 0) throw new InvalidOperationException("Unexpected dialog: " + request.Title + " / " + request.Message);
        return Task.FromResult(Answers.Dequeue());
    }

    public Task<SaveAsChoice?> SaveAsAsync(Window owner, SaveAsRequest request)
    {
        SaveAsAsked.Add(request);
        return Task.FromResult(SaveAs.Count > 0 ? SaveAs.Dequeue() : throw new InvalidOperationException("Unexpected Save As dialog"));
    }

    public Task<IReadOnlyList<string>> PickFilesAsync(Window owner) => Task.FromResult<IReadOnlyList<string>>([.. Picked]);

    public Task<string?> PromptAsync(Window owner, string title, string label, string initial) =>
        Task.FromResult(Prompts.Count > 0 ? Prompts.Dequeue() : null);

    public Task ShowAboutAsync(Window owner) => Task.CompletedTask;
}

internal static class TestApp
{
    /// <summary>Points the static startup context at a throw-away profile (settings, journal and backups live in it) and builds the services of that run.</summary>
    public static App Prepare(TempDir temp, ScriptedDialogs? dialogs = null)
    {
        App.Startup = new StartupContext(CommandLine.Parse(["--smoke"], temp.Path), new ProfileStore(temp.Path), "test", null);
        var app = (App)Application.Current!;
        app.Configure();
        app.Dialogs = dialogs ?? new ScriptedDialogs();
        return app;
    }

    /// <summary>Same profile, new run: the journal of the previous run is left as a crash leaves it (no clean-exit flag).</summary>
    public static App Restart(TempDir temp, App previous, ScriptedDialogs? dialogs = null)
    {
        foreach (var w in previous.Windows.ToList()) w.Close();
        previous.Journal.Dispose();
        return Prepare(temp, dialogs);
    }

    /// <summary>Closes every window and releases the journal so the folder can be deleted.</summary>
    public static void Cleanup(App app)
    {
        foreach (var w in app.Windows.ToList()) w.Close();
        app.Journal.Dispose();
    }

    public static RawInputModifiers Mod => Shortcuts.Primary == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control;

    public static void Press(Window w, Key key, RawInputModifiers mods = RawInputModifiers.None, string? text = null) =>
        w.KeyPress(key, mods, PhysicalKeyFor(key), text);

    private static PhysicalKey PhysicalKeyFor(Key key) => Enum.TryParse<PhysicalKey>(key.ToString(), out var p) ? p : PhysicalKey.None;

    public static void Type(Window w, string text)
    {
        foreach (var c in text) w.KeyTextInput(c.ToString());
    }
}
