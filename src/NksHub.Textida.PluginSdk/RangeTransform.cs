using System.Text;
using System.Text.Json;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginSdk;

/// <summary>What a transform made of one range: the new text, or the reason it cannot (<see cref="Error"/>, shown to the user). <see cref="Diagnostics"/> are lines for the output window.</summary>
public sealed record RangeResult(string? Text, string? Error, IReadOnlyList<string>? Diagnostics = null)
{
    public static RangeResult Changed(string text, IReadOnlyList<string>? diagnostics = null) => new(text, null, diagnostics);
    public static RangeResult Failed(string error, IReadOnlyList<string>? diagnostics = null) => new(null, error, diagnostics);
}

/// <summary>How a range command ended. <see cref="Status"/> is one of <c>applied</c>, <c>proposed</c>, <c>unchanged</c>, <c>stale</c>, <c>tooLarge</c> or <c>failed</c>.</summary>
public sealed record RangeRun(string Status, IReadOnlyList<string> Diagnostics);

/// <summary>
/// The flow of a command that rewrites the selections (the whole document when nothing is selected) with a function of the text: reads the ranges at the revision the
/// command started on, transforms each, and sends all new texts in one batch, so the result is one undo step; with <c>preview</c> the batch is proposed instead of applied.
/// Nothing is changed unless every range could be transformed, and no byte outside the ranges is touched.
/// </summary>
public static class RangeTransform
{
    private sealed record Range(long Start, long End);

    /// <param name="maxBytes">Largest range that is transformed; a larger one is refused with a message.</param>
    /// <param name="title">Title of the proposal in the change preview.</param>
    /// <param name="transform">Called with the text of a range, the line ending to use, the 1-based number of the range and how many there are.</param>
    public static async Task<RangeRun> RunAsync(JsonRpcDispatcher rpc, JsonElement p, long maxBytes, string title, bool preview,
        Func<string, string, int, int, Task<RangeResult>> transform, CancellationToken ct)
    {
        string language = p.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString()! : "en";
        string document = p.GetProperty("document").GetString()!;
        long revision = p.GetProperty("revision").GetInt64();
        var diagnostics = new List<string>();

        async Task<JsonElement> Call(string method, string json) => (await rpc.RequestAsync(method, Encoding.UTF8.GetBytes(json), null, ct)).Clone();
        async Task<RangeRun> Report(string status, NoticeLevel level, string message)
        {
            await Call(UiMethods.Notify, $"{{\"level\":\"{level.ToString().ToLowerInvariant()}\",\"message\":\"{JsonEncodedText.Encode(message)}\"}}");
            return new RangeRun(status, diagnostics);
        }

        try
        {
            var selections = await Call(DocumentMethods.Selections, $"{{\"document\":\"{document}\"}}");
            if (selections.GetProperty("revision").GetInt64() != revision) return await Report("stale", NoticeLevel.Warning, RangeTexts.Get(language, "stale"));
            var info = await Call(DocumentMethods.Info, $"{{\"document\":\"{document}\"}}");
            long bom = info.GetProperty("bomLength").GetInt64(), length = info.GetProperty("byteLength").GetInt64();

            var ranges = selections.GetProperty("selections").EnumerateArray()
                .Select(s => (A: s.GetProperty("anchor").GetInt64(), C: s.GetProperty("caret").GetInt64()))
                .Where(s => s.A != s.C).Select(s => new Range(Math.Min(s.A, s.C), Math.Max(s.A, s.C))).OrderBy(r => r.Start).ToList();
            if (ranges.Count == 0) ranges.Add(new Range(bom, length));
            for (int i = 1; i < ranges.Count; i++)
                if (ranges[i].Start < ranges[i - 1].End) return await Report("failed", NoticeLevel.Error, RangeTexts.Get(language, "overlap"));
            if (ranges.FirstOrDefault(r => r.End - r.Start > maxBytes) is { } big)
                return await Report("tooLarge", NoticeLevel.Error, RangeTexts.Get(language, "tooLarge", ("size", (big.End - big.Start).ToString()), ("max", maxBytes.ToString())));

            var edits = new StringBuilder();
            int changed = 0;
            for (int i = 0; i < ranges.Count; i++)
            {
                var r = ranges[i];
                string prefix = ranges.Count > 1 ? RangeTexts.Get(language, "range", ("n", (i + 1).ToString()), ("count", ranges.Count.ToString())) + " " : "";
                var read = await Call(DocumentMethods.ReadText, $"{{\"document\":\"{document}\",\"revision\":{revision},\"start\":{r.Start},\"end\":{r.End}}}");
                if (read.GetProperty("invalid").GetArrayLength() > 0) return await Report("failed", NoticeLevel.Error, prefix + RangeTexts.Get(language, "invalidBytes"));
                var text = read.GetProperty("text").GetString()!;
                var result = await transform(text, NewLineOf(text), i + 1, ranges.Count);
                if (result.Diagnostics is not null) diagnostics.AddRange(result.Diagnostics);
                if (result.Text is null) return await Report("failed", NoticeLevel.Error, prefix + result.Error);
                if (result.Text == text) continue;
                if (changed++ > 0) edits.Append(',');
                edits.Append($"{{\"start\":{r.Start},\"end\":{r.End},\"text\":\"{JsonEncodedText.Encode(result.Text)}\"}}");
            }
            if (changed == 0) return await Report("unchanged", NoticeLevel.Info, RangeTexts.Get(language, "unchanged"));

            if (preview)
            {
                await Call(DocumentMethods.EditPropose, $"{{\"document\":\"{document}\",\"revision\":{revision},\"title\":\"{JsonEncodedText.Encode(title)}\",\"edits\":[{edits}]}}");
                return new RangeRun("proposed", diagnostics);
            }
            await Call(DocumentMethods.EditApply, $"{{\"document\":\"{document}\",\"revision\":{revision},\"batchId\":\"{Guid.NewGuid():N}\",\"edits\":[{edits}]}}");
            return await Report("applied", NoticeLevel.Info, changed == 1 ? RangeTexts.Get(language, "doneOne") : RangeTexts.Get(language, "doneMany", ("count", changed.ToString())));
        }
        catch (RpcException e) when (e.Code == DocumentErrorCodes.RevisionConflict)
        {
            return await Report("stale", NoticeLevel.Warning, RangeTexts.Get(language, "stale"));
        }
        catch (RpcException e) when (e.Code == RpcErrorCodes.MessageTooLarge)
        {
            return await Report("tooLarge", NoticeLevel.Error, RangeTexts.Get(language, "tooLarge", ("size", (maxBytes + 1).ToString()), ("max", maxBytes.ToString())));
        }
        catch (RpcException e)
        {
            return await Report("failed", NoticeLevel.Error, e.Message);
        }
    }

