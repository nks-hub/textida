using System.Diagnostics;

namespace NksHub.Textida.Install;

/// <summary>
/// A program cannot delete its own file while it runs. The uninstaller removes everything else, then starts a command prompt that waits a few seconds for the uninstaller to end,
/// deletes its file and removes the folder if nothing else is in it. A folder that still holds the portable profile stays.
/// </summary>
public static class SelfRemoval
{
    /// <summary>The command; throws <see cref="ArgumentException"/> for a path cmd.exe would not take literally (it expands <c>%name%</c> on the /c line, a quote ends the path).</summary>
    public static ProcessStartInfo Command(string program, string dir)
    {
        if (!IsLiteral(program) || !IsLiteral(dir)) throw new ArgumentException($"cmd.exe cannot be given this path literally: {program}");
        // the cmd.exe of the system folder, not one the current folder or PATH puts first; /d skips the AutoRun commands
        return new(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/d /c ping -n {InstallLimits.SelfRemovalPings} 127.0.0.1 >nul & del /f /q \"{program}\" & rmdir \"{dir}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
    }

    /// <summary>Starts the command and does not wait for it. A path cmd.exe would not take literally is left behind instead.</summary>
    public static void Schedule(string program, string dir)
    {
        if (IsLiteral(program) && IsLiteral(dir)) Process.Start(Command(program, dir))?.Dispose();
    }

    private static bool IsLiteral(string path) => !path.Contains('%') && !path.Contains('"');
}
