using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex;

namespace NksHub.Textida.App.Editing;

internal sealed partial class HexView
{
    internal readonly record struct Hit(long Offset, HexColumn Column);

    /// <summary>Draws the grid. All geometry is in character cells of the monospace font, so the three columns line up whatever the font is.</summary>
    private sealed class HexSurface(HexView owner) : Control
    {
        private const double Pad = 10;
        private double _cw = 8, _rh = 18;
        private double _hexLeft, _charLeft;
        private int[] _hexChar = [];
        private int _hexChars, _digits;
        private int _layoutKey;
        private Typeface _face;

        public long RenderedAt { get; private set; }
        public double RenderMs { get; private set; }
        private (long Revision, long Caret, long Anchor, long Top, int Mode, int Nibble) _drawn = (-1, -1, -1, -1, -1, -1);

        public HexSurface Self => this;

        public int VisibleRows => Math.Max(1, (int)Math.Floor(Math.Max(0, Bounds.Height) / _rh));

        private FormattedText Format(string text, IBrush brush) => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _face, owner._options.FontSize, brush);

        protected override Size MeasureOverride(Size availableSize)
        {
            _face = new Typeface(new FontFamily(owner._options.FontFamily));
            var sample = Format(new string('0', 32), Brushes.Black);
            _cw = sample.Width / 32;
            _rh = Math.Ceiling(sample.Height) + 2;
            _layoutKey = -1;
            return availableSize;
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            owner.UpdateScrollRange();
        }

        private void EnsureLayout()
        {
            int bpr = owner._bytesPerRow, group = owner._group;
            long length = owner.Doc.ByteLength;
            int digits = Math.Max(8, owner._decimalOffsets ? length.ToString(CultureInfo.InvariantCulture).Length : length.ToString("X", CultureInfo.InvariantCulture).Length);
            int key = HashCode.Combine(bpr, group, digits, owner._decimalOffsets);
            if (key == _layoutKey) return;
            _layoutKey = key;
            _digits = digits;
            _hexChar = new int[bpr];
            int pos = 0;
            for (int col = 0; col < bpr; col++)
            {
                if (col > 0)
                {
                    pos += 1;
                    if (IsGap(col, bpr, group)) pos += 1;
                }
                _hexChar[col] = pos;
                pos += 2;
            }
            _hexChars = pos;
            _hexLeft = Pad + (digits + 2) * _cw;
            _charLeft = _hexLeft + (_hexChars + 2) * _cw;
        }

        private static bool IsGap(int col, int bpr, int group) => (group > 1 && col % group == 0) || col == bpr / 2;

        private double HexX(int col) => _hexLeft + _hexChar[col] * _cw;

        public Hit? HitTest(Point p)
        {
            EnsureLayout();
            int bpr = owner._bytesPerRow;
            long row = owner._firstRow + (long)Math.Floor(Math.Max(0, p.Y) / _rh);
            long length = owner.Doc.ByteLength;
            HexColumn column;
            int col;
            if (p.X >= _charLeft - _cw / 2)
            {
                column = HexColumn.Text;
                col = (int)Math.Clamp(Math.Floor((p.X - _charLeft) / _cw), 0, bpr - 1);
            }
            else
            {
                column = HexColumn.Hex;
                col = 0;
                for (int i = 0; i < bpr; i++) if (HexX(i) - _cw / 2 <= p.X) col = i;
            }
            long offset = Math.Min(row * bpr + col, owner.Insert ? length : Math.Max(0, length - 1));
            return new Hit(Math.Max(0, offset), column);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            e.Pointer.Capture(this);
            owner.PointerPressed(e.GetPosition(this), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) owner.PointerMoved(e.GetPosition(this));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            e.Pointer.Capture(null);
            owner.PointerReleased();
        }

        public override void Render(DrawingContext context)
        {
            long started = Stopwatch.GetTimestamp();
            Draw(context);
            long ended = Stopwatch.GetTimestamp();
            var e = owner.Editor;
            var state = (owner.Doc.Revision, e.Caret, e.Anchor, owner._firstRow, (e.Insert ? 1 : 0) + (e.Column == HexColumn.Text ? 2 : 0), e.Nibble);
            if (_drawn == state) return;
            _drawn = state;
            RenderedAt = ended;
            RenderMs = (ended - started) * 1000.0 / Stopwatch.Frequency;
        }

