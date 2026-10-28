using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Tools;

/// <summary>NotChecked: the catalog names no versions for the linter. Unreadable: the program did not tell a version (or could not be asked).</summary>
public enum LinterVersionState { NotChecked, Supported, TooOld, TooNew, Unreadable }

/// <param name="Found">The version as the program printed it; null when it could not be read.</param>
/// <param name="Range">The versions the catalog says the linter was written for; null for <see cref="LinterVersionState.NotChecked"/>.</param>
/// <param name="Reason">What went wrong, in English, when <paramref name="State"/> is <see cref="LinterVersionState.Unreadable"/>.</param>
public sealed record LinterVersionCheck(LinterVersionState State, string? Found, CatalogVersionRange? Range, string Reason = "")
{
    /// <summary>The program is a version the arguments and the parser were not written for, so its output may be read wrongly.</summary>
    public bool Mismatch => State is LinterVersionState.TooOld or LinterVersionState.TooNew;
}

/// <summary>
/// Asks a linter's program for its version and compares it with <see cref="CatalogLinter.Supported"/>. The caller runs it only for a program the user has approved, as the version request starts the same program.
/// The answer is kept for the file of the program (path and time of the last change), so the program is started once per session, not on every run. Nothing here downloads or installs a tool.
/// </summary>
public static class LinterVersions
{
    private static readonly ConcurrentDictionary<string, LinterVersionCheck> Known = new(StringComparer.Ordinal);
    private static readonly Regex Number = new(@"(?<!\d)(\d+)\.(\d+)(?:\.(\d+))?", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The first <c>major.minor[.patch]</c> in what a program printed for <c>--version</c> ("PHPStan - PHP Static Analysis Tool 1.10.50", "v9.12.0", "golangci-lint has version 2.1.6 built with go1.24"); null when there is none.</summary>
    public static Version? Parse(string output, out string text)
    {
        text = "";
        try
        {
            var m = Number.Match(output);
            if (!m.Success) return null;
            text = m.Value;
            var culture = CultureInfo.InvariantCulture;
            return new Version(int.Parse(m.Groups[1].ValueSpan, culture), int.Parse(m.Groups[2].ValueSpan, culture), m.Groups[3].Success ? int.Parse(m.Groups[3].ValueSpan, culture) : 0);
        }
        catch (Exception e) when (e is OverflowException or FormatException or RegexMatchTimeoutException) { return null; }
    }

    /// <summary>Compares a version text with the window of the catalog: below <see cref="CatalogVersionRange.Min"/> is too old, at or above <see cref="CatalogVersionRange.Below"/> too new.</summary>
    public static LinterVersionCheck Compare(string output, CatalogVersionRange range)
    {
        if (Parse(output, out string text) is not { } found) return new LinterVersionCheck(LinterVersionState.Unreadable, null, range, "The program printed no version number.");
        var min = new Version(range.Min);
        var state = found < min ? LinterVersionState.TooOld : range.Below is { } below && found >= new Version(below) ? LinterVersionState.TooNew : LinterVersionState.Supported;
        return new LinterVersionCheck(state, text, range);
    }

    /// <summary>
    /// Starts the program of <paramref name="tool"/> with the version arguments of the linter (a short time limit, the environment of the linter) and compares what it prints.
    /// Never throws for a program that is missing, slow or silent: that is <see cref="LinterVersionState.Unreadable"/>, which is not cached.
    /// </summary>
    public static async Task<LinterVersionCheck> CheckAsync(CatalogLinter linter, ToolInvocation tool, TimeSpan timeout, CancellationToken ct = default)
    {
        if (linter.Supported is not { } range) return new LinterVersionCheck(LinterVersionState.NotChecked, null, null);
        string key = Key(linter, tool.Executable);
        if (Known.TryGetValue(key, out var known)) return known;

        var output = new List<string>();
        ToolResult run;
        try { run = await ToolRunner.RunAsync(tool with { Args = linter.VersionArgs ?? ["--version"], Timeout = timeout }, output.Add, ct); }
        catch (ToolException e) { return new LinterVersionCheck(LinterVersionState.Unreadable, null, range, e.Message); }
        if (run.End == ToolEnd.TimedOut) return new LinterVersionCheck(LinterVersionState.Unreadable, null, range, $"The program did not print its version in {timeout.TotalSeconds:0.##} s.");
        if (run.End == ToolEnd.Cancelled) return new LinterVersionCheck(LinterVersionState.Unreadable, null, range, "The version request was cancelled.");

        var check = Compare(string.Join('\n', output), range);
        if (check.State != LinterVersionState.Unreadable) Known[key] = check;
        else if (run.ExitCode != 0) check = check with { Reason = $"The program stopped with code {run.ExitCode} when asked for its version." };
        return check;
    }

    private static string Key(CatalogLinter linter, string executable)
    {
        long stamp = 0;
        try { stamp = File.GetLastWriteTimeUtc(executable).Ticks; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { /* the program is checked again next time */ }
        return linter.Id + "|" + executable + "|" + stamp;
    }
}
