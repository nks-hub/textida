using System.Text.Json;

namespace NksHub.Textida.Core.Agents.Transcripts;

/// <summary>
/// Reads the lines of a Codex session file (<c>~/.codex/sessions</c>, JSONL). The format has no prose documentation and is internal to Codex, so only what its open-source protocol definitions state is
/// read: a line is <c>{"timestamp", "type", "payload"}</c> and, for <c>type</c> <c>response_item</c>, the payload is an item of the public Responses API: <c>message</c> (<c>role</c> and blocks
/// <c>input_text</c> or <c>output_text</c> with <c>text</c>), <c>reasoning</c> (<c>summary</c> blocks <c>summary_text</c>, <c>content</c> blocks <c>reasoning_text</c> or <c>text</c>), <c>function_call</c>
/// (<c>name</c>, <c>arguments</c> as a string of JSON, <c>call_id</c>), <c>local_shell_call</c> (<c>action.command</c>, <c>call_id</c>), <c>custom_tool_call</c> (<c>name</c>, <c>input</c>, <c>call_id</c>) and
/// the outputs of calls (<c>call_id</c>, <c>output</c> as a string or a list of <c>input_text</c> blocks). Every other line (events, session data, turn contexts) and every other item or block is
/// shown as the raw JSON it is. A turn starts at a user message, so the instructions Codex adds as user messages are turns of their own.
/// </summary>
internal static class CodexTranscript
{
    private const string ResponseItem = "response_item";
    private const int MaxStrings = 256;

    private static readonly string[] PatchMarkers = ["*** Add File: ", "*** Update File: ", "*** Delete File: ", "*** Move to: "];

    private static string? Str(JsonElement e, string name) => TranscriptParser.Str(e, name);

    private static bool IsResponseItem(JsonElement root, out JsonElement payload)
    {
        payload = default;
        return root.ValueKind == JsonValueKind.Object && Str(root, "type") == ResponseItem
            && root.TryGetProperty("payload", out payload) && payload.ValueKind == JsonValueKind.Object;
    }

    // ---- what kind of record a line is ----

