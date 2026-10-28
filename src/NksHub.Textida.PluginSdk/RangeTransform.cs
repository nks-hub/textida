using System.Text.Json;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginSdk;

/// <summary>
/// What a transform made of one range: the new text, or the reason it cannot (<see cref="Error"/>, shown to the user). <see cref="Diagnostics"/> are lines for the output window.
/// <see cref="FailStatus"/> is the status of the run when the transform failed.
/// </summary>
public sealed record RangeResult(string? Text, string? Error, IReadOnlyList<string>? Diagnostics = null, string FailStatus = "failed")
{
    public static RangeResult Changed(string text, IReadOnlyList<string>? diagnostics = null) => new(text, null, diagnostics);
    public static RangeResult Failed(string error, IReadOnlyList<string>? diagnostics = null, string status = "failed") => new(null, error, diagnostics, status);
}

/// <summary>How a range command ended. <see cref="Status"/> is one of <c>applied</c>, <c>proposed</c>, <c>unchanged</c>, <c>stale</c>, <c>tooLarge</c> or <c>failed</c>, or the status a failed transform named.</summary>
public sealed record RangeRun(string Status, IReadOnlyList<string> Diagnostics);

/// <summary>
/// One range handed to a transform. <paramref name="NewLine"/> is the line ending to write: the first one inside the range, else the one most lines of the document use, else a line feed.
/// <paramref name="Number"/> counts from 1 out of <paramref name="Count"/>; <paramref name="Start"/> and <paramref name="End"/> are the bytes of the range in the document.
/// </summary>
public sealed record RangeContext(string Text, string NewLine, int Number, int Count, long Start, long End);

/// <summary>
/// The flow of a command that rewrites the selections (the whole document when nothing is selected) with a function of the text: reads the ranges at the revision the
/// command started on, transforms each, and sends all new texts in one batch, so the result is one undo step; with <c>preview</c> the batch is proposed instead of applied.
/// Nothing is changed unless every range could be transformed, and no byte outside the ranges is touched, so the encoding and the line endings elsewhere stay as they are.
/// </summary>
public static class RangeTransform
{
    /// <param name="maxBytes">Largest range that is transformed; a larger one is refused with a message.</param>
    /// <param name="title">Title of the proposal in the change preview.</param>
    /// <param name="transform">Called with the text of a range, the line ending to use, the 1-based number of the range and how many there are.</param>
    public static Task<RangeRun> RunAsync(JsonRpcDispatcher rpc, JsonElement p, long maxBytes, string title, bool preview,
        Func<string, string, int, int, Task<RangeResult>> transform, CancellationToken ct) =>
        RunAsync(rpc, CommandContext.From(p), maxBytes, title, preview, range => transform(range.Text, range.NewLine, range.Number, range.Count), ct);

