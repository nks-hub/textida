using System.Text;
using NksHub.Textida.PluginHost.Conversions;

namespace NksHub.Textida.App.Services;

/// <summary>
/// The recipes the user keeps as files: one JSON file per recipe in the <c>recipes</c> folder of the profile, in the format of <see cref="RecipeFile"/>.
/// A file with a problem is left out and named; the recipes of data plugins are in <see cref="ClipLibrary"/>.
/// </summary>
internal sealed class RecipeStore(string directory)
{
    public const string PluginId = "profile";

    public string Directory { get; } = directory;

    /// <summary>Every readable recipe as an entry whose plugin name is the file name; a file that cannot be read or fails the checks of a recipe is described in <paramref name="problems"/>.</summary>
    public IReadOnlyList<RecipeEntry> Load(List<(string File, string Reason)> problems)
    {
        var list = new List<RecipeEntry>();
        if (!System.IO.Directory.Exists(Directory)) return list;
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var file = RecipeFile.Parse(File.ReadAllText(path, Encoding.UTF8), out var error);
                if (file is null) problems.Add((Path.GetFileName(path), error ?? ""));
                else list.Add(new RecipeEntry(PluginId, Path.GetFileName(path), file.Recipe, file.Converters));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                problems.Add((Path.GetFileName(path), e.Message));
            }
        }
        return list;
    }
}
