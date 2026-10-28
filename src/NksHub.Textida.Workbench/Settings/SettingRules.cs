using System.Text.Json;
using System.Text.Json.Nodes;
using NksHub.Textida.Workbench.Contributions;

namespace NksHub.Textida.Workbench.Settings;

/// <summary>
/// Where a value comes from. The declaration order is the precedence: a later source wins over an earlier one.
/// Plugin only sits above the built-in default, so a plugin never overrides the user.
/// </summary>
public enum SettingSource
{
    Default,
    Plugin,
    /// <summary>Values a behaviour profile (for example "pspad") sets; the user layer still wins.</summary>
    Profile,
    User,
    UserLanguage,
    Workspace,
    WorkspaceLanguage,
    EditorConfig,
    Document,
}

/// <summary>Widest layer allowed to set a value; narrower scopes include the wider ones.</summary>
public enum SettingScope
{
    Application,
    Workspace,
    Language,
    Document,
}

public static class SettingRules
{
    public static IReadOnlyList<string> Types { get; } = ["boolean", "integer", "number", "string"];

    public static bool IsLanguageSource(SettingSource source) =>
        source is SettingSource.UserLanguage or SettingSource.WorkspaceLanguage;

    public static bool TryParseScope(string text, out SettingScope scope)
    {
        scope = text switch
        {
            "application" => SettingScope.Application,
            "workspace" => SettingScope.Workspace,
            "language" => SettingScope.Language,
            "document" => SettingScope.Document,
            _ => (SettingScope)(-1),
        };
        return (int)scope >= 0;
    }

    /// <summary>The narrowest scope a source needs.</summary>
    public static SettingScope RequiredScope(SettingSource source) => source switch
    {
        SettingSource.Default or SettingSource.Plugin or SettingSource.Profile or SettingSource.User => SettingScope.Application,
        SettingSource.Workspace => SettingScope.Workspace,
        SettingSource.UserLanguage or SettingSource.WorkspaceLanguage => SettingScope.Language,
        _ => SettingScope.Document,
    };

    public static bool IsAllowedAt(SettingDefinition definition, SettingSource source) =>
        TryParseScope(definition.Scope, out var scope) && scope >= RequiredScope(source);

    /// <summary>Returns null when <paramref name="value"/> is valid for the definition, otherwise the reason.</summary>
    public static string? Validate(SettingDefinition definition, JsonNode? value)
    {
        if (value is not JsonValue v)
        {
            return "Value must be a boolean, number or string.";
        }

        var kind = v.GetValueKind();
        switch (definition.Type)
        {
            case "boolean":
                return kind is JsonValueKind.True or JsonValueKind.False ? null : "Expected a boolean.";
            case "integer":
                if (kind != JsonValueKind.Number || ToNumber(v) is not { } l || l != Math.Floor(l) || Math.Abs(l) > MaxExactInteger)
                {
                    return "Expected an integer.";
                }

                return CheckRange(definition, l);
            case "number":
                return kind != JsonValueKind.Number || ToNumber(v) is not { } d ? "Expected a number." : CheckRange(definition, d);
            case "string":
                if (kind != JsonValueKind.String)
                {
                    return "Expected a string.";
                }

                var s = v.GetValue<string>();
                return definition.Values is { } allowed && !allowed.Contains(s, StringComparer.Ordinal)
                    ? $"Expected one of: {string.Join(", ", allowed)}."
                    : null;
            default:
                return $"Unknown setting type '{definition.Type}'.";
        }
    }

    private const double MaxExactInteger = 9007199254740992;

    /// <summary>The numeric value of a JSON number whatever CLR type backs the node (parsed nodes and nodes built in code differ).</summary>
    public static double? ToNumber(JsonNode? node)
    {
        if (node is not JsonValue v || v.GetValueKind() != JsonValueKind.Number) return null;
        if (v.TryGetValue(out long l)) return l; // the text round trip below allocates a stream and a writer per call
        if (v.TryGetValue(out double fast)) return fast;
        return double.TryParse(ToText(v), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static string? CheckRange(SettingDefinition definition, double value)
    {
        if (definition.Minimum is { } min && value < min)
        {
            return $"Value is below the minimum {min}.";
        }

        return definition.Maximum is { } max && value > max ? $"Value is above the maximum {max}." : null;
    }

    /// <summary>Compact JSON text of a value (no indentation, non-ASCII kept).</summary>
    public static string ToText(JsonNode? node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            if (node is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                node.WriteTo(writer);
            }
        }

        return System.Text.Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }
}

/// <summary>All known setting definitions (core and contributed). Keys are unique.</summary>
public sealed class SettingsRegistry
{
    private readonly Dictionary<string, SettingDefinition> _byKey = new(StringComparer.Ordinal);
    private readonly List<SettingDefinition> _ordered = [];

    public IReadOnlyList<SettingDefinition> Definitions => _ordered;

    public bool TryGet(string key, out SettingDefinition definition) => _byKey.TryGetValue(key, out definition!);

    /// <summary>Forgets a setting (its plugin was removed); false when the key is not defined. Values that were stored for it stay where they are.</summary>
    public bool Remove(string key)
    {
        if (!_byKey.Remove(key, out var definition))
        {
            return false;
        }

        _ordered.Remove(definition);
        return true;
    }

    /// <summary>Returns an error text when the key is already defined.</summary>
    public string? Add(SettingDefinition definition, string source)
    {
        if (_byKey.ContainsKey(definition.Key))
        {
            return $"Setting '{definition.Key}' from '{source}' is already defined.";
        }

        _byKey[definition.Key] = definition;
        _ordered.Add(definition);
        return null;
    }
}