    /// <summary>The kind of a parsed line: <see cref="TranscriptKind.Other"/> for everything that is not an item this reader knows.</summary>
    public static TranscriptKind Classify(JsonElement root, int previewChars, out string preview)
    {
        preview = "";
        if (!IsResponseItem(root, out var payload)) return TranscriptKind.Other;
        string? type = Str(payload, "type"), role = Str(payload, "role");
        var kind = KindOf(type, role);
        if (kind == TranscriptKind.UserPrompt && payload.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            foreach (var block in content.EnumerateArray())
                if (Str(block, "type") == "input_text" && Str(block, "text") is { } text) { preview = TranscriptParser.Preview(text, previewChars); break; }
        return kind;
    }

    private static TranscriptKind KindOf(string? type, string? role) => type switch
    {
        "message" => role switch { "user" => TranscriptKind.UserPrompt, "assistant" => TranscriptKind.Assistant, _ => TranscriptKind.Other },
        "reasoning" or "function_call" or "custom_tool_call" or "local_shell_call" => TranscriptKind.Assistant,
        "function_call_output" or "custom_tool_call_output" => TranscriptKind.ToolResult,
        _ => TranscriptKind.Other,
    };

    /// <summary>
    /// The kind of an item whose line is too long to parse, read from the start of its payload: the tag <c>type</c> is its first property, <c>role</c> of a message follows after a few optional ones.
    /// What the start does not settle counts as <see cref="TranscriptKind.Other"/>. The reader stands on the start of the payload object.
    /// </summary>
    public static TranscriptKind PayloadPrefix(ref Utf8JsonReader reader)
    {
        string? type = null, role = null;
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName) break;
            bool isType = reader.ValueTextEquals("type"u8), isRole = reader.ValueTextEquals("role"u8);
            if (!reader.Read()) break;
            if (reader.TokenType == JsonTokenType.String)
            {
                if (isType) type = reader.GetString();
                else if (isRole) role = reader.GetString();
            }
            if (type is not null && (type != "message" || role is not null)) break;
            if (!reader.TrySkip()) break;
        }
        return KindOf(type, role);
    }

    // ---- the rows of a line ----

    /// <summary>Reads one parsed line of a kind this reader knows (<see cref="Classify"/> said so): rows into <paramref name="into"/> when given, and what a secrets file shows into the masker.</summary>
    public static void Read(long line, JsonElement root, TranscriptContext ctx, TurnContent? into, Func<string> rawText)
    {
        if (!IsResponseItem(root, out var payload))
        {
            TranscriptParser.Raw(line, rawText, ctx, into);
            return;
        }
        switch (Str(payload, "type"))
        {
            case "message":
                if (into is not null) MessageRows(line, payload, ctx, into);
                break;
            case "reasoning":
                if (into is not null) ReasoningRows(line, payload, ctx, into);
                break;
            case "function_call":
            {
                string arguments = Str(payload, "arguments") ?? "";
                var values = new List<string>();
                if (!TryCollectJsonStrings(arguments, ctx.Limits.MaxDepth, values)) values.Add(arguments);
                Call(line, Str(payload, "name") ?? "", Str(payload, "call_id"), arguments, values, ctx, into);
                break;
            }
            case "custom_tool_call":
            {
                string input = Str(payload, "input") ?? "";
                Call(line, Str(payload, "name") ?? "", Str(payload, "call_id"), input, [input], ctx, into);
                break;
            }
            case "local_shell_call":
            {
                var command = new List<string>();
                if (payload.TryGetProperty("action", out var action) && action.ValueKind == JsonValueKind.Object && action.TryGetProperty("command", out var words) && words.ValueKind == JsonValueKind.Array)
                    foreach (var word in words.EnumerateArray())
                        if (word.ValueKind == JsonValueKind.String) command.Add(word.GetString()!);
                Call(line, "local_shell", Str(payload, "call_id"), string.Join(' ', command), command, ctx, into);
                break;
            }
            case "function_call_output" or "custom_tool_call_output":
                Output(line, payload, ctx, into);
                break;
            default:
                TranscriptParser.Raw(line, rawText, ctx, into);
                break;
        }
    }

    private static void MessageRows(long line, JsonElement payload, TranscriptContext ctx, TurnContent into)
    {
        var kind = Str(payload, "role") == "assistant" ? TranscriptRowKind.Assistant : TranscriptRowKind.User;
        if (!payload.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return;
        foreach (var block in content.EnumerateArray())
        {
            if (Str(block, "type") is "input_text" or "output_text" && Str(block, "text") is { } text) TranscriptParser.Add(into, line, kind, ctx.Masker.Mask(text), ctx);
            else TranscriptParser.Add(into, line, TranscriptRowKind.Raw, ctx.Masker.Mask(block.GetRawText()), ctx);
        }
    }

    private static void ReasoningRows(long line, JsonElement payload, TranscriptContext ctx, TurnContent into)
    {
        foreach (var (name, types) in new[] { ("summary", new[] { "summary_text" }), ("content", new[] { "reasoning_text", "text" }) })
        {
            if (!payload.TryGetProperty(name, out var blocks) || blocks.ValueKind != JsonValueKind.Array) continue;
            foreach (var block in blocks.EnumerateArray())
            {
                if (Str(block, "type") is { } type && types.Contains(type) && Str(block, "text") is { } text) TranscriptParser.Add(into, line, TranscriptRowKind.Thinking, ctx.Masker.Mask(text), ctx);
                else TranscriptParser.Add(into, line, TranscriptRowKind.Raw, ctx.Masker.Mask(block.GetRawText()), ctx);
            }
        }
    }

    /// <summary>
    /// A call of a tool. When any text of it names a secrets file, the call is remembered so that the output which answers it is masked by position, and the texts of the call are learned
    /// (a patch holds the new content of the file after a diff mark). Without rows to fill only this is done.
    /// </summary>
    private static void Call(long line, string tool, string? callId, string display, List<string> values, TranscriptContext ctx, TurnContent? into)
    {
        if (values.Any(TranscriptMasker.TargetsSecretFile))
        {
            foreach (var value in values) Learn(ctx.Masker, value);
            if (callId is not null)
            {
                if (ctx.Pending.Count >= ctx.Limits.MaxPendingTools) ctx.Pending.Clear(); // shortcut: the oldest waiting calls are forgotten together; upgrade to an ordered map when results come back late
                ctx.Pending[callId] = true;
            }
        }
        if (into is null) return;
        TranscriptParser.Add(into, line, TranscriptRowKind.ToolCall, ctx.Masker.Mask(display), ctx, null, tool);
        foreach (var value in values)
            foreach (var path in PatchedFiles(value))
                into.Files.Add(new TouchedFile(path, tool, line));
    }

    private static void Output(long line, JsonElement payload, TranscriptContext ctx, TurnContent? into)
    {
        bool secret = Str(payload, "call_id") is { } id && ctx.Pending.Remove(id, out bool isSecret) && isSecret;
        if (!secret && into is null) return;
        string text = payload.TryGetProperty("output", out var output) ? OutputText(output) : "";
        if (secret)
        {
            var inner = new List<string>();
            if (TryCollectJsonStrings(text, ctx.Limits.MaxDepth, inner)) foreach (var value in inner) Learn(ctx.Masker, value);
            text = ctx.Masker.Mask(ctx.Masker.MaskSecretFile(text));
        }
        else text = ctx.Masker.Mask(text);
        TranscriptParser.Add(into, line, TranscriptRowKind.ToolResult, text, ctx);
    }

    private static string OutputText(JsonElement output)
    {
        if (output.ValueKind == JsonValueKind.String) return output.GetString()!;
        if (output.ValueKind != JsonValueKind.Array) return output.ValueKind == JsonValueKind.Undefined ? "" : output.GetRawText();
        var parts = new List<string>();
        foreach (var block in output.EnumerateArray())
            parts.Add(Str(block, "type") == "input_text" && Str(block, "text") is { } text ? text : block.ValueKind == JsonValueKind.Object ? block.GetRawText() : "");
        return string.Join("\n", parts);
    }

    // ---- secrets ----

    /// <summary>Learns the values of a text that may be the content of a secrets file; a patch shows the lines of the file after a diff mark, which is taken off for a second look.</summary>
    private static void Learn(TranscriptMasker masker, string text)
    {
        masker.MaskSecretFile(text);
        if (text.Contains('\n')) masker.MaskSecretFile(string.Join("\n", text.ReplaceLineEndings("\n").Split('\n').Select(l => (l.Length > 0 && l[0] is ('+' or '-' or ' ')) ? l[1..] : l)));
    }

    /// <summary>
    /// Every text value of the JSON in <paramref name="json"/>, whatever its name. False when the text is not a JSON object or list.
    /// </summary>
    private static bool TryCollectJsonStrings(string json, int maxDepth, List<string> into)
    {
        int start = 0;
        while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
        if (start == json.Length || json[start] is not ('{' or '[')) return false;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = maxDepth });
            Collect(doc.RootElement, into);
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static void Collect(JsonElement e, List<string> into)
    {
        if (into.Count >= MaxStrings) return; // shortcut: the strings after the first ones are not looked at; upgrade when a call holds more than a few hundred texts
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                into.Add(e.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray()) Collect(item, into);
                break;
            case JsonValueKind.Object:
                foreach (var property in e.EnumerateObject()) Collect(property.Value, into);
                break;
        }
    }

    /// <summary>The files a patch (the text of <c>apply_patch</c>) adds, updates, deletes or moves to, from its marker lines.</summary>
    private static IEnumerable<string> PatchedFiles(string text)
    {
        if (!text.Contains("*** ", StringComparison.Ordinal)) yield break;
        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
            foreach (var marker in PatchMarkers)
                if (raw.StartsWith(marker, StringComparison.Ordinal) && raw[marker.Length..].Trim() is { Length: > 0 } path) yield return path;
    }
}
