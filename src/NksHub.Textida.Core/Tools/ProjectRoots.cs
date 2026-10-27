namespace NksHub.Textida.Core.Tools;

/// <summary>
/// The root of the project a file belongs to. Each language lists marker files in tiers (a name, or a pattern with <c>*</c>): the nearest folder with a marker of the first tier wins, then
/// the nearest with one of the second, and so on. Without a marker the folder <see cref="ToolVariables.IsProjectRoot"/> accepts is the root. That folder also ends the search, because a marker above a
/// repository belongs to something else. The search climbs at most a given number of folders, the first being the folder of the file.
/// </summary>
public static class ProjectRoots
{
    // shortcut: the nearest marker wins, so a nested CMakeLists.txt or Cargo.toml makes its own root; upgrade when a real project needs the outermost one.
    private static readonly Dictionary<string, string[][]> Markers = new(StringComparer.Ordinal)
    {
        ["csharp"] = [["*.sln", "*.slnx"], ["*.csproj"]],
        ["rust"] = [["Cargo.toml"]],
        ["php"] = [["composer.json"]],
        ["dart"] = [["pubspec.yaml"]],
        ["javascript"] = [["tsconfig.json", "package.json"]],
        ["typescript"] = [["tsconfig.json", "package.json"]],
        ["c"] = [["compile_commands.json"], ["CMakeLists.txt"]],
        ["cpp"] = [["compile_commands.json"], ["CMakeLists.txt"]],
    };

    private static readonly EnumerationOptions Simple = new() { MatchType = MatchType.Simple, IgnoreInaccessible = true };

    /// <summary>The project root for a file in <paramref name="fromDirectory"/> written in <paramref name="language"/> (a language mode id), or null when no marker is found within <paramref name="maxLevels"/> folders.</summary>
    public static string? Find(string? fromDirectory, string language, int maxLevels)
    {
        var tiers = Markers.GetValueOrDefault(language) ?? [];
        var found = new string?[tiers.Length + 1]; // the last one is the folder of a repository
        try
        {
            var dir = fromDirectory is null ? null : new DirectoryInfo(Path.GetFullPath(fromDirectory));
            for (int level = 0; dir is not null && level < maxLevels; level++, dir = dir.Parent)
            {
                for (int tier = 0; tier < tiers.Length; tier++)
                    if (found[tier] is null && tiers[tier].Any(marker => Has(dir.FullName, marker))) found[tier] = dir.FullName;
                if (ToolVariables.IsProjectRoot(dir.FullName))
                {
                    found[^1] = dir.FullName;
                    break;
                }
                if (found.Length > 1 && found[0] is not null) break; // nothing can beat the first tier
            }
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { /* no project root */ }
        return found.FirstOrDefault(x => x is not null);
    }

    private static bool Has(string directory, string marker) =>
        marker.Contains('*') ? Directory.EnumerateFiles(directory, marker, Simple).Any() : File.Exists(Path.Combine(directory, marker));
}
