using System.Reflection;
using System.Text.Json;
using NksPad.Workbench.Contributions;

namespace NksPad.Workbench.Commands;

/// <summary>Resource keys to texts for one UI language, with an optional fallback table (usually English).</summary>
public sealed class StringTable(IReadOnlyDictionary<string, string> texts, StringTable? fallback = null)
{
    /// <summary>True when this table itself (not its fallback) has the key, i.e. the text is translated.</summary>
    public bool HasOwn(string key) => texts.ContainsKey(key);

    public bool Contains(string key) => texts.ContainsKey(key) || (fallback?.Contains(key) ?? false);

    /// <summary>The text, or the key itself when no table has it.</summary>
    public string Get(string key) =>
        texts.TryGetValue(key, out var text) ? text : fallback?.Get(key) ?? key;

    public static StringTable FromJson(string json, StringTable? fallback = null) =>
        new(JsonSerializer.Deserialize(json, ContributionJson.Default.DictionaryStringString) ?? [], fallback);

    /// <summary>Loads the built-in table for <paramref name="language"/> ("en", "cs"); unknown languages get English.</summary>
    public static StringTable LoadBuiltin(string language)
    {
        var english = FromJson(BuiltinResources.ReadText("strings.en.json"));
        return language == "en" || !BuiltinResources.Exists($"strings.{language}.json")
            ? english
            : FromJson(BuiltinResources.ReadText($"strings.{language}.json"), english);
    }
}

internal static class BuiltinResources
{
    public static bool Exists(string name) => Assembly.GetExecutingAssembly().GetManifestResourceInfo(name) is not null;

    public static string ReadText(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new FileNotFoundException($"Embedded resource '{name}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
