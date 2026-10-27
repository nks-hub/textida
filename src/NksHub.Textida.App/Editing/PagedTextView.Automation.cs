using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NksHub.Textida.Accessibility;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Editing;

/// <summary>The paged surface as <see cref="ITextAutomationSurface"/>, so the screen reader bridges of the normal editor serve it too.</summary>
internal sealed partial class PagedTextView
{
    private const int WindowMarginLines = 200;
    private const int WindowRebuildLines = 50;

    /// <summary>
    /// A window of whole lines around the first line on screen. A file of gigabytes has no UTF-16 offsets for all of its text, so the text
    /// the screen reader sees is this window: offsets count from its first line, and a window that moves is reported as a text change.
    /// The window is rebuilt when a revision or the index changes or when the view scrolls near its edge; it is read through the same line
    /// loader as the drawing, so blocks of the file do not matter.
    /// </summary>
    /// <remarks>
    /// shortcut: a selection that starts or ends outside the window is clipped to it, and a line longer than <see cref="MaxLineBytes"/> shows
    /// its first part; move the window to the caret when the selection spans more than a window (P7.1.7 for files over 10 GiB).
    /// </remarks>
    internal sealed class TextSurface : ITextAutomationSurface, IDisposable
    {
        private readonly PagedTextView _owner;
        private string _text = "";
        private int[] _starts = [0, 0];
        private long[] _bytes = [];
        private long[] _textEnds = [];
        private long[] _lineEnds = [];
        private LineView[] _views = [];
        private long _first;
        private long _revision = -1;
        private long _shown = -1;
        private bool _exact = true;
        private bool _atEnd;
        private int _version;
        private (int Version, long First) _raised = (0, 0);
        private string _raisedText = "";
        private (int, int, int) _selectionSeen;

        public TextSurface(PagedTextView owner)
        {
            _owner = owner;
            Ensure();
            _raised = (_version, _first);
            _raisedText = _text;
            _selectionSeen = SelectionKey();
            owner.ViewChanged += OnViewChanged;
            owner.Doc.Changed += OnDocChanged;
            owner.GotFocus += OnFocus;
            owner.LostFocus += OnFocus;
        }

        private Document Doc => _owner.Doc;
        private LinesSurface Canvas => _owner._canvas;

        /// <summary>Number of the file line shown first in the window (zero based).</summary>
        public long FirstLine
        {
            get { Ensure(); return _first; }
        }

        public long Revision => Doc.Revision;
        public int Length { get { Ensure(); return _text.Length; } }
        public int LineCount { get { Ensure(); return _starts.Length - 1; } }
        public bool IsReadOnly => !_owner.Indexed || _owner.ReadOnlyReason is not null;
        public bool HasFocus => _owner.IsFocused;
        public void Focus() => _owner.Focus();

        public event Action<TextChange>? TextChanged;
        public event Action? SelectionChanged;
        public event Action? FocusChanged;

        public string GetText(int start, int length)
        {
            Ensure();
            if (start < 0 || length < 0 || start + length > _text.Length) throw new ArgumentOutOfRangeException(nameof(start));
            return _text.Substring(start, length);
        }

        public int LineStart(int line)
        {
            Ensure();
            if (line < 0 || line >= _starts.Length) throw new ArgumentOutOfRangeException(nameof(line));
            return _starts[line];
        }

        public int LineOfOffset(int offset)
        {
            Ensure();
            offset = Math.Clamp(offset, 0, _text.Length);
            int i = Array.BinarySearch(_starts, 0, _starts.Length - 1, offset);
            return i >= 0 ? i : ~i - 1;
        }

        // ---- the window ----

        private void Ensure()
        {
            long top = _owner.TopLine, shown = _owner.ShownLines;
            int visible = Math.Max(1, Canvas.VisibleLines), count = _views.Length;
            bool stale = _version == 0 || _revision != Doc.Revision || _exact != _owner.LinesExact || _atEnd && _shown != shown
                || top < _first || top + visible > _first + count && !_atEnd
                || top < _first + WindowRebuildLines && _first > _owner.FirstKnownLine
                || top + visible > _first + count - WindowRebuildLines && !_atEnd;
            if (stale) Rebuild(top, visible);
        }

