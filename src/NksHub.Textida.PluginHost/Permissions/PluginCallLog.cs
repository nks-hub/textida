using System.Diagnostics;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Permissions;

public enum CallOutcome { Ok, Denied, Failed }

/// <summary>
/// One call of a plugin to the editor. By construction it holds no parameters, no result and no error text,
/// so neither document text nor a secret or token can end up in it.
/// </summary>
public sealed record CallLogEntry(DateTimeOffset At, string PluginId, string Method, Guid? DocumentId, CallOutcome Outcome, int? ErrorCode, long Milliseconds);

/// <summary>Bounded log of the calls plugins made to the editor; the oldest entry goes first.</summary>
public sealed class PluginCallLog(int capacity, TimeProvider? time = null)
{
    private readonly object _gate = new();
    private readonly Queue<CallLogEntry> _entries = new();
    private readonly int _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public event Action<CallLogEntry>? Recorded;

    public IReadOnlyList<CallLogEntry> Entries()
    {
        lock (_gate) return [.. _entries];
    }

    private void Record(CallLogEntry entry)
    {
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > _capacity) _entries.Dequeue();
        }
        Recorded?.Invoke(entry);
    }

    /// <summary>Runs <paramref name="work"/> and logs how it ended; the exception, if any, passes on unchanged.</summary>
    public async ValueTask<byte[]?> RunAsync(string pluginId, string method, Guid? document, Func<ValueTask<byte[]?>> work)
    {
        var start = Stopwatch.GetTimestamp();
        var at = _time.GetUtcNow();
        try
        {
            var result = await work().ConfigureAwait(false);
            Record(new CallLogEntry(at, pluginId, method, document, CallOutcome.Ok, null, Elapsed(start)));
            return result;
        }
        catch (RpcException e)
        {
            Record(new CallLogEntry(at, pluginId, method, document, e.Code == Documents.DocumentErrorCodes.PermissionDenied ? CallOutcome.Denied : CallOutcome.Failed, e.Code, Elapsed(start)));
            throw;
        }
        catch (Exception)
        {
            Record(new CallLogEntry(at, pluginId, method, document, CallOutcome.Failed, null, Elapsed(start)));
            throw;
        }
    }

    private static long Elapsed(long start) => (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
}
