using System.Runtime.InteropServices;

namespace NksHub.Textida.App.Startup;

/// <summary>
/// Windows rounds every timed wait up to its timer tick, 15.6 ms unless a process asks for a finer one. While a background garbage collection ran, keystrokes
/// stalled for one whole tick (1000 carets: p95 30 ms, of which 15 ms were such a stall); with a 1 ms tick the same run has p95 21 ms. The request ends with the process,
/// and Windows 11 ignores it while all windows of the process are minimised or hidden.
/// </summary>
internal static partial class TimerResolution
{
    private static int _requested;

    /// <summary>Asks for a 1 ms timer tick once per process; does nothing on other platforms.</summary>
    public static void Request()
    {
        if (!OperatingSystem.IsWindows() || Interlocked.Exchange(ref _requested, 1) == 1) return;
        _ = timeBeginPeriod(1);
    }

    [LibraryImport("winmm.dll")]
    private static partial uint timeBeginPeriod(uint period);
}
