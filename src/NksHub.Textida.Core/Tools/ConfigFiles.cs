using System.Text;

namespace NksHub.Textida.Core.Tools;

/// <summary>A configuration file of <see cref="LanguageCatalog"/> and the nearest one on disk; <paramref name="Path"/> is null when no folder from the file upwards has one.</summary>
public sealed record ConfigHit(CatalogConfig Config, string? Path);

/// <summary>
/// The configuration files that apply to a file (<c>.editorconfig</c>, <c>.clang-format</c>, <c>.rubocop.yml</c>, ...), found only by looking in the folder of the file and its parents.
/// No program is started, so a tool is never asked what it would use.
/// </summary>
public static class ConfigFiles
{
    public static IReadOnlyList<ConfigHit> For(string filePath)
    {
        var configs = LanguageCatalog.ConfigsFor(filePath);
        var hits = new string?[configs.Count];
        try
        {
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(filePath))!); dir is not null; dir = dir.Parent)
                for (int i = 0; i < configs.Count; i++)
                    if (hits[i] is null && configs[i].Names.Select(n => Path.Combine(dir.FullName, n)).FirstOrDefault(File.Exists) is { } found) hits[i] = found;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { /* what could not be read is reported as missing */ }
        return [.. configs.Select((c, i) => new ConfigHit(c, hits[i]))];
    }

    /// <summary>The folder a new configuration goes to: the nearest parent that holds <c>.git</c>, otherwise the folder of the file.</summary>
    public static string FolderForNew(string filePath)
    {
        string own = Path.GetDirectoryName(Path.GetFullPath(filePath))!;
        for (var dir = new DirectoryInfo(own); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
        return own;
    }

    /// <summary>
    /// Writes the template of <paramref name="config"/> into <see cref="FolderForNew"/> and returns the path. Never replaces a file; throws <see cref="IOException"/> when one exists
    /// and <see cref="InvalidOperationException"/> for a configuration that has no neutral template.
    /// </summary>
    public static string Create(CatalogConfig config, string filePath)
    {
        string template = config.Template ?? throw new InvalidOperationException($"{config.Names[0]} has no template.");
        string path = Path.Combine(FolderForNew(filePath), config.Names[0]);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        stream.Write(new UTF8Encoding(false).GetBytes(template));
        return path;
    }
}
