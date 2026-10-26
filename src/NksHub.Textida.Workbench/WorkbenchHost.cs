using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Contributions;
using NksHub.Textida.Workbench.Keys;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.Workbench;

/// <summary>
/// Collects contributions from manifests into one command registry, one settings registry and the default
/// keymap layers. The core's own features are added the same way a plugin would be.
/// </summary>
public sealed class WorkbenchHost
{
    public const string BuiltinManifestResource = "builtin.contributions.json";

    private readonly List<KeymapLayer> _layers = [];

    public CommandRegistry Commands { get; } = new();

    public SettingsRegistry Settings { get; } = new();

    /// <summary>Keymap layers contributed by manifests (Default for built-ins, Plugin for plugins).</summary>
    public IReadOnlyList<KeymapLayer> ContributedLayers => _layers;

    /// <summary>A host with the built-in commands, menus, keybindings and settings loaded from the embedded manifest.</summary>
    public static WorkbenchHost CreateBuiltin()
    {
        var host = new WorkbenchHost();
        var errors = host.AddManifest(BuiltinResources.ReadText(BuiltinManifestResource), ManifestTrust.Builtin);
        return errors.Count == 0
            ? host
            : throw new InvalidOperationException("Built-in manifest is invalid: " + string.Join("; ", errors));
    }

    /// <summary>Loads a manifest; nothing is added unless the whole manifest is valid and conflict-free.</summary>
    public IReadOnlyList<string> AddManifest(string json, ManifestTrust trust)
    {
        var loaded = ManifestLoader.Load(json, trust);
        return loaded.Manifest is { } manifest ? AddManifest(manifest, trust) : loaded.Errors;
    }

    public IReadOnlyList<string> AddManifest(ContributionManifest manifest, ManifestTrust trust)
    {
        var errors = ManifestLoader.Validate(manifest, trust);
        if (errors.Count > 0)
        {
            return errors;
        }

        var duplicateSettings = manifest.Settings.Where(s => Settings.TryGet(s.Key, out _)).Select(s => $"Setting '{s.Key}' from '{manifest.Id}' is already defined.").ToList();
        if (duplicateSettings.Count > 0)
        {
            return duplicateSettings;
        }

        errors = Commands.AddManifest(manifest).ToList();
        if (errors.Count > 0)
        {
            return errors;
        }

        foreach (var s in manifest.Settings)
        {
            Settings.Add(s, manifest.Id);
        }

        if (manifest.Keybindings.Count > 0)
        {
            _layers.Add(new KeymapLayer(trust == ManifestTrust.Builtin ? KeymapLayerKind.Default : KeymapLayerKind.Plugin, manifest.Id, manifest.Keybindings));
        }

        return [];
    }

    /// <summary>Builds the keymap of a platform from the contributed layers plus profile and user layers.</summary>
    public Keymap BuildKeymap(Platform platform, params KeymapLayer[] extraLayers) => new(platform, _layers.Concat(extraLayers));

    /// <summary>Turns a keymap file into a layer.</summary>
    public static KeymapLayer LayerFromFile(KeymapFile file, KeymapLayerKind kind, string source) => new(kind, source, file.Bindings);

    /// <summary>
    /// Whole-system checks: references resolve, and every command with a shortcut is reachable from a menu or the palette
    /// (not hidden). Returns the problems found.
    /// </summary>
    public IReadOnlyList<string> Validate(Keymap keymap)
    {
        var errors = Commands.Validate().ToList();
        errors.AddRange(keymap.Errors);
        var inMenus = Commands.CommandsInMenus();
        foreach (var b in keymap.Bindings)
        {
            if (!Commands.TryGet(b.CommandId, out var command))
            {
                errors.Add($"Keybinding {b} refers to unknown command '{b.CommandId}'.");
            }
            else if (command.Hidden && !inMenus.Contains(command.Id))
            {
                errors.Add($"Command '{command.Id}' has a shortcut but is in no menu and hidden from the palette.");
            }
        }

        return errors;
    }
}
