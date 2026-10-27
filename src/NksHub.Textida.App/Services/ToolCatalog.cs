using System.Text.Json;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Manifest;

namespace NksHub.Textida.App.Services;

/// <summary>A tool of the catalog: the definition, the spec the launcher runs and the title shown to the user.</summary>
internal sealed record ToolEntry(ExternalToolContribution Definition, ToolSpec Spec, string? LogPatternId)
{
    public string Title => Definition.Title;
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
/// </summary>
internal sealed class ToolCatalog(string directory)
{
    public const string FileName = "tools.json";

    public string FilePath { get; } = Path.Combine(directory, FileName);

    public ToolCatalogContent Load()
    {
        var errors = new List<string>();
        PluginContributions contributions;
        try
        {
            if (!File.Exists(FilePath)) return new([], [], []);
            contributions = JsonSerializer.Deserialize(File.ReadAllText(FilePath), PluginManifestJson.Default.PluginContributions) ?? new();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new([], [], [$"{FileName}: {e.Message}"]);
        }

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
            var timeout = t.TimeoutSeconds is { } s ? TimeSpan.FromSeconds(s) : (TimeSpan?)null;
            tools.Add(new ToolEntry(t, new ToolSpec(t.Id, t.Executable, t.Args, t.Cwd, timeout), t.LogPattern));
        }
        return new(tools, patterns, errors);
    }
}
