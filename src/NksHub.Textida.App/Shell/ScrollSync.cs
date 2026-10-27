using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.Core.Compare;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Keeps two text views level while one of them scrolls (P5.2.2): the first line shown by one view is mapped through the line diff of the two
/// documents to the line of the other, so equal text stays side by side across inserted and removed blocks. Until the first diff is ready, and for
/// two views of one document, the line is the same line. The diff is made again, in the background, after either document changed.
/// </summary>
internal sealed class ScrollSync : IDisposable
{
    private readonly IMinimapHost _a, _b;
    private readonly Func<DiffSource> _openA, _openB;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly long[] _expected = [-1, -1];
    private DiffSource? _sourceA, _sourceB;
    private CancellationTokenSource? _cts;
    private int _generation;
    private bool _disposed;

    public ScrollSync(IMinimapHost a, IMinimapHost b, Func<DiffSource> openA, Func<DiffSource> openB)
    {
        (_a, _b, _openA, _openB) = (a, b, openA, openB);
        _a.MapChanged += OnA;
        _b.MapChanged += OnB;
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = RefreshAsync();
        };
    }

    /// <summary>The diff the lines are mapped through; null until the first one is ready.</summary>
    public TextDiff? Diff { get; private set; }

    /// <summary>A document changed: the diff is made again once the typing pauses.</summary>
    public void Invalidate()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Makes the diff of the two documents as they are now, then puts the second view level with the first.</summary>
    public async Task RefreshAsync()
    {
        if (_disposed) return;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        int generation = ++_generation;
        DiffSource? a = null, b = null;
        try
        {
            a = _openA();
            b = _openB();
            var diff = await Task.Run(() => LineDiff.Compute(a, b, ct: cts.Token), cts.Token);
            if (generation != _generation || _disposed) throw new OperationCanceledException();
            _sourceA?.Release();
            _sourceB?.Release();
            (_sourceA, _sourceB, Diff) = (a, b, diff);
            a = b = null;
            Follow(_a, _b, DiffSide.A);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException)
        {
            // a stale or failed diff keeps the previous mapping; the next change tries again
        }
        finally
        {
            a?.Release();
            b?.Release();
        }
    }

    private void OnA() => Follow(_a, _b, DiffSide.A);
    private void OnB() => Follow(_b, _a, DiffSide.B);

    /// <summary>Scrolls <paramref name="to"/> to the line that stands level with the first line shown by <paramref name="from"/>.</summary>
    internal void Follow(IMinimapHost from, IMinimapHost to, DiffSide side)
    {
        if (_disposed) return;
        int me = (int)side, other = 1 - me;
        long first = from.Visible.First;
        if (_expected[me] == first)
        {
            _expected[me] = -1;
            return;
        }
        long mapped = Diff?.Layout.MapLine(side, first) ?? first;
        mapped = Math.Clamp(mapped, 0, Math.Max(0, to.LineCount - 1));
        if (to.Visible.First == mapped) return;
        _expected[other] = mapped;
        to.ScrollToLine(mapped);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _generation++;
        _cts?.Cancel();
        _debounce.Stop();
        _a.MapChanged -= OnA;
        _b.MapChanged -= OnB;
        _sourceA?.Release();
        _sourceB?.Release();
    }
}
