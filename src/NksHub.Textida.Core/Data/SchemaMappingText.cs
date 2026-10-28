namespace NksHub.Textida.Core.Data;

/// <summary>One rule of the setting <c>schema.mappings</c>: a mask (<see cref="Glob"/>) mapped to a schema (<see cref="Schema"/>: the id of a known schema, an address or a file path) and optionally to a dialect name.</summary>
public sealed record SchemaMappingEntry(string Glob, string Schema, string? Dialect);

/// <summary>
/// The text of the setting <c>schema.mappings</c>: rules separated by ';' or line breaks, each written <c>MASK=SCHEMA</c> or <c>MASK=SCHEMA as DIALECT</c> with the dialect one of <see cref="Dialects"/>.
/// A line that starts with '#' is a comment. The first '=' ends the mask, so the schema may hold '=' (an address with a query).
/// </summary>
public static class SchemaMappingText
{
    public static readonly IReadOnlyList<string> Dialects = ["json", "jsonc", "toml", "yaml", "markdown"];

    /// <summary>The rules of <paramref name="text"/> in order; <paramref name="rejected"/> lists the rules that are not written as above (they are left out).</summary>
    public static IReadOnlyList<SchemaMappingEntry> Parse(string? text, out IReadOnlyList<string> rejected)
    {
        var entries = new List<SchemaMappingEntry>();
        var bad = new List<string>();
        foreach (var raw in (text ?? "").Split(';', '\r', '\n'))
        {
            var part = raw.Trim();
            if (part.Length == 0 || part[0] == '#') continue;
            int eq = part.IndexOf('=');
            if (eq <= 0) { bad.Add(part); continue; }
            string glob = part[..eq].Trim(), schema = part[(eq + 1)..].Trim();
            string? dialect = null;
            int at = schema.LastIndexOf(" as ", StringComparison.OrdinalIgnoreCase);
            if (at > 0 && Dialects.FirstOrDefault(d => d.Equals(schema[(at + 4)..].Trim(), StringComparison.OrdinalIgnoreCase)) is { } known)
            {
                dialect = known;
                schema = schema[..at].Trim();
            }
            if (glob.Length == 0 || schema.Length == 0) bad.Add(part);
            else entries.Add(new SchemaMappingEntry(glob, schema, dialect));
        }
        rejected = bad;
        return entries;
    }
}
