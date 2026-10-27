using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Tools;

/// <summary>
/// A formatter that is configured by a file next to the code. <paramref name="Args"/> use <c>${File}</c>, which is a temporary copy of the document that the program rewrites in place.
/// The formatter is offered for a file only when one of <paramref name="ConfigFiles"/> exists in its folder or above it.
/// </summary>
public sealed record FormatterDef(string Id, string Title, string Executable, IReadOnlyList<string> Args, IReadOnlyList<string> ConfigFiles, IReadOnlyList<string> Extensions);

public enum FormatResult { Formatted, Unchanged, Skipped, Failed, TimedOut, Missing }

public sealed record FormatOutcome(FormatResult Result, string Reason = "");

/// <summary>
/// Formatters that run only where the project has their configuration (<c>gofmt</c>, <c>rustfmt</c>, <c>clang-format</c>, RuboCop). They go through <see cref="ToolRunner"/>.
/// None of them is on until the user turns it on.
/// </summary>
public static class ConfigFormatters
{
    /// <summary>The formatters of <see cref="LanguageCatalog"/> that Save Actions offers.</summary>
    public static readonly IReadOnlyList<FormatterDef> All =
        [.. LanguageCatalog.Formatters.Where(f => f.OnSave).Select(f => new FormatterDef(f.Id, f.Title, f.Executable, f.Args, f.ConfigFiles, f.Extensions))];

    public static FormatterDef? Find(string id) => All.FirstOrDefault(f => f.Id == id);

    /// <summary>The configuration file of <paramref name="def"/> that applies to <paramref name="path"/>, or null.</summary>
    public static string? ConfigFor(FormatterDef def, string path)
    {
        try
        {
            if (!def.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) return null;
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!); dir is not null; dir = dir.Parent)
                foreach (var name in def.ConfigFiles)
                    if (File.Exists(Path.Combine(dir.FullName, name))) return Path.Combine(dir.FullName, name);
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { /* not offered */ }
        return null;
    }

    /// <summary>The formatters that may be turned on for this file: their configuration exists.</summary>
    public static IReadOnlyList<FormatterDef> Offered(string path) => [.. All.Where(f => ConfigFor(f, path) is not null)];

    /// <summary>
    /// One bounded transaction on the document: the bytes go to a temporary copy next to the file (so the program finds the same configuration), the program runs with
    /// <paramref name="timeout"/>, and only a clean exit with a changed result edits the document, as one undo step. A document that changed while the program ran is left as it is. The continuations stay on the thread that called, because the document belongs to it.
    /// A timeout, a failing exit, a program that is not found or a changed byte order mark leave the document untouched.
    /// </summary>
    public static async Task<FormatOutcome> FormatAsync(Document doc, FormatterDef def, TimeSpan timeout, CancellationToken ct = default)
    {
        if (doc.FilePath is not { } path) return new(FormatResult.Skipped, "The document has no file name yet.");
        if (doc.ByteLength > SaveTransforms.MaxBytes) return new(FormatResult.Skipped, "The document is too large to format on save.");
        long revision = doc.Revision;
        byte[] before = doc.ReadBytes(new ByteRange(0, doc.ByteLength));
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string temp = Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + ".textida-fmt-" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(path));
        try
        {
            ToolInvocation tool;
            try
            {
                await File.WriteAllBytesAsync(temp, before, ct);
                tool = ToolRunner.Resolve(new ToolSpec("format:" + def.Id, def.Executable, def.Args, null, timeout), name => name == "File" ? temp : null, dir);
            }
            catch (ToolException e) { return new(FormatResult.Missing, e.Message); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(FormatResult.Failed, e.Message); }

            var output = new List<string>();
            ToolResult run;
            try { run = await ToolRunner.RunAsync(tool, output.Add, ct); }
            catch (ToolException e) { return new(FormatResult.Missing, e.Message); }
            if (run.End == ToolEnd.TimedOut) return new(FormatResult.TimedOut, $"{def.Title} did not finish in {timeout.TotalSeconds:0.##} s.");
            if (run.End == ToolEnd.Cancelled) return new(FormatResult.Skipped, "The format was cancelled.");
            if (run.ExitCode != 0) return new(FormatResult.Failed, Reason(def, run.ExitCode, output));

            byte[] after;
            try { after = await File.ReadAllBytesAsync(temp, ct); }
            catch (IOException e) { return new(FormatResult.Failed, e.Message); }
            if (after.AsSpan().SequenceEqual(before)) return new(FormatResult.Unchanged);
            if (doc.Revision != revision) return new(FormatResult.Skipped, "The document changed while it was formatted, so the result was dropped.");
            int bom = doc.BomLength;
            if (after.Length < bom || !after.AsSpan(0, bom).SequenceEqual(before.AsSpan(0, bom))) return new(FormatResult.Failed, $"{def.Title} changed the byte order mark.");
            var (range, insert) = Narrow(before, after, bom, lineSafe: doc.Codec.Encode("\n") is [0x0A]);
            var result = doc.Replace(range, insert, new EditOptions { Source = "format-on-save" });
            return result.Applied ? new(FormatResult.Formatted) : new(FormatResult.Skipped, "The document changed while it was formatted, so the result was dropped.");
        }
        finally
        {
            try { File.Delete(temp); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the copy is a temporary file; a later format uses another name */ }
        }
    }

    private static string Reason(FormatterDef def, int exitCode, List<string> output)
    {
        var lines = output.Where(l => l.Trim().Length > 0).TakeLast(3).Select(l => l.Trim());
        string text = string.Join(" / ", lines);
        if (text.Length > 200) text = text[..200] + "...";
        return text.Length > 0 ? $"{def.Title} stopped with code {exitCode}: {text}" : $"{def.Title} stopped with code {exitCode}.";
    }

    /// <summary>The smallest edit that turns <paramref name="before"/> into <paramref name="after"/>; with a line break that is one byte, the edit starts and ends on line boundaries, so it never cuts a character.</summary>
    internal static (ByteRange Range, byte[] Insert) Narrow(byte[] before, byte[] after, int start, bool lineSafe)
    {
        if (!lineSafe) return (new ByteRange(start, before.Length), after[start..]);
        int common = 0;
        int limit = Math.Min(before.Length, after.Length);
        while (common < limit && before[common] == after[common]) common++;
        int head = common;
        while (head > start && before[head - 1] != 0x0A) head--;
        if (head < start) head = start;
        int tail = 0;
        while (tail < limit - head && before[^(tail + 1)] == after[^(tail + 1)]) tail++;
        // the unchanged tail has to begin where a line begins (or right where the edit begins)
        while (tail > 0 && before.Length - tail != head && (before.Length - tail < head || after.Length - tail < head || before[before.Length - tail - 1] != 0x0A)) tail--;
        return (new ByteRange(head, before.Length - tail), after[head..(after.Length - tail)]);
    }
}
