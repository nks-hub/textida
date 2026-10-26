using System.Text;
using System.Text.Json;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugins.Json;

/// <summary>Limits of the formatter; the editor passes both on the command line, so no number is fixed in the plugin.</summary>
/// <param name="MaxBytes">Largest range (in bytes of the document) that is formatted; larger input is refused with a message.</param>
public sealed record FormatSettings(long MaxBytes, int Indent);

/// <summary>
/// "Format JSON" through the document API: reads the selections (or the whole document) at the revision the command was started on,
/// formats each, and sends all ranges in one batch, so the result is one Undo step. With <c>preview</c> the batch is proposed instead of applied.
/// Nothing is changed unless every range is valid JSON.
/// </summary>
public sealed class FormatCommand(JsonRpcDispatcher rpc, FormatSettings settings)
{
    public const string Format = "json.format";
    public const string Preview = "json.preview";

    private sealed record Range(long Start, long End);

    public async Task<string> RunAsync(JsonElement p)
    {
        var messages = Messages.For(p.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString()! : "en");
        var preview = p.GetProperty("command").GetString() == Preview;
        var document = p.GetProperty("document").GetString()!;
        var revision = p.GetProperty("revision").GetInt64();
        try
        {
            var selections = await CallAsync(DocumentMethods.Selections, $"{{\"document\":\"{document}\"}}");
            if (selections.GetProperty("revision").GetInt64() != revision) return await ReportAsync("stale", NoticeLevel.Warning, messages.Stale);
            var info = await CallAsync(DocumentMethods.Info, $"{{\"document\":\"{document}\"}}");
            long bom = info.GetProperty("bomLength").GetInt64(), length = info.GetProperty("byteLength").GetInt64();

            var ranges = selections.GetProperty("selections").EnumerateArray()
                .Select(s => (A: s.GetProperty("anchor").GetInt64(), C: s.GetProperty("caret").GetInt64()))
                .Where(s => s.A != s.C).Select(s => new Range(Math.Min(s.A, s.C), Math.Max(s.A, s.C))).OrderBy(r => r.Start).ToList();
            if (ranges.Count == 0) ranges.Add(new Range(bom, length));
            for (int i = 1; i < ranges.Count; i++)
                if (ranges[i].Start < ranges[i - 1].End) return await ReportAsync("failed", NoticeLevel.Error, messages.Overlap);

            var oversized = ranges.FirstOrDefault(r => r.End - r.Start > settings.MaxBytes);
            if (oversized is not null) return await ReportAsync("tooLarge", NoticeLevel.Error, messages.TooLarge(oversized.End - oversized.Start, settings.MaxBytes));

            var edits = new StringBuilder();
            int changed = 0;
            for (int i = 0; i < ranges.Count; i++)
            {
                var r = ranges[i];
                var read = await CallAsync(DocumentMethods.ReadText, $"{{\"document\":\"{document}\",\"revision\":{revision},\"start\":{r.Start},\"end\":{r.End}}}");
                if (read.GetProperty("invalid").GetArrayLength() > 0) return await ReportAsync("failed", NoticeLevel.Error, messages.InvalidBytes(i + 1, ranges.Count));
                var text = read.GetProperty("text").GetString()!;
                if (!JsonFormatter.TryFormat(text, NewLineOf(text), settings.Indent, out var formatted, out var error))
                    return await ReportAsync("syntaxError", NoticeLevel.Error, messages.Syntax(i + 1, ranges.Count, error));
                if (formatted == text) continue;
                if (changed++ > 0) edits.Append(',');
                edits.Append($"{{\"start\":{r.Start},\"end\":{r.End},\"text\":{JsonSerializer.Serialize(formatted, PluginJsonStrings.Default.String)}}}");
            }
            if (changed == 0) return await ReportAsync("unchanged", NoticeLevel.Info, messages.Unchanged);

            if (preview)
            {
                await CallAsync(DocumentMethods.EditPropose, $"{{\"document\":\"{document}\",\"revision\":{revision},\"title\":{JsonSerializer.Serialize(messages.Title, PluginJsonStrings.Default.String)},\"edits\":[{edits}]}}");
                return "proposed";
            }
            await CallAsync(DocumentMethods.EditApply, $"{{\"document\":\"{document}\",\"revision\":{revision},\"batchId\":\"{Guid.NewGuid():N}\",\"edits\":[{edits}]}}");
            return await ReportAsync("applied", NoticeLevel.Info, messages.Done(changed));
        }
        catch (RpcException e) when (e.Code == DocumentErrorCodes.RevisionConflict)
        {
            return await ReportAsync("stale", NoticeLevel.Warning, messages.Stale);
        }
        catch (RpcException e) when (e.Code == RpcErrorCodes.MessageTooLarge)
        {
            return await ReportAsync("tooLarge", NoticeLevel.Error, messages.TooLarge(settings.MaxBytes + 1, settings.MaxBytes));
        }
        catch (RpcException e)
        {
            return await ReportAsync("failed", NoticeLevel.Error, e.Message);
        }
    }

    /// <summary>The first line ending of the text, so the result looks like the range it replaces; a line feed when the text has none.</summary>
    private static string NewLineOf(string text)
    {
        int i = text.AsSpan().IndexOfAny('\r', '\n');
        if (i < 0 || text[i] == '\n') return "\n";
        return i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : "\r";
    }

    private async Task<JsonElement> CallAsync(string method, string paramsJson) => (await rpc.RequestAsync(method, Encoding.UTF8.GetBytes(paramsJson))).Clone();

    private async Task<string> ReportAsync(string status, NoticeLevel level, string message)
    {
        var json = $"{{\"level\":\"{level.ToString().ToLowerInvariant()}\",\"message\":{JsonSerializer.Serialize(message, PluginJsonStrings.Default.String)}}}";
        await rpc.RequestAsync(UiMethods.Notify, Encoding.UTF8.GetBytes(json));
        return status;
    }
}
