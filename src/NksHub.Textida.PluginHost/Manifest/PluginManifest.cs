using NksHub.Textida.PluginHost.Conversions;
using System.Text.Json.Serialization;
using NksHub.Textida.Workbench.Clips;
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

    /// <summary>Permissions the plugin asks for (names of <see cref="NksHub.Textida.PluginHost.Permissions.PluginPermissionNames.Declarable"/>). The user decides on each one when the plugin is installed.</summary>
    public List<string> Permissions { get; set; } = [];
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
    public List<RecipeContribution> Recipes { get; set; } = [];
    public List<ClipContribution> Clips { get; set; } = [];
    public List<TemplateContribution> Templates { get; set; } = [];
    public List<LogPatternContribution> LogPatterns { get; set; } = [];
    public List<ExternalToolContribution> ExternalTools { get; set; } = [];
    public List<LanguageHelpContribution> LanguageHelp { get; set; } = [];

    /// <summary>Language servers the built-in language server client may start for this plugin; needs the <c>language.server</c> permission.</summary>
    public List<LanguageServerContribution> LanguageServers { get; set; } = [];
}

/// <summary>
/// A language server the plugin offers. The server serves the documents whose language mode id is in <see cref="Languages"/> or whose file name matches one of <see cref="Globs"/>
/// (<c>*</c>, <c>?</c> and <c>**</c>; a glob without <c>/</c> is matched against the file name, otherwise against the full path with <c>/</c> separators). <see cref="Locate"/>
/// <c>path</c>: <see cref="Command"/> is a bare program name looked up in <c>PATH</c>; <c>plugin</c>: it is a relative path inside the plugin folder.
/// </summary>
public sealed class LanguageServerContribution
{
    public string Id { get; set; } = "";
    /// <summary>Language mode ids; the first one is the language the server is started and shared for when a document matches only by glob.</summary>
    public List<string> Languages { get; set; } = [];
    public List<string> Globs { get; set; } = [];
    public string Command { get; set; } = "";
    public List<string> Args { get; set; } = [];
    /// <summary>Sent as <c>initializationOptions</c> of the LSP <c>initialize</c> request; must be a JSON object.</summary>
    public System.Text.Json.JsonElement? InitializationOptions { get; set; }
    public string Locate { get; set; } = "path";
}

/// <summary>
/// Where the help for a language is. <see cref="Kind"/> <c>url</c>: <see cref="Target"/> is an <c>http</c> or <c>https</c> address in which <c>${word}</c> stands for the word under the
/// caret and <c>${language}</c> for the language id; the editor shows it and opens it only after the user agrees. <c>file</c>: <see cref="Target"/> is a file inside the plugin folder,
/// opened in the editor.
/// </summary>
public sealed class LanguageHelpContribution
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>The language mode id the source belongs to, for example <c>php</c>.</summary>
    public string Language { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Target { get; set; } = "";
}

/// <summary>
/// Text converter made of ordered find and replace rules; each rule works on the result of the one before. Entity and character tables are rules with literal
/// text, the rest are regular expressions of the dialect of the search. <see cref="Samples"/> are test texts that are converted when the plugin is installed
/// (a converter whose sample gives another result is refused); the first one is what the list shows when nothing is selected.
/// </summary>
public sealed class ConverterContribution
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>Command id that runs this item.</summary>
    public string? Command { get; set; }
    public List<ConverterRule> Rules { get; set; } = [];
    public List<ConverterSample> Samples { get; set; } = [];
    /// <summary>Also offered in Copy Table As: the converter gets the selected cells as Copy writes them (tab-separated, one row per line) and its result goes to the clipboard.</summary>
    public bool Table { get; set; }
}

public sealed class ConverterRule
{
    public string Find { get; set; } = "";
    public string Replace { get; set; } = "";
    /// <summary>Find is a regular expression instead of literal text; Replace then may use <c>$1</c>, <c>${name}</c>, <c>$&amp;</c> and the case codes of the search.</summary>
    public bool Regex { get; set; }
    public bool IgnoreCase { get; set; }
}

