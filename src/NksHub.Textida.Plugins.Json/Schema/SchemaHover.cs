using System.Text;
using NksHub.Textida.Core.Data;

namespace NksHub.Textida.Plugins.Json.Schema;

/// <summary>What the schema says about the key or value at a position: <see cref="Text"/> describes the characters <see cref="Start"/> to <see cref="End"/> (offsets in the text).</summary>
public sealed record HoverInfo(string Text, int Start, int End);

/// <summary>
/// Hover text from a schema: the description of the property whose key or scalar value is at the position, with its type, the allowed values, the default and a note when it is deprecated.
/// The file is read as it is, so there is no text while it does not parse.
/// </summary>
public static class SchemaHover
{
    private const int MaxValues = 12;

    public static HoverInfo? Describe(DataNode schemaRoot, DataDialect dialect, string text, int position, SchemaOptions options, SchemaTexts texts)
    {
        position = Math.Clamp(position, 0, text.Length);
        if (!SchemaCatalog.TryParse(text, dialect, options.MaxDepth, out var data, out _)) return null;
        var schemas = new List<DataNode> { schemaRoot };
        var node = data!;
        while (true)
        {
            DataNode? inner = null;
            if (node.Kind == DataKind.Object)
            {
                foreach (var m in node.Members)
                {
                    var children = schemas.SelectMany(s => SchemaCatalog.Flatten(schemaRoot, s, options)).Select(s => SchemaCatalog.ChildSchema(s, m.Name, options)).OfType<DataNode>().ToList();
                    if (position >= m.NameStart && position <= m.NameEnd) return Make(schemaRoot, children, m.NameStart, m.NameEnd, options, texts);
                    if (position >= m.Value.Start && position <= m.Value.End)
                    {
                        if (m.Value.Kind is not (DataKind.Object or DataKind.Array)) return Make(schemaRoot, children, m.Value.Start, m.Value.End, options, texts);
                        inner = m.Value;
                        schemas = children;
                        break;
                    }
                }
            }
            else if (node.Kind == DataKind.Array)
            {
                foreach (var item in node.Items)
                {
                    if (position < item.Start || position > item.End) continue;
                    var children = schemas.SelectMany(s => SchemaCatalog.Flatten(schemaRoot, s, options)).Select(s => s.Get("items")).OfType<DataNode>().ToList();
                    if (item.Kind is not (DataKind.Object or DataKind.Array)) return Make(schemaRoot, children, item.Start, item.End, options, texts);
                    inner = item;
                    schemas = children;
                    break;
                }
            }
            if (inner is null) return null;
            node = inner;
        }
    }

    private static HoverInfo? Make(DataNode root, List<DataNode> schemas, int start, int end, SchemaOptions options, SchemaTexts texts)
    {
        var flat = schemas.SelectMany(s => SchemaCatalog.Flatten(root, s, options)).ToList();
        string? title = null, description = null, deprecation = null, defaultValue = null;
        bool deprecated = false;
        var types = new List<string>();
        var values = new List<string>();
        foreach (var s in flat)
        {
            title ??= Text(s, "title");
            description ??= Text(s, "markdownDescription") ?? Text(s, "description");
            if (s.Get("deprecated") is { Kind: DataKind.Boolean, Bool: true }) deprecated = true;
            deprecation ??= Text(s, "deprecationMessage");
            if (s.Get("type") is { } t)
            {
                if (t.Kind == DataKind.String) AddOnce(types, t.Text);
                else foreach (var x in t.Items.Where(i => i.Kind == DataKind.String)) AddOnce(types, x.Text);
            }
            if (s.Get("enum") is { Kind: DataKind.Array } en)
                foreach (var v in en.Items.Where(e => e.Kind is not (DataKind.Object or DataKind.Array))) AddOnce(values, Show(v));
            if (s.Get("const") is { } c && c.Kind is not (DataKind.Object or DataKind.Array)) AddOnce(values, Show(c));
            if (defaultValue is null && s.Get("default") is { } d && d.Kind is not (DataKind.Object or DataKind.Array)) defaultValue = Show(d);
        }
        if (title is null && description is null && types.Count == 0 && values.Count == 0 && !deprecated) return null;
        var sb = new StringBuilder();
        void Line(string text) => sb.Append(text).Append('\n');
        if (title is not null && title != description) Line(title);
        if (description is not null) Line(description);
        if (deprecated) Line(texts.Deprecated + (deprecation is null ? "" : ": " + deprecation));
        if (sb.Length > 0 && (types.Count > 0 || values.Count > 0 || defaultValue is not null)) sb.Append('\n');
        if (types.Count > 0) Line(texts.Type + ": " + string.Join(", ", types.Select(t => texts.TypeNames.TryGetValue(t, out var name) ? name : t)));
        if (values.Count > 0) Line(texts.Values + ": " + string.Join(", ", values.Take(MaxValues)) + (values.Count > MaxValues ? ", …" : ""));
        if (defaultValue is not null) Line(texts.Default + ": " + defaultValue);
        return new HoverInfo(sb.ToString().TrimEnd(), start, end);
    }

    private static string? Text(DataNode schema, string name) => schema.Get(name) is { Kind: DataKind.String } n && n.Text.Trim().Length > 0 ? n.Text.Trim() : null;

    private static void AddOnce(List<string> list, string value)
    {
        if (!list.Contains(value)) list.Add(value);
    }

    private static string Show(DataNode v) => v.Kind switch
    {
        DataKind.Boolean => v.Bool ? "true" : "false",
        DataKind.Null => "null",
        _ => v.Text,
    };
}
