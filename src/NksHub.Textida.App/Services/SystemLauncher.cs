using System.ComponentModel;
using System.Diagnostics;

namespace NksHub.Textida.App.Services;

/// <summary>Hands a file or a folder to the program the system assigns to it: the default app of a file, the file manager for a folder.</summary>
internal static class SystemLauncher
{
    internal enum Platform { Windows, MacOs, Linux }

    public static Platform Current => OperatingSystem.IsWindows() ? Platform.Windows : OperatingSystem.IsMacOS() ? Platform.MacOs : Platform.Linux;

    /// <summary>The process that opens <paramref name="path"/> on the platform: the shell verb on Windows, <c>open</c> on macOS, <c>xdg-open</c> elsewhere.</summary>
    public static ProcessStartInfo StartInfo(string path, Platform platform)
    {
        if (platform == Platform.Windows) return new ProcessStartInfo(path) { UseShellExecute = true };
        var info = new ProcessStartInfo(platform == Platform.MacOs ? "open" : "xdg-open") { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(path);
        return info;
    }

    /// <summary>Starts the process and leaves it running. False when the system cannot start it (no such program, no app assigned to the file).</summary>
    public static bool Start(ProcessStartInfo info)
    {
        try
        {
            // a shell start may hand the work to a process that is already running and give none back
            Process.Start(info)?.Dispose();
            return true;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }
    }
}
