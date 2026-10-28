using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Tools;

public enum LintState { Done, Missing, TimedOut, Failed, BadOutput, Skipped }

/// <summary>A finding placed in the text: <see cref="Range"/> is bytes of revision <see cref="LintOutcome.Revision"/>, <see cref="Line"/> and <see cref="Column"/> are 1-based (the column counts characters).</summary>
public sealed record LintDiagnostic(ByteRange Range, ToolSeverity Severity, string Message, string? Code, int Line, int Column);

/// <param name="Reason">What happened, in English, when <paramref name="State"/> is not <see cref="LintState.Done"/>.</param>
/// <param name="Dropped">Findings that could not be placed: another file, a line the text does not have.</param>
public sealed record LintOutcome(CatalogLinter Linter, LintState State, IReadOnlyList<LintDiagnostic> Findings, long Revision, string Reason = "", int Dropped = 0);

/// <summary>
/// Runs a linter of <see cref="LanguageCatalog"/> over a saved document and turns its output into placed findings. The program is looked up by <see cref="ToolLocator"/>, started by <see cref="ToolRunner"/>
/// (an argument list, no shell, a time limit) and never reads the unsaved text: it checks the file on disk, so a document with unsaved changes is not linted. The findings belong to the revision the text had when the run started;
/// the caller maps them to a newer one. A linter that writes findings exits with a code other than 0, so the code alone is no failure: the output decides.
/// </summary>
public static class Linters
{
    /// <summary>The linters of the catalog whose output a parser here can read and that handle the extension of <paramref name="path"/>.</summary>
    public static IReadOnlyList<CatalogLinter> For(string path) =>
        [.. LanguageCatalog.Linters.Where(l => LinterOutput.Formats.Contains(l.OutputFormat) && l.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))];

    public static CatalogLinter? Find(string id) => LanguageCatalog.Linters.FirstOrDefault(l => l.Id == id && LinterOutput.Formats.Contains(l.OutputFormat));

    /// <summary>
    /// The one linter a manual run uses when several handle the file: the first one whose configuration file exists in the folder of the file or above it, else the first one whose program is found, else the first one.
    /// A save action names its linter itself and never asks this.
    /// </summary>
    /// <param name="toolPaths">The program paths the user set, by linter id (see <see cref="SaveActionSet.ToolPaths"/>).</param>
    public static CatalogLinter? Choose(string path, IReadOnlyDictionary<string, string>? toolPaths = null)
    {
        var all = For(path);
        return all.FirstOrDefault(l => Root(l, path, markers: false) is not null)
            ?? all.FirstOrDefault(l => ToolLocator.Find(l.Executable, Path.GetDirectoryName(Path.GetFullPath(path)), toolPaths?.GetValueOrDefault(l.Id)) is not null)
            ?? all.FirstOrDefault();
    }

    /// <summary>The folder the linter runs in: the nearest one above the file with a configuration file of the linter or the manifest of its language, else the folder of the file.</summary>
    private static string? Root(CatalogLinter linter, string path, bool markers = true)
    {
        var group = LanguageCatalog.GroupOfPath(path);
        var names = linter.ConfigFiles.Concat(markers ? group?.Manifests.Select(m => m.File).Where(f => !f.Contains('*')) ?? [] : []).ToList();
        try
        {
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!); dir is not null; dir = dir.Parent)
                if (names.Any(n => File.Exists(Path.Combine(dir.FullName, n)))) return dir.FullName;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { /* no root */ }
        return null;
    }

    /// <summary>
    /// The command that would run: program found, arguments with the file filled in, folder to run in. Throws <see cref="ToolException"/> when the program is not found or, for Clippy, no <c>Cargo.toml</c> is above the file.
    /// The caller shows it for confirmation where the program comes from the project and starts it with <see cref="RunAsync"/>.
    /// </summary>
    /// <param name="userPath">A program or a folder the user chose for this linter; a path that does not exist is an error, never replaced by a guess.</param>
    public static ToolInvocation Prepare(CatalogLinter linter, string path, TimeSpan timeout, string? userPath = null)
    {
        string full = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(full)!;
        string root = Root(linter, full) ?? directory;
        if (linter.Executable == "cargo" && !File.Exists(Path.Combine(root, "Cargo.toml"))) throw new ToolException("Clippy needs a Cargo.toml in the folder of the file or above it.");
        if (!string.IsNullOrWhiteSpace(userPath) && !File.Exists(userPath) && !Directory.Exists(userPath))
            throw new ToolException($"The path '{userPath}' set for {linter.Title} does not exist.") { Missing = true };
        string program = ToolLocator.Find(linter.Executable, directory, userPath)
            ?? throw new ToolException($"The program '{linter.Executable}' was not found in the project folders (vendor/bin, node_modules/.bin, .venv) or in PATH.") { Missing = true };
        return ToolRunner.Resolve(new ToolSpec("lint:" + linter.Id, program.Replace("$", "$$", StringComparison.Ordinal), linter.Args, null, timeout, Environment: linter.Environment), name => name == "File" ? full : null, root);
    }

    public static async Task<LintOutcome> RunAsync(Document doc, CatalogLinter linter, ToolInvocation tool, CancellationToken ct = default)
    {
        long revision = doc.Revision;
        LintOutcome Not(LintState state, string reason) => new(linter, state, [], revision, reason);
        if (doc.FilePath is not { } path) return Not(LintState.Skipped, "The document has no file name yet.");
        if (doc.IsModified) return Not(LintState.Skipped, $"{linter.Title} checks the file on disk, which is not what the editor holds: save first.");
        if (doc.ByteLength > SaveTransforms.MaxBytes) return Not(LintState.Skipped, "The document is too large to check.");
        if (doc.Codec.Encode("\n") is not [0x0A]) return Not(LintState.Skipped, "Only ASCII-compatible encodings such as UTF-8 can be placed.");
        byte[] bytes = doc.ReadBytes(new ByteRange(0, doc.ByteLength));
        int bom = doc.BomLength;

        var output = new List<string>();
        ToolResult run;
        try { run = await ToolRunner.RunAsync(tool, output.Add, ct); }
        catch (ToolException e) { return Not(LintState.Missing, e.Message); }
        if (run.End == ToolEnd.TimedOut) return Not(LintState.TimedOut, $"{linter.Title} did not finish in {tool.Timeout?.TotalSeconds:0.##} s.");
        if (run.End == ToolEnd.Cancelled) return Not(LintState.Skipped, "The check was cancelled.");

        var parsed = await Task.Run(() => LinterOutput.Parse(linter.OutputFormat, output), CancellationToken.None);
        if (!parsed.Readable)
            return Not(run.ExitCode == 0 ? LintState.BadOutput : LintState.Failed, Reason(linter, run.ExitCode, parsed.Problem, output));
        if (parsed.Findings.Count == 0 && run.ExitCode != 0 && linter.OutputFormat == "gcc-text" && output.Any(l => l.Trim().Length > 0))
            return Not(LintState.Failed, Reason(linter, run.ExitCode, "", output));

        var placed = await Task.Run(() => Place(linter, parsed.Findings, bytes, bom, path, tool.WorkingDirectory), CancellationToken.None);
        return new LintOutcome(linter, LintState.Done, placed.Findings, revision, "", placed.Dropped);
    }

    private static string Reason(CatalogLinter linter, int exitCode, string problem, List<string> output)
    {
        string text = string.Join(" / ", output.Where(l => l.Trim().Length > 0).TakeLast(3).Select(l => l.Trim()));
        if (text.Length > 200) text = text[..200] + "...";
        string head = exitCode == 0 ? $"The output of {linter.Title} is not {linter.OutputFormat}" : $"{linter.Title} stopped with code {exitCode}";
        return $"{head}{(problem.Length > 0 ? " (" + problem.TrimEnd('.') + ")" : "")}{(text.Length > 0 ? ": " + text : ".")}";
    }

    // ---- placing ----

    /// <summary>The severity of a word of the linter's output by its table in the catalog: the word itself, else <c>*</c>, else a warning.</summary>
    public static ToolSeverity SeverityOf(CatalogLinter linter, string word) =>
        (linter.Severities.FirstOrDefault(s => string.Equals(s.Tool, word, StringComparison.OrdinalIgnoreCase)) ?? linter.Severities.FirstOrDefault(s => s.Tool == "*"))?.Editor ?? ToolSeverity.Warning;

    /// <summary>
    /// Turns findings into byte ranges of <paramref name="bytes"/> (the whole file as the editor holds it, byte order mark included). A finding with a byte range uses it; with a column it starts there and covers the
    /// word or the end the tool gave; without a column it covers the text of the line. Findings of other files (a crate has many) are dropped, as are lines the text does not have.
    /// </summary>
    internal static (IReadOnlyList<LintDiagnostic> Findings, int Dropped) Place(CatalogLinter linter, IReadOnlyList<LintFinding> findings, byte[] bytes, int bom, string documentPath, string workingDirectory)
    {
        var starts = new List<int> { bom };
        for (int i = bom; i < bytes.Length; i++)
            if (bytes[i] == 0x0A) starts.Add(i + 1);
        int LineEnd(int line)
        {
            int end = line < starts.Count ? starts[line] - 1 : bytes.Length;
            return end > starts[line - 1] && bytes[end - 1] == 0x0D ? end - 1 : end;
        }

        var result = new List<LintDiagnostic>();
        int dropped = 0;
        foreach (var f in findings)
        {
            if (f.File is { Length: > 0 } file && linter.OutputFormat is "cargo-json" or "gcc-text" or "dart-machine" && !SameFile(file, workingDirectory, documentPath)) { dropped++; continue; }
            if (f.Line < 1 || f.Line > starts.Count) { dropped++; continue; }
            int lineStart = starts[f.Line - 1], lineEnd = LineEnd(f.Line);
            int start, end;
            if (f.ByteStart is { } bs && f.ByteEnd is { } be && bs >= bom && be >= bs && be <= bytes.Length) (start, end) = ((int)bs, (int)be);
            else if (f.Column is not { } column)
            {
                start = lineStart;
                while (start < lineEnd && bytes[start] is (byte)' ' or (byte)'\t') start++;
                end = lineEnd;
            }
            else
            {
                start = Advance(bytes, lineStart, lineEnd, Math.Max(column, 1) - 1, f.ByteColumns);
                if (f.EndLine is { } endLine && endLine >= f.Line && endLine <= starts.Count)
                    end = f.EndColumn is { } endColumn ? Advance(bytes, starts[endLine - 1], LineEnd(endLine), Math.Max(endColumn, 1) - 1, f.ByteColumns) : LineEnd(endLine);
                else if (f.EndColumn is { } sameLine) end = Advance(bytes, lineStart, lineEnd, Math.Max(sameLine, 1) - 1, f.ByteColumns);
                else
                {
                    end = start;
                    while (end < lineEnd && (bytes[end] is (>= (byte)'0' and <= (byte)'9') or (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z') or (byte)'_' or >= 0x80)) end++;
                    if (end == start) end = Advance(bytes, start, lineEnd, 1, false);
                }
            }
            if (end < start) end = start;
            result.Add(new LintDiagnostic(new ByteRange(start, end), SeverityOf(linter, f.SeverityWord), f.Message, f.Code, f.Line, CharsBetween(bytes, lineStart, Math.Min(start, lineEnd)) + 1));
        }
        return (result, dropped);
    }

    /// <summary>The position <paramref name="count"/> columns after <paramref name="from"/> on a line, never past <paramref name="limit"/>; characters are UTF-8 sequences.</summary>
    private static int Advance(byte[] b, int from, int limit, int count, bool bytes)
    {
        if (bytes) return Math.Min(from + count, limit);
        int i = from;
        for (; count > 0 && i < limit; count--)
        {
            i++;
            while (i < limit && (b[i] & 0xC0) == 0x80) i++;
        }
        return i;
    }

    private static int CharsBetween(byte[] b, int from, int to)
    {
        int n = 0;
        for (int i = from; i < to; i++)
            if ((b[i] & 0xC0) != 0x80) n++;
        return n;
    }

    /// <summary>The file a tool names is the document: the same path, or a relative name that ends the path of the document (a workspace member is reported from the workspace root).</summary>
    private static bool SameFile(string reported, string workingDirectory, string documentPath)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        try
        {
            string document = Path.GetFullPath(documentPath);
            if (string.Equals(Path.GetFullPath(reported, workingDirectory), document, comparison)) return true;
            if (Path.IsPathRooted(reported)) return false;
            string tail = Path.DirectorySeparatorChar + reported.Replace('/', Path.DirectorySeparatorChar).TrimStart('.', Path.DirectorySeparatorChar);
            return document.EndsWith(tail, comparison);
        }
        catch (ArgumentException) { return false; }
    }
}
