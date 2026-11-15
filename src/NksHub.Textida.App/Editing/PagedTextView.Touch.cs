using Avalonia;
using Avalonia.Input;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Editing;

internal sealed partial class PagedTextView
{
    private TextTouchInput _touchInput = null!;

    private void InitTouch()
    {
        _touchInput = new TextTouchInput(_canvas, HitTestTouchAsync, TouchWordAtAsync,
            () => new TouchRange(HasSelection ? _anchor : _caret, _caret), SelectTouch, ScrollTouch, TouchCaretRect);
        ViewChanged += _touchInput.Refresh;
        _hBar.ValueChanged += (_, _) => _touchInput.Refresh();
    }

    internal async Task<long?> HitTestTouchAsync(Point point, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (LineCount <= 0) return null;
        long line = Math.Clamp(TopLine + (long)Math.Floor(point.Y / _canvas.LineHeight), 0, LineCount - 1);
        if (GetLine(line) is not { } view) return null;
        double x = point.X - _canvas.TextLeft + _hScroll;
        if (!view.Truncated || x / _canvas.CharWidth < view.Display.Length)
            return view.Start + view.Map.ByteOffsetOf(_canvas.IndexAtX(view, x));

        long column = _canvas.ColumnAtX(view, x);
        if (!ColumnIsFar(view, column)) return OffsetOfColumn(view, column);
        long revision = Doc.Revision;
        var paged = Paged;
        var snapshot = paged.SnapshotColumns();
        var hook = FarMoveHook;
        try
        {
            long offset = await Task.Run(() =>
            {
                hook?.Invoke(token);
                return snapshot.OffsetOfColumn(view.Start, column, token);
            }, token);
            token.ThrowIfCancellationRequested();
            if (revision != Doc.Revision || !ReferenceEquals(paged, Paged)) return null;
            paged.KeepColumns(snapshot);
            return offset;
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or IOException)
        {
            return null;
        }
    }

    internal async Task<TouchRange?> TouchWordAtAsync(long offset, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var snapshot = Doc.GetSnapshot();
        var paged = Paged;
        int bom = Doc.BomLength;
        try
        {
            var word = await Task.Run<TouchRange?>(() =>
            {
                var text = new ByteText((long at, Span<byte> bytes) =>
                {
                    token.ThrowIfCancellationRequested();
                    return snapshot.Read(at, bytes);
                }, snapshot.ByteLength, snapshot.Codec, bom);
                return ByteCarets.TryWordAt(text, offset, out long start, out long end) ? new TouchRange(start, end) : null;
            }, token);
            token.ThrowIfCancellationRequested();
            return snapshot.Revision == Doc.Revision && ReferenceEquals(paged, Paged) ? word : null;
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or IOException)
        {
            return null;
        }
    }

    internal void SelectTouch(TouchRange range)
    {
        Focus();
        CancelFarMove();
        SetCarets(SelectionSet.Single(new TextSelection(range.Anchor, range.Caret)));
        _wholeWord = false;
        Reveal();
        RaiseViewChanged();
    }

    internal void ScrollTouch(Vector delta)
    {
        _glide.Stop();
        _bar.Value = Math.Clamp(_bar.Value + delta.Y / _canvas.LineHeight, 0, _bar.Maximum);
        _hBar.Value = Math.Clamp(_hBar.Value + delta.X / _canvas.CharWidth, 0, _hBar.Maximum);
    }

    internal Rect? TouchCaretRect(long offset)
    {
        if (!CanFindLine(offset)) return null;
        long line = LineOf(offset);
        if (line < TopLine || line >= TopLine + _canvas.VisibleLines || GetLine(line) is not { } view) return null;
        if (view.Truncated && OffsetIsFar(view, offset)) return null;
        double x = _canvas.TextLeft - _hScroll + _canvas.XOfColumn(view, ColumnAt(view, offset));
        if (x < _canvas.TextLeft || x > _canvas.Bounds.Width) return null;
        return new Rect(x, (line - TopLine) * _canvas.LineHeight, 1.5, _canvas.LineHeight);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Mouse) return;
        Focus();
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if ((e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)) && StackTraceAt(e.GetPosition(_canvas)) is { } link)
        {
            e.Handled = true;
            StackTraceClicked?.Invoke(link);
            return;
        }
        if (e.KeyModifiers == KeyModifiers.Alt)
        {
            // Alt+click adds a caret where it lands, or removes the one that is there
            if (OffsetAt(e.GetPosition(_canvas)) is { } at) AddOrRemoveCaretAt(at.Offset);
            e.Handled = true;
            return;
        }
        Place(e.GetPosition(_canvas), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        _dragFrom = _caret;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Type != PointerType.Mouse || _dragFrom < 0) return;
        Place(e.GetPosition(_canvas), extend: true);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Type != PointerType.Mouse || _dragFrom < 0) return;
        _dragFrom = -1;
        e.Pointer.Capture(null);
    }

    /// <summary>The line and the byte offset under a point of the text area; null while the line is not known yet.</summary>
    private (long Offset, long Line)? OffsetAt(Point point)
    {
        long line = Math.Clamp(TopLine + (long)Math.Floor(point.Y / _canvas.LineHeight), 0, LineCount - 1);
        if (GetLine(line) is not { } view) return null;
        double x = point.X - _canvas.TextLeft + _hScroll;
        long to = view.Truncated && x / _canvas.CharWidth >= view.Display.Length
            ? OffsetOfColumn(view, _canvas.ColumnAtX(view, x))
            : view.Start + view.Map.ByteOffsetOf(_canvas.IndexAtX(view, x));
        return (to, line);
    }

    private void Place(Point point, bool extend)
    {
        if (OffsetAt(point) is not { } at) return;
        _extend = extend;
        try { MoveTo(at.Offset, at.Line); }
        finally { _extend = false; }
    }

}