public sealed class ConverterSample
{
    public string Input { get; set; } = "";
    public string Expected { get; set; } = "";
}

/// <summary>
/// An edit recipe: steps that first choose parts of the open file and then change each chosen part. Step kinds, selecting ones first:
/// <c>matches</c> (keep the matches of <c>find</c> inside the current parts), <c>columns</c> (keep columns <c>from</c> to <c>to</c> of every line, counted in
/// characters from 1; <c>to</c> 0 or missing means the end of the line); changing ones: <c>convert</c> (the converter of the same plugin named in
/// <c>converter</c>), <c>replace</c> (find and replace like a converter rule), <c>set</c> (the part becomes <c>text</c> in the template language of clips,
/// where <c>${selection}</c> is the part itself), <c>format</c> (the formatter named in <c>formatter</c> runs over the whole result; only over a set of files, and last).
/// Without a selection the recipe works on the whole file.
/// </summary>
public sealed class RecipeContribution
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Command { get; set; }
    public List<RecipeStep> Steps { get; set; } = [];
}

public sealed class RecipeStep
{
    public const string Matches = "matches", Columns = "columns", Convert = "convert", Replace = "replace", Set = "set", Format = "format";

    public string Kind { get; set; } = "";
    public string Find { get; set; } = "";
    public string Replacement { get; set; } = "";
    public bool Regex { get; set; }
    public bool IgnoreCase { get; set; }
    public bool WholeWord { get; set; }
    public int From { get; set; }
    public int To { get; set; }
    public string Converter { get; set; } = "";
    public string Text { get; set; } = "";
    public string Formatter { get; set; } = "";

    public bool Selects => Kind is Matches or Columns;
}

/// <summary>
/// Text that goes into the document at every caret. The text uses the template language of <see cref="Variables"/> (<c>${name}</c>, <c>$|</c> for carets, <c>$$</c>);
/// <see cref="Fields"/> make a form the user fills in first. A clip with a <see cref="Command"/> has its own command; every clip is in the clip list.
/// </summary>
public sealed class ClipContribution
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Language { get; set; }
    public string? Command { get; set; }
    public List<ClipField> Fields { get; set; } = [];
}

/// <summary>Content of a new file; same template language and form as a clip. The carets (<c>$|</c>) stand where the template says.</summary>
public sealed class TemplateContribution
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Command { get; set; }
    public List<ClipField> Fields { get; set; } = [];
}

/// <summary>
/// Pattern that finds the place of an error in a line of tool output. <see cref="Syntax"/> <c>mask</c> is the language of PSPad (<c>* %F %L %C %S %E %D</c>);
/// <c>regex</c> (the default) is a regular expression with the groups <c>file</c>, <c>line</c> and <c>column</c>.
/// </summary>
public sealed class LogPatternContribution
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Pattern { get; set; } = "";
    public string? Syntax { get; set; }
    /// <summary>Regular expressions that give a line its level, keyed <c>error</c>, <c>warning</c>, <c>info</c> or <c>debug</c>.</summary>
    public Dictionary<string, string>? Levels { get; set; }
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
    /// <summary>Seconds after which the program is ended; none when absent.</summary>
    public int? TimeoutSeconds { get; set; }
    /// <summary>Id of the log pattern that makes the output clickable.</summary>
    public string? LogPattern { get; set; }
    /// <summary>Variables the tool declares, each with a type; a value of the wrong type stops the tool before it starts.</summary>
    public List<ToolVariableContribution>? Variables { get; set; }
}

/// <summary>A variable of an external tool: <c>type</c> is <c>text</c> or <c>path</c>; without <c>value</c> the user or built-in variable of the same name is used.</summary>
public sealed class ToolVariableContribution
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "text";
    public string? Value { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true)]
[JsonSerializable(typeof(PluginManifest))]
[JsonSerializable(typeof(PluginContributions))]
[JsonSerializable(typeof(RecipeFile))]
public sealed partial class PluginManifestJson : JsonSerializerContext;
