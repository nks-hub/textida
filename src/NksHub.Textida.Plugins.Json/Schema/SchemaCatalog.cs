using System.Reflection;

namespace NksHub.Textida.Plugins.Json.Schema;

public enum DataDialect { Json, Jsonc, Toml }

/// <summary>A schema the plugin ships. <see cref="FileMatch"/> are globs; a glob without '/' matches the file name, any other is matched against the end of the path.</summary>
public sealed record SchemaEntry(string Id, string Name, string Resource, string[] FileMatch);

/// <summary>A rule of the user: a file, folder or mask (<see cref="Glob"/>) mapped to a schema id (a bundled one) and optionally to a dialect.</summary>
public sealed record SchemaMapping(string Glob, string SchemaId, DataDialect? Dialect = null);

/// <summary>What the check of one file found. <see cref="Schema"/> is null when no schema applies (the status line shows "no schema").</summary>
public sealed record SchemaCheckResult(SchemaEntry? Schema, DataDialect Dialect, DataSyntaxException? Syntax, IReadOnlyList<SchemaIssue> Issues);

/// <summary>Bundled schemas, the user's mappings, and the check of a file. Nothing here touches the network; schemas are data embedded in the plugin.</summary>
public sealed class SchemaCatalog(IEnumerable<SchemaMapping> userMappings)
{
    public static readonly IReadOnlyList<SchemaEntry> Bundled =
    [
        new("package.json", "package.json", "schemas/package.schema.json", ["package.json"]),
        new("cargo", "Cargo.toml", "schemas/cargo.schema.json", ["Cargo.toml"]),
        new("textida-settings", "Textida settings", "schemas/textida-settings.schema.json", []),
        new("textida-keymap", "Textida keymap", "schemas/textida-keymap.schema.json", []),
    ];

    private readonly List<SchemaMapping> _user = [.. userMappings];

