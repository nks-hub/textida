using System.Diagnostics;
using System.Text;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.App.Tests.RealAgents;

/// <summary>
/// The real programs the <c>RealAgents</c> and <c>RealWord</c> tests run: where they are, what they report and how they are started. Nothing here is a path of one machine: programs
/// are found through PATH, and a test whose program is missing is skipped with the reason.
/// </summary>
internal static class RealTools
{
    /// <summary>The environment variables a parent session of an agent leaves in the shell; an agent started for a test must not take itself for a child of that session.</summary>
    private static bool IsSessionVariable(string name) =>
        name.StartsWith("CLAUDE_CODE_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("MCP_", StringComparison.OrdinalIgnoreCase)
        || name is "CLAUDECODE" or "CLAUDE_PID" or "CLAUDE_EFFORT" || name.StartsWith("CLAUDE_BASH_", StringComparison.OrdinalIgnoreCase);

    /// <summary>The environment of this process without the variables of a session of an agent. <c>CLAUDE_CONFIG_DIR</c> stays: it says which login the agent uses.</summary>
    public static Dictionary<string, string> AgentEnvironment() =>
        Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .Where(e => e.Key is string name && !IsSessionVariable(name) && e.Value is string)
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!, StringComparer.OrdinalIgnoreCase);

    /// <summary>The full path of a program in PATH, or null. On Windows a <c>.cmd</c> shim is found too.</summary>
    public static string? Find(string name)
    {
        string[] suffixes = OperatingSystem.IsWindows() ? [".exe", ".cmd", ".bat"] : [""];
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var suffix in suffixes)
                if (Path.Combine(folder, name + suffix) is var candidate && File.Exists(candidate)) return candidate;
        return null;
    }

    /// <summary>Skips the test, with the reason, unless the program is in PATH. Returns its full path.</summary>
    public static string Require(string name)
    {
        var path = Find(name);
        Assert.SkipUnless(path is not null, $"'{name}' is not in PATH; this test runs a real agent program.");
        return path!;
    }

    /// <summary>What the program prints for <c>--version</c> (the first line), or null when it does not run.</summary>
    public static string? VersionLine(string program, params string[] args)
    {
        var info = StartInfo(program, args.Length == 0 ? ["--version"] : args, Path.GetTempPath());
        try
        {
            using var process = Process.Start(info)!;
            var text = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
            return text.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
    }

    /// <summary>A start info for a program, with the cleaned environment. A <c>.cmd</c> shim goes through cmd.exe the way the editor starts it.</summary>
    public static ProcessStartInfo StartInfo(string program, IEnumerable<string> args, string workingDirectory)
    {
        var info = ProcessLaunch.CreateStartInfo(program, [.. args]);
        info.WorkingDirectory = workingDirectory;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.RedirectStandardInput = true;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.StandardOutputEncoding = new UTF8Encoding(false);
        info.StandardErrorEncoding = new UTF8Encoding(false);
        info.Environment.Clear();
        foreach (var (name, value) in AgentEnvironment()) info.Environment[name] = value;
        return info;
    }

    /// <summary>
    /// Runs the program to its end (or kills it, with everything it started, when <paramref name="timeout"/> passes) and returns what it printed. The standard input is closed at once.
    /// </summary>
    public static async Task<(int ExitCode, string Output, bool TimedOut)> RunAsync(string program, IEnumerable<string> args, string workingDirectory, TimeSpan timeout)
    {
        using var process = Process.Start(StartInfo(program, args, workingDirectory))!;
        process.StandardInput.Close();
        var output = new StringBuilder();
        var pumps = new[]
        {
            Task.Run(async () => { string? l; while ((l = await process.StandardOutput.ReadLineAsync()) is not null) lock (output) output.AppendLine(l); }),
            Task.Run(async () => { string? l; while ((l = await process.StandardError.ReadLineAsync()) is not null) lock (output) output.AppendLine(l); }),
        };
        using var cancel = new CancellationTokenSource(timeout);
        try { await process.WaitForExitAsync(cancel.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { /* ended meanwhile */ }
            await process.WaitForExitAsync();
            await Task.WhenAny(Task.WhenAll(pumps), Task.Delay(2000));
            return (-1, output.ToString(), true);
        }
        await Task.WhenAll(pumps);
        return (process.ExitCode, output.ToString(), false);
    }
}
