using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NksHub.Textida.Core.Tools;

/// <summary>
/// One match of ast-grep. <paramref name="Start"/> and <paramref name="End"/> are byte offsets in the text the program read (a UTF-8 file, or the bytes sent on its standard input); <paramref name="Line"/> is 1-based.
/// <paramref name="Replacement"/> is the text for the range when a rewrite was asked for, else null.
/// </summary>
public sealed record AstGrepMatch(string File, int Start, int End, int Line, int Column, string Text, string? Replacement);

/// <summary>What one run of ast-grep gave: the matches, what it wrote to the error stream, the exit code, and whether the time or the limit of matches ended it.</summary>
public sealed record AstGrepRun(IReadOnlyList<AstGrepMatch> Matches, string Errors, int ExitCode, bool TimedOut, bool LimitReached);

/// <summary>
/// Structural search with ast-grep (PZ.6.5): <c>ast-grep run --json=stream</c> prints one JSON object per match, with the byte range of the match and, for a rewrite, the replacement text. The program is found through
/// <see cref="LanguageCatalog.StructuralSearch"/> and started like every tool of the catalog, behind the loopback proxy the editor sets in <see cref="ToolRunner.NetworkGuard"/>; it is never downloaded or started by a shell.
/// The pattern and the rewrite are passed as separate arguments in the form <c>--pattern=text</c>, so text that starts with a dash is not read as an option. A file open in the editor is not read from disk: its bytes go to the standard input (<c>--stdin</c>).
/// </summary>
public static class AstGrep
{
    public const int MaxMatches = 5000;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    /// <summary>The name ast-grep knows the language by, for a language mode of the editor (and the extension, for TSX); null when ast-grep has no grammar for it.</summary>
    public static string? LanguageFor(string languageId, string? extension = null) => languageId switch
    {
        "javascript" => "js",
        "typescript" => string.Equals(extension, ".tsx", StringComparison.OrdinalIgnoreCase) ? "tsx" : "ts",
        "python" => "py",
        "go" => "go",
        "rust" => "rs",
        "java" => "java",
        "csharp" => "cs",
        "c" => "c",
        "cpp" => "cpp",
        "kotlin" => "kt",
        "lua" => "lua",
        "php" => "php",
        "ruby" => "rb",
        "swift" => "swift",
        "html" => "html",
        "css" => "css",
        "json" => "json",
        "yaml" => "yaml",
        "shell" => "bash",
        "dart" => "dart",
        _ => null,
    };

    /// <summary>The arguments of one run. <paramref name="path"/> is ignored with <paramref name="stdin"/>; <paramref name="excluded"/> are paths (relative to the folder the run starts in) that are left out because the editor sends them on standard input.</summary>
    public static IReadOnlyList<string> Arguments(string pattern, string? rewrite, string language, bool stdin, IEnumerable<string> excluded, string path = ".")
    {
        var args = new List<string> { "run", "--pattern=" + pattern };
        if (!string.IsNullOrEmpty(rewrite)) args.Add("--rewrite=" + rewrite);
        args.Add("--lang=" + language);
        args.Add("--json=stream");
        if (stdin) args.Add("--stdin");
        else
        {
            foreach (var skip in excluded) args.Add("--globs=!" + skip.Replace('\\', '/'));
            args.Add(path);
        }
        return args;
    }

    /// <summary>The invocation to approve and to run. The fingerprint is tied to the program file only, so the user is asked once per program and not for every pattern.</summary>
    public static ToolInvocation Invocation(string program, string folder, IReadOnlyList<string> sampleArgs, TimeSpan? timeout = null) =>
        new("ast-grep", program, sampleArgs, folder, timeout ?? DefaultTimeout, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("structural-search\0" + program))));

    /// <summary>Reads one line of the stream; null for a line that is not a match.</summary>
    public static AstGrepMatch? ParseLine(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("range", out var range) || !range.TryGetProperty("byteOffset", out var bytes)) return null;
            var start = range.GetProperty("start");
            return new AstGrepMatch(root.TryGetProperty("file", out var file) ? file.GetString() ?? "" : "", bytes.GetProperty("start").GetInt32(), bytes.GetProperty("end").GetInt32(),
                start.GetProperty("line").GetInt32() + 1, start.GetProperty("column").GetInt32(), root.TryGetProperty("text", out var text) ? text.GetString() ?? "" : "",
                root.TryGetProperty("replacement", out var replacement) ? replacement.GetString() : null);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return null; }
    }

    /// <summary>
    /// Runs the program. <paramref name="input"/> goes to its standard input, which is closed after it (empty when there is none).
    /// Cancelling, the time limit or <see cref="MaxMatches"/> ends the program and everything it started; what was read until then is returned. Throws <see cref="ToolException"/> when it cannot be started.
    /// </summary>
    public static async Task<AstGrepRun> RunAsync(ToolInvocation tool, byte[]? input, CancellationToken ct = default)
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
        using var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException) { throw new ToolException(e.Message); }
        ProcessLaunch.Track(process);

        var matches = new List<AstGrepMatch>();
        var errors = new StringBuilder();
        bool limit = false;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var outPump = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (ParseLine(line) is not { } match) continue;
                lock (matches)
                {
                    if (matches.Count >= MaxMatches) { limit = true; break; }
                    matches.Add(match);
                }
            }
            if (limit) await stop.CancelAsync().ConfigureAwait(false);
        });
        var errPump = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
                lock (errors) if (errors.Length < 4000) errors.AppendLine(line);
        });
        try
        {
            await using (var stdin = process.StandardInput.BaseStream)
                if (input is { Length: > 0 }) await stdin.WriteAsync(input, stop.Token).ConfigureAwait(false);
        }
        catch (IOException) { /* the program ended before it read everything */ }

        using var timeout = tool.Timeout is { } t ? new CancellationTokenSource(t) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, timeout.Token);
        bool timedOut = false;
        try { await process.WaitForExitAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            timedOut = timeout.IsCancellationRequested;
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* ended by itself in the meantime */ }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            if (ct.IsCancellationRequested && !limit) throw new OperationCanceledException(ct);
        }
        try { await Task.WhenAll(outPump, errPump).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (TimeoutException) { /* a program it started still holds the pipes */ }
        lock (matches) return new AstGrepRun([.. matches], errors.ToString().Trim(), process.ExitCode, timedOut, limit);
    }
}
