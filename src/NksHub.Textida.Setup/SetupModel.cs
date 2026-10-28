using System.Diagnostics;
using System.Runtime.Versioning;
using NksHub.Textida.Install;

namespace NksHub.Textida.Setup;

/// <summary>Everything the setup touches outside its own window. The program uses the real ones; tests replace them so nothing real is read or written.</summary>
[SupportedOSPlatform("windows")]
internal sealed class SetupEnvironment
{
    public required SystemLocations Locations { get; init; }

    /// <summary>The ZIP package to install, a new stream at every call, or null when there is none.</summary>
    public required Func<Stream?> OpenPackage { get; init; }

    /// <summary>The running setup program; a copy of it without the package stays in the install folder as the uninstaller.</summary>
    public string? SetupExe { get; init; }

    public string UiLanguage { get; init; } = SetupText.Detect();

    /// <summary>Replaces the check for a running Textida.exe.</summary>
    public Func<string, bool>? IsRunning { get; init; }

    /// <summary>Starts Textida: the program and its folder.</summary>
    public Action<string, string> Launch { get; init; } = (exe, dir) => Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = dir, UseShellExecute = true })?.Dispose();

    /// <summary>Removes the setup program that ran the uninstall and the folder, once it ended.</summary>
    public Action<string, string> RemoveSelf { get; init; } = SelfRemoval.Schedule;

    /// <summary>Waits for the process of the editor to end before the folders are switched; true when it has.</summary>
    public Func<int, bool> WaitForProcess { get; init; } = pid =>
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.WaitForExit(60_000);
        }
        catch (ArgumentException) { return true; } // already gone
    };

    public static SetupEnvironment ForThisUser(SetupCommand command)
    {
        var self = Environment.ProcessPath;
        return new SetupEnvironment
        {
            Locations = command.UninstallKey is { } key ? new SystemLocations { UninstallKeyPath = key } : new SystemLocations(),
            OpenPackage = command.Payload is { } file ? () => File.OpenRead(file) : () => self is null ? null : PayloadFile.Open(self),
            SetupExe = self,
        };
    }
}

/// <summary>
/// The choices of the user and the work behind the pages, shared by the window and the silent mode so both give the same result. The defaults of a first install are
/// Start menu shortcut, Explorer menu entry and start after the setup; over an installed Textida the choices start as that Textida has them now.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SetupModel
{
    public SetupEnvironment Env { get; }
    public SetupText Text { get; private set; }

    /// <summary>The Textida that is installed now, or null.</summary>
    public InstalledProduct? Installed { get; }

    /// <summary>The version the package carries, or empty when the setup carries none.</summary>
    public string PackageVersion { get; }

    public bool HasPackage => PackageVersion.Length > 0;

    public string Dir { get; set; }
    public bool StartMenu { get; set; } = true;
    public bool Desktop { get; set; }
    public bool ContextMenu { get; set; } = true;
    public bool Launch { get; set; } = true;
    public bool DeleteSettings { get; set; }
    public HashSet<string> Extensions { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The folder this setup lies in when it is the uninstaller of an install, else null.</summary>
    public string? OwnDir { get; }

    public SetupModel(SetupEnvironment env, SetupText? text = null)
    {
        Env = env;
        Text = text ?? new SetupText(env.UiLanguage);
        Installed = NewInstaller().FindInstalled();
        using (var package = env.OpenPackage()) PackageVersion = package is null ? "" : Installer.PackageVersion(package);
        Dir = Installed?.Dir ?? env.Locations.DefaultInstallDir;
        if (Installed is not null)
        {
            var integration = new SystemIntegration(env.Locations, Path.Combine(Installed.Dir, Installer.ProgramFile));
            StartMenu = integration.HasStartMenuShortcut();
            Desktop = integration.HasDesktopShortcut();
            ContextMenu = integration.HasMenu();
            Extensions.UnionWith(integration.AssociatedExtensions());
        }
        if (env.SetupExe is { } self && Path.GetDirectoryName(Path.GetFullPath(self)) is { } own && File.Exists(Path.Combine(own, Installer.ProgramFile))) OwnDir = own;
    }

    public void SetLanguage(string tag) => Text = new SetupText(tag);

    public Installer NewInstaller(Action<SetupStage, double>? progress = null) =>
        Env.IsRunning is { } isRunning ? new Installer(Env.Locations) { Progress = progress, IsRunning = isRunning } : new Installer(Env.Locations) { Progress = progress };

    /// <summary>Takes what the command line names; the rest stays as the first install or the installed program has it. Silent runs do not start the program unless asked.</summary>
    public void Apply(SetupCommand command)
    {
        if (command.Mode == SetupMode.Uninstall) Dir = command.Dir ?? OwnDir ?? Installed?.Dir ?? Dir;
        else if (command.Dir is { } dir) Dir = dir;
        if (command.StartMenu is { } startMenu) StartMenu = startMenu;
        if (command.Desktop is { } desktop) Desktop = desktop;
        if (command.ContextMenu is { } menu) ContextMenu = menu;
        if (command.Extensions is { } extensions) { Extensions.Clear(); Extensions.UnionWith(extensions); }
        Launch = command.Launch ?? (!command.Silent && Launch);
        DeleteSettings = command.DeleteSettings;
        if (command.Language is { } language) SetLanguage(language);
    }

    /// <summary>The sentence that tells why the chosen folder cannot be used, or null when it can.</summary>
    public string? DirProblem()
    {
        try
        {
            var full = Installer.NormalizeDir(Dir);
            return !File.Exists(Path.Combine(full, Installer.ProgramFile)) && Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any()
                ? Text.Get(S.OptionsDirNotEmpty, ("dir", full))
                : null;
        }
        catch (SetupException e) { return Text.Describe(e); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return e.Message; }
    }

    public InstallOptions ToOptions() => new(Dir, StartMenu, Desktop, ContextMenu, [.. ShellIntegration.Extensions.Where(Extensions.Contains)], Text.Get(S.MenuLabel), Env.SetupExe);

    public InstallResult Install(Action<SetupStage, double>? progress = null)
    {
        using var package = Env.OpenPackage() ?? throw new SetupException(SetupError.BadPackage, Installer.ProgramFile);
        return NewInstaller(progress).Install(package, ToOptions());
    }

    /// <summary>Removes the install in <see cref="Dir"/> and returns where the settings stayed (or were).</summary>
    public string Uninstall(Action<SetupStage, double>? progress = null)
    {
        var installer = NewInstaller(progress);
        var data = installer.UserDataLocation(Dir);
        installer.Uninstall(Dir, DeleteSettings, Env.SetupExe);
        if (Env.SetupExe is { } self && File.Exists(self) && IsInside(self, Dir)) Env.RemoveSelf(self, Dir);
        return data;
    }

    public static bool IsInside(string path, string dir) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public string DoneMessage(InstallResult result) => result.Updated
        ? Text.Get(S.DoneUpdated, ("version", result.Version), ("dir", result.Dir), ("previous", result.PreviousDir ?? ""))
        : Text.Get(S.DoneInstalled, ("version", result.Version), ("dir", result.Dir));
}
