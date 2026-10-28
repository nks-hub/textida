using System.Globalization;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Compare;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Services;

/// <summary>What the editor tells a plugin when a button of its comparison tab is pressed, and when such a press is out of date (see <see cref="DiffAction"/>).</summary>
internal static class DiffActionPayload
{
    /// <summary>
    /// The <c>diff</c> member of the command request, written as JSON that starts with a comma: the key of the comparison, the number of the hunk as the tab shows it, and for both sides the hunk's lines
    /// and byte range (and the document and revision of a side that is a document). The ranges have the shape <c>diff/compute</c> answers with.
    /// </summary>
    public static string Json(DiffRequest request, TextDiff diff, int hunk)
    {
        var h = diff.Hunks[hunk];
        var json = new StringBuilder(",\"diff\":{");
        if (request.Key is { } key) json.Append("\"key\":\"").Append(JsonEncodedText.Encode(key).ToString()).Append("\",");
        json.Append(CultureInfo.InvariantCulture, $"\"hunk\":{hunk},\"hunkCount\":{diff.Hunks.Count},");
        Side(json, "left", request.Left, diff.A, h.AStart, h.ACount).Append(',');
        return Side(json, "right", request.Right, diff.B, h.BStart, h.BCount).Append('}').ToString();
    }

    /// <summary>The <c>diff</c> member of the command that refreshes the comparison: only its key, or nothing when it has none.</summary>
    public static string RefreshJson(DiffRequest request) =>
        request.Key is { } key ? $",\"diff\":{{\"key\":\"{JsonEncodedText.Encode(key)}\"}}" : "";

    private static StringBuilder Side(StringBuilder json, string name, DiffInput input, LineIndex index, long first, long lines)
    {
        json.Append('"').Append(name).Append("\":{");
        if (!input.Detached) json.Append(CultureInfo.InvariantCulture, $"\"document\":\"{input.Snapshot.DocumentId}\",\"revision\":{input.Snapshot.Revision},");
        return json.Append(CultureInfo.InvariantCulture, $"\"startLine\":{first},\"lines\":{lines},\"start\":{index.OffsetOf(first)},\"end\":{index.OffsetOf(first + lines)}}}");
    }

    /// <summary>Whether the file a side was read from is not as it was then (changed, replaced or gone).</summary>
    public static bool FileChanged(DiffFile file)
    {
        var info = new FileInfo(file.Path);
        return !info.Exists || info.Length != file.Length || info.LastWriteTimeUtc != file.WriteTimeUtc;
    }
}
