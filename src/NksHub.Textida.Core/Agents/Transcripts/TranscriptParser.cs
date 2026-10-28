using System.Text;
using System.Text.Json;

namespace NksHub.Textida.Core.Agents.Transcripts;

/// <summary>What reading a transcript carries from line to line: the limits, the masker and the tool calls that wait for their result.</summary>
public sealed class TranscriptContext(TranscriptLimits limits, TranscriptMasker masker)
{
    public TranscriptLimits Limits { get; } = limits;
    public TranscriptMasker Masker { get; } = masker;

    /// <summary>Tool calls that read a secrets file, by id: the result that answers one is the content of that file.</summary>
    internal Dictionary<string, bool> Pending { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Reads the lines of a Claude Code session transcript (JSONL). The public documentation says that the entry format is internal to Claude Code and changes between versions, so only what the
/// public Messages API and Agent SDK documentation define is read: <c>message.role</c>, <c>message.content</c> (a string, or blocks <c>text</c>, <c>thinking</c>, <c>tool_use</c> with <c>id</c>, <c>name</c>
/// and <c>input</c>, <c>tool_result</c> with <c>tool_use_id</c>, <c>content</c> and <c>is_error</c>) and the documented inputs of the tools Edit, Write, NotebookEdit, Read, Bash, Glob and Grep.
/// Every other record, and every block that is not one of these, is shown as the raw JSON it is.
/// </summary>
public static class TranscriptParser
{
    private static readonly HashSet<string> FileTools = ["Edit", "Write", "NotebookEdit", "Read"];

    internal static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    internal static string Cut(string text, int max)
    {
        if (text.Length <= max) return text;
        int end = max > 0 && char.IsHighSurrogate(text[max - 1]) ? max - 1 : max;
        return text[..end] + "…";
    }

    private static bool IsBlank(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes) if (b is not (0x20 or 0x09 or 0x0D or 0x0A)) return false;
        return true;
    }

    internal static string Preview(string text, int max)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return Cut(flat, max);
    }

    // ---- what kind of record a line is ----