        private void Rebuild(long top, int visible)
        {
            long first = Math.Max(_owner.FirstKnownLine, top - WindowMarginLines), last = top + visible + WindowMarginLines;
            var text = new StringBuilder();
            var starts = new List<int>();
            var bytes = new List<long>();
            var textEnds = new List<long>();
            var lineEnds = new List<long>();
            var views = new List<LineView>();
            long endByte = 0;
            var terminator = new byte[8];
            for (long line = first; line < last; line++)
            {
                if (_owner.GetLine(line) is not { } view || !_owner.TryBounds(line, out long start, out long end, out long next))
                    break;
                starts.Add(text.Length);
                bytes.Add(start);
                views.Add(view);
                textEnds.Add(view.Start + view.Map.ByteOffsetOf(view.Map.Text.Length));
                lineEnds.Add(end);
                text.Append(view.Map.Text);
                if (next > end)
                {
                    int n = Doc.Read(end, terminator.AsSpan(0, (int)Math.Min(8, next - end)));
                    for (int i = 0; i < n; i++) text.Append((char)terminator[i]);
                }
                endByte = next;
            }
            bool atEnd = _owner.GetLine(first + views.Count) is null;
            if (views.Count == 0) { starts.Add(0); bytes.Add(Doc.BomLength); endByte = Doc.BomLength; }
            bytes.Add(endByte);
            starts.Add(text.Length);

            _first = first;
            _starts = [.. starts];
            _bytes = [.. bytes];
            _textEnds = [.. textEnds];
            _lineEnds = [.. lineEnds];
            _views = [.. views];
            _atEnd = atEnd;
            _revision = Doc.Revision;
            _shown = _owner.ShownLines;
            _exact = _owner.LinesExact;
            _text = text.ToString();
            _version++;
        }

        // ---- mapping between window offsets and bytes ----

        private int ToOffset(long position)
        {
            if (_views.Length == 0 || position <= _bytes[0]) return 0;
            if (position >= _bytes[^1]) return _text.Length;
            int i = Array.BinarySearch(_bytes, 0, _bytes.Length - 1, position);
            if (i < 0) i = ~i - 1;
            var view = _views[i];
            int length = view.Map.Text.Length, column;
            if (position >= _lineEnds[i]) column = length + (int)Math.Min(position - _lineEnds[i], _starts[i + 1] - _starts[i] - length);
            else if (position >= _textEnds[i]) column = length;
            else column = view.Map.CharIndexOf(position - view.Start);
            return _starts[i] + column;
        }

        private long ToBytes(int offset)
        {
            if (_views.Length == 0) return Doc.BomLength;
            offset = Math.Clamp(offset, 0, _text.Length);
            int i = Math.Min(LineOfOffset(offset), _views.Length - 1);
            var view = _views[i];
            int column = offset - _starts[i], length = view.Map.Text.Length;
            if (column > length) return Math.Min(_lineEnds[i] + (column - length), _bytes[i + 1]);
            while (column > 0 && !view.Map.IsCharBoundary(column)) column--;
            return view.Start + view.Map.ByteOffsetOf(column);
        }

        // ---- selection ----

        public TextSpan Selection
        {
            get
            {
                Ensure();
                int a = ToOffset(_owner.SelectionRange.Start), b = ToOffset(_owner.SelectionRange.End);
                return new(a, b - a);
            }
        }

        public int CaretOffset
        {
            get { Ensure(); return ToOffset(_owner.CaretOffset); }
        }

        public void SetSelection(int start, int length)
        {
            Ensure();
            long a = ToBytes(start), b = ToBytes(start + length);
            if (a == b) _owner.SelectBytes(b, b);
            else _owner.SelectBytes(a, b);
        }

        private (int, int, int) SelectionKey()
        {
            var selection = Selection;
            return (selection.Start, selection.Length, CaretOffset);
        }

        // ---- geometry ----

        public ScreenRect ScreenBounds => AutomationScreen.ToScreen(_owner, new Rect(_owner.Bounds.Size));

        public TextSpan VisibleRange
        {
            get
            {
                Ensure();
                long shift = _owner.TopLine - _first;
                if (_views.Length == 0 || shift < 0 || shift >= _views.Length) return default;
                int top = (int)shift, end = (int)Math.Min(_views.Length, shift + Math.Max(1, Canvas.VisibleLines));
                return new(_starts[top], _starts[end] - _starts[top]);
            }
        }

