using NksHub.Textida.Install;

namespace NksHub.Textida.App.Services;

/// <summary>
/// The integration of this running program with Windows: the Tools commands and the settings page open it here. It exists only for Textida.exe on Windows,
/// because the entries it writes name the program that is running.
/// </summary>
internal static class ShellHost
{
    /// <summary>What Windows opens to choose the default apps; Textida is offered there for the extensions that were associated.</summary>
    public const string DefaultAppsUri = "ms-settings:defaultapps";

    /// <summary>Test seam: stands in for the real registry keys and folders.</summary>
    internal static SystemLocations? LocationsOverride { get; set; }

    /// <summary>Test seam: the program path that Explorer is given, because the test host is not Textida.exe.</summary>
    internal static string? ExeOverride { get; set; }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static SystemLocations Locations => LocationsOverride ?? new SystemLocations();

    public static string? ProgramPath
    {
        get
        {
            if (ExeOverride is { } forced) return forced;
            var exe = Environment.ProcessPath;
            return OperatingSystem.IsWindows() && exe is not null && Path.GetFileName(exe).Equals("Textida.exe", StringComparison.OrdinalIgnoreCase) ? exe : null;
        }
    }

    /// <summary>The integration of this program, or null when this is not Textida.exe on Windows.</summary>
    public static SystemIntegration? Open() =>
        OperatingSystem.IsWindows() && ProgramPath is { } exe ? new SystemIntegration(Locations, exe) : null;
}
