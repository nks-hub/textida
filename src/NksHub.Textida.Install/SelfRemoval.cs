using System.Diagnostics;

namespace NksHub.Textida.Install;

/// <summary>
/// A program cannot delete its own file while it runs. The uninstaller removes everything else, then starts a command prompt that waits a few seconds for the uninstaller to end,
/// deletes its file and removes the folder if nothing else is in it. A folder that still holds the portable profile stays.
/// </summary>
public static class SelfRemoval
{
    public static ProcessStartInfo Command(string program, string dir) => new("cmd.exe", $"/c ping -n {InstallLimitsPlaceholders.SelfRemovalPings} 127.0.0.1 >nul & del /f /q \"{program}\" & rmdir \"{dir}\"")
    {
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    /// <summary>Starts the command and does not wait for it.</summary>
    public static void Schedule(string program, string dir) => Process.Start(Command(program, dir))?.Dispose();
}
