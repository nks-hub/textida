using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace NksHub.Textida.PluginSdk;

/// <summary>
/// How a plugin process notices that the editor which started it is gone: the editor names itself in the environment (process id and start time) and says how often to look;
/// the plugin then ends by itself when that process no longer runs under the same start time. The editor side is <c>ParentWatch</c> in the core of the editor; a test keeps the names equal.
/// </summary>
internal sealed class EditorWatch(int pid, long startTicks, TimeSpan interval, TimeSpan startTolerance)
{
    internal const string ParentPidVariable = "TEXTIDA_PARENT_PID";
    internal const string ParentStartVariable = "TEXTIDA_PARENT_START";
    internal const string PollVariable = "TEXTIDA_PARENT_POLL_MS";
    internal const string ToleranceVariable = "TEXTIDA_START_TOLERANCE_MS";

    /// <summary>The watch this process was asked for, or null when the environment does not ask for one or is not readable.</summary>
    public static EditorWatch? FromEnvironment() =>
        TryNumber(ParentPidVariable, out var pid) && TryNumber(ParentStartVariable, out var ticks)
        && TryNumber(PollVariable, out var poll) && poll > 0 && TryNumber(ToleranceVariable, out var tolerance) && tolerance > 0 && pid is > 0 and <= int.MaxValue
            ? new EditorWatch((int)pid, ticks, TimeSpan.FromMilliseconds(poll), TimeSpan.FromMilliseconds(tolerance)) : null;

    /// <summary>True when no process runs with the id of the editor and its start time. False when it runs, and also when it cannot be looked at: only what is certain counts as gone.</summary>
    public bool IsGone()
    {
        Process candidate;
        try { candidate = Process.GetProcessById(pid); }
        catch (ArgumentException) { return true; }
        using (candidate)
        {
            try
            {
                return candidate.HasExited || Math.Abs(candidate.StartTime.ToUniversalTime().Ticks - startTicks) > startTolerance.Ticks;
            }
            catch (InvalidOperationException) { return true; } // ended while it was looked at
            catch (Exception e) when (e is Win32Exception or NotSupportedException) { return false; } // not ours to look at
        }
    }

    /// <summary>Completes when the editor is gone. Looks every interval until <paramref name="ct"/> is cancelled.</summary>
    public async Task WaitUntilGoneAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                if (IsGone()) return;
        }
        catch (OperationCanceledException) { }
    }

    private static bool TryNumber(string variable, out long value) =>
        long.TryParse(Environment.GetEnvironmentVariable(variable), NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
