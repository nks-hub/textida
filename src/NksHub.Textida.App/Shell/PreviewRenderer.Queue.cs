using Avalonia.Threading;

namespace NksHub.Textida.App.Shell;

// A preview has at most one active parser and one pending snapshot; typing replaces the pending work.
internal sealed class MarkdownRenderQueue : IDisposable
{
    private readonly SemaphoreSlim _parser = new(1, 1);
    private CancellationTokenSource? _cancel;
    private long _generation;
    private bool _disposed;

    public async Task RunAsync(string source, Func<bool> current, Action<MarkdownReaderDocument> apply)
    {
        Cancel();
        if (_disposed) return;
        long generation = _generation;
        var cancel = _cancel = new CancellationTokenSource();
        try
        {
            await _parser.WaitAsync(cancel.Token);
            MarkdownReaderDocument document;
            try
            {
                document = await Task.Run(() =>
                {
                    cancel.Token.ThrowIfCancellationRequested();
                    return MarkdownReaderModel.Build(source);
                }, cancel.Token);
            }
            finally { _parser.Release(); }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_disposed && !cancel.IsCancellationRequested && generation == _generation && current()) apply(document);
            });
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_cancel, cancel)) _cancel = null;
            cancel.Dispose();
        }
    }

    public void Cancel()
    {
        _generation++;
        _cancel?.Cancel();
    }

    public void Dispose()
    {
        _disposed = true;
        Cancel();
    }
}
