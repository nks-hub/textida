using System.Text.Json;
using System.Threading.Channels;

namespace NksHub.Textida.Rpc;

/// <summary>
/// One JSON-RPC 2.0 connection. Reads and writes run in the background from construction until the peer closes the stream,
/// the stream breaks or the dispatcher is disposed. Framing, cancel and progress rules come from the <see cref="RpcProtocol"/>.
/// </summary>
public sealed class JsonRpcDispatcher : IAsyncDisposable
{
    readonly FrameReader _reader;
    readonly Stream _output;
    readonly RpcProtocol _protocol;
    readonly RpcLimits _limits;
    readonly IRpcHandler _handler;
    readonly object _gate = new();
    readonly Dictionary<RpcId, TaskCompletionSource<RpcMessage>> _pending = new();
    readonly Dictionary<RpcId, CancellationTokenSource> _incoming = new();
    readonly SemaphoreSlim _pendingSlots;
    readonly ByteBudget _outBudget;
    readonly Channel<(byte[] Payload, bool Budgeted)> _writes = Channel.CreateUnbounded<(byte[], bool)>(new() { SingleReader = true });
    readonly Channel<RpcMessage> _notifications = Channel.CreateUnbounded<RpcMessage>(new() { SingleReader = true });
    readonly CancellationTokenSource _lifetime = new();
    readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Task _readLoop, _writeLoop, _notifyLoop;
    long _nextId;
    long _inboundBytes;
    int _closed;
    int _lateResponses;

    public JsonRpcDispatcher(Stream input, Stream output, RpcProtocol protocol, RpcLimits limits, IRpcHandler handler)
    {
        limits.Validate();
        _reader = new FrameReader(input);
        _output = output;
        _protocol = protocol;
        _limits = limits;
        _handler = handler;
        _pendingSlots = new SemaphoreSlim(limits.MaxPendingOutgoing);
        _outBudget = new ByteBudget(limits.MaxOutgoingQueueBytes);
        _readLoop = Task.Run(ReadLoopAsync);
        _writeLoop = Task.Run(WriteLoopAsync);
        _notifyLoop = Task.Run(NotifyLoopAsync);
    }

    /// <summary>Completes (never faults) when the connection has ended.</summary>
    public Task Completion => _completion.Task;
    /// <summary>Why the connection ended; null for a clean end of stream.</summary>
    public Exception? Fault { get; private set; }
    public bool IsClosed => Volatile.Read(ref _closed) == 1;
    public RpcProtocol Protocol => _protocol;
    public int PendingOutgoing { get { lock (_gate) return _pending.Count; } }
    public int InFlightIncoming { get { lock (_gate) return _incoming.Count; } }
    public long QueuedOutgoingBytes => _outBudget.Used;
    public long InboundBytesInUse => Interlocked.Read(ref _inboundBytes);
    /// <summary>Responses that matched no waiting request (for example an answer arriving after the request was cancelled).</summary>
    public int LateResponses => Volatile.Read(ref _lateResponses);

    public async Task<JsonElement> RequestAsync(string method, ReadOnlyMemory<byte> paramsJson = default, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        ThrowIfClosed();
        await _pendingSlots.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var id = RpcId.FromNumber(Interlocked.Increment(ref _nextId));
            var payload = RpcWire.Request(id, method, paramsJson);
            CheckSize(payload);
            await _outBudget.AcquireAsync(payload.Length, ct).ConfigureAwait(false);
            var tcs = new TaskCompletionSource<RpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                if (IsClosed) { _outBudget.Release(payload.Length); throw new RpcConnectionClosedException(Fault); }
                _pending.Add(id, tcs);
            }
            if (!_writes.Writer.TryWrite((payload, true))) _outBudget.Release(payload.Length);

            RpcMessage msg;
            try { msg = await tcs.Task.WaitAsync(timeout ?? Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { Abandon(id, tcs); throw; }
            catch (TimeoutException) { Abandon(id, tcs); throw new RpcTimeoutException(method, timeout); }
            using (msg)
            {
                if (msg.Kind == RpcKind.ErrorResponse)
                    throw new RpcException(msg.ErrorCode, msg.ErrorMessage ?? "") { Id = id, DataJson = msg.ErrorData.ValueKind == JsonValueKind.Undefined ? null : msg.ErrorData.GetRawText() };
                return msg.Result.Clone();
            }
        }
        finally { _pendingSlots.Release(); }
    }

    public async Task NotifyAsync(string method, ReadOnlyMemory<byte> paramsJson = default, CancellationToken ct = default)
    {
        ThrowIfClosed();
        var payload = RpcWire.Notification(method, paramsJson);
        CheckSize(payload);
        await _outBudget.AcquireAsync(payload.Length, ct).ConfigureAwait(false);
        if (!_writes.Writer.TryWrite((payload, true))) _outBudget.Release(payload.Length);
    }

