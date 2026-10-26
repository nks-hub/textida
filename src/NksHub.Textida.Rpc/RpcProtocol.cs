using System.Text.Json;

namespace NksHub.Textida.Rpc;

/// <summary>
/// What differs between protocols that share JSON-RPC 2.0: framing, how a request is cancelled and how progress is reported.
/// A dispatcher is bound to exactly one protocol, so a message of one protocol is never built from another one's rules.
/// </summary>
public abstract class RpcProtocol
{
    public abstract string Name { get; }
    public abstract RpcFraming Framing { get; }
    /// <summary>Whether a request cancelled by the peer still gets a response (MCP says it should not).</summary>
    public virtual bool RespondsToCancelled => true;

    public abstract string CancelMethod { get; }
    public abstract byte[] CancelParams(RpcId id);
    /// <summary>Recognises this protocol's cancel notification and extracts the id of the request to cancel.</summary>
    public abstract bool TryParseCancel(string method, JsonElement parameters, out RpcId id);

    public abstract string ProgressMethod { get; }
    /// <summary>Recognises this protocol's progress notification and extracts its token and payload.</summary>
    public abstract bool TryParseProgress(string method, JsonElement parameters, out string token, out JsonElement value);

    internal static bool ReadId(JsonElement e, out RpcId id)
    {
        if (e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var n)) { id = RpcId.FromNumber(n); return true; }
        if (e.ValueKind == JsonValueKind.String) { id = RpcId.FromText(e.GetString()!); return true; }
        id = default;
        return false;
    }

    internal static byte[] IdObject(string member, RpcId id)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            if (id.Text is null) w.WriteNumber(member, id.Number); else w.WriteString(member, id.Text);
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    internal bool TryCancelById(string method, string expectedMethod, string member, JsonElement p, out RpcId id)
    {
        id = default;
        return method == expectedMethod && p.ValueKind == JsonValueKind.Object && p.TryGetProperty(member, out var e) && ReadId(e, out id);
    }

    internal static bool TryProgress(string method, string expectedMethod, string tokenMember, string valueMember, JsonElement p, out string token, out JsonElement value)
    {
        token = "";
        value = default;
        if (method != expectedMethod || p.ValueKind != JsonValueKind.Object || !p.TryGetProperty(tokenMember, out var t)) return false;
        token = t.ValueKind == JsonValueKind.String ? t.GetString()! : t.ValueKind == JsonValueKind.Number ? t.GetRawText() : "";
        if (token.Length == 0) return false;
        value = p.TryGetProperty(valueMember, out var v) ? v : default;
        return true;
    }
}

/// <summary>LSP: Content-Length framing, <c>$/cancelRequest</c>, <c>$/progress</c>.</summary>
public sealed class LspProtocol : RpcProtocol
{
    public override string Name => "lsp";
    public override RpcFraming Framing { get; } = new ContentLengthFraming();
    public override string CancelMethod => "$/cancelRequest";
    public override byte[] CancelParams(RpcId id) => IdObject("id", id);
    public override bool TryParseCancel(string method, JsonElement p, out RpcId id) => TryCancelById(method, CancelMethod, "id", p, out id);
    public override string ProgressMethod => "$/progress";
    public override bool TryParseProgress(string method, JsonElement p, out string token, out JsonElement value) =>
        TryProgress(method, ProgressMethod, "token", "value", p, out token, out value);
}

/// <summary>MCP over stdio: newline-delimited messages, <c>notifications/cancelled</c> (the receiver sends no answer), <c>notifications/progress</c>.</summary>
public sealed class McpProtocol : RpcProtocol
{
    public override string Name => "mcp";
    public override RpcFraming Framing { get; } = new LineFraming();
    public override bool RespondsToCancelled => false;
    public override string CancelMethod => "notifications/cancelled";
    public override byte[] CancelParams(RpcId id) => IdObject("requestId", id);
    public override bool TryParseCancel(string method, JsonElement p, out RpcId id) => TryCancelById(method, CancelMethod, "requestId", p, out id);
    public override string ProgressMethod => "notifications/progress";
    public override bool TryParseProgress(string method, JsonElement p, out string token, out JsonElement value) =>
        TryProgress(method, ProgressMethod, "progressToken", "progress", p, out token, out value);
}

/// <summary>
/// ACP over stdio: newline-delimited messages. A turn is cancelled with <c>session/cancel</c> (an ordinary notification for the handler);
/// a single request is cancelled with <c>$/cancel_request</c>. Progress of a turn arrives as <c>session/update</c>.
/// </summary>
public sealed class AcpProtocol : RpcProtocol
{
    public const string SessionCancelMethod = "session/cancel";

    public override string Name => "acp";
    public override RpcFraming Framing { get; } = new LineFraming();
    public override string CancelMethod => "$/cancel_request";
    public override byte[] CancelParams(RpcId id) => IdObject("requestId", id);
    public override bool TryParseCancel(string method, JsonElement p, out RpcId id) => TryCancelById(method, CancelMethod, "requestId", p, out id);
    public override string ProgressMethod => "session/update";
    public override bool TryParseProgress(string method, JsonElement p, out string token, out JsonElement value) =>
        TryProgress(method, ProgressMethod, "sessionId", "update", p, out token, out value);
}

/// <summary>Plugin protocol: newline-delimited messages, <c>$/cancel</c> and <c>$/progress</c>; the handshake is defined by the plugin host.</summary>
public sealed class PluginProtocol : RpcProtocol
{
    public override string Name => "plugin";
    public override RpcFraming Framing { get; } = new LineFraming();
    public override string CancelMethod => "$/cancel";
    public override byte[] CancelParams(RpcId id) => IdObject("id", id);
    public override bool TryParseCancel(string method, JsonElement p, out RpcId id) => TryCancelById(method, CancelMethod, "id", p, out id);
    public override string ProgressMethod => "$/progress";
    public override bool TryParseProgress(string method, JsonElement p, out string token, out JsonElement value) =>
        TryProgress(method, ProgressMethod, "token", "value", p, out token, out value);
}

public static class RpcProtocols
{
    public static readonly RpcProtocol Lsp = new LspProtocol();
    public static readonly RpcProtocol Mcp = new McpProtocol();
    public static readonly RpcProtocol Acp = new AcpProtocol();
    public static readonly RpcProtocol Plugin = new PluginProtocol();
}
