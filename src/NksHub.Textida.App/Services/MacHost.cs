using System.Runtime.Versioning;
using NksHub.Textida.Install;

namespace NksHub.Textida.App.Services;

/// <summary>
/// The integration of this running program with macOS: the Tools commands and the System integration window open it here. It exists only for the program that runs on a Mac, because the command,
/// the quick action and the associations name the program that is running.
/// </summary>
internal static class MacHost
{
    /// <summary>Test seam: stands in for the real folders of the user.</summary>
    internal static MacLocations? LocationsOverride { get; set; }

    /// <summary>Test seam: the program path the integration points at, because the test host is not Textida.</summary>
    internal static string? ExeOverride { get; set; }

    /// <summary>Test seam: stands in for Launch Services.</summary>
    internal static ILaunchServices? LaunchServicesOverride { get; set; }

    public static string? ProgramPath => ExeOverride ?? (OperatingSystem.IsMacOS() ? Environment.ProcessPath : null);

    /// <summary>The integration of this program, or null when this is not a program on macOS.</summary>
    public static MacIntegration? Open() =>
        ProgramPath is { } exe ? new MacIntegration(LocationsOverride ?? new MacLocations(), exe, LaunchServicesOverride ?? SystemServices()) : null;

    private static ILaunchServices SystemServices() => OperatingSystem.IsMacOS() ? Real() : new NoLaunchServices();

    [SupportedOSPlatform("macos")]
    private static ILaunchServices Real() => new SystemLaunchServices();

    /// <summary>Stands in where Launch Services does not exist: nothing is associated and nothing can be.</summary>
    private sealed class NoLaunchServices : ILaunchServices
    {
        public string? DefaultHandlerOf(string extension) => null;

        public void SetDefaultHandler(string extension, string bundleId) => throw new IOException("Launch Services exist only on macOS.");
    }
}
