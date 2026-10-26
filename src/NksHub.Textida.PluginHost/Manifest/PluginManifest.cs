using System.Text.Json.Serialization;
using NksHub.Textida.Workbench.Contributions;

namespace NksHub.Textida.PluginHost.Manifest;

/// <summary>Contents of <c>nkspad-plugin.json</c>. Pure data; reading it never starts anything.</summary>
public sealed class PluginManifest
{
    public const string FileName = "nkspad-plugin.json";

    public int SchemaVersion { get; set; } = 1;

    /// <summary>Plugin id, e.g. <c>acme.formatter</c>; may not start with <c>nks.</c>. Every contributed id starts with it.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Plugin version, <c>major.minor.patch</c> with an optional suffix.</summary>
    public string Version { get; set; } = "";

    /// <summary>Range of plugin API versions the plugin works with; the editor must provide one inside it.</summary>
    public ApiRange Api { get; set; } = new();

    /// <summary>The process. Without it the plugin is data only and no process is ever started.</summary>
    public PluginMain? Main { get; set; }

    public PluginContributions Contributes { get; set; } = new();
}

public sealed class ApiRange
{
    public int Min { get; set; }
    public int Max { get; set; }
}

public sealed class PluginMain
{
    /// <summary>Absolute path to the program (for a script: the interpreter) or a path inside the plugin folder. Bare names are refused so no PATH lookup decides what runs.</summary>
    public string Executable { get; set; } = "";

    public List<string> Args { get; set; } = [];

    /// <summary>Working directory relative to the plugin folder; defaults to the folder.</summary>
    public string? Cwd { get; set; }

    /// <summary><c>onCommand:id</c>, <c>onLanguage:id</c>, <c>onFileType:.ext</c>, <c>onPanel:id</c>. Defaults to <c>onCommand</c> for every contributed command.</summary>
    public List<string>? ActivationEvents { get; set; }
}

public sealed class PluginContributions
{
    public List<CommandContribution> Commands { get; set; } = [];
    public List<MenuContribution> Menus { get; set; } = [];
    public List<KeybindingContribution> Keybindings { get; set; } = [];
    public List<SettingDefinition> Settings { get; set; } = [];

    /// <summary>Paths of grammar files (the format of the built-in grammars) relative to the plugin folder.</summary>
    public List<string> Grammars { get; set; } = [];

    public List<ConverterContribution> Converters { get; set; } = [];
    public List<ClipContribution> Clips { get; set; } = [];
    public List<LogPatternContribution> LogPatterns { get; set; } = [];
    public List<ExternalToolContribution> ExternalTools { get; set; } = [];
}

/// <summary>Text converter made of ordered find and replace rules.</summary>
public sealed class ConverterContribution
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>Command id that runs this item.</summary>
    public string? Command { get; set; }
    public List<ConverterRule> Rules { get; set; } = [];
}

public sealed class ConverterRule
{
    public string Find { get; set; } = "";
    public string Replace { get; set; } = "";
    /// <summary>Find is a regular expression instead of literal text.</summary>
    public bool Regex { get; set; }
}

/// <summary>Text snippet that a command inserts.</summary>
public sealed class ClipContribution
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Language { get; set; }
    public string? Command { get; set; }
}

/// <summary>Pattern that recognises a line of a log; named groups become fields.</summary>
public sealed class LogPatternContribution
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Pattern { get; set; } = "";
}

/// <summary>Program the editor runs on the current file or selection.</summary>
public sealed class ExternalToolContribution
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Executable { get; set; } = "";
    public List<string> Args { get; set; } = [];
    public string? Cwd { get; set; }
    public string? Command { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(PluginManifest))]
public sealed partial class PluginManifestJson : JsonSerializerContext;
