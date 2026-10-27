using NksHub.Textida.Core.Data;
namespace NksHub.Textida.Plugins.Json.Schema;

/// <summary>
/// The keys the schema offers at a caret and the characters of the text they replace. <see cref="Start"/> and <see cref="End"/> are offsets in the text the caret was given in;
/// <see cref="Insert"/> turns a key into the text that replaces them.
/// </summary>
public sealed record KeyOffer(IReadOnlyList<string> Keys, int Start, int End, DataDialect Dialect, bool ColonFollows)
{
    public string Insert(string key) => Dialect switch
    {
        DataDialect.Toml => (IsBare(key) ? key : Quote(key)) + " = ",
        DataDialect.Yaml or DataDialect.Markdown => (IsBare(key) ? key : Quote(key)) + (ColonFollows ? "" : ": "),
        _ => Quote(key) + (ColonFollows ? "" : ": "),
    };

    private static string Quote(string key) => "\"" + key.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static bool IsBare(string key) => key.Length > 0 && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}

/// <summary>
/// Key completion from a schema. A file is rarely valid while a key is being typed, so the part being typed is taken out before the file is read: in JSON the string
/// around the caret becomes an empty key, in TOML the word in front of the caret goes. The offer then names the keys the schema allows in the object around the caret that are not there yet.
/// </summary>
public static class KeyCompletion
{
    private const string Placeholder = "textida-key-placeholder";

    private static bool IsWordChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-';

    /// <summary>Whether the <c>]</c> at <paramref name="at"/> closes a table header: its line starts with <c>[</c>.</summary>
    private static bool EndsHeader(string text, int at)
    {
        int lineStart = at;
        while (lineStart > 0 && text[lineStart - 1] is not ('\n' or '\r')) lineStart--;
        var line = text.AsSpan(lineStart, at - lineStart).TrimStart();
        return line.Length > 0 && line[0] == '[';
    }

    /// <summary>The offer at <paramref name="caret"/> for a schema the plugin carries, or null when the caret is not where a key can be written or the file cannot be read.</summary>
    public static KeyOffer? Offer(SchemaEntry schema, DataDialect dialect, string text, int caret, SchemaOptions options) =>
        SchemaCatalog.TryParse(SchemaCatalog.LoadSchemaText(schema), DataDialect.Json, options.MaxDepth, out var schemaRoot, out _) ? Offer(schemaRoot!, dialect, text, caret, options) : null;

    /// <summary>The offer at <paramref name="caret"/> from a parsed schema, or null when the caret is not where a key can be written or the file cannot be read.</summary>
    public static KeyOffer? Offer(DataNode schemaRoot, DataDialect dialect, string text, int caret, SchemaOptions options)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        string repaired;
        int start, end, at;
        string typed;
        if (dialect == DataDialect.Toml)
        {
            start = caret;
            while (start > 0 && IsWordChar(text[start - 1])) start--;
            end = caret;
            typed = text[start..caret];
            repaired = text[..start] + text[caret..];
            // a TOML table ends with its last value, so the caret counts as inside it when it stands on the last written character before it
            at = start;
            while (at > 0 && char.IsWhiteSpace(repaired[at - 1])) at--;
            at = Math.Max(at - 1, 0);
            if (repaired.Length > 0 && repaired[at] == ']' && EndsHeader(repaired, at))
            {
                // a table with nothing in it ends with its header, so a key of ours is put in to give the table a body the caret stands in
                repaired = text[..start] + Placeholder + " = 0" + text[caret..];
                at = start;
            }
        }
        else if (dialect is DataDialect.Yaml or DataDialect.Markdown)
        {
            if (dialect == DataDialect.Markdown && (!YamlDataReader.TryFrontmatterBody(text, out int bodyStart, out int bodyEnd) || caret < bodyStart || caret >= bodyEnd)) return null;
            start = caret;
            while (start > 0 && IsWordChar(text[start - 1])) start--;
            end = caret;
            while (end < text.Length && IsWordChar(text[end])) end++;
            typed = text[start..caret];
            int lineStart = start;
            while (lineStart > 0 && text[lineStart - 1] is not ('\n' or '\r')) lineStart--;
            if (text.AsSpan(lineStart, start - lineStart).ContainsAnyExcept(' ', '-')) return null; // after a key, a ':' or other text the caret is not at a key
            int after = end;
            while (after < text.Length && text[after] is ' ' or '\t') after++;
            bool hasValue = after < text.Length && text[after] == ':' && (after + 1 >= text.Length || text[after + 1] is ' ' or '\t' or '\r' or '\n');
            // the word becomes a key that the schema does not know, so that the line reads as a key of the mapping the caret is in
            repaired = text[..start] + Placeholder + (hasValue ? "" : ": null") + text[end..];
            at = start;
        }
        else
        {
            int lineStart = caret;
            while (lineStart > 0 && text[lineStart - 1] is not ('\n' or '\r')) lineStart--;
            int open = -1;
            for (int i = lineStart; i < caret; i++)
            {
                if (text[i] == '\\' && open >= 0) i++;
                else if (text[i] == '"') open = open < 0 ? i : -1;
            }
            if (open >= 0)
            {
                int close = open + 1;
                while (close < text.Length && text[close] is not ('\n' or '\r'))
                {
                    if (text[close] == '\\') close++;
                    else if (text[close] == '"') break;
                    close++;
                }
                end = close < text.Length && text[close] == '"' ? close + 1 : Math.Min(close, text.Length);
                start = open;
                typed = text[(open + 1)..caret];
                int after = end;
                while (after < text.Length && text[after] is ' ' or '\t') after++;
                bool hasValue = after < text.Length && text[after] == ':'; // the key already has its value: only the key is taken out
                repaired = text[..open] + (hasValue ? "\"\"" : "\"\":null") + text[end..];
                at = open;
            }
            else
            {
                start = caret;
                while (start > 0 && IsWordChar(text[start - 1])) start--;
                end = caret;
                typed = text[start..caret];
                repaired = typed.Length == 0 ? text : text[..start] + text[caret..];
                at = start;
            }
        }
        if (!SchemaCatalog.TryParse(repaired, dialect, options.MaxDepth, out var data, out _)) return null;
        var keys = SchemaCatalog.CompleteKeys(schemaRoot, data!, at, options)
            .Where(k => k.StartsWith(typed, StringComparison.OrdinalIgnoreCase) && !string.Equals(k, typed, StringComparison.Ordinal))
            .ToList();
        int next = end;
        while (next < text.Length && text[next] is ' ' or '\t') next++;
        return keys.Count == 0 ? null : new KeyOffer(keys, start, end, dialect, next < text.Length && text[next] == ':');
    }
}
