using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Manifest;

namespace NksHub.Textida.App.Services;

/// <summary>
/// A tool of the catalog: the definition, the spec the launcher runs and the title shown to the user. <paramref name="Place"/> is the compiled LOG pattern of the tool.
/// <paramref name="Source"/> is the config file of a tool from a project folder, or null for a tool of the profile.
/// </summary>
internal sealed record ToolEntry(ExternalToolContribution Definition, ToolSpec Spec, string? LogPatternId, LogPattern? Place = null, string? Source = null)
{
    public string Title => Definition.Title;
    public bool Foreign => Source is not null;
}

/// <summary>A LOG pattern of the catalog with its compiled forms; either part may be missing (a pattern that only gives levels has no place pattern).</summary>
internal sealed record LogPatternEntry(LogPatternContribution Definition, LogPattern? Place, LogLevelRules? Levels)
{
    public string Title => Definition.Title;
}

internal sealed record ToolCatalogContent(IReadOnlyList<ToolEntry> Tools, IReadOnlyList<LogPatternEntry> Patterns, IReadOnlyList<string> Errors);

/// <summary>
/// The tools and LOG patterns the user defined, read from <c>tools.json</c> in the profile. The file has the shape of the <c>contributes</c> part of a plugin
/// manifest (<c>externalTools</c>, <c>logPatterns</c>), so a definition written here can move into a plugin unchanged. It is read again at each use;
/// an entry that is not valid is left out and named in <see cref="ToolCatalogContent.Errors"/>, the others stay usable.
/// A folder can bring tools too: <c>.textida/tools.json</c> in the folder of the open file or above it. Those tools are marked foreign (see <see cref="ToolEntry.Foreign"/>)
/// and the launcher runs them only after the user confirmed the command and the directory.
/// </summary>
internal sealed class ToolCatalog(string directory)
{
    public const string FileName = "tools.json";
    public const string ProjectFolderName = ".textida";

    public string FilePath { get; } = Path.Combine(directory, FileName);

    /// <summary>The <c>.textida/tools.json</c> nearest to <paramref name="fromDirectory"/>, or null.</summary>
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

    /// <param name="documentDirectory">The folder of the open file; the tools of a project folder at or above it are added as foreign tools.</param>
    public ToolCatalogContent Load(string? documentDirectory = null)
    {
        var own = Read(FilePath, null);
        if (FindProjectFile(documentDirectory) is not { } project || string.Equals(Path.GetFullPath(project), Path.GetFullPath(FilePath), StringComparison.OrdinalIgnoreCase)) return own;
        var foreign = Read(project, project);
        var taken = own.Tools.Select(t => t.Spec.Id).ToHashSet(StringComparer.Ordinal);
        var errors = own.Errors.Concat(foreign.Errors).ToList();
        var tools = own.Tools.ToList();
        foreach (var t in foreign.Tools)
        {
            if (taken.Add(t.Spec.Id)) tools.Add(t);
            else errors.Add($"{t.Spec.Id}: the id is used by a tool of the profile.");
        }
        return new(tools, own.Patterns.Concat(foreign.Patterns).ToList(), errors);
    }

    private static ToolCatalogContent Read(string path, string? source)
    {
        PluginContributions contributions;
        string name = source is null ? FileName : source;
        try
        {
            if (!File.Exists(path)) return new([], [], []);
            contributions = JsonSerializer.Deserialize(File.ReadAllText(path), PluginManifestJson.Default.PluginContributions) ?? new();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new([], [], [$"{name}: {e.Message}"]);
        }

        return FromContributions(contributions, source);
    }

