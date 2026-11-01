using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NksHub.Textida.Core.Tools;

/// <summary>A function or method that calls the symbol; <paramref name="Line"/> is the 1-based line where the caller is defined, <paramref name="File"/> relative to the project.</summary>
public sealed record CodeGraphCaller(string Name, string Kind, string File, int Line);

/// <summary>One definition of the symbol with its callers (a name may be defined in several places).</summary>
public sealed record CodeGraphDefinition(string Name, string Kind, string File, int Line, IReadOnlyList<CodeGraphCaller> Callers);

/// <summary>The answer of <c>codegraph callers --json</c>: the definitions, a note of the program (for instance that a file filter matched nothing), and the case where the symbol is not in the graph.</summary>
public sealed record CodeGraphAnswer(string Symbol, IReadOnlyList<CodeGraphDefinition> Definitions, string? Note, bool NotFound, string? Message);

/// <summary>
/// "Who calls this" from the CodeGraph command line (PZ.6.4): <c>codegraph callers --json</c> reads the graph of the project in its <c>.codegraph</c> folder and prints the callers of a symbol. The editor only reads: it never starts the indexing
/// (<c>codegraph init</c>, <c>sync</c>), never writes the folder, and offers the call only where the folder exists. The program is found through <see cref="LanguageCatalog.CodeGraph"/> and started like every catalog tool, behind the loopback proxy of the editor.
/// What the graph says is the state of the files when it was indexed; a tab with unsaved text is newer than that, which the window says.
/// </summary>
public static class CodeGraphCallers
{
    public const int Limit = 50;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>The folder with a <c>.codegraph</c> folder that holds <paramref name="start"/> (itself or the nearest folder above); null when there is none.</summary>
    public static string? ProjectRoot(string start)
    {
        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            for (var dir = new DirectoryInfo(Path.GetFullPath(start)); dir is not null; dir = dir.Parent)
            {
                // the home folder is no project: a graph there (made by mistake) would answer for every file below it
                if (home.Length > 0 && string.Equals(dir.FullName.TrimEnd(Path.DirectorySeparatorChar), home.TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)) return null;
                if (Directory.Exists(Path.Combine(dir.FullName, ".codegraph"))) return dir.FullName;
            }
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { /* no graph there */ }
        return null;
    }

    /// <summary>The arguments: the symbol follows <c>--</c>, so a name that starts with a dash is a name.</summary>
    public static IReadOnlyList<string> Arguments(string root, string symbol, string? fileFilter) =>
        ["callers", "--json", "-p", root, "-l", Limit.ToString(System.Globalization.CultureInfo.InvariantCulture), .. fileFilter is null ? Array.Empty<string>() : ["-f", fileFilter], "--", symbol];

    /// <summary>The invocation to approve and run; the fingerprint is tied to the program file, so the user is asked once per program.</summary>
    public static ToolInvocation Invocation(string program, string root, IReadOnlyList<string> args) =>
        new("codegraph", program, args, root, Timeout, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("codegraph-callers\0" + program))),
            Environment: new Dictionary<string, string> { ["DO_NOT_TRACK"] = "1", ["NODE_USE_ENV_PROXY"] = "1" });

    /// <summary>Reads the output. Text that is not JSON (the program says <c>Symbol "x" not found</c> that way) becomes a not-found answer with that text.</summary>
    public static CodeGraphAnswer Parse(string symbol, string output)
    {
        string text = output.Trim();
        if (!text.StartsWith('{')) return new CodeGraphAnswer(symbol, [], null, true, text.Length == 0 ? null : text.TrimStart('ℹ', ' ').Split('\n')[0].Trim());
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var definitions = new List<CodeGraphDefinition>();
            if (root.TryGetProperty("definitions", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var item in list.EnumerateArray())
                {
                    var def = item.GetProperty("definition");
                    var callers = new List<CodeGraphCaller>();
                    if (item.TryGetProperty("callers", out var calls) && calls.ValueKind == JsonValueKind.Array)
                        foreach (var call in calls.EnumerateArray())
                        {
                            // a file that only imports the symbol is listed by the graph too; it does not call it
                            if (call.TryGetProperty("relationships", out var how) && how.ValueKind == JsonValueKind.Array && !how.EnumerateArray().Any(r => r.GetString() == "calls")) continue;
                            callers.Add(new CodeGraphCaller(Str(call, "name"), Str(call, "kind"), Str(call, "filePath"), call.TryGetProperty("startLine", out var line) && line.TryGetInt32(out int l) ? l : 1));
                        }
                    definitions.Add(new CodeGraphDefinition(Str(def, "name"), Str(def, "kind"), Str(def, "filePath"), def.TryGetProperty("startLine", out var dl) && dl.TryGetInt32(out int d) ? d : 1, callers));
                }
            return new CodeGraphAnswer(root.TryGetProperty("symbol", out var s) ? s.GetString() ?? symbol : symbol, definitions, root.TryGetProperty("note", out var note) ? note.GetString() : null, false, null);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { return new CodeGraphAnswer(symbol, [], null, true, "The answer of codegraph could not be read: " + e.Message); }
    }

    private static string Str(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

    /// <summary>Runs the program and returns the text of its standard output with the exit code; throws <see cref="ToolException"/> when it cannot be started. A run over the time limit or cancelled is ended with its process tree.</summary>
    public static async Task<(string Output, string Errors, int ExitCode, bool TimedOut)> RunAsync(ToolInvocation tool, CancellationToken ct = default)
    {
        var info = ProcessLaunch.CreateStartInfo(tool.Executable, tool.Args);
        info.WorkingDirectory = tool.WorkingDirectory;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardOutputEncoding = info.StandardErrorEncoding = Encoding.UTF8;
        ToolRunner.NetworkGuard?.Invoke(info);
        if (tool.Environment is { } environment) foreach (var (name, value) in environment) info.Environment[name] = value;
        using var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException) { throw new ToolException(e.Message); }
        ProcessLaunch.Track(process);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var limit = tool.Timeout is { } t ? new CancellationTokenSource(t) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, limit.Token);
        bool timedOut = false;
        try { await process.WaitForExitAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            timedOut = limit.IsCancellationRequested;
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* ended by itself in the meantime */ }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            if (!timedOut) throw new OperationCanceledException(ct);
        }
        try { await Task.WhenAll(output, errors).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (TimeoutException) { /* a program it started still holds the pipes */ }
        return (output.IsCompletedSuccessfully ? output.Result : "", errors.IsCompletedSuccessfully ? errors.Result : "", process.ExitCode, timedOut);
    }
}