        private IBrush Brush(string key, IBrush fallback) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;

        private void Draw(DrawingContext context)
        {
            var back = Brush("BgEditor", Brushes.White);
            var fore = Brush("Fg", Brushes.Black);
            var muted = Brush("FgMuted", Brushes.Gray);
            var dim = Brush("LineNumbers", Brushes.Gray);
            var accent = Brush("Accent", Brushes.Teal);
            var soft = Brush("AccentSoft", Brushes.LightGray);
            var selection = Brush("SelectionBg", Brushes.LightBlue);
            var currentRow = Brush("CurrentLine", Brushes.Transparent);
            var danger = Brush("Danger", Brushes.Red);
            var warn = Brush("WarnBg", Brushes.LightYellow);
            context.FillRectangle(back, new Rect(Bounds.Size));
            EnsureLayout();

            var doc = owner.Doc;
            var editor = owner.Editor;
            int bpr = owner._bytesPerRow;
            long length = doc.ByteLength;
            int rows = (int)Math.Ceiling(Bounds.Height / _rh);
            long first = owner._firstRow * bpr;
            var set = editor.Charset;

            byte[] buf;
            long bufStart = Math.Max(0, first - set.Lookbehind);
            try
            {
                long end = Math.Min(length, first + (long)rows * bpr + set.Lookahead);
                buf = new byte[Math.Max(0, end - bufStart)];
                for (int done = 0; done < buf.Length;)
                {
                    int n = doc.Read(bufStart + done, buf.AsSpan(done));
                    if (n <= 0) break;
                    done += n;
                }
            }
            catch (IOException)
            {
                owner.FileChangedBehind();
                return;
            }

            var ranges = owner.ChangedRanges();
            var sel = editor.HasSelection ? editor.Selection : default;
            bool hasSel = editor.HasSelection;
            var cells = new HexCell[bpr];
            var hexBuilder = new StringBuilder(_hexChars);
            long caret = editor.Caret;

            for (int r = 0; r < rows; r++)
            {
                long rowOffset = first + (long)r * bpr;
                if (rowOffset > length) break;
                double y = r * _rh;
                int n = (int)Math.Min(bpr, length - rowOffset);
                bool caretRow = caret >= rowOffset && caret < rowOffset + bpr;
                if (caretRow) context.FillRectangle(currentRow, new Rect(0, y, Bounds.Width, _rh));

                string offsetText = owner._decimalOffsets ? rowOffset.ToString("D" + _digits, CultureInfo.InvariantCulture) : rowOffset.ToString("X" + _digits, CultureInfo.InvariantCulture);
                context.DrawText(Format(offsetText, dim), new Point(Pad, y + 1));

                // changed bytes: a tint and an underline, so the mark does not rest on colour alone
                for (int k = FirstRangeAfter(ranges, rowOffset); k < ranges.Count && ranges[k].Start < rowOffset + n; k++)
                {
                    int a = (int)Math.Max(0, ranges[k].Start - rowOffset), z = (int)Math.Min(n, ranges[k].End - rowOffset);
                    if (z <= a) continue;
                    Mark(context, soft, y, a, z);
                    context.FillRectangle(accent, new Rect(HexX(a), y + _rh - 2, HexX(z - 1) + 2 * _cw - HexX(a), 1.5));
                    context.FillRectangle(accent, new Rect(_charLeft + a * _cw, y + _rh - 2, (z - a) * _cw, 1.5));
                }
                if (hasSel && sel.Start < rowOffset + n && sel.End > rowOffset)
                    Mark(context, selection, y, (int)Math.Max(0, sel.Start - rowOffset), (int)Math.Min(n, sel.End - rowOffset));

                if (n > 0)
                {
                    hexBuilder.Clear();
                    for (int col = 0; col < n; col++)
                    {
                        if (col > 0)
                        {
                            hexBuilder.Append(' ');
                            if (IsGap(col, bpr, owner._group)) hexBuilder.Append(' ');
                        }
                        byte b = buf[rowOffset - bufStart + col];
                        hexBuilder.Append(HexDigit(b >> 4)).Append(HexDigit(b & 15));
                    }
                    var hex = Format(hexBuilder.ToString(), fore);
                    for (int col = 0; col < n; col++)
                    {
                        byte b = buf[rowOffset - bufStart + col];
                        if (b == 0) hex.SetForegroundBrush(dim, _hexChar[col], 2);
                        else if (b == 0xFF) hex.SetForegroundBrush(accent, _hexChar[col], 2);
                        else if (b < 0x20 || b == 0x7F) hex.SetForegroundBrush(muted, _hexChar[col], 2);
                    }
                    context.DrawText(hex, new Point(_hexLeft, y + 1));

                    int window = (int)(rowOffset - bufStart);
                    int before = Math.Min(window, set.Lookbehind);
                    set.Map(buf.AsSpan(window - before, Math.Min(buf.Length - (window - before), before + n + set.Lookahead)), before, n, rowOffset - before, cells);
                    DrawChars(context, cells, n, y, fore, dim, danger, warn);
                }
                if (caret >= rowOffset && caret <= rowOffset + n && (caret < rowOffset + bpr)) DrawCaret(context, accent, muted, y, (int)(caret - rowOffset), caret < length);
            }
        }