    /// <summary>Compiles the log patterns and tools of a manifest or tools file; an entry that is not valid is left out and named in the errors.</summary>
    internal static ToolCatalogContent FromContributions(PluginContributions contributions, string? source)
    {
        var errors = new List<string>();
        var patterns = new List<LogPatternEntry>();
        foreach (var p in contributions.LogPatterns)
        {
            try
            {
                var place = p.Pattern.Length == 0 ? null : LogPattern.Compile(p.Pattern, p.Syntax == "mask" ? LogPatternSyntax.Mask : LogPatternSyntax.Regex);
                var levels = p.Levels is { Count: > 0 } l ? LogLevelRules.Create(l) : null;
                if (place is null && levels is null) throw new LogPatternException("The pattern has neither a place pattern nor levels.");
                patterns.Add(new LogPatternEntry(p, place, levels));
            }
            catch (LogPatternException e) { errors.Add($"{p.Id}: {e.Message}"); }
        }

        var tools = new List<ToolEntry>();
        foreach (var t in contributions.ExternalTools)
        {
            if (string.IsNullOrWhiteSpace(t.Id) || string.IsNullOrWhiteSpace(t.Title) || string.IsNullOrWhiteSpace(t.Executable)) { errors.Add($"{t.Id}: id, title and executable are needed."); continue; }
            if (t.TimeoutSeconds is <= 0) { errors.Add($"{t.Id}: the time limit must be positive."); continue; }
            if (t.LogPattern is { } id && !patterns.Any(p => p.Definition.Id == id && p.Place is not null)) { errors.Add($"{t.Id}: unknown log pattern '{id}'."); continue; }
            if (Declarations(t) is not { } variables) { errors.Add($"{t.Id}: a variable type must be text or path."); continue; }
            if (t.OutputEncoding is { } encoding && FindEncoding(encoding) is null) { errors.Add($"{t.Id}: unknown output encoding '{encoding}'."); continue; }
            var place = t.LogPattern is { } pid ? patterns.First(p => p.Definition.Id == pid).Place : null;
            tools.Add(new ToolEntry(t, ToSpec(t, variables, source), t.LogPattern, place, source));
        }
        return new(tools, patterns, errors);
    }

    /// <summary>The spec the launcher runs for a definition; the one place that turns a definition into arguments, so the editor and the menu start the same thing.</summary>
    public static ToolSpec ToSpec(ExternalToolContribution t, IReadOnlyList<ToolVariableDecl> variables, string? source = null) =>
        new(t.Id, t.Executable, t.Args, t.Cwd, t.TimeoutSeconds is { } s ? TimeSpan.FromSeconds(s) : null, variables, source, t.OutputEncoding is { } name ? FindEncoding(name) : null);

    /// <summary>The encoding with this name or code page, or null when the system does not know it (an empty name too).</summary>
    public static Encoding? FindEncoding(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try { return Encoding.GetEncoding(name.Trim()); }
        catch (ArgumentException) { return null; }
    }

    /// <summary>The declared variables of a definition, or null when one has a type other than text or path.</summary>
    public static List<ToolVariableDecl>? Declarations(ExternalToolContribution t)
    {
        var result = new List<ToolVariableDecl>();
        foreach (var v in t.Variables ?? [])
        {
            if (!Enum.TryParse<ToolVariableType>(v.Type, ignoreCase: true, out var type) || !Enum.IsDefined(type)) return null;
            result.Add(new ToolVariableDecl(v.Name, type, v.Value));
        }
        return result;
    }

    /// <summary>The tool definitions of the profile exactly as written, also those that are not valid; the editor of definitions works on these. Throws <see cref="JsonException"/> for a damaged file.</summary>
    public List<ExternalToolContribution> LoadDefinitions() =>
        File.Exists(FilePath) ? JsonSerializer.Deserialize(File.ReadAllText(FilePath), PluginManifestJson.Default.PluginContributions)?.ExternalTools ?? [] : [];

    /// <summary>
    /// Writes the tools of the profile back to <c>tools.json</c> and leaves the rest of the file (the LOG patterns) as it is. Comments in the file are not kept.
    /// Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when the file cannot be written and <see cref="JsonException"/> when it cannot be read.
    /// </summary>
    public void Save(IReadOnlyList<ExternalToolContribution> tools)
    {
        var root = File.Exists(FilePath)
            ? JsonNode.Parse(File.ReadAllText(FilePath), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
              ?? throw new JsonException("The file must hold one JSON object.")
            : new JsonObject { ["logPatterns"] = new JsonArray() };
        var array = new JsonArray();
        foreach (var t in tools)
        {
            var node = new JsonObject { ["id"] = t.Id, ["title"] = t.Title, ["executable"] = t.Executable, ["args"] = new JsonArray([.. t.Args.Select(a => (JsonNode?)JsonValue.Create(a))]) };
            if (t.Cwd is { Length: > 0 }) node["cwd"] = t.Cwd;
            if (t.TimeoutSeconds is { } s) node["timeoutSeconds"] = s;
            if (t.LogPattern is { Length: > 0 }) node["logPattern"] = t.LogPattern;
            if (t.OutputEncoding is { Length: > 0 }) node["outputEncoding"] = t.OutputEncoding;
            if (t.Variables is { Count: > 0 } vars)
                node["variables"] = new JsonArray([.. vars.Select(v =>
                {
                    var item = new JsonObject { ["name"] = v.Name, ["type"] = v.Type };
                    if (v.Value is not null) item["value"] = v.Value;
                    return (JsonNode?)item;
                })]);
            array.Add((JsonNode?)node);
        }
        root["externalTools"] = array;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        File.WriteAllText(FilePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
