using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace NksHub.Textida.Core.Tools;

/// <summary>A process as the system tells it apart from a later one with the same id: the id and the start time in UTC ticks.</summary>
public readonly record struct ProcessKey(int Pid, long StartTicks);

/// <summary>
/// Keeps the children of the editor from outliving it on macOS and Linux, which have no job object (Windows uses a job object, see <see cref="ProcessLaunch.Track"/>).
/// Every child gets a small file in one folder with its process key and the key of the editor that owns it; the next start of the editor ends the children whose files were left behind.
/// A process id is acted on only together with its start time, so a number the system gave to another program later is never killed.
/// A plugin process gets the folder and the key of the editor in its environment (<see cref="PassTo"/>), so what the plugin starts is listed under the editor too.
/// </summary>
public static class ChildProcessGuard
{
    /// <summary>Environment of a plugin process: process id of the editor that started it.</summary>
    public const string ParentPidVariable = "TEXTIDA_PARENT_PID";

    /// <summary>Environment of a plugin process: start time of that editor in UTC ticks.</summary>
    public const string ParentStartVariable = "TEXTIDA_PARENT_START";

    /// <summary>Environment of a plugin process: the folder of the child files.</summary>
    public const string RegistryVariable = "TEXTIDA_CHILD_REGISTRY";

    private const string Extension = ".child";

    private static readonly Lazy<ProcessKey?> Current = new(() =>
    {
        using var self = Process.GetCurrentProcess();
        return KeyOf(self);
    });

    private static string? _folder;
    private static TimeSpan _tolerance;

    /// <summary>The key of this process; null when the system does not tell its start time.</summary>
    public static ProcessKey? Self => Current.Value;

    /// <summary>
    /// Sets the folder of the child files and how far apart two readings of the start time of one process may be (the system reports it to within a fraction of a second only).
    /// Without it no file is written.
    /// </summary>
    public static void Configure(string folder, TimeSpan startTolerance)
    {
        _folder = folder;
        _tolerance = startTolerance;
    }

