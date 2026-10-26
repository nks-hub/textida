using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using NksHub.Textida.App.Profile;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Startup;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App;

/// <summary>What <see cref="Program"/> decided before Avalonia started.</summary>
internal sealed record StartupContext(CommandLineArgs Args, ProfileStore Profile, string Channel, IDisposable? InstanceLock);

public sealed partial class App : Application
{
    internal static StartupContext Startup { get; set; } = null!;

    internal Settings Settings { get; private set; } = new();
    private readonly List<MainWindow> _windows = [];
    private MainWindow? _active;
    private IpcServer? _server;
    private IClassicDesktopStyleApplicationLifetime? _desktop;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        _desktop = desktop;
        var ctx = Startup;
        Settings = ctx.Args.Smoke ? new Settings() : ctx.Profile.LoadSettings();
        Loc.SetLanguage(Settings.Language);
        ApplyThemeVariant();

        var first = CreateWindow(ctx.Args.Smoke ? null : ctx.Profile.LoadWindow(), show: false);
        desktop.MainWindow = first;

        if (!ctx.Args.Smoke)
        {
            _server = new IpcServer(ctx.Channel, request => Dispatcher.UIThread.InvokeAsync(() => OpenRequestAsync(request)));
            desktop.Exit += (_, _) => StopServer();
            first.Opened += (_, _) => { if (ctx.Args.Files.Count > 0) RunStartupOpen(ctx.Args); };
        }
        else first.Opened += (_, _) => _ = new SmokeTest(this, first, desktop).RunAsync();

        base.OnFrameworkInitializationCompleted();
    }

    private void RunStartupOpen(CommandLineArgs args)
    {
        var request = new IpcRequest { NewWindow = false, Wait = false, Files = args.Files.Select(f => new IpcFile { Path = f.Path, Line = f.Line }).ToList() };
        _ = OpenRequestAsync(request).ContinueWith(t => { if (t.Exception is { } e) _active?.SetStatusMessage(e.InnerException?.Message ?? e.Message); }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void StopServer()
    {
        if (_server is null) return;
        _server.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
        _server = null;
    }

    internal MainWindow CreateWindow(SavedWindow? saved, bool show = true)
    {
        var window = new MainWindow(this, ShellLook.ForThisMachine(), saved);
        window.Activated += (_, _) => _active = window;
        _windows.Add(window);
        _active = window;
        if (show) window.Show();
        return window;
    }

    /// <summary>Opens another window slightly offset from <paramref name="from"/>.</summary>
    internal MainWindow NewWindow(MainWindow? from)
    {
        var g = from?.CaptureGeometry();
        if (g is not null) { g.X += 32; g.Y += 32; g.Maximized = false; }
        return CreateWindow(g);
    }

    internal void WindowClosed(MainWindow window)
    {
        if (!Startup.Args.Smoke)
        {
            try { Startup.Profile.SaveWindow(window.CaptureGeometry()); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* losing the window position must not block closing */ }
        }
        _windows.Remove(window);
        if (_active == window) _active = _windows.LastOrDefault();
    }

    internal void Exit() => _desktop?.Shutdown();

    internal IReadOnlyList<MainWindow> Windows => _windows;

    /// <summary>
    /// Runs on the UI thread. The returned task completes when every requested tab is closed if <see cref="IpcRequest.Wait"/> is set, otherwise at once.
    /// Fails with the first file that could not be read, after opening the others.
    /// </summary>
    internal Task OpenRequestAsync(IpcRequest request)
    {
        var window = request.NewWindow || _active is null ? NewWindow(_active) : _active;
        var waits = new List<Task>();
        Exception? failure = null;
        foreach (var f in request.Files)
        {
            try
            {
                var owner = _windows.FirstOrDefault(w => w.Tabs.Any(t => string.Equals(t.Path, Path.GetFullPath(f.Path), MainWindow.PathComparison))) ?? window;
                var tab = owner.OpenFile(f.Path, f.Line);
                if (!ReferenceEquals(owner, window)) window = owner;
                if (request.Wait) waits.Add(tab.Closed.Task);
            }
            catch (IOException e) { failure ??= e; }
        }

        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        if (failure is not null) return Task.FromException(failure);
        return waits.Count == 0 ? Task.CompletedTask : Task.WhenAll(waits);
    }

    // ---- settings ----

    internal void SetTheme(string tag)
    {
        Settings.Theme = tag;
        ApplyThemeVariant();
        SaveSettings();
        foreach (var w in _windows) w.ApplyTexts();
    }

    internal void SetLanguage(string tag)
    {
        Settings.Language = Loc.SetLanguage(tag);
        SaveSettings();
    }

    private void ApplyThemeVariant() => RequestedThemeVariant = Settings.Theme switch
    {
        "light" => ThemeVariant.Light,
        "dark" => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    private void SaveSettings()
    {
        if (Startup.Args.Smoke) return;
        try { Startup.Profile.SaveSettings(Settings); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _active?.SetStatusMessage(e.Message); }
    }
}
