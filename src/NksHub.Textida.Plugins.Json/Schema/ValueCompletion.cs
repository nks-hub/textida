using System.Globalization;
using NksHub.Textida.Core.Data;

namespace NksHub.Textida.Plugins.Json.Schema;

/// <summary>The values the schema offers where a value is being written: <see cref="Items"/> are the label and the text that replaces <see cref="Start"/> to <see cref="End"/> (offsets in the text the caret was given in).</summary>
public sealed record ValueOffer(IReadOnlyList<(string Label, string Insert)> Items, int Start, int End);

/// <summary>
/// Value completion from a schema: the <c>enum</c> and <c>const</c> values of the property whose value is at the caret, and <c>true</c> and <c>false</c> where the schema says boolean.
/// As for keys, the word or string at the caret is replaced by a placeholder value before the file is read, because a file is rarely valid while it is typed. The offer is made only when the
/// placeholder turns out to be a value of a property (or an array item) in the file; at a key, in a comment and in a file that does not read, there is none.
/// </summary>
public static class ValueCompletion
{
    private static bool IsValueChar(char c) => !char.IsWhiteSpace(c) && ",[]{}:#\"'=".IndexOf(c) < 0;

    /// <summary>The offer at <paramref name="caret"/>, or null when no value is written there or the schema offers none.</summary>
    public static ValueOffer? Offer(DataNode schemaRoot, DataDialect dialect, string text, int caret, SchemaOptions options)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        if (dialect == DataDialect.Markdown && (!YamlDataReader.TryFrontmatterBody(text, out int bodyStart, out int bodyEnd) || caret < bodyStart || caret >= bodyEnd)) return null;
        if (!TokenAround(dialect, text, caret, out int start, out int end, out string typed)) return null;
        string placeholder = dialect == DataDialect.Toml ? "\"\"" : "null";
        string repaired = text[..start] + placeholder + text[end..];
        if (!SchemaCatalog.TryParse(repaired, dialect, options.MaxDepth, out var data, out _)) return null;
        var (node, schemas) = SchemaCatalog.Locate(schemaRoot, data!, start, options, scalars: true);
        if (ReferenceEquals(node, data) || node.Kind is DataKind.Object or DataKind.Array || node.Start != start) return null;

        var candidates = new List<DataNode>();
        foreach (var s in schemas.SelectMany(x => SchemaCatalog.Flatten(schemaRoot, x, options)))
        {
            if (s.Get("enum") is { Kind: DataKind.Array } values) candidates.AddRange(values.Items);
            if (s.Get("const") is { } constant) candidates.Add(constant);
            if (IsBoolean(s))
            {
                candidates.Add(new DataNode(DataKind.Boolean, 0, 0) { Bool = true });
                candidates.Add(new DataNode(DataKind.Boolean, 0, 0));
            }
        }
        var items = new List<(string Label, string Insert)>();
        foreach (var c in candidates)
        {
            if (c.Kind is DataKind.Object or DataKind.Array || c.Kind == DataKind.Null && dialect == DataDialect.Toml) continue;
            var (label, insert) = Describe(c, dialect);
            if (label.StartsWith(typed, StringComparison.OrdinalIgnoreCase) && !string.Equals(label, typed, StringComparison.Ordinal) && items.All(i => i.Label != label)) items.Add((label, insert));
        }
        return items.Count == 0 ? null : new ValueOffer(items, start, end);
    }

    private static bool IsBoolean(DataNode schema) => schema.Get("type") is { } t && (t.Kind == DataKind.String ? t.Text == "boolean" : t.Items.Any(i => i.Kind == DataKind.String && i.Text == "boolean"));

    private static (string Label, string Insert) Describe(DataNode value, DataDialect dialect) => value.Kind switch
    {
        DataKind.String => (value.Text, dialect is DataDialect.Yaml or DataDialect.Markdown && IsPlainYaml(value.Text) ? value.Text : Quote(value.Text)),
        DataKind.Boolean => value.Bool ? ("true", "true") : ("false", "false"),
        DataKind.Null => ("null", "null"),
        _ => (value.Text, value.Text),
    };

    private static string Quote(string text)
    {
        var sb = new System.Text.StringBuilder("\"");
        foreach (var c in text)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    private static readonly HashSet<string> YamlWords = new(StringComparer.OrdinalIgnoreCase) { "true", "false", "null", "yes", "no", "on", "off", "y", "n", "~" };

    /// <summary>A string that reads back as the same string when it is written without quotes in YAML.</summary>
    private static bool IsPlainYaml(string text) =>
        text.Length > 0 && !YamlWords.Contains(text) && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
        && char.IsAsciiLetterOrDigit(text[0]) && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '/');

    /// <summary>
    /// The string or the word the caret is in, from <paramref name="start"/> to <paramref name="end"/>, and the part of it in front of the caret. False in a comment, and in a JSON string
    /// that is followed by a colon (a key).
    /// </summary>
    private static bool TokenAround(DataDialect dialect, string text, int caret, out int start, out int end, out string typed)
    {
        start = end = caret;
        typed = "";
        int lineStart = caret;
        while (lineStart > 0 && text[lineStart - 1] is not ('\n' or '\r')) lineStart--;
        bool json = dialect is DataDialect.Json or DataDialect.Jsonc;
        int open = -1;
        char quote = '\0';
        for (int i = lineStart; i < caret; i++)
        {
            char c = text[i];
            if (open >= 0)
            {
                if (c == '\\' && quote == '"') i++;
                else if (c == quote) open = -1;
            }
            else if (c == '"' || c == '\'' && !json) { open = i; quote = c; }
            else if (c == '#' && !json && (i == lineStart || text[i - 1] is ' ' or '\t')) return false;
            else if (c == '/' && dialect == DataDialect.Jsonc && i + 1 < text.Length && text[i + 1] == '/') return false;
        }
        if (open >= 0)
        {
            int close = caret;
            while (close < text.Length && text[close] is not ('\n' or '\r'))
            {
                if (text[close] == '\\' && quote == '"') close++;
                else if (text[close] == quote) break;
                close++;
            }
            close = Math.Min(close, text.Length);
            end = close < text.Length && text[close] == quote ? close + 1 : close;
            start = open;
            typed = text[(open + 1)..caret];
            if (json)
            {
                int after = end;
                while (after < text.Length && text[after] is ' ' or '\t') after++;
                if (after < text.Length && text[after] == ':') return false; // a key, not a value
            }
            return true;
        }
        while (start > lineStart && IsValueChar(text[start - 1])) start--;
        while (end < text.Length && IsValueChar(text[end])) end++;
        typed = text[start..caret];
        return true;
    }
}