    void ThrowIfClosed()
    {
        if (IsClosed) throw new RpcConnectionClosedException(Fault);
    }

    void CheckSize(byte[] payload)
    {
        if (payload.Length > _limits.MaxMessageBytes)
            throw new RpcException(RpcErrorCodes.MessageTooLarge, $"The message is {payload.Length} bytes; the limit is {_limits.MaxMessageBytes}.");
    }

    /// <summary>Forgets a request nobody waits for any more and tells the peer, so a late answer finds no owner.</summary>
    void Abandon(RpcId id, TaskCompletionSource<RpcMessage> tcs)
    {
        bool removed;
        lock (_gate) removed = _pending.Remove(id);
        if (tcs.Task.IsCompletedSuccessfully) tcs.Task.Result.Dispose();
        if (removed) Enqueue(RpcWire.Notification(_protocol.CancelMethod, _protocol.CancelParams(id)));
    }

    void Enqueue(byte[] payload) => _writes.Writer.TryWrite((payload, false));

    async Task ReadLoopAsync()
    {
        Exception? fault = null;
        try
        {
            while (true)
            {
                var frame = await _protocol.Framing.ReadAsync(_reader, _limits.MaxMessageBytes, _lifetime.Token).ConfigureAwait(false);
                if (frame.Status == FrameStatus.Eof) break;
                if (frame.Status == FrameStatus.Oversize)
                {
                    OnOversize(frame.Prefix);
                    continue;
                }
                RpcMessage msg;
                try { msg = RpcMessage.Parse(frame.Payload!); }
                catch (RpcException e) { Enqueue(RpcWire.Error(e.Id, e.Code, e.Message)); continue; }
                Dispatch(msg);
            }
        }
        catch (Exception e) { fault = e; }
        Close(fault);
    }

    /// <summary>
    /// A message beyond the size limit was dropped. If its id can be read, a waiting caller learns why its answer never comes
    /// (response) or the peer gets an error for that request; otherwise the peer gets an error without an id.
    /// </summary>
    void OnOversize(byte[]? prefix)
    {
        var text = $"The message exceeds the limit of {_limits.MaxMessageBytes} bytes.";
        if (prefix is not null && RpcMessage.TryPeekId(prefix, out var id, out var hasMethod))
        {
            if (hasMethod) { Enqueue(RpcWire.Error(id, RpcErrorCodes.MessageTooLarge, text)); return; }
            TaskCompletionSource<RpcMessage>? tcs;
            lock (_gate) tcs = _pending.Remove(id, out var t) ? t : null;
            if (tcs is not null) { tcs.TrySetException(new RpcException(RpcErrorCodes.MessageTooLarge, text) { Id = id }); return; }
        }
        Enqueue(RpcWire.Error(null, RpcErrorCodes.MessageTooLarge, text));
    }

    void Dispatch(RpcMessage msg)
    {
        switch (msg.Kind)
        {
            case RpcKind.Response or RpcKind.ErrorResponse:
                OnResponse(msg);
                break;
            case RpcKind.Notification:
                OnNotification(msg);
                break;
            default:
                OnRequest(msg);
                break;
        }
    }

    void OnResponse(RpcMessage msg)
    {
        TaskCompletionSource<RpcMessage>? tcs = null;
        lock (_gate)
        {
            if (msg.Id is { } id && _pending.Remove(id, out var t)) tcs = t;
        }
        if (tcs is null)
        {
            Interlocked.Increment(ref _lateResponses);
            msg.Dispose();
            return;
        }
        if (msg.Kind == RpcKind.Response && msg.Result.ValueKind == JsonValueKind.Array && msg.Result.GetArrayLength() > _limits.MaxResultItems)
        {
            var count = msg.Result.GetArrayLength();
            msg.Dispose();
            tcs.TrySetException(new RpcException(RpcErrorCodes.TooManyResults, $"The result has {count} items; the limit is {_limits.MaxResultItems}."));
            return;
        }
        if (!tcs.TrySetResult(msg)) msg.Dispose();
    }

    void OnNotification(RpcMessage msg)
    {
        if (_protocol.TryParseCancel(msg.Method!, msg.Params, out var cancelId))
        {
            CancelIncoming(cancelId);
            msg.Dispose();
            return;
        }
        // A notification cannot be answered with an error, so exceeding the session budget ends the connection.
        if (Interlocked.Add(ref _inboundBytes, msg.Size) > _limits.MaxSessionBytes)
        {
            Interlocked.Add(ref _inboundBytes, -msg.Size);
            msg.Dispose();
            throw new RpcException(RpcErrorCodes.SessionMemoryExceeded, $"Unprocessed peer messages exceed {_limits.MaxSessionBytes} bytes.");
        }
        _notifications.Writer.TryWrite(msg);
    }

