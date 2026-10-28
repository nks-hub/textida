using System.Text.Json;
using System.Text.Json.Serialization;

namespace NksHub.Textida.Core.Tools;

/// <summary>What an action does around a save: <c>Format</c> before it, <c>Tool</c>, <c>Upload</c> and <c>Lint</c> (a linter of the catalog that underlines its findings) after it.</summary>
public enum SaveActionKind { Format, Tool, Upload, Lint }

/// <summary>Where a save comes from. Only a manual save runs every action; an autosave leaves out the actions marked "only on manual save"; a save made by a plugin runs none, so a plugin that saves in answer to a save cannot start a loop.</summary>
public enum SaveOrigin { Manual, Autosave, Plugin }

/// <summary>Where an action is written down: the profile of the user or the <c>.textida</c> folder of a project.</summary>
public enum SaveActionSource { Profile, Project }

/// <param name="Id">The formatter (<see cref="ConfigFormatters"/>), the linter of <see cref="LanguageCatalog"/> or the tool; empty for an upload.</param>
/// <param name="OnlyManual">The action does not run after an autosave.</param>
/// <param name="Server">For an upload of a project file: the server of the project. Null for the upload of a file opened from a server, which goes back where it came from.</param>
public sealed record SaveAction(SaveActionKind Kind, string Id, bool OnlyManual, bool Enabled, SaveActionSource Source = SaveActionSource.Profile, ServerTarget? Server = null)
{
    public string Key => Kind + ":" + Id;
}

/// <summary>
/// The default server of a project: a saved connection (its name or id) and the folder on the server that stands for <paramref name="ProjectFolder"/>, the folder that holds <c>.textida</c>.
/// A file of the project goes to the same relative path under <paramref name="RemoteFolder"/>.
/// </summary>
public sealed record ServerTarget(string Connection, string RemoteFolder, string ProjectFolder)
{
    /// <summary>The server path of <paramref name="localPath"/>, or null for a file outside the project folder or inside its <c>.textida</c> folder.</summary>
    public string? RemotePathOf(string localPath)
    {
        string relative;
        try { relative = Path.GetRelativePath(ProjectFolder, Path.GetFullPath(localPath)); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException) { return null; }
        string first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        if (Path.IsPathRooted(relative) || first is "." or ".." || string.Equals(first, SaveActionConfig.ProjectFolderName, StringComparison.OrdinalIgnoreCase)) return null;
        return RemoteFolder.TrimEnd('/') + "/" + relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>What the consent of the user is tied to: this project folder, the connection it resolved to and the server folder. A project file that names another server asks again.</summary>
    public string ApprovalKey(string connectionId) =>
        "upload:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(ProjectFolder) + "\0" + connectionId + "\0" + RemoteFolder)));
}

/// <param name="FormatTimeout">The time a formatter has before the file is saved unformatted; null while nobody has set it (the product has not decided a value), and then a formatter does not run.</param>
/// <param name="LintTimeout">The same for a linter: the time before its run is stopped and reported; null while it is not set (the app then applies its provisional limit).</param>
/// <param name="ToolPaths">The program (or the folder holding it) the user chose for a formatter or linter, by its id in <see cref="LanguageCatalog"/>. Only the profile file can set it: a project must not name the program that runs.</param>
public sealed record SaveActionSet(IReadOnlyList<SaveAction> Actions, TimeSpan? FormatTimeout, IReadOnlyList<string> Errors, TimeSpan? LintTimeout = null, IReadOnlyDictionary<string, string>? ToolPaths = null);

/// <summary>One entry of <c>saveActions.json</c>; <see cref="Kind"/> is <c>format</c>, <c>tool</c>, <c>upload</c> or <c>lint</c>.</summary>
public sealed class SaveActionEntry
{
    public string Kind { get; set; } = "";
    public string? Id { get; set; }
    public bool OnlyManual { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>An upload in a project file: the name or id of the saved connection that is the default server of the project.</summary>
    public string? Server { get; set; }
    /// <summary>With <see cref="Server"/>: the folder on the server that stands for the project folder, starting with <c>/</c>.</summary>
    public string? RemoteFolder { get; set; }
}

public sealed class SaveActionsFile
{
    public int? FormatTimeoutSeconds { get; set; }
    public int? LintTimeoutSeconds { get; set; }
    /// <summary>Program paths by formatter or linter id, for example <c>{"phpstan": "C:\tools\phpstan.phar"}</c>; read from the profile file only.</summary>
    public Dictionary<string, string>? ToolPaths { get; set; }
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

