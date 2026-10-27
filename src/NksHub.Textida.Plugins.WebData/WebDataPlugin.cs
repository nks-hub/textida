using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Colors;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugins.WebData;

/// <summary>Limits of the plugin; the editor passes them on the command line, so no number is fixed in the plugin.</summary>
/// <param name="ChunkBytes">Bytes read from the document per request; must not exceed what the document API returns in one read.</param>
/// <param name="MaxBytes">Largest document the plugin reads whole (colour conversion and key paths work on the text).</param>
/// <param name="MaxSymbols">Keys published as symbols of one document; the editor refuses more.</param>
/// <param name="MaxSymbolChars">Longest key path published as a symbol; the editor refuses longer.</param>
public sealed record WebDataSettings(int ChunkBytes, int MaxBytes, int MaxSymbols, int MaxSymbolChars);

/// <summary>
/// The built-in web data tools: conversion of the colour values in a selection or at the caret between hex, rgb(), hsl() and oklch() (F086), and the key paths of
/// YAML and JSON files (F212): the path of the key at a position, the keys as symbols for the outline and Go to Anything, and the comparison of the keys of two files.
/// Every answer names the revision it was computed for. Commands arrive as <c>command/execute</c>; the answer is a JSON object with a <c>status</c>.
/// </summary>
public sealed class WebDataPlugin : PluginBase
{
    public const string ColorHex = "color.hex";
    public const string ColorRgb = "color.rgb";
    public const string ColorHsl = "color.hsl";
    public const string ColorOklch = "color.oklch";
    public const string KeyAt = "keypath.at";
    public const string KeyIndex = "keypath.index";
    public const string KeyCompare = "keypath.compare";

    private readonly WebDataSettings _settings;

    public WebDataPlugin(WebDataSettings settings) : base("web-data", apiVersion: 1)
    {
        _settings = settings;
        On("command/execute", ExecuteAsync);
    }

    private sealed record Context(string Document, long Revision, string Language, JsonElement Parameters);

    private async ValueTask<byte[]?> ExecuteAsync(JsonElement p, CancellationToken ct)
    {
        var command = p.GetProperty("command").GetString();
        var context = new Context(p.GetProperty("document").GetString()!, p.GetProperty("revision").GetInt64(),
            p.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString()! : "en", p);
        bool quiet = command == KeyAt;
        string result;
        try
        {
            result = command switch
            {
                ColorHex => await RunColorAsync(context, ColorFormat.Hex, ct),
                ColorRgb => await RunColorAsync(context, ColorFormat.Rgb, ct),
                ColorHsl => await RunColorAsync(context, ColorFormat.Hsl, ct),
                ColorOklch => await RunColorAsync(context, ColorFormat.Oklch, ct),
                KeyAt => await RunKeyAtAsync(context, ct),
                KeyIndex => await RunIndexAsync(context, ct),
                KeyCompare => await RunCompareAsync(context, ct),
                _ => throw new RpcException(RpcErrorCodes.InvalidParams, "Unknown command."),
            };
        }
        catch (RpcException e) when (e.Code == DocumentErrorCodes.RevisionConflict)
        {
            result = await ReportAsync("stale", NoticeLevel.Warning, Texts.Get(context.Language, "stale"), quiet, ct);
        }
        catch (RpcException e) when (e.Code is DocumentErrorCodes.SnapshotExpired or DocumentErrorCodes.UnknownDocument or DocumentErrorCodes.InvalidRange)
        {
            result = await ReportAsync("failed", NoticeLevel.Error, e.Message, quiet, ct);
        }
        catch (DocumentTooLargeException e)
        {
            var text = Texts.Get(context.Language, "tooLarge", ("size", e.Size.ToString()), ("max", _settings.MaxBytes.ToString()));
            result = await ReportAsync("tooLarge", NoticeLevel.Error, text, quiet, ct);
        }
        return Encoding.UTF8.GetBytes(result);
    }

    // ---- editor calls ----

    private async Task<JsonElement> CallAsync(string method, string paramsJson, CancellationToken ct) =>
        (await Rpc.RequestAsync(method, Encoding.UTF8.GetBytes(paramsJson), null, ct)).Clone();

    private static string Quote(string text) => JsonEncodedText.Encode(text).ToString();

