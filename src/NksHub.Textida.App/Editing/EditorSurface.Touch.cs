using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using NksHub.Textida.AvaloniaEditAdapter;

namespace NksHub.Textida.App.Editing;

internal sealed partial class EditorSurface
{
    private TextTouchInput? touch;

    private void InitTouch()
    {
        var view = TextArea.TextView;
        touch = new TextTouchInput(view, TouchOffset, TouchWord, () =>
        {
            var primary = PrimarySel();
            return new TouchRange(primary.Anchor, primary.Caret);
        }, range =>
        {
            TextArea.Focus();
            SetCarets(Carets.One(new Sel((int)range.Anchor, (int)range.Caret)), reveal: false);
        }, delta =>
        {
            _glide?.Stop();
            _scroller ??= this.FindDescendantOfType<ScrollViewer>();
            if (_scroller is { } viewer) viewer.Offset += delta;
        }, TouchCaretRect);
        view.ScrollOffsetChanged += RefreshTouch;
        view.VisualLinesChanged += RefreshTouch;
        TextArea.SelectionChanged += RefreshTouch;
        TextArea.Caret.PositionChanged += RefreshTouch;
        AddHandler(KeyDownEvent, CancelTouchKey, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void RefreshTouch(object? sender, EventArgs e) => touch?.Refresh();
    private void CancelTouchKey(object? sender, KeyEventArgs e) => touch?.Cancel();

    private Task<long?> TouchOffset(Point point, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var view = TextArea.TextView;
        view.EnsureVisualLines();
        var visual = point + view.ScrollOffset;
        visual = visual.WithY(Math.Clamp(visual.Y, 0, Math.Max(0, Math.BitDecrement(view.DocumentHeight))));
        long? result = view.GetPosition(visual) is { } position
            ? Document.GetOffset(position.Location) : null;
        return Task.FromResult(result);
    }

    private async Task<TouchRange?> TouchWord(long offset, CancellationToken token)
    {
        var document = Document;
        long revision = Doc.Doc.Revision;
        var text = document.CreateSnapshot();
        var range = await Task.Run(() =>
        {
            int at = (int)Math.Clamp(offset, 0, text.TextLength);
            if (at > 0 && at < text.TextLength && char.IsLowSurrogate(text.GetCharAt(at)) && char.IsHighSurrogate(text.GetCharAt(at - 1))) at--;
            int start = at, end = at;
            static bool Word(char c) => char.IsLetterOrDigit(c) || c == '_';
            while (start > 0 && Word(text.GetCharAt(start - 1)))
            {
                if ((start & 255) == 0) token.ThrowIfCancellationRequested();
                start--;
            }
            while (end < text.TextLength && Word(text.GetCharAt(end)))
            {
                if ((end & 255) == 0) token.ThrowIfCancellationRequested();
                end++;
            }
            if (start == end && at < text.TextLength)
                end += char.IsHighSurrogate(text.GetCharAt(at)) && at + 1 < text.TextLength && char.IsLowSurrogate(text.GetCharAt(at + 1)) ? 2 : 1;
            token.ThrowIfCancellationRequested();
            return new TouchRange(start, end);
        }, token);
        return !token.IsCancellationRequested && ReferenceEquals(document, Document) && revision == Doc.Doc.Revision ? range : null;
    }

    private Rect? TouchCaretRect(long offset)
    {
        var view = TextArea.TextView;
        if (!view.VisualLinesValid || offset < 0 || offset > Document.TextLength) return null;
        var location = Document.GetLocation((int)offset);
        if (view.GetVisualLine(location.Line) is null) return null;
        var position = new TextViewPosition(location);
        var top = view.GetVisualPosition(position, VisualYPosition.LineTop) - view.ScrollOffset;
        var bottom = view.GetVisualPosition(position, VisualYPosition.LineBottom) - view.ScrollOffset;
        return new Rect(top, new Size(1, Math.Max(1, bottom.Y - top.Y)));
    }

    private void ReleaseTouch()
    {
        touch?.Dispose();
        touch = null;
        TextArea.TextView.ScrollOffsetChanged -= RefreshTouch;
        TextArea.TextView.VisualLinesChanged -= RefreshTouch;
        TextArea.SelectionChanged -= RefreshTouch;
        TextArea.Caret.PositionChanged -= RefreshTouch;
        RemoveHandler(KeyDownEvent, CancelTouchKey);
    }
}
