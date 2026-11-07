using NksHub.Textida.Install;

namespace NksHub.Textida.App.Services;

/// <summary>
/// The integration of this running program with Linux desktops: the settings page turns the folder action "Open in Textida as project" on and off here. It exists only for the installed
/// program, because the entry it writes names the program that is running.
/// </summary>
internal static class LinuxHost
{
    /// <summary>Test seam: stands in for the real data folders.</summary>
    internal static LinuxLocations? LocationsOverride { get; set; }

    /// <summary>Test seam: the program path the entry points at, because the test host is not Textida; it also stands for running on Linux.</summary>
    internal static string? ExeOverride { get; set; }

    /// <summary>True on Linux, where the settings show the section even for a program that is not installed.</summary>
    public static bool Applies => ExeOverride is not null || OperatingSystem.IsLinux();

    public static string? ProgramPath
    {
        get
        {
            if (ExeOverride is { } forced) return forced;
            var exe = Environment.ProcessPath;
            return OperatingSystem.IsLinux() && exe is not null && Path.GetFileName(exe) == "Textida" ? exe : null;
        }
    }

    public static LinuxLocations Locations => LocationsOverride ?? new LinuxLocations();
}