    public static DataDialect DialectOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".toml" => DataDialect.Toml,
        ".jsonc" => DataDialect.Jsonc,
        _ => DataDialect.Json,
    };

    /// <summary>The schema for <paramref name="path"/> and the dialect of the file; the user's rules come first, in order, then the bundled masks.</summary>
    public (SchemaEntry? Schema, DataDialect Dialect) Resolve(string path)
    {
        var dialect = DialectOf(path);
        foreach (var m in _user)
        {
            if (!Glob.IsMatch(m.Glob, path)) continue;
            if (Bundled.FirstOrDefault(b => b.Id == m.SchemaId) is { } e) return (e, m.Dialect ?? dialect);
        }
        foreach (var b in Bundled)
            if (b.FileMatch.Any(g => Glob.IsMatch(g, path))) return (b, dialect);
        return (null, dialect);
    }

    public static string LoadSchemaText(SchemaEntry e)
    {
        using var s = typeof(SchemaCatalog).Assembly.GetManifestResourceStream(e.Resource) ?? throw new InvalidOperationException($"Missing schema resource {e.Resource}.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public static bool TryParse(string text, DataDialect dialect, int maxDepth, out DataNode? node, out DataSyntaxException? error) =>
        dialect == DataDialect.Toml ? TomlDataReader.TryParse(text, maxDepth, out node, out error) : JsonDataReader.TryParse(text, dialect == DataDialect.Jsonc, maxDepth, out node, out error);

    /// <summary>Parses the file in its dialect and checks it against the schema that applies to <paramref name="path"/>.</summary>
    public SchemaCheckResult Check(string path, string text, SchemaOptions options)
    {
        var (schema, dialect) = Resolve(path);
        if (!TryParse(text, dialect, options.MaxDepth, out var data, out var syntax)) return new(schema, dialect, syntax, []);
        if (schema is null) return new(null, dialect, null, []);
        if (!TryParse(LoadSchemaText(schema), DataDialect.Json, options.MaxDepth, out var schemaNode, out _)) throw new InvalidOperationException($"The bundled schema {schema.Id} is not valid JSON.");
        return new(schema, dialect, null, new SchemaValidator(schemaNode!, options).Validate(data!));
    }

    /// <summary>Property names the schema allows in the object around <paramref name="offset"/> and that are not written there yet.</summary>
    public static IReadOnlyList<string> CompleteKeys(DataNode schemaRoot, DataNode data, int offset, SchemaOptions options)
    {
        var schemas = new List<DataNode> { schemaRoot };
        var node = data;
        while (true)
        {
            DataNode? inner = null;
            foreach (var m in node.Members)
                if (offset > m.NameEnd && offset <= m.Value.End && offset >= m.Value.Start && m.Value.Kind is DataKind.Object or DataKind.Array) { inner = m.Value; schemas = schemas.SelectMany(s => Flatten(schemaRoot, s, options.MaxDepth)).Select(s => s.Get("properties")?.Get(m.Name)).OfType<DataNode>().ToList(); break; }
            if (inner is null && node.Kind == DataKind.Array)
                foreach (var i in node.Items)
                    if (offset >= i.Start && offset <= i.End && i.Kind is DataKind.Object or DataKind.Array) { inner = i; schemas = schemas.SelectMany(s => Flatten(schemaRoot, s, options.MaxDepth)).Select(s => s.Get("items")).OfType<DataNode>().ToList(); break; }
            if (inner is null) break;
            node = inner;
        }
        if (node.Kind != DataKind.Object) return [];
        var existing = node.Members.Select(m => m.Name).ToHashSet();
        return schemas.SelectMany(s => Flatten(schemaRoot, s, options.MaxDepth))
            .Where(s => s.Get("properties") is { Kind: DataKind.Object })
            .SelectMany(s => s.Get("properties")!.Members.Select(m => m.Name))
            .Where(n => !existing.Contains(n)).Distinct().Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>The schema with its <c>$ref</c> and <c>allOf</c> parts folded in, as a list of schemas to read <c>properties</c> from.</summary>
    private static IEnumerable<DataNode> Flatten(DataNode root, DataNode s, int depth)
    {
        if (depth < 0 || s.Kind != DataKind.Object) yield break;
        yield return s;
        if (s.Get("$ref") is { Kind: DataKind.String } r && r.Text.StartsWith("#/", StringComparison.Ordinal))
        {
            DataNode? t = root;
            foreach (var part in r.Text[2..].Split('/')) t = t?.Get(part);
            if (t is not null) foreach (var x in Flatten(root, t, depth - 1)) yield return x;
        }
        if (s.Get("allOf") is { Kind: DataKind.Array } all)
            foreach (var a in all.Items) foreach (var x in Flatten(root, a, depth - 1)) yield return x;
    }
}

/// <summary>Glob masks over paths with '/' or '\' separators: <c>*</c> and <c>?</c> inside one segment, <c>**</c> over any number of segments.</summary>
public static class Glob
{
    public static bool IsMatch(string pattern, string path)
    {
        var p = path.Replace('\\', '/');
        var g = pattern.Replace('\\', '/');
        if (!g.Contains('/')) return Segment(g, p[(p.LastIndexOf('/') + 1)..]);
        if (!g.StartsWith("**/", StringComparison.Ordinal) && !g.StartsWith('/')) g = "**/" + g; // a relative mask may start anywhere in the path
        return Segments(g.Split('/', StringSplitOptions.RemoveEmptyEntries), 0, p.Split('/', StringSplitOptions.RemoveEmptyEntries), 0);
    }

    private static bool Segments(string[] g, int gi, string[] p, int pi)
    {
        if (gi == g.Length) return pi == p.Length;
        if (g[gi] == "**")
        {
            for (int k = pi; k <= p.Length; k++) if (Segments(g, gi + 1, p, k)) return true;
            return false;
        }
        return pi < p.Length && Segment(g[gi], p[pi]) && Segments(g, gi + 1, p, pi + 1);
    }

    private static bool Segment(string g, string s)
    {
        int gi = 0, si = 0, star = -1, mark = 0;
        while (si < s.Length)
        {
            if (gi < g.Length && (g[gi] == '?' || g[gi] == s[si])) { gi++; si++; }
            else if (gi < g.Length && g[gi] == '*') { star = gi++; mark = si; }
            else if (star >= 0) { gi = star + 1; si = ++mark; }
            else return false;
        }
        while (gi < g.Length && g[gi] == '*') gi++;
        return gi == g.Length;
    }
}
