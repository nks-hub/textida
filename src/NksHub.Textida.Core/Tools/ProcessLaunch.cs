using System.Diagnostics;

namespace NksHub.Textida.Core.Tools;

/// <summary>
/// The one way to start a resolved program with an explicit argument list. A Windows batch file (<c>.bat</c>, <c>.cmd</c>) is started through <c>cmd.exe</c> with a command line quoted for it
/// (see <see cref="BatchCommandLine"/>, CVE-2024-24576); any other program gets the plain <see cref="ProcessStartInfo.ArgumentList"/>. Code that starts a program itself uses this instead of building the start info, and calls <see cref="Track"/> once the program runs.
/// </summary>
public static class ProcessLaunch
{
    /// <summary>A Windows batch file, which needs the cmd.exe quoting.</summary>
    public static bool IsBatch(string program) => OperatingSystem.IsWindows() && BatchCommandLine.IsBatchFile(program);

    /// <summary>
    /// The start info for <paramref name="program"/> (a full path) with one entry per argument; the caller sets the directory, the redirections and the environment.
    /// Throws <see cref="ToolException"/> for an argument that cannot be passed safely to a batch file (a control character, or <c>!</c> when the batch file turns delayed expansion on).
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(string program, IReadOnlyList<string> args)
    {
        if (IsBatch(program)) return new ProcessStartInfo(BatchCommandLine.Interpreter) { Arguments = BatchCommandLine.Build(program, args) };
        var info = new ProcessStartInfo(program);
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return info;
    }

    /// <summary>
    /// Call right after starting a child with the start info of <see cref="CreateStartInfo"/>, so the child does not outlive this process when it is killed or crashes. On Windows the child joins a job object that the system
    /// closes with this process (what the child starts joins it too); elsewhere it is listed in <see cref="ChildProcessGuard"/> for the next start to end. Never throws: a child that cannot be tracked runs as before.
    /// shortcut: a grandchild started in the instant between the start and this call stays out of the job; the children here start nothing that early, upgrade to a suspended start if one does.
    /// </summary>
    public static void Track(Process process)
    {
        if (OperatingSystem.IsWindows()) ChildJob.Assign(process);
        else ChildProcessGuard.Register(process);
    }
}
