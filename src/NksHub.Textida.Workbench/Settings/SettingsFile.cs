using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NksHub.Textida.Workbench.Settings;

/// <summary>One step that upgrades a settings document from <see cref="From"/> to <c>From + 1</c>. It may change only the keys it owns.</summary>
public sealed record SettingsMigration(int From, Action<JsonObject> Apply)
{
    /// <summary>A step that renames a key in the general settings and in every language section. When the new key already exists, both keys stay as they are.</summary>
    public static SettingsMigration RenameKey(int from, string oldKey, string newKey) => new(from, root =>
    {
        foreach (var section in SettingsFile.Sections(root))
        {
            if (!section.ContainsKey(newKey) && section.Remove(oldKey, out var value))
            {
                section[newKey] = value;
            }
        }
    });
}

/// <summary>Upgrades older documents step by step. Version numbers start at 1 and have no gaps.</summary>
public sealed class SettingsMigrator
{
    private readonly Dictionary<int, SettingsMigration> _steps;

    public SettingsMigrator(int currentVersion, IEnumerable<SettingsMigration> steps)
    {
        CurrentVersion = currentVersion;
        _steps = steps.ToDictionary(s => s.From);
        for (var v = 1; v < currentVersion; v++)
        {
            if (!_steps.ContainsKey(v))
            {
                throw new ArgumentException($"Missing migration from version {v}.", nameof(steps));
            }
        }
    }

    /// <summary>Latest format this build reads and writes.</summary>
    public int CurrentVersion { get; }

    /// <summary>The migrator of this build.</summary>
    public static SettingsMigrator Default { get; } = new(1, []);

    internal void Upgrade(JsonObject root, int from)
    {
        for (var v = from; v < CurrentVersion; v++)
        {
            _steps[v].Apply(root);
            root["schemaVersion"] = v + 1;
        }
    }
}

public enum SettingsSaveResult
{
    Saved,

    /// <summary>The file was written by a newer editor; it is kept untouched so that nothing is lost.</summary>
    RefusedNewerVersion,

    /// <summary>The file could not be read as settings; it is kept untouched.</summary>
    RefusedLoadError,
}

/// <summary>
/// A settings file (user or workspace) kept as a JSON tree. Layout:
/// <c>{ "schemaVersion": 1, "settings": { "key": value }, "languages": { "csharp": { "key": value } } }</c>.
/// Everything the editor does not know (other keys, other top-level properties) stays in the tree and is written back.
/// A file with a newer schema version, or one that fails to parse, is never overwritten.
/// </summary>
public sealed class SettingsFile
{
    private readonly SettingsMigrator _migrator;

    private SettingsFile(JsonObject root, int version, int? migratedFrom, string? loadError, SettingsMigrator migrator)
    {
        Root = root;
        Version = version;
        MigratedFrom = migratedFrom;
        LoadError = loadError;
        _migrator = migrator;
    }

    public JsonObject Root { get; }

    public int Version { get; }

    /// <summary>Version of the file on disk when it was upgraded in memory.</summary>
    public int? MigratedFrom { get; }

    public string? LoadError { get; }

    public bool IsNewerThanSupported => Version > _migrator.CurrentVersion;

    public static SettingsFile Create(SettingsMigrator? migrator = null)
    {
        migrator ??= SettingsMigrator.Default;
        return new SettingsFile(new JsonObject { ["schemaVersion"] = migrator.CurrentVersion }, migrator.CurrentVersion, null, null, migrator);
    }

    public static SettingsFile Parse(string json, SettingsMigrator? migrator = null)
    {
        migrator ??= SettingsMigrator.Default;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            return Broken("Invalid JSON: " + ex.Message, migrator);
        }

        if (node is not JsonObject root)
        {
            return Broken("The root must be a JSON object.", migrator);
        }

        var version = 1;
        if (root.TryGetPropertyValue("schemaVersion", out var v))
        {
            if (v is not JsonValue value || !value.TryGetValue<int>(out version) || version < 1)
            {
                return Broken("schemaVersion must be a positive integer.", migrator);
            }
        }

        foreach (var name in new[] { "settings", "languages" })
        {
            if (root.TryGetPropertyValue(name, out var section) && section is not JsonObject)
            {
                return Broken($"'{name}' must be a JSON object.", migrator);
            }
        }

        if (root["languages"] is JsonObject languages && languages.Any(l => l.Value is not JsonObject))
        {
            return Broken("Every entry of 'languages' must be a JSON object.", migrator);
        }

        if (version >= migrator.CurrentVersion)
        {
            return new SettingsFile(root, version, null, null, migrator);
        }

        try
        {
            migrator.Upgrade(root, version);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or KeyNotFoundException)
        {
            return Broken("Migration failed: " + ex.Message, migrator);
        }

