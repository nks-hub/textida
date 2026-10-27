namespace NksHub.Textida.Core.Tools;

// No dependency on the rest of the assembly: the language server plugin compiles this file too (see LanguageCatalog).

/// <summary>
/// Finds a program on disk and nothing else: it never starts one, never downloads one and never touches the network.
/// The order is a path the user gave, then the project folders (<c>node_modules/.bin</c>, <c>vendor/bin</c>, <c>.venv</c>) of the starting folder and its parents, then <c>PATH</c>.
/// </summary>
public static class ToolLocator
{
    /// <summary>Folders of a project that hold its own tools, relative to a project folder.</summary>
    public static readonly IReadOnlyList<string> ProjectFolders = ["node_modules/.bin", "vendor/bin", ".venv/Scripts", ".venv/bin"];

    /// <param name="program">A bare name, or a path (then only that file counts).</param>
    /// <param name="startDirectory">The folder to start the walk up from, usually the folder of the open file; null skips the project folders.</param>
    /// <param name="userPath">A file or folder the user chose for the tool; a folder is searched for <paramref name="program"/>.</param>
    public static string? Find(string program, string? startDirectory = null, string? userPath = null)
    {
        if (program.Contains(Path.DirectorySeparatorChar) || program.Contains(Path.AltDirectorySeparatorChar)) return File.Exists(program) ? Path.GetFullPath(program) : null;
        if (!string.IsNullOrWhiteSpace(userPath))
        {
            if (File.Exists(userPath)) return Path.GetFullPath(userPath);
            if (Directory.Exists(userPath) && InFolder(userPath, program) is { } inUser) return inUser;
        }
        if (startDirectory is not null)
            try
            {
                for (var dir = new DirectoryInfo(Path.GetFullPath(startDirectory)); dir is not null; dir = dir.Parent)
                    foreach (var folder in ProjectFolders)
                        if (InFolder(Path.Combine(dir.FullName, folder), program) is { } local) return local;
            }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { /* an unreadable folder has no tool for us */ }
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            if (InFolder(folder.Trim('"'), program) is { } found) return found;
        return null;
    }

    /// <summary>The file named <paramref name="program"/> in <paramref name="folder"/>; on Windows with each extension of <c>PATHEXT</c> too.</summary>
    private static string? InFolder(string folder, string program)
    {
        string[] extensions = OperatingSystem.IsWindows()
            ? [.. (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE").Split(';', StringSplitOptions.RemoveEmptyEntries).Prepend("")]
            : [""];
        foreach (var extension in extensions)
            try
            {
                var candidate = Path.Combine(folder, program + extension);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { /* a PATH entry with characters a path cannot have */ }
        return null;
    }
}
