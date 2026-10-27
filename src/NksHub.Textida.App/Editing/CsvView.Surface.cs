using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace NksHub.Textida.App.Editing;

internal sealed partial class CsvView
{
    /// <summary>What lies under the pointer: a cell, the row number column or the header strip. <see cref="Row"/> is -1 in the header strip.</summary>
    internal readonly record struct Hit(int Row, int Column, bool Gutter, bool Header);

    /// <summary>Draws the grid. Geometry is in character cells of the editor font; only the rows and columns on screen are read.</summary>
    private sealed class CsvSurface(CsvView owner) : Control
    {
        private const double Pad = 8, CellPad = 6;
        private const int FallbackRows = 20;
        private double _cw = 8, _rh = 20;
        private Typeface _face, _bold;

        public long RenderedAt { get; private set; }
        public double RowHeight => _rh;

        private double HeaderHeight => owner.HeaderRows > 0 ? _rh : 0;

        public int VisibleRows => Bounds.Height < 1 ? FallbackRows : Math.Max(1, (int)Math.Floor(Math.Max(0, Bounds.Height - HeaderHeight) / _rh));

        private double Gutter => Pad + (Math.Max(3, owner._table.RowCount.ToString(CultureInfo.InvariantCulture).Length + (owner._table.IsComplete ? 0 : 1)) + 1) * _cw;

        private FormattedText Format(string text, IBrush brush, bool bold, double width)
        {
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? _bold : _face, owner._options.FontSize, brush)
            {
                MaxTextWidth = Math.Max(1, width),
                MaxTextHeight = _rh,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            return ft;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var family = new FontFamily(owner._options.FontFamily);
            _face = new Typeface(family);
            _bold = new Typeface(family, FontStyle.Normal, FontWeight.SemiBold);
            var sample = new FormattedText(new string('0', 32), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _face, owner._options.FontSize, Brushes.Black);
            _cw = sample.Width / 32;
            _rh = Math.Ceiling(sample.Height) + 6;
            return availableSize;
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            owner.UpdateScrollRange();
        }

        private double WidthPx(int column) => owner.WidthOf(column) * _cw + CellPad;

        /// <summary>True when the columns from <paramref name="first"/> to <paramref name="column"/> fit in the width of the view.</summary>
        public bool ColumnFits(int first, int column)
        {
            if (Bounds.Width < 1) return true;
            double x = Gutter;
            for (int c = first; c <= column; c++) x += WidthPx(c);
            return x <= Bounds.Width;
        }

        /// <summary>The rectangle of a cell in this control; null when the cell is not on screen.</summary>
        public Rect? CellRect(int row, int column)
        {
            if (column < owner._firstCol || (row >= 0 && (row < owner._firstRow || row >= owner._firstRow + VisibleRows))) return null;
            double x = Gutter;
            for (int c = owner._firstCol; c < column; c++) x += WidthPx(c);
            double y = row < 0 ? 0 : HeaderHeight + (row - owner._firstRow) * _rh;
            return new Rect(x, y, WidthPx(column), _rh);
        }

        public Hit? HitTest(Point p)
        {
            double gutter = Gutter;
            bool header = HeaderHeight > 0 && p.Y < HeaderHeight;
            int row = header ? -1 : owner._firstRow + (int)Math.Floor(Math.Max(0, p.Y - HeaderHeight) / _rh);
            row = Math.Min(row, Math.Max(owner.MinRow, owner.DataRowCount - 1));
            if (p.X < gutter) return new Hit(row, 0, Gutter: !header, Header: header);
            double x = gutter;
            int col = owner._firstCol;
            while (col < owner.ColumnCount - 1 && x + WidthPx(col) <= p.X)
            {
                x += WidthPx(col);
                col++;
            }
            return new Hit(row, col, false, header);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            e.Pointer.Capture(this);
            owner.CellPressed(e.GetPosition(this), e.KeyModifiers.HasFlag(KeyModifiers.Shift), e.ClickCount);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) owner.CellMoved(e.GetPosition(this));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            e.Pointer.Capture(null);
            owner.CellReleased();
        }

