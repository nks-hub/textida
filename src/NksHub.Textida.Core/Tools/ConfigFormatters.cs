using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Tools;

/// <summary>
/// A formatter that is configured by a file next to the code. <paramref name="Args"/> use <c>${File}</c>, which is a temporary copy of the document that the program rewrites in place.
/// The formatter is offered for a file only when one of <paramref name="ConfigFiles"/> exists in its folder or above it.
/// </summary>
public sealed record FormatterDef(string Id, string Title, string Executable, IReadOnlyList<string> Args, IReadOnlyList<string> ConfigFiles, IReadOnlyList<string> Extensions, string? Archive = null, bool ProjectLocal = false);

public enum FormatResult { Formatted, Unchanged, Skipped, Failed, TimedOut, Missing }

public sealed record FormatOutcome(FormatResult Result, string Reason = "");

/// <summary>
/// Formatters that run only where the project has their configuration (every formatter of <see cref="LanguageCatalog"/>: <c>gofmt</c>, <c>rustfmt</c>, <c>clang-format</c>, RuboCop, Prettier and the others).
/// They go through <see cref="ToolRunner"/>. None of them is on until the user turns it on.
/// </summary>
public static class ConfigFormatters
{
    /// <summary>The formatters Save Actions offers: all of <see cref="LanguageCatalog"/>.</summary>
    public static readonly IReadOnlyList<FormatterDef> All =
        [.. LanguageCatalog.Formatters.Select(f => new FormatterDef(f.Id, f.Title, f.Executable, f.Args, f.ConfigFiles, f.Extensions, f.Archive, f.ProjectLocal))];

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
    /// <param name="path">The file the next save writes when it is not the file of the document (Save As); its folder holds the copy and gives the configuration.</param>
    /// <param name="toolPath">The program, or the folder holding it, that the user chose for this formatter (<see cref="SaveActionSet.ToolPaths"/>); null looks it up in PATH.</param>
    public static async Task<FormatOutcome> FormatAsync(Document doc, FormatterDef def, TimeSpan timeout, CancellationToken ct = default, string? path = null, string? toolPath = null)
    {
        path ??= doc.FilePath;
        if (path is null) return new(FormatResult.Skipped, "The document has no file name yet.");
        if (doc.ByteLength > SaveTransforms.MaxBytes) return new(FormatResult.Skipped, "The document is too large to format on save.");
        long revision = doc.Revision;
        byte[] before = doc.ReadBytes(new ByteRange(0, doc.ByteLength));
        var (outcome, after) = await FormatBytesAsync(path, before, def, timeout, ct, toolPath);
        if (after is null) return outcome;
        if (doc.Revision != revision) return new(FormatResult.Skipped, "The document changed while it was formatted, so the result was dropped.");
        int bom = doc.BomLength;
        if (after.Length < bom || !after.AsSpan(0, bom).SequenceEqual(before.AsSpan(0, bom))) return new(FormatResult.Failed, $"{def.Title} changed the byte order mark.");
        var (range, insert) = Narrow(before, after, bom, lineSafe: doc.Codec.Encode("\n") is [0x0A]);
        var result = doc.Replace(range, insert, new EditOptions { Source = "format-on-save" });
        return result.Applied ? new(FormatResult.Formatted) : new(FormatResult.Skipped, "The document changed while it was formatted, so the result was dropped.");
    }

    /// <summary>What a formatter would change in a document: the smallest edit (<see cref="Range"/> of bytes replaced by <see cref="NewText"/>) based on <see cref="Revision"/>, or just the outcome when there is nothing to change.</summary>
    public sealed record FormatPlan(FormatOutcome Outcome, long Revision = 0, ByteRange Range = default, string NewText = "");

    /// <summary>
    /// The same run as <see cref="FormatAsync"/>, but the document is not changed: the edit is returned, so a caller can show it in the change review and let the user accept it. A document that changed while the program ran gives a skipped outcome.
    /// </summary>
    public static async Task<FormatPlan> PlanAsync(Document doc, FormatterDef def, TimeSpan timeout, CancellationToken ct = default, string? toolPath = null)
    {
        string? path = doc.FilePath;
        if (path is null) return new(new(FormatResult.Skipped, "The document has no file name yet."));
        if (doc.ByteLength > SaveTransforms.MaxBytes) return new(new(FormatResult.Skipped, "The document is too large to format."));
        long revision = doc.Revision;
        byte[] before = doc.ReadBytes(new ByteRange(0, doc.ByteLength));
        var (outcome, after) = await FormatBytesAsync(path, before, def, timeout, ct, toolPath);
        if (after is null) return new(outcome);
        if (doc.Revision != revision) return new(new(FormatResult.Skipped, "The document changed while it was formatted, so the result was dropped."));
        int bom = doc.BomLength;
        if (after.Length < bom || !after.AsSpan(0, bom).SequenceEqual(before.AsSpan(0, bom))) return new(new(FormatResult.Failed, $"{def.Title} changed the byte order mark."));
        var (range, insert) = Narrow(before, after, bom, lineSafe: doc.Codec.Encode("\n") is [0x0A]);
        return new(new(FormatResult.Formatted), revision, range, doc.Codec.GetString(insert));
    }

