using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NksHub.Textida.Workbench.Contributions;

/// <summary>
/// Declarative contributions of one source (the core or, later, a plugin): commands, menu items,
/// keybindings and settings. Pure data, no code. Built-in features use exactly the same shape.
/// </summary>
public sealed class ContributionManifest
{
    /// <summary>Format version of this file. Currently 1.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Owner id, e.g. <c>nks.core</c> or <c>acme.formatter</c>. Plugin ids may not start with <c>nks.</c>.</summary>
    public string Id { get; set; } = "";

    public List<CommandContribution> Commands { get; set; } = [];

    public List<MenuContribution> Menus { get; set; } = [];

    public List<KeybindingContribution> Keybindings { get; set; } = [];

    public List<SettingDefinition> Settings { get; set; } = [];
}

public sealed class CommandContribution
{
    /// <summary>Stable id, <c>nks.&lt;area&gt;.&lt;verb&gt;</c> for built-ins, <c>&lt;owner id&gt;.&lt;name&gt;</c> for plugins.</summary>
    public string Id { get; set; } = "";

    /// <summary>Resource key of the title.</summary>
    public string Title { get; set; } = "";

    /// <summary>Resource key of the category shown in the palette.</summary>
    public string? Category { get; set; }

    /// <summary>The command is available (shown, bound) only while this expression is true.</summary>
    public string? When { get; set; }

    /// <summary>The command is shown but disabled while this expression is false.</summary>
    public string? Enablement { get; set; }

    /// <summary>Command run when this one reports that it did not handle the invocation.</summary>
    public string? Fallback { get; set; }

    /// <summary><c>none</c> or <c>transaction</c>: the whole command is one undo step.</summary>
    public string Undo { get; set; } = "none";

    public bool Cancellable { get; set; }

    /// <summary>"Repeat last command" may run this command again; set on commands that change the text.</summary>
    public bool Repeatable { get; set; }

    /// <summary>A macro may record and replay this command: it needs no dialog and does its work before it returns. Off by default: any other command is refused while a macro is recorded.</summary>
    public bool Macro { get; set; }

    /// <summary>Hidden commands are not offered in the command palette.</summary>
    public bool Hidden { get; set; }

    /// <summary>Alternative names, e.g. action names of other editors, resolved to this command.</summary>
    public List<string> Aliases { get; set; } = [];
}

public sealed class MenuContribution
{
    public string Menu { get; set; } = "";

    public string Command { get; set; } = "";

    /// <summary>Items are grouped by this string (ordinal order), then sorted by <see cref="Order"/>.</summary>
    public string Group { get; set; } = "";

    public int Order { get; set; }

    /// <summary>
    /// Id of a nested menu; entries of one menu with the same id share one submenu, placed at the group and order of the first. Its title is the string <c>submenu.&lt;id&gt;</c>.
    /// A path <c>outer/inner</c> puts the entry into the submenu <c>inner</c> of the submenu <c>outer</c> (at most three levels); the title of <c>inner</c> is <c>submenu.outer.inner</c>.
    /// </summary>
    public string? Submenu { get; set; }

    public string? When { get; set; }
}

public sealed class KeybindingContribution
{
    /// <summary>Key sequence, e.g. <c>mod+s</c> or <c>mod+k mod+u</c>. <c>mod</c> is Ctrl, or Command on macOS.</summary>
    public string Key { get; set; } = "";

    /// <summary>Replaces <see cref="Key"/> on macOS.</summary>
    public string? Mac { get; set; }

    /// <summary>Command id. A leading <c>-</c> removes a binding of that command from lower layers.</summary>
    public string Command { get; set; } = "";

    public string? When { get; set; }
}

public sealed class SettingDefinition
{
    public string Key { get; set; } = "";

    /// <summary><c>boolean</c>, <c>integer</c>, <c>number</c> or <c>string</c>.</summary>
    public string Type { get; set; } = "string";

    public JsonNode? Default { get; set; }

    /// <summary>Resource key of the title.</summary>
    public string Title { get; set; } = "";

    /// <summary>Resource key of the description.</summary>
    public string? Description { get; set; }

    /// <summary>Resource key of the category.</summary>
    public string? Category { get; set; }

    /// <summary>Widest layer that may set the value: <c>application</c>, <c>workspace</c>, <c>language</c> or <c>document</c>.</summary>
    public string Scope { get; set; } = "document";

    /// <summary>Allowed values of a string setting.</summary>
    [JsonPropertyName("enum")]
    public List<string>? Values { get; set; }

    public double? Minimum { get; set; }

    public double? Maximum { get; set; }

    /// <summary>Extra search words, English.</summary>
    public List<string> Tags { get; set; } = [];
}

/// <summary>Keymap file of the user or of a profile: only keybindings.</summary>
public sealed class KeymapFile
{
    public int SchemaVersion { get; set; } = 1;

    public List<KeybindingContribution> Bindings { get; set; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true)]
[JsonSerializable(typeof(ContributionManifest))]
[JsonSerializable(typeof(KeymapFile))]
[JsonSerializable(typeof(ProfileFile))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public sealed partial class ContributionJson : JsonSerializerContext
{
}
