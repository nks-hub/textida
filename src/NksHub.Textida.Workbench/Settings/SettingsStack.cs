using System.Text.Json.Nodes;

namespace NksHub.Textida.Workbench.Settings;

/// <summary>A value that was present in a layer but ignored, with the reason.</summary>
public sealed record SkippedValue(SettingSource Source, string Reason);

/// <summary>The effective value of a setting and where it comes from.</summary>
public sealed record ResolvedSetting(string Key, JsonNode Value, SettingSource Source, string? Language, string? LayerName, IReadOnlyList<SkippedValue> Skipped)
{
    public string ValueText => SettingRules.ToText(Value);
}

/// <summary>
/// Layered setting values. The winner of each key is the highest <see cref="SettingSource"/> that has a valid value
/// the key's scope allows; invalid or out-of-scope values are skipped (and reported), never applied.
/// Only keys defined in the registry resolve; other keys stay in the files untouched.
/// </summary>
public sealed class SettingsStack(SettingsRegistry registry)
{
    private sealed record Layer(JsonObject Values, string? Name);

    private readonly Dictionary<(SettingSource, string), Layer> _layers = [];

    public SettingsRegistry Registry => registry;

    /// <summary>Sets or removes (null) the values of a layer. Language layers need a language id.</summary>
    public void SetLayer(SettingSource source, JsonObject? values, string? language = null, string? name = null)
    {
        if (source == SettingSource.Default)
        {
            throw new ArgumentException("Defaults come from the setting definitions.", nameof(source));
        }

        if (SettingRules.IsLanguageSource(source) != (language is not null))
        {
            throw new ArgumentException("A language is required exactly for language layers.", nameof(language));
        }

        var slot = (source, language?.ToLowerInvariant() ?? "");
        if (values is null)
        {
            _layers.Remove(slot);
        }
        else
        {
            _layers[slot] = new Layer((JsonObject)values.DeepClone(), name);
        }
    }

    /// <summary>Resolves a key for an optional language. Returns null for keys that are not defined.</summary>
    public ResolvedSetting? Resolve(string key, string? language = null) => Resolve(key, language, clone: true);

    /// <summary>The typed getters only read the value, so they skip the copy that protects the layers from a caller that edits the result.</summary>
    private ResolvedSetting? Resolve(string key, string? language, bool clone)
    {
        if (!registry.TryGet(key, out var definition))
        {
            return null;
        }

        List<SkippedValue>? skipped = null;
        var lang = language?.ToLowerInvariant();
        foreach (var source in SourcesByPrecedence)
        {
            if (source == SettingSource.Default)
            {
                break;
            }

            var isLanguage = SettingRules.IsLanguageSource(source);
            if (isLanguage && lang is null)
            {
                continue;
            }

            if (!_layers.TryGetValue((source, isLanguage ? lang! : ""), out var layer) || !layer.Values.TryGetPropertyValue(key, out var value))
            {
                continue;
            }

            if (!SettingRules.IsAllowedAt(definition, source))
            {
                (skipped ??= []).Add(new SkippedValue(source, $"Setting '{key}' cannot be set in this layer (scope '{definition.Scope}')."));
            }
            else if (SettingRules.Validate(definition, value) is { } reason)
            {
                (skipped ??= []).Add(new SkippedValue(source, reason));
            }
            else
            {
                return new ResolvedSetting(key, clone ? value!.DeepClone() : value!, source, isLanguage ? lang : null, layer.Name, (IReadOnlyList<SkippedValue>?)skipped ?? []);
            }
        }

        return new ResolvedSetting(key, clone ? definition.Default!.DeepClone() : definition.Default!, SettingSource.Default, null, null, (IReadOnlyList<SkippedValue>?)skipped ?? []);
    }

    private static readonly SettingSource[] SourcesByPrecedence = [.. Enum.GetValues<SettingSource>().OrderDescending()];

    public bool GetBool(string key, string? language = null) => Resolve(key, language, clone: false)!.Value.GetValue<bool>();

    public long GetInt(string key, string? language = null) => (long)SettingRules.ToNumber(Resolve(key, language, clone: false)!.Value)!.Value;

    public double GetNumber(string key, string? language = null) => SettingRules.ToNumber(Resolve(key, language, clone: false)!.Value)!.Value;

    public string GetString(string key, string? language = null) => Resolve(key, language, clone: false)!.Value.GetValue<string>();
}
