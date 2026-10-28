using System.Text.Json;
using System.Text.Json.Nodes;
using NksHub.Textida.Workbench;
using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Contributions;
using NksHub.Textida.Workbench.Keys;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.App.Services;

/// <summary>
/// The command registry, keymap, settings stack and texts of the app, wired to the profile folder. Menus, palette and keys all read
/// from here; nothing in the shell keeps its own list of commands.
/// </summary>
internal sealed class AppWorkbench
{
    public const string UserSettingsFile = "user-settings.json";
    public const string UserKeymapFile = "keymap.json";

    private readonly string _profileDirectory;
    private SettingsFile _userFile;
    private readonly Dictionary<string, List<string>> _pluginSettings = [];

    public AppWorkbench(string profileDirectory, string language)
    {
        _profileDirectory = profileDirectory;
        Host = WorkbenchHost.CreateBuiltin();
        Platform = OperatingSystem.IsMacOS() ? Platform.MacOS : OperatingSystem.IsWindows() ? Platform.Windows : Platform.Linux;
        Settings = new SettingsStack(Host.Settings);
        _userFile = SettingsFile.Load(UserSettingsPath);
        _userFile.ApplyTo(Settings, SettingSource.User, SettingSource.UserLanguage, UserSettingsFile);
        BehaviourProfiles.ApplySettings(Settings, Profile);
        Strings = StringTable.LoadBuiltin(language);
        Keymap = BuildKeymap(out var problem);
        KeymapProblem = problem;
        Dispatcher = new KeyDispatcher(Keymap, Host.Commands);
        SearchIndex = new SettingsSearchIndex(Host.Settings, Strings);
    }

    public WorkbenchHost Host { get; }
    public CommandRegistry Commands => Host.Commands;
    public SettingsStack Settings { get; }
    public Platform Platform { get; }
    public StringTable Strings { get; private set; }
    public Keymap Keymap { get; private set; }
    public KeyDispatcher Dispatcher { get; private set; }
    public SettingsSearchIndex SearchIndex { get; private set; }
    /// <summary>Why the user's keymap file was not applied, or null.</summary>
    public string? KeymapProblem { get; private set; }
    /// <summary>Why the user's settings file cannot be changed (damaged or written by a newer version), or null.</summary>
    public string? SettingsProblem => _userFile.LoadError ?? (_userFile.IsNewerThanSupported ? "newer" : null);

    /// <summary>Id of the behaviour profile in use: <c>default</c> or <c>pspad</c>.</summary>
    public string Profile => Settings.GetString("editor.profile");

    public string UserSettingsPath => Path.Combine(_profileDirectory, UserSettingsFile);

    /// <summary>Raised with the key of a setting after it changed in the user layer.</summary>
    public event Action<string>? SettingChanged;

    private Keymap BuildKeymap(out string? problem)
    {
        problem = null;
        var profileLayer = BehaviourProfiles.KeymapLayerOf(Profile);
        var path = Path.Combine(_profileDirectory, UserKeymapFile);
        if (File.Exists(path))
        {
            try
            {
                var file = JsonSerializer.Deserialize(File.ReadAllText(path), ContributionJson.Default.KeymapFile);
                if (file is not null)
                {
                    var user = WorkbenchHost.LayerFromFile(file, KeymapLayerKind.User, UserKeymapFile);
                    var layered = Host.BuildKeymap(Platform, profileLayer is null ? [user] : [profileLayer, user]);
                    if (layered.Errors.Count == 0) return layered;
                    problem = string.Join("; ", layered.Errors);
                }
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                problem = e.Message;
            }
        }
        return profileLayer is null ? Host.BuildKeymap(Platform) : Host.BuildKeymap(Platform, profileLayer);
    }

    /// <summary>Puts the settings and the keys of the chosen profile in their layers and builds the keymap again.</summary>
    private void ApplyProfile()
    {
        BehaviourProfiles.ApplySettings(Settings, Profile);
        RebuildKeymap();
    }

