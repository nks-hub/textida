using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using NksHub.Textida.App.Profile;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Startup;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Journal;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.App;

/// <summary>What <see cref="Program"/> decided before Avalonia started.</summary>
internal sealed record StartupContext(CommandLineArgs Args, ProfileStore Profile, string Channel, IDisposable? InstanceLock);

public sealed partial class App : Application
{
    internal static StartupContext Startup { get; set; } = null!;

    /// <summary>Theme and language as last applied; the source of truth is the user settings file behind <see cref="Workbench"/>.</summary>
    internal Settings Settings { get; private set; } = new();
    internal AppWorkbench Workbench { get; private set; } = null!;
    internal JournalService Journal { get; private set; } = null!;
    internal BackupService Backups { get; private set; } = null!;
    internal RecentFiles Recent { get; private set; } = null!;
    internal IDialogs Dialogs { get; set; } = new AvaloniaDialogs();
    /// <summary>Folder of the settings, journal and backups of this run; a private subfolder in smoke mode so a check never touches the real profile.</summary>
    internal string ProfileDirectory { get; private set; } = "";

    private readonly List<MainWindow> _windows = [];
    private MainWindow? _active;
    private IpcServer? _server;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer? _sessionTimer;

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
        Configure();

        var first = CreateWindow(ctx.Args.Smoke ? null : ctx.Profile.LoadWindow(), show: false);
        desktop.MainWindow = first;
        desktop.Exit += (_, _) =>
        {
            Journal.Shutdown();
            if (!ctx.Args.Smoke) return;
            try { Directory.Delete(ProfileDirectory, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* a check leaves nothing in the profile it ran in */ }
        };

        if (!ctx.Args.Smoke)
        {
            _server = new IpcServer(ctx.Channel, request => Dispatcher.UIThread.InvokeAsync(() => OpenRequestAsync(request)));
            desktop.Exit += (_, _) => StopServer();
            first.Opened += (_, _) =>
            {
                first.RestoreFrom(Journal);
                first.ShowJournalWarnings();
                if (ctx.Args.Files.Count > 0) RunStartupOpen(ctx.Args);
            };
        }
        else if (PagedMeasure.Requested) first.Opened += (_, _) => _ = new PagedMeasure(this, first, desktop).RunAsync();
        else first.Opened += (_, _) => _ = new SmokeTest(this, first, desktop).RunAsync();

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Builds the services of one run from <see cref="Startup"/>: workbench (commands, keys, settings), journal and backups.
    /// Called once at start; tests call it for each throw-away profile.
    /// </summary>
    internal void Configure()
    {
        var ctx = Startup;
        Journal?.Dispose();
        ProfileDirectory = ctx.Args.Smoke ? Path.Combine(ctx.Profile.Directory, "smoke-" + Environment.ProcessId) : ctx.Profile.Directory;
        Workbench = new AppWorkbench(ProfileDirectory, Loc.DefaultLanguage);
        AppCommands.Register(this);
        Workbench.SettingChanged += OnSettingChanged;

        var settings = Workbench.Settings;
        Settings = new Settings { Theme = settings.GetString("workbench.theme"), Language = ctx.Args.Smoke ? Loc.DefaultLanguage : EffectiveLanguage() };
        Workbench.SetLanguage(Settings.Language);
        Loc.SetLanguage(Settings.Language);
        ApplyThemeVariant();

        string primary = settings.GetString("files.journalDirectory") is { Length: > 0 } p ? p : Path.Combine(ProfileDirectory, "journal");
        string fallback = settings.GetString("files.journalFallbackDirectory") is { Length: > 0 } f ? f : Path.Combine(Path.GetTempPath(), "Textida", "journal-" + Environment.UserName);
        Journal = JournalService.Start(primary, fallback);
        Recent = RecentFiles.Load(ProfileDirectory);
        Backups = new BackupService(new BackupPolicy { Enabled = true, Placement = BackupPlacement.Directory, Directory = Path.Combine(ProfileDirectory, "backups"), KeepVersions = 5 });
    }

    private string EffectiveLanguage()
    {
        var chosen = Workbench.Settings.GetString("workbench.uiLanguage");
        if (chosen != "auto") return chosen;
        var system = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Loc.Languages.Contains(system) ? system : Loc.DefaultLanguage;
    }

    private void RunStartupOpen(CommandLineArgs args)
    {
        var request = new IpcRequest { NewWindow = false, Wait = false, Files = args.Files.Select(f => new IpcFile { Path = f.Path, Line = f.Line, Hex = f.Hex, Offset = f.Offset }).ToList() };
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
        if (_windows.Count > 0) SaveSession();
    }

    internal void Exit() => _desktop?.Shutdown();

    internal IReadOnlyList<MainWindow> Windows => _windows;

    internal MainWindow? ActiveWindow => _active ?? _windows.LastOrDefault();

    // ---- session ----

    /// <summary>The tab layout changed; it is written shortly after, not on every click.</summary>
    internal void SessionChanged()
    {
        _sessionTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _sessionTimer.Tick -= OnSessionTimer;
        _sessionTimer.Tick += OnSessionTimer;
        _sessionTimer.Stop();
        _sessionTimer.Start();
    }

    private void OnSessionTimer(object? sender, EventArgs e)
    {
        _sessionTimer?.Stop();
        SaveSession();
    }

    /// <summary>Writes the tab layout of every window into the journal.</summary>
    internal void SaveSession()
    {
        var groups = new List<SessionGroup>();
        int active = 0;
        foreach (var w in _windows)
        {
            var model = w.CaptureSession();
            if (w == ActiveWindow) active = groups.Count + model.ActiveGroup;
            groups.AddRange(model.Groups);
        }
        Journal.UpdateSession(new SessionModel(groups, active));
    }

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
                if (f.Hex) tab = owner.OpenHex(tab, f.Offset);
                if (!ReferenceEquals(owner, window)) window = owner;
                if (request.Wait) waits.Add(tab.Closed.Task);
            }
            catch (IOException e) { failure ??= e; }
        }

        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        Dispatcher.UIThread.Post(window.FocusActiveView, DispatcherPriority.Background); // the keyboard goes to the document, not to the menu
        if (failure is not null) return Task.FromException(failure);
        return waits.Count == 0 ? Task.CompletedTask : Task.WhenAll(waits);
    }

    // ---- settings ----

    internal void OpenSettings(string? query = null)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else _settingsWindow.Activate();
        if (query is not null) _settingsWindow.SetQuery(query);
    }

    internal SettingsWindow? SettingsView => _settingsWindow;

    internal void SetTheme(string tag) => Workbench.Set("workbench.theme", tag);

    internal void SetLanguage(string tag) => Workbench.Set("workbench.uiLanguage", tag);

    private void OnSettingChanged(string key)
    {
        switch (key)
        {
            case "workbench.theme":
                Settings.Theme = Workbench.Settings.GetString("workbench.theme");
                ApplyThemeVariant();
                foreach (var w in _windows) w.ApplyTexts();
                break;
            case "workbench.uiLanguage":
                Settings.Language = EffectiveLanguage();
                Workbench.SetLanguage(Settings.Language);
                Loc.SetLanguage(Settings.Language);
                foreach (var w in _windows) w.ApplyTexts();
                break;
        }
    }

    private void ApplyThemeVariant() => RequestedThemeVariant = Settings.Theme switch
    {
        "light" => ThemeVariant.Light,
        "dark" => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };
}