        private static int FirstRangeAfter(IReadOnlyList<ByteRange> ranges, long offset)
        {
            int lo = 0, hi = ranges.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (ranges[mid].End <= offset) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        private static char HexDigit(int v) => (char)(v < 10 ? '0' + v : 'A' + v - 10);

        private void Mark(DrawingContext context, IBrush brush, double y, int from, int to)
        {
            context.FillRectangle(brush, new Rect(HexX(from) - _cw * 0.5, y, HexX(to - 1) + 2 * _cw - HexX(from) + _cw * 0.5 + 1, _rh));
            context.FillRectangle(brush, new Rect(_charLeft + from * _cw, y, (to - from) * _cw, _rh));
        }

        private void DrawChars(DrawingContext context, HexCell[] cells, int n, double y, IBrush fore, IBrush dim, IBrush danger, IBrush warn)
        {
            var run = new StringBuilder(n);
            int runStart = 0;
            void Flush(int upTo)
            {
                if (run.Length > 0) context.DrawText(Format(run.ToString(), fore), new Point(_charLeft + runStart * _cw, y + 1));
                run.Clear();
                runStart = upTo;
            }
            for (int i = 0; i < n; i++)
            {
                var cell = cells[i];
                if (cell.Kind == HexCellKind.Invalid) context.FillRectangle(warn, new Rect(_charLeft + i * _cw, y, _cw, _rh));
                if (cell.Glyph.Length == 1 && cell.Kind is HexCellKind.Normal)
                {
                    if (run.Length == 0) runStart = i;
                    run.Append(cell.Glyph);
                    continue;
                }
                Flush(i + 1);
                var brush = cell.Kind switch { HexCellKind.Invalid => danger, HexCellKind.Normal => fore, _ => dim };
                context.DrawText(Format(cell.Glyph, brush), new Point(_charLeft + i * _cw, y + 1));
            }
            Flush(n);
        }

        private void DrawCaret(DrawingContext context, IBrush accent, IBrush muted, double y, int col, bool onByte)
        {
            bool hexActive = owner.Editor.Column == HexColumn.Hex;
            double hx = HexX(col), cx = _charLeft + col * _cw;
            var active = new Pen(accent, 1.5);
            var quiet = new Pen(muted, 1);
            if (owner.Insert)
            {
                var bar = hexActive ? new Rect(hx - 1.5, y, 2, _rh) : new Rect(cx - 1, y, 2, _rh);
                context.FillRectangle(accent, bar);
                context.DrawRectangle(quiet, hexActive ? new Rect(cx, y + 1, _cw, _rh - 2) : new Rect(hx, y + 1, 2 * _cw, _rh - 2));
                return;
            }
            var hexBox = new Rect(hx - 1, y + 0.5, 2 * _cw + 2, _rh - 1);
            var charBox = new Rect(cx, y + 0.5, _cw, _rh - 1);
            context.DrawRectangle(hexActive ? active : quiet, hexBox);
            context.DrawRectangle(hexActive ? quiet : active, charBox);
            if (hexActive && onByte) context.FillRectangle(accent, new Rect(hx + owner.Editor.Nibble * _cw, y + _rh - 3.5, _cw, 2));
        }
    }
}
