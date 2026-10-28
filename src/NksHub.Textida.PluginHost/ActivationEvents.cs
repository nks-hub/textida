using System.Text.RegularExpressions;
using NksHub.Textida.PluginHost.Manifest;

namespace NksHub.Textida.PluginHost;

/// <summary>
/// The events that start a plugin: <c>onCommand:id</c>, <c>onLanguage:id</c>, <c>onFileType:.ext</c>, <c>onPanel:id</c>, <c>workspaceContains:glob</c> and <c>onStartup</c>.
/// The editor fires an event with <see cref="PluginHost.ActivateAsync"/> and every inactive plugin that lists it starts.
/// </summary>
public static class ActivationEvents
{
    public const string Startup = "onStartup";
    public const string CommandPrefix = "onCommand:";
    public const string LanguagePrefix = "onLanguage:";
    public const string FileTypePrefix = "onFileType:";
    public const string PanelPrefix = "onPanel:";
    public const string WorkspaceContainsPrefix = "workspaceContains:";

    private static readonly Regex Pattern = new(@"^(onStartup|(onCommand|onLanguage|onFileType|onPanel|workspaceContains):\S+)$", RegexOptions.CultureInvariant);

    public static string Command(string id) => CommandPrefix + id;

    public static string Language(string languageId) => LanguagePrefix + languageId;

    public static string Panel(string panelId) => PanelPrefix + panelId;

    /// <summary>The event for the extension of a file name (lower case, with the dot); null for a file without one.</summary>
    public static string? FileType(string? path)
    {
        var extension = path is null ? "" : Path.GetExtension(path);
        return extension.Length > 1 ? FileTypePrefix + extension.ToLowerInvariant() : null;
    }

    /// <summary>Whether the text is a well-formed event: a kind and a value (<c>onStartup</c> has none); the glob of <c>workspaceContains</c> has to be a usable glob.</summary>
    public static bool IsValid(string activationEvent) =>
        Pattern.IsMatch(activationEvent)
        && (!activationEvent.StartsWith(WorkspaceContainsPrefix, StringComparison.Ordinal) || LanguageServerMatcher.IsValidGlob(activationEvent[WorkspaceContainsPrefix.Length..]));

    /// <summary>The event names are compared as written, except that a file type does not depend on the case of the extension.</summary>
    public static bool Same(string listed, string fired) =>
        listed.StartsWith(FileTypePrefix, StringComparison.Ordinal) && fired.StartsWith(FileTypePrefix, StringComparison.Ordinal)
            ? string.Equals(listed, fired, StringComparison.OrdinalIgnoreCase)
            : string.Equals(listed, fired, StringComparison.Ordinal);

    public static bool Lists(IEnumerable<string> listed, string fired) => listed.Any(e => Same(e, fired));

    /// <summary>The globs of the <c>workspaceContains</c> events among <paramref name="events"/>.</summary>
    public static IEnumerable<string> WorkspaceGlobs(IEnumerable<string> events) =>
        events.Where(e => e.StartsWith(WorkspaceContainsPrefix, StringComparison.Ordinal)).Select(e => e[WorkspaceContainsPrefix.Length..]);
}

/// <summary>Looks for files in a workspace for the <c>workspaceContains</c> activation event, within a budget, so a huge folder tree cannot hold the editor.</summary>
public static class WorkspaceScan
{
    /// <summary>
    /// Returns the globs that match a file under one of the roots. A glob without <c>/</c> matches the name of a file at any depth, one with <c>/</c> the end of its path relative to the root
    /// (<see cref="LanguageServerMatcher.MatchesGlob"/>). At most <paramref name="maxEntries"/> files and folders are looked at and folders deeper than <paramref name="maxDepth"/> are not entered;
    /// a folder named in <paramref name="skipFolders"/> is passed over.
    /// </summary>
    public static IReadOnlySet<string> Find(IEnumerable<string> roots, IReadOnlyCollection<string> globs, int maxEntries, int maxDepth, IReadOnlyCollection<string> skipFolders)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        if (globs.Count == 0) return found;
        var seen = 0;
        foreach (var root in roots)
        {
            var queue = new Queue<(string Path, int Depth)>();
            queue.Enqueue((root, 0));
            while (queue.Count > 0 && seen < maxEntries && found.Count < globs.Count)
            {
                var (directory, depth) = queue.Dequeue();
                IEnumerable<string> entries;
                try { entries = Directory.EnumerateFileSystemEntries(directory).ToList(); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
                foreach (var entry in entries)
                {
                    if (++seen > maxEntries) break;
                    var name = Path.GetFileName(entry);
                    bool folder;
                    try { folder = Directory.Exists(entry); }
                    catch (IOException) { continue; }
                    if (folder)
                    {
                        if (depth < maxDepth && !skipFolders.Contains(name, StringComparer.OrdinalIgnoreCase)) queue.Enqueue((entry, depth + 1));
                        continue;
                    }
                    var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
                    foreach (var glob in globs)
                        if (!found.Contains(glob) && LanguageServerMatcher.MatchesGlob(glob, relative)) found.Add(glob);
                }
            }
        }
        return found;
    }
}