    /// <summary>The key of the editor that owns the children started here: the one named in the environment of a plugin process, else this process.</summary>
    private static ProcessKey? Owner =>
        int.TryParse(Environment.GetEnvironmentVariable(ParentPidVariable), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
        && long.TryParse(Environment.GetEnvironmentVariable(ParentStartVariable), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ? new ProcessKey(pid, ticks) : Self;

    private static string? Folder => _folder ?? (Environment.GetEnvironmentVariable(RegistryVariable) is { Length: > 0 } fromEnvironment ? fromEnvironment : null);

    /// <summary>Sets the environment a plugin process needs: the key of this editor and, when there is one, the folder of the child files.</summary>
    public static void PassTo(ProcessStartInfo info)
    {
        if (Self is { } self)
        {
            info.Environment[ParentPidVariable] = self.Pid.ToString(CultureInfo.InvariantCulture);
            info.Environment[ParentStartVariable] = self.StartTicks.ToString(CultureInfo.InvariantCulture);
        }
        if (_folder is { } folder) info.Environment[RegistryVariable] = folder;
    }

    /// <summary>Lists a started child in the folder of the child files and takes it out again when it ends. Does nothing without a folder.</summary>
    public static void Register(Process child)
    {
        if (Folder is { } folder && Owner is { } owner) Register(child, folder, owner);
    }

    internal static void Register(Process child, string folder, ProcessKey owner)
    {
        if (KeyOf(child) is not { } key) return;
        var path = Path.Combine(folder, key.Pid.ToString(CultureInfo.InvariantCulture) + Extension);
        try
        {
            Directory.CreateDirectory(folder);
            Write(path, key, owner);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; } // the child runs as without the file
        try
        {
            child.EnableRaisingEvents = true;
            child.Exited += (_, _) => Delete(path);
        }
        catch (InvalidOperationException) { /* it ended already: the next start of the editor finds nothing alive behind the file and removes it */ }
    }

    /// <summary>Ends the children left behind by an editor that is gone (see <see cref="Sweep"/>), in the folder set by <see cref="Configure"/>. Returns how many were ended.</summary>
    public static int SweepLeftovers() => _folder is { } folder ? Sweep(folder, _tolerance) : 0;

    /// <summary>
    /// Ends every child listed in <paramref name="folder"/> whose owning editor no longer runs and which still runs as the same process (same id and a start time within <paramref name="startTolerance"/>),
    /// and removes the files of those children. An id that now belongs to a process of another start time is left alone, and so is a process whose start time cannot be read.
    /// The file of a child whose editor still runs (another window of the profile) is kept. Returns how many were ended.
    /// </summary>
    public static int Sweep(string folder, TimeSpan startTolerance)
    {
        if (!Directory.Exists(folder)) return 0;
        var ended = 0;
        foreach (var path in Directory.GetFiles(folder, "*" + Extension))
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            if (!TryParse(text, out var child, out var owner)) Delete(path);
            else if (IsGone(owner, startTolerance))
            {
                if (child.Pid != Environment.ProcessId && KillIfSame(child, startTolerance)) ended++;
                Delete(path);
            }
        }
        return ended;
    }

    /// <summary>
    /// True when no process runs with this id and the start time of the key (within <paramref name="startTolerance"/>): none has this id, or the one that has started at another time.
    /// False when it runs, and also when it cannot be looked at: only what is certain counts as gone.
    /// </summary>
    public static bool IsGone(ProcessKey key, TimeSpan startTolerance)
    {
        var verdict = Probe(key, startTolerance, out var process);
        process?.Dispose();
        return verdict == Verdict.Different;
    }

    internal static void Write(string path, ProcessKey child, ProcessKey owner) =>
        File.WriteAllText(path, string.Create(CultureInfo.InvariantCulture, $"{child.Pid} {child.StartTicks} {owner.Pid} {owner.StartTicks}"));

    private static bool KillIfSame(ProcessKey key, TimeSpan startTolerance)
    {
        if (Probe(key, startTolerance, out var process) != Verdict.Same) return false;
        using (process)
        {
            try
            {
                process!.Kill(entireProcessTree: true);
                return true;
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException) { return false; }
        }
    }

    private enum Verdict { Same, Different, Unknown }

    /// <summary>Looks at the process with the id of the key; <paramref name="process"/> is the open process when the verdict is <see cref="Verdict.Same"/> (the caller disposes it).</summary>
    private static Verdict Probe(ProcessKey key, TimeSpan startTolerance, out Process? process)
    {
        process = null;
        Process candidate;
        try { candidate = Process.GetProcessById(key.Pid); }
        catch (ArgumentException) { return Verdict.Different; }
        try
        {
            if (candidate.HasExited) return Verdict.Different;
            if (Math.Abs(candidate.StartTime.ToUniversalTime().Ticks - key.StartTicks) > startTolerance.Ticks) return Verdict.Different;
            process = candidate;
            return Verdict.Same;
        }
        catch (InvalidOperationException) { return Verdict.Different; } // ended while it was looked at
        catch (Exception e) when (e is Win32Exception or NotSupportedException) { return Verdict.Unknown; } // not ours to look at
        finally
        {
            if (process is null) candidate.Dispose();
        }
    }

    private static ProcessKey? KeyOf(Process process)
    {
        try { return new ProcessKey(process.Id, process.StartTime.ToUniversalTime().Ticks); }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException) { return null; }
    }

    private static bool TryParse(string text, out ProcessKey child, out ProcessKey owner)
    {
        child = owner = default;
        var part = text.Split(' ');
        if (part.Length != 4
            || !int.TryParse(part[0], NumberStyles.None, CultureInfo.InvariantCulture, out var childPid) || !long.TryParse(part[1], NumberStyles.None, CultureInfo.InvariantCulture, out var childTicks)
            || !int.TryParse(part[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ownerPid) || !long.TryParse(part[3], NumberStyles.None, CultureInfo.InvariantCulture, out var ownerTicks)) return false;
        child = new ProcessKey(childPid, childTicks);
        owner = new ProcessKey(ownerPid, ownerTicks);
        return true;
    }

    private static void Delete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
