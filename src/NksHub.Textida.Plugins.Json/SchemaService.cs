using System.Buffers;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Data;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Plugins.Json.Schema;
using NksHub.Textida.Rpc;
using Doc = NksHub.Textida.Plugins.Json.DocumentAccess.Doc;

namespace NksHub.Textida.Plugins.Json;

/// <summary>Limits of the schema features; the editor passes them on the command line, so no number is fixed in the plugin.</summary>
/// <param name="MaxBytes">Largest file that is read whole to be checked or completed; a larger one gets no diagnostics and no offers. Must not exceed what one read returns.</param>
public sealed record SchemaSettings(long MaxBytes, int MaxDepth, int RegexTimeoutMilliseconds);

/// <summary>
/// The schema engine behind the language service API: <c>schema.check</c> reads a JSON, JSONC, TOML or YAML file (or the YAML frontmatter of a Markdown file), checks it against the schema that applies to its path
/// (or that it names itself) and publishes the findings as diagnostics of the source <see cref="Source"/>; <c>completion/request</c> offers the keys and the enum values the schema allows around the caret;
/// <c>hover/request</c> describes the key or value under the pointer. All work on one revision and say so.
/// </summary>
public sealed class SchemaService(JsonRpcDispatcher rpc, SchemaSettings settings, SchemaCatalog catalog, SchemaLoader loader)
{
    public const string Check = "schema.check";
    public const string Source = "schema";

    private SchemaOptions Options => new(settings.MaxDepth, TimeSpan.FromMilliseconds(settings.RegexTimeoutMilliseconds));

    private readonly DocumentAccess _docs = new(rpc);

    private Task<JsonElement> CallAsync(string method, string paramsJson) => _docs.CallAsync(method, paramsJson);

    private Task<Doc> InfoAsync(string id, long revision) => _docs.InfoAsync(id, revision);

    private Task<string?> ReadAsync(Doc d, long? end = null) => _docs.ReadAsync(d, end);

    private static Dictionary<int, long> ToBytes(Doc d, string text, IEnumerable<int> chars) => DocumentAccess.ToBytes(d, text, chars);

    private static string Quote(string text) => DocumentAccess.Quote(text);

    private static string Language(JsonElement p) => p.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString()! : "en";

    private static string StateName(SchemaState state) => state switch
    {
        SchemaState.NoConsent => "noConsent",
        SchemaState.Unavailable => "unavailable",
        SchemaState.TooLarge => "tooLarge",
        SchemaState.Invalid => "invalid",
        _ => "ready",
    };

    /// <summary>The schema of the file: the one it names itself, else the one its path maps to; when the first cannot be loaded and the path maps to another, that one is used.</summary>
    private async Task<(SchemaEntry? Entry, SchemaText Load, DataDialect Dialect)> PickAsync(string path, string text, bool network)
    {
        var declared = SchemaCatalog.DeclaredSchema(SchemaCatalog.DialectOf(path), text, settings.MaxDepth);
        var (entry, dialect) = catalog.Resolve(path, declared);
        if (entry is null) return (null, new SchemaText(null, SchemaState.Ready), dialect);
        var load = await loader.LoadAsync(entry, path, network);
        if (load.Text is null && declared is not null)
        {
            var (byMask, maskDialect) = catalog.Resolve(path, null);
            if (byMask is not null && byMask.Id != entry.Id && await loader.LoadAsync(byMask, path, network) is { Text: not null } second) return (byMask, second, maskDialect);
        }
        return (entry, load, dialect);
    }

    /// <summary>A YAML file with template tags (Helm, Jinja) is not YAML until it is rendered, so a syntax error in it is not an error of the file.</summary>
    private static bool IsTemplate(DataDialect dialect, string text) =>
        dialect == DataDialect.Yaml && (text.Contains("{{", StringComparison.Ordinal) || text.Contains("{%", StringComparison.Ordinal));

    /// <summary>Line and column, counted from 1, of a character offset.</summary>
    private static (int Line, int Column) LineColumn(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        int line = 1, lineStart = 0;
        for (int i = 0; i < offset; i++)
            if (text[i] == '\n') { line++; lineStart = i + 1; }
        return (line, offset - lineStart + 1);
    }

