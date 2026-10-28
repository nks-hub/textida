using System.Text.Json;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Mcp;

/// <summary>
/// One connection of one client. A request that carries <c>_meta["io.modelcontextprotocol/protocolVersion"]</c> is served statelessly by the current revision; a client that opens with
/// <c>initialize</c> is served by the 2025-11-25 rules for the rest of the connection. The two eras never share state.
/// </summary>
internal sealed class McpSession(McpServer server, McpPeer? peer = null) : IRpcHandler
{
    public const int UnsupportedProtocolVersion = -32022;
    private const string VersionKey = "io.modelcontextprotocol/protocolVersion";
    private const string CapabilitiesKey = "io.modelcontextprotocol/clientCapabilities";
    private const string ServerInfoKey = "io.modelcontextprotocol/serverInfo";
    private const string SubscriptionKey = "io.modelcontextprotocol/subscriptionId";

    private static readonly string[] Supported = [McpServer.ModernVersion, McpServer.LegacyVersion];
    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    private readonly object _gate = new();
    private readonly List<RpcId> _toolSubscriptions = [];
    private JsonRpcDispatcher? _rpc;
    private volatile bool _legacy;

    public void Attach(JsonRpcDispatcher rpc) => _rpc = rpc;

    public void Close() => _ = _rpc?.DisposeAsync().AsTask();

    /// <summary>True once the client has opened with <c>initialize</c>; a notification sent earlier would reach a client that has not asked for the server yet.</summary>
    public bool IsInitialized => _legacy;

    public async Task SendNotificationAsync(string method, byte[] payload)
    {
        if (_rpc is not { IsClosed: false } rpc) return;
        try { await rpc.NotifyAsync(method, payload).ConfigureAwait(false); }
        catch (Exception e) when (e is IOException or OperationCanceledException) { /* the client is gone */ }
    }

    public async ValueTask<byte[]?> HandleRequestAsync(string method, JsonElement p, CancellationToken cancellation)
    {
        if (p.ValueKind != JsonValueKind.Object) p = EmptyObject;
        switch (method)
        {
            case "initialize": return Initialize(p);
            case "ping" when _legacy: return DocumentApi.Obj(_ => { });
            case "server/discover": return Discover(p);
            case "tools/list": return ListTools(p);
            case "tools/call": return await CallToolAsync(p, cancellation).ConfigureAwait(false);
            case "subscriptions/listen": return await ListenAsync(p, cancellation).ConfigureAwait(false);
            default: throw new RpcException(RpcErrorCodes.MethodNotFound, $"The method '{method}' is not supported.");
        }
    }

    public ValueTask HandleNotificationAsync(string method, JsonElement parameters)
    {
        if (method == "notifications/initialized") peer?.Ready();
        return ValueTask.CompletedTask;
    }

    // ---- eras ----

    private static RpcException InvalidParams(string message) => new(RpcErrorCodes.InvalidParams, message);

