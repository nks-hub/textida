using NksHub.Textida.Core.Search;

namespace NksHub.Textida.Core.Changes;

/// <summary>
/// Which files of a watched folder the review of changes from outside leaves out (P6.1.1): everything below a <c>.git</c> or <c>.codegraph</c> folder, the files that the ignore files of the folder
/// (<c>.gitignore</c> and <c>.ignore</c>, read the way <see cref="FileWalker"/> reads them: from the folder they sit in downwards, a deeper file overrides a shallower one, an ignored folder takes
/// its content with it) ignore, and the transcripts of agents (a <c>projects</c> folder below <c>.claude*</c> and a <c>sessions</c> folder below <c>.codex*</c>, wherever they lie), which the agent
/// itself keeps writing. Safe to call from several threads.
/// </summary>
public sealed class ReviewIgnore
{
    private static readonly string[] SkippedDirectories = [".git", ".codegraph"];
    private static readonly string[] IgnoreFileNames = [".gitignore", ".ignore"];

    private readonly string _root;
    private readonly bool _ignoreCase;
    private readonly StringComparison _names;
    private readonly Dictionary<string, IgnoreRules[]> _sets;
    private readonly Dictionary<string, bool> _folders;

    /// <param name="ignoreCase">Match names and patterns without regard to case; by default on Windows and macOS.</param>
    public ReviewIgnore(string root, bool? ignoreCase = null)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _ignoreCase = ignoreCase ?? (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());
        _names = _ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        _sets = new(_ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        _folders = new(_ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    public string Root => _root;

    /// <summary>Forgets the ignore files read so far, so a changed one is read again. Call it before a batch of paths.</summary>
    public void Refresh()
    {
        lock (_sets)
        {
            _sets.Clear();
            _folders.Clear();
        }
    }

    /// <summary>True when the file at <paramref name="path"/> is left out. A path that is not below the root is left out too.</summary>
    public bool IsIgnored(string path)
    {
        string full = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(_root, full);
        if (relative == "." || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative)) return true;

        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (int i = 0; i < segments.Length - 1; i++)
            if (SkippedDirectories.Any(d => string.Equals(d, segments[i], _names))) return true;
        if (string.Equals(segments[^1], ".git", _names)) return true; // the file that points a linked work tree to its repository
        if (IsAgentTranscript(full)) return true;

        string prefix = _root;
        for (int k = 1; k <= segments.Length; k++)
        {
            // the first k segments are a folder, or the file when k is all of them; the verdict on a folder is remembered, as a build writes thousands of files below one
            bool isDirectory = k < segments.Length;
            prefix = Path.Combine(prefix, segments[k - 1]);
            if (isDirectory)
            {
                bool known;
                bool ignoredFolder;
                lock (_sets) known = _folders.TryGetValue(prefix, out ignoredFolder);
                if (!known)
                {
                    ignoredFolder = Judge(segments, k, isDirectory: true);
                    lock (_sets) _folders[prefix] = ignoredFolder;
                }
                if (ignoredFolder) return true;
            }
            else if (Judge(segments, k, isDirectory: false)) return true;
        }
        return false;
    }

    /// <summary>Each ignore file, from the root down, judges the first <paramref name="count"/> segments from its own folder; a deeper file overrides a shallower one.</summary>
    private bool Judge(string[] segments, int count, bool isDirectory)
    {
        bool? verdict = null;
        string directory = _root;
        for (int j = 0; j < count; j++)
        {
            string local = string.Join('/', segments, j, count - j);
            foreach (var set in SetsOf(directory)) verdict = set.Evaluate(local, isDirectory, verdict);
            directory = Path.Combine(directory, segments[j]);
        }
        return verdict == true;
    }

    /// <summary>True for a path below a <c>projects</c> folder that is below a folder named <c>.claude…</c>, or below a <c>sessions</c> folder below one named <c>.codex…</c>.</summary>
    public static bool IsAgentTranscript(string fullPath)
    {
        var parts = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (int i = 0; i < parts.Length; i++)
        {
            string wanted = parts[i].StartsWith(".claude", StringComparison.OrdinalIgnoreCase) ? "projects" : parts[i].StartsWith(".codex", StringComparison.OrdinalIgnoreCase) ? "sessions" : "";
            if (wanted.Length > 0 && parts.Skip(i + 1).Take(parts.Length - i - 2).Any(p => string.Equals(p, wanted, StringComparison.OrdinalIgnoreCase))) return true;
        }
        return false;
    }

    private IgnoreRules[] SetsOf(string directory)
    {
        lock (_sets)
        {
            if (_sets.TryGetValue(directory, out var known)) return known;
            var sets = new List<IgnoreRules>();
            foreach (var name in IgnoreFileNames)
            {
                string file = Path.Combine(directory, name);
                if (File.Exists(file) && IgnoreRules.TryLoad(file, _ignoreCase) is { Count: > 0 } rules) sets.Add(rules);
            }
            return _sets[directory] = [.. sets];
        }
    }
}
