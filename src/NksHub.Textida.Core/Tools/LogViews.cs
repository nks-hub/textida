using System.Text.Json;
using System.Text.Json.Serialization;
using NksHub.Textida.Core.History;

namespace NksHub.Textida.Core.Tools;

/// <summary>
/// A named view of a log: what it keeps (<paramref name="Filter"/>), the rules that give the lines their level (<paramref name="LevelRules"/>, a name from
/// <c>tools.json</c>; null for the default words), the expression to highlight, and where the user was (<paramref name="Position"/>, a byte offset in <paramref name="FilePath"/>).
/// </summary>
public sealed record LogView(string Name, LogFilter Filter, string? LevelRules, string? Highlight, string? FilePath, long Position);

/// <summary>The views saved by the user, in one JSON file of the profile. Every save writes the whole file through a temporary file, so a crash leaves the old or the new one.</summary>
public sealed partial class LogViewStore(string path)
{
    private const int FormatVersion = 1;

    public string Path { get; } = path;

    public sealed class ViewDto
    {
        public string Name { get; set; } = "";
        public List<string> Levels { get; set; } = [];
        public string? FilterText { get; set; }
        public bool FilterIsRegex { get; set; }
        public string? LevelRules { get; set; }
        public string? Highlight { get; set; }
        public string? FilePath { get; set; }
        public long Position { get; set; }
    }

    public sealed class FileDto
    {
        public int Version { get; set; } = FormatVersion;
        public List<ViewDto> Views { get; set; } = [];
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(FileDto))]
    internal sealed partial class ViewsContext : JsonSerializerContext;

    /// <summary>Every readable view by name. A file that cannot be read gives an empty list and the reason in <paramref name="problem"/>; a view in it that cannot be understood is left out and named there.</summary>
    public IReadOnlyList<LogView> Load(out string? problem)
    {
        problem = null;
        if (!File.Exists(Path)) return [];
        FileDto? file;
        try { file = JsonSerializer.Deserialize(File.ReadAllBytes(Path), ViewsContext.Default.FileDto); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            problem = e.Message;
            return [];
        }
        if (file is null) { problem = "empty file"; return []; }
        var views = new List<LogView>();
        foreach (var dto in file.Views)
        {
            if (FromDto(dto) is { } view) views.Add(view);
            else problem = $"view '{dto.Name}' is not valid";
        }
        return views.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Saves the view, replacing the one of the same name (letter case ignored). Throws <see cref="IOException"/> when the file cannot be written.</summary>
    public void Save(LogView view)
    {
        var all = Load(out _).Where(v => !string.Equals(v.Name, view.Name, StringComparison.OrdinalIgnoreCase)).Append(view);
        var file = new FileDto { Views = [.. all.Select(ToDto)] };
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
            FileHistory.WriteAtomic(Path, JsonSerializer.SerializeToUtf8Bytes(file, ViewsContext.Default.FileDto));
        }
        catch (UnauthorizedAccessException e) { throw new IOException(e.Message, e); }
    }

    private static ViewDto ToDto(LogView v) => new()
    {
        Name = v.Name, Levels = [.. v.Filter.Levels.Select(l => l.ToString().ToLowerInvariant())], FilterText = v.Filter.Text, FilterIsRegex = v.Filter.TextIsRegex,
        LevelRules = v.LevelRules, Highlight = v.Highlight, FilePath = v.FilePath, Position = v.Position,
    };

    private static LogView? FromDto(ViewDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Position < 0) return null;
        var levels = new List<LogLevel>();
        foreach (string name in dto.Levels)
        {
            if (!Enum.TryParse<LogLevel>(name, ignoreCase: true, out var level) || !Enum.IsDefined(level)) return null;
            levels.Add(level);
        }
        return new LogView(dto.Name, new LogFilter(levels, dto.FilterText, dto.FilterIsRegex), dto.LevelRules, dto.Highlight, dto.FilePath, dto.Position);
    }
}