    /// <summary>True for a modern request (validated), false for a legacy one; throws what the specification says for a request that is neither.</summary>
    private bool IsModern(JsonElement p)
    {
        if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("_meta", out var meta) && meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty(VersionKey, out var version))
        {
            if (version.ValueKind != JsonValueKind.String) throw InvalidParams($"_meta['{VersionKey}'] must be a string.");
            var requested = version.GetString()!;
            if (!Supported.Contains(requested))
            {
                throw new RpcException(UnsupportedProtocolVersion, "Unsupported protocol version")
                {
                    DataJson = Encoded(w => { w.WriteStartArray("supported"); foreach (var v in Supported) w.WriteStringValue(v); w.WriteEndArray(); w.WriteString("requested", requested); }),
                };
            }
            if (!meta.TryGetProperty(CapabilitiesKey, out var caps) || caps.ValueKind != JsonValueKind.Object)
                throw InvalidParams($"_meta['{CapabilitiesKey}'] is required and must be an object.");
            return true;
        }
        if (_legacy) return false;
        throw InvalidParams($"The request has no _meta['{VersionKey}'] and no initialize came before it.");
    }

    private static string Encoded(Action<Utf8JsonWriter> body) => System.Text.Encoding.UTF8.GetString(DocumentApi.Obj(body));

    private void WriteServerInfo(Utf8JsonWriter w)
    {
        w.WriteString("name", server.Options.ServerName);
        w.WriteString("title", "Textida");
        w.WriteString("version", server.Options.ServerVersion);
    }

    private static void WriteCapabilities(Utf8JsonWriter w)
    {
        w.WriteStartObject("capabilities");
        w.WriteStartObject("tools");
        w.WriteBoolean("listChanged", true);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    /// <summary>Modern results carry <c>resultType</c>, and the server's identity in <c>_meta</c>; cacheable ones also say they must not be kept (the list changes when the user switches a tool).</summary>
    private byte[] Result(bool modern, bool cacheable, Action<Utf8JsonWriter> body) => DocumentApi.Obj(w =>
    {
        if (modern) w.WriteString("resultType", "complete");
        body(w);
        if (!modern) return;
        if (cacheable)
        {
            w.WriteNumber("ttlMs", 0);
            w.WriteString("cacheScope", "private");
        }
        w.WriteStartObject("_meta");
        w.WriteStartObject(ServerInfoKey);
        WriteServerInfo(w);
        w.WriteEndObject();
        w.WriteEndObject();
    });

    private byte[] Initialize(JsonElement p)
    {
        if (p.ValueKind != JsonValueKind.Object || !p.TryGetProperty("protocolVersion", out var v) || v.ValueKind != JsonValueKind.String)
            throw InvalidParams("initialize needs a protocolVersion. This server also speaks " + McpServer.ModernVersion + ", where every request carries its version in _meta.");
        _legacy = true;
        if (peer is not null)
        {
            string? name = null, version = null;
            if (p.TryGetProperty("clientInfo", out var info) && info.ValueKind == JsonValueKind.Object)
            {
                if (info.TryGetProperty("name", out var clientName) && clientName.ValueKind == JsonValueKind.String) name = clientName.GetString();
                if (info.TryGetProperty("version", out var clientVersion) && clientVersion.ValueKind == JsonValueKind.String) version = clientVersion.GetString();
            }
            peer.Identify(name, version);
        }
        return DocumentApi.Obj(w =>
        {
            w.WriteString("protocolVersion", McpServer.LegacyVersion);
            WriteCapabilities(w);
            w.WriteStartObject("serverInfo");
            WriteServerInfo(w);
            w.WriteEndObject();
            w.WriteString("instructions", McpServer.Instructions);
        });
    }

    private byte[] Discover(JsonElement p)
    {
        if (!IsModern(p)) throw new RpcException(RpcErrorCodes.MethodNotFound, "server/discover is a method of the " + McpServer.ModernVersion + " protocol and needs its _meta fields.");
        return Result(true, true, w =>
        {
            w.WriteStartArray("supportedVersions");
            foreach (var version in Supported) w.WriteStringValue(version);
            w.WriteEndArray();
            WriteCapabilities(w);
            w.WriteString("instructions", McpServer.Instructions);
        });
    }

    // ---- tools ----

    private byte[] ListTools(JsonElement p)
    {
        bool modern = IsModern(p);
        if (p.TryGetProperty("cursor", out var cursor) && cursor.ValueKind == JsonValueKind.String && cursor.GetString()!.Length > 0) throw InvalidParams("Invalid cursor");
        return Result(modern, true, w =>
        {
            w.WriteStartArray("tools");
            foreach (var tool in server.EnabledTools().Concat(peer?.ExtraTools ?? []))
            {
                w.WriteStartObject();
                w.WriteString("name", tool.Name);
                w.WriteString("title", tool.Title);
                w.WriteString("description", tool.Description);
                w.WritePropertyName("inputSchema");
                w.WriteRawValue(tool.InputSchema);
                w.WriteStartObject("annotations");
                w.WriteString("title", tool.Title);
                w.WriteBoolean("readOnlyHint", tool.ReadOnly);
                if (!tool.ReadOnly) w.WriteBoolean("destructiveHint", false);
                w.WriteBoolean("openWorldHint", false);
                w.WriteEndObject();
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });
    }

    private async Task<byte[]> CallToolAsync(JsonElement p, CancellationToken ct)
    {
        bool modern = IsModern(p);
        if (!p.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String) throw InvalidParams("Parameter 'name' must be a string.");
        var name = n.GetString()!;
        var tool = server.FindEnabled(name) ?? peer?.ExtraTools.FirstOrDefault(t => t.Name == name);
        if (tool is null)
        {
            if (peer?.Decline(name) is not { } declined) throw InvalidParams("Unknown tool: " + name);
            return Result(modern, false, w => WriteText(w, declined, failed: true));
        }
        var args = EmptyObject;
        if (p.TryGetProperty("arguments", out var a) && a.ValueKind != JsonValueKind.Null)
            args = a.ValueKind == JsonValueKind.Object ? a : throw InvalidParams("Parameter 'arguments' must be an object.");

        string text;
        bool failed = false;
        var context = new McpToolContext(server, ct, peer?.Scope);
        try { text = await tool.Run(context, args).ConfigureAwait(false); }
        catch (ToolArgumentException e)
        {
            if (peer is not null && peer.ExtraTools.Contains(tool)) peer.ReportIncompatible($"the agent called the tool '{name}' with arguments the bridge does not understand ({e.Message})");
            throw InvalidParams($"Invalid arguments for tool {name}: {e.Message}");
        }
        catch (ToolFailure e) { text = e.Message; failed = true; }
        catch (RpcException e) { text = McpToolContext.Describe(e); failed = true; }
        int length = text.Length + context.MoreText.Sum(t => t.Length);
        if (length > server.Options.MaxResultChars)
        {
            text = $"The result has {length} characters and the limit is {server.Options.MaxResultChars}. Ask for less: a smaller range, fewer results or a narrower pattern.";
            failed = true;
        }
        return Result(modern, false, w => WriteText(w, text, failed, failed ? [] : context.MoreText));
    }

    private static void WriteText(Utf8JsonWriter w, string text, bool failed, IReadOnlyList<string>? more = null)
    {
        w.WriteStartArray("content");
        foreach (var block in new[] { text }.Concat(more ?? []))
        {
            w.WriteStartObject();
            w.WriteString("type", "text");
            w.WriteString("text", block);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteBoolean("isError", failed);
    }

    // ---- subscriptions ----

    /// <summary>Holds the request open and sends <c>notifications/tools/list_changed</c> tagged with the id of this request, until the client cancels it.</summary>
    private async Task<byte[]> ListenAsync(JsonElement p, CancellationToken ct)
    {
        if (!IsModern(p)) throw new RpcException(RpcErrorCodes.MethodNotFound, "subscriptions/listen is a method of the " + McpServer.ModernVersion + " protocol and needs its _meta fields.");
        var id = RpcRequestContext.CurrentId ?? throw new RpcException(RpcErrorCodes.InternalError, "The request id is not known.");
        bool tools = p.TryGetProperty("notifications", out var filter) && filter.ValueKind == JsonValueKind.Object
            && filter.TryGetProperty("toolsListChanged", out var t) && t.ValueKind == JsonValueKind.True;
        await Notify("notifications/subscriptions/acknowledged", id, w =>
        {
            w.WriteStartObject("notifications");
            if (tools) w.WriteBoolean("toolsListChanged", true);
            w.WriteEndObject();
        }).ConfigureAwait(false);
        if (tools) lock (_gate) _toolSubscriptions.Add(id);
        try
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return [];
        }
        finally
        {
            lock (_gate) _toolSubscriptions.Remove(id);
        }
    }

    public void ToolsListChanged()
    {
        RpcId[] ids;
        lock (_gate) ids = [.. _toolSubscriptions];
        foreach (var id in ids) _ = Notify("notifications/tools/list_changed", id, _ => { });
    }

    private async Task Notify(string method, RpcId subscription, Action<Utf8JsonWriter> body)
    {
        if (_rpc is not { IsClosed: false } rpc) return;
        var payload = DocumentApi.Obj(w =>
        {
            w.WriteStartObject("_meta");
            if (subscription.Text is { } text) w.WriteString(SubscriptionKey, text); else w.WriteNumber(SubscriptionKey, subscription.Number);
            w.WriteEndObject();
            body(w);
        });
        try { await rpc.NotifyAsync(method, payload).ConfigureAwait(false); }
        catch (Exception e) when (e is IOException or OperationCanceledException) { /* the client is gone */ }
    }
}
