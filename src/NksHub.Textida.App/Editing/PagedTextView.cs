using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// Read-only view of a document in the paged backend. It reads only the lines on screen, so the size of the file does not matter.
/// Editing on the paged surface is P3.1.5.
/// </summary>
/// <remarks>
/// shortcut: no selection, no copy and no horizontal scrolling (long lines are cut at <see cref="MaxLineBytes"/>); upgrade with the paged editing surface in P3.
/// </remarks>
internal sealed class PagedTextView : Grid
{
    private const int MaxLineBytes = 16 * 1024;
    private readonly OpenDocument _doc;
    private readonly LinesSurface _canvas;
    private readonly ScrollBar _bar = new() { Orientation = Orientation.Vertical, Minimum = 0, SmallChange = 1, AllowAutoHide = false };
    private readonly DispatcherTimer _indexTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private EditorOptions _options;

    public PagedTextView(OpenDocument doc, EditorOptions options)
    {
        _doc = doc;
        _options = options;
        Focusable = true;
        ColumnDefinitions = new ColumnDefinitions("*,Auto");
        _canvas = new LinesSurface(this);
        SetColumn(_bar, 1);
        Children.Add(_canvas);
        Children.Add(_bar);
        _bar.ValueChanged += (_, _) => { _canvas.InvalidateVisual(); TopLineChanged?.Invoke(); };
        _indexTimer.Tick += (_, _) =>
        {
            UpdateScrollRange();
            _canvas.InvalidateVisual();
            if (_doc.Doc.LineIndexState.Complete) _indexTimer.Stop();
            TopLineChanged?.Invoke();
        };
    }

    public event Action? TopLineChanged;

    /// <summary>Zero-based first line on screen.</summary>
    public long TopLine => (long)_bar.Value;
    public (long Lines, bool Complete) Index => _doc.Doc.LineIndexState;

    public void ApplyOptions(EditorOptions o)
    {
        _options = o;
        _canvas.InvalidateMeasure();
        _canvas.InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!_doc.Doc.LineIndexState.Complete) _indexTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _indexTimer.Stop();
    }

    public void GoToLine(long line)
    {
        _bar.Value = Math.Clamp(line - 1, 0, _bar.Maximum);
    }

    private void UpdateScrollRange()
    {
        long lines = Math.Max(1, Index.Lines);
        int visible = Math.Max(1, _canvas.VisibleLines);
        _bar.Maximum = Math.Max(0, lines - visible);
        _bar.ViewportSize = visible;
        _bar.LargeChange = Math.Max(1, visible - 1);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        _bar.Value = Math.Clamp(_bar.Value - e.Delta.Y * 3, 0, _bar.Maximum);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        base.OnPointerPressed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        double step = e.Key switch
        {
            Key.Down => 1, Key.Up => -1, Key.PageDown => _bar.LargeChange, Key.PageUp => -_bar.LargeChange,
            Key.Home when e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta) => -_bar.Maximum,
            Key.End when e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta) => _bar.Maximum,
            _ => 0,
        };
        if (step == 0) { base.OnKeyDown(e); return; }
        _bar.Value = Math.Clamp(_bar.Value + step, 0, _bar.Maximum);
        e.Handled = true;
    }

    private string? ReadLine(long line)
    {
        var doc = _doc.Doc;
        if (!doc.TryGetOffset(new LinePosition(line, 0), out long start)) return null;
        long end = doc.TryGetOffset(new LinePosition(line + 1, 0), out long next) ? next : doc.ByteLength;
        var bytes = new byte[(int)Math.Min(Math.Max(0, end - start), MaxLineBytes)];
        int n = doc.Read(start, bytes);
        var text = doc.Codec.GetString(bytes.AsSpan(0, doc.Codec.CompleteLength(bytes.AsSpan(0, n))));
        return text.TrimEnd('\r', '\n');
    }

    private sealed class LinesSurface(PagedTextView owner) : Control
    {
        private double _lineHeight = 18;
        private double _digitWidth = 8;

        public int VisibleLines => (int)Math.Ceiling(Math.Max(0, Bounds.Height) / _lineHeight);

        private FormattedText Format(string text, IBrush brush) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(owner._options.FontFamily)), owner._options.FontSize, brush);

        protected override Size MeasureOverride(Size availableSize)
        {
            var sample = Format("0", Brushes.Black);
            _lineHeight = Math.Ceiling(sample.Height);
            _digitWidth = sample.Width;
            return availableSize;
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            owner.UpdateScrollRange();
        }

        public override void Render(DrawingContext context)
        {
            IBrush Brush(string key, IBrush fallback) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;
            var back = Brush("BgEditor", Brushes.White);
            var fore = Brush("Fg", Brushes.Black);
            var numbers = Brush("LineNumbers", Brushes.Gray);
            context.FillRectangle(back, new Rect(Bounds.Size));
            var (lines, complete) = owner.Index;
            int digits = Math.Max(3, (lines > 0 ? lines : 1).ToString(CultureInfo.InvariantCulture).Length);
            double gutter = (digits + 1) * _digitWidth + 14;
            long top = owner.TopLine;
            using (context.PushClip(new Rect(gutter, 0, Math.Max(0, Bounds.Width - gutter), Bounds.Height)))
            {
                for (int i = 0; i < VisibleLines + 1; i++)
                {
                    if (owner.ReadLine(top + i) is not { } text) break;
                    context.DrawText(Format(text.Replace('\t', ' '), fore), new Point(gutter + 4, i * _lineHeight));
                }
            }
            for (int i = 0; i < VisibleLines + 1; i++)
            {
                long line = top + i;
                if (!complete && line >= lines) break;
                if (line >= lines) break;
                var number = Format((line + 1).ToString(CultureInfo.InvariantCulture), numbers);
                context.DrawText(number, new Point(gutter - 10 - number.Width, i * _lineHeight));
            }
        }
    }
}
