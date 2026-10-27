using System.Text.Json;
using System.Text.Json.Serialization;

namespace NksHub.Textida.Core.Tools;

/// <summary>What an action does around a save: <c>Format</c> before it, <c>Tool</c> and <c>Upload</c> after it.</summary>
public enum SaveActionKind { Format, Tool, Upload }

/// <summary>Where a save comes from. Only a manual save runs every action; an autosave leaves out the actions marked "only on manual save"; a save made by a plugin runs none, so a plugin that saves in answer to a save cannot start a loop.</summary>
public enum SaveOrigin { Manual, Autosave, Plugin }

/// <summary>Where an action is written down: the profile of the user or the <c>.textida</c> folder of a project.</summary>
public enum SaveActionSource { Profile, Project }

/// <param name="Id">The formatter (<see cref="ConfigFormatters"/>) or the tool of the catalog; empty for an upload.</param>
/// <param name="OnlyManual">The action does not run after an autosave.</param>
public sealed record SaveAction(SaveActionKind Kind, string Id, bool OnlyManual, bool Enabled, SaveActionSource Source = SaveActionSource.Profile)
{
    public string Key => Kind + ":" + Id;
}

/// <param name="FormatTimeout">The time a formatter has before the file is saved unformatted; null while nobody has set it (the product has not decided a value), and then a formatter does not run.</param>
public sealed record SaveActionSet(IReadOnlyList<SaveAction> Actions, TimeSpan? FormatTimeout, IReadOnlyList<string> Errors);

/// <summary>One entry of <c>saveActions.json</c>; <see cref="Kind"/> is <c>format</c>, <c>tool</c> or <c>upload</c>.</summary>
public sealed class SaveActionEntry
{
    public string Kind { get; set; } = "";
    public string? Id { get; set; }
    public bool OnlyManual { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class SaveActionsFile
{
    public int? FormatTimeoutSeconds { get; set; }
    public List<SaveActionEntry> Actions { get; set; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(SaveActionsFile))]
internal sealed partial class SaveActionsJson : JsonSerializerContext;

/// <summary>
/// The list of actions of a save, from the profile (<c>saveActions.json</c>) and from a project folder (<c>.textida/saveActions.json</c> in the folder of the file or above it).
/// A project entry replaces the entry of the profile with the same kind and id. An entry that is not valid is left out and named in <see cref="SaveActionSet.Errors"/>.
/// </summary>
public static class SaveActionConfig
{
    public const string FileName = "saveActions.json";
    public const string ProjectFolderName = ".textida";

    /// <summary>The project file nearest to <paramref name="fromDirectory"/>, or null.</summary>
    public static string? FindProjectFile(string? fromDirectory)
    {
        try
        {
            for (var dir = fromDirectory is null ? null : new DirectoryInfo(Path.GetFullPath(fromDirectory)); dir is not null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, ProjectFolderName, FileName);
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { /* no project file */ }
        return null;
    }

    /// <summary>Reads a file; a missing file is an empty one. Throws <see cref="JsonException"/> for a damaged file and <see cref="IOException"/> when it cannot be read.</summary>
    public static SaveActionsFile Read(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), SaveActionsJson.Default.SaveActionsFile) ?? new() : new();

    public static void Write(string path, SaveActionsFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(file, SaveActionsJson.Default.SaveActionsFile) + "\n");
    }

    /// <summary>Changes the entry of <paramref name="kind"/> and <paramref name="id"/> in a file, adding it when it is not there, and writes the file. Comments in the file are not kept.</summary>
    public static void Edit(string path, SaveActionKind kind, string id, Action<SaveActionEntry> change)
    {
        var file = Read(path);
        string name = kind.ToString();
        var entry = file.Actions.FirstOrDefault(e => string.Equals(e.Kind, name, StringComparison.OrdinalIgnoreCase) && (kind == SaveActionKind.Upload || e.Id == id));
        if (entry is null)
        {
            entry = new SaveActionEntry { Kind = name.ToLowerInvariant(), Id = kind == SaveActionKind.Upload ? null : id, Enabled = false };
            file.Actions.Add(entry);
        }
        change(entry);
        Write(path, file);
    }

    public static SaveActionSet Load(string profileFile, string? projectFile)
    {
        var errors = new List<string>();
        var actions = new List<SaveAction>();
        TimeSpan? timeout = null;
        foreach (var (path, source) in new (string? Location, SaveActionSource Source)[] { (profileFile, SaveActionSource.Profile), (projectFile, SaveActionSource.Project) })
        {
            if (path is null || (source == SaveActionSource.Project && string.Equals(Path.GetFullPath(path), Path.GetFullPath(profileFile), StringComparison.OrdinalIgnoreCase))) continue;
            SaveActionsFile file;
            try { file = Read(path); }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(path)}: {e.Message}");
                continue;
            }
            // the user's own limit wins over the limit a project file brings
            if (timeout is null && file.FormatTimeoutSeconds is > 0) timeout = TimeSpan.FromSeconds(file.FormatTimeoutSeconds.Value);
            foreach (var entry in file.Actions)
            {
                if (!Enum.TryParse<SaveActionKind>(entry.Kind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind)) { errors.Add($"{entry.Kind}: the kind must be format, tool or upload."); continue; }
                string id = entry.Id?.Trim() ?? "";
                if (kind != SaveActionKind.Upload && id.Length == 0) { errors.Add($"{entry.Kind}: the id is needed."); continue; }
                var action = new SaveAction(kind, kind == SaveActionKind.Upload ? "" : id, entry.OnlyManual, entry.Enabled, source);
                int same = actions.FindIndex(a => a.Key == action.Key);
                if (same >= 0) actions[same] = action;
                else actions.Add(action);
            }
        }
        return new SaveActionSet(actions, timeout, errors);
    }
}
