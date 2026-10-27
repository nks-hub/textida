using System.Diagnostics;

namespace NksHub.Textida.Core.Tools;

/// <summary>
/// The one way to start a resolved program with an explicit argument list. A Windows batch file (<c>.bat</c>, <c>.cmd</c>) is started through <c>cmd.exe</c> with a command line quoted for it
/// (see <see cref="BatchCommandLine"/>, CVE-2024-24576); any other program gets the plain <see cref="ProcessStartInfo.ArgumentList"/>. Code that starts a program itself uses this instead of building the start info.
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
}
