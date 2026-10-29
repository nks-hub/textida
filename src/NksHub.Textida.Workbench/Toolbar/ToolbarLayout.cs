using System.Text.Json;
using NksHub.Textida.Workbench.Contributions;

namespace NksHub.Textida.Workbench.Toolbar;

/// <summary>
/// The buttons of the user toolbar: the set a profile starts with, reading and writing <c>toolbar.json</c>, and the rules that keep a list usable. A button is the id of a command, or
/// <see cref="Separator"/>. A button is a name only: whether its command exists is asked when the toolbar is drawn, so a button for a command that is not there (a plugin that was
/// removed, a feature of a later version) stays in the file and stays hidden.
/// </summary>
public static class ToolbarLayout
{
    /// <summary>Name of the file in the profile that holds the buttons.</summary>
    public const string FileName = "toolbar.json";

    /// <summary>The entry that draws a thin line between two groups of buttons.</summary>
    public const string Separator = "-";

    /// <summary>
    /// The buttons a profile starts with: New, Open, Save, Save All | Undo, Redo | Find, Replace | Git commit, pull and push. The Git commands come with the Git plugin; where they are not
    /// registered their buttons (and the separator before them) are not shown.
    /// </summary>
    public static IReadOnlyList<string> Default { get; } =
    [
        "nks.file.new", "nks.file.open", "nks.file.save", "nks.file.saveAll", Separator,
        "nks.edit.undo", "nks.edit.redo", Separator,
        "nks.search.find", "nks.search.replace", Separator,
        "nks.git.commit", "nks.git.pull", "nks.git.push",
    ];

    /// <summary>True for a separator or an id that follows the scheme of command ids.</summary>
    public static bool IsValidButton(string? button) => button == Separator || ManifestLoader.IsValidId(button);

    /// <summary>
    /// Reads <c>toolbar.json</c>. False with the reason in <paramref name="problem"/> when the text is not JSON of this shape, has another version, holds an id that is not one, or holds more than
    /// <paramref name="maxButtons"/> buttons; <paramref name="buttons"/> is then the default set.
    /// </summary>
    public static bool TryRead(string json, int maxButtons, out IReadOnlyList<string> buttons, out string? problem)
    {
        buttons = Default;
        ToolbarFile? file;
        try { file = JsonSerializer.Deserialize(json, ContributionJson.Default.ToolbarFile); }
        catch (JsonException e) { problem = e.Message; return false; }
        if (file is null) { problem = "the file is empty"; return false; }
        if (file.SchemaVersion != 1) { problem = $"unsupported schemaVersion {file.SchemaVersion}"; return false; }
        if (file.Buttons.Count > maxButtons) { problem = $"more than {maxButtons} buttons"; return false; }
        if (file.Buttons.FirstOrDefault(b => !IsValidButton(b)) is { } bad) { problem = $"'{bad}' is not a command id"; return false; }
        buttons = Clean(file.Buttons, maxButtons);
        problem = null;
        return true;
    }

    /// <summary>The text of <c>toolbar.json</c> for these buttons.</summary>
    public static string Write(IReadOnlyList<string> buttons) =>
        JsonSerializer.Serialize(new ToolbarFile { Buttons = [.. buttons] }, ContributionJson.Default.ToolbarFile);

    /// <summary>
    /// The list as it is kept: a command only once (its first place), no separator at the start or the end or next to another, and at most <paramref name="maxButtons"/> entries.
    /// Entries that are not valid are dropped.
    /// </summary>
    public static IReadOnlyList<string> Clean(IEnumerable<string> buttons, int maxButtons)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var button in buttons)
        {
            if (!IsValidButton(button) || (button != Separator && !seen.Add(button))) continue;
            if (button == Separator && (result.Count == 0 || result[^1] == Separator)) continue;
            if (result.Count >= maxButtons) break;
            result.Add(button);
        }

        while (result.Count > 0 && result[^1] == Separator) result.RemoveAt(result.Count - 1);
        return result;
    }

    /// <summary>The buttons to draw: the ones whose command <paramref name="isAvailable"/> knows, with the separators that are left between two buttons.</summary>
    public static IReadOnlyList<string> Shown(IEnumerable<string> buttons, Func<string, bool> isAvailable) =>
        Clean(buttons.Where(b => b == Separator || isAvailable(b)), int.MaxValue);
}
