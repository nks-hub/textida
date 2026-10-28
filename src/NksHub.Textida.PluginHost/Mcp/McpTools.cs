using System.Text.Json;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Mcp;

/// <summary>The arguments do not fit the schema of the tool: a protocol error (-32602).</summary>
internal sealed class ToolArgumentException(string message) : Exception(message);

/// <summary>The call is well formed but cannot be done; the assistant reads why in the result (<c>isError</c>) and can correct itself.</summary>
internal sealed class ToolFailure(string message) : Exception(message);

internal sealed record McpToolDefinition(string Name, string Title, string Description, string InputSchema, bool ReadOnly, Func<McpToolContext, JsonElement, Task<string>> Run);

/// <summary>
/// What a tool needs while it runs: calls into the document API as the assistant, argument reading and the revision retry.
/// <paramref name="scope"/> narrows the documents the client may reach to those it answers true for (one window of the editor); a document outside it is as good as not shared.
/// </summary>
internal sealed class McpToolContext(McpServer server, CancellationToken cancellation, Func<Guid, bool>? scope = null)
{
    private const int RevisionAttempts = 3;

    public static JsonElement NoArguments { get; } = JsonDocument.Parse("{}").RootElement.Clone();

    public McpServerOptions Options => server.Options;
    public CancellationToken Cancellation => cancellation;

    /// <summary>Text blocks that follow the result of the tool in the same answer; a client that reads more than the first block (<c>openDiff</c> of the /ide bridge) gets them.</summary>
    public List<string> MoreText { get; } = [];

    // ---- arguments ----

    public static Guid Document(JsonElement args) =>
        args.TryGetProperty("document", out var d) && d.ValueKind == JsonValueKind.String && Guid.TryParse(d.GetString(), out var id)
            ? id : throw new ToolArgumentException("'document' must be a document id from editor_list_documents.");

    public static string Text(JsonElement args, string name, bool allowEmpty = false) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && (allowEmpty || v.GetString()!.Length > 0)
            ? v.GetString()! : throw new ToolArgumentException($"'{name}' must be {(allowEmpty ? "a string" : "a non-empty string")}.");

    public static string? OptionalText(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? (v.ValueKind == JsonValueKind.String ? v.GetString() : throw new ToolArgumentException($"'{name}' must be a string.")) : null;

    public static bool? OptionalBool(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
            ? (v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : throw new ToolArgumentException($"'{name}' must be true or false."))
            : null;

    public static long? OptionalNumber(JsonElement args, string name, long min = 0) =>
        args.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
            ? (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) && n >= min ? n : throw new ToolArgumentException($"'{name}' must be an integer of at least {min}."))
            : null;

    public static long Number(JsonElement args, string name, long min = 0) =>
        OptionalNumber(args, name, min) ?? throw new ToolArgumentException($"'{name}' is required.");

    public int Count(JsonElement args, string name) =>
        (int)Math.Min(OptionalNumber(args, name, 1) ?? Options.MaxItems, Options.MaxItems);

    // ---- the document API ----

    public async Task<JsonElement> CallAsync(string method, Action<Utf8JsonWriter> body)
    {
        using var request = JsonDocument.Parse(DocumentApi.Obj(body));
        if (scope is not null && request.RootElement.TryGetProperty("document", out var named) && named.ValueKind == JsonValueKind.String && Guid.TryParse(named.GetString(), out var id) && !scope(id))
            throw new RpcException(DocumentErrorCodes.PermissionDenied, "The document is not shared with this connection.");
        var answer = await Options.Documents.HandleAsync(McpServer.ClientId, method, request.RootElement, cancellation).ConfigureAwait(false);
        using var parsed = JsonDocument.Parse(answer!);
        return parsed.RootElement.Clone();
    }

    /// <summary>The documents shared with the assistant. Without any grant the API refuses the call; that is an empty list here, because nothing is shared.</summary>
    public async Task<JsonElement> ListSharedAsync()
    {
        JsonElement list;
        try { list = await CallAsync(DocumentMethods.List, _ => { }).ConfigureAwait(false); }
        catch (RpcException e) when (e.Code == DocumentErrorCodes.PermissionDenied) { return NoDocuments; }
        if (scope is null) return list;
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartArray();
            foreach (var d in list.EnumerateArray())
                if (scope(d.GetProperty("id").GetGuid())) d.WriteTo(w);
            w.WriteEndArray();
        }
        using var filtered = JsonDocument.Parse(buffer.WrittenMemory);
        return filtered.RootElement.Clone();
    }

    private static JsonElement NoDocuments { get; } = JsonDocument.Parse("[]").RootElement.Clone();

    public Task<JsonElement> CallAsync(string method, Guid document, Action<Utf8JsonWriter>? more = null) =>
        CallAsync(method, w => { w.WriteString("document", document); more?.Invoke(w); });

    /// <summary>
    /// Runs <paramref name="body"/> against the revision the arguments name, or against the current one. A call that named no revision and lost a race with a typing user
    /// is repeated against the new revision; one that named a revision gets the conflict, because its offsets are only meaningful there.
    /// </summary>
    public async Task<string> AtRevisionAsync(Guid document, JsonElement args, Func<long, Task<string>> body)
    {
        if (OptionalNumber(args, "revision") is { } named) return await body(named).ConfigureAwait(false);
        for (int attempt = 1; ; attempt++)
        {
            var info = await CallAsync(DocumentMethods.Info, document).ConfigureAwait(false);
            try { return await body(info.GetProperty("revision").GetInt64()).ConfigureAwait(false); }
            catch (RpcException e) when (e.Code == DocumentErrorCodes.RevisionConflict && attempt < RevisionAttempts) { }
        }
    }

