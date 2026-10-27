using System.Text;
using System.Text.Json;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugins.CodeExplorer;

/// <summary>Limits of the plugin; the editor passes them on the command line, so no number is fixed in the plugin.</summary>
/// <param name="ChunkBytes">Bytes read from the document per request; must not exceed what the document API returns in one read.</param>
/// <param name="MaxBytes">Largest document the plugin reads whole.</param>
/// <param name="MaxSymbols">Symbols published for one document; the editor refuses more.</param>
/// <param name="MaxSymbolChars">Longest symbol name published; the editor refuses longer.</param>
/// <param name="MatchTimeoutMs">Longest time one rule may spend matching a text; a text that exceeds it gets no outline.</param>
public sealed record CodeExplorerSettings(int ChunkBytes, int MaxBytes, int MaxSymbols, int MaxSymbolChars, int MatchTimeoutMs);

/// <summary>
/// The built-in Code Explorer: the functions, classes and headings of the open file as the symbols of the outline and of Go to Anything, from simple per-language rules (no language
/// server, no project index). The answer names the revision the symbols were read for; the editor shows them as current only while the document is still at that revision.
/// </summary>
public sealed class CodeExplorerPlugin : PluginBase
{
    public const string Index = "outline.index";

    private readonly CodeExplorerSettings _settings;

    public CodeExplorerPlugin(CodeExplorerSettings settings) : base("code-explorer", apiVersion: 1)
    {
        _settings = settings;
        AppContext.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromMilliseconds(settings.MatchTimeoutMs)); // read when the first regex of the outline rules is built
        On("command/execute", ExecuteAsync);
    }

    private static string Quote(string text) => JsonEncodedText.Encode(text).ToString();

    private static string Status(string status, string extra = "") => $"{{\"status\":\"{status}\"{extra}}}";

    private async Task NotifyAsync(NoticeLevel level, string message, CancellationToken ct) =>
        await Rpc.RequestAsync(UiMethods.Notify, Encoding.UTF8.GetBytes($"{{\"level\":\"{level.ToString().ToLowerInvariant()}\",\"message\":\"{Quote(message)}\"}}"), null, ct);

    private async ValueTask<byte[]?> ExecuteAsync(JsonElement p, CancellationToken ct)
    {
        if (p.GetProperty("command").GetString() != Index) throw new RpcException(RpcErrorCodes.InvalidParams, "Unknown command.");
        string document = p.GetProperty("document").GetString()!, language = p.GetProperty("outlineLanguage").GetString()!;
        long revision = p.GetProperty("revision").GetInt64();
        string ui = p.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString()! : "en";
        bool notify = p.TryGetProperty("notify", out var n) && n.ValueKind == JsonValueKind.True;
        bool partial = p.TryGetProperty("partial", out var part) && part.ValueKind == JsonValueKind.True;
        string result;
        try
        {
            result = await IndexAsync(document, revision, language, ui, notify, partial, ct);
        }
        catch (RpcException e) when (e.Code == DocumentErrorCodes.RevisionConflict)
        {
            result = Status("stale"); // the editor asks again for the revision it has now
        }
        catch (RpcException e) when (e.Code is DocumentErrorCodes.SnapshotExpired or DocumentErrorCodes.UnknownDocument or DocumentErrorCodes.InvalidRange)
        {
            result = Status("failed");
        }
        catch (DocumentTooLargeException e)
        {
            if (notify) await NotifyAsync(NoticeLevel.Error, Texts.Get(ui, "tooLarge", ("size", e.Size.ToString()), ("max", _settings.MaxBytes.ToString())), ct);
            result = Status("tooLarge");
        }
        return Encoding.UTF8.GetBytes(result);
    }

    private async Task<string> IndexAsync(string document, long revision, string language, string ui, bool notify, bool partial, CancellationToken ct)
    {
        if (!Outlines.Supports(language)) return Status("unsupported");
        var loaded = await DocumentText.LoadAsync(Rpc, document, revision, _settings.ChunkBytes, _settings.MaxBytes, ct, allowPartial: partial);
        var found = Outlines.Extract(language, loaded.Text);
        bool truncated = found.Count > _settings.MaxSymbols;
        var symbols = new StringBuilder();
        for (int k = 0; k < Math.Min(found.Count, _settings.MaxSymbols); k++)
        {
            var s = found[k];
            string name = s.Name.Length <= _settings.MaxSymbolChars ? s.Name : s.Name[..(char.IsHighSurrogate(s.Name[_settings.MaxSymbolChars - 1]) ? _settings.MaxSymbolChars - 1 : _settings.MaxSymbolChars)];
            if (k > 0) symbols.Append(',');
            symbols.Append($"{{\"id\":\"{k}\",\"name\":\"{Quote(name)}\",\"kind\":\"{Quote(s.Kind)}\"");
            if (s.Parent >= 0) symbols.Append($",\"parent\":\"{s.Parent}\"");
            symbols.Append($",\"start\":{loaded.ByteOffset(s.Start)},\"end\":{loaded.ByteOffset(s.End)}}}");
        }
        await Rpc.RequestAsync(DocumentMethods.SymbolsPublish, Encoding.UTF8.GetBytes($"{{\"document\":\"{document}\",\"revision\":{revision},\"symbols\":[{symbols}]}}"), null, ct);
        if (truncated && notify) await NotifyAsync(NoticeLevel.Warning, Texts.Get(ui, "tooMany", ("max", _settings.MaxSymbols.ToString())), ct);
        if (loaded.Truncated && notify) await NotifyAsync(NoticeLevel.Warning, Texts.Get(ui, "partial", ("max", _settings.MaxBytes.ToString())), ct);
        return Status("indexed", $",\"revision\":{revision},\"count\":{Math.Min(found.Count, _settings.MaxSymbols)},\"truncated\":{(truncated || loaded.Truncated ? "true" : "false")}");
    }
}
