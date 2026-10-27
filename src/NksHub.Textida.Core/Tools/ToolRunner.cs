using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace NksHub.Textida.Core.Tools;

/// <summary>
/// A tool as its definition states it: variables are still in the text. <paramref name="Timeout"/> is null for no limit.
/// </summary>
public sealed record ToolSpec(string Id, string Executable, IReadOnlyList<string> Args, string? Cwd = null, TimeSpan? Timeout = null);

/// <summary>
/// A tool with every variable replaced: the program (a full path), one entry per argument and the directory it runs in. This is exactly what is shown
/// before the first run and what is started; nothing is joined into a command line.
/// </summary>
public sealed record ToolInvocation(string ToolId, string Executable, IReadOnlyList<string> Args, string WorkingDirectory, TimeSpan? Timeout, string Fingerprint)
{
    /// <summary>The command for the eyes of the user. Arguments with spaces or quotes are put in quotes here only; the process gets the plain list.</summary>
    public string CommandLine => string.Join(' ', new[] { Quote(Executable) }.Concat(Args.Select(Quote)));

    private static string Quote(string arg) =>
        arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"', '\'', '&', '|', '<', '>', '^', '%']) < 0 ? arg : "\"" + arg.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

public enum ToolEnd { Exited, TimedOut, Cancelled }

/// <param name="ExitCode">The code the process ended with; after a time-out or a cancel it is the code of the killed process.</param>
public sealed record ToolResult(ToolEnd End, int ExitCode);

/// <summary>
/// Starts external programs with an explicit argument list. No shell is involved: the arguments go to the process one by one, so text from the
/// document or a variable can hold spaces, quotes or shell operators without changing what runs. Batch files are refused on Windows because the
/// command interpreter that runs them splits and expands arguments again.
/// </summary>
public static class ToolRunner
{
    private static readonly TimeSpan DrainGrace = TimeSpan.FromSeconds(2);

    /// <summary>Replaces the variables and checks that the program and the directory exist. Throws <see cref="ToolException"/> with a message saying which part is wrong.</summary>
    /// <param name="defaultDirectory">The directory when the definition names none.</param>
    public static ToolInvocation Resolve(ToolSpec spec, Func<string, string?> variables, string defaultDirectory)
    {
        if (string.IsNullOrWhiteSpace(spec.Executable)) throw new ToolException("The tool has no program.");
        string cwd = spec.Cwd is { Length: > 0 } c ? ToolVariables.Expand(c, variables) : defaultDirectory;
        if (!Directory.Exists(cwd)) throw new ToolException($"The directory '{cwd}' does not exist.");
        cwd = Path.GetFullPath(cwd);
        string exe = FindExecutable(ToolVariables.Expand(spec.Executable, variables), cwd);
        var args = spec.Args.Select(a => ToolVariables.Expand(a, variables)).ToArray();
        return new ToolInvocation(spec.Id, exe, args, cwd, spec.Timeout, Fingerprint(spec, exe));
    }

    /// <summary>
    /// What an approval is tied to: the definition as written (with its variables), the full path of the program it resolves to and the
    /// directory text. Changing any of them asks again; a different file in the same place does not.
    /// </summary>
    internal static string Fingerprint(ToolSpec spec, string resolvedExecutable)
    {
        var sb = new StringBuilder();
        foreach (var part in new[] { spec.Id, spec.Executable, resolvedExecutable, spec.Cwd ?? "" }.Concat(spec.Args)) sb.Append(part.Length).Append(':').Append(part).Append('\0');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static string FindExecutable(string name, string cwd)
    {
        string? found = null;
        if (name.IndexOfAny(['/', '\\']) >= 0 || Path.IsPathRooted(name))
        {
            string full = Path.GetFullPath(name, cwd);
            found = ExistingFile(full);
        }
        else
        {
            // the lookup is done here, in the PATH only: the process API would also look in the working directory first
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try { found = ExistingFile(Path.Combine(dir.Trim('"'), name)); }
                catch (ArgumentException) { continue; }
                if (found is not null) break;
            }
        }
        if (found is null) throw new ToolException($"The program '{name}' was not found.");
        if (OperatingSystem.IsWindows() && Path.GetExtension(found) is { } ext && (ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) || ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase)))
            throw new ToolException($"'{Path.GetFileName(found)}' is a batch file; run its program directly, a batch file would split the arguments again.");
        return found;
    }

    private static string? ExistingFile(string path)
    {
        if (OperatingSystem.IsWindows() && Path.GetExtension(path).Length == 0)
            foreach (var ext in new[] { ".exe", ".com" })
                if (File.Exists(path + ext)) return path + ext;
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Runs the tool and passes every line of its output (standard output and error together, in arrival order) to <paramref name="onLine"/>.
    /// Cancelling <paramref name="ct"/> or running over the time limit ends the process and everything it started. Throws <see cref="ToolException"/> when the process cannot be started.
    /// </summary>
    public static async Task<ToolResult> RunAsync(ToolInvocation tool, Action<string> onLine, CancellationToken ct = default)
    {
        var info = new ProcessStartInfo(tool.Executable)
        {
            WorkingDirectory = tool.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in tool.Args) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException) { throw new ToolException(e.Message); }

        process.StandardInput.Close();
        var gate = new object();
        bool open = true;
        Task Pump(StreamReader reader) => Task.Run(async () =>
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                lock (gate)
                    if (open) onLine(line);
        });
        var pumps = Task.WhenAll(Pump(process.StandardOutput), Pump(process.StandardError));

        using var limit = tool.Timeout is { } t ? new CancellationTokenSource(t) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, limit.Token);
        var end = ToolEnd.Exited;
        try { await process.WaitForExitAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            end = limit.IsCancellationRequested ? ToolEnd.TimedOut : ToolEnd.Cancelled;
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* ended by itself in the meantime */ }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        try { await pumps.WaitAsync(DrainGrace).ConfigureAwait(false); }
        catch (TimeoutException) { /* a program it started still holds the pipes: the output ends here */ }
        lock (gate) open = false;
        return new ToolResult(end, process.ExitCode);
    }
}
