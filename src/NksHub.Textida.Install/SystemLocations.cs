using System.Runtime.Versioning;
using Microsoft.Win32;

namespace NksHub.Textida.Install;

/// <summary>
/// Where the install and the system integration of one user write: the classes key, the Apps list key, the shortcut folders and the folder of the settings.
/// The defaults are the real per-user locations; tests point every one of them at a temporary folder or a key of their own, so no test touches the real registry or Start menu.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed record SystemLocations
{
    public const string DefaultUninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Textida";

    /// <summary>Opens the key that stands for <c>HKCU\Software\Classes</c>; the caller disposes it.</summary>
    public Func<RegistryKey> OpenClasses { get; init; } = ShellIntegration.OpenUserClasses;

    /// <summary>Path below <c>HKCU</c> of the entry in Apps &amp; features.</summary>
    public string UninstallKeyPath { get; init; } = DefaultUninstallKeyPath;

    public string StartMenuDir { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.Programs);

    public string DesktopDir { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>The settings and the journal of the user, kept by an uninstall unless the user asks to delete them.</summary>
    public string UserDataDir { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Textida");

    /// <summary>Tells Explorer about changed associations; off in tests.</summary>
    public bool NotifyExplorer { get; init; } = true;

    /// <summary>Where a first install goes unless the user picks another folder.</summary>
    public string DefaultInstallDir { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Textida");
}
