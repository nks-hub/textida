using System.Text.Json;

namespace NksHub.Textida.Rpc;

/// <summary>A JSON-RPC request id: either a number or a string. Ids of the two directions live in separate tables.</summary>
public readonly record struct RpcId(long Number, string? Text)
{
    /// <summary>An id that is a number.</summary>
    public static RpcId FromNumber(long n) => new(n, null);
    /// <summary>An id that is a string.</summary>
    public static RpcId FromText(string s) => new(0, s);
    /// <summary>The id as it is written in the message.</summary>
    public override string ToString() => Text ?? Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// JSON-RPC 2.0 error codes plus the implementation-defined codes of the shared layer (-32011..-32019). The document API uses -32020..-32049, the UI API -32050..-32059,
/// the HTTP transport of the MCP client -32061..-32062; no number is used twice (a test reads the constants and checks it).
/// </summary>
public static class RpcErrorCodes
{
    /// <summary>The message is not valid JSON.</summary>
    public const int ParseError = -32700;
    /// <summary>The message is not a valid JSON-RPC request.</summary>
    public const int InvalidRequest = -32600;
    /// <summary>The peer has no such method.</summary>
    public const int MethodNotFound = -32601;
    /// <summary>The parameters are missing, of a wrong type or out of range.</summary>
    public const int InvalidParams = -32602;
    /// <summary>The handler failed in a way the caller cannot fix.</summary>
    public const int InternalError = -32603;
    /// <summary>The request was cancelled by the peer.</summary>
    public const int RequestCancelled = -32800;
    /// <summary>A message is larger than the limit of the connection.</summary>
    public const int MessageTooLarge = -32011;
    /// <summary>The peer has as many requests running as the limit of the connection allows.</summary>
    public const int TooManyRequests = -32012;
    /// <summary>A result holds more items than the limit of the connection allows.</summary>
    public const int TooManyResults = -32013;
    /// <summary>The messages being processed hold more memory than the limit of the connection allows.</summary>
    public const int SessionMemoryExceeded = -32014;
    /// <summary>A handler gave up waiting (a <see cref="TimeoutException"/>, such as <see cref="RpcTimeoutException"/> of a call it forwarded); the peer is told this instead of an internal error.</summary>
    public const int RequestTimeout = -32015;
}

/// <summary>An error that travels as a JSON-RPC error object.</summary>
public class RpcException(int code, string message) : Exception(message)
{
    /// <summary>The JSON-RPC error code (<see cref="RpcErrorCodes"/>, or the codes of an API).</summary>
    public int Code { get; } = code;
    /// <summary>The id of the request the error answers, when it came from a peer.</summary>
    public RpcId? Id { get; init; }
    /// <summary>Raw JSON of the error "data" member, if the peer sent one.</summary>
    public string? DataJson { get; init; }
}

/// <summary>The byte stream can no longer be split into frames; the connection is unusable.</summary>
public sealed class RpcFramingException(string message) : IOException(message);

/// <summary>The connection ended before the answer came; the call may or may not have had its effect.</summary>
public sealed class RpcConnectionClosedException(Exception? cause)
    : IOException("The JSON-RPC connection is closed" + (cause is null ? "." : ": " + cause.Message), cause);

/// <summary>The peer did not answer a request within its timeout.</summary>
public sealed class RpcTimeoutException(string method, TimeSpan? timeout)
    : TimeoutException($"No response to '{method}' within {timeout}.");

/// <summary>
/// Quota values. There are deliberately no defaults: the numbers are an owner decision (TODO P3b.1.4, "Před zahájením").
/// </summary>
/// <param name="MaxMessageBytes">Largest single message in either direction.</param>
/// <param name="MaxConcurrentIncoming">Requests from the peer handled at the same time; more are answered with <see cref="RpcErrorCodes.TooManyRequests"/>.</param>
/// <param name="MaxPendingOutgoing">Our requests awaiting an answer; further callers wait (back-pressure) until their timeout.</param>
/// <param name="MaxResultItems">Largest array in a result.</param>
/// <param name="MaxSessionBytes">Bytes of peer messages being processed at the same time.</param>
/// <param name="MaxOutgoingQueueBytes">
/// Bytes queued for writing; further senders, and answers to the peer, wait (back-pressure). Errors and cancels owed to the peer do not wait;
/// when those alone exceed it, the peer sends without reading and the connection ends with <see cref="RpcErrorCodes.SessionMemoryExceeded"/>.
/// </param>
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

    /// <summary>The id of the request being served on this logical thread of control, or null outside a handler.</summary>
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
    /// <summary>Handles a notification; no answer goes back.</summary>
    ValueTask HandleNotificationAsync(string method, JsonElement parameters);
}
