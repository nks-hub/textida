using System.Text.Json;
using System.Text.Json.Nodes;
using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Keys;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.Workbench.Contributions;

/// <summary>A behaviour profile as data: setting values for the profile settings layer and keybindings for the profile keymap layer.</summary>
public sealed class ProfileFile
{
    public int SchemaVersion { get; set; } = 1;

    public string Id { get; set; } = "";

    public JsonObject Settings { get; set; } = [];

    public List<KeybindingContribution> Keybindings { get; set; } = [];
}

/// <summary>
/// The profiles that ship with the app. A profile is a settings layer plus a keymap layer over the default ones, not other code:
/// every behaviour it changes is a setting, and a user value or user keybinding still wins over it.
/// </summary>
public static class BehaviourProfiles
{
    public const string Default = "default";
    public const string Pspad = "pspad";

    public static IReadOnlyList<string> Ids { get; } = [Default, Pspad];

    /// <summary>The profile with this id; null for the default one (no layers) and for an unknown id.</summary>
    public static ProfileFile? Load(string id)
    {
        string resource = $"profile.{id}.json";
        if (id == Default || !BuiltinResources.Exists(resource)) return null;
        return JsonSerializer.Deserialize(BuiltinResources.ReadText(resource), ContributionJson.Default.ProfileFile);
    }

    public static KeymapLayer? KeymapLayerOf(string id) =>
        Load(id) is { } profile ? new KeymapLayer(KeymapLayerKind.Profile, "profile:" + profile.Id, profile.Keybindings) : null;

    /// <summary>Puts the settings of a profile into the profile layer of <paramref name="stack"/>; the default profile empties the layer.</summary>
    public static void ApplySettings(SettingsStack stack, string id) =>
        stack.SetLayer(SettingSource.Profile, Load(id)?.Settings, null, id == Default ? null : "profile:" + id);
}
