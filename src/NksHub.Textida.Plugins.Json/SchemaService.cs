using System.Buffers;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Plugins.Json.Schema;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugins.Json;

/// <summary>Limits of the schema features; the editor passes them on the command line, so no number is fixed in the plugin.</summary>
/// <param name="MaxBytes">Largest file that is read whole to be checked or completed; a larger one gets no diagnostics and no offers. Must not exceed what one read returns.</param>
public sealed record SchemaSettings(long MaxBytes, int MaxDepth, int RegexTimeoutMilliseconds);

/// <summary>
/// The schema engine behind the language service API: <c>schema.check</c> reads a JSON, JSONC, TOML or YAML file (or the YAML frontmatter of a Markdown file), checks it against the schema that applies to its path and publishes the
/// findings as diagnostics of the source <see cref="Source"/>; <c>completion/request</c> offers the keys the schema allows around the caret. Both work on one revision and say so.
/// </summary>
public sealed class SchemaService(JsonRpcDispatcher rpc, SchemaSettings settings, SchemaCatalog catalog)
{
    public const string Check = "schema.check";
    public const string Source = "schema";

    private SchemaOptions Options => new(settings.MaxDepth, TimeSpan.FromMilliseconds(settings.RegexTimeoutMilliseconds));

    private sealed record Doc(string Id, long Revision, string? Path, TextCodec Codec, int Bom, long Length);

    private async Task<JsonElement> CallAsync(string method, string paramsJson) => (await rpc.RequestAsync(method, Encoding.UTF8.GetBytes(paramsJson))).Clone();

    private async Task<Doc> InfoAsync(string id, long revision)
    {
        var info = await CallAsync(DocumentMethods.Info, $"{{\"document\":\"{id}\"}}");
        if (info.GetProperty("revision").GetInt64() != revision) throw new RpcException(DocumentErrorCodes.RevisionConflict, "Stale.");
        var path = info.GetProperty("path") is { ValueKind: JsonValueKind.String } p ? p.GetString() : null;
        return new Doc(id, revision, path, TextCodec.ByName(info.GetProperty("codec").GetString()!) ?? TextCodec.Utf8, (int)info.GetProperty("bomLength").GetInt64(), info.GetProperty("byteLength").GetInt64());
    }

    /// <summary>The text of the whole file, or null when it holds bytes the encoding cannot read (then nothing is checked).</summary>
    private async Task<string?> ReadAsync(Doc d, long? end = null)
    {
        var read = await CallAsync(DocumentMethods.ReadText, $"{{\"document\":\"{d.Id}\",\"revision\":{d.Revision},\"start\":{d.Bom},\"end\":{end ?? d.Length}}}");
        return read.GetProperty("invalid").GetArrayLength() > 0 ? null : read.GetProperty("text").GetString()!;
    }

    /// <summary>Byte offsets (from the start of the file, BOM included) of character offsets of <paramref name="text"/>, by character offset.</summary>
    private static Dictionary<int, long> ToBytes(Doc d, string text, IEnumerable<int> chars)
    {
        var codec = d.Codec.WithBom(false);
        var result = new Dictionary<int, long>();
        int at = 0;
        long bytes = d.Bom;
        foreach (var c in chars.Distinct().Order())
        {
            int target = Math.Clamp(c, at, text.Length);
            if (target > 0 && target < text.Length && char.IsHighSurrogate(text[target - 1]) && char.IsLowSurrogate(text[target])) target++;
            bytes += codec.Encode(text[at..target]).Length;
            at = target;
            result[c] = bytes;
        }
        return result;
    }

    private static string Quote(string text) => JsonEncodedText.Encode(text).ToString();

    /// <summary>
    /// Checks the file at the revision of the command and publishes the result. The answer is <c>{"status":..., "schema":id|null, "name":text|null, "issues":n}</c>;
    /// <c>status</c> is <c>ok</c>, <c>tooLarge</c>, <c>invalidBytes</c> or <c>stale</c>, and in the last three the diagnostics of the source are taken away.
    /// </summary>
    public async Task<byte[]> CheckAsync(JsonElement p)
    {
        string id = p.GetProperty("document").GetString()!;
        long revision = p.GetProperty("revision").GetInt64();
        SchemaEntry? schema = null;
        string status = "ok";
        int issues = 0;
        try
        {
            var d = await InfoAsync(id, revision);
            (schema, var dialect) = catalog.Resolve(d.Path ?? "");
            var items = new List<(int Start, int End, string Message, string Code)>();
            string? text = null;
            if (d.Length > settings.MaxBytes) status = "tooLarge";
            else if ((text = await ReadAsync(d)) is null) status = "invalidBytes";
            else
            {
                var result = catalog.Check(d.Path ?? "", text, Options);
                if (result.Syntax is { } syntax && (schema is not null || dialect is DataDialect.Json or DataDialect.Jsonc)) items.Add((syntax.Start, Math.Max(syntax.End, syntax.Start), syntax.Message, "syntax"));
                items.AddRange(result.Issues.Select(i => (i.Start, i.End, i.Pointer.Length == 0 ? i.Detail : i.Pointer + ": " + i.Detail, i.Code)));
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
        return Encoding.UTF8.GetBytes($"{{\"status\":\"{status}\",\"schema\":{(schema is null ? "null" : $"\"{Quote(schema.Id)}\"")},\"name\":{name},\"issues\":{issues}}}");
    }

    /// <summary>
    /// Answers <c>completion/request</c> with the keys of the schema that applies to the file, for the revision that was asked. No schema, a file that is too large or bytes
    /// the encoding cannot read give no items; so does a caret where no key can be written.
    /// </summary>
    public async Task<byte[]> CompleteAsync(JsonElement p)
    {
        string id = p.GetProperty("document").GetString()!;
        long revision = p.GetProperty("revision").GetInt64(), offset = p.GetProperty("offset").GetInt64();
        var items = new StringBuilder();
        try
        {
            var d = await InfoAsync(id, revision);
            var (schema, dialect) = catalog.Resolve(d.Path ?? "");
            if (schema is not null && d.Length <= settings.MaxBytes && offset >= d.Bom && offset <= d.Length
                && await ReadAsync(d) is { } text && await ReadAsync(d, offset) is { } before
                && KeyCompletion.Offer(schema, dialect, text, before.Length, Options) is { } offer)
            {
                var bytes = ToBytes(d, text, [offer.Start, offer.End]);
                foreach (var key in offer.Keys)
                {
                    if (items.Length > 0) items.Append(',');
                    items.Append($"{{\"label\":\"{Quote(key)}\",\"detail\":\"{Quote(schema.Name)}\",\"kind\":\"property\",\"start\":{bytes[offer.Start]},\"end\":{bytes[offer.End]},\"text\":\"{Quote(offer.Insert(key))}\"}}");
                }
            }
        }
        catch (RpcException e) when (e.Code is DocumentErrorCodes.RevisionConflict or DocumentErrorCodes.InvalidRange) { items.Clear(); }
        return Encoding.UTF8.GetBytes($"{{\"revision\":{revision},\"items\":[{items}]}}");
    }

    /// <summary>Offers the editor to ask this plugin for completion; sent on every start, so a restarted process answers again.</summary>
    public async Task RegisterAsync() => await CallAsync(DocumentMethods.CompletionRegister, "{\"enabled\":true}");
}
