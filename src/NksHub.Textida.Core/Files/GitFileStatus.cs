namespace NksHub.Textida.Core.Files;

/// <summary>What Git says about a file or a folder, from the lightest to the heaviest. A folder takes the heaviest state found below it, except <see cref="Ignored"/>, which stays with the ignored entry.</summary>
public enum GitFileState { None, Ignored, New, Deleted, Modified, Conflict }

/// <param name="State">What Git says about the entry.</param>
/// <param name="Below">The state is not the entry's own: a folder holds a file with it.</param>
public readonly record struct GitFileMark(GitFileState State, bool Below);

/// <summary>
/// The Git state of the files below one folder, for the rows of the file tree: the entries <c>git status</c> names, and the folders above them. A folder that Git names whole (an untracked
/// folder, an ignored one) gives its state to every path below it. The paths are those of the folder as the tree spells them. Built once, then read only.
/// </summary>
public sealed class GitFileStatus
{
    private static StringComparer PathComparer => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly Dictionary<string, GitFileState> _own = new(PathComparer);
    private readonly HashSet<string> _wholeFolders = new(PathComparer);
    private readonly Dictionary<string, GitFileState> _rolled = new(PathComparer);

    /// <summary>The folder the paths are relative to, without a closing separator.</summary>
    public string Root { get; }

    private GitFileStatus(string root) => Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

    /// <summary>Entries Git names, ignored ones included.</summary>
    public int Count => _own.Count;

    /// <summary>
    /// Builds the state of the folder <paramref name="root"/> from the entries of Git: a state and a path relative to the folder, written with slashes; a path that ends with a slash is a folder Git names whole.
    /// </summary>
    public static GitFileStatus Build(string root, IEnumerable<(GitFileState State, string Path)> entries)
    {
        var status = new GitFileStatus(root);
        char separator = Path.DirectorySeparatorChar;
        foreach (var (state, relative) in entries)
        {
            if (state == GitFileState.None || relative.Length == 0) continue;
            bool whole = relative[^1] == '/';
            string full = Path.TrimEndingDirectorySeparator(status.Root + separator + relative.Replace('/', separator));
            if (!status._own.TryGetValue(full, out var known) || known < state) status._own[full] = state;
            if (whole) status._wholeFolders.Add(full);
            if (state == GitFileState.Ignored) continue;
            // a folder already at least this heavy has its parents at least this heavy too
            for (var dir = Path.GetDirectoryName(full); dir is not null && dir.Length >= status.Root.Length; dir = Path.GetDirectoryName(dir))
            {
                if (status._rolled.TryGetValue(dir, out var seen) && seen >= state) break;
                status._rolled[dir] = state;
            }
        }
        return status;
    }

    /// <summary>The state of a path of the folder: its own, the one of the folder Git names whole above it, or, for a folder, the heaviest below it.</summary>
    public GitFileMark StateOf(string path, bool isDirectory)
    {
        if (_own.Count == 0) return default;
        if (_own.TryGetValue(path, out var own)) return new GitFileMark(own, false);
        if (_wholeFolders.Count > 0)
            for (var dir = Path.GetDirectoryName(path); dir is not null && dir.Length >= Root.Length; dir = Path.GetDirectoryName(dir))
                if (_wholeFolders.Contains(dir)) return new GitFileMark(_own[dir], false);
        return isDirectory && _rolled.TryGetValue(path, out var below) ? new GitFileMark(below, true) : default;
    }

    /// <summary>The first state any of <paramref name="statuses"/> has for the path (a tree of several folders keeps one status per folder).</summary>
    public static GitFileMark StateOf(IReadOnlyList<GitFileStatus> statuses, string path, bool isDirectory)
    {
        foreach (var status in statuses)
            if (status.StateOf(path, isDirectory) is { State: not GitFileState.None } mark) return mark;
        return default;
    }

    public bool SameAs(GitFileStatus other) =>
        PathComparer.Equals(Root, other.Root) && _own.Count == other._own.Count && _own.All(e => other._own.TryGetValue(e.Key, out var state) && state == e.Value);

    /// <summary>The letter the Git plugin writes for a state in its answer.</summary>
    public static char Code(GitFileState state) => state switch
    {
        GitFileState.Ignored => 'I', GitFileState.New => 'N', GitFileState.Deleted => 'D', GitFileState.Modified => 'M', GitFileState.Conflict => 'C', _ => '?',
    };

    public static GitFileState FromCode(char code) => code switch
    {
        'I' => GitFileState.Ignored, 'N' => GitFileState.New, 'D' => GitFileState.Deleted, 'M' => GitFileState.Modified, 'C' => GitFileState.Conflict, _ => GitFileState.None,
    };

    /// <summary>
    /// The folder with the index and <c>HEAD</c> of the working tree that holds <paramref name="folder"/>: the <c>.git</c> folder of it or of a folder above it, or the folder a <c>.git</c> file
    /// (a linked worktree, a submodule) points to. Null when there is none. Nothing is started.
    /// </summary>
    public static string? FindGitDirectory(string folder)
    {
        for (var dir = Path.GetFullPath(folder); dir is not null; dir = Path.GetDirectoryName(dir))
        {
            string marker = Path.Combine(dir, ".git");
            if (Directory.Exists(marker)) return marker;
            if (!File.Exists(marker)) continue;
            try
            {
                var line = File.ReadLines(marker).FirstOrDefault();
                if (line is not null && line.StartsWith("gitdir:", StringComparison.Ordinal)) return Path.GetFullPath(line["gitdir:".Length..].Trim(), dir);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
            return null;
        }
        return null;
    }
}