    /// <summary>
    /// Checks the file at the revision of the command and publishes the result. The answer is
    /// <c>{"status":..., "schema":id|null, "name":text|null, "issues":n, "state":..., "host":text|null, "cached":bool, "offline":bool}</c>;
    /// <c>status</c> is <c>ok</c>, <c>tooLarge</c>, <c>invalidBytes</c> or <c>stale</c>, and in the last three the diagnostics of the source are taken away.
    /// <c>state</c> says what became of the schema: <c>ready</c>, <c>noConsent</c> (a remote schema whose <c>host</c> the user has not allowed), <c>unavailable</c>, <c>tooLarge</c>, <c>invalid</c>,
    /// or <c>unsupported</c> (the file uses a form of YAML the reader does not know, or is a template with <c>{{</c> or <c>{%</c>, so it was not checked); <c>cached</c> and <c>offline</c> tell that a remote schema came from the copy on disk.
    /// A syntax error is published with or without a schema, except for a form the reader does not support. The language of the command writes the messages.
    /// </summary>
    public async Task<byte[]> CheckAsync(JsonElement p)
    {
        string id = p.GetProperty("document").GetString()!;
        long revision = p.GetProperty("revision").GetInt64();
        var texts = SchemaTexts.For(Language(p));
        SchemaEntry? schema = null;
        SchemaText load = new(null, SchemaState.Ready);
        string status = "ok", state = "ready";
        int issues = 0;
        try
        {
            var d = await InfoAsync(id, revision);
            schema = catalog.Resolve(d.Path ?? "").Schema;
            var items = new List<(int Start, int End, string Message, string Code)>();
            string? text = null;
            if (d.Length > settings.MaxBytes) status = "tooLarge";
            else if ((text = await ReadAsync(d)) is null) status = "invalidBytes";
            else
            {
                var (picked, pickedLoad, dialect) = await PickAsync(d.Path ?? "", text, network: true);
                (schema, load) = (picked, pickedLoad);
                var result = catalog.Check(d.Path ?? "", text, Options, picked, dialect, load.Text);
                state = StateName(load.State);
                if (result.Syntax is { } syntax)
                {
                    if (syntax.Unsupported || IsTemplate(dialect, text)) state = "unsupported";
                    else if (schema is not null || dialect != DataDialect.Markdown)
                    {
                        var (line, column) = LineColumn(text, syntax.Start);
                        items.Add((syntax.Start, Math.Max(syntax.End, syntax.Start), texts.Syntax(line, column, syntax.Message), "syntax"));
                    }
                }
                items.AddRange(result.Issues.Select(i => (i.Start, i.End, texts.Issue(i), i.Code)));
            }
            issues = items.Count;
            var ordered = items.OrderBy(i => i.Start).ThenBy(i => i.End).ToList();
            var bytes = text is null ? new Dictionary<int, long>() : ToBytes(d, text, ordered.SelectMany(i => new[] { i.Start, i.End }));
            var json = new StringBuilder($"{{\"document\":\"{id}\",\"revision\":{revision},\"source\":\"{Source}\",\"diagnostics\":[");
            for (int n = 0; n < ordered.Count; n++)
            {
                if (n > 0) json.Append(',');
                var item = ordered[n];
                json.Append($"{{\"start\":{bytes[item.Start]},\"end\":{Math.Max(bytes[item.End], bytes[item.Start])},\"severity\":\"error\",\"message\":\"{Quote(item.Message)}\",\"code\":\"{Quote(item.Code)}\"}}");
            }
            json.Append("]}");
            await CallAsync(DocumentMethods.DiagnosticsPublish, json.ToString());
        }
        catch (RpcException e) when (e.Code == DocumentErrorCodes.RevisionConflict) { status = "stale"; }
        var name = schema is null ? "null" : $"\"{Quote(schema.Name)}\"";
        var host = load.Host is null ? "null" : $"\"{Quote(load.Host)}\"";
        return Encoding.UTF8.GetBytes($"{{\"status\":\"{status}\",\"schema\":{(schema is null ? "null" : $"\"{Quote(schema.Id)}\"")},\"name\":{name},\"issues\":{issues},\"state\":\"{state}\",\"host\":{host},\"cached\":{(load.FromCache ? "true" : "false")},\"offline\":{(load.Offline ? "true" : "false")}}}");
    }

    /// <summary>
    /// Answers <c>completion/request</c> with the keys of the schema that applies to the file, or its enum values when a value is being written, for the revision that was asked. No schema, a remote schema
    /// that has not been downloaded, a file that is too large or bytes the encoding cannot read give no items; so does a caret where nothing can be offered.
    /// </summary>
    public async Task<byte[]> CompleteAsync(JsonElement p)
    {
        string id = p.GetProperty("document").GetString()!;
        long revision = p.GetProperty("revision").GetInt64(), offset = p.GetProperty("offset").GetInt64();
        var items = new StringBuilder();
        void Add(string schemaName, string label, string kind, int start, int end, string insert, Dictionary<int, long> bytes)
        {
            if (items.Length > 0) items.Append(',');
            items.Append($"{{\"label\":\"{Quote(label)}\",\"detail\":\"{Quote(schemaName)}\",\"kind\":\"{kind}\",\"start\":{bytes[start]},\"end\":{bytes[end]},\"text\":\"{Quote(insert)}\"}}");
        }
        try
        {
            var d = await InfoAsync(id, revision);
            if (d.Length <= settings.MaxBytes && offset >= d.Bom && offset <= d.Length && await ReadAsync(d) is { } text && await ReadAsync(d, offset) is { } before)
            {
                var (schema, load, dialect) = await PickAsync(d.Path ?? "", text, network: false);
                if (schema is not null && load.Text is not null && SchemaCatalog.TryParse(load.Text, DataDialect.Json, settings.MaxDepth, out var root, out _))
                {
                    if (KeyCompletion.Offer(root!, dialect, text, before.Length, Options) is { } offer)
                    {
                        var bytes = ToBytes(d, text, [offer.Start, offer.End]);
                        foreach (var key in offer.Keys) Add(schema.Name, key, "property", offer.Start, offer.End, offer.Insert(key), bytes);
                    }
                    else if (ValueCompletion.Offer(root!, dialect, text, before.Length, Options) is { } values)
                    {
                        var bytes = ToBytes(d, text, [values.Start, values.End]);
                        foreach (var (label, insert) in values.Items) Add(schema.Name, label, "value", values.Start, values.End, insert, bytes);
                    }
                }
            }
        }
        catch (RpcException e) when (e.Code is DocumentErrorCodes.RevisionConflict or DocumentErrorCodes.InvalidRange) { items.Clear(); }
        return Encoding.UTF8.GetBytes($"{{\"revision\":{revision},\"items\":[{items}]}}");
    }

    /// <summary>
    /// Answers <c>hover/request</c>: what the schema says about the key or the scalar value at the byte offset, for the revision that was asked, or just the revision when there is nothing to say
    /// (no schema, a remote schema that has not been downloaded, a file that does not parse or that is too large). The text is written in the language of the request.
    /// </summary>
    public async Task<byte[]> HoverAsync(JsonElement p)
    {
        string id = p.GetProperty("document").GetString()!;
        long revision = p.GetProperty("revision").GetInt64(), offset = p.GetProperty("offset").GetInt64();
        var texts = SchemaTexts.For(Language(p));
        string said = "";
        try
        {
            var d = await InfoAsync(id, revision);
            if (d.Length <= settings.MaxBytes && offset >= d.Bom && offset <= d.Length && await ReadAsync(d) is { } text && await ReadAsync(d, offset) is { } before)
            {
                var (schema, load, dialect) = await PickAsync(d.Path ?? "", text, network: false);
                if (schema is not null && load.Text is not null && SchemaCatalog.TryParse(load.Text, DataDialect.Json, settings.MaxDepth, out var root, out _)
                    && SchemaHover.Describe(root!, dialect, text, before.Length, Options, texts) is { } info)
                {
                    var bytes = ToBytes(d, text, [info.Start, info.End]);
                    said = $",\"text\":\"{Quote(info.Text)}\",\"start\":{bytes[info.Start]},\"end\":{bytes[info.End]}";
                }
            }
        }
        catch (RpcException e) when (e.Code is DocumentErrorCodes.RevisionConflict or DocumentErrorCodes.InvalidRange) { said = ""; }
        return Encoding.UTF8.GetBytes($"{{\"revision\":{revision}{said}}}");
    }

    /// <summary>Offers the editor to ask this plugin for completion and hover; sent on every start, so a restarted process answers again.</summary>
    public async Task RegisterAsync()
    {
        await CallAsync(DocumentMethods.CompletionRegister, "{\"enabled\":true}");
        try { await CallAsync(DocumentMethods.HoverRegister, "{\"enabled\":true}"); }
        catch (RpcException) { /* an editor without hover text: the plugin still completes */ }
    }
}
