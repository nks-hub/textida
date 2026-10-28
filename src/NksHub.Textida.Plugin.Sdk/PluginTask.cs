using System.Diagnostics;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginSdk;

/// <summary>
/// A task the user sees in the editor, with a progress bar and a cancel button (<c>ui/task/*</c>). <see cref="Cancellation"/> is cancelled when the user presses cancel or the
/// request that started the task is cancelled; ending the task (dispose) tells the editor it is over. Obtain one from <see cref="PluginBase.StartTaskAsync"/>.
/// </summary>
public sealed class PluginTask : IAsyncDisposable
{
    private readonly JsonRpcDispatcher _rpc;
    private readonly string _id;
    private readonly CancellationTokenSource _source;
    private readonly Action<string> _forget;
    private readonly TimeSpan? _reportEvery;
    private readonly Stopwatch _since = Stopwatch.StartNew();
    private bool _reported;

    internal PluginTask(JsonRpcDispatcher rpc, string id, CancellationTokenSource source, TimeSpan? reportEvery, Action<string> forget)
    {
        (_rpc, _id, _source, _reportEvery, _forget) = (rpc, id, source, reportEvery, forget);
    }

    /// <summary>Cancelled when the user presses cancel or the request that started the task is cancelled.</summary>
    public CancellationToken Cancellation => _source.Token;

    /// <summary>Reports <paramref name="fraction"/> (0 to 1) of the work done. When the task was started with a reporting interval, a report sooner than that after the last one is dropped.</summary>
    public async ValueTask ReportAsync(double fraction, string? message = null)
    {
        if (_reported && _reportEvery is { } every && _since.Elapsed < every) return;
        _reported = true;
        _since.Restart();
        await _rpc.RequestAsync(UiMethods.TaskProgress, new TaskProgressParams(_id, Math.Clamp(fraction, 0, 1), message), SdkJson.Default.TaskProgressParams, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Tells the editor the task is over.</summary>
    public async ValueTask DisposeAsync()
    {
        _forget(_id);
        _source.Dispose();
        // an editor that is gone has no task left to end
        try { await _rpc.RequestAsync(UiMethods.TaskEnd, new TaskEndParams(_id), SdkJson.Default.TaskEndParams, CancellationToken.None).ConfigureAwait(false); }
        catch (RpcConnectionClosedException) { }
    }
}
