using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Selecting, copying and links of <see cref="MarkdownReader"/>, as on a web page: drag across any blocks, Shift+click extends, a double click takes a word and a triple click the block, Ctrl+A everything;
/// the page scrolls while the pointer is dragged past its edge. Copy gives the plain text (cells of a table separated by tabs); "Copy as Markdown" gives the part of the source the selection covers.
/// A link is followed by a click that did not drag, or from the keyboard: Tab moves between the links and Enter follows one. A code block whose lines reach past its box scrolls sideways with
/// Left and Right, and Home and End take it to its start and end, while the caret (the place of the last click or of the selection's end) stands in it.
/// </summary>
internal sealed partial class MarkdownReader
{
    private int _anchor, _caret;
    private int _hoverLink = -1, _focusedLink = -1, _pressLink = -1;
    private bool _dragging, _moved, _thumbDrag;
    private int _dragUnit; // 1 character, 2 word, 3 block
    private (int Start, int End) _dragOrigin;
    private Point _pressPoint, _lastPointer;
    private int _thumbGroup;
    private double _thumbStartX, _thumbStartScroll;
    private DispatcherTimer? _autoScroll;

    private static readonly Cursor TextCursor = new(StandardCursorType.Ibeam), LinkCursor = new(StandardCursorType.Hand);

    /// <summary>The selection as positions in <see cref="MarkdownReaderDocument.PlainText"/>, start before end.</summary>
    internal (int Start, int End) SelectionRange => (Math.Min(_anchor, _caret), Math.Max(_anchor, _caret));

    internal bool HasSelection => _anchor != _caret;

    internal void Select(int anchor, int caret)
    {
        _anchor = Math.Clamp(anchor, 0, _doc.Length);
        _caret = Math.Clamp(caret, 0, _doc.Length);
        InvalidateVisual();
    }

    internal void SelectAll() => Select(0, _doc.Length);