    /// <summary>The first line ending of the text, so the result looks like the range it replaces; a line feed when the text has none.</summary>
    public static string NewLineOf(string text)
    {
        int i = text.AsSpan().IndexOfAny('\r', '\n');
        if (i < 0 || text[i] == '\n') return "\n";
        return i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : "\r";
    }
}

/// <summary>The messages of <see cref="RangeTransform"/> in the languages of the editor (cs, en, sk, de). Placeholders are written <c>{name}</c>.</summary>
public static class RangeTexts
{
    // columns: cs, en, sk, de
    private static readonly Dictionary<string, string[]> Table = new()
    {
        ["stale"] = ["Dokument se mezitím změnil. Spusťte příkaz znovu.", "The document changed in the meantime. Run the command again.", "Dokument sa medzitým zmenil. Spustite príkaz znova.", "Das Dokument hat sich inzwischen geändert. Führen Sie den Befehl erneut aus."],
        ["unchanged"] = ["Text už je v požadované podobě.", "The text is already in the wanted form.", "Text je už v požadovanej podobe.", "Der Text hat bereits die gewünschte Form."],
        ["overlap"] = ["Označené části se překrývají.", "The selected ranges overlap.", "Označené časti sa prekrývajú.", "Die markierten Bereiche überlappen sich."],
        ["doneOne"] = ["Hotovo.", "Done.", "Hotovo.", "Fertig."],
        ["doneMany"] = ["Změněno částí: {count}.", "{count} ranges changed.", "Zmenených častí: {count}.", "{count} Bereiche geändert."],
        ["tooLarge"] = ["Text má {size} B, limit je {max} B. Označte menší část.", "The text is {size} bytes; the limit is {max} bytes. Select a smaller part.", "Text má {size} B, limit je {max} B. Označte menšiu časť.", "Der Text hat {size} Bytes, das Limit ist {max} Bytes. Markieren Sie einen kleineren Teil."],
        ["invalidBytes"] = ["Text obsahuje bajty, které nejsou v kódování souboru platné. Nic se nezměnilo.", "The text contains bytes that are not valid in the encoding of the file. Nothing was changed.", "Text obsahuje bajty, ktoré nie sú v kódovaní súboru platné. Nič sa nezmenilo.", "Der Text enthält Bytes, die in der Dateikodierung nicht gültig sind. Nichts wurde geändert."],
        ["range"] = ["Část {n} z {count}:", "Range {n} of {count}:", "Časť {n} z {count}:", "Bereich {n} von {count}:"],
    };

    private static int Column(string language) => language switch { "cs" => 0, "sk" => 2, "de" => 3, _ => 1 };

    public static string Get(string language, string key, params (string Name, string Value)[] arguments)
    {
        var text = Table[key][Column(language)];
        foreach (var (name, value) in arguments) text = text.Replace("{" + name + "}", value);
        return text;
    }
}
