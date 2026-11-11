using System.Globalization;
using Avalonia.Media;

namespace NksHub.Textida.App.Editing;

internal sealed partial class JsonPrettyView
{
    internal void UpdateScrollRange()
    {
        _syncing = true;
        try
        {
            long rows = RowCount;
            _top = Math.Clamp(_top, 0, Math.Max(0, rows - 1));
            _canvas.UpdateWidth();
            _vbar.Maximum = Math.Max(0, rows - 1);
            _vbar.ViewportSize = Math.Max(1, VisibleRows);
            _vbar.LargeChange = Math.Max(1, VisibleRows - 1);
            _vbar.Value = _top;
            _hbar.Maximum = Math.Max(0, _canvas.WidestLine - _canvas.TextWidth / 2);
            _hbar.ViewportSize = Math.Max(1, _canvas.TextWidth);
            _hbar.LargeChange = Math.Max(1, _canvas.TextWidth / 2);
            _hbar.Value = Math.Min(_hScroll, _hbar.Maximum);
        }
        finally { _syncing = false; }
    }

    private sealed partial class Surface
    {
        private readonly Dictionary<string, double> widths = new(StringComparer.Ordinal);
        private (string Family, double Size, string Culture)? widthFont;

        private void UpdateGutter()
        {
            int digits = Math.Max(3, owner.LineCount.ToString(CultureInfo.InvariantCulture).Length);
            _gutter = owner._options.LineNumbers ? (digits + 2) * _charWidth + Pad : 2 * _charWidth;
        }

        public void UpdateWidth()
        {
            if (!owner.IsEffectivelyVisible) return;
            var font = (owner._options.FullFontFamily, owner._options.FontSize, CultureInfo.CurrentCulture.Name);
            // The cache holds about two viewports; each edit can introduce another version of its line.
            if (widthFont != font || widths.Count > Math.Max(64, 2 * VisibleRows))
            {
                widths.Clear();
                widthFont = font;
            }
            try
            {
                UpdateGutter();
                double widest = 0;
                for (int i = 0; i < VisibleRows; i++)
                {
                    var (line, part) = owner.At(owner._top + i);
                    if (owner.RowRendered(line, part) is not { } rendered) break;
                    if (!widths.TryGetValue(rendered.Text, out double width))
                    {
                        width = Format(rendered.Text, Brushes.Black).WidthIncludingTrailingWhitespace;
                        widths[rendered.Text] = width;
                    }
                    widest = Math.Max(widest, width);
                }
                WidestLine = widest;
            }
            catch (IOException)
            {
                // The file watcher reports an invalidated snapshot; input keeps the last width like drawing does.
                widths.Clear();
            }
        }
    }
}