    public string? TitleOf(Guid document, JsonElement info)
    {
        if (Options.TitleOf?.Invoke(document) is { Length: > 0 } title) return title;
        return info.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String ? Path.GetFileName(path.GetString()) : null;
    }

    public static string Describe(RpcException e)
    {
        var detail = e.Code switch
        {
            DocumentErrorCodes.PermissionDenied => "This document is not shared with the assistant, or the user withdrew the access.",
            DocumentErrorCodes.UnknownDocument => "There is no shared document with this id; editor_list_documents lists them.",
            DocumentErrorCodes.RevisionConflict => e.Message + " Read the document again and use the new revision.",
            _ => e.Message,
        };
        return e.DataJson is { } data ? $"{detail} [code {e.Code}, {data}]" : $"{detail} [code {e.Code}]";
    }
}

/// <summary>The tools. Each one is a thin translation to the document API, which owns permissions, hidden values and revisions.</summary>
internal static class McpTools
{
    private const string DocumentProperty = "\"document\":{\"type\":\"string\",\"description\":\"Document id from editor_list_documents.\"}";
    private const string RevisionProperty = "\"revision\":{\"type\":\"integer\",\"minimum\":0,\"description\":\"Revision the offsets refer to. Without it the current revision is used and reported back.\"}";

    private static string Schema(string properties, string required = "") =>
        $"{{\"type\":\"object\",\"properties\":{{{properties}}},{(required.Length > 0 ? $"\"required\":[{required}]," : "")}\"additionalProperties\":false}}";

    public static IReadOnlyList<McpToolDefinition> All { get; } =
    [
        new("editor_list_documents", "List shared documents",
            "Lists the tabs the user shared with you: id, title, path, current revision, size in bytes and encoding. Tabs that are not shared are not listed.",
            "{\"type\":\"object\",\"additionalProperties\":false}", true, ListDocuments),
        new("editor_read_range", "Read text",
            "Reads text of a shared document, also when it has unsaved changes. Give a byte range (start, end) or lines (startLine, endLine; 0-based, end exclusive). " +
            "Without an end it reads on as far as the size limit allows and says so in 'truncated'. The range grows to whole characters. Hidden .env values are returned as *.",
            Schema(DocumentProperty + ",\"start\":{\"type\":\"integer\",\"minimum\":0},\"end\":{\"type\":\"integer\",\"minimum\":0},\"startLine\":{\"type\":\"integer\",\"minimum\":0},\"endLine\":{\"type\":\"integer\",\"minimum\":0}," + RevisionProperty, "\"document\""),
            true, ReadRange),
        new("editor_search", "Search in a document",
            "Finds literal, case-sensitive text in one shared document and returns each match with its byte offsets, its line and a short snippet around it. The text itself is not returned beyond the snippets.",
            Schema(DocumentProperty + ",\"text\":{\"type\":\"string\",\"minLength\":1},\"maxResults\":{\"type\":\"integer\",\"minimum\":1}", "\"document\",\"text\""),
            true, Search),
        new("editor_get_selection", "Cursor and selection",
            "Tells where the user's cursor and selection are in a shared document: for each selection its byte range (anchor and caret), the line it starts on and its text, which is cut at the size limit. " +
            "An empty text is a cursor without a selection; 'primary' is the index of the main one. The offsets are valid for the revision reported. Hidden .env values are returned as *.",
            Schema(DocumentProperty, "\"document\""), true, GetSelection),
        new("editor_prepare_changes", "Propose text changes",
            "Proposes changes to a shared document; nothing is written. The user sees the proposal as a diff and applies or rejects it. 'revision' is required: the proposal is refused if the document has moved on, so read it again then. " +
            "Each edit replaces a range given as start and end (byte offsets of that revision) or the one place where 'find' occurs, with 'text' (empty text deletes; start equal to end inserts). Returns a proposal id for editor_get_change_status.",
            Schema(DocumentProperty + ",\"revision\":{\"type\":\"integer\",\"minimum\":0},\"title\":{\"type\":\"string\"},\"edits\":{\"type\":\"array\",\"minItems\":1,\"items\":{\"type\":\"object\",\"properties\":{" +
                   "\"start\":{\"type\":\"integer\",\"minimum\":0},\"end\":{\"type\":\"integer\",\"minimum\":0},\"find\":{\"type\":\"string\",\"minLength\":1},\"text\":{\"type\":\"string\"}},\"required\":[\"text\"],\"additionalProperties\":false}}",
                "\"document\",\"revision\",\"edits\""),
            false, PrepareChanges),
        new("editor_get_change_status", "Status of a proposal",
            "Tells what became of a proposal: pending (the user has not decided), applied, rejected, stale (the text it touched changed first) or failed.",
            Schema("\"proposal\":{\"type\":\"string\"}", "\"proposal\""), true, ChangeStatus),
        new("editor_outline", "Outline of a document",
            "Lists the symbols (headings, functions, keys) of a shared document, also with unsaved changes: name, kind, parent and byte range. 'current' is false when the document changed after the outline was made.",
            Schema(DocumentProperty, "\"document\""), true, Outline),
        new("editor_find_in_files", "Search in all shared documents",
            "Finds literal, case-sensitive text in every document the user shared, in the text as it is in the editor now. It does not read files that are not shared.",
            Schema("\"text\":{\"type\":\"string\",\"minLength\":1},\"maxResults\":{\"type\":\"integer\",\"minimum\":1}", "\"text\""), true, FindInFiles),
        new("editor_file_facts", "Facts about a document",
            "Encoding, byte order mark, line endings (counts of LF, CRLF and CR), invalid byte sequences, size, storage mode and whether the line index is complete. Counts cover the part scanned; 'complete' says whether that was all.",
            Schema(DocumentProperty, "\"document\""), true, FileFacts),
        new("editor_log_query", "Find log lines by level",
            "Returns lines of a level (error, warning, info, debug) from a log of any size without loading it. A call scans a limited amount; 'next' is the offset to pass as 'start' to continue, null at the end.",
            Schema(DocumentProperty + ",\"level\":{\"type\":\"string\",\"enum\":[\"error\",\"warning\",\"info\",\"debug\"]},\"start\":{\"type\":\"integer\",\"minimum\":0},\"maxLines\":{\"type\":\"integer\",\"minimum\":1}", "\"document\",\"level\""),
            true, LogQuery),
        new("editor_diagnostics", "Errors and warnings of a document",
            "Lists the errors, warnings and hints that plugins and the editor's linter found in a shared document: source, severity, message, code, byte range and line. A diagnostic that touches a hidden .env value is not returned; 'withheld' counts them.",
            Schema(DocumentProperty, "\"document\""), true, Diagnostics),
        new("editor_read_bytes", "Read bytes",
            $"Reads at most {McpServer.MaxReadBytes} bytes of a shared document from an offset, as hex or base64. Hidden .env values are returned as *.",
            Schema(DocumentProperty + $",\"start\":{{\"type\":\"integer\",\"minimum\":0}},\"length\":{{\"type\":\"integer\",\"minimum\":1,\"maximum\":{McpServer.MaxReadBytes}}},\"encoding\":{{\"type\":\"string\",\"enum\":[\"hex\",\"base64\"]}}," + RevisionProperty, "\"document\",\"start\",\"length\""),
            true, ReadBytes),
        new("editor_search_bytes", "Search for a byte pattern",
            "Finds a byte pattern given as hex (for example '4D 5A ?? 90'; '?' stands for any nibble) and returns the offsets. Matches that touch a hidden .env value are left out.",
            Schema(DocumentProperty + ",\"pattern\":{\"type\":\"string\",\"minLength\":1},\"start\":{\"type\":\"integer\",\"minimum\":0},\"end\":{\"type\":\"integer\",\"minimum\":0},\"maxResults\":{\"type\":\"integer\",\"minimum\":1}," + RevisionProperty, "\"document\",\"pattern\""),
            true, SearchBytes),
        new("editor_prepare_byte_changes", "Propose byte changes",
            "Proposes changes to bytes of a shared document; nothing is written. The user sees the bytes before and after and applies or rejects them. 'revision' is required and must be current. " +
            "Each edit writes 'bytes' (hex) at 'offset' and replaces as many bytes as it writes unless 'replace' says otherwise (0 inserts; more than the bytes written shortens). Returns a proposal id for editor_get_change_status.",
            Schema(DocumentProperty + ",\"revision\":{\"type\":\"integer\",\"minimum\":0},\"title\":{\"type\":\"string\"},\"edits\":{\"type\":\"array\",\"minItems\":1,\"items\":{\"type\":\"object\",\"properties\":{" +
                   "\"offset\":{\"type\":\"integer\",\"minimum\":0},\"bytes\":{\"type\":\"string\"},\"replace\":{\"type\":\"integer\",\"minimum\":0}},\"required\":[\"offset\",\"bytes\"],\"additionalProperties\":false}}",
                "\"document\",\"revision\",\"edits\""),
            false, PrepareByteChanges),
    ];

    public static McpToolDefinition? Find(string name) => All.FirstOrDefault(t => t.Name == name);

    private static string Json(Action<Utf8JsonWriter> body) => System.Text.Encoding.UTF8.GetString(DocumentApi.Obj(body));

    private static void WriteId(Utf8JsonWriter w, Guid id) => w.WriteString("document", id);

    // ---- P6.3.1 ----

    private static async Task<string> ListDocuments(McpToolContext c, JsonElement args)
    {
        var list = await c.ListSharedAsync();
        return Json(w =>
        {
            w.WriteStartArray("documents");
            foreach (var d in list.EnumerateArray())
            {
                var id = d.GetProperty("id").GetGuid();
                w.WriteStartObject();
                w.WriteString("id", id);
                if (c.TitleOf(id, d) is { } title) w.WriteString("title", title); else w.WriteNull("title");
                w.WritePropertyName("path");
                d.GetProperty("path").WriteTo(w);
                w.WriteNumber("revision", d.GetProperty("revision").GetInt64());
                w.WriteNumber("byteLength", d.GetProperty("byteLength").GetInt64());
                w.WriteString("encoding", d.GetProperty("codec").GetString());
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });
    }

    private static async Task<long> OffsetOfLineAsync(McpToolContext c, Guid id, long revision, long line)
    {
        var r = await c.CallAsync(DocumentMethods.PositionToOffset, id, w => { w.WriteNumber("revision", revision); w.WriteNumber("line", line); w.WriteNumber("column", 0); });
        return r.GetProperty("offset").GetInt64();
    }

    private static Task<string> ReadRange(McpToolContext c, JsonElement args)
    {
        var id = McpToolContext.Document(args);
        return c.AtRevisionAsync(id, args, async revision =>
        {
            var info = await c.CallAsync(DocumentMethods.Info, id);
            long length = info.GetProperty("byteLength").GetInt64();
            long start, wanted;
            if (McpToolContext.OptionalNumber(args, "startLine") is { } startLine)
            {
                start = await OffsetOfLineAsync(c, id, revision, startLine);
                wanted = McpToolContext.OptionalNumber(args, "endLine") is { } endLine ? await OffsetOfLineAsync(c, id, revision, endLine) : length;
            }
            else
            {
                start = McpToolContext.OptionalNumber(args, "start") ?? info.GetProperty("bomLength").GetInt64();
                wanted = McpToolContext.OptionalNumber(args, "end") ?? length;
            }
            wanted = Math.Min(wanted, length);
            if (start > wanted) throw new ToolFailure($"The range starts at {start} and ends at {wanted}.");
            long end = Math.Min(wanted, start + c.Options.MaxRangeBytes);
            var read = await c.CallAsync(DocumentMethods.ReadText, id, w =>
            {
                w.WriteNumber("revision", revision);
                w.WriteNumber("start", start);
                w.WriteNumber("end", end);
                w.WriteBoolean("snap", true);
            });
            return Json(w =>
            {
                WriteId(w, id);
                w.WriteNumber("revision", read.GetProperty("revision").GetInt64());
                w.WriteNumber("start", read.GetProperty("start").GetInt64());
                w.WriteNumber("end", read.GetProperty("end").GetInt64());
                w.WriteString("text", read.GetProperty("text").GetString());
                w.WriteBoolean("truncated", end < wanted);
                var invalid = read.GetProperty("invalid");
                if (invalid.GetArrayLength() > 0)
                {
                    w.WritePropertyName("invalidCharacterIndexes");
                    invalid.WriteTo(w);
                }
            });
        });
    }

    /// <summary>Text around a match: <see cref="McpServerOptions.SnippetBytes"/> before and after it, grown to whole characters.</summary>
    private static async Task<string> SnippetAsync(McpToolContext c, Guid id, long revision, long length, long start, long end, long bom)
    {
        long from = Math.Max(bom, start - c.Options.SnippetBytes), to = Math.Min(length, end + c.Options.SnippetBytes);
        var read = await c.CallAsync(DocumentMethods.ReadText, id, w =>
        {
            w.WriteNumber("revision", revision);
            w.WriteNumber("start", from);
            w.WriteNumber("end", to);
            w.WriteBoolean("snap", true);
        });
        return read.GetProperty("text").GetString()!;
    }

    private static async Task<int?> LineOfAsync(McpToolContext c, Guid id, long revision, long offset)
    {
        try
        {
            var r = await c.CallAsync(DocumentMethods.OffsetToPosition, id, w => { w.WriteNumber("revision", revision); w.WriteNumber("offset", offset); });
            return r.GetProperty("line").GetInt32() + 1;
        }
        catch (RpcException e) when (e.Code is DocumentErrorCodes.PositionUnknown or DocumentErrorCodes.InvalidRange) { return null; }
    }

    private static Task<string> Search(McpToolContext c, JsonElement args)
    {
        var id = McpToolContext.Document(args);
        var text = McpToolContext.Text(args, "text");
        int max = c.Count(args, "maxResults");
        return c.AtRevisionAsync(id, args, revision => SearchAtAsync(c, id, text, max, revision));
    }

    private static async Task<string> SearchAtAsync(McpToolContext c, Guid id, string text, int max, long revision)
    {
        var info = await c.CallAsync(DocumentMethods.Info, id);
        long length = info.GetProperty("byteLength").GetInt64(), bom = info.GetProperty("bomLength").GetInt64();
        var found = await c.CallAsync(DocumentMethods.Find, id, w => { w.WriteNumber("revision", revision); w.WriteString("text", text); });
        var matches = found.GetProperty("matches").EnumerateArray().Take(max).ToList();
        var rows = new List<(long Start, long End, int? Line, string Snippet)>();
        foreach (var m in matches)
        {
            long start = m.GetProperty("start").GetInt64(), end = m.GetProperty("end").GetInt64();
            rows.Add((start, end, await LineOfAsync(c, id, revision, start), await SnippetAsync(c, id, revision, length, start, end, bom)));
        }
        return Json(w =>
        {
            WriteId(w, id);
            w.WriteNumber("revision", revision);
            WriteMatches(w, rows);
            w.WriteBoolean("truncated", found.GetProperty("truncated").GetBoolean() || found.GetProperty("matches").GetArrayLength() > max);
        });
    }

    private static void WriteMatches(Utf8JsonWriter w, List<(long Start, long End, int? Line, string Snippet)> rows)
    {
        w.WriteStartArray("matches");
        foreach (var (start, end, line, snippet) in rows)
        {
            w.WriteStartObject();
            w.WriteNumber("start", start);
            w.WriteNumber("end", end);
            if (line is { } l) w.WriteNumber("line", l);
            w.WriteString("snippet", snippet);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static Task<string> GetSelection(McpToolContext c, JsonElement args)
    {
        var id = McpToolContext.Document(args);
        // The selections name the revision they were taken at; when the user types before their text is read, the whole read is repeated against the new revision.
        return c.AtRevisionAsync(id, McpToolContext.NoArguments, async _ =>
        {
            var taken = await c.CallAsync(DocumentMethods.Selections, id);
            long revision = taken.GetProperty("revision").GetInt64(), budget = c.Options.MaxRangeBytes;
            bool truncated = false;
            var rows = new List<(long Anchor, long Caret, long Start, long End, int? Line, string Text, bool Cut)>();
            foreach (var s in taken.GetProperty("selections").EnumerateArray())
            {
                if (rows.Count >= c.Options.MaxItems) { truncated = true; break; }
                long anchor = s.GetProperty("anchor").GetInt64(), caret = s.GetProperty("caret").GetInt64();
                long start = Math.Min(anchor, caret), end = Math.Max(anchor, caret), take = Math.Min(end - start, budget);
                string text = "";
                if (take > 0)
                {
                    var read = await c.CallAsync(DocumentMethods.ReadText, id, w =>
                    {
                        w.WriteNumber("revision", revision);
                        w.WriteNumber("start", start);
                        w.WriteNumber("end", start + take);
                        w.WriteBoolean("snap", true);
                    });
                    text = read.GetProperty("text").GetString()!;
                    budget -= take;
                }
                truncated |= take < end - start;
                rows.Add((anchor, caret, start, end, await LineOfAsync(c, id, revision, start), text, take < end - start));
            }
            return Json(w =>
            {
                WriteId(w, id);
                w.WriteNumber("revision", revision);
                w.WriteNumber("primary", taken.GetProperty("primary").GetInt32());
                w.WriteStartArray("selections");
                foreach (var (anchor, caret, start, end, line, text, cut) in rows)
                {
                    w.WriteStartObject();
                    w.WriteNumber("anchor", anchor);
                    w.WriteNumber("caret", caret);
                    w.WriteNumber("start", start);
                    w.WriteNumber("end", end);
                    if (line is { } l) w.WriteNumber("line", l);
                    w.WriteString("text", text);
                    w.WriteBoolean("truncated", cut);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteBoolean("truncated", truncated);
                if (rows.Count == 0) w.WriteString("note", "The tab has no cursor to report: it is not shown in a text or hex view.");
            });
        });
    }

    /// <summary>Refuses a proposal while as many wait for the user as the limit allows, so an assistant cannot queue reviews without end.</summary>
    private static void CheckPending(McpToolContext c)
    {
        int pending = c.Options.Proposals.PendingCount;
        if (pending >= c.Options.MaxPendingProposals)
            throw new ToolFailure($"{pending} proposals are waiting for the decision of the user, which is the limit. Wait until the user has decided on some (editor_get_change_status) before proposing more.");
    }

    private static Task<string> PrepareChanges(McpToolContext c, JsonElement args)
    {
        var id = McpToolContext.Document(args);
        long revision = McpToolContext.Number(args, "revision");
        var title = McpToolContext.OptionalText(args, "title");
        if (!args.TryGetProperty("edits", out var edits) || edits.ValueKind != JsonValueKind.Array || edits.GetArrayLength() == 0)
            throw new ToolArgumentException("'edits' must be a non-empty array.");
        return PrepareChangesAsync(c, id, revision, title, edits);
    }

    private static async Task<string> PrepareChangesAsync(McpToolContext c, Guid id, long revision, string? title, JsonElement edits)
    {
        CheckPending(c);
        var resolved = new List<(long Start, long End, string Text)>();
        int index = 0;
        foreach (var edit in edits.EnumerateArray())
        {
            if (edit.ValueKind != JsonValueKind.Object) throw new ToolArgumentException($"edits[{index}] must be an object.");
            var text = McpToolContext.Text(edit, "text", allowEmpty: true);
            if (McpToolContext.OptionalText(edit, "find") is { Length: > 0 } find)
            {
                if (edit.TryGetProperty("start", out _) || edit.TryGetProperty("end", out _)) throw new ToolArgumentException($"edits[{index}] has both 'find' and a range.");
                var found = await c.CallAsync(DocumentMethods.Find, id, w => { w.WriteNumber("revision", revision); w.WriteString("text", find); });
                var matches = found.GetProperty("matches");
                if (matches.GetArrayLength() != 1)
                    throw new ToolFailure($"edits[{index}]: the text to find occurs {(found.GetProperty("truncated").GetBoolean() ? "many" : matches.GetArrayLength().ToString())} times; it must occur exactly once. Add surrounding text or give start and end.");
                resolved.Add((matches[0].GetProperty("start").GetInt64(), matches[0].GetProperty("end").GetInt64(), text));
            }
            else
            {
                if (!edit.TryGetProperty("start", out _) || !edit.TryGetProperty("end", out _)) throw new ToolArgumentException($"edits[{index}] needs 'find' or both 'start' and 'end'.");
                resolved.Add((McpToolContext.Number(edit, "start"), McpToolContext.Number(edit, "end"), text));
            }
            index++;
        }
        var answer = await c.CallAsync(DocumentMethods.EditPropose, id, w =>
        {
            w.WriteNumber("revision", revision);
            if (title is not null) w.WriteString("title", title);
            w.WriteStartArray("edits");
            foreach (var (start, end, text) in resolved)
            {
                w.WriteStartObject();
                w.WriteNumber("start", start);
                w.WriteNumber("end", end);
                w.WriteString("text", text);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });
        return ProposalAnswer(id, answer, resolved.Count);
    }

    private static string ProposalAnswer(Guid id, JsonElement answer, int edits) => Json(w =>
    {
        w.WriteString("proposal", answer.GetProperty("proposal").GetString());
        WriteId(w, id);
        w.WriteNumber("revision", answer.GetProperty("revision").GetInt64());
        w.WriteNumber("edits", edits);
        w.WriteString("status", "pending");
        w.WriteString("note", "Nothing was written. The user sees the proposal as a diff and decides; read the outcome with editor_get_change_status.");
    });

    private static Task<string> ChangeStatus(McpToolContext c, JsonElement args)
    {
        var id = McpToolContext.Text(args, "proposal");
        var entry = c.Options.Proposals.Get(id);
        return Task.FromResult(Json(w =>
        {
            w.WriteString("proposal", id);
            if (entry is null)
            {
                w.WriteString("status", "unknown");
                return;
            }
            w.WriteString("status", entry.Status.ToString().ToLowerInvariant());
            WriteId(w, entry.Document);
            w.WriteNumber("revision", entry.Revision);
            w.WriteNumber("edits", entry.Edits);
            if (entry.AppliedRevision is { } applied) w.WriteNumber("appliedRevision", applied);
        }));
    }

    // ---- P6.3.2 ----

    private static async Task<string> Outline(McpToolContext c, JsonElement args)
    {
        var id = McpToolContext.Document(args);
        await c.CallAsync(DocumentMethods.Info, id); // refuses a document that is not shared before anything is indexed
        if (c.Options.EnsureOutline is { } ensure)
        {
            try { await ensure(id, c.Cancellation); }
            catch (Exception e) when (e is not OperationCanceledException) { /* an outline that cannot be made is reported as empty below */ }
        }
        var listed = await c.CallAsync(DocumentMethods.SymbolsList, id);
        int budget = c.Options.MaxItems;
        bool truncated = false;
        return Json(w =>
        {
            WriteId(w, id);
            w.WriteNumber("revision", listed.GetProperty("revision").GetInt64());
            w.WriteStartArray("sets");
            foreach (var set in listed.GetProperty("sets").EnumerateArray())
            {
                var names = set.GetProperty("symbols").EnumerateArray().ToDictionary(s => s.GetProperty("id").GetString()!, s => s.GetProperty("name").GetString()!);
                w.WriteStartObject();
                w.WriteString("provider", set.GetProperty("plugin").GetString());
                w.WriteNumber("revision", set.GetProperty("revision").GetInt64());
                w.WriteBoolean("current", set.GetProperty("current").GetBoolean());
                w.WriteStartArray("symbols");
                foreach (var s in set.GetProperty("symbols").EnumerateArray())
                {
                    if (budget-- <= 0) { truncated = true; break; }
                    w.WriteStartObject();
                    w.WriteString("name", s.GetProperty("name").GetString());
                    w.WriteString("kind", s.GetProperty("kind").GetString());
                    if (s.GetProperty("parent").GetString() is { } parent && names.TryGetValue(parent, out var parentName)) w.WriteString("parent", parentName);
                    w.WriteNumber("start", s.GetProperty("start").GetInt64());
                    w.WriteNumber("end", s.GetProperty("end").GetInt64());
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteBoolean("truncated", truncated);
            if (listed.GetProperty("sets").GetArrayLength() == 0) w.WriteString("note", "No outline provider has symbols for this document (its language mode has none).");
        });
    }

    private static async Task<string> FindInFiles(McpToolContext c, JsonElement args)
    {
        var text = McpToolContext.Text(args, "text");
        int budget = c.Count(args, "maxResults");
        var list = await c.ListSharedAsync();
        var results = new List<(Guid Id, string? Title, JsonElement Path, long Revision, List<(long Start, long End, int? Line, string Snippet)> Rows)>();
        bool truncated = false;
        foreach (var d in list.EnumerateArray())
        {
            if (budget <= 0) { truncated = true; break; }
            var id = d.GetProperty("id").GetGuid();
            var bom = d.GetProperty("bomLength").GetInt64();
            var rows = new List<(long, long, int?, string)>();
            long usedRevision = 0;
            await c.AtRevisionAsync(id, McpToolContext.NoArguments, async revision =>
            {
                rows.Clear();
                usedRevision = revision;
                var length = (await c.CallAsync(DocumentMethods.Info, id)).GetProperty("byteLength").GetInt64();
                var found = await c.CallAsync(DocumentMethods.Find, id, w => { w.WriteNumber("revision", revision); w.WriteString("text", text); });
                foreach (var m in found.GetProperty("matches").EnumerateArray())
                {
                    if (budget <= 0) { truncated = true; break; }
                    long start = m.GetProperty("start").GetInt64(), end = m.GetProperty("end").GetInt64();
                    rows.Add((start, end, await LineOfAsync(c, id, revision, start), await SnippetAsync(c, id, revision, length, start, end, bom)));
                    budget--;
                }
                truncated |= found.GetProperty("truncated").GetBoolean();
                return "";
            });
            if (rows.Count > 0) results.Add((id, c.TitleOf(id, d), d.GetProperty("path"), usedRevision, rows));
        }
        return Json(w =>
        {
            w.WriteString("text", text);
            w.WriteStartArray("documents");
            foreach (var (id, title, path, revision, rows) in results)
            {
                w.WriteStartObject();
                WriteId(w, id);
                if (title is not null) w.WriteString("title", title); else w.WriteNull("title");
                w.WritePropertyName("path");
                path.WriteTo(w);
                w.WriteNumber("revision", revision);
                WriteMatches(w, rows);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteBoolean("truncated", truncated);
        });
    }

    private static Task<string> FileFacts(McpToolContext c, JsonElement args)
    {
        var id = McpToolContext.Document(args);
        return c.AtRevisionAsync(id, args, async revision =>
        {
            var facts = await c.CallAsync(DocumentMethods.Facts, id, w => w.WriteNumber("revision", revision));
            var info = await c.CallAsync(DocumentMethods.Info, id);
            return Json(w =>
            {
                WriteId(w, id);
                if (c.TitleOf(id, info) is { } title) w.WriteString("title", title); else w.WriteNull("title");
                foreach (var property in facts.EnumerateObject())
                {
                    w.WritePropertyName(property.Name);
                    property.Value.WriteTo(w);
                }
            });
        });
    }

    private static Task<string> LogQuery(McpToolContext c, JsonElement args)
    {
        var id = McpToolContext.Document(args);
        var level = McpToolContext.Text(args, "level");
        if (level is not ("error" or "warning" or "info" or "debug")) throw new ToolArgumentException("'level' must be error, warning, info or debug.");
        long? start = McpToolContext.OptionalNumber(args, "start");
        int max = c.Count(args, "maxLines");
        return c.AtRevisionAsync(id, args, async revision =>
        {
            var found = await c.CallAsync(DocumentMethods.FindLog, id, w =>
            {
                w.WriteNumber("revision", revision);
                w.WriteString("level", level);
                if (start is { } s) w.WriteNumber("start", s);
                w.WriteNumber("max", max);
            });
            return Json(w =>
            {
                WriteId(w, id);
                w.WriteNumber("revision", revision);
                w.WriteString("level", level);
                w.WriteStartArray("lines");
                foreach (var line in found.GetProperty("lines").EnumerateArray())
                {
                    var text = line.GetProperty("text").GetString()!;
                    bool cut = line.GetProperty("truncated").GetBoolean() || text.Length > c.Options.MaxLineChars;
                    w.WriteStartObject();
                    w.WriteNumber("offset", line.GetProperty("offset").GetInt64());
                    w.WriteString("text", text.Length > c.Options.MaxLineChars ? text[..c.Options.MaxLineChars] : text);
                    w.WriteBoolean("truncated", cut);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WritePropertyName("next");
                found.GetProperty("next").WriteTo(w);
            });
        });
    }

    // ---- P8.4.1 ----

    private sealed record DiagnosticRow(JsonElement Item, (int Line, int Column)? Start, (int Line, int Column)? End);

    /// <summary>The stored diagnostics of a shared document at its current revision, each with the 0-based position of its range (null when the revision moved on or the position is unknown).</summary>
    private static async Task<(long Revision, int Withheld, List<DiagnosticRow> Rows, bool Truncated)> DiagnosticsAsync(McpToolContext c, Guid id, int max)
    {
        var listed = await c.CallAsync(DocumentMethods.DiagnosticsList, id);
        long revision = listed.GetProperty("revision").GetInt64();
        async Task<(int, int)?> Place(long offset)
        {
            try
            {
                var r = await c.CallAsync(DocumentMethods.OffsetToPosition, id, w => { w.WriteNumber("revision", revision); w.WriteNumber("offset", offset); });
                return (r.GetProperty("line").GetInt32(), r.GetProperty("column").GetInt32());
            }
            catch (RpcException e) when (e.Code is DocumentErrorCodes.PositionUnknown or DocumentErrorCodes.InvalidRange or DocumentErrorCodes.RevisionConflict) { return null; }
        }
        var rows = new List<DiagnosticRow>();
        foreach (var item in listed.GetProperty("diagnostics").EnumerateArray().Take(max))
            rows.Add(new(item, await Place(item.GetProperty("start").GetInt64()), await Place(item.GetProperty("end").GetInt64())));
        return (revision, listed.GetProperty("withheld").GetInt32(), rows, listed.GetProperty("diagnostics").GetArrayLength() > max);
    }

    private static async Task<string> Diagnostics(McpToolContext c, JsonElement args)
    {
        var id = McpToolContext.Document(args);
        var (revision, withheld, rows, truncated) = await DiagnosticsAsync(c, id, c.Options.MaxItems);
        return Json(w =>
        {
            WriteId(w, id);
            w.WriteNumber("revision", revision);
            w.WriteStartArray("diagnostics");
            foreach (var row in rows)
            {
                w.WriteStartObject();
                foreach (var property in row.Item.EnumerateObject().Where(x => x.Name != "plugin"))
                {
                    w.WritePropertyName(property.Name);
                    property.Value.WriteTo(w);
                }
                if (row.Start is { } s) w.WriteNumber("line", s.Line + 1);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteNumber("withheld", withheld);
            w.WriteBoolean("truncated", truncated);
        });
    }

    /// <summary>
    /// <c>getDiagnostics</c> of the /ide bridge: per shared document that has diagnostics (or the one whose file URI is given), the same data as editor_diagnostics.
    /// The result is an array of <c>{uri, diagnostics:[{message, severity, range, source}]}</c>, the shape the community description of the protocol (coder/claudecode.nvim, PROTOCOL.md) gives.
    /// That description shows only <c>range.start</c>; <c>range.end</c> is added because a range without an end is not a range. The diagnostic code is left out, as it is not part of the shape.
    /// </summary>
    internal static async Task<string> IdeDiagnostics(McpToolContext c, JsonElement args)
    {
        var uri = McpToolContext.OptionalText(args, "uri");
        var list = await c.ListSharedAsync();
        var files = new List<(string Uri, List<DiagnosticRow> Rows)>();
        foreach (var d in list.EnumerateArray())
        {
            var id = d.GetProperty("id").GetGuid();
            var path = d.GetProperty("path").GetString();
            var fileUri = path is null ? "untitled:" + id.ToString("N") : new Uri(path).AbsoluteUri;
            if (uri is not null && !string.Equals(uri, fileUri, StringComparison.OrdinalIgnoreCase)) continue;
            var (_, _, rows, _) = await DiagnosticsAsync(c, id, c.Options.MaxItems);
            if (rows.Count > 0) files.Add((fileUri, rows));
        }
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartArray();
            foreach (var (fileUri, rows) in files)
            {
                w.WriteStartObject();
                w.WriteString("uri", fileUri);
                w.WriteStartArray("diagnostics");
                foreach (var row in rows)
                {
                    var x = row.Item;
                    w.WriteStartObject();
                    w.WriteString("message", x.GetProperty("message").GetString());
                    w.WriteString("severity", x.GetProperty("severity").GetString() switch { "error" => "Error", "warning" => "Warning", "hint" => "Hint", _ => "Information" });
                    if (row.Start is { } s && row.End is { } e)
                    {
                        w.WriteStartObject("range");
                        w.WriteStartObject("start"); w.WriteNumber("line", s.Line); w.WriteNumber("character", s.Column); w.WriteEndObject();
                        w.WriteStartObject("end"); w.WriteNumber("line", e.Line); w.WriteNumber("character", e.Column); w.WriteEndObject();
                        w.WriteEndObject();
                    }
                    w.WriteString("source", x.GetProperty("source").GetString());
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // ---- P6.3.3 ----

    private static Task<string> ReadBytes(McpToolContext c, JsonElement args)
    {
        var id = McpToolContext.Document(args);
        long start = McpToolContext.Number(args, "start");
        long length = McpToolContext.Number(args, "length", 1);
        if (length > McpServer.MaxReadBytes) throw new ToolArgumentException($"'length' is at most {McpServer.MaxReadBytes}.");
        var encoding = McpToolContext.OptionalText(args, "encoding") ?? "hex";
        if (encoding is not ("hex" or "base64")) throw new ToolArgumentException("'encoding' must be hex or base64.");
        return c.AtRevisionAsync(id, args, async revision =>
        {
            var info = await c.CallAsync(DocumentMethods.Info, id);
            long end = Math.Min(start + length, info.GetProperty("byteLength").GetInt64());
            if (start > end) throw new ToolFailure($"The document has {info.GetProperty("byteLength").GetInt64()} bytes; there is nothing at {start}.");
            var read = await c.CallAsync(DocumentMethods.ReadBytes, id, w => { w.WriteNumber("revision", revision); w.WriteNumber("start", start); w.WriteNumber("end", end); });
            var base64 = read.GetProperty("bytes").GetString()!;
            return Json(w =>
            {
                WriteId(w, id);
                w.WriteNumber("revision", read.GetProperty("revision").GetInt64());
                w.WriteNumber("start", start);
                w.WriteNumber("end", end);
                w.WriteString("encoding", encoding);
                w.WriteString("data", encoding == "base64" ? base64 : Convert.ToHexString(Convert.FromBase64String(base64)));
            });
        });
    }

    private static Task<string> SearchBytes(McpToolContext c, JsonElement args)
    {
        var id = McpToolContext.Document(args);
        var pattern = McpToolContext.Text(args, "pattern");
        long? start = McpToolContext.OptionalNumber(args, "start"), end = McpToolContext.OptionalNumber(args, "end");
        int max = c.Count(args, "maxResults");
        return c.AtRevisionAsync(id, args, async revision =>
        {
            var found = await c.CallAsync(DocumentMethods.FindBytes, id, w =>
            {
                w.WriteNumber("revision", revision);
                w.WriteString("pattern", pattern);
                if (start is { } s) w.WriteNumber("start", s);
                if (end is { } e) w.WriteNumber("end", e);
                w.WriteNumber("max", max);
            });
            return Json(w =>
            {
                WriteId(w, id);
                w.WriteNumber("revision", revision);
                w.WriteNumber("length", found.GetProperty("length").GetInt32());
                w.WritePropertyName("matches");
                found.GetProperty("matches").WriteTo(w);
                w.WriteBoolean("truncated", found.GetProperty("truncated").GetBoolean());
            });
        });
    }

    private static Task<string> PrepareByteChanges(McpToolContext c, JsonElement args)
    {
        var id = McpToolContext.Document(args);
        long revision = McpToolContext.Number(args, "revision");
        var title = McpToolContext.OptionalText(args, "title");
        if (!args.TryGetProperty("edits", out var edits) || edits.ValueKind != JsonValueKind.Array || edits.GetArrayLength() == 0)
            throw new ToolArgumentException("'edits' must be a non-empty array.");
        var parsed = new List<(long Start, long End, byte[] Bytes)>();
        int index = 0;
        foreach (var edit in edits.EnumerateArray())
        {
            if (edit.ValueKind != JsonValueKind.Object) throw new ToolArgumentException($"edits[{index}] must be an object.");
            long offset = McpToolContext.Number(edit, "offset");
            byte[] bytes;
            try { bytes = Convert.FromHexString(string.Concat(McpToolContext.Text(edit, "bytes", allowEmpty: true).Where(ch => !char.IsWhiteSpace(ch)))); }
            catch (FormatException) { throw new ToolArgumentException($"edits[{index}].bytes must be hex digits in pairs."); }
            long replace = McpToolContext.OptionalNumber(edit, "replace") ?? bytes.Length;
            parsed.Add((offset, offset + replace, bytes));
            index++;
        }
        return ProposeBytesAsync(c, id, revision, title, parsed);
    }

    private static async Task<string> ProposeBytesAsync(McpToolContext c, Guid id, long revision, string? title, List<(long Start, long End, byte[] Bytes)> edits)
    {
        CheckPending(c);
        var answer = await c.CallAsync(DocumentMethods.EditPropose, id, w =>
        {
            w.WriteNumber("revision", revision);
            w.WriteBoolean("rawBytes", true);
            if (title is not null) w.WriteString("title", title);
            w.WriteStartArray("edits");
            foreach (var (start, end, bytes) in edits)
            {
                w.WriteStartObject();
                w.WriteNumber("start", start);
                w.WriteNumber("end", end);
                w.WriteBase64String("bytes", bytes);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });
        return ProposalAnswer(id, answer, edits.Count);
    }
}