        return new SettingsFile(root, migrator.CurrentVersion, version, null, migrator);
    }

    /// <summary>Reads a file. A missing file gives an empty current-version document.</summary>
    public static SettingsFile Load(string path, SettingsMigrator? migrator = null)
    {
        if (!File.Exists(path))
        {
            return Create(migrator);
        }

        try
        {
            return Parse(File.ReadAllText(path, Encoding.UTF8), migrator);
        }
        catch (IOException ex)
        {
            return Broken("Cannot read the file: " + ex.Message, migrator ?? SettingsMigrator.Default);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Broken("Cannot read the file: " + ex.Message, migrator ?? SettingsMigrator.Default);
        }
    }

    private static SettingsFile Broken(string error, SettingsMigrator migrator) =>
        new(new JsonObject(), migrator.CurrentVersion, null, error, migrator);

    /// <summary>The general section and each language section of a document tree.</summary>
    public static IEnumerable<JsonObject> Sections(JsonObject root)
    {
        if (root["settings"] is JsonObject general)
        {
            yield return general;
        }

        if (root["languages"] is JsonObject languages)
        {
            foreach (var l in languages)
            {
                if (l.Value is JsonObject section)
                {
                    yield return section;
                }
            }
        }
    }

    public IEnumerable<string> Languages => (Root["languages"] as JsonObject)?.Select(l => l.Key) ?? [];

    /// <summary>The raw value of a key in the general section or in a language section.</summary>
    public JsonNode? Get(string key, string? language = null) => Section(language, create: false)?[key];

    public void Set(string key, JsonNode? value, string? language = null)
    {
        var section = Section(language, create: true)!;
        if (value is null)
        {
            section.Remove(key);
        }
        else
        {
            section[key] = value.DeepClone();
        }
    }

    /// <summary>Keys of the file that no registered definition describes (typos, newer or not loaded plugins). They are kept.</summary>
    public IReadOnlyList<string> UnknownKeys(SettingsRegistry registry) =>
        Sections(Root).SelectMany(s => s.Select(p => p.Key)).Where(k => !registry.TryGet(k, out _)).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Puts the general section and the language sections into a stack as the given sources.</summary>
    public void ApplyTo(SettingsStack stack, SettingSource general, SettingSource languageSource, string? name = null)
    {
        stack.SetLayer(general, Root["settings"] as JsonObject ?? new JsonObject(), null, name);
        foreach (var language in Languages)
        {
            stack.SetLayer(languageSource, (Root["languages"] as JsonObject)![language] as JsonObject, language, name);
        }
    }

    private JsonObject? Section(string? language, bool create)
    {
        if (language is null)
        {
            return Child(Root, "settings", create);
        }

        var languages = Child(Root, "languages", create);
        return languages is null ? null : Child(languages, language.ToLowerInvariant(), create);
    }

    private static JsonObject? Child(JsonObject parent, string name, bool create)
    {
        if (parent[name] is JsonObject existing)
        {
            return existing;
        }

        if (!create)
        {
            return null;
        }

        var child = new JsonObject();
        parent[name] = child;
        return child;
    }

    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            Root.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length) + "\n";
    }

    /// <summary>
    /// Writes the file atomically (temp file in the same folder, then replace). When the document was upgraded,
    /// the original is first copied to <c>name.v{N}.bak</c> (kept if it already exists).
    /// </summary>
    public SettingsSaveResult Save(string path)
    {
        if (LoadError is not null)
        {
            return SettingsSaveResult.RefusedLoadError;
        }

        if (IsNewerThanSupported)
        {
            return SettingsSaveResult.RefusedNewerVersion;
        }

        if (MigratedFrom is { } from && File.Exists(path))
        {
            var backup = $"{path}.v{from}.bak";
            if (!File.Exists(backup))
            {
                File.Copy(path, backup);
            }
        }

        AtomicFile.WriteAllText(path, ToJson());
        return SettingsSaveResult.Saved;
    }
}

public static class AtomicFile
{
    /// <summary>Writes UTF-8 text (no BOM) to a temp file next to <paramref name="path"/>, flushes it to disk and swaps it in.</summary>
    public static void WriteAllText(string path, string text)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                var bytes = new UTF8Encoding(false).GetBytes(text);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            Swap(temp, path);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
    }

    // Same retry as Core's FileSwap.Replace: a scanner holding the fresh file for a few ms fails File.Replace with 1175.
    private static void Swap(string temp, string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                return;
            }
            catch (IOException e) when (attempt < 8 && (e.HResult & 0xFFFF) is 32 or 33 or 1175 or 1176 or 1177)
            {
                Thread.Sleep(10 << Math.Min(attempt, 6));
            }
        }
    }
}