    /// <param name="texts">Messages of this command in place of the standard ones, by the keys of <see cref="RangeTexts"/> (columns cs, en, sk, de).</param>
    public static async Task<RangeRun> RunAsync(JsonRpcDispatcher rpc, CommandContext command, long maxBytes, string title, bool preview,
        Func<RangeContext, Task<RangeResult>> transform, CancellationToken ct, IReadOnlyDictionary<string, string[]>? texts = null)
    {
        var document = DocumentClient.For(rpc, command);
        var diagnostics = new List<string>();
        string Text(string key, params (string Name, string Value)[] arguments) => RangeTexts.Get(texts, command.Language, key, arguments);
        async Task<RangeRun> Report(string status, NoticeLevel level, string message)
        {
            await DocumentClient.NotifyAsync(rpc, level, message, ct).ConfigureAwait(false);
            return new RangeRun(status, diagnostics);
        }

        try
        {
            var selections = await document.SelectionsAsync(ct).ConfigureAwait(false);
            var info = await document.InfoAsync(ct).ConfigureAwait(false);
            var ranges = selections.Where(s => !s.IsEmpty).Select(s => (s.Start, s.End)).OrderBy(r => r.Start).ToList();
            if (ranges.Count == 0) ranges.Add((info.BomLength, info.ByteLength));
            for (int i = 1; i < ranges.Count; i++)
                if (ranges[i].Start < ranges[i - 1].End) return await Report("failed", NoticeLevel.Error, Text("overlap")).ConfigureAwait(false);
            if (ranges.Where(r => r.End - r.Start > maxBytes).Select(r => (long?)(r.End - r.Start)).FirstOrDefault() is { } tooBig)
                return await Report("tooLarge", NoticeLevel.Error, Text("tooLarge", ("size", tooBig.ToString()), ("max", maxBytes.ToString()))).ConfigureAwait(false);

            var edits = new List<RangeEdit>();
            string? documentLineEnding = null;
            bool asked = false;
            for (int i = 0; i < ranges.Count; i++)
            {
                var (start, end) = ranges[i];
                string prefix = ranges.Count > 1 ? Text("range", ("n", (i + 1).ToString()), ("count", ranges.Count.ToString())) + " " : "";
                var read = await document.ReadTextAsync(start, end, ct).ConfigureAwait(false);
                if (read.HasInvalid) return await Report("failed", NoticeLevel.Error, prefix + Text("invalidBytes")).ConfigureAwait(false);
                var newLine = LineEndingIn(read.Text);
                if (newLine is null)
                {
                    // a range without a line ending takes the one of the document, so a one-line selection in a CRLF file does not bring in a line feed
                    if (!asked) { documentLineEnding = await document.DominantLineEndingAsync(ct).ConfigureAwait(false); asked = true; }
                    newLine = documentLineEnding ?? "\n";
                }
                var result = await transform(new RangeContext(read.Text, newLine, i + 1, ranges.Count, start, end)).ConfigureAwait(false);
                if (result.Diagnostics is not null) diagnostics.AddRange(result.Diagnostics);
                if (result.Text is null) return await Report(result.FailStatus, NoticeLevel.Error, prefix + result.Error).ConfigureAwait(false);
                if (result.Text != read.Text) edits.Add(new RangeEdit(start, end, result.Text));
            }
            if (edits.Count == 0) return await Report("unchanged", NoticeLevel.Info, Text("unchanged")).ConfigureAwait(false);

            if (preview)
            {
                await document.ProposeAsync(title, edits, ct).ConfigureAwait(false);
                return new RangeRun("proposed", diagnostics);
            }
            await document.ApplyAsync(edits, null, ct).ConfigureAwait(false);
            return await Report("applied", NoticeLevel.Info, edits.Count == 1 ? Text("doneOne") : Text("doneMany", ("count", edits.Count.ToString()))).ConfigureAwait(false);
        }
        catch (RpcException e) when (e.Code == DocumentErrorCodes.RevisionConflict)
        {
            return await Report("stale", NoticeLevel.Warning, Text("stale")).ConfigureAwait(false);
        }
        catch (RpcException e) when (e.Code == RpcErrorCodes.MessageTooLarge)
        {
            return await Report("tooLarge", NoticeLevel.Error, Text("tooLarge", ("size", (maxBytes + 1).ToString()), ("max", maxBytes.ToString()))).ConfigureAwait(false);
        }
        catch (RpcException e)
        {
            return await Report("failed", NoticeLevel.Error, e.Message).ConfigureAwait(false);
        }
    }

    /// <summary>The first line ending of the text (<c>"\r\n"</c>, <c>"\n"</c> or <c>"\r"</c>), or null when it has none.</summary>
    public static string? LineEndingIn(string text)
    {
        int i = text.AsSpan().IndexOfAny('\r', '\n');
        if (i < 0) return null;
        if (text[i] == '\n') return "\n";
        return i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : "\r";
    }

    /// <summary>The first line ending of the text, so the result looks like the range it replaces; a line feed when the text has none.</summary>
    public static string NewLineOf(string text) => LineEndingIn(text) ?? "\n";
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

    public static string Get(string language, string key, params (string Name, string Value)[] arguments) => Get(null, language, key, arguments);

    /// <summary>The message <paramref name="key"/>; a command that words it differently passes its own table in <paramref name="overrides"/>, which wins over the standard one.</summary>
    public static string Get(IReadOnlyDictionary<string, string[]>? overrides, string language, string key, params (string Name, string Value)[] arguments)
    {
        var text = (overrides is not null && overrides.TryGetValue(key, out var own) ? own : Table[key])[Column(language)];
        foreach (var (name, value) in arguments) text = text.Replace("{" + name + "}", value);
        return text;
    }
}