    /// <summary>
    /// The program half of <see cref="FormatAsync"/> for content that is not an open document (a recipe over many files): <paramref name="before"/> goes to a temporary copy next to
    /// <paramref name="path"/>, the program runs with <paramref name="timeout"/> and the copy is removed. <c>After</c> is the formatted content, or null when nothing changed or the program failed (the outcome says which).
    /// </summary>
    public static async Task<(FormatOutcome Outcome, byte[]? After)> FormatBytesAsync(string path, byte[] before, FormatterDef def, TimeSpan timeout, CancellationToken ct = default, string? toolPath = null)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string temp = Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + ".textida-fmt-" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(path));
        try
        {
            ToolInvocation tool;
            try
            {
                await File.WriteAllBytesAsync(temp, before, ct);
                var (program, args) = Launch(def, toolPath, dir);
                string? config = null;
                tool = ToolRunner.Resolve(new ToolSpec("format:" + def.Id, program, args, null, timeout), name => name switch { "File" => temp, "Config" => config ??= ConfigFor(def, path)?.Replace("$", "$$", StringComparison.Ordinal), _ => null }, dir);
            }
            catch (ToolException e) { return (new(FormatResult.Missing, e.Message), null); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return (new(FormatResult.Failed, e.Message), null); }

            var output = new List<string>();
            ToolResult run;
            try { run = await ToolRunner.RunAsync(tool, output.Add, ct); }
            catch (ToolException e) { return (new(FormatResult.Missing, e.Message), null); }
            if (run.End == ToolEnd.TimedOut) return (new(FormatResult.TimedOut, $"{def.Title} did not finish in {timeout.TotalSeconds:0.##} s."), null);
            if (run.End == ToolEnd.Cancelled) return (new(FormatResult.Skipped, "The format was cancelled."), null);
            if (run.ExitCode != 0) return (new(FormatResult.Failed, Reason(def, run.ExitCode, output)), null);

            byte[] after;
            try { after = await File.ReadAllBytesAsync(temp, ct); }
            catch (IOException e) { return (new(FormatResult.Failed, e.Message), null); }
            return after.AsSpan().SequenceEqual(before) ? (new(FormatResult.Unchanged), null) : (new(FormatResult.Formatted), after);
        }
        finally
        {
            try { File.Delete(temp); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the copy is a temporary file; a later format uses another name */ }
        }
    }

    /// <summary>
    /// What to start for <paramref name="def"/>. A project-local formatter is looked for in the folders of the project first and then on the PATH; when the program is nowhere, a PHAR of its <see cref="FormatterDef.Archive"/> in the project
    /// (<c>vendor/bin</c>, <c>tools</c>, <c>bin</c>, the folder itself) or on the PATH is run by <c>php</c>. The path the user set for the formatter wins over all of it (a PHAR there is run by <c>php</c> too).
    /// </summary>
    private static (string Program, IReadOnlyList<string> Args) Launch(FormatterDef def, string? toolPath, string dir)
    {
        if (!string.IsNullOrWhiteSpace(toolPath) && def.Archive is not null && toolPath.EndsWith(".phar", StringComparison.OrdinalIgnoreCase)) return Archive(def, toolPath, dir);
        if (!def.ProjectLocal || !string.IsNullOrWhiteSpace(toolPath)) return (Program(def, toolPath), def.Args);
        if (ToolLocator.Find(def.Executable, dir) is { } local) return (Dollars(local), def.Args);
        if (def.Archive is not null && FindArchive(def.Archive, dir) is { } phar) return Archive(def, phar, dir);
        throw new ToolException($"The program '{def.Executable}' was not found.") { Missing = true };
    }

    private static (string, IReadOnlyList<string>) Archive(FormatterDef def, string phar, string dir)
    {
        string php = ToolLocator.Find("php", dir) ?? throw new ToolException($"The program 'php' was not found, which runs {Path.GetFileName(phar)}.") { Missing = true };
        return (Dollars(php), [Dollars(phar), .. def.Args]);
    }

    private static string Dollars(string path) => path.Replace("$", "$$", StringComparison.Ordinal);

    private static readonly string[] ArchiveFolders = [".", "tools", "bin", "vendor/bin", "tools/php-cs-fixer/vendor/bin"];

    /// <summary>The PHAR named <paramref name="name"/> in the folders of the project (the folder of the file and every folder above it), else on the PATH.</summary>
    private static string? FindArchive(string name, string dir)
    {
        try
        {
            for (var d = new DirectoryInfo(Path.GetFullPath(dir)); d is not null; d = d.Parent)
                foreach (var folder in ArchiveFolders)
                    if (Path.Combine(d.FullName, folder, name) is var candidate && File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { /* an unreadable folder has no tool for us */ }
        return ToolLocator.Find(name);
    }

    /// <summary>The program to start: the one at the path the user set (a wrong path is an error, not a search in PATH), else the bare name for the PATH lookup of the runner.</summary>
    private static string Program(FormatterDef def, string? toolPath)
    {
        if (string.IsNullOrWhiteSpace(toolPath)) return def.Executable;
        if (!File.Exists(toolPath) && !Directory.Exists(toolPath)) throw new ToolException($"The path '{toolPath}' set for {def.Title} does not exist.") { Missing = true };
        string found = ToolLocator.Find(def.Executable, null, toolPath) ?? throw new ToolException($"The program '{def.Executable}' was not found at '{toolPath}'.") { Missing = true };
        return found.Replace("$", "$$", StringComparison.Ordinal);
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