    /// <summary>After a new rendering the selection stays on the text that did not change; when the selected text itself changed it is dropped.</summary>
    private void MapSelection(MarkdownReaderDocument old, MarkdownReaderDocument now)
    {
        if (_anchor == _caret) { _anchor = _caret = 0; return; }
        string a = old.PlainText, b = now.PlainText;
        int prefix = 0, suffix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[^(suffix + 1)] == b[^(suffix + 1)]) suffix++;
        var (start, end) = SelectionRange;
        int delta = b.Length - a.Length;
        if (end <= prefix) return;
        if (start >= a.Length - suffix) { _anchor += delta; _caret += delta; return; }
        _anchor = _caret = 0;
    }

    /// <summary>The selected text as plain text: blocks one per line, paragraphs apart by an empty line, cells of a table by a tab.</summary>
    internal string SelectedText()
    {
        var (start, end) = SelectionRange;
        if (start >= end) return "";
        var text = new StringBuilder();
        for (int i = _doc.BlockAt(start); i < _doc.Blocks.Count; i++)
        {
            var b = _doc.Blocks[i];
            if (b.DocStart > end) break;
            int from = Math.Max(0, start - b.DocStart), to = Math.Min(b.Text.Length, end - b.DocStart);
            if (to < from || b.Text.Length == 0) continue;
            if (text.Length > 0) text.Append(b.Join);
            text.Append(b.Text, from, to - from);
        }
        return text.ToString();
    }

    /// <summary>The part of the Markdown source the selection covers: from where its first character stands to where its last one does, whole lines when it starts or ends at the edge of a block.</summary>
    internal string SelectedMarkdown()
    {
        var (start, end) = SelectionRange;
        if (start >= end) return "";
        int from = SourceAt(start, atEnd: false), to = SourceAt(end, atEnd: true);
        return to > from ? _doc.Source[from..to] : SelectedText();
    }

    private int SourceAt(int position, bool atEnd)
    {
        string source = _doc.Source;
        int index = _doc.BlockAt(position);
        var b = _doc.Blocks[index];
        int offset = Math.Clamp(position - b.DocStart, 0, b.Text.Length);
        if (atEnd && offset == 0 && index > 0 && position == b.DocStart && b.Text.Length > 0)
        {
            // a selection that ends at the very start of a block ends after the block before
            b = _doc.Blocks[index - 1];
            offset = b.Text.Length;
        }
        int LineStart(int at) { at = Math.Clamp(at, 0, source.Length); while (at > 0 && source[at - 1] != '\n') at--; return at; }
        int LineEnd(int at) { at = Math.Clamp(at, 0, source.Length); while (at < source.Length && source[at] != '\n' && source[at] != '\r') at++; return at; }
        if (!atEnd && offset == 0) return LineStart(Math.Max(0, b.SourceStart));
        if (atEnd && offset >= b.Text.Length) return LineEnd(Math.Max(0, b.SourceEnd - 1));
        ReaderSpan? before = null;
        foreach (var span in b.Spans)
        {
            if (offset >= span.Start && offset <= span.Start + span.Length && span.Source >= 0 && (atEnd ? offset > span.Start : offset < span.Start + span.Length))
                return Math.Min(source.Length, span.Source + offset - span.Start);
            if (span.Start + span.Length <= offset && span.Source >= 0) before = span;
        }
        if (before is { } known) return Math.Min(source.Length, known.Source + known.Length);
        return atEnd ? LineEnd(Math.Max(0, b.SourceEnd - 1)) : LineStart(Math.Max(0, b.SourceStart));
    }

    private void CopyToClipboard(string text)
    {
        if (text.Length > 0 && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) _ = clipboard.SetTextAsync(text);
    }

    internal void Copy() => CopyToClipboard(SelectedText());

    internal void CopyMarkdown() => CopyToClipboard(SelectedMarkdown());

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        MenuItem Item(string key, KeyGesture? gesture, Action action)
        {
            var item = new MenuItem { Header = Loc.Get(key), Tag = key };
            item.InputGesture = gesture;
            item.Click += (_, _) => action();
            return item;
        }
        var copy = Item(K.ReaderCopy, new KeyGesture(Key.C, PlatformCtrl), Copy);
        var markdown = Item(K.ReaderCopyMarkdown, new KeyGesture(Key.C, PlatformCtrl | KeyModifiers.Shift), CopyMarkdown);
        menu.Items.Add(copy);
        menu.Items.Add(markdown);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(K.ReaderSelectAll, new KeyGesture(Key.A, PlatformCtrl), SelectAll));
        menu.Opening += (_, _) =>
        {
            copy.IsEnabled = markdown.IsEnabled = HasSelection;
            RetranslateMenu();
        };
        return menu;
    }

    /// <summary>The items of the context menu in the interface language.</summary>
    private void RetranslateMenu()
    {
        if (ContextMenu is null) return;
        foreach (var item in ContextMenu.Items.OfType<MenuItem>())
            if (item.Tag is string key) item.Header = Loc.Get(key);
    }

    // ---- hit testing ----

    /// <summary>The position in the text at a point of the page; a point in a gap or beside a block takes the nearest place of the block.</summary>
    internal int PositionAt(Point point)
    {
        if (_doc.Blocks.Count == 0) return 0;
        int index = BlockAtY(point.Y);
        var b = _doc.Blocks[index];
        if (!_laid.TryGetValue(index, out var laid) || laid.Boxes.Count == 0)
            return point.Y < _tops[index] + _heights[index] / 2 || b.Text.Length == 0 ? b.DocStart : b.DocStart + b.Text.Length;
        double top = _tops[index];
        if (point.Y < top) return b.DocStart;
        var box = laid.Boxes[0];
        double scroll = ScrollOf(b), x = point.X + scroll;
        (double Y, double X) best = (double.MaxValue, double.MaxValue);
        foreach (var candidate in laid.Boxes)
        {
            // the boxes of a row of cells stand side by side, those of a paragraph cut at a tall picture one under the other
            double y = point.Y - top, dy = y < candidate.Origin.Y ? candidate.Origin.Y - y : Math.Max(0, y - (candidate.Origin.Y + candidate.Layout.Height));
            double dx = x < candidate.Origin.X ? candidate.Origin.X - x : Math.Max(0, x - (candidate.Origin.X + Math.Max(candidate.Layout.WidthIncludingTrailingWhitespace, candidate.Clip > 0 ? candidate.Clip : candidate.Layout.MaxWidth)));
            if (dy < best.Y || dy == best.Y && dx < best.X) { best = (dy, dx); box = candidate; }
        }
        var local = new Point(x - box.Origin.X, point.Y - top - box.Origin.Y);
        if (local.Y > box.Layout.Height) local = new Point(local.X, box.Layout.Height - 1);
        return b.DocStart + box.Start + box.Hit(local);
    }

    /// <summary>The link under a point, or -1.</summary>
    internal int LinkAt(Point point)
    {
        if (_doc.Links.Count == 0 || _doc.Blocks.Count == 0) return -1;
        int index = BlockAtY(point.Y);
        if (!_laid.TryGetValue(index, out var laid)) return -1;
        var b = _doc.Blocks[index];
        foreach (var box in laid.Boxes)
        {
            double scroll = ScrollOf(b);
            var origin = new Point(box.Origin.X - scroll, _tops[index] + box.Origin.Y);
            foreach (var span in b.Spans)
            {
                if (span.Link < 0) continue;
                int from = Math.Max(span.Start, box.Start), to = Math.Min(span.Start + span.Length, box.Start + box.Length);
                if (to <= from) continue;
                foreach (var r in box.Range(from - box.Start, to - box.Start))
                    if (new Rect(origin.X + r.X, origin.Y + r.Y, r.Width, r.Height).Contains(point)) return span.Link;
            }
        }
        return -1;
    }

    /// <summary>Follows a link: a heading or footnote of the page scrolls there, an address goes to the handler that asks the user first.</summary>
    internal void ActivateLink(int link)
    {
        if (link < 0 || link >= _doc.Links.Count) return;
        var target = _doc.Links[link];
        if (target.Line is { } line) _ctx.JumpToLine?.Invoke(line);
        else if (target.External is { } uri) _ctx.Open?.Invoke(uri);
    }

    /// <summary>The page position (top of the first line) where a link is drawn, or null while its block is not laid out.</summary>
    internal Point? LinkPoint(int link)
    {
        foreach (var (index, laid) in _laid)
        {
            var b = _doc.Blocks[index];
            foreach (var span in b.Spans)
                if (span.Link == link)
                    foreach (var box in laid.Boxes)
                    {
                        int from = Math.Max(span.Start, box.Start), to = Math.Min(span.Start + span.Length, box.Start + box.Length);
                        if (to <= from) continue;
                        foreach (var r in box.Range(from - box.Start, to - box.Start))
                            return new Point(box.Origin.X + r.X + r.Width / 2, _tops[index] + box.Origin.Y + r.Y + r.Height / 2);
                    }
        }
        return null;
    }

    // ---- words ----

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_' || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.NonSpacingMark;

    private (int Start, int End) WordAt(int position)
    {
        int index = _doc.BlockAt(position);
        var b = _doc.Blocks[index];
        string text = b.Text;
        int offset = Math.Clamp(position - b.DocStart, 0, text.Length);
        if (text.Length == 0) return (b.DocStart, b.DocStart);
        if (offset == text.Length || offset > 0 && !IsWord(text[offset]) && IsWord(text[offset - 1])) offset--;
        bool word = IsWord(text[offset]);
        int start = offset, end = offset + 1;
        if (word || !char.IsWhiteSpace(text[offset]))
        {
            Func<char, bool> same = word ? IsWord : c => !IsWord(c) && !char.IsWhiteSpace(c);
            while (start > 0 && same(text[start - 1])) start--;
            while (end < text.Length && same(text[end])) end++;
        }
        return (b.DocStart + start, b.DocStart + end);
    }

    private (int Start, int End) BlockRange(int position)
    {
        var b = _doc.Blocks[_doc.BlockAt(position)];
        return (b.DocStart, b.DocStart + b.Text.Length);
    }

    private (int Start, int End) Unit(int position) => _dragUnit switch
    {
        2 => WordAt(position),
        3 => BlockRange(position),
        _ => (position, position),
    };

    // ---- pointer ----

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;
        Focus();
        var p = point.Position;
        for (int i = _realized.First; i <= _realized.Last && i < _doc.Blocks.Count; i++)
            if (CodeThumb(i) is { } thumb && thumb.Inflate(4).Contains(p))
            {
                (_thumbDrag, _thumbGroup, _thumbStartX, _thumbStartScroll) = (true, _doc.Blocks[i].Group, p.X, _codeScroll.GetValueOrDefault(_doc.Blocks[i].Group));
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
        int position = PositionAt(p);
        _dragUnit = Math.Clamp(e.ClickCount, 1, 3);
        _pressLink = _dragUnit == 1 && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? LinkAt(p) : -1;
        if (_dragUnit == 1 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _dragOrigin = (_anchor, _anchor);
            _caret = position;
        }
        else
        {
            _dragOrigin = Unit(position);
            (_anchor, _caret) = _dragOrigin;
        }
        (_dragging, _moved, _pressPoint, _lastPointer) = (true, false, p, p);
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_thumbDrag)
        {
            var (overflow, clip) = CodeOverflow(_thumbGroup);
            if (overflow > 0 && clip > 0)
            {
                double thumb = Math.Max(24, clip * clip / (clip + overflow));
                _codeScroll[_thumbGroup] = Math.Clamp(_thumbStartScroll + (p.X - _thumbStartX) * overflow / Math.Max(1, clip - thumb), 0, overflow);
                InvalidateVisual();
            }
            return;
        }
        if (!_dragging)
        {
            int link = LinkAt(p);
            if (link != _hoverLink)
            {
                _hoverLink = link;
                ToolTip.SetTip(this, link >= 0 ? _doc.Links[link].Target : null);
                InvalidateVisual();
            }
            Cursor = link >= 0 ? LinkCursor : TextCursor;
            return;
        }
        _lastPointer = p;
        if (!_moved && (Math.Abs(p.X - _pressPoint.X) > 3 || Math.Abs(p.Y - _pressPoint.Y) > 3)) _moved = true;
        ExtendTo(p);
        var view = View;
        if (p.Y < view.Top || p.Y > view.Bottom)
        {
            _autoScroll ??= new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Input, (_, _) => AutoScroll());
            _autoScroll.Start();
        }
        else _autoScroll?.Stop();
    }

    private void ExtendTo(Point p)
    {
        int position = PositionAt(p);
        var unit = Unit(position);
        if (unit.Start < _dragOrigin.Start) (_anchor, _caret) = (_dragOrigin.End, unit.Start);
        else (_anchor, _caret) = (_dragOrigin.Start, Math.Max(unit.End, _dragOrigin.End));
        if (_dragUnit == 1) _caret = position;
        InvalidateVisual();
    }

    /// <summary>One step of scrolling while the pointer is held past the top or bottom of the view; the further past, the faster.</summary>
    private void AutoScroll()
    {
        if (!_dragging || this.FindAncestorOfType<ScrollViewer>() is not { } scroller) { _autoScroll?.Stop(); return; }
        var view = View;
        double past = _lastPointer.Y < view.Top ? _lastPointer.Y - view.Top : _lastPointer.Y > view.Bottom ? _lastPointer.Y - view.Bottom : 0;
        if (past == 0) { _autoScroll?.Stop(); return; }
        double step = Math.Clamp(past / 2, -60, 60);
        double before = scroller.Offset.Y;
        scroller.Offset = new Vector(scroller.Offset.X, Math.Max(0, before + step));
        double moved = scroller.Offset.Y - before;
        _lastPointer = new Point(_lastPointer.X, _lastPointer.Y + moved);
        ExtendTo(_lastPointer);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _autoScroll?.Stop();
        if (_thumbDrag)
        {
            _thumbDrag = false;
            e.Pointer.Capture(null);
            return;
        }
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        if (!_moved && _pressLink >= 0 && _pressLink == LinkAt(e.GetPosition(this)))
        {
            _anchor = _caret;
            ActivateLink(_pressLink);
        }
        _pressLink = -1;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverLink >= 0) { _hoverLink = -1; InvalidateVisual(); }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        double sideways = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? e.Delta.Y : e.Delta.X;
        if (sideways == 0) return;
        int index = BlockAtY(e.GetPosition(this).Y);
        if (index >= _doc.Blocks.Count || _doc.Blocks[index].Kind is not (ReaderBlockKind.Code or ReaderBlockKind.TableRow)) return;
        int group = _doc.Blocks[index].Group;
        if (CodeOverflow(group).Overflow <= 0) return;
        ScrollCode(group, -sideways * 3 * Em);
        e.Handled = true;
    }

    // ---- keyboard ----

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        bool control = e.KeyModifiers.HasFlag(PlatformCtrl);
        switch (e.Key)
        {
            case Key.A when control:
                SelectAll();
                break;
            case Key.C when control && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                CopyMarkdown();
                break;
            case Key.C when control:
            case Key.Insert when control:
                Copy();
                break;
            case Key.Tab when e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift && _doc.Links.Count > 0:
            {
                int next = _focusedLink + (e.KeyModifiers == KeyModifiers.Shift ? -1 : 1);
                if (next < 0 || next >= _doc.Links.Count)
                {
                    // past the last link the focus leaves the page
                    _focusedLink = -1;
                    ClearValue(Avalonia.Automation.AutomationProperties.NameProperty);
                    InvalidateVisual();
                    return;
                }
                _focusedLink = next;
                var focused = _doc.Links[next];
                // a screen reader reads the name of the focused element: the link the keyboard is on
                Avalonia.Automation.AutomationProperties.SetName(this, focused.External is null ? focused.Label : Loc.Get(K.PreviewPanelLink, ("text", focused.Label), ("url", focused.Target)));
                BringLinkIntoView(next);
                InvalidateVisual();
                break;
            }
            case Key.Enter or Key.Space when e.KeyModifiers == KeyModifiers.None && _focusedLink >= 0:
                ActivateLink(_focusedLink);
                break;
            case Key.Left or Key.Right or Key.Home or Key.End when e.KeyModifiers == KeyModifiers.None && CodeGroupAtCaret() is >= 0 and var group:
                ScrollCode(group, e.Key switch { Key.Left => -CodeKeyStep, Key.Right => CodeKeyStep, Key.Home => double.NegativeInfinity, _ => double.PositiveInfinity });
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>How far Left and Right scroll a code block: three characters of the code font.</summary>
    private double CodeKeyStep => 3 * 0.85 * Em * 0.6;

    /// <summary>The code block or table the caret stands in when it reaches past its box, or -1.</summary>
    internal int CodeGroupAtCaret()
    {
        if (_doc.Blocks.Count == 0) return -1;
        var b = _doc.Blocks[_doc.BlockAt(_caret)];
        return b.Kind is ReaderBlockKind.Code or ReaderBlockKind.TableRow && CodeOverflow(b.Group).Overflow > 0 ? b.Group : -1;
    }

    /// <summary>The keys the page handles itself, which the window then does not pass to its commands: copy, select all, and Tab and Enter while there are links.</summary>
    internal bool OwnsKey(KeyEventArgs e)
    {
        bool control = e.KeyModifiers.HasFlag(PlatformCtrl);
        return e.Key switch
        {
            Key.A or Key.C => control && !e.KeyModifiers.HasFlag(KeyModifiers.Alt),
            Key.Insert => control,
            Key.Tab => e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift && _doc.Links.Count > 0,
            Key.Enter or Key.Space => e.KeyModifiers == KeyModifiers.None && _focusedLink >= 0,
            Key.Left or Key.Right or Key.Home or Key.End => e.KeyModifiers == KeyModifiers.None && CodeGroupAtCaret() >= 0,
            _ => false,
        };
    }

    private static KeyModifiers PlatformCtrl =>OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    internal int FocusedLink => _focusedLink;

    private void BringLinkIntoView(int link)
    {
        for (int i = 0; i < _doc.Blocks.Count; i++)
            if (_doc.Blocks[i].Spans.Any(s => s.Link == link))
            {
                var bounds = BlockBounds(i);
                if (this.FindAncestorOfType<ScrollViewer>() is { } scroller && (bounds.Top < View.Top || bounds.Bottom > View.Bottom))
                    scroller.Offset = new Vector(scroller.Offset.X, Math.Max(0, bounds.Top - scroller.Viewport.Height / 3));
                return;
            }
    }
}
