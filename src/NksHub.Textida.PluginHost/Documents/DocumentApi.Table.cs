using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Documents;

/// <summary>
/// The table API: the selected cells of a table view as a model (values, header, revision), and the clipboard written in more than one form.
/// A cell that touches a hidden value of a secrets file reads <c>*</c> unless the plugin was allowed to see secrets, as in every other read.
/// </summary>
public sealed partial class DocumentApi
{
    /// <summary>
    /// The cells selected in the table view, for the revision the plugin names. The result carries the same revision, the first selected column, the header
    /// of the selected columns (null without one), and per row its number and cells with their byte ranges. The size of all values is bound by the read limit.
    /// </summary>
    private async Task<byte[]> TableSelectionAsync(string pluginId, JsonElement p)
    {
        var e = Find(NeedDocumentId(p));
        long revision = NeedLong(p, "revision");
        return await _owner.InvokeAsync(() =>
        {
            var d = e.Doc;
            CheckCurrent(d, revision);
            var table = _options.Tools.TableSelection(d.Id) ?? throw Fail(DocumentErrorCodes.NoTableView, "The document is not shown as a table.");
            var hidden = Hidden(pluginId, d.Id, SecretsOf(d, d.GetSnapshot(), d.BomLength));
            long chars = 0;
            string Value(TableCell cell)
            {
                var value = hidden is not null && SecretValues.Overlaps(hidden, cell.Range.Start, cell.Range.End) ? "*" : cell.Value;
                chars += value.Length;
                if (chars > _options.MaxReadBytes) throw new RpcException(RpcErrorCodes.MessageTooLarge, $"A table selection holds at most {_options.MaxReadBytes} characters of values.");
                return value;
            }
            void Cell(Utf8JsonWriter w, TableCell cell)
            {
                w.WriteStartObject();
                w.WriteString("value", Value(cell));
                w.WriteNumber("start", cell.Range.Start);
                w.WriteNumber("end", cell.Range.End);
                w.WriteEndObject();
            }
            return Obj(w =>
            {
                w.WriteNumber("revision", d.Revision);
                w.WriteNumber("firstColumn", table.FirstColumn);
                w.WriteNumber("columns", table.Columns);
                if (table.Header is null) w.WriteNull("header");
                else
                {
                    w.WriteStartArray("header");
                    foreach (var cell in table.Header) Cell(w, cell);
                    w.WriteEndArray();
                }
                w.WriteStartArray("rows");
                foreach (var row in table.Rows)
                {
                    w.WriteStartObject();
                    w.WriteNumber("row", row.Number);
                    w.WriteStartArray("cells");
                    foreach (var cell in row.Cells) Cell(w, cell);
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            });
        });
    }

    /// <summary>Replaces the clipboard with <c>text</c> and, when sent, <c>html</c> (a fragment). Both are limited; the editor decides how the forms are put on the system clipboard.</summary>
    private async Task<byte[]> ClipboardWriteAsync(JsonElement p)
    {
        var e = Find(NeedDocumentId(p));
        var text = NeedString(p, "text");
        string? html = p.TryGetProperty("html", out var h) && h.ValueKind != JsonValueKind.Null
            ? h.ValueKind == JsonValueKind.String ? h.GetString() : throw Invalid("Parameter 'html' must be a string.")
            : null;
        if (text.Length > _options.MaxClipboardChars || html?.Length > _options.MaxClipboardChars)
            throw new RpcException(RpcErrorCodes.MessageTooLarge, $"The clipboard takes at most {_options.MaxClipboardChars} characters per form.");
        return await _owner.InvokeAsync(() =>
        {
            _options.Tools.SetClipboard(e.Doc.Id, text, html);
            return Obj(w => w.WriteBoolean("written", true));
        });
    }
}