    /// <summary>The kind of a parsed line. A user entry is a prompt, or the answer to tool calls when one of its blocks is a <c>tool_result</c>.</summary>
    public static TranscriptKind Classify(JsonElement root, int previewChars, out string preview)
    {
        preview = "";
        if (root.ValueKind != JsonValueKind.Object) return TranscriptKind.Invalid;
        if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object) return CodexTranscript.Classify(root, previewChars, out preview);
        switch (Str(message, "role"))
        {
            case "assistant":
                return TranscriptKind.Assistant;
            case "user":
                if (!message.TryGetProperty("content", out var content)) return TranscriptKind.Other;
                if (content.ValueKind == JsonValueKind.String)
                {
                    preview = Preview(content.GetString()!, previewChars);
                    return TranscriptKind.UserPrompt;
                }
                if (content.ValueKind != JsonValueKind.Array) return TranscriptKind.Other;
                foreach (var block in content.EnumerateArray())
                    if (Str(block, "type") == "tool_result") return TranscriptKind.ToolResult;
                foreach (var block in content.EnumerateArray())
                    if (Str(block, "type") == "text" && Str(block, "text") is { } text) { preview = Preview(text, previewChars); break; }
                return TranscriptKind.UserPrompt;
            default:
                return TranscriptKind.Other;
        }
    }

    /// <summary>
    /// The kind of a line that is too long to parse, from its first bytes. The role of the message decides (it comes early in the message); a user entry whose content is a string is a prompt, one whose
    /// first block is a <c>tool_result</c> is an answer to tool calls. What the start does not settle counts as <see cref="TranscriptKind.Other"/>, or as an answer to tool calls for a user entry.
    /// </summary>
    public static TranscriptKind ClassifyPrefix(ReadOnlySpan<byte> prefix, int previewChars, out string preview)
    {
        preview = "";
        try
        {
            var reader = new Utf8JsonReader(prefix, isFinalBlock: false, default);
            if (!reader.Read()) return TranscriptKind.Other;
            if (reader.TokenType != JsonTokenType.StartObject) return TranscriptKind.Invalid;
            bool responseItem = false;
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName) return TranscriptKind.Other;
                bool message = reader.ValueTextEquals("message"u8), type = reader.ValueTextEquals("type"u8), payload = reader.ValueTextEquals("payload"u8);
                if (!reader.Read()) return TranscriptKind.Other;
                if (message && reader.TokenType == JsonTokenType.StartObject) return MessagePrefix(ref reader, prefix, previewChars, out preview);
                if (type && reader.TokenType == JsonTokenType.String) responseItem = reader.ValueTextEquals("response_item"u8);
                if (payload && responseItem && reader.TokenType == JsonTokenType.StartObject) return CodexTranscript.PayloadPrefix(ref reader);
                if (!reader.TrySkip()) return TranscriptKind.Other;
            }
        }
        catch (JsonException) { return TranscriptKind.Invalid; }
        return TranscriptKind.Other;
    }

    private static TranscriptKind MessagePrefix(ref Utf8JsonReader reader, ReadOnlySpan<byte> prefix, int previewChars, out string preview)
    {
        preview = "";
        string? role = null;
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName) return TranscriptKind.Other;
            bool isRole = reader.ValueTextEquals("role"u8), isContent = reader.ValueTextEquals("content"u8);
            int after = (int)reader.BytesConsumed;
            if (!reader.Read())
            {
                // the value is cut off: a content that starts with a quote is a string, one that starts with a bracket is a list of blocks
                if (!isContent || role != "user") return role == "assistant" ? TranscriptKind.Assistant : TranscriptKind.Other;
                while (after < prefix.Length && prefix[after] is 0x20 or 0x09 or 0x0D or 0x0A or 0x3A) after++;
                return after < prefix.Length && prefix[after] == (byte)'"' ? TranscriptKind.UserPrompt : TranscriptKind.ToolResult;
            }
            if (isRole && reader.TokenType == JsonTokenType.String)
            {
                role = reader.GetString();
                if (role == "assistant") return TranscriptKind.Assistant;
                if (role != "user") return TranscriptKind.Other;
                continue;
            }
            if (isContent && role == "user")
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    preview = Preview(reader.GetString()!, previewChars);
                    return TranscriptKind.UserPrompt;
                }
                if (reader.TokenType != JsonTokenType.StartArray) return TranscriptKind.Other;
                return FirstBlockPrefix(ref reader);
            }
            if (!reader.TrySkip()) return role == "assistant" ? TranscriptKind.Assistant : TranscriptKind.Other;
        }
        return TranscriptKind.Other;
    }

    private static TranscriptKind FirstBlockPrefix(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return TranscriptKind.ToolResult;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool isType = reader.ValueTextEquals("type"u8);
            if (!reader.Read()) break;
            if (isType && reader.TokenType == JsonTokenType.String) return reader.ValueTextEquals("tool_result"u8) ? TranscriptKind.ToolResult : TranscriptKind.UserPrompt;
            if (!reader.TrySkip()) break;
        }
        return TranscriptKind.ToolResult;
    }

    // ---- the rows of a line ----

    /// <summary>
    /// Reads one line: learns the values of secrets files it shows and, when <paramref name="into"/> is given, adds the rows of the line to it. A line that cannot be parsed, a record of an
    /// unknown kind and an unknown block become one raw row each.
    /// </summary>
    public static void ReadLine(long line, ReadOnlyMemory<byte> bytes, TranscriptContext ctx, TurnContent? into)
    {
        if (IsBlank(bytes.Span)) return;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = ctx.Limits.MaxDepth }); }
        catch (JsonException)
        {
            Raw(line, () => Encoding.UTF8.GetString(bytes.Span), ctx, into);
            return;
        }
        using (doc)
            ReadParsed(line, doc.RootElement, Classify(doc.RootElement, ctx.Limits.PreviewChars, out _), ctx, into, () => Encoding.UTF8.GetString(bytes.Span));
    }

    /// <summary>The same for a line that is parsed already; <paramref name="kind"/> is what <see cref="Classify"/> said about <paramref name="root"/>.</summary>
    public static void ReadParsed(long line, JsonElement root, TranscriptKind kind, TranscriptContext ctx, TurnContent? into, Func<string> rawText)
    {
        if (kind is TranscriptKind.Other or TranscriptKind.Invalid)
        {
            Raw(line, rawText, ctx, into);
            return;
        }
        if (!root.TryGetProperty("message", out var message))
        {
            CodexTranscript.Read(line, root, ctx, into, rawText);
            return;
        }
        var content = message.TryGetProperty("content", out var c) ? c : default;
        if (kind == TranscriptKind.Assistant) AssistantContent(line, content, ctx, into);
        else UserContent(line, content, ctx, into);
    }

    internal static void Raw(long line, Func<string> text, TranscriptContext ctx, TurnContent? into)
    {
        if (into is null) return;
        into.Rows.Add(new TranscriptRow(line, TranscriptRowKind.Raw, Cut(ctx.Masker.Mask(text()), ctx.Limits.MaxRowChars)));
    }

    internal static void Add(TurnContent? into, long line, TranscriptRowKind kind, string text, TranscriptContext ctx, string? path = null, string? tool = null) =>
        into?.Rows.Add(new TranscriptRow(line, kind, Cut(text, ctx.Limits.MaxRowChars), path, tool));

    // Without rows to fill (into is null) only the calls and results that decide what a secrets file shows are read; the rest of a line costs nothing.

    private static void AssistantContent(long line, JsonElement content, TranscriptContext ctx, TurnContent? into)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            if (into is not null) Add(into, line, TranscriptRowKind.Assistant, ctx.Masker.Mask(content.GetString()!), ctx);
            return;
        }
        if (content.ValueKind != JsonValueKind.Array) return;
        foreach (var block in content.EnumerateArray())
        {
            string? type = Str(block, "type");
            if (type == "tool_use") ToolUse(line, block, ctx, into);
            else if (into is null) continue;
            else if (type == "text" && Str(block, "text") is { } text) Add(into, line, TranscriptRowKind.Assistant, ctx.Masker.Mask(text), ctx);
            else if (type == "thinking" && Str(block, "thinking") is { } thinking) Add(into, line, TranscriptRowKind.Thinking, ctx.Masker.Mask(thinking), ctx);
            else Add(into, line, TranscriptRowKind.Raw, ctx.Masker.Mask(block.GetRawText()), ctx);
        }
    }

    private static void UserContent(long line, JsonElement content, TranscriptContext ctx, TurnContent? into)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            if (into is not null) Add(into, line, TranscriptRowKind.User, ctx.Masker.Mask(content.GetString()!), ctx);
            return;
        }
        if (content.ValueKind != JsonValueKind.Array) return;
        foreach (var block in content.EnumerateArray())
        {
            string? type = Str(block, "type");
            if (type == "tool_result") ToolResult(line, block, ctx, into);
            else if (into is null) continue;
            else if (type == "text" && Str(block, "text") is { } text) Add(into, line, TranscriptRowKind.User, ctx.Masker.Mask(text), ctx);
            else Add(into, line, TranscriptRowKind.Raw, ctx.Masker.Mask(block.GetRawText()), ctx);
        }
    }

    private static string ResultText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String) return content.GetString()!;
        if (content.ValueKind != JsonValueKind.Array) return "";
        var parts = new List<string>();
        foreach (var block in content.EnumerateArray())
            parts.Add(Str(block, "type") == "text" && Str(block, "text") is { } text ? text : block.ValueKind == JsonValueKind.Object ? block.GetRawText() : "");
        return string.Join("\n", parts);
    }

    private static void ToolResult(long line, JsonElement block, TranscriptContext ctx, TurnContent? into)
    {
        bool secret = Str(block, "tool_use_id") is { } id && ctx.Pending.Remove(id, out bool isSecret) && isSecret;
        if (!secret && into is null) return;
        string text = block.TryGetProperty("content", out var content) ? ResultText(content) : "";
        text = secret ? ctx.Masker.MaskSecretFile(text) : ctx.Masker.Mask(text);
        bool error = block.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;
        Add(into, line, error ? TranscriptRowKind.ToolError : TranscriptRowKind.ToolResult, text, ctx);
    }

    private static void ToolUse(long line, JsonElement block, TranscriptContext ctx, TurnContent? into)
    {
        string name = Str(block, "name") ?? "";
        string? id = Str(block, "id");
        block.TryGetProperty("input", out var input);
        string? path = Str(input, "file_path") ?? Str(input, "notebook_path") ?? Str(input, "path");
        string? command = Str(input, "command");
        bool secret = TranscriptMasker.TargetsSecretFile(path) || TranscriptMasker.TargetsSecretFile(command) || TranscriptMasker.TargetsSecretFile(Str(input, "pattern"));
        bool writes = name is "Edit" or "Write" or "NotebookEdit";
        if (secret && !writes && id is not null)
        {
            if (ctx.Pending.Count >= ctx.Limits.MaxPendingTools) ctx.Pending.Clear(); // shortcut: the oldest waiting calls are forgotten together; upgrade to an ordered map when results come back late
            ctx.Pending[id] = true;
        }
        string Text(string? s) => s is null || (into is null && !(secret && writes)) ? "" : secret && writes ? ctx.Masker.MaskSecretFile(s) : ctx.Masker.Mask(s);

        switch (name)
        {
            case "Edit":
                Add(into, line, TranscriptRowKind.ToolCall, path ?? "", ctx, path, name);
                string removed = Text(Str(input, "old_string")), added = Text(Str(input, "new_string"));
                if (into is not null) foreach (var row in DiffRows(line, removed, added, ctx)) into.Rows.Add(row);
                break;
            case "Write":
                Add(into, line, TranscriptRowKind.ToolCall, path ?? "", ctx, path, name);
                if (into is not null && Text(Str(input, "content")) is { Length: > 0 } written) into.Rows.Add(new TranscriptRow(line, TranscriptRowKind.DiffAdded, Cut(Prefixed("+", written), ctx.Limits.MaxRowChars)));
                break;
            case "NotebookEdit":
                Add(into, line, TranscriptRowKind.ToolCall, path ?? "", ctx, path, name);
                if (into is not null && Text(Str(input, "new_source")) is { Length: > 0 } source) into.Rows.Add(new TranscriptRow(line, TranscriptRowKind.DiffAdded, Cut(Prefixed("+", source), ctx.Limits.MaxRowChars)));
                break;
            case "Read":
                Add(into, line, TranscriptRowKind.ToolCall, path ?? "", ctx, path, name);
                break;
            case "Bash":
                Add(into, line, TranscriptRowKind.ToolCall, ctx.Masker.Mask(command ?? ""), ctx, null, name);
                break;
            default:
                if (into is not null) Add(into, line, TranscriptRowKind.ToolCall, input.ValueKind == JsonValueKind.Object ? ctx.Masker.Mask(input.GetRawText()) : "", ctx, null, name);
                break;
        }
        if (into is not null && path is not null && FileTools.Contains(name)) into.Files.Add(new TouchedFile(path, name, line));
    }

    private static string Prefixed(string prefix, string text) => string.Join("\n", text.ReplaceLineEndings("\n").Split('\n').Select(l => prefix + l));

    /// <summary>
    /// The change of an Edit call: the lines that differ, without the lines the old and the new text have in common at the start and at the end. The edit replaces the old text by the new one,
    /// so nothing else is needed to show it.
    /// </summary>
    internal static IEnumerable<TranscriptRow> DiffRows(long line, string removed, string added, TranscriptContext ctx)
    {
        var a = removed.Length == 0 ? [] : removed.ReplaceLineEndings("\n").Split('\n');
        var b = added.Length == 0 ? [] : added.ReplaceLineEndings("\n").Split('\n');
        int head = 0;
        while (head < a.Length && head < b.Length && a[head] == b[head]) head++;
        int tail = 0;
        while (tail < a.Length - head && tail < b.Length - head && a[^(tail + 1)] == b[^(tail + 1)]) tail++;
        var gone = a[head..(a.Length - tail)];
        var came = b[head..(b.Length - tail)];
        if (gone.Length > 0)
            yield return new TranscriptRow(line, TranscriptRowKind.DiffRemoved, Cut(Prefixed("-", string.Join("\n", gone)), ctx.Limits.MaxRowChars));
        if (came.Length > 0)
            yield return new TranscriptRow(line, TranscriptRowKind.DiffAdded, Cut(Prefixed("+", string.Join("\n", came)), ctx.Limits.MaxRowChars));
    }
}
