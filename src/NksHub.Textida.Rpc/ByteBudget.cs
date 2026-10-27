namespace NksHub.Textida.Rpc;

/// <summary>Async byte allowance: acquirers wait while the budget is spent. A single item larger than the budget is admitted when nothing else is held.</summary>
internal sealed class ByteBudget(long max)
{
    readonly object _gate = new();
    readonly Queue<(long Bytes, TaskCompletionSource Waiter)> _waiters = new();
    long _used;
    Exception? _closed;

    public long Used { get { lock (_gate) return _used; } }

    public async ValueTask AcquireAsync(long bytes, CancellationToken ct)
    {
        TaskCompletionSource waiter;
        lock (_gate)
        {
            if (_closed is not null) throw _closed;
            if (_waiters.Count == 0 && (_used == 0 || _used + bytes <= max)) { _used += bytes; return; }
            waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue((bytes, waiter));
        }
        try { await waiter.Task.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            // Granted in the same instant as the cancellation: hand the bytes back. Failed by a close: nothing was granted.
            if (!waiter.TrySetCanceled() && waiter.Task.IsCompletedSuccessfully) Release(bytes);
            throw;
        }
    }

    public void Release(long bytes)
    {
        lock (_gate)
        {
            _used -= bytes;
            while (_waiters.TryPeek(out var w))
            {
                if (w.Waiter.Task.IsCompleted) { _waiters.Dequeue(); continue; }
                if (_used != 0 && _used + w.Bytes > max) break;
                _waiters.Dequeue();
                if (w.Waiter.TrySetResult()) _used += w.Bytes;
            }
        }
    }

    public void Close(Exception error)
    {
        lock (_gate)
        {
            _closed = error;
            while (_waiters.TryDequeue(out var w)) w.Waiter.TrySetException(error);
        }
    }
}
