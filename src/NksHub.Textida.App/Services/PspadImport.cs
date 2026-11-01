namespace NksHub.Textida.App.Services;

/// <summary>The clips of one <c>Context\*.DEF</c> file with the marks it uses and how many it cannot carry over.</summary>
internal sealed record PspadClipFile(string File, string Stem, IReadOnlyList<PspadClip> Clips, char Cursor, char Selection)
{
    public int Skipped => Clips.Count(c => c.UsesMacro || c.Name.Length == 0);
}

/// <summary>What would be imported from a PSPad folder, found by reading only. Nothing is changed before the user has seen <see cref="Preview"/> and agreed.</summary>
internal sealed record PspadPlan(
    string Root,
    IReadOnlyList<PspadConnection> Connections,
    int ConnectionsWithPassword,
    IReadOnlyList<PspadProject> Projects,
    IReadOnlyList<PspadClipFile> ClipFiles,
    IReadOnlyList<PspadMacro> Macros,
    IReadOnlyList<PspadHighlighter> Highlighters,
    IReadOnlyList<PspadKey> Keys,
    IReadOnlyList<string> SkippedKeys,
    IReadOnlyList<string> Problems)
{
    public bool IsEmpty => Connections.Count + Projects.Count + ClipFiles.Count + Macros.Count + Highlighters.Count + Keys.Count == 0;
}

/// <summary>
/// Reads the data of the user from a PSPad installation they point at (PZ.1.1): FTP connections (without passwords), projects, clips, macros, highlighters and shortcuts.
/// It never writes to that folder, never starts PSPad and does not copy PSPad's own data: a highlighter or a clip file that PSPad delivered unchanged is left out, because only
/// files that are newer than the program were made or changed by the user.
/// </summary>
internal static class PspadImport
{
    private const long MaxFileBytes = 8 << 20;

    private static string? Find(string folder, string name) =>
        Directory.Exists(folder) ? Directory.EnumerateFiles(folder).FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase)) : null;

    private static string? FindFolder(string folder, string name) =>
        Directory.Exists(folder) ? Directory.EnumerateDirectories(folder).FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase)) : null;

    private static string? ReadText(string path, List<string> problems)
    {
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes) { problems.Add($"{Path.GetFileName(path)}: larger than {MaxFileBytes >> 20} MiB, skipped."); return null; }
            return PspadText.Decode(File.ReadAllBytes(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problems.Add($"{Path.GetFileName(path)}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// The files of the user in <paramref name="root"/>: the folder of PSPad, or the folder with its settings. A file counts as the user's when it is newer than <c>PSPad.exe</c> by more than
    /// <paramref name="grace"/> (the program and the files it was delivered with share the day they were built); without the program every file counts.
    /// </summary>
    public static PspadPlan Build(string root, TimeSpan? grace = null)
    {
        var problems = new List<string>();
        var connections = new List<PspadConnection>();
        int withPassword = 0;
        if (Find(root, "PSPadFTP.INI") is { } ftp && ReadText(ftp, problems) is { } ftpText)
        {
            connections.AddRange(PspadFtp.Read(ftpText, withPasswords: false));
            withPassword = PspadFtp.WithSavedPassword(ftpText);
        }

        var projectFiles = new List<string>();
        if (Directory.Exists(root)) projectFiles.AddRange(Directory.EnumerateFiles(root, "*.ppr"));
        if (Find(root, "Recent.ini") is { } recent && ReadText(recent, problems) is { } recentText)
            foreach (var entry in PspadIni.Parse(recentText).Named("Recent Projects").SelectMany(e => e))
                if (entry.Value is { Length: > 0 } path && path.EndsWith(".ppr", StringComparison.OrdinalIgnoreCase) && File.Exists(path)) projectFiles.Add(path);
        var projects = new List<PspadProject>();
        foreach (string file in projectFiles.Distinct(StringComparer.OrdinalIgnoreCase))
            if (ReadText(file, problems) is { } text)
            {
                if (PspadProjects.Read(text, file) is { } project) projects.Add(project);
                else problems.Add($"{Path.GetFileName(file)}: no project tree.");
            }

        DateTime? installed = Find(root, "PSPad.exe") is { } exe ? File.GetLastWriteTimeUtc(exe) : null;
        bool Users(string file) => installed is not { } built || File.GetLastWriteTimeUtc(file) > built + (grace ?? TimeSpan.FromDays(2));

        var highlighters = new List<PspadHighlighter>();
        if (FindFolder(root, "Syntax") is { } syntax)
            foreach (string file in Directory.EnumerateFiles(syntax, "*.ini").Order(StringComparer.OrdinalIgnoreCase).Where(Users))
                if (ReadText(file, problems) is { } text)
                {
                    if (PspadHighlighters.Read(text, Path.GetFileName(file)) is { } h) highlighters.Add(h);
                    else problems.Add($"{Path.GetFileName(file)}: not a highlighter (no [Settings] Name).");
                }

        var clipFiles = new List<PspadClipFile>();
        if (FindFolder(root, "Context") is { } context)
            foreach (string file in Directory.EnumerateFiles(context, "*.def").Order(StringComparer.OrdinalIgnoreCase).Where(Users))
                if (ReadText(file, problems) is { } text)
                {
                    var (cursor, selection) = PspadClips.Marks(text);
                    var clips = PspadClips.Read(text);
                    if (clips.Count > 0) clipFiles.Add(new PspadClipFile(file, Path.GetFileNameWithoutExtension(file), clips, cursor, selection));
                }

        var macros = new List<PspadMacro>();
        if ((FindFolder(root, "Macro") ?? FindFolder(Path.Combine(root, "PSPad"), "Macro")) is { } macroFolder)
            foreach (string file in Directory.EnumerateFiles(macroFolder, "*.pme").Order(StringComparer.OrdinalIgnoreCase))
                if (ReadText(file, problems) is { } text) macros.Add(PspadMacros.Read(Path.GetFileNameWithoutExtension(file), text));

        var keys = new List<PspadKey>();
        var skippedKeys = new List<string>();
        if (Find(root, "KeyMap.ini") is { } keyMap && ReadText(keyMap, problems) is { } keyText) keys.AddRange(PspadKeys.Read(keyText, skippedKeys));

        return new PspadPlan(root, connections, withPassword, projects, clipFiles, macros, highlighters, keys, skippedKeys, problems);
    }
}
