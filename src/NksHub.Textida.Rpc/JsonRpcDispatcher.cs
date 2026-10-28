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
    readonly Channel<Outgoing> _writes = Channel.CreateUnbounded<Outgoing>(new() { SingleReader = true });
    readonly Channel<RpcMessage> _notifications = Channel.CreateUnbounded<RpcMessage>(new() { SingleReader = true });
    readonly CancellationTokenSource _lifetime = new();
    readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Task _readLoop, _writeLoop, _notifyLoop;
    long _nextId;
    long _inboundBytes;
    long _replyBytes;
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
    /// <summary>Bytes of errors and cancels owed to the peer that are not written yet.</summary>
    internal long QueuedReplyBytes => Interlocked.Read(ref _replyBytes);
    /// <summary>Responses that matched no waiting request (for example an answer arriving after the request was cancelled).</summary>
    public int LateResponses => Volatile.Read(ref _lateResponses);

    /// <summary>
    /// Sends a request and waits for its answer. The <paramref name="timeout"/> covers the whole call, the waits for a free slot and for room in the
    /// outgoing queue included, so a peer that stops reading ends the call with <see cref="RpcTimeoutException"/> instead of holding it forever.
    /// </summary>
    public async Task<JsonElement> RequestAsync(string method, ReadOnlyMemory<byte> paramsJson = default, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        ThrowIfClosed();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout is { } limit && limit != Timeout.InfiniteTimeSpan) deadline.CancelAfter(limit);
        try { await _pendingSlots.WaitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw TimedOut(method, timeout, ct); }
        try
        {
            var id = RpcId.FromNumber(Interlocked.Increment(ref _nextId));
            var payload = RpcWire.Request(id, method, paramsJson);
            CheckSize(payload);
            try { await _outBudget.AcquireAsync(payload.Length, deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw TimedOut(method, timeout, ct); }
            var tcs = new TaskCompletionSource<RpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var request = new Outgoing(payload, budgeted: true);
            lock (_gate)
            {
                if (IsClosed) { _outBudget.Release(payload.Length); throw new RpcConnectionClosedException(Fault); }
                _pending.Add(id, tcs);
            }
            Write(request);

            RpcMessage msg;
            try { msg = await tcs.Task.WaitAsync(deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                Abandon(id, tcs, request);
                throw TimedOut(method, timeout, ct);
            }
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
        await QueueAsync(payload, ct).ConfigureAwait(false);
    }

    /// <summary>The caller's own cancellation stays an <see cref="OperationCanceledException"/>; the deadline running out is a timeout.</summary>
    static Exception TimedOut(string method, TimeSpan? timeout, CancellationToken ct) =>
        ct.IsCancellationRequested ? new OperationCanceledException(ct) : new RpcTimeoutException(method, timeout);

    void ThrowIfClosed()
    {
        if (IsClosed) throw new RpcConnectionClosedException(Fault);
    }

    void CheckSize(byte[] payload)
    {
        if (payload.Length > _limits.MaxMessageBytes)
            throw new RpcException(RpcErrorCodes.MessageTooLarge, $"The message is {payload.Length} bytes; the limit is {_limits.MaxMessageBytes}.");
    }

    /// <summary>
    /// Forgets a request nobody waits for any more. One still in the outgoing queue is taken back with its bytes and never reaches the peer;
    /// one already sent is cancelled at the peer, so a late answer finds no owner.
    /// </summary>
    void Abandon(RpcId id, TaskCompletionSource<RpcMessage> tcs, Outgoing request)
    {
        bool removed;
        lock (_gate) removed = _pending.Remove(id);
        if (tcs.Task.IsCompletedSuccessfully) tcs.Task.Result.Dispose();
        if (request.Withdraw()) Settle(request);
        else if (removed) Enqueue(RpcWire.Notification(_protocol.CancelMethod, _protocol.CancelParams(id)));
    }

    /// <summary>Waits for room in the outgoing queue (back-pressure), then queues the message.</summary>
    async ValueTask QueueAsync(byte[] payload, CancellationToken ct)
    {
        await _outBudget.AcquireAsync(payload.Length, ct).ConfigureAwait(false);
        Write(new Outgoing(payload, budgeted: true));
    }

    /// <summary>
    /// Queues a reply the protocol owes the peer (an error, a cancel) without waiting, so reading never stops for it. A peer that keeps sending
    /// while it reads none of them would grow the queue without end: once they alone exceed the outgoing limit, the connection ends.
    /// </summary>
    void Enqueue(byte[] payload)
    {
        if (Interlocked.Add(ref _replyBytes, payload.Length) > _limits.MaxOutgoingQueueBytes)
        {
            Interlocked.Add(ref _replyBytes, -payload.Length);
            Close(new RpcException(RpcErrorCodes.SessionMemoryExceeded, $"Replies the peer has not read exceed {_limits.MaxOutgoingQueueBytes} bytes; it sends without reading."));
            return;
        }
        Write(new Outgoing(payload, budgeted: false));
    }

    void Write(Outgoing item)
    {
        // A completed queue takes nothing more, and the writer will never hand these bytes back.
        if (!_writes.Writer.TryWrite(item) && item.Withdraw()) Settle(item);
    }

    void Settle(Outgoing item)
    {
        if (item.Budgeted) _outBudget.Release(item.Payload.Length);
        else Interlocked.Add(ref _replyBytes, -item.Payload.Length);
    }

    /// <summary>A message in the outgoing queue: the writer takes it, or its sender withdraws it first.</summary>
    sealed class Outgoing(byte[] payload, bool budgeted)
    {
        int _state;

        public byte[] Payload { get; } = payload;
        public bool Budgeted { get; } = budgeted;
        public bool Take() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;
        public bool Withdraw() => Interlocked.CompareExchange(ref _state, 2, 0) == 0;
    }

    async Task ReadLoopAsync()
    {
        Exception? fault = null;
        try
        {
            while (!IsClosed)
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
                catch (RpcException e) { Enqueue(RpcWire.Error(e.Id, e.Code, e.Message, e.DataJson)); continue; }
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
            RpcRequestContext.CurrentId = id;
            var result = await _handler.HandleRequestAsync(msg.Method!, msg.Params, cts.Token).ConfigureAwait(false);
            cts.Token.ThrowIfCancellationRequested();
            response = RpcWire.Result(id, result, _limits.MaxResultItems);
            CheckSize(response);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { response = Cancelled(id); }
        catch (RpcException e) { response = RpcWire.Error(id, e.Code, e.Message, e.DataJson); }
        catch (TimeoutException e) { response = RpcWire.Error(id, RpcErrorCodes.RequestTimeout, e.Message); }
        catch (Exception e) { response = RpcWire.Error(id, RpcErrorCodes.InternalError, e.Message); }
        finally
        {
            Interlocked.Add(ref _inboundBytes, -msg.Size);
            msg.Dispose();
        }
        var budgeted = false;
        try
        {
            // The answer keeps its slot while it waits for room, so a peer that asks without reading the answers meets the concurrency limit.
            if (response is not null) { await _outBudget.AcquireAsync(response.Length, cts.Token).ConfigureAwait(false); budgeted = true; }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { response = Cancelled(id); }
        catch (IOException) { response = null; } // the connection ended while the answer waited
        finally
        {
            // Freed before the answer is queued: a peer that sends its next request as soon as it sees the answer must find the slot free.
            lock (_gate) _incoming.Remove(id);
            cts.Dispose();
        }
        if (budgeted) Write(new Outgoing(response!, budgeted: true));
        else if (response is not null) Enqueue(response);
    }

    byte[]? Cancelled(RpcId id) => _protocol.RespondsToCancelled ? RpcWire.Error(id, RpcErrorCodes.RequestCancelled, "The request was cancelled.") : null;

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
        await foreach (var item in _writes.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (!item.Take()) continue; // withdrawn; its bytes went back then
            try
            {
                if (!broken)
                {
                    await _output.WriteAsync(_protocol.Framing.Encode(item.Payload)).ConfigureAwait(false);
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
                Settle(item);
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
