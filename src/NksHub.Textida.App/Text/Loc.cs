using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NksHub.Textida.App.Text;

[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class StringsJson : JsonSerializerContext;

/// <summary>
/// UI text catalogue. One embedded JSON file per language (<c>Strings/{tag}.json</c>, flat key to text), so a language is added by dropping in a file.
/// Placeholders are named, <c>{name}</c>; <c>{{</c> and <c>}}</c> are literal braces.
/// Plural forms are separate keys <c>key.one</c>, <c>key.few</c>, <c>key.many</c>, <c>key.other</c> chosen by <see cref="PluralRules"/>.
/// A missing key falls back to the default language, then to the key itself, so a label is never empty.
/// </summary>
public static class Loc
{
    public const string DefaultLanguage = "cs";

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Catalogs = LoadAll();
    private static IReadOnlyDictionary<string, string> _current = Catalogs[DefaultLanguage];

    public static string Language { get; private set; } = DefaultLanguage;

    /// <summary>Raised after <see cref="SetLanguage"/> changed the active language.</summary>
    public static event Action? Changed;

    public static IReadOnlyCollection<string> Languages => Catalogs.Keys;

    internal static IReadOnlyDictionary<string, string> Catalog(string language) => Catalogs[language];

    /// <summary>Switches the language; an unknown tag falls back to the default. Returns the tag actually in use.</summary>
    public static string SetLanguage(string tag)
    {
        var use = Catalogs.ContainsKey(tag) ? tag : DefaultLanguage;
        if (use == Language) return use;
        Language = use;
        _current = Catalogs[use];
        Changed?.Invoke();
        return use;
    }

    public static string Get(string key, params (string Name, object Value)[] args) => Format(Lookup(key), args);

    /// <summary>Text for a count: picks the plural form of the active language; <c>{count}</c> is filled in automatically.</summary>
    public static string Plural(string key, long count, params (string Name, object Value)[] args)
    {
        var category = PluralRules.Select(Language, count);
        var text = Lookup($"{key}.{category}", fallbackKey: $"{key}.other");
        return Format(text, [("count", count), .. args]);
    }

    private static string Lookup(string key, string? fallbackKey = null) => Resolve(_current, Catalogs[DefaultLanguage], key, fallbackKey);

    /// <summary>Active language first, then the default language, then the key itself. <paramref name="fallbackKey"/> is tried within each language (plural "other").</summary>
    internal static string Resolve(IReadOnlyDictionary<string, string> current, IReadOnlyDictionary<string, string> defaults, string key, string? fallbackKey = null)
    {
        foreach (var catalog in new[] { current, defaults })
        {
            if (catalog.TryGetValue(key, out var text)) return text;
            if (fallbackKey is not null && catalog.TryGetValue(fallbackKey, out text)) return text;
        }
        return key;
    }

    internal static string Format(string template, (string Name, object Value)[] args)
    {
        if (template.IndexOf('{') < 0 && template.IndexOf('}') < 0) return template;
        var sb = new StringBuilder(template.Length + 16);
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if ((c == '{' || c == '}') && i + 1 < template.Length && template[i + 1] == c) { sb.Append(c); i++; }
            else if (c == '{')
            {
                int end = template.IndexOf('}', i);
                if (end < 0) { sb.Append(template, i, template.Length - i); break; }
                var name = template.AsSpan(i + 1, end - i - 1);
                string? value = null;
                foreach (var a in args) if (name.SequenceEqual(a.Name)) { value = Convert.ToString(a.Value, System.Globalization.CultureInfo.InvariantCulture); break; }
                sb.Append(value ?? template.Substring(i, end - i + 1));
                i = end;
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Names of the placeholders in a template, for the consistency tests.</summary>
    internal static IReadOnlySet<string> Placeholders(string template)
    {
        var set = new HashSet<string>();
        for (int i = 0; i < template.Length; i++)
        {
            if ((template[i] == '{' || template[i] == '}') && i + 1 < template.Length && template[i + 1] == template[i]) { i++; continue; }
            if (template[i] != '{') continue;
            int end = template.IndexOf('}', i);
            if (end < 0) break;
            set.Add(template.Substring(i + 1, end - i - 1));
            i = end;
        }
        return set;
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> LoadAll()
    {
        var asm = typeof(Loc).Assembly;
        const string prefix = "NksHub.Textida.App.Strings.", suffix = ".json";
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal)) continue;
            using var stream = asm.GetManifestResourceStream(name)!;
            result[name[prefix.Length..^suffix.Length]] = JsonSerializer.Deserialize(stream, StringsJson.Default.DictionaryStringString)
                ?? throw new InvalidDataException($"empty string catalogue {name}");
        }
        if (!result.ContainsKey(DefaultLanguage)) throw new InvalidDataException($"default language '{DefaultLanguage}' is not embedded");
        return result;
    }
}

/// <summary>CLDR plural categories for whole numbers. Add a language by adding a case.</summary>
public static class PluralRules
{
    /// <summary>Categories a catalogue must provide for the language (always includes "other").</summary>
    public static IReadOnlyList<string> Categories(string language) => language switch
    {
        "cs" or "sk" => ["one", "few", "other"],
        _ => ["one", "other"], // en, de and the default rule
    };

    public static string Select(string language, long n)
    {
        n = Math.Abs(n);
        return language switch
        {
            "cs" or "sk" => n == 1 ? "one" : n is >= 2 and <= 4 ? "few" : "other",
            _ => n == 1 ? "one" : "other",
        };
    }
}
