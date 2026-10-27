namespace NksHub.Textida.Plugins.Json.Schema;

/// <summary>
/// The keys the schema offers at a caret and the characters of the text they replace. <see cref="Start"/> and <see cref="End"/> are offsets in the text the caret was given in;
/// <see cref="Insert"/> turns a key into the text that replaces them.
/// </summary>
public sealed record KeyOffer(IReadOnlyList<string> Keys, int Start, int End, DataDialect Dialect, bool ColonFollows)
{
    public string Insert(string key) => Dialect == DataDialect.Toml
        ? (IsBare(key) ? key : "\"" + key.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"") + " = "
        : "\"" + key.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" + (ColonFollows ? "" : ": ");

    private static bool IsBare(string key) => key.Length > 0 && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}

/// <summary>
/// Key completion from a schema. A file is rarely valid while a key is being typed, so the part being typed is taken out before the file is read: in JSON the string
/// around the caret becomes an empty key, in TOML the word in front of the caret goes. The offer then names the keys the schema allows in the object around the caret that are not there yet.
/// </summary>
public static class KeyCompletion
{
    private static bool IsWordChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-';

    /// <summary>The offer at <paramref name="caret"/>, or null when the caret is not where a key can be written or the file cannot be read.</summary>
    public static KeyOffer? Offer(SchemaEntry schema, DataDialect dialect, string text, int caret, SchemaOptions options)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        if (!SchemaCatalog.TryParse(SchemaCatalog.LoadSchemaText(schema), DataDialect.Json, options.MaxDepth, out var schemaRoot, out _)) return null;
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
        var keys = SchemaCatalog.CompleteKeys(schemaRoot!, data!, at, options)
            .Where(k => k.StartsWith(typed, StringComparison.OrdinalIgnoreCase) && !string.Equals(k, typed, StringComparison.Ordinal))
            .ToList();
        int next = end;
        while (next < text.Length && text[next] is ' ' or '\t') next++;
        return keys.Count == 0 ? null : new KeyOffer(keys, start, end, dialect, next < text.Length && text[next] == ':');
    }
}
