using System.Text.Json;
using System.Text.Json.Serialization;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Secrets;

namespace NksHub.Textida.App.Services;

public sealed class RecentData
{
    /// <summary>Files opened by the user, newest first.</summary>
    public List<string> Files { get; set; } = [];

    /// <summary>The folder shown in the file panel last time.</summary>
    public string? Folder { get; set; }
}

[JsonSerializable(typeof(RecentData))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class RecentJson : JsonSerializerContext;

/// <summary>
/// The recently opened files and the last folder of the file panel, kept in <c>recent.json</c> of the profile. Only opening a file adds to it;
/// a path that no longer exists stays listed (the list shows it as missing) until the user removes it or the list is full.
/// </summary>
internal sealed class RecentFiles
{
    public const int Capacity = 50;
    private const string FileName = "recent.json";

    private readonly string _path;
    private readonly RecentData _data;

    private RecentFiles(string directory, RecentData data)
    {
        _path = System.IO.Path.Combine(directory, FileName);
        _data = data;
    }

    public IReadOnlyList<string> Files => _data.Files;

    public string? Folder => _data.Folder;

    public static RecentFiles Load(string directory)
    {
        RecentData? data = null;
        try { data = JsonSerializer.Deserialize(File.ReadAllText(System.IO.Path.Combine(directory, FileName)), RecentJson.Default.RecentData); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { /* a missing or damaged list starts empty */ }
        return new RecentFiles(directory, data ?? new RecentData());
    }

    public void Add(string path)
    {
        // a URI of a file source is listed masked: the list is written to disk and shown, and the secrets of a URI belong to neither
        path = UriMask.IsUri(path) ? UriMask.Mask(path) : System.IO.Path.GetFullPath(path);
        _data.Files.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _data.Files.Insert(0, path);
        if (_data.Files.Count > Capacity) _data.Files.RemoveRange(Capacity, _data.Files.Count - Capacity);
        Save();
    }

    public void Remove(string path)
    {
        if (_data.Files.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0) Save();
    }

    /// <summary>A file or a folder was renamed or moved: the list follows it.</summary>
    public void Rename(string oldPath, string newPath)
    {
        bool changed = false;
        for (int i = 0; i < _data.Files.Count; i++)
        {
            if (PathMove.Rebase(_data.Files[i], oldPath, newPath) is not { } moved) continue;
            _data.Files[i] = moved;
            changed = true;
        }
        if (PathMove.Rebase(_data.Folder, oldPath, newPath) is { } folder) { _data.Folder = folder; changed = true; }
        if (changed) Save();
    }

    public void SetFolder(string? folder)
    {
        if (_data.Folder == folder) return;
        _data.Folder = folder;
        Save();
    }

    private void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_data, RecentJson.Default.RecentData));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // the list is a convenience; failing to store it must never get in the way of opening a file
        }
    }
}
