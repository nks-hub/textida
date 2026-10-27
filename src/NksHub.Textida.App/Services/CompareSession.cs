using NksHub.Textida.App.Editing;
using NksHub.Textida.Core.Compare;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.App.Services;

/// <summary>
/// One side of a comparison: an open document (read at its current revision and the target of a transfer), a revision of a document, a file on disk, or a text
/// (the clipboard, a selection, or text the user pasted). Only a document can receive a block from the other side.
/// </summary>
internal sealed class CompareSide
{
    /// <summary>What heads the side in the view; the window sets it from the localised catalogue.</summary>
    public string Title { get; set; } = "";
    public OpenDocument? Document { get; init; }
    public string? FilePath { get; init; }
    /// <summary>A revision of a document a plugin asked to compare; it stays as it was asked for (<see cref="SnapshotBom"/> bytes at its start are the byte order mark).</summary>
    public DocumentSnapshot? Snapshot { get; init; }
    public int SnapshotBom { get; init; }
    public TextCodec? FileCodec { get; init; }
    public string Text { get; set; } = "";
    /// <summary>The user fills this side by pasting; the view offers a button for it.</summary>
    public bool Pasteable { get; init; }

    public bool CanReceive => Document is { ReadOnly: false };

    /// <summary>A snapshot of the side as it is now. Dispose it with <see cref="DiffSource.Release"/>.</summary>
    internal DiffSource Open() =>
        Document is { } od ? DiffSource.Of(od.Doc)
        : Snapshot is { } snapshot ? new DiffSource(snapshot, snapshot.Codec, SnapshotBom)
        : FilePath is { } path ? DiffSource.OfFile(path, FileCodec)
        : DiffSource.OfText(Text);
}

public enum TransferStatus
{
    Done,
    /// <summary>A document changed after the diff was made; the diff is made again and nothing was changed.</summary>
    Stale,
    /// <summary>The side is not a document that can be edited.</summary>
    ReadOnly,
    /// <summary>The encoding of the target cannot hold the characters of the block; nothing was changed.</summary>
    Lossy,
    /// <summary>A side could not be read; nothing was changed.</summary>
    Failed,
}

/// <summary>
/// The comparison of two sides and the diff of their lines. It never reads the sides whole: the diff works on snapshots (see <see cref="LineDiff"/>),
/// in the background, and <see cref="Transfer"/> changes the target document by one edit, which is one undo step.
/// </summary>
internal sealed class CompareSession(CompareSide left, CompareSide right, DiffLimits? limits = null) : IDisposable
{
    private CancellationTokenSource? _cts;
    private int _generation;
    private DiffSource? _leftSource, _rightSource;
    private long _leftRevision = -1, _rightRevision = -1;
    private bool _disposed;

    public CompareSide Left { get; } = left;
    public CompareSide Right { get; } = right;
    /// <summary>The diff of the last finished refresh; null until the first one ends.</summary>
    public TextDiff? Diff { get; private set; }
    /// <summary>Why the last refresh could not read a side; null when it could.</summary>
    public string? Problem { get; private set; }
    public bool Busy { get; private set; }
    /// <summary>What the next refresh does not tell apart (the <c>compare.ignore*</c> settings).</summary>
    public DiffIgnore Ignore { get; set; }

    /// <summary>False when a document side changed since the diff was made, so the diff shows an older text, or the diff ignores other differences than <see cref="Ignore"/>.</summary>
    public bool IsCurrent => Diff is not null && Diff.Ignore == Ignore && (Left.Document is not { } a || a.Doc.Revision == _leftRevision) && (Right.Document is not { } b || b.Doc.Revision == _rightRevision);

    /// <summary>Raised on the thread that called <see cref="RefreshAsync"/> when a refresh ended.</summary>
    public event Action? Updated;

    public async Task RefreshAsync()
    {
        if (_disposed) return;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        int generation = ++_generation;
        Busy = true;
        DiffSource? l = null, r = null;
        long lr = Left.Document?.Doc.Revision ?? -1, rr = Right.Document?.Doc.Revision ?? -1;
        var ignore = Ignore;
        try
        {
            l = Left.Open();
            r = Right.Open();
            var diff = await Task.Run(() => LineDiff.Compute(l, r, limits, cts.Token, ignore), cts.Token);
            if (generation != _generation || _disposed) throw new OperationCanceledException();
            _leftSource?.Release();
            _rightSource?.Release();
            (_leftSource, _rightSource, Diff, _leftRevision, _rightRevision, Problem) = (l, r, diff, lr, rr, null);
            l = r = null;
        }
        catch (OperationCanceledException)
        {
            l?.Release();
            r?.Release();
            if (generation != _generation || _disposed) return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            l?.Release();
            r?.Release();
            if (generation != _generation) return;
            Problem = e.Message;
        }
        Busy = false;
        Updated?.Invoke();
    }

    /// <summary>
    /// Makes the lines of the hunk on the <paramref name="target"/> side equal to the other side, as one edit of the target document.
    /// Nothing is changed unless both documents are still at the revision the diff was made for.
    /// </summary>
    public TransferStatus Transfer(int hunk, DiffSide target, out string? problem)
    {
        problem = null;
        var side = target == DiffSide.A ? Left : Right;
        if (Diff is not { } diff || hunk < 0 || hunk >= diff.Hunks.Count || !side.CanReceive) return TransferStatus.ReadOnly;
        if ((Left.Document is { } a && a.Doc.Revision != _leftRevision) || (Right.Document is { } b && b.Doc.Revision != _rightRevision)) return TransferStatus.Stale;
        try
        {
            var edit = DiffTransfer.Plan(diff, hunk, target);
            if (edit.Lossy) return TransferStatus.Lossy;
            side.Document!.Doc.Replace(edit.Range, edit.Bytes);
            return TransferStatus.Done;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            problem = e.Message;
            return TransferStatus.Failed;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _generation++;
        _cts?.Cancel();
        _leftSource?.Release();
        _rightSource?.Release();
        _leftSource = _rightSource = null;
        Diff = null;
    }
}