    private static string Status(string status, string extra = "") => $"{{\"status\":\"{status}\"{extra}}}";

    /// <summary>Shows the message (unless the command is one that runs on its own, like the status path) and answers with the status.</summary>
    private async Task<string> ReportAsync(string status, NoticeLevel level, string message, bool quiet, CancellationToken ct)
    {
        if (!quiet) await CallAsync(UiMethods.Notify, $"{{\"level\":\"{level.ToString().ToLowerInvariant()}\",\"message\":\"{Quote(message)}\"}}", ct);
        return Status(status);
    }

    private Task<LoadedText> LoadAsync(string document, long revision, CancellationToken ct) =>
        DocumentText.LoadAsync(Rpc, document, revision, _settings.ChunkBytes, _settings.MaxBytes, ct);

    // ---- colours ----

    private async Task<string> RunColorAsync(Context c, ColorFormat format, CancellationToken ct)
    {
        var loaded = await LoadAsync(c.Document, c.Revision, ct);
        var selections = await CallAsync(DocumentMethods.Selections, $"{{\"document\":\"{c.Document}\"}}", ct);
        if (selections.GetProperty("revision").GetInt64() != c.Revision) throw new RpcException(DocumentErrorCodes.RevisionConflict, "Stale.");

        var all = ColorLiterals.Find(loaded.Text);
        var chosen = new SortedDictionary<int, ColorMatch>();
        foreach (var s in selections.GetProperty("selections").EnumerateArray())
        {
            long a = s.GetProperty("anchor").GetInt64(), caret = s.GetProperty("caret").GetInt64();
            int from = loaded.Decoded.CharIndexOf(Math.Max(0, Math.Min(a, caret) - loaded.Bom)), to = loaded.Decoded.CharIndexOf(Math.Max(0, Math.Max(a, caret) - loaded.Bom));
            foreach (var m in all)
                if (from == to ? m.Start <= from && from <= m.End : m.Start >= from && m.End <= to) chosen[m.Start] = m;
        }
        if (chosen.Count == 0) return await ReportAsync("noColor", NoticeLevel.Warning, Texts.Get(c.Language, "color.none"), false, ct);

        var edits = new StringBuilder();
        int changed = 0;
        foreach (var m in chosen.Values)
        {
            string text = ColorLiterals.Format(m.Color, format, m.UpperHex);
            if (text == loaded.Text.Substring(m.Start, m.Length)) continue;
            if (changed++ > 0) edits.Append(',');
            edits.Append($"{{\"start\":{loaded.Bom + loaded.Decoded.ByteOffsetOf(m.Start)},\"end\":{loaded.Bom + loaded.Decoded.ByteOffsetOf(m.End)},\"text\":\"{Quote(text)}\"}}");
        }
        if (changed == 0) return await ReportAsync("unchanged", NoticeLevel.Info, Texts.Get(c.Language, "color.unchanged"), false, ct);
        await CallAsync(DocumentMethods.EditApply, $"{{\"document\":\"{c.Document}\",\"revision\":{c.Revision},\"batchId\":\"{Guid.NewGuid():N}\",\"edits\":[{edits}]}}", ct);
        var done = changed == 1 ? Texts.Get(c.Language, "color.doneOne") : Texts.Get(c.Language, "color.doneMany", ("count", changed.ToString()));
        return await ReportAsync("applied", NoticeLevel.Info, done, false, ct);
    }

    // ---- key paths ----

    private static KeyParse ParseKeys(string syntax, string text) => syntax == "json" ? JsonKeys.Parse(text) : YamlKeys.Parse(text);

