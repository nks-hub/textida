using System.Text.Json;

namespace NksHub.Textida.Rpc;

/// <summary>A JSON-RPC request id: either a number or a string. Ids of the two directions live in separate tables.</summary>
public readonly record struct RpcId(long Number, string? Text)
{
    public static RpcId FromNumber(long n) => new(n, null);
    public static RpcId FromText(string s) => new(0, s);
    public override string ToString() => Text ?? Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>JSON-RPC 2.0 error codes plus the implementation-defined quota codes (range -32000..-32099).</summary>
public static class RpcErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
    public const int RequestCancelled = -32800;
    public const int MessageTooLarge = -32011;
    public const int TooManyRequests = -32012;
    public const int TooManyResults = -32013;
    public const int SessionMemoryExceeded = -32014;
}

/// <summary>An error that travels as a JSON-RPC error object.</summary>
public class RpcException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
    public RpcId? Id { get; init; }
    /// <summary>Raw JSON of the error "data" member, if the peer sent one.</summary>
    public string? DataJson { get; init; }
}

/// <summary>The byte stream can no longer be split into frames; the connection is unusable.</summary>
public sealed class RpcFramingException(string message) : IOException(message);

public sealed class RpcConnectionClosedException(Exception? cause)
    : IOException("The JSON-RPC connection is closed" + (cause is null ? "." : ": " + cause.Message), cause);

public sealed class RpcTimeoutException(string method, TimeSpan? timeout)
    : TimeoutException($"No response to '{method}' within {timeout}.");

/// <summary>
/// Quota values. There are deliberately no defaults: the numbers are an owner decision (TODO P3b.1.4, "Před zahájením").
/// </summary>
/// <param name="MaxMessageBytes">Largest single message in either direction.</param>
/// <param name="MaxConcurrentIncoming">Requests from the peer handled at the same time; more are answered with <see cref="RpcErrorCodes.TooManyRequests"/>.</param>
/// <param name="MaxPendingOutgoing">Our requests awaiting an answer; further callers wait (back-pressure).</param>
/// <param name="MaxResultItems">Largest array in a result.</param>
/// <param name="MaxSessionBytes">Bytes of peer messages being processed at the same time.</param>
/// <param name="MaxOutgoingQueueBytes">Bytes queued for writing; further senders wait (back-pressure).</param>
public sealed record RpcLimits(
    int MaxMessageBytes,
    int MaxConcurrentIncoming,
    int MaxPendingOutgoing,
    int MaxResultItems,
    long MaxSessionBytes,
    long MaxOutgoingQueueBytes)
{
    internal void Validate()
    {
        if (MaxMessageBytes < 1 || MaxConcurrentIncoming < 1 || MaxPendingOutgoing < 1 || MaxResultItems < 1
            || MaxSessionBytes < 1 || MaxOutgoingQueueBytes < 1)
            throw new ArgumentException("Every RPC limit must be positive.");
    }
}

/// <summary>The id of the request a handler is serving, for handlers that must name it in what they send back (an MCP subscription is identified by the id of its request).</summary>
public static class RpcRequestContext
{
    private static readonly AsyncLocal<RpcId?> Current = new();

    public static RpcId? CurrentId
    {
        get => Current.Value;
        internal set => Current.Value = value;
    }
}

/// <summary>Receiver of what the peer sends. Requests run concurrently, notifications one after another in arrival order.</summary>
public interface IRpcHandler
{
    /// <summary>Returns the raw JSON result (null means JSON null). Throw <see cref="RpcException"/> for a protocol error.</summary>
    ValueTask<byte[]?> HandleRequestAsync(string method, JsonElement parameters, CancellationToken cancellation);
    ValueTask HandleNotificationAsync(string method, JsonElement parameters);
}
