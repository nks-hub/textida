using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NksHub.Textida.App.Services;

/// <summary>A named set of folders with the files that were open in it. Its own settings live in <c>projects/&lt;key&gt;.settings.json</c> of the profile.</summary>
public sealed class ProjectData
{
    public string Name { get; set; } = "";
    public List<string> Folders { get; set; } = [];
    public List<string> Files { get; set; } = [];
    public string? ActiveFile { get; set; }
}

public sealed class WorkspaceData
{
    public List<ProjectData> Projects { get; set; } = [];
    public List<string> Favorites { get; set; } = [];
    /// <summary>The project whose folders the file panel shows; null for a plain folder.</summary>
    public string? ActiveProject { get; set; }
}

/// <summary>A tab of a named session. <see cref="Text"/> is set only when the tab had unsaved work (or has no file), so the work comes back with it.</summary>
public sealed class SessionTabData
{
    public string? Path { get; set; }
    public string? Text { get; set; }
    public int AnchorLine { get; set; } = 1;
    public int AnchorColumn { get; set; } = 1;
    public int CaretLine { get; set; } = 1;
    public int CaretColumn { get; set; } = 1;
}

public sealed class SessionGroupData
{
    public List<SessionTabData> Tabs { get; set; } = [];
    public int ActiveTab { get; set; }
}

public sealed class SessionData
{
    public string Name { get; set; } = "";
    public DateTimeOffset Saved { get; set; }
    public List<SessionGroupData> Groups { get; set; } = [];
    public int ActiveGroup { get; set; }
}

[JsonSerializable(typeof(WorkspaceData))]
[JsonSerializable(typeof(SessionData))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class WorkspaceJson : JsonSerializerContext;

/// <summary>
/// Projects, favorites and named sessions of the profile. <c>workspace.json</c> holds projects and favorites, every session is its own file in
/// <c>sessions/</c>, so one damaged file costs one session. Files are written to a temporary file and moved into place. A file that cannot be
/// read is skipped and listed in <see cref="Problems"/> instead of stopping the start.
/// </summary>
internal sealed class WorkspaceStore
{
    private const string WorkspaceFile = "workspace.json";

    private readonly string _directory;
    private readonly WorkspaceData _data;
    private readonly List<string> _problems = [];

    private WorkspaceStore(string directory, WorkspaceData data)
    {
        _directory = directory;
        _data = data;
    }

    /// <summary>The files that could not be read, each as "name: reason"; refreshed by <see cref="Sessions"/>.</summary>
    public IReadOnlyList<string> Problems => _problems;

    public IReadOnlyList<ProjectData> Projects => _data.Projects;

    public IReadOnlyList<string> Favorites => _data.Favorites;

    public ProjectData? Active => Find(_data.ActiveProject);

    public static WorkspaceStore Load(string directory)
    {
        WorkspaceData? data = null;
        string? problem = null;
        string path = Path.Combine(directory, WorkspaceFile);
        if (File.Exists(path))
        {
            try { data = JsonSerializer.Deserialize(File.ReadAllText(path), WorkspaceJson.Default.WorkspaceData); }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { problem = $"{WorkspaceFile}: {e.Message}"; }
        }
        var store = new WorkspaceStore(directory, data ?? new WorkspaceData());
        if (problem is not null) store._problems.Add(problem);
        return store;
    }

    // ---- projects ----

    public ProjectData? Find(string? name) => name is null ? null : _data.Projects.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds the project or replaces the one with the same name (case is ignored).</summary>
    public void SaveProject(ProjectData project)
    {
        int i = _data.Projects.FindIndex(p => string.Equals(p.Name, project.Name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) _data.Projects[i] = project;
        else _data.Projects.Add(project);
        Save();
    }

    public bool DeleteProject(string name)
    {
        if (_data.Projects.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) == 0) return false;
        if (_data.ActiveProject is { } active && string.Equals(active, name, StringComparison.OrdinalIgnoreCase)) _data.ActiveProject = null;
        Save();
        return true;
    }

    public void SetActive(string? name)
    {
        if (string.Equals(_data.ActiveProject, name, StringComparison.Ordinal)) return;
        _data.ActiveProject = name;
        try { Save(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* which project is active is a convenience; it must never stop a folder from opening */ }
    }

    public string ProjectSettingsPath(string name) => Path.Combine(_directory, "projects", Key(name) + ".settings.json");

    // ---- favorites ----

    public bool AddFavorite(string path)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (_data.Favorites.Any(f => string.Equals(f, path, MainPathComparison))) return false;
        _data.Favorites.Add(path);
        Save();
        return true;
    }

    public bool RemoveFavorite(string path)
    {
        if (_data.Favorites.RemoveAll(f => string.Equals(f, path, MainPathComparison)) == 0) return false;
        Save();
        return true;
    }

    private static StringComparison MainPathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    // ---- named sessions ----

    private string SessionPath(string name) => Path.Combine(_directory, "sessions", Key(name) + ".json");

    public void SaveSession(SessionData session)
    {
        session.Saved = DateTimeOffset.Now;
        Write(SessionPath(session.Name), JsonSerializer.Serialize(session, WorkspaceJson.Default.SessionData));
    }

    /// <summary>The saved sessions, newest first. A file that is damaged is left out and named in <see cref="Problems"/>.</summary>
    public IReadOnlyList<SessionData> Sessions()
    {
        _problems.RemoveAll(p => p.StartsWith("sessions/", StringComparison.Ordinal));
        var found = new List<SessionData>();
        string dir = Path.Combine(_directory, "sessions");
        if (!Directory.Exists(dir)) return found;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                var session = JsonSerializer.Deserialize(File.ReadAllText(file), WorkspaceJson.Default.SessionData);
                if (session is null || session.Name.Length == 0) throw new JsonException("no session name");
                found.Add(session);
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                _problems.Add($"sessions/{Path.GetFileName(file)}: {e.Message}");
            }
        }
        return [.. found.OrderByDescending(s => s.Saved)];
    }

    public SessionData? FindSession(string name) => Sessions().FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool DeleteSession(string name)
    {
        string path = SessionPath(name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    // ---- files ----

    private static string Key(string name) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name.ToLowerInvariant())))[..16];

    private void Save() => Write(Path.Combine(_directory, WorkspaceFile), JsonSerializer.Serialize(_data, WorkspaceJson.Default.WorkspaceData));

    private static void Write(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }
}
