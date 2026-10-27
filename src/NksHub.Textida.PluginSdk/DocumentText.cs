using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginSdk;

/// <summary>A document read whole at one revision. <paramref name="Bom"/> bytes of byte order mark come before the text; offsets of the document API count them.</summary>
/// <param name="Truncated">Only the first <c>maxBytes</c> were read because the caller allowed a partial read.</param>
public sealed record LoadedText(string Text, DecodedText Decoded, int Bom, long Length, bool Truncated = false)
{
    /// <summary>The byte offset in the document of a UTF-16 offset in <see cref="Text"/>.</summary>
    public long ByteOffset(int charIndex) => Bom + Decoded.ByteOffsetOf(charIndex);
}

/// <summary>The document is larger than the plugin reads whole.</summary>
public sealed class DocumentTooLargeException(long size) : Exception
{
    public long Size { get; } = size;
}

public static class DocumentText
{
    /// <summary>
    /// Reads a document in chunks of <paramref name="chunkBytes"/> at <paramref name="revision"/> and decodes it. Throws an <see cref="RpcException"/> with
    /// <see cref="DocumentErrorCodes.RevisionConflict"/> when the document has moved on, and <see cref="DocumentTooLargeException"/> above <paramref name="maxBytes"/>,
    /// unless <paramref name="allowPartial"/> asks for the first <paramref name="maxBytes"/> only.
    /// </summary>
    public static async Task<LoadedText> LoadAsync(JsonRpcDispatcher rpc, string document, long revision, int chunkBytes, long maxBytes, CancellationToken ct, bool allowPartial = false)
    {
        async Task<JsonElement> Call(string method, string json) => (await rpc.RequestAsync(method, Encoding.UTF8.GetBytes(json), null, ct).ConfigureAwait(false)).Clone();

        var info = await Call(DocumentMethods.Info, $"{{\"document\":\"{document}\"}}").ConfigureAwait(false);
        if (info.GetProperty("revision").GetInt64() != revision) throw new RpcException(DocumentErrorCodes.RevisionConflict, "Stale.");
        long length = info.GetProperty("byteLength").GetInt64();
        int bom = (int)info.GetProperty("bomLength").GetInt64();
        var codec = TextCodec.ByName(info.GetProperty("codec").GetString()!) ?? TextCodec.Utf8;
        long size = length - bom;
        bool truncated = size > maxBytes;
        if (truncated && !allowPartial) throw new DocumentTooLargeException(size);
        if (truncated) size = maxBytes;
        var bytes = new byte[size];
        for (long at = 0; at < size;)
        {
            int count = (int)Math.Min(chunkBytes, size - at);
            var read = await Call(DocumentMethods.ReadBytes, $"{{\"document\":\"{document}\",\"revision\":{revision},\"start\":{bom + at},\"end\":{bom + at + count}}}").ConfigureAwait(false);
            read.GetProperty("bytes").GetBytesFromBase64().CopyTo(bytes.AsSpan((int)at));
            at += count;
        }
        var decoded = codec.WithBom(false).Decode(bytes);
        return new LoadedText(decoded.Text, decoded, bom, length, truncated);
    }
}
