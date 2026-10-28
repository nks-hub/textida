using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NksHub.Textida.Plugins.Json;

[JsonSerializable(typeof(string))]
internal sealed partial class PluginJsonStrings : JsonSerializerContext;

/// <summary>How a text is read and written.</summary>
/// <param name="NewLine">Line ending of the lines the formatter breaks.</param>
/// <param name="Jsonc">Comments and trailing commas are part of the language (a JSONC file); otherwise they are syntax errors.</param>
/// <param name="MaxDepth">Deepest nesting accepted; there is no default, the editor decides. The formatter works on a stack, so it never goes deeper than <see cref="JsonFormatter.StackSafeDepth"/> whatever is asked.</param>
public readonly record struct JsonStyle(string NewLine, int Indent, bool Jsonc, int MaxDepth);

/// <summary>What kind of mistake a text has, so the plugin can say it in the language of the editor.</summary>
public enum JsonErrorKind { Syntax, Comment, TrailingComma, Depth, SelfCheck }

/// <summary>
/// Formatting of JSON and JSONC text. Tokens are copied as written (strings keep their escapes, numbers their digits, comments their text); only the white space between them
/// changes, apart from the commands that say otherwise. Every result is read back before it is returned: it must be valid in the same language and hold the same tokens, so a
/// bug here cannot damage a file.
/// </summary>
public static class JsonFormatter
{
    public const int StackSafeDepth = 512;

    /// <param name="Line">1-based line of the error inside the text that was read.</param>
    /// <param name="Column">1-based column in characters.</param>
    public readonly record struct SyntaxError(string Detail, long Line, long Column, JsonErrorKind Kind = JsonErrorKind.Syntax);

    /// <summary>
    /// Pretty prints <paramref name="text"/>; white space before and after the value stays as it was. Comments of a JSONC text stay with the member they belong to, and a trailing comma
    /// stays. False with <paramref name="error"/> when the text is not one valid value.
    /// </summary>
    public static bool TryFormat(string text, JsonStyle style, out string formatted, out SyntaxError error) =>
        Transform(text, style, Operation.Format, out formatted, out error);

    /// <summary>Writes the value on one line with no white space. Comments and trailing commas do not survive, so the result is valid JSON.</summary>
    public static bool TryMinify(string text, JsonStyle style, out string minified, out SyntaxError error) =>
        Transform(text, style, Operation.Minify, out minified, out error);

    /// <summary>Orders the members of every object by name (ordinal, equal names stay in their order) and lays the text out as <see cref="TryFormat"/> does. A comment moves with its member.</summary>
    public static bool TrySortKeys(string text, JsonStyle style, out string sorted, out SyntaxError error) =>
        Transform(text, style, Operation.SortKeys, out sorted, out error);