        private IBrush Brush(string key, IBrush fallback) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;

        public override void Render(DrawingContext context)
        {
            try { Draw(context); }
            catch (IOException) { return; }
            RenderedAt = Stopwatch.GetTimestamp();
        }

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
            var border = Brush("Border", Brushes.LightGray);
            context.FillRectangle(back, new Rect(Bounds.Size));

            double gutter = Gutter, headerHeight = HeaderHeight;
            var columns = new List<(int Index, double X, double W)>();
            for (double x = gutter; ; )
            {
                int c = owner._firstCol + columns.Count;
                if (c >= owner.ColumnCount || (x >= Bounds.Width && columns.Count > 0)) break;
                columns.Add((c, x, WidthPx(c)));
                x += WidthPx(c);
            }
            if (columns.Count == 0) return;
            double right = columns[^1].X + columns[^1].W;
            var sel = owner.Selection;
            int rows = Math.Min(owner.DataRowCount - owner._firstRow, (int)Math.Ceiling(Math.Max(0, Bounds.Height - headerHeight) / _rh));
            var pen = new Pen(border, 1);

            if (headerHeight > 0)
            {
                context.FillRectangle(soft, new Rect(0, 0, Math.Max(right, Bounds.Width), headerHeight));
                foreach (var (index, x, w) in columns)
                {
                    if (sel.FirstRow <= -1 && sel.FirstColumn <= index && index <= sel.LastColumn) context.FillRectangle(selection, new Rect(x, 0, w, headerHeight));
                    string name = owner._table.Cell(0, index) + (owner._filter is { } f && f.Column == index ? " ▾" : "");
                    context.DrawText(Format(name, fore, bold: true, w - CellPad), new Point(x + CellPad / 2, 2));
                }
            }

            for (int i = 0; i < rows; i++)
            {
                int d = owner._firstRow + i;
                double y = headerHeight + i * _rh;
                if (d == owner._row) context.FillRectangle(currentRow, new Rect(0, y, Math.Max(right, Bounds.Width), _rh));
                var cells = owner.Preview(d, columns[0].Index, columns.Count);
                for (int k = 0; k < columns.Count; k++)
                {
                    var (index, x, w) = columns[k];
                    if (d >= sel.FirstRow && d <= sel.LastRow && index >= sel.FirstColumn && index <= sel.LastColumn && !sel.IsSingleCell)
                        context.FillRectangle(selection, new Rect(x, y, w, _rh));
                    string text = cells[k].Replace("\r\n", "↵").Replace('\n', '↵').Replace('\r', '↵');
                    if (text.Length > 0) context.DrawText(Format(text, fore, bold: false, w - CellPad), new Point(x + CellPad / 2, y + 2));
                }
                string number = owner.RowNumber(d).ToString(CultureInfo.InvariantCulture);
                var numberText = Format(number, d == owner._row ? accent : dim, bold: false, gutter - Pad);
                context.DrawText(numberText, new Point(gutter - _cw - numberText.Width, y + 2));
            }

            double bottom = headerHeight + Math.Max(0, rows) * _rh;
            context.DrawLine(pen, new Point(gutter, 0), new Point(gutter, bottom));
            foreach (var (_, x, w) in columns) context.DrawLine(pen, new Point(x + w, 0), new Point(x + w, bottom));
            for (int i = 0; i <= rows; i++) context.DrawLine(pen, new Point(0, headerHeight + i * _rh), new Point(right, headerHeight + i * _rh));
            if (headerHeight > 0) context.DrawLine(new Pen(accent, 1), new Point(0, headerHeight), new Point(right, headerHeight));

            if (CellRect(owner._row, owner._col) is { } cell) context.DrawRectangle(new Pen(accent, 1.5), cell.Deflate(0.75));
        }
    }
}
