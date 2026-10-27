using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using NksHub.Textida.Core.Compare;

namespace NksHub.Textida.App.Editing;

internal sealed partial class DiffView
{
    /// <summary>The rows of the two sides as pixels. Geometry is in character cells of the monospace font; text of a side is read in windows of lines from its index.</summary>
    private sealed class DiffSurface(DiffView owner) : Control
    {
        private double _cw = 8, _rh = 18, _hScroll;
        private Typeface _face;
        private readonly LineWindow _left = new(), _right = new();

        public int VisibleRows => Math.Max(1, (int)Math.Floor(Math.Max(0, Bounds.Height) / _rh));

        /// <summary>A run of lines of one side kept between frames, so scrolling by a row does not read the file again.</summary>
        private sealed class LineWindow
        {
            private LineIndex? _index;
            private long _first;
            private List<string> _lines = [];

            public string Get(LineIndex index, long line)
            {
                if (!ReferenceEquals(index, _index) || line < _first || line >= _first + _lines.Count)
                {
                    _index = index;
                    _first = Math.Max(0, line - 32);
                    _lines = index.ReadLines(_first, 256);
                }
                return line - _first < _lines.Count ? _lines[(int)(line - _first)] : "";
            }
        }

        private FormattedText Format(string text, IBrush brush) =>
            new(text.Replace("\t", "    "), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _face, owner._options.FontSize, brush);

        protected override Size MeasureOverride(Size availableSize)
        {
            _face = new Typeface(new FontFamily(owner._options.FontFamily));
            var sample = Format(new string('0', 32), Brushes.Black);
            _cw = sample.Width / 32;
            _rh = Math.Ceiling(sample.Height) + 2;
            return availableSize;
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            owner.SizeChanged();
        }

        public void ScrollHorizontally(double delta)
        {
            _hScroll = Math.Max(0, _hScroll - delta * _cw * 6);
            InvalidateVisual();
        }

        private IBrush Brush(string key, IBrush fallback) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            owner.Focus();
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || owner._session.Diff is not { } diff) return;
            var p = e.GetPosition(this);
            int hunk = diff.Layout.HunkAtRow(owner._top + (long)Math.Floor(Math.Max(0, p.Y) / _rh));
            if (hunk < 0) return;
            double paneW = PaneWidth;
            bool firstRow = owner._top + (long)Math.Floor(Math.Max(0, p.Y) / _rh) == diff.Layout.RowOfHunk(hunk);
            owner.SelectHunk(hunk);
            if (firstRow && p.X >= paneW && p.X < paneW + GutterWidth)
            {
                if (p.X < paneW + GutterWidth / 2 && owner._session.Left.CanReceive) owner.Copy(hunk, DiffSide.A);
                else if (p.X >= paneW + GutterWidth / 2 && owner._session.Right.CanReceive) owner.Copy(hunk, DiffSide.B);
            }
            e.Handled = true;
        }

        private double PaneWidth => Math.Max(0, (Bounds.Width - GutterWidth) / 2);

        public override void Render(DrawingContext context)
        {
            var back = Brush("BgEditor", Brushes.White);
            var fore = Brush("Fg", Brushes.Black);
            var dim = Brush("LineNumbers", Brushes.Gray);
            var accent = Brush("Accent", Brushes.Teal);
            var removed = Brush("DiffRemoved", Brushes.MistyRose);
            var added = Brush("DiffAdded", Brushes.Honeydew);
            var changed = Brush("WarnBg", Brushes.LightYellow);
            var filler = Brush("CurrentLine", Brushes.WhiteSmoke);
            var border = Brush("Border", Brushes.Gray);
            context.FillRectangle(back, new Rect(Bounds.Size));
            if (owner._session.Diff is not { } diff) return;

            var layout = diff.Layout;
            double paneW = PaneWidth, rightX = paneW + GutterWidth;
            int digits = Math.Max(3, Math.Max(layout.LinesA, layout.LinesB).ToString(CultureInfo.InvariantCulture).Length);
            double numW = (digits + 1) * _cw + 6;
            int rows = (int)Math.Ceiling(Bounds.Height / _rh);
            long top = owner._top;

            for (int pass = 0; pass < 2; pass++)
            {
                bool leftPane = pass == 0;
                double x0 = leftPane ? 0 : rightX;
                var index = leftPane ? diff.A : diff.B;
                var window = leftPane ? _left : _right;
                using (context.PushClip(new Rect(x0 + numW, 0, Math.Max(0, paneW - numW), Bounds.Height)))
                {
                    for (int i = 0; i < rows && top + i < layout.RowCount; i++)
                    {
                        var row = layout.Row(top + i);
                        long line = leftPane ? row.A : row.B;
                        double y = i * _rh;
                        context.FillRectangle(Tint(row, line, leftPane, removed, added, changed, filler), new Rect(x0 + numW, y, paneW - numW, _rh));
                        if (line < 0) continue;
                        string text = window.Get(index, line);
                        if (text.Length > 0) context.DrawText(Format(text, fore), new Point(x0 + numW + 4 - _hScroll, y + 1));
                    }
                }
                for (int i = 0; i < rows && top + i < layout.RowCount; i++)
                {
                    var row = layout.Row(top + i);
                    long line = leftPane ? row.A : row.B;
                    double y = i * _rh;
                    context.FillRectangle(Tint(row, line, leftPane, removed, added, changed, filler), new Rect(x0, y, numW, _rh));
                    if (line < 0) continue;
                    var number = Format((line + 1).ToString(CultureInfo.InvariantCulture), dim);
                    context.DrawText(number, new Point(x0 + numW - 4 - number.Width, y + 1));
                }
            }

            // the gutter: an arrow pair on the first row of each block, a bar on the following rows
            context.FillRectangle(filler, new Rect(paneW, 0, GutterWidth, Bounds.Height));
            var pen = new Pen(accent, 1.5);
            for (int i = 0; i < rows && top + i < layout.RowCount; i++)
            {
                long r = top + i;
                int h = layout.HunkAtRow(r);
                if (h < 0) continue;
                double y = i * _rh;
                if (r == layout.RowOfHunk(h))
                {
                    if (owner._session.Left.CanReceive) context.DrawText(Format("‹", accent), new Point(paneW + 8, y + 1));
                    if (owner._session.Right.CanReceive) context.DrawText(Format("›", accent), new Point(paneW + GutterWidth - 8 - _cw, y + 1));
                }
                else context.FillRectangle(accent, new Rect(paneW + GutterWidth / 2 - 1, y, 2, _rh));
            }

            if (owner._current >= 0 && owner._current < layout.HunkCount)
            {
                var hunk = diff.Hunks[owner._current];
                long first = layout.RowOfHunk(owner._current), count = Math.Max(1, Math.Max(hunk.ACount, hunk.BCount));
                double y0 = (first - top) * _rh, h = count * _rh;
                context.DrawRectangle(pen, new Rect(0.75, y0, Bounds.Width - 1.5, h));
            }
            context.DrawLine(new Pen(border, 1), new Point(paneW, 0), new Point(paneW, Bounds.Height));
            context.DrawLine(new Pen(border, 1), new Point(rightX, 0), new Point(rightX, Bounds.Height));
        }

        private static IBrush Tint(DiffRow row, long line, bool leftPane, IBrush removed, IBrush added, IBrush changed, IBrush filler) =>
            row.Kind == DiffRowKind.Equal ? Brushes.Transparent
            : line < 0 ? filler
            : row.Kind == DiffRowKind.Changed ? changed
            : leftPane ? removed : added;
    }
}
