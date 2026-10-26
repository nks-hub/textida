using System.Buffers;
using System.Text.Json;

namespace NksHub.Textida.Rpc;

public enum RpcKind { Request, Notification, Response, ErrorResponse }

/// <summary>One parsed JSON-RPC 2.0 message. Batches are not supported and are rejected as invalid requests.</summary>
public sealed class RpcMessage : IDisposable
{
    readonly JsonDocument _doc;

    RpcMessage(JsonDocument doc) => _doc = doc;

    public RpcKind Kind { get; private set; }
    public RpcId? Id { get; private set; }
    public string? Method { get; private set; }
    public JsonElement Params { get; private set; }
    public JsonElement Result { get; private set; }
    public int ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public JsonElement ErrorData { get; private set; }
    public int Size { get; private set; }

    public void Dispose() => _doc.Dispose();

    /// <summary>
    /// Finds the id and whether a "method" member exists in the first bytes of a message that is too big to parse.
    /// Only members that come before the cut-off are seen; the serializer here writes the id first.
    /// </summary>
    public static bool TryPeekId(ReadOnlySpan<byte> prefix, out RpcId id, out bool hasMethod)
    {
        id = default;
        hasMethod = false;
        var found = false;
        var reader = new Utf8JsonReader(prefix, isFinalBlock: false, default);
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                if (!reader.Read()) break;
                if (name == "method") hasMethod = true;
                else if (name == "id" && reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var n)) { id = RpcId.FromNumber(n); found = true; }
                else if (name == "id" && reader.TokenType == JsonTokenType.String) { id = RpcId.FromText(reader.GetString()!); found = true; }
                else if (!reader.TrySkip()) break;
            }
        }
        catch (JsonException) { }
        return found;
    }

    public static RpcMessage Parse(byte[] utf8)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(utf8); }
        catch (JsonException) { throw new RpcException(RpcErrorCodes.ParseError, "The message is not valid JSON."); }

        RpcId? id = null;
        try
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new RpcException(RpcErrorCodes.InvalidRequest, "Only single JSON-RPC objects are supported; batches are not.");
            if (!root.TryGetProperty("jsonrpc", out var version) || version.ValueKind != JsonValueKind.String || version.GetString() != "2.0")
                throw new RpcException(RpcErrorCodes.InvalidRequest, "Missing or wrong \"jsonrpc\" version.");

            if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind != JsonValueKind.Null)
            {
                if (idEl.ValueKind == JsonValueKind.Number && idEl.TryGetInt64(out var n)) id = RpcId.FromNumber(n);
                else if (idEl.ValueKind == JsonValueKind.String) id = RpcId.FromText(idEl.GetString()!);
                else throw new RpcException(RpcErrorCodes.InvalidRequest, "The id must be an integer or a string.");
            }

            var msg = new RpcMessage(doc) { Id = id, Size = utf8.Length };
            if (root.TryGetProperty("method", out var method))
            {
                if (method.ValueKind != JsonValueKind.String) throw new RpcException(RpcErrorCodes.InvalidRequest, "The method must be a string.");
                msg.Method = method.GetString();
                msg.Kind = id is null ? RpcKind.Notification : RpcKind.Request;
                if (root.TryGetProperty("params", out var p)) msg.Params = p;
            }
            else if (root.TryGetProperty("result", out var result))
            {
                if (id is null) throw new RpcException(RpcErrorCodes.InvalidRequest, "A response needs an id.");
                msg.Kind = RpcKind.Response;
                msg.Result = result;
            }
            else if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                msg.Kind = RpcKind.ErrorResponse;
                msg.ErrorCode = error.TryGetProperty("code", out var c) && c.TryGetInt32(out var code) ? code : RpcErrorCodes.InternalError;
                msg.ErrorMessage = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : "";
                if (error.TryGetProperty("data", out var d)) msg.ErrorData = d;
            }
            else throw new RpcException(RpcErrorCodes.InvalidRequest, "Not a request, notification or response.");
            return msg;
        }
        catch (RpcException e)
        {
            doc.Dispose();
            throw new RpcException(e.Code, e.Message) { Id = id };
        }
    }
}

/// <summary>Serialises messages with Utf8JsonWriter; raw JSON arguments are re-emitted compactly, which also keeps newlines out of line-framed output.</summary>
public static class RpcWire
{
    static byte[] Build(Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", "2.0");
            body(w);
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    static void WriteId(Utf8JsonWriter w, RpcId? id)
    {
        if (id is not { } v) w.WriteNull("id");
        else if (v.Text is not null) w.WriteString("id", v.Text);
        else w.WriteNumber("id", v.Number);
    }

    static void WriteRaw(Utf8JsonWriter w, string name, ReadOnlyMemory<byte> json)
    {
        using var d = JsonDocument.Parse(json);
        w.WritePropertyName(name);
        d.RootElement.WriteTo(w);
    }

    public static byte[] Request(RpcId id, string method, ReadOnlyMemory<byte> paramsJson = default) => Build(w =>
    {
        WriteId(w, id);
        w.WriteString("method", method);
        if (!paramsJson.IsEmpty) WriteRaw(w, "params", paramsJson);
    });

    public static byte[] Notification(string method, ReadOnlyMemory<byte> paramsJson = default) => Build(w =>
    {
        w.WriteString("method", method);
        if (!paramsJson.IsEmpty) WriteRaw(w, "params", paramsJson);
    });

    /// <summary>Success response. An array result longer than <paramref name="maxItems"/> throws <see cref="RpcErrorCodes.TooManyResults"/>.</summary>
    public static byte[] Result(RpcId id, ReadOnlyMemory<byte> resultJson, int maxItems) => Build(w =>
    {
        WriteId(w, id);
        if (resultJson.IsEmpty) { w.WriteNull("result"); return; }
        using var d = JsonDocument.Parse(resultJson);
        if (d.RootElement.ValueKind == JsonValueKind.Array && d.RootElement.GetArrayLength() > maxItems)
            throw new RpcException(RpcErrorCodes.TooManyResults, $"The result has {d.RootElement.GetArrayLength()} items; the limit is {maxItems}.");
        w.WritePropertyName("result");
        d.RootElement.WriteTo(w);
    });

    public static byte[] Error(RpcId? id, int code, string message) => Build(w =>
    {
        WriteId(w, id);
        w.WriteStartObject("error");
        w.WriteNumber("code", code);
        w.WriteString("message", message);
        w.WriteEndObject();
    });
}