    private static string Syntax(JsonElement p, string name) => p.TryGetProperty(name, out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()! : "yaml";

    private static int LineOf(string text, int offset)
    {
        int line = 1;
        for (int i = 0; i < Math.Min(offset, text.Length); i++)
            if (text[i] == '\n') line++;
        return line;
    }

    private async Task<string> RunKeyAtAsync(Context c, CancellationToken ct)
    {
        var loaded = await LoadAsync(c.Document, c.Revision, ct);
        long offset = c.Parameters.GetProperty("offset").GetInt64();
        var parse = ParseKeys(Syntax(c.Parameters, "syntax"), loaded.Text);
        var node = parse.At(loaded.Decoded.CharIndexOf(Math.Max(0, offset - loaded.Bom)));
        if (node is null) return Status("none", $",\"revision\":{c.Revision}");
        long start = loaded.Bom + loaded.Decoded.ByteOffsetOf(node.KeyStart), end = loaded.Bom + loaded.Decoded.ByteOffsetOf(node.KeyEnd);
        return Status("ok", $",\"revision\":{c.Revision},\"path\":\"{Quote(node.Path)}\",\"start\":{start},\"end\":{end}");
    }

    /// <summary>Publishes the keys of the revision as the symbols of the document: the editor lists them in the outline and in Go to Anything and jumps to the bytes of the key.</summary>
    private async Task<string> RunIndexAsync(Context c, CancellationToken ct)
    {
        var loaded = await LoadAsync(c.Document, c.Revision, ct);
        var parse = ParseKeys(Syntax(c.Parameters, "syntax"), loaded.Text);
        var ids = new Dictionary<KeyNode, string>();
        var used = new HashSet<string>();
        var symbols = new StringBuilder();
        int count = 0;
        bool truncated = false;
        foreach (var node in parse.Nodes)
        {
            if (node.Path.Length > _settings.MaxSymbolChars || (node.Parent is not null && !ids.ContainsKey(node.Parent))) continue;
            if (count == _settings.MaxSymbols) { truncated = true; break; }
            string id = node.Path;
            for (int n = 2; !used.Add(id); n++) id = node.Path + "#" + n;
            ids[node] = id;
            if (count++ > 0) symbols.Append(',');
            symbols.Append($"{{\"id\":\"{Quote(id)}\",\"name\":\"{Quote(node.Path)}\",\"kind\":\"key\"");
            if (node.Parent is not null) symbols.Append($",\"parent\":\"{Quote(ids[node.Parent])}\"");
            symbols.Append($",\"start\":{loaded.Bom + loaded.Decoded.ByteOffsetOf(node.KeyStart)},\"end\":{loaded.Bom + loaded.Decoded.ByteOffsetOf(node.KeyEnd)}}}");
        }
        await CallAsync(DocumentMethods.SymbolsPublish, $"{{\"document\":\"{c.Document}\",\"revision\":{c.Revision},\"symbols\":[{symbols}]}}", ct);
        if (truncated) await CallAsync(UiMethods.Notify, $"{{\"level\":\"warning\",\"message\":\"{Quote(Texts.Get(c.Language, "keys.tooMany", ("max", _settings.MaxSymbols.ToString())))}\"}}", ct);
        else if (parse.ErrorAt is { } at)
            await CallAsync(UiMethods.Notify, $"{{\"level\":\"warning\",\"message\":\"{Quote(Texts.Get(c.Language, "keys.syntax", ("line", LineOf(loaded.Text, at).ToString())))}\"}}", ct);
        return Status("indexed", $",\"revision\":{c.Revision},\"count\":{count},\"truncated\":{(truncated ? "true" : "false")}");
    }

    /// <summary>The keys that hold a value in one file and are missing in the other, each file against the revision it was read in.</summary>
    private async Task<string> RunCompareAsync(Context c, CancellationToken ct)
    {
        string other = c.Parameters.GetProperty("other").GetString()!;
        long otherRevision = c.Parameters.GetProperty("otherRevision").GetInt64();
        var first = await LoadAsync(c.Document, c.Revision, ct);
        var second = await LoadAsync(other, otherRevision, ct);
        var a = ParseKeys(Syntax(c.Parameters, "syntax"), first.Text);
        var b = ParseKeys(Syntax(c.Parameters, "otherSyntax"), second.Text);
        var inA = a.Leaves();
        var inB = b.Leaves();
        string List(KeyParse from, HashSet<string> missingFrom) =>
            string.Join(',', from.Nodes.Where(n => !n.HasChildren && !missingFrom.Contains(n.Path)).Select(n => n.Path).Distinct().Select(p => $"\"{Quote(p)}\""));
        int errorA = a.ErrorAt is { } ea ? LineOf(first.Text, ea) : 0, errorB = b.ErrorAt is { } eb ? LineOf(second.Text, eb) : 0;
        return Status("compared", $",\"missingInOther\":[{List(a, inB)}],\"missingInDocument\":[{List(b, inA)}],\"errorLine\":{errorA},\"otherErrorLine\":{errorB}");
    }
}