    private void RebuildKeymap()
    {
        Keymap = BuildKeymap(out var problem);
        KeymapProblem = problem;
        Dispatcher = new KeyDispatcher(Keymap, Host.Commands);
    }

    /// <summary>
    /// Puts the keybindings and settings of a plugin into the keymap and the searchable settings; its commands are registered by the caller. The settings are listed under
    /// <paramref name="name"/> with the defaults of the manifest. Done once per plugin and run; returns why the settings were refused, or an empty list.
    /// </summary>
    public IReadOnlyList<string> AddPlugin(ContributionManifest manifest, string name)
    {
        if (_pluginSettings.ContainsKey(manifest.Id)) return [];
        foreach (var setting in manifest.Settings) setting.Category = name;
        var errors = Host.AddDeclarations(manifest, ManifestTrust.Plugin);
        if (errors.Count > 0) return errors;
        _pluginSettings[manifest.Id] = [.. manifest.Settings.Select(s => s.Key)];
        if (manifest.Settings.Count > 0) SearchIndex = new SettingsSearchIndex(Host.Settings, Strings);
        if (manifest.Keybindings.Count > 0) RebuildKeymap();
        return [];
    }

    /// <summary>Takes back what a plugin put into the app: its commands, menu items, keys and settings, so none of them is shown or works any more. Done for a plugin that was removed, switched off or replaced by another version.</summary>
    public void RemovePlugin(string id)
    {
        _pluginSettings.Remove(id, out var keys);
        Host.RemovePlugin(id, keys ?? []);
        SearchIndex = new SettingsSearchIndex(Host.Settings, Strings);
        RebuildKeymap();
    }

    /// <summary>The current values of the settings a plugin declared, by full key; null when it declared none.</summary>
    public JsonObject? PluginSettingValues(string pluginId)
    {
        if (!_pluginSettings.TryGetValue(pluginId, out var keys) || keys.Count == 0) return null;
        var values = new JsonObject();
        foreach (var key in keys) values[key] = Settings.Resolve(key)!.Value;
        return values;
    }

    /// <summary>The id of the plugin that declared the setting, or null.</summary>
    public string? PluginOfSetting(string key) => _pluginSettings.FirstOrDefault(p => p.Value.Contains(key)).Key;

    /// <summary>Conflicts the keybindings of a plugin take part in. A built-in, profile or user binding on the same keys wins; the plugin's binding then does not work.</summary>
    public IReadOnlyList<KeyConflict> KeyConflictsOf(string pluginId) =>
        [.. Keymap.Conflicts.Where(c => c.Winner is { Layer: KeymapLayerKind.Plugin } w && w.Source == pluginId || c.Loser is { Layer: KeymapLayerKind.Plugin } l && l.Source == pluginId)];

    public void SetLanguage(string language) => UseStrings(StringTable.LoadBuiltin(language));

    /// <summary>Uses a prepared table of texts (the layout checks lengthen them); <see cref="SetLanguage"/> brings the real ones back.</summary>
    internal void UseStrings(StringTable strings)
    {
        Strings = strings;
        SearchIndex = new SettingsSearchIndex(Host.Settings, Strings);
    }

    /// <summary>Sets (or with null removes) a value in the user layer and saves the file. Returns why it was refused, or null.</summary>
    public string? Set(string key, JsonNode? value, string? language = null)
    {
        if (!Host.Settings.TryGet(key, out var definition)) return $"unknown setting {key}";
        if (value is not null && SettingRules.Validate(definition, value) is { } reason) return reason;
        _userFile.Set(key, value, language);
        var saved = _userFile.Save(UserSettingsPath);
        if (saved != SettingsSaveResult.Saved)
        {
            _userFile = SettingsFile.Load(UserSettingsPath);
            return saved.ToString();
        }
        _userFile.ApplyTo(Settings, SettingSource.User, SettingSource.UserLanguage, UserSettingsFile);
        if (key == "editor.profile") ApplyProfile();
        SettingChanged?.Invoke(key);
        return null;
    }

    public string? Set(string key, string value) => Set(key, JsonValue.Create(value));
}