    /// <summary>
    /// Changes <c>enabled</c> and <c>onlyManual</c> of the entry of <paramref name="kind"/> and <paramref name="id"/> in a file, adding the entry at the end of the list when it is not there, and writes the file.
    /// Only the changed values (or the added entry) are written into the text, so comments and the layout of the rest of the file stay.
    /// </summary>
    public static void Edit(string path, SaveActionKind kind, string id, Action<SaveActionEntry> change)
    {
        var file = Read(path);
        string name = kind.ToString();
        int index = file.Actions.FindIndex(e => string.Equals(e.Kind, name, StringComparison.OrdinalIgnoreCase) && (kind == SaveActionKind.Upload || e.Id == id));
        var before = index >= 0 ? file.Actions[index] : null;
        var entry = before is null
            ? new SaveActionEntry { Kind = name.ToLowerInvariant(), Id = kind == SaveActionKind.Upload ? null : id, Enabled = false }
            : new SaveActionEntry { Kind = before.Kind, Id = before.Id, OnlyManual = before.OnlyManual, Enabled = before.Enabled, Server = before.Server, RemoteFolder = before.RemoteFolder };
        change(entry);
        if (File.Exists(path) && Patch(File.ReadAllBytes(path), index, before, entry) is { } patched)
        {
            File.WriteAllBytes(path, patched);
            return;
        }
        if (before is null) file.Actions.Add(entry);
        else file.Actions[index] = entry;
        Write(path, file);
    }