    void OnRequest(RpcMessage msg)
    {
        var id = msg.Id!.Value;
        var reject = (int code, string text) =>
        {
            Enqueue(RpcWire.Error(id, code, text));
            msg.Dispose();
        };
        if (Interlocked.Add(ref _inboundBytes, msg.Size) > _limits.MaxSessionBytes)
        {
            Interlocked.Add(ref _inboundBytes, -msg.Size);
            reject(RpcErrorCodes.SessionMemoryExceeded, $"Peer messages in progress exceed {_limits.MaxSessionBytes} bytes.");
            return;
        }
        var cts = new CancellationTokenSource();
        string? refusal = null;
        var refusalCode = RpcErrorCodes.TooManyRequests;
        lock (_gate)
        {
            if (_incoming.Count >= _limits.MaxConcurrentIncoming) refusal = $"More than {_limits.MaxConcurrentIncoming} requests in progress.";
            else if (!_incoming.TryAdd(id, cts)) { refusal = "A request with this id is already in progress."; refusalCode = RpcErrorCodes.InvalidRequest; }
        }
        if (refusal is not null)
        {
            Interlocked.Add(ref _inboundBytes, -msg.Size);
            cts.Dispose();
            reject(refusalCode, refusal);
            return;
        }
        _ = Task.Run(() => RunRequestAsync(msg, id, cts));
    }

    async Task RunRequestAsync(RpcMessage msg, RpcId id, CancellationTokenSource cts)
    {
        byte[]? response = null;
        try
        {
            var result = await _handler.HandleRequestAsync(msg.Method!, msg.Params, cts.Token).ConfigureAwait(false);
            cts.Token.ThrowIfCancellationRequested();
            response = RpcWire.Result(id, result, _limits.MaxResultItems);
            CheckSize(response);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            if (_protocol.RespondsToCancelled) response = RpcWire.Error(id, RpcErrorCodes.RequestCancelled, "The request was cancelled.");
        }
        catch (RpcException e) { response = RpcWire.Error(id, e.Code, e.Message); }
        catch (Exception e) { response = RpcWire.Error(id, RpcErrorCodes.InternalError, e.Message); }
        finally
        {
            lock (_gate) _incoming.Remove(id);
            cts.Dispose();
            Interlocked.Add(ref _inboundBytes, -msg.Size);
            msg.Dispose();
        }
        if (response is not null) Enqueue(response);
    }

    void CancelIncoming(RpcId id)
    {
        CancellationTokenSource? cts;
        lock (_gate) _incoming.TryGetValue(id, out cts);
        try { cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    async Task NotifyLoopAsync()
    {
        await foreach (var msg in _notifications.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try { await _handler.HandleNotificationAsync(msg.Method!, msg.Params).ConfigureAwait(false); }
            catch (Exception) { /* a failing notification handler must not end the connection; notifications have no reply */ }
            finally
            {
                Interlocked.Add(ref _inboundBytes, -msg.Size);
                msg.Dispose();
            }
        }
    }

    async Task WriteLoopAsync()
    {
        var broken = false;
        await foreach (var (payload, budgeted) in _writes.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (!broken)
                {
                    await _output.WriteAsync(_protocol.Framing.Encode(payload)).ConfigureAwait(false);
                    await _output.FlushAsync().ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                broken = true;
                Close(e);
            }
            finally
            {
                if (budgeted) _outBudget.Release(payload.Length);
            }
        }
        // Closing our side lets the peer see the end of the connection, also after a protocol violation.
        try { _output.Dispose(); }
        catch (IOException) { }
    }

    void Close(Exception? fault)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1) return;
        List<TaskCompletionSource<RpcMessage>> pending;
        List<CancellationTokenSource> incoming;
        lock (_gate)
        {
            Fault = fault;
            pending = [.. _pending.Values];
            _pending.Clear();
            incoming = [.. _incoming.Values];
        }
        var closed = new RpcConnectionClosedException(fault);
        foreach (var p in pending) p.TrySetException(closed);
        foreach (var c in incoming)
        {
            try { c.Cancel(); }
            catch (ObjectDisposedException) { }
        }
        _outBudget.Close(closed);
        _notifications.Writer.TryComplete();
        _writes.Writer.TryComplete();
        _completion.TrySetResult();
    }

    public async ValueTask DisposeAsync()
    {
        Close(null);
        _lifetime.Cancel();
        try { _output.Dispose(); }
        catch (IOException) { }
        await Task.WhenAny(Task.WhenAll(_readLoop, _writeLoop, _notifyLoop), Task.Delay(1000)).ConfigureAwait(false);
    }
}
