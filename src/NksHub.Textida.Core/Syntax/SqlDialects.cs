using System.Text.Json.Nodes;

namespace NksHub.Textida.Core.Syntax;

/// <summary>An SQL dialect the editor knows: the id stored in settings and <c>.editorconfig</c>, and its name.</summary>
public sealed record SqlDialect(string Id, string Name);

/// <summary>
/// The SQL dialects. A dialect is the generic SQL grammar plus the words and rules the dialect adds (<c>Syntax/Dialects/sql.json</c>), registered as the grammar
/// <c>sql-{id}</c>; the generic dialect is the grammar <c>sql</c> itself. The editor never connects to a database, so a dialect only decides colors and formatting.
/// The dialect of a document is the first one set of: the file, its folder (<c>sql_dialect</c> in <c>.editorconfig</c>), the global setting.
/// </summary>
public static class SqlDialects
{
    public const string Generic = "generic";
    public const string FolderProperty = "sql_dialect";

    private static readonly Lazy<IReadOnlyList<SqlDialect>> Known = new(() => [new SqlDialect(Generic, "Generic"), .. Load().Select(d => new SqlDialect(d.Id, d.Name))]);

    public static IReadOnlyList<SqlDialect> All => Known.Value;

    public static bool IsKnown(string? id) => id is not null && All.Any(d => d.Id == id);

    public static string GrammarId(string dialect) => dialect == Generic || !IsKnown(dialect) ? "sql" : "sql-" + dialect;

    /// <summary>The first known dialect of <paramref name="file"/>, <paramref name="folder"/> and <paramref name="global"/> (ids compared without case); unknown or missing ones are passed over, and nothing set gives <see cref="Generic"/>.</summary>
    public static string Resolve(string? file, string? folder, string? global)
    {
        foreach (var candidate in new[] { file, folder, global })
            if (candidate?.Trim().ToLowerInvariant() is { } id && IsKnown(id)) return id;
        return Generic;
    }

    private sealed record Definition(string Id, string Name, JsonObject Node);

    private static List<Definition> Load()
    {
        using var stream = typeof(SqlDialects).Assembly.GetManifestResourceStream("Dialects/sql.json") ?? throw new FileNotFoundException("Missing embedded dialect file sql.json");
        using var reader = new StreamReader(stream);
        var list = new List<Definition>();
        foreach (var node in JsonNode.Parse(reader.ReadToEnd())!["dialects"]!.AsArray())
            list.Add(new Definition(node!["id"]!.GetValue<string>(), node["name"]!.GetValue<string>(), node.AsObject()));
        return list;
    }

    /// <summary>Adds the grammar of every dialect, made from the generic grammar <paramref name="genericJson"/>.</summary>
    internal static void Register(GrammarRegistry registry, string genericJson)
    {
        foreach (var dialect in Load())
        {
            var grammar = JsonNode.Parse(genericJson)!.AsObject();
            grammar["id"] = "sql-" + dialect.Id;
            grammar["name"] = "SQL (" + dialect.Name + ")";
            grammar.Remove("extensions"); // the file name selects the generic grammar; the dialect is chosen separately
            foreach (var group in new[] { "keywords", "types", "functions" })
            {
                var words = grammar["keywords"]!.AsArray().First(g => g!["name"]!.GetValue<string>() == group)!["words"]!.AsArray();
                foreach (var word in dialect.Node[group]!.AsArray()) words.Add((JsonNode?)JsonValue.Create(word!.GetValue<string>()));
            }
            var rules = grammar["rules"]!.AsArray();
            int at = 0;
            foreach (var rule in dialect.Node["rules"]!.AsArray()) rules.Insert(at++, rule!.DeepClone());
            registry.Add(grammar.ToJsonString());
        }
    }
}