    /// <summary>The text of a valid file with the entry at <paramref name="index"/> changed to <paramref name="after"/> (or appended when <paramref name="before"/> is null); null when the text has no object to edit in place.</summary>
    internal static byte[]? Patch(byte[] text, int index, SaveActionEntry? before, SaveActionEntry after)
    {
        int bom = text is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0;
        var reader = new Utf8JsonReader(text.AsSpan(bom), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        int root = -1, list = -1, listEnd = -1, element = -1, open = -1, lastEnd = -1;
        bool rootHasProperties = false, inList = false;
        var values = new Dictionary<string, (int Start, int Length)>();
        try
        {
            while (reader.Read())
            {
                int at = bom + (int)reader.TokenStartIndex;
                switch (reader.TokenType, reader.CurrentDepth)
                {
                    case (JsonTokenType.StartObject, 0): root = at; break;
                    case (JsonTokenType.PropertyName, 1):
                        rootHasProperties = true;
                        inList = list < 0 && reader.ValueTextEquals("actions"u8);
                        break;
                    case (JsonTokenType.StartArray, 1) when inList: list = at; break;
                    case (JsonTokenType.EndArray, 1) when inList: listEnd = at; inList = false; break;
                    case (JsonTokenType.StartObject, 2) when inList:
                        if (++element == index) open = at;
                        break;
                    case (JsonTokenType.EndObject, 2) when inList: lastEnd = at + 1; break;
                    case (JsonTokenType.PropertyName, 3) when inList && element == index && open >= 0:
                        string property = reader.GetString()!;
                        reader.Read();
                        if (reader.TokenType is JsonTokenType.True or JsonTokenType.False) values[property] = (bom + (int)reader.TokenStartIndex, reader.ValueSpan.Length);
                        else reader.Skip();
                        break;
                }
            }
        }
        catch (JsonException) { return null; } // not UTF-8 text the reader takes: the caller writes the file anew
        if (root < 0) return null;

        var edits = new List<(int At, int Remove, string Insert)>();
        static string Bool(bool value) => value ? "true" : "false";
        if (before is not null)
        {
            if (open < 0) return null;
            foreach (var (property, was, now) in new[] { ("enabled", before.Enabled, after.Enabled), ("onlyManual", before.OnlyManual, after.OnlyManual) })
            {
                if (was == now) continue;
                edits.Add(values.TryGetValue(property, out var value) ? (value.Start, value.Length, Bool(now)) : (open + 1, 0, $"\"{property}\": {Bool(now)}, "));
            }
        }
        else
        {
            var buffer = new System.Buffers.ArrayBufferWriter<byte>();
            using (var w = new Utf8JsonWriter(buffer))
            {
                w.WriteStartObject();
                w.WriteString("kind", after.Kind);
                if (after.Id is not null) w.WriteString("id", after.Id);
                if (after.OnlyManual) w.WriteBoolean("onlyManual", true);
                w.WriteBoolean("enabled", after.Enabled);
                w.WriteEndObject();
            }
            string json = System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
            if (list >= 0 && listEnd > list) edits.Add(lastEnd > list ? (lastEnd, 0, ", " + json) : (list + 1, 0, json));
            else if (list < 0) edits.Add((root + 1, 0, $"\"actions\": [{json}]" + (rootHasProperties ? ", " : "")));
            else return null;
        }

        var result = new List<byte>(text);
        foreach (var (at, remove, insert) in edits.OrderByDescending(e => e.At))
        {
            result.RemoveRange(at, remove);
            result.InsertRange(at, System.Text.Encoding.UTF8.GetBytes(insert));
        }
        return [.. result];
    }

    public static SaveActionSet Load(string profileFile, string? projectFile)
    {
        var errors = new List<string>();
        var actions = new List<SaveAction>();
        TimeSpan? timeout = null, lintTimeout = null;
        Dictionary<string, string> toolPaths = [];
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
            if (lintTimeout is null && file.LintTimeoutSeconds is > 0) lintTimeout = TimeSpan.FromSeconds(file.LintTimeoutSeconds.Value);
            if (file.ToolPaths is { Count: > 0 } paths)
            {
                if (source == SaveActionSource.Project) errors.Add($"{Path.GetFileName(path)}: toolPaths is read from the profile file only, not from a project.");
                else foreach (var (tool, program) in paths.Where(p => !string.IsNullOrWhiteSpace(p.Value))) toolPaths[tool] = program.Trim();
            }
            foreach (var entry in file.Actions)
            {
                if (!Enum.TryParse<SaveActionKind>(entry.Kind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind)) { errors.Add($"{entry.Kind}: the kind must be format, tool, upload or lint."); continue; }
                string id = entry.Id?.Trim() ?? "";
                if (kind != SaveActionKind.Upload && id.Length == 0) { errors.Add($"{entry.Kind}: the id is needed."); continue; }
                ServerTarget? server = null;
                if (kind == SaveActionKind.Upload && entry.Server?.Trim() is { Length: > 0 } connection)
                {
                    // the server folder stands for the folder of the project file, so a server can only be named there
                    if (source != SaveActionSource.Project) { errors.Add($"{entry.Kind}: a server is named in the .textida/{FileName} of a project, not in the profile."); continue; }
                    if (entry.RemoteFolder?.Trim() is not ['/', ..] remote) { errors.Add($"{entry.Kind}: remoteFolder must be a folder on the server that starts with /."); continue; }
                    server = new ServerTarget(connection, remote, Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(path)))!);
                }
                var action = new SaveAction(kind, kind == SaveActionKind.Upload ? "" : id, entry.OnlyManual, entry.Enabled, source, server);
                int same = actions.FindIndex(a => a.Key == action.Key);
                if (same >= 0) actions[same] = action;
                else actions.Add(action);
            }
        }
        return new SaveActionSet(actions, timeout, errors, lintTimeout, toolPaths);
    }
}