        public IReadOnlyList<ScreenRect> GetRangeBounds(int start, int length)
        {
            Ensure();
            var rects = new List<ScreenRect>();
            if (_views.Length == 0) return rects;
            int end = start + length, firstLine = LineOfOffset(start);
            for (int i = firstLine; i < _views.Length; i++)
            {
                if (i > firstLine && (length == 0 || _starts[i] >= end)) break;
                long fileLine = _first + i;
                if (fileLine > _owner.TopLine + Canvas.VisibleLines) break;
                if (fileLine < _owner.TopLine) continue;
                var view = _views[i];
                int text = view.Map.Text.Length;
                int from = Math.Clamp(start - _starts[i], 0, text), to = Math.Clamp(end - _starts[i], 0, text);
                while (from > 0 && !view.Map.IsCharBoundary(from)) from--;
                while (to > 0 && !view.Map.IsCharBoundary(to)) to--;
                double x1 = Canvas.XOf(view, from), x2 = Canvas.XOf(view, to) + (end - _starts[i] > text ? Canvas.DigitWidth : 0);
                var local = new Rect(Canvas.TextLeft - _owner._hScroll + x1, (fileLine - _owner.TopLine) * Canvas.LineHeight, Math.Max(1, x2 - x1), Canvas.LineHeight);
                rects.Add(AutomationScreen.ToScreen(Canvas, local));
            }
            return rects;
        }

        public int OffsetFromPoint(double x, double y)
        {
            Ensure();
            if (TopLevel.GetTopLevel(Canvas) is null || _views.Length == 0) return -1;
            double unit = AutomationScreen.PixelsPerUnit(Canvas);
            var local = Canvas.PointToClient(new PixelPoint((int)Math.Round(x * unit), (int)Math.Round(y * unit)));
            if (!new Rect(Canvas.Bounds.Size).Contains(local)) return -1;
            long line = _owner.TopLine + (long)Math.Floor(local.Y / Canvas.LineHeight) - _first;
            if (line < 0 || line >= _views.Length) return -1;
            var view = _views[line];
            return _starts[line] + Canvas.IndexAtX(view, local.X - Canvas.TextLeft + _owner._hScroll);
        }

        public void ScrollIntoView(int start, int length, bool alignToTop)
        {
            Ensure();
            if (_views.Length == 0) return;
            long line = _first + Math.Min(LineOfOffset(start), _views.Length - 1);
            int visible = Math.Max(1, Canvas.VisibleLines - 1);
            if (alignToTop || line < _owner.TopLine || line >= _owner.TopLine + visible)
                _owner._bar.Value = Math.Clamp(alignToTop ? line : line - visible / 2, _owner._bar.Minimum, _owner._bar.Maximum);
        }

        public void InvokeOnUiThread(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess()) action();
            else Dispatcher.UIThread.Invoke(action);
        }

        // ---- events ----

        private void OnFocus(object? sender, RoutedEventArgs e) => FocusChanged?.Invoke();
        private void OnViewChanged() => Publish();
        private void OnDocChanged(object? sender, DocumentChangedEventArgs e) => Publish();

        private void Publish()
        {
            Ensure();
            if (_version != _raised.Version)
            {
                var change = _first == _raised.First ? Diff(_raisedText, _text) : new TextChange(0, _raisedText.Length, _text.Length, Doc.Revision);
                _raised = (_version, _first);
                _raisedText = _text;
                TextChanged?.Invoke(change);
            }
            var key = SelectionKey();
            if (key == _selectionSeen) return;
            _selectionSeen = key;
            SelectionChanged?.Invoke();
        }

        private TextChange Diff(string old, string now)
        {
            int prefix = 0, max = Math.Min(old.Length, now.Length);
            while (prefix < max && old[prefix] == now[prefix]) prefix++;
            int suffix = 0;
            while (suffix < max - prefix && old[old.Length - 1 - suffix] == now[now.Length - 1 - suffix]) suffix++;
            return new TextChange(prefix, old.Length - prefix - suffix, now.Length - prefix - suffix, Doc.Revision);
        }

        public void Dispose()
        {
            _owner.ViewChanged -= OnViewChanged;
            Doc.Changed -= OnDocChanged;
            _owner.GotFocus -= OnFocus;
            _owner.LostFocus -= OnFocus;
        }
    }

    /// <summary>First line the view can show: the first line of the detour, else the first line of the file.</summary>
    private long FirstKnownLine => _detour?.BaseLine ?? 0;
}
