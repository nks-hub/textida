using System.Diagnostics;
using System.Globalization;

namespace NksHub.Textida.Core.Tools;

/// <summary>
/// How a plugin process notices that the editor which started it is gone: the editor names itself in the environment (<see cref="ChildProcessGuard.PassTo"/>) and says how often to look;
/// the plugin then ends by itself when that process no longer runs under the same start time.
/// </summary>
/// <param name="Parent">The editor.</param>
/// <param name="Interval">Time between two looks.</param>
/// <param name="StartTolerance">How far apart two readings of the start time of the editor may be.</param>
public sealed record ParentWatch(ProcessKey Parent, TimeSpan Interval, TimeSpan StartTolerance)
{
    /// <summary>Environment of a plugin process: milliseconds between two looks at the editor. Absent: the plugin does not look.</summary>
    public const string PollVariable = "TEXTIDA_PARENT_POLL_MS";

    /// <summary>Environment of a plugin process: milliseconds two readings of the start time of the editor may differ by.</summary>
    public const string ToleranceVariable = "TEXTIDA_START_TOLERANCE_MS";

    /// <summary>Adds to <paramref name="info"/> (together with <see cref="ChildProcessGuard.PassTo"/>) what a plugin needs to watch the editor.</summary>
    public static void Apply(ProcessStartInfo info, TimeSpan interval, TimeSpan startTolerance)
    {
        info.Environment[PollVariable] = ((long)interval.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
        info.Environment[ToleranceVariable] = ((long)startTolerance.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The watch this process was asked for, or null when the environment does not ask for one or is not readable.</summary>
    public static ParentWatch? FromEnvironment() =>
        TryNumber(ChildProcessGuard.ParentPidVariable, out var pid) && TryNumber(ChildProcessGuard.ParentStartVariable, out var ticks)
        && TryNumber(PollVariable, out var poll) && poll > 0 && TryNumber(ToleranceVariable, out var tolerance) && tolerance > 0 && pid is > 0 and <= int.MaxValue
            ? new ParentWatch(new ProcessKey((int)pid, ticks), TimeSpan.FromMilliseconds(poll), TimeSpan.FromMilliseconds(tolerance)) : null;

    /// <summary>True when the editor does not run any more.</summary>
    public bool IsParentGone() => ChildProcessGuard.IsGone(Parent, StartTolerance);

    /// <summary>Completes when the editor is gone. Looks every <see cref="Interval"/> until <paramref name="ct"/> is cancelled.</summary>
    public async Task WaitUntilGoneAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                if (IsParentGone()) return;
        }
        catch (OperationCanceledException) { }
    }

    private static bool TryNumber(string variable, out long value) =>
        long.TryParse(Environment.GetEnvironmentVariable(variable), NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