    /// <summary>
    /// Removes the commas in front of a closing bracket and changes nothing else, not even the white space. A text with comments is read as JSONC, so this also repairs
    /// the file of a strict JSON that a trailing comma made invalid.
    /// </summary>
    public static bool TryFixTrailingCommas(string text, int maxDepth, out string fixedText, out SyntaxError error)
    {
        var style = new JsonStyle("\n", 0, Jsonc: true, maxDepth);
        fixedText = text;
        if (!Validate(text, style, out error)) return false;
        var tokens = JsonTokens.Scan(text);
        var cuts = new List<int>();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != TokenKind.Comma) continue;
            int next = i + 1;
            while (tokens[next].IsComment) next++;
            if (tokens[next].Kind == TokenKind.Close) cuts.Add(tokens[i].Start);
        }
        var sb = new StringBuilder(text.Length);
        int from = 0;
        foreach (var cut in cuts)
        {
            sb.Append(text, from, cut - from);
            from = cut + 1;
        }
        sb.Append(text, from, text.Length - from);
        return Accept(text, sb.ToString(), style, sorted: false, comments: true, out fixedText, out error);
    }

    private enum Operation { Format, Minify, SortKeys }

    private static bool Transform(string text, JsonStyle style, Operation operation, out string result, out SyntaxError error)
    {
        result = text;
        if (!Validate(text, style, out error)) return false;
        string made;
        try
        {
            var tokens = JsonTokens.Scan(text);
            var tree = JsonTokens.Parse(text, tokens);
            if (operation == Operation.SortKeys) Sort(tree.Root);
            var printer = new Printer(style);
            var body = operation == Operation.Minify ? printer.Compact(tree) : printer.Layout(tree);
            int lead = tokens[0].Start, trail = text.Length - tokens[^1].End;
            made = string.Concat(text.AsSpan(0, lead), body, text.AsSpan(text.Length - trail));
        }
        catch (ArgumentOutOfRangeException)
        {
            error = new SyntaxError("", 0, 0, JsonErrorKind.SelfCheck);
            return false;
        }
        // a minified text has neither comments nor trailing commas, so it is read as strict JSON whatever it came from
        var resultStyle = operation == Operation.Minify ? style with { Jsonc = false } : style;
        return Accept(text, made, resultStyle, sorted: operation == Operation.SortKeys, comments: operation != Operation.Minify, out result, out error);
    }

    /// <summary>The result is taken when it reads as valid text of the same language and holds the same tokens (the same set of them after sorting) as the text it came from.</summary>
    internal static bool Accept(string before, string after, JsonStyle style, bool sorted, bool comments, out string result, out SyntaxError error)
    {
        result = before;
        error = default;
        bool same = Validate(after, style, out _);
        if (same)
        {
            var (tokensBefore, commentsBefore) = Significant(before);
            var (tokensAfter, commentsAfter) = Significant(after);
            if (sorted)
            {
                tokensBefore.Sort(StringComparer.Ordinal);
                tokensAfter.Sort(StringComparer.Ordinal);
            }
            // a comment may move to the line of its member, so only the set of comments is compared
            commentsBefore.Sort(StringComparer.Ordinal);
            commentsAfter.Sort(StringComparer.Ordinal);
            same = tokensBefore.SequenceEqual(tokensAfter) && (!comments || commentsBefore.SequenceEqual(commentsAfter));
        }
        if (!same)
        {
            error = new SyntaxError("", 0, 0, JsonErrorKind.SelfCheck);
            return false;
        }
        result = after;
        return true;
    }

    /// <summary>The tokens of a text apart from commas (a trailing comma may go) and apart from comments, which come as a list of their own.</summary>
    private static (List<string> Tokens, List<string> Comments) Significant(string text)
    {
        List<string> tokens = [], comments = [];
        foreach (var t in JsonTokens.Scan(text))
        {
            if (t.IsComment) comments.Add(text[t.Start..t.End]);
            else if (t.Kind != TokenKind.Comma) tokens.Add(text[t.Start..t.End]);
        }
        return (tokens, comments);
    }

    private static bool Validate(string text, JsonStyle style, out SyntaxError error)
    {
        error = default;
        var bytes = Encoding.UTF8.GetBytes(text);
        int depth = Math.Clamp(style.MaxDepth, 1, StackSafeDepth);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            CommentHandling = style.Jsonc ? JsonCommentHandling.Skip : JsonCommentHandling.Disallow,
            AllowTrailingCommas = style.Jsonc,
            MaxDepth = depth,
        });
        try
        {
            while (reader.Read()) { }
            return true;
        }
        catch (JsonException e)
        {
            // the reader counts the depth of a start token one lower than the containers that are open
            int open = reader.CurrentDepth + (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray ? 1 : 0);
            error = Locate(e, bytes, style.Jsonc, depth, open);
            return false;
        }
    }

    private static SyntaxError Locate(JsonException e, byte[] bytes, bool jsonc, int maxDepth, int currentDepth)
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
        // the reader ends a line at a line feed only; the editor ends it at a lone carriage return as well, so the place is counted again here
        int at = start + upTo, lineStart = 0;
        line = 0;
        for (int i = 0; i < at; i++)
        {
            if (bytes[i] != '\n' && !(bytes[i] == '\r' && (i + 1 == bytes.Length || bytes[i + 1] != '\n'))) continue;
            line++;
            lineStart = i + 1;
        }
        long column = Encoding.UTF8.GetCharCount(bytes, lineStart, at - lineStart) + 1;
        var message = e.Message;
        // the framework appends "Path: ... | LineNumber: ... | BytePositionInLine: ..."; the place is reported on its own
        foreach (var marker in new[] { " Path:", " LineNumber:" })
        {
            int cut = message.IndexOf(marker, StringComparison.Ordinal);
            if (cut > 0) message = message[..cut];
        }
        return new SyntaxError(message, line + 1, column, KindAt(bytes, start + upTo, jsonc, maxDepth, currentDepth));
    }

    private static JsonErrorKind KindAt(byte[] bytes, int at, bool jsonc, int maxDepth, int currentDepth)
    {
        if (at >= bytes.Length) return JsonErrorKind.Syntax;
        byte b = bytes[at];
        if ((b == '{' || b == '[') && currentDepth >= maxDepth) return JsonErrorKind.Depth;
        if (jsonc) return JsonErrorKind.Syntax;
        if (b == '/' && at + 1 < bytes.Length && (bytes[at + 1] == '/' || bytes[at + 1] == '*')) return JsonErrorKind.Comment;
        if (b == '}' || b == ']')
        {
            int before = at - 1;
            while (before >= 0 && bytes[before] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') before--;
            if (before >= 0 && bytes[before] == ',') return JsonErrorKind.TrailingComma;
        }
        return JsonErrorKind.Syntax;
    }

    private static void Sort(JsonValueNode node)
    {
        if (!node.IsContainer) return;
        foreach (var entry in node.Entries) Sort(entry.Value);
        if (node.Open == '{') node.Entries = [.. node.Entries.OrderBy(e => NameOf(e.Key!), StringComparer.Ordinal)];
    }

    private static string NameOf(string quoted) => JsonSerializer.Deserialize(quoted, PluginJsonStrings.Default.String)!;

    private sealed class Printer(JsonStyle style)
    {
        private readonly StringBuilder _sb = new();

        private void NewLine(int depth) => _sb.Append(style.NewLine).Append(' ', style.Indent * depth);

        public string Layout(JsonDocumentNode document)
        {
            foreach (var comment in document.Before) _sb.Append(comment.Raw).Append(style.NewLine);
            Write(document.Root, 0);
            foreach (var comment in document.After) _sb.Append(comment.OwnLine ? style.NewLine : " ").Append(comment.Raw);
            return _sb.ToString();
        }

        public string Compact(JsonDocumentNode document)
        {
            WriteCompact(document.Root);
            return _sb.ToString();
        }

        private void Write(JsonValueNode node, int depth)
        {
            if (!node.IsContainer)
            {
                _sb.Append(node.Raw);
                return;
            }
            char close = node.Open == '{' ? '}' : ']';
            _sb.Append(node.Open);
            foreach (var comment in node.AfterOpen) _sb.Append(' ').Append(comment.Raw);
            if (node.Entries.Count == 0 && node.Dangling.Count == 0 && node.AfterOpen.Count == 0)
            {
                _sb.Append(close);
                return;
            }
            for (int n = 0; n < node.Entries.Count; n++)
            {
                var entry = node.Entries[n];
                foreach (var comment in entry.Leading)
                {
                    NewLine(depth + 1);
                    _sb.Append(comment.Raw);
                }
                NewLine(depth + 1);
                if (entry.Key is not null) _sb.Append(entry.Key).Append(": ");
                Write(entry.Value, depth + 1);
                if (n < node.Entries.Count - 1 || node.TrailingComma) _sb.Append(',');
                foreach (var comment in entry.Trailing) _sb.Append(' ').Append(comment.Raw);
            }
            foreach (var comment in node.Dangling)
            {
                NewLine(depth + 1);
                _sb.Append(comment.Raw);
            }
            NewLine(depth);
            _sb.Append(close);
        }

        private void WriteCompact(JsonValueNode node)
        {
            if (!node.IsContainer)
            {
                _sb.Append(node.Raw);
                return;
            }
            _sb.Append(node.Open);
            for (int n = 0; n < node.Entries.Count; n++)
            {
                if (n > 0) _sb.Append(',');
                var entry = node.Entries[n];
                if (entry.Key is not null) _sb.Append(entry.Key).Append(':');
                WriteCompact(entry.Value);
            }
            _sb.Append(node.Open == '{' ? '}' : ']');
        }
    }
}
