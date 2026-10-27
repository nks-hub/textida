using System.Diagnostics;
using System.Text;

namespace NksHub.Textida.Plugins.Formatters;

/// <summary>Where HTML Tidy was looked for and what was found. <see cref="Path"/> is null when no program was found.</summary>
public sealed record TidyLocation(string? Path, IReadOnlyList<string> Searched);

/// <summary>What a Tidy run gave: the formatted text (empty when Tidy produced none), its messages one per line, and its exit code (0 clean, 1 warnings, 2 errors).</summary>
public sealed record TidyRun(string Output, IReadOnlyList<string> Diagnostics, int ExitCode);

/// <summary>
/// HTML Tidy as an external program. It is looked for, in this order, at the path the user configured, next to the plugin (<c>tidy</c> folder), in <c>tools/tidy</c> of the
/// application folder, and in every folder of <c>PATH</c>. Nothing is linked or loaded into the plugin: the text goes to the program on standard input and the result comes
/// back on standard output, the messages on standard error.
/// </summary>
public static class Tidy
{
    public static string ProgramName => OperatingSystem.IsWindows() ? "tidy.exe" : "tidy";

    /// <param name="configured">Path the user set, or empty.</param>
    /// <param name="pluginFolder">Folder of the plugin program; the application folder is two levels up (<c>plugins/formatters</c>).</param>
    /// <param name="path">The value of <c>PATH</c>.</param>
    public static TidyLocation Locate(string? configured, string pluginFolder, string? path)
    {
        var searched = new List<string>();
        string? Check(string candidate)
        {
            searched.Add(candidate);
            return File.Exists(candidate) ? candidate : null;
        }
        if (!string.IsNullOrWhiteSpace(configured) && Check(configured) is { } own) return new TidyLocation(own, searched);
        if (Check(System.IO.Path.Combine(pluginFolder, "tidy", ProgramName)) is { } beside) return new TidyLocation(beside, searched);
        var application = System.IO.Path.GetFullPath(System.IO.Path.Combine(pluginFolder, "..", ".."));
        if (Check(System.IO.Path.Combine(application, "tools", "tidy", ProgramName)) is { } bundled) return new TidyLocation(bundled, searched);
        var folders = (path ?? "").Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var folder in folders)
            if (Check(System.IO.Path.Combine(folder, ProgramName)) is { } onPath) return new TidyLocation(onPath, searched);
        return new TidyLocation(null, searched);
    }

    /// <summary>Runs Tidy on <paramref name="html"/>. Throws <see cref="TimeoutException"/> when it does not finish in <paramref name="timeout"/> (the process is ended).</summary>
    public static async Task<TidyRun> RunAsync(string program, IReadOnlyList<string> arguments, string html, TimeSpan timeout, CancellationToken ct)
    {
        var info = new ProcessStartInfo(program)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("The program did not start.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(limit.Token);
            var errors = process.StandardError.ReadToEndAsync(limit.Token);
            try
            {
                await process.StandardInput.WriteAsync(html.AsMemory(), limit.Token);
                process.StandardInput.Close();
            }
            catch (IOException) { } // Tidy stopped reading; its exit code and messages tell why
            await process.WaitForExitAsync(limit.Token);
            var lines = (await errors).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new TidyRun(await output, lines, process.ExitCode);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException();
        }
    }
}
