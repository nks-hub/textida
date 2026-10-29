using NksHub.Textida.Workbench.Contributions;

namespace NksHub.Textida.Workbench.Settings;

/// <summary>One node of the category tree of the settings window: its id is also the resource key of its title and the value of <c>category</c> in a setting that belongs to it.</summary>
public sealed record SettingCategory(string Id, string? Parent);

/// <summary>
/// The category tree of the settings window. Every built-in setting names one of these nodes in its <c>category</c>; a node can hold settings whatever its place in the tree. The settings of a plugin
/// are listed under <see cref="Plugins"/>, each plugin in a node of its own (the category of such a setting is the name of the plugin). The keys of the old flat list stay valid as <see cref="Legacy"/>
/// nodes, so a setting declared against them still finds its place.
/// </summary>
public static class SettingCategories
{
    public const string Appearance = "cat.appearance";
    public const string Editor = "cat.editor";
    public const string EditorDisplay = "cat.editor.display";
    public const string EditorTyping = "cat.editor.typing";
    public const string EditorFormat = "cat.editor.format";
    public const string EditorEncoding = "cat.editor.encoding";
    public const string Files = "cat.files";
    public const string Search = "cat.search";
    public const string Hex = "cat.hex";
    public const string Log = "cat.log";
    public const string Git = "cat.git";
    public const string History = "cat.history";
    public const string Ai = "cat.ai";
    public const string Tools = "cat.toolsLanguages";
    public const string Plugins = "cat.plugins";
    public const string Keys = "cat.keys";
    public const string System = "cat.system";
    public const string Advanced = "cat.advanced";

    /// <summary>The nodes in the order the tree shows them.</summary>
    public static IReadOnlyList<SettingCategory> All { get; } =
    [
        new(Appearance, null),
        new(Editor, null),
        new(EditorDisplay, Editor),
        new(EditorTyping, Editor),
        new(EditorFormat, Editor),
        new(EditorEncoding, Editor),
        new(Files, null),
        new(Search, null),
        new(Hex, null),
        new(Log, null),
        new(Git, null),
        new(History, null),
        new(Ai, null),
        new(Tools, null),
        new(Plugins, null),
        new(Keys, null),
        new(System, null),
        new(Advanced, null),
        new("cat.tools", null),
        new("cat.workbench", null),
        new("cat.app", null),
    ];

    /// <summary>Keys of the flat list the tree replaced; they are nodes of their own, shown only while they hold a setting.</summary>
    public static IReadOnlyList<string> Legacy { get; } = ["cat.tools", "cat.workbench", "cat.app"];

    public static bool IsKnown(string? id) => id is not null && All.Any(c => c.Id == id);

    public static string? ParentOf(string id) => All.FirstOrDefault(c => c.Id == id)?.Parent;

    /// <summary>The nodes from the top of the tree down to <paramref name="id"/>.</summary>
    public static IReadOnlyList<string> PathOf(string id)
    {
        var path = new List<string> { id };
        for (var parent = ParentOf(id); parent is not null; parent = ParentOf(parent)) path.Insert(0, parent);
        return path;
    }

    /// <summary>True when the category is not a node of the tree but the name of a plugin: the setting is listed under <see cref="Plugins"/>.</summary>
    public static bool IsPluginCategory(string? category) => !string.IsNullOrEmpty(category) && !IsKnown(category);

    /// <summary>What is wrong with the categories of built-in settings: one without a category, or with one the tree does not have, would be listed nowhere. Empty when all is well.</summary>
    public static IReadOnlyList<string> Problems(IEnumerable<SettingDefinition> builtin) =>
        [.. builtin.Where(d => !IsKnown(d.Category)).Select(d => $"Setting '{d.Key}' is in no category of the tree (category: '{d.Category ?? "none"}').")];
}
