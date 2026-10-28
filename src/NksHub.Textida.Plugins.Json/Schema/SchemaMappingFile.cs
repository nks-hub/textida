using System.Text;

using NksHub.Textida.Core.Data;
namespace NksHub.Textida.Plugins.Json.Schema;

/// <summary>
/// The file in which the editor keeps the text of the setting <c>schema.mappings</c> (see <see cref="SchemaMappingText"/>). <see cref="Refresh"/> reads it again only when its size or time changed, so a change
/// made in the settings is used by the next check without a restart. A file that is missing means no rules of the user; one that cannot be read leaves the rules as they were.
/// </summary>
/// <param name="maxBytes">Largest file that is read; a larger one is taken as unreadable.</param>
public sealed class SchemaMappingFile(string path, long maxBytes)
{
    private readonly object _gate = new();
    private (DateTime Written, long Length)? _seen;

    /// <summary>The rules of the user as the catalog needs them; an entry with a dialect name the plugin does not know keeps the dialect of the file.</summary>
    public static IEnumerable<SchemaMapping> Convert(IEnumerable<SchemaMappingEntry> entries) =>
        entries.Select(e => new SchemaMapping(e.Glob, e.Schema, e.Dialect is { } d && Enum.TryParse<DataDialect>(d, ignoreCase: true, out var dialect) ? dialect : null));

    public void Refresh(SchemaCatalog catalog)
    {
        lock (_gate)
        {
            try
            {
                var info = new FileInfo(path);
                (DateTime, long)? stamp = info.Exists ? (info.LastWriteTimeUtc, info.Length) : null;
                if (stamp == _seen) return;
                if (stamp is { } s && s.Item2 > maxBytes) return;
                var rules = stamp is null ? [] : Convert(SchemaMappingText.Parse(File.ReadAllText(path, Encoding.UTF8), out _));
                catalog.SetMappings(rules);
                _seen = stamp;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the rules stay as they were; the next check reads the file again */ }
        }
    }
}
