using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugins.Json;

/// <summary>Reads a document of the editor through the document API: its facts, its text at one revision, and the byte offsets of character offsets in that text.</summary>
internal sealed class DocumentAccess(JsonRpcDispatcher rpc)
{
    internal sealed record Doc(string Id, long Revision, string? Path, TextCodec Codec, int Bom, long Length);

    public async Task<JsonElement> CallAsync(string method, string paramsJson) => (await rpc.RequestAsync(method, Encoding.UTF8.GetBytes(paramsJson))).Clone();

    public async Task<Doc> InfoAsync(string id, long revision)
    {
        var info = await CallAsync(DocumentMethods.Info, $"{{\"document\":\"{id}\"}}");
        if (info.GetProperty("revision").GetInt64() != revision) throw new RpcException(DocumentErrorCodes.RevisionConflict, "Stale.");
        var path = info.GetProperty("path") is { ValueKind: JsonValueKind.String } p ? p.GetString() : null;
        return new Doc(id, revision, path, TextCodec.ByName(info.GetProperty("codec").GetString()!) ?? TextCodec.Utf8, (int)info.GetProperty("bomLength").GetInt64(), info.GetProperty("byteLength").GetInt64());
    }

    /// <summary>The text of the whole file, or null when it holds bytes the encoding cannot read (then nothing is checked).</summary>
    public async Task<string?> ReadAsync(Doc d, long? end = null)
    {
        var read = await CallAsync(DocumentMethods.ReadText, $"{{\"document\":\"{d.Id}\",\"revision\":{d.Revision},\"start\":{d.Bom},\"end\":{end ?? d.Length}}}");
        return read.GetProperty("invalid").GetArrayLength() > 0 ? null : read.GetProperty("text").GetString()!;
    }

    /// <summary>Byte offsets (from the start of the file, BOM included) of character offsets of <paramref name="text"/>, by character offset.</summary>
    public static Dictionary<int, long> ToBytes(Doc d, string text, IEnumerable<int> chars)
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

    public static string Quote(string text) => JsonEncodedText.Encode(text).ToString();
}
