using System.Text;
using System.Text.Json;

namespace NksHub.Textida.Plugins.Json;

/// <summary>Pretty printer for JSON text. Tokens are copied as written (strings keep their escapes, numbers their digits); only the white space between them is replaced.</summary>
public static class JsonFormatter
{
    /// <param name="Line">1-based line of the error inside the text that was formatted.</param>
    /// <param name="Column">1-based column in characters.</param>
    public readonly record struct SyntaxError(string Detail, long Line, long Column);

    /// <summary>
    /// Formats <paramref name="text"/>; white space before and after the value stays as it was.
    /// False with <paramref name="error"/> when the text is not one valid JSON value (comments and trailing commas are not accepted).
    /// </summary>
    public static bool TryFormat(string text, string newLine, int indent, out string formatted, out SyntaxError error)
    {
        formatted = "";
        error = default;
        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
        var sb = new StringBuilder(text.Length + text.Length / 4);
        var pad = new string(' ', indent);
        int depth = 0;
        bool afterName = false, justOpened = false;

        void Break()
        {
            sb.Append(newLine);
            for (int i = 0; i < depth; i++) sb.Append(pad);
        }

        void Entry()
        {
            if (afterName) { afterName = false; return; }
            if (depth == 0) return;
            if (justOpened) justOpened = false;
            else sb.Append(',');
            Break();
        }

        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        Entry();
                        sb.Append(reader.TokenType == JsonTokenType.StartObject ? '{' : '[');
                        depth++;
                        justOpened = true;
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        depth--;
                        if (!justOpened) Break();
                        justOpened = false;
                        sb.Append(reader.TokenType == JsonTokenType.EndObject ? '}' : ']');
                        break;
                    case JsonTokenType.PropertyName:
                        Entry();
                        sb.Append('"').Append(Encoding.UTF8.GetString(reader.ValueSpan)).Append("\": ");
                        afterName = true;
                        break;
                    case JsonTokenType.String:
                        Entry();
                        sb.Append('"').Append(Encoding.UTF8.GetString(reader.ValueSpan)).Append('"');
                        break;
                    default:
                        Entry();
                        sb.Append(Encoding.UTF8.GetString(reader.ValueSpan));
                        break;
                }
            }
        }
        catch (JsonException e)
        {
            error = Locate(e, bytes);
            return false;
        }
        int lead = 0, trail = 0;
        while (lead < text.Length && char.IsWhiteSpace(text[lead])) lead++;
        while (trail < text.Length - lead && char.IsWhiteSpace(text[text.Length - 1 - trail])) trail++;
        formatted = string.Concat(text.AsSpan(0, lead), sb.ToString(), text.AsSpan(text.Length - trail));
        return true;
    }

    private static SyntaxError Locate(JsonException e, byte[] bytes)
    {
        long line = e.LineNumber ?? 0, byteColumn = e.BytePositionInLine ?? 0;
        int start = 0;
        for (long l = 0; l < line; l++)
        {
            int next = Array.IndexOf(bytes, (byte)'\n', start);
            if (next < 0) break;
            start = next + 1;
        }
        int upTo = (int)Math.Min(bytes.Length - start, byteColumn);
        long column = Encoding.UTF8.GetCharCount(bytes, start, upTo) + 1;
        var message = e.Message;
        int cut = message.IndexOf(" Path:", StringComparison.Ordinal);
        return new SyntaxError(cut > 0 ? message[..cut] : message, line + 1, column);
    }
}
