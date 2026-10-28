using System.Text.RegularExpressions;

using NksHub.Textida.Core.Data;
namespace NksHub.Textida.Plugins.Json.Schema;

/// <param name="MaxDepth">Longest chain of schema documents counted from the schema of the file: 0 follows no reference to another document.</param>
/// <param name="MaxDocuments">Most other documents the references of one schema pull in together.</param>
public sealed record SchemaRefLimits(int MaxDepth, int MaxDocuments);

/// <summary>
/// The references of a schema, resolved. <see cref="Map"/> gives, for the value node of each <c>$ref</c> in the schema and in the documents it pulled in, the schema it points to; a reference that is not in it asks nothing.
/// <see cref="Unresolved"/> counts those; <see cref="NoConsentHost"/> is the first host a reference needed and the user has not allowed; <see cref="Documents"/> is the number of other documents that were read.
/// </summary>
public sealed class SchemaLinks(IReadOnlyDictionary<DataNode, DataNode> map)
{
    public IReadOnlyDictionary<DataNode, DataNode> Map { get; } = map;
    public int Unresolved { get; internal set; }
    public int Documents { get; internal set; }
    public string? NoConsentHost { get; internal set; }
}

/// <summary>
/// Follows the <c>$ref</c> of a schema to other documents. A reference from a file (or from the schema carried by the plugin) may name another file on this computer, or an http(s) address that is read only
/// through <see cref="SchemaLoader"/>, so only after the user allowed its host; a reference from a document that came from the network may name only the network, never a file. A document is read once however many
/// references name it, so a cycle between documents ends; the chain of documents and their number are bounded by <see cref="SchemaRefLimits"/>. A fragment is a JSON pointer; anchors are not followed.
/// </summary>
public static partial class SchemaRefs
{
    /// <summary>One document: where a relative reference in it starts from. <see cref="Remote"/> is its address when it came from the network, else <see cref="Folder"/> is its folder (null for the carried schemas, which have none).</summary>
    private sealed record Doc(DataNode Root, Uri? Remote, string? Folder);

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*:", RegexOptions.CultureInvariant, 200)]
    private static partial Regex Scheme();

    private static bool IsHttp(string address) => address.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || address.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>The value nodes of every <c>$ref</c> in the schema, leaving out the places that hold data and not schema (<c>enum</c>, <c>const</c>, <c>default</c>, <c>examples</c>).</summary>
    private static List<DataNode> RefsOf(DataNode root)
    {
        var found = new List<DataNode>();
        var stack = new Stack<DataNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            foreach (var m in node.Members)
            {
                if (m.Name == "$ref" && m.Value.Kind == DataKind.String) found.Add(m.Value);
                else if (m.Name is not ("enum" or "const" or "default" or "examples" or "example")) stack.Push(m.Value);
            }
            foreach (var item in node.Items) stack.Push(item);
        }
        return found;
    }

    /// <summary>The absolute address (an http(s) address or a path on this computer) that <paramref name="reference"/> names from <paramref name="from"/>, or null when that document may not name it.</summary>
    private static string? Absolute(Doc from, string reference)
    {
        if (from.Remote is { } baseUri)
        {
            if (!IsHttp(reference) && Scheme().IsMatch(reference)) return null; // a document from the network does not read files
            return Uri.TryCreate(baseUri, reference, out var joined) && joined.Scheme is "http" or "https" ? joined.GetLeftPart(UriPartial.Query) : null;
        }
        if (IsHttp(reference)) return Uri.TryCreate(reference, UriKind.Absolute, out var url) ? url.GetLeftPart(UriPartial.Query) : null;
        bool file = reference.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
        if (!file && Scheme().IsMatch(reference) && reference.IndexOf(':') > 1) return null; // a drive letter is no scheme
        var text = reference.Contains('%') ? Uri.UnescapeDataString(reference) : reference;
        var path = file || Path.IsPathRooted(text) ? SchemaLoader.FilePathOf(text, null) : from.Folder is null ? null : Path.Combine(from.Folder, text);
        return path is null ? null : FullPath(path);
    }

    private static string? FullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    private static string KeyOf(string absolute) => IsHttp(absolute) || !OperatingSystem.IsWindows() ? absolute : absolute.ToUpperInvariant();

    private static Doc DocOf(DataNode root, string absolute) =>
        IsHttp(absolute) ? new Doc(root, new Uri(absolute), null) : new Doc(root, null, Path.GetDirectoryName(absolute));

    /// <summary>
    /// Resolves every reference of <paramref name="root"/>, the schema of <paramref name="entry"/>, and of the documents they pull in.
    /// </summary>
    /// <param name="documentPath">The file the schema is for; a schema at a relative path is found from its folder.</param>
    /// <param name="network">False for completion and hover: a remote document is used only when a copy is on disk.</param>
    public static async Task<SchemaLinks> LinkAsync(DataNode root, SchemaEntry entry, string? documentPath, SchemaLoader loader, SchemaRefLimits limits, SchemaOptions options, bool network, CancellationToken ct = default)
    {
        var map = new Dictionary<DataNode, DataNode>();
        var links = new SchemaLinks(map);
        var docs = new Dictionary<string, Doc?>(StringComparer.Ordinal);
        var queue = new Queue<(Doc Doc, int Depth)>();

        string? rootAddress = null;
        if (entry.Url is { } url)
        {
            if (IsHttp(url)) rootAddress = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Query) : null;
            else if (!entry.IsBundled && SchemaLoader.FilePathOf(url, documentPath) is { } path) rootAddress = FullPath(path);
        }
        // a carried schema has no place to start a relative reference from, but a reference back to its published address finds it
        var rootDoc = entry.IsBundled || rootAddress is null ? new Doc(root, null, null) : DocOf(root, rootAddress);
        if (rootAddress is not null) docs[KeyOf(rootAddress)] = rootDoc;
        queue.Enqueue((rootDoc, 0));
        int tried = 0;

        while (queue.Count > 0)
        {
            var (doc, depth) = queue.Dequeue();
            foreach (var r in RefsOf(doc.Root))
            {
                int hash = r.Text.IndexOf('#');
                string address = hash < 0 ? r.Text : r.Text[..hash], fragment = hash < 0 ? "" : r.Text[(hash + 1)..];
                var target = doc;
                if (address.Length > 0)
                {
                    var absolute = Absolute(doc, address);
                    if (absolute is null) { links.Unresolved++; continue; }
                    var key = KeyOf(absolute);
                    if (!docs.TryGetValue(key, out target))
                    {
                        if (depth + 1 > limits.MaxDepth || tried >= limits.MaxDocuments || SchemaCatalog.ByAddress(absolute) is not { } wanted) { links.Unresolved++; continue; }
                        tried++;
                        var loaded = await loader.LoadAsync(wanted, null, network, ct);
                        if (loaded.Text is not null && SchemaCatalog.TryParse(loaded.Text, DataDialect.Json, options.MaxDepth, out var node, out _))
                        {
                            target = DocOf(node!, absolute);
                            links.Documents++;
                            queue.Enqueue((target, depth + 1));
                        }
                        else if (loaded.State == SchemaState.NoConsent) links.NoConsentHost ??= loaded.Host;
                        docs[key] = target;
                    }
                }
                if (target is not null && SchemaValidator.Pointer(target.Root, fragment) is { } found) map[r] = found;
                else links.Unresolved++;
            }
        }
        return links;
    }
}
