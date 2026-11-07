using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using Avalonia.VisualTree;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The Markdown reader (P9.3.4): one control that lays out the blocks of a <see cref="MarkdownReaderDocument"/> itself and draws them in the look of GitHub's page. Only the blocks in and around the
/// visible part are laid out; the height of any other is estimated from its text and corrected once it has been measured, and the scroll position is kept on the block at the top while that happens.
/// So a document of any length opens, and there is no limit on its number of blocks. Pictures and Mermaid diagrams are child controls that exist while their block is laid out.
/// The selection is a pair of positions in the text of the document (see <see cref="MarkdownReaderDocument"/>), so it runs across blocks and survives scrolling and a new rendering.
/// Sizes follow github-markdown-css 5.8.1 in multiples of <see cref="BaseFontSize"/>.
/// </summary>
internal sealed partial class MarkdownReader : Control
{
    public static readonly StyledProperty<double> BaseFontSizeProperty = AvaloniaProperty.Register<MarkdownReader, double>(nameof(BaseFontSize), 16);

    /// <summary>The widest the column of text gets; GitHub's article is 980 px with 45 px of padding.</summary>
    internal const double ColumnMax = 890;
    internal const double PagePaddingX = 32, PagePaddingY = 24;

    internal static readonly FontFamily Sans = new("Segoe UI, SF Pro Text, Helvetica Neue, Noto Sans, Liberation Sans, Arial");
    internal static readonly FontFamily Monospace = new("Consolas, SF Mono, Menlo, Liberation Mono, Courier New");

    /// <summary>The font size of a heading of each level relative to the text (GitHub: 2, 1.5, 1.25, 1, 0.875 and 0.85em).</summary>
    internal static readonly double[] HeadingScale = [0, 2, 1.5, 1.25, 1, 0.875, 0.85];

    private readonly PreviewRenderer.Ctx _ctx;
    private MarkdownReaderDocument _doc;
    private double[] _heights = [];
    private bool[] _measured = [];
    private double[] _tops = [];
    private bool _topsDirty = true;
    private double _total;
    private readonly Dictionary<int, Laid> _laid = [];
    private readonly Dictionary<int, double[]> _columns = [];
    private readonly Dictionary<int, double> _codeScroll = [];
    private double _width = -1, _left, _column;
    private Rect _viewport;
    private double _pendingShift;
    private PreviewScrollMap? _map;
    private ReaderPalette? _palette;
    private (int First, int Last) _realized = (0, -1);

    private sealed class Laid
    {
        public double Height;
        public readonly List<Box> Boxes = [];
        public Control? Embed;
        /// <summary>The pictures that stand in the text of the block; each is placed where its run is in the layout.</summary>
        public readonly List<(Control Control, Box Box, int Position, Size Size)> Pictures = [];
        /// <summary>A text layout of each marker of the block, beside its first line.</summary>
        public readonly List<(int Depth, TextLayout Layout)> Markers = [];
    }

    /// <summary>
    /// A laid-out run of text: a paragraph, a heading, the lines of a code slice or one cell, placed relative to the left of the page and the top of its block. Its layout may hold runs that are not
    /// text (the side padding of inline code, a picture in a sentence): <see cref="Inserts"/> keeps where they stand, so the offsets of the text, and with them the selection, stay those of the block.
    /// </summary>
    private sealed class Box
    {
        public required TextLayout Layout { get; init; }
        public required Point Origin { get; init; }
        public required int Start { get; init; }
        public required int Length { get; init; }
        /// <summary>The width a code slice is clipped to (it scrolls sideways instead of wrapping); 0 for text that wraps.</summary>
        public double Clip { get; init; }
        /// <summary>For each run that is not text, the offset in the text of the box it stands before; in the order of the layout, where each takes one position.</summary>
        public int[] Inserts { get; init; } = [];
        /// <summary>The runs of <see cref="Inserts"/>, in the same order.</summary>
        public DrawableTextRun[] Runs { get; init; } = [];

        /// <summary>The position in the layout of the run of a picture in a sentence.</summary>
        public int PicturePosition(Control control)
        {
            for (int i = 0; i < Runs.Length; i++)
                if (Runs[i] is PictureRun run && ReferenceEquals(run.Control, control)) return Inserts[i] + i;
            return -1;
        }

        /// <summary>The position in the layout of the character at <paramref name="offset"/> of the box (a run that stands before it comes first).</summary>
        public int ToLayout(int offset)
        {
            int n = 0;
            while (n < Inserts.Length && Inserts[n] <= offset) n++;
            return offset + n;
        }

        /// <summary>The end in the layout of a range of the box's text that ends at <paramref name="offset"/>: runs that stand right after it are not part of it.</summary>
        public int EndToLayout(int offset)
        {
            int n = 0;
            while (n < Inserts.Length && Inserts[n] < offset) n++;
            return offset + n;
        }

        /// <summary>The offset in the box's text of a position in the layout; a run that is not text gives the offset it stands before.</summary>
        public int FromLayout(int position)
        {
            for (int i = 0; i < Inserts.Length; i++)
            {
                int at = Inserts[i] + i;
                if (position == at) return Inserts[i];
                if (position < at) return position - i;
            }
            return position - Inserts.Length;
        }

        public Rect Caret(int offset) => Layout.HitTestTextPosition(ToLayout(offset));

        public IEnumerable<Rect> Range(int from, int to)
        {
            int start = ToLayout(from);
            return Layout.HitTestTextRange(start, Math.Max(0, EndToLayout(to) - start));
        }

        public int Hit(Point local)
        {
            var hit = Layout.HitTestPoint(local);
            return Math.Clamp(FromLayout(hit.CharacterHit.FirstCharacterIndex + hit.CharacterHit.TrailingLength), 0, Length);
        }
    }

    public MarkdownReader(MarkdownReaderDocument document, PreviewRenderer.Ctx ctx)
    {
        _ctx = ctx;
        _doc = document;
        Focusable = true;
        ClipToBounds = true;
        Reset(null);
        EffectiveViewportChanged += (_, e) =>
        {
            var before = _viewport;
            _viewport = e.EffectiveViewport;
            if (NeedsRealize(before)) InvalidateMeasure();
            InvalidateVisual();
        };
        LayoutUpdated += (_, _) => ApplyShift();
        ActualThemeVariantChanged += (_, _) => Relayout();
        ContextMenu = BuildMenu();
    }

    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new MarkdownReaderAutomationPeer(this);

    /// <summary>What a screen reader reads as the value of the page: the selection, or else the text of the blocks in view.</summary>
    internal string VisibleText()
    {
        if (HasSelection) return SelectedText();
        if (_doc.Blocks.Count == 0) return "";
        var view = View;
        int first = BlockAtY(view.Top), last = BlockAtY(view.Bottom);
        return string.Join('\n', Enumerable.Range(first, last - first + 1).Select(i => _doc.Blocks[i].Text).Where(t => t.Length > 0));
    }

    public double BaseFontSize
    {
        get => GetValue(BaseFontSizeProperty);
        set => SetValue(BaseFontSizeProperty, value);
    }

    internal MarkdownReaderDocument Document => _doc;

    internal PreviewRenderer.Ctx Context => _ctx;

    /// <summary>The blocks that are laid out now: those in and around the visible part.</summary>
    internal (int First, int Last) Realized => _realized;

    internal int LaidCount => _laid.Count;

    /// <summary>The page height, with the estimated heights of the blocks not yet measured.</summary>
    internal double PageHeight => EnsureTops();

    /// <summary>Shows another rendering of the document. Blocks that did not change keep their measured height and the selection follows the text that did not change.</summary>
    public void SetDocument(MarkdownReaderDocument document)
    {
        var old = _doc;
        _doc = document;
        MapSelection(old, document);
        Reset(old);
        RetranslateMenu();
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BaseFontSizeProperty) Relayout();
        else if (change.Property == IsFocusedProperty) InvalidateVisual();
    }

    private void Relayout()
    {
        _palette = null;
        Reset(_doc);
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void Reset(MarkdownReaderDocument? previous)
    {
        foreach (var laid in _laid.Values) Detach(laid);
        _laid.Clear();
        _columns.Clear();
        _codeScroll.Clear();
        _focusedLink = Math.Min(_focusedLink, _doc.Links.Count - 1);
        int n = _doc.Blocks.Count;
        var heights = new double[n];
        var measured = new bool[n];
        if (previous is not null && ReferenceEquals(previous, _doc))
        {
            // the same document laid out again (another font size or theme): estimates only
        }
        else if (previous is not null && _heights.Length == previous.Blocks.Count)
        {
            int prefix = 0, suffix = 0, m = previous.Blocks.Count;
            while (prefix < n && prefix < m && Same(previous.Blocks[prefix], _doc.Blocks[prefix])) prefix++;
            while (suffix < n - prefix && suffix < m - prefix && Same(previous.Blocks[m - 1 - suffix], _doc.Blocks[n - 1 - suffix])) suffix++;
            for (int i = 0; i < prefix; i++) (heights[i], measured[i]) = (_heights[i], _measured[i]);
            for (int i = 0; i < suffix; i++) (heights[n - 1 - i], measured[n - 1 - i]) = (_heights[m - 1 - i], _measured[m - 1 - i]);
        }
        _heights = heights;
        _measured = measured;
        if (_width > 0)
            for (int i = 0; i < n; i++)
                if (!_measured[i]) _heights[i] = Estimate(i);
        _tops = new double[n];
        _topsDirty = true;
        _realized = (0, -1);
    }

    private static bool Same(ReaderBlock a, ReaderBlock b) =>
        a.Kind == b.Kind && a.Text == b.Text && a.Level == b.Level && a.Path.Length == b.Path.Length && a.Small == b.Small && a.Markers.Length == b.Markers.Length
        && a.GroupFirst == b.GroupFirst && a.GroupLast == b.GroupLast && a.Pictures.Length == 0 && b.Pictures.Length == 0 && a.Kind is not (ReaderBlockKind.TableRow or ReaderBlockKind.Images or ReaderBlockKind.Diagram);

    // ---- geometry ----

    private double Em => BaseFontSize;

    private static double IndentEm(ReaderContainer c) => c.Kind switch
    {
        ReaderContainerKind.Quote or ReaderContainerKind.Alert => 1.25, // a bar of 0.25em and 1em of padding
        ReaderContainerKind.ListItem => 2,
        _ => 1,
    };

    /// <summary>The left edge of the content of the first <paramref name="depth"/> containers of a block.</summary>
    private double LeftAt(ReaderBlock block, int depth)
    {
        double x = _left;
        for (int i = 0; i < depth && i < block.Path.Length; i++) x += IndentEm(block.Path[i]) * Em;
        return x;
    }

    private double Left(ReaderBlock block) => LeftAt(block, block.Path.Length);

    private double BlockWidth(ReaderBlock block)
    {
        double right = 0;
        foreach (var c in block.Path) if (c.Kind is ReaderContainerKind.Quote or ReaderContainerKind.Alert) right += Em;
        return Math.Max(Em * 4, _left + _column - right - Left(block));
    }

    private double FontSize(ReaderBlock block) => block.Kind switch
    {
        ReaderBlockKind.Heading => HeadingScale[Math.Clamp(block.Level, 1, 6)] * Em,
        ReaderBlockKind.Code => 0.85 * Em,
        _ => block.Small ? 0.75 * Em : Em,
    };

    private double LineHeight(ReaderBlock block) => block.Kind switch
    {
        ReaderBlockKind.Heading => 1.25 * FontSize(block),
        ReaderBlockKind.Code => 1.45 * FontSize(block),
        ReaderBlockKind.AlertTitle => Em,
        _ => 1.5 * FontSize(block),
    };

    private double Gap(int index) => _doc.Blocks[index].GapEm * Em;

    private double EnsureTops()
    {
        if (!_topsDirty) return _total;
        double y = PagePaddingY;
        for (int i = 0; i < _heights.Length; i++)
        {
            y += Gap(i);
            _tops[i] = y;
            y += _heights[i];
        }
        _total = y + PagePaddingY;
        _topsDirty = false;
        _map = null;
        return _total;
    }

    /// <summary>The block at a height of the page: the one it falls in, or after a gap the one below.</summary>
    internal int BlockAtY(double y)
    {
        EnsureTops();
        int lo = 0, hi = _tops.Length - 1;
        if (hi < 0) return 0;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_tops[mid] - Gap(mid) <= y) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>Where a block is on the page; the height is estimated while it has not been laid out.</summary>
    internal Rect BlockBounds(int index)
    {
        EnsureTops();
        var block = _doc.Blocks[index];
        return new Rect(Left(block), _tops[index], BlockWidth(block), _heights[index]);
    }

    internal bool IsMeasured(int index) => _measured[index];

    /// <summary>The text layout of a laid-out block (a table row has one per cell), or null while the block is not laid out.</summary>
    internal TextLayout? LayoutOf(int index, int box = 0) => _laid.TryGetValue(index, out var laid) && box < laid.Boxes.Count ? laid.Boxes[box].Layout : null;

    /// <summary>The backgrounds of the inline code of a laid-out block, on the page.</summary>
    internal IReadOnlyList<Rect> InlineCodeBounds(int index)
    {
        if (!_laid.TryGetValue(index, out var laid)) return [];
        var b = _doc.Blocks[index];
        return [.. laid.Boxes.SelectMany(box => InlineCodeRects(b, box).Select(r => r.Translate(new Vector(box.Origin.X, _tops[index] + box.Origin.Y))))];
    }

    /// <summary>The selection rectangles of a range of a laid-out block (offsets of its text), on the page.</summary>
    internal IReadOnlyList<Rect> RangeBounds(int index, int from, int to)
    {
        if (!_laid.TryGetValue(index, out var laid)) return [];
        var rects = new List<Rect>();
        foreach (var box in laid.Boxes)
        {
            int s = Math.Max(from, box.Start), e = Math.Min(to, box.Start + box.Length);
            if (e > s) rects.AddRange(box.Range(s - box.Start, e - box.Start).Select(r => r.Translate(new Vector(box.Origin.X, _tops[index] + box.Origin.Y))));
        }
        return rects;
    }

    /// <summary>The pictures that stand in the text of a laid-out block, with where they are on the page.</summary>
    internal IReadOnlyList<(Control Control, Rect Bounds)> InlinePictures(int index) =>
        _laid.TryGetValue(index, out var laid) ? [.. laid.Pictures.Select(p => (p.Control, p.Control.Bounds))] : [];

    /// <summary>The widths of the columns of a table (by its index in the document) as the page lays them out.</summary>
    internal IReadOnlyList<double> ColumnWidths(int table) => _columns.TryGetValue(table, out var widths) ? widths : [];

    /// <summary>Where the text of a box of a laid-out block starts, relative to the page.</summary>
    internal Point? BoxOrigin(int index, int box = 0) => _laid.TryGetValue(index, out var laid) && box < laid.Boxes.Count ? laid.Boxes[box].Origin + new Point(0, _tops[index]) : null;

    /// <summary>The middle of the character at a position, on the page; null while its block is not laid out.</summary>
    internal Point? PointAt(int position)
    {
        int index = _doc.BlockAt(position);
        var b = _doc.Blocks[index];
        if (!_laid.TryGetValue(index, out var laid)) return null;
        int offset = Math.Clamp(position - b.DocStart, 0, b.Text.Length);
        foreach (var box in laid.Boxes)
            if (offset >= box.Start && offset <= box.Start + box.Length)
            {
                var r = box.Caret(offset - box.Start);
                double scroll = box.Clip > 0 ? _codeScroll.GetValueOrDefault(b.Group) : 0;
                return new Point(box.Origin.X - scroll + r.X + 1, _tops[index] + box.Origin.Y + r.Y + r.Height / 2);
            }
        return new Point(Left(b) + 1, _tops[index] + laid.Height / 2);
    }

    // ---- scroll map ----

    /// <summary>The source lines against heights of the page, from the top of each block in source order; blocks drawn out of source order (footnotes) do not take part.</summary>
    internal PreviewScrollMap Map
    {
        get
        {
            EnsureTops();
            if (_map is not null) return _map;
            var points = new List<(int Line, double Top)>();
            int last = -1;
            for (int i = 0; i < _doc.Blocks.Count; i++)
            {
                var b = _doc.Blocks[i];
                if (b.Line > last)
                {
                    points.Add((b.Line, _tops[i]));
                    last = b.Line;
                }
                if (b.EndLine + 1 > last && i + 1 < _doc.Blocks.Count && _doc.Blocks[i + 1].Line > b.EndLine + 1)
                {
                    points.Add((b.EndLine + 1, _tops[i] + _heights[i]));
                    last = b.EndLine + 1;
                }
            }
            return _map = new PreviewScrollMap(points, _doc.Lines.Length, _total);
        }
    }

    // ---- layout ----

    private bool NeedsRealize(Rect before)
    {
        if (_realized.Last < _realized.First || before.Width != _viewport.Width) return true;
        var range = RealizeRange(_viewport);
        return range.From < (_realized.First > 0 ? _tops[_realized.First] : double.NegativeInfinity)
            || range.To > (_realized.Last < _tops.Length - 1 ? _tops[_realized.Last] + _heights[_realized.Last] : double.PositiveInfinity);
    }

    private (double From, double To) RealizeRange(Rect view)
    {
        double margin = Math.Max(view.Height, 400);
        return (view.Top - margin, view.Bottom + margin);
    }

    private Rect View => _viewport.Width > 0 || _viewport.Height > 0 ? _viewport : new Rect(0, 0, Math.Max(_width, 1), 1000);

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) && availableSize.Width > 0 ? availableSize.Width : 900;
        if (Math.Abs(width - _width) > 0.5)
        {
            _width = width;
            _column = Math.Min(ColumnMax, Math.Max(200, width - 2 * PagePaddingX));
            _left = Math.Max(0, (width - _column) / 2);
            foreach (var laid in _laid.Values) Detach(laid);
            _laid.Clear();
            _columns.Clear();
            for (int i = 0; i < _heights.Length; i++)
            {
                _measured[i] = false;
                _heights[i] = Estimate(i);
            }
            _topsDirty = true;
        }
        DropResizedPictures(width);
        EnsureTops();
        var view = View;
        var (from, to) = RealizeRange(view);
        int anchor = BlockAtY(view.Top);
        double anchorTop = _tops.Length > 0 ? _tops[anchor] : 0;
        int first = BlockAtY(from), last = first - 1;
        double y = _tops.Length > 0 ? _tops[first] : 0;
        for (int i = first; i < _heights.Length; i++)
        {
            if (i > first) y += Gap(i);
            if (y > to) break;
            Realize(i);
            y += _heights[i];
            last = i;
        }
        _realized = (first, last);
        // drop what is far away: the layouts of blocks beyond a few screens, and every child control outside the laid-out range
        foreach (int key in _laid.Keys.Where(k => k < first - 200 || k > last + 200 || ((_laid[k].Embed is not null || _laid[k].Pictures.Count > 0) && (k < first || k > last))).ToList())
        {
            Detach(_laid[key]);
            _laid.Remove(key);
        }
        _topsDirty = true;
        EnsureTops();
        if (_tops.Length > 0 && _viewport.Height > 0 && Math.Abs(_tops[anchor] - anchorTop) > 0.25) _pendingShift += _tops[anchor] - anchorTop;
        return new Size(width, _total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (index, laid) in _laid)
        {
            if (laid.Embed is { } embed)
            {
                var b = _doc.Blocks[index];
                embed.Arrange(new Rect(Left(b), _tops[index], BlockWidth(b), laid.Height));
            }
            foreach (var (control, box, position, size) in laid.Pictures)
            {
                // the bottom of the picture on the baseline of its line, as a browser puts an image in a sentence
                var run = box.Layout.HitTestTextRange(position, 1).FirstOrDefault();
                var line = box.Layout.TextLines[Math.Clamp(box.Layout.GetLineIndexFromCharacterIndex(position, false), 0, box.Layout.TextLines.Count - 1)];
                control.Arrange(new Rect(new Point(box.Origin.X + run.X, _tops[index] + box.Origin.Y + run.Y + line.Baseline - size.Height), size));
            }
        }
        return finalSize;
    }

    /// <summary>A picture in a sentence that got another size (it finished loading, or the site was allowed) is laid out again with its block.</summary>
    private void DropResizedPictures(double width)
    {
        List<int>? stale = null;
        foreach (var (index, laid) in _laid)
            foreach (var (control, _, _, size) in laid.Pictures)
            {
                control.Measure(new Size(BlockWidth(_doc.Blocks[index]), double.PositiveInfinity));
                if (Math.Abs(control.DesiredSize.Width - size.Width) > 0.5 || Math.Abs(control.DesiredSize.Height - size.Height) > 0.5) (stale ??= []).Add(index);
            }
        if (stale is null || width <= 0) return;
        foreach (int index in stale.Distinct())
        {
            Detach(_laid[index]);
            _laid.Remove(index);
            _measured[index] = false;
        }
    }

    /// <summary>Keeps the block at the top of the view where it was after blocks above it got their measured height.</summary>
    private void ApplyShift()
    {
        if (Math.Abs(_pendingShift) < 0.25) return;
        double shift = _pendingShift;
        _pendingShift = 0;
        if (this.FindAncestorOfType<ScrollViewer>() is { } scroller)
            scroller.Offset = new Vector(scroller.Offset.X, Math.Max(0, scroller.Offset.Y + shift));
    }

    private void Detach(Laid laid)
    {
        foreach (var (control, _, _, _) in laid.Pictures)
        {
            VisualChildren.Remove(control);
            LogicalChildren.Remove(control);
        }
        laid.Pictures.Clear();
        if (laid.Embed is not { } embed) return;
        VisualChildren.Remove(embed);
        LogicalChildren.Remove(embed);
        laid.Embed = null;
    }

    private double Estimate(int index)
    {
        var b = _doc.Blocks[index];
        double width = BlockWidth(b);
        switch (b.Kind)
        {
            case ReaderBlockKind.Rule:
                return b.Level == 1 ? 1 : 0.25 * Em;
            case ReaderBlockKind.Images:
                return 2 * Em;
            case ReaderBlockKind.Diagram:
                return 12 * Em;
            case ReaderBlockKind.Code:
            {
                int lines = 1;
                foreach (char c in b.Text) if (c == '\n') lines++;
                return lines * LineHeight(b) + (b.GroupFirst ? Em : 0) + (b.GroupLast ? Em : 0);
            }
            case ReaderBlockKind.TableRow:
            {
                var widths = Columns(b, measure: false);
                double tallest = 1;
                for (int c = 0; c < b.Cells.Length; c++) tallest = Math.Max(tallest, LinesOf(b.Text.AsSpan(b.Cells[c], b.CellEnd(c) - b.Cells[c]), widths[c] - 26 / 16.0 * Em, Em));
                return tallest * 1.5 * Em + 0.75 * Em;
            }
            default:
            {
                double size = FontSize(b);
                double height = LinesOf(b.Text, width, size) * LineHeight(b);
                if (b.Kind == ReaderBlockKind.Heading && b.Level <= 2) height += 0.3 * size + 1;
                return height;
            }
        }
    }

    private static double LinesOf(ReadOnlySpan<char> text, double width, double size)
    {
        double perLine = Math.Max(1, width / (0.5 * size));
        double lines = 0;
        foreach (var range in text.Split('\n')) lines += Math.Max(1, Math.Ceiling((range.End.GetOffset(text.Length) - range.Start.GetOffset(text.Length)) / perLine));
        return Math.Max(1, lines);
    }

    /// <summary>Rows of a table whose cells are measured for the widths of its columns; a longer table measures, in each column, the cells with the most characters.</summary>
    internal const int TableMeasuredRows = 256;

    /// <summary>
    /// The column widths of a table: each column as wide as its widest cell laid out on one line with the fonts and styles of the page, and the table no wider than the column of text.
    /// <paramref name="measure"/> false (the estimate of a row that is not laid out) takes the widths from the characters until the table has been measured.
    /// </summary>
    private double[] Columns(ReaderBlock row, bool measure = true)
    {
        if (_columns.TryGetValue(row.Level, out var widths)) return widths;
        var table = _doc.Tables[row.Level];
        double available = BlockWidth(row), padding = 26 / 16.0 * Em;
        if (!measure) return [.. table.Chars.Select(chars => Math.Max(3 * Em, Math.Min(chars, 80) * 0.55 * Em + padding + 2))];
        widths = [.. MeasureColumns(row.Level).Select(natural => Math.Max(3 * Em, Math.Ceiling(natural) + padding + 2))];
        double sum = widths.Sum();
        if (sum > available)
        {
            // shrink the wide columns first, never below a minimum that keeps a word readable
            double minimum = Math.Min(6 * Em, available / Math.Max(1, widths.Length));
            double room = sum - available, shrinkable = widths.Sum(w => Math.Max(0, w - minimum));
            if (shrinkable > 0)
                for (int c = 0; c < widths.Length; c++) widths[c] -= Math.Max(0, widths[c] - minimum) * Math.Min(1, room / shrinkable);
        }
        return _columns[row.Level] = widths;
    }

    /// <summary>The width of the widest cell of each column of a table, its text laid out on one line.</summary>
    private double[] MeasureColumns(int tableIndex)
    {
        var table = _doc.Tables[tableIndex];
        var rows = new List<ReaderBlock>(table.Rows);
        foreach (var b in _doc.Blocks)
            if (b.Kind == ReaderBlockKind.TableRow && b.Level == tableIndex) rows.Add(b);
        var natural = new double[table.Columns];
        for (int c = 0; c < table.Columns; c++)
        {
            int column = c;
            var cells = rows.Count <= TableMeasuredRows ? rows : rows.Where(r => r.Header).Concat(rows.OrderByDescending(r => column < r.Cells.Length ? r.CellEnd(column) - r.Cells[column] : 0).Take(TableMeasuredRows));
            foreach (var r in cells)
            {
                if (c >= r.Cells.Length) continue;
                int start = r.Cells[c], length = r.CellEnd(c) - start;
                if (length == 0) continue;
                var box = TextBox(r, start, length, double.PositiveInfinity, TextWrapping.NoWrap, Palette.Fg, Sans, r.Header ? FontWeight.SemiBold : FontWeight.Normal, default);
                natural[c] = Math.Max(natural[c], box.Layout.WidthIncludingTrailingWhitespace);
            }
        }
        return natural;
    }

    private void Realize(int index)
    {
        if (_laid.TryGetValue(index, out var existing))
        {
            if (existing.Embed is null && _doc.Blocks[index].Kind is ReaderBlockKind.Images or ReaderBlockKind.Diagram) _laid.Remove(index);
            else return;
        }
        var laid = Lay(index);
        _laid[index] = laid;
        if (!_measured[index] || Math.Abs(_heights[index] - laid.Height) > 0.01)
        {
            _heights[index] = laid.Height;
            _topsDirty = true;
        }
        _measured[index] = true;
    }

    private Laid Lay(int index)
    {
        var b = _doc.Blocks[index];
        var p = Palette;
        var laid = new Laid();
        double left = Left(b), width = BlockWidth(b);
        switch (b.Kind)
        {
            case ReaderBlockKind.Rule:
                laid.Height = b.Level == 1 ? 1 : 0.25 * Em;
                break;
            case ReaderBlockKind.Images or ReaderBlockKind.Diagram:
            {
                var embed = Embed(b);
                laid.Embed = embed;
                LogicalChildren.Add(embed);
                VisualChildren.Add(embed);
                embed.Measure(new Size(width, double.PositiveInfinity));
                laid.Height = Math.Max(1, embed.DesiredSize.Height);
                break;
            }
            case ReaderBlockKind.Code:
            {
                double pad = Em;
                var box = TextBox(b, 0, b.Text.Length, double.PositiveInfinity, TextWrapping.NoWrap, p.Fg, Monospace, FontWeight.Normal, new Point(left + pad, b.GroupFirst ? pad : 0), clip: Math.Max(1, width - 2 * pad));
                laid.Boxes.Add(box);
                laid.Height = Math.Max(box.Layout.Height, LineHeight(b)) + (b.GroupFirst ? pad : 0) + (b.GroupLast ? pad : 0);
                break;
            }
            case ReaderBlockKind.TableRow:
            {
                var widths = Columns(b);
                var table = _doc.Tables[b.Level];
                double x = left, tallest = 0, padX = 13 / 16.0 * Em, padY = 6 / 16.0 * Em;
                var cells = new List<Box>();
                for (int c = 0; c < b.Cells.Length; c++)
                {
                    int start = b.Cells[c], end = b.CellEnd(c);
                    var align = table.Align[c] switch { Markdig.Extensions.Tables.TableColumnAlign.Center => TextAlignment.Center, Markdig.Extensions.Tables.TableColumnAlign.Right => TextAlignment.Right, _ => TextAlignment.Left };
                    var box = TextBox(b, start, end - start, Math.Max(1, widths[c] - 2 * padX - 1), TextWrapping.Wrap, p.Fg, Sans, b.Header ? FontWeight.SemiBold : FontWeight.Normal, new Point(x + padX + 1, padY + 1), align);
                    cells.Add(box);
                    tallest = Math.Max(tallest, box.Layout.Height);
                    x += widths[c];
                }
                laid.Boxes.AddRange(cells);
                laid.Height = tallest + 2 * padY + 1;
                break;
            }
            case ReaderBlockKind.AlertTitle:
            {
                var colour = p.Alert(b.Path.LastOrDefault(c => c.Kind == ReaderContainerKind.Alert)?.AlertKind);
                var box = TextBox(b, 0, b.Text.Length, Math.Max(1, width - 1.5 * Em), TextWrapping.Wrap, colour, Sans, FontWeight.Medium, new Point(left + 1.5 * Em, 0));
                laid.Boxes.Add(box);
                laid.Height = Math.Max(box.Layout.Height, Em);
                break;
            }
            default:
            {
                bool heading = b.Kind == ReaderBlockKind.Heading;
                bool muted = b.Small || heading && b.Level == 6 || b.Path.Any(c => c.Kind == ReaderContainerKind.Quote);
                var pictures = b.Pictures.Select(picture => (picture.Offset, Control: PictureControl(picture.Image))).ToList();
                foreach (var (_, control) in pictures)
                {
                    LogicalChildren.Add(control);
                    VisualChildren.Add(control);
                    control.Measure(new Size(width, double.PositiveInfinity));
                }
                var brush = muted ? p.Muted : p.Fg;
                var face = heading ? FontWeight.SemiBold : FontWeight.Normal;
                var box = TextBox(b, 0, b.Text.Length, width, TextWrapping.Wrap, brush, Sans, face, new Point(left, 0), pictures: pictures);
                var boxes = box.Layout.TextLines.Any(l => l.Extent > l.Height + 0.5) ? SplitTallLines(b, box, pictures, width, brush, face) : [box];
                laid.Boxes.AddRange(boxes);
                foreach (var part in boxes)
                    foreach (var (_, control) in pictures)
                        if (part.PicturePosition(control) is >= 0 and var position) laid.Pictures.Add((control, part, position, control.DesiredSize));
                laid.Height = Math.Max(boxes[^1].Origin.Y + boxes[^1].Layout.Height, LineHeight(b));
                if (heading && b.Level <= 2) laid.Height += 0.3 * FontSize(b) + 1;
                break;
            }
        }
        foreach (int depth in b.Markers)
            if (b.Path[depth].Marker is { Length: > 0 } marker)
            {
                double size = b.Kind is ReaderBlockKind.Paragraph or ReaderBlockKind.Heading ? FontSize(b) : b.Small ? 0.75 * Em : Em;
                laid.Markers.Add((depth, new TextLayout(marker, new Typeface(Sans), size, b.Small ? p.Muted : p.Fg, TextAlignment.Right, TextWrapping.NoWrap,
                    TextTrimming.None, null, FlowDirection.LeftToRight, IndentEm(b.Path[depth]) * Em - 0.4 * Em, double.PositiveInfinity, 1.5 * size, 0, 0, null, null)));
            }
        return laid;
    }

    /// <summary>
    /// A laid-out box of part of a block with the styles of its runs: bold, italic, struck through, inline code in the code font at 85 % with GitHub's 0.4em of padding at its sides, links in the
    /// link colour. <paramref name="pictures"/> are the controls of pictures that stand in the text, each before the character at its offset of the block.
    /// </summary>
    private Box TextBox(ReaderBlock b, int start, int length, double width, TextWrapping wrapping, IBrush foreground, FontFamily family, FontWeight weight, Point origin,
        TextAlignment alignment = TextAlignment.Left, double clip = 0, IReadOnlyList<(int Offset, Control Control)>? pictures = null, double? lineHeight = null)
    {
        var p = Palette;
        double size = FontSize(b);
        bool code = b.Kind == ReaderBlockKind.Code;
        string text = b.Text.Substring(start, length);
        var overrides = new List<ValueSpan<TextRunProperties>>();
        // runs that are not text, by the offset they stand before; at one offset the end of a code span comes first, then pictures, then the start of the next code span
        var inserts = new List<(int Offset, int Order, DrawableTextRun Run)>();
        double codePad = InlineCodePadEm * 0.85 * size;
        var defaults = new GenericTextRunProperties(new Typeface(family, FontStyle.Normal, weight), size, null, foreground);
        foreach (var span in b.Spans)
        {
            int from = Math.Max(span.Start, start), to = Math.Min(span.Start + span.Length, start + length);
            if (to <= from || span.Style == ReaderStyle.None || code) continue;
            var style = span.Style;
            var typeface = new Typeface((style & ReaderStyle.Code) != 0 ? Monospace : family,
                (style & ReaderStyle.Italic) != 0 ? FontStyle.Italic : FontStyle.Normal,
                (style & ReaderStyle.Bold) != 0 ? FontWeight.SemiBold : weight);
            double runSize = (style & ReaderStyle.Code) != 0 ? 0.85 * size : size;
            var brush = (style & ReaderStyle.Link) != 0 ? p.Link : foreground;
            overrides.Add(new ValueSpan<TextRunProperties>(from - start, to - from,
                new GenericTextRunProperties(typeface, runSize, (style & ReaderStyle.Strike) != 0 ? TextDecorations.Strikethrough : null, brush)));
            if ((style & ReaderStyle.Code) != 0)
            {
                if (from == span.Start) inserts.Add((from - start, 2, new Spacer(codePad, defaults)));
                if (to == span.Start + span.Length) inserts.Add((to - start, 0, new Spacer(codePad, defaults)));
            }
        }
        if (pictures is not null)
            foreach (var (offset, control) in pictures)
                if (offset >= start && offset <= start + length) inserts.Add((offset - start, 1, new PictureRun(control, control.DesiredSize, defaults)));
        TextLayout layout;
        int[] at = [];
        DrawableTextRun[] runs = [];
        if (inserts.Count == 0)
            layout = new TextLayout(text, defaults.Typeface, size, foreground, alignment, wrapping, TextTrimming.None, null, FlowDirection.LeftToRight,
                width, double.PositiveInfinity, lineHeight ?? LineHeight(b), 0, 0, null, overrides.Count > 0 ? overrides : null);
        else
        {
            var ordered = inserts.OrderBy(i => i.Offset).ThenBy(i => i.Order).ToList();
            at = [.. ordered.Select(i => i.Offset)];
            runs = [.. ordered.Select(i => i.Run)];
            var paragraph = new GenericTextParagraphProperties(FlowDirection.LeftToRight, alignment, true, false, defaults, wrapping, lineHeight ?? LineHeight(b), 0, 0);
            layout = new TextLayout(new InsertedSource(text, defaults, overrides, at, runs), paragraph, TextTrimming.None, width, double.PositiveInfinity, 0);
        }
        return new Box { Layout = layout, Origin = origin, Start = start, Length = length, Clip = clip, Inserts = at, Runs = runs };
    }

    /// <summary>
    /// A paragraph with a picture taller than its line, cut into one box per line stacked one under the other: ordinary lines keep the line height of the page, and a line with a tall picture gets
    /// the height it needs (as in a browser, only that line grows). Each box holds exactly one line of the whole paragraph and does not wrap again, so the text breaks where it did; wrapping a cut
    /// again would push the space that ends it onto an empty line of its own when it overhangs the width.
    /// </summary>
    private List<Box> SplitTallLines(ReaderBlock b, Box whole, IReadOnlyList<(int Offset, Control Control)> pictures, double width, IBrush brush, FontWeight weight)
    {
        var parts = new List<Box>();
        var lines = whole.Layout.TextLines;
        double y = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            bool tall = lines[i].Extent > lines[i].Height + 0.5;
            int from = lines[i].FirstTextSourceIndex, to = from + lines[i].Length;
            int start = whole.FromLayout(from), end = i == lines.Count - 1 ? whole.Length : whole.FromLayout(to);
            var own = pictures.Where(x => whole.PicturePosition(x.Control) is var at && at >= from && at < to).ToList();
            var part = TextBox(b, start, end - start, width, TextWrapping.NoWrap, brush, Sans, weight, new Point(whole.Origin.X, y), pictures: own, lineHeight: tall ? double.NaN : null);
            parts.Add(part);
            y += part.Layout.Height;
        }
        return parts;
    }

    /// <summary>The side padding of inline code in multiples of its font size (github-markdown-css: <c>padding: .2em .4em</c>).</summary>
    internal const double InlineCodePadEm = 0.4;

    /// <summary>Empty room in a line: the padding at a side of inline code.</summary>
    private sealed class Spacer(double width, TextRunProperties properties) : DrawableTextRun
    {
        public override TextRunProperties Properties => properties;
        public override Size Size => new(width, 0);
        public override double Baseline => 0;
        public override void Draw(DrawingContext drawingContext, Point origin) { }
    }

    /// <summary>The room of a picture in a sentence; the picture is a child control placed over it, so the run draws nothing.</summary>
    private sealed class PictureRun(Control control, Size size, TextRunProperties properties) : DrawableTextRun
    {
        public Control Control => control;
        public override TextRunProperties Properties => properties;
        public override Size Size => size;
        public override double Baseline => size.Height;
        public override void Draw(DrawingContext drawingContext, Point origin) { }
    }

    /// <summary>The text of a box with its runs that are not text placed between the characters; positions count those runs as one each (<see cref="Box.Inserts"/>).</summary>
    private sealed class InsertedSource(string text, TextRunProperties defaults, IReadOnlyList<ValueSpan<TextRunProperties>> overrides, int[] inserts, DrawableTextRun[] runs) : ITextSource
    {
        public TextRun GetTextRun(int textSourceIndex)
        {
            int i = 0;
            for (; i < inserts.Length; i++)
            {
                int at = inserts[i] + i;
                if (textSourceIndex == at) return runs[i];
                if (textSourceIndex < at) break;
            }
            int offset = textSourceIndex - i;
            if (offset >= text.Length) return new TextEndOfParagraph();
            int end = i < inserts.Length ? inserts[i] : text.Length;
            var properties = defaults;
            foreach (var o in overrides)
            {
                if (offset >= o.Start && offset < o.Start + o.Length)
                {
                    properties = o.Value;
                    end = Math.Min(end, o.Start + o.Length);
                }
                else if (o.Start > offset) end = Math.Min(end, o.Start);
            }
            return new TextCharacters(text.AsMemory(offset, end - offset), properties);
        }
    }

    /// <summary>The child control of a row of pictures or of a diagram; a picture inside a link opens it like a link of the text.</summary>
    private Control Embed(ReaderBlock b)
    {
        if (b.Chart is { } chart) return PreviewRenderer.DrawFlowchart(chart);
        var row = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 4, LineSpacing = 4 };
        foreach (var image in b.Images) row.Children.Add(PictureControl(image));
        return row;
    }

    /// <summary>The control of one picture, or the note that stands for it; inside a link it is a button that follows the link.</summary>
    private Control PictureControl(ReaderImage image)
    {
        Control picture = PreviewRenderer.ImageControl(image.Target, image.Alt, _ctx) ?? PreviewImages.Note(Text.K.PreviewPanelImage, image.Alt);
        if (image.Link < 0 || image.Link >= _doc.Links.Count) return picture;
        int link = image.Link;
        var button = new Button { Content = picture };
        button.Classes.Add("previewlink");
        Avalonia.Automation.AutomationProperties.SetName(button, Text.Loc.Get(Text.K.PreviewPanelLink, ("text", image.Alt), ("url", _doc.Links[link].Target)));
        ToolTip.SetTip(button, _doc.Links[link].Target);
        button.Click += (_, _) => ActivateLink(link);
        return button;
    }

    // ---- drawing ----

    public override void Render(DrawingContext context)
    {
        var p = Palette;
        context.FillRectangle(p.Page, new Rect(Bounds.Size));
        if (_doc.Blocks.Count == 0) return;
        EnsureTops();
        var view = View;
        int first = BlockAtY(view.Top), last = BlockAtY(view.Bottom);
        var (selStart, selEnd) = SelectionRange;
        for (int i = first; i <= last; i++)
            if (_laid.TryGetValue(i, out var laid)) DrawBlock(context, i, laid, selStart, selEnd);
    }

    private void DrawBlock(DrawingContext dc, int index, Laid laid, int selStart, int selEnd)
    {
        var b = _doc.Blocks[index];
        var p = Palette;
        double top = _tops[index], height = laid.Height, left = Left(b), width = BlockWidth(b);
        DrawContainers(dc, index, b, laid);
        switch (b.Kind)
        {
            case ReaderBlockKind.Rule:
                dc.FillRectangle(p.Border, new Rect(left, top, width, height));
                break;
            case ReaderBlockKind.Code:
            {
                var rect = new Rect(left, top, width, height);
                using (dc.PushClip(rect))
                {
                    double r = 6;
                    var round = new Rect(left, top - (b.GroupFirst ? 0 : r), width, height + (b.GroupFirst ? 0 : r) + (b.GroupLast ? 0 : r));
                    dc.DrawRectangle(p.CodeBg, null, new RoundedRect(round, r));
                }
                if (b.GroupLast) DrawCodeScrollBar(dc, index, b, top + height);
                break;
            }
            case ReaderBlockKind.TableRow:
            {
                var widths = Columns(b);
                double x = left;
                var pen = new Pen(p.Border, 1);
                bool stripe = !b.Header && b.Row % 2 == 1;
                for (int c = 0; c < widths.Length; c++)
                {
                    var cell = new Rect(Math.Round(x) + 0.5, Math.Round(top) + 0.5, Math.Round(widths[c]), Math.Round(height));
                    dc.DrawRectangle(stripe ? p.Stripe : null, pen, cell);
                    x += widths[c];
                }
                break;
            }
            case ReaderBlockKind.AlertTitle:
            {
                var kind = b.Path.LastOrDefault(c => c.Kind == ReaderContainerKind.Alert)?.AlertKind ?? "NOTE";
                string icon = MarkdownReaderModel.AlertKinds.TryGetValue(kind, out var look) ? look.Icon : MarkdownReaderModel.AlertKinds["NOTE"].Icon;
                double scale = Em / 16;
                var geometry = Geometry.Parse(icon);
                using (dc.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(left, top + (height - 16 * scale) / 2)))
                    dc.DrawGeometry(null, new Pen(p.Alert(kind), 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
                break;
            }
            case ReaderBlockKind.Heading when b.Level <= 2:
                dc.FillRectangle(p.HeadingRule, new Rect(left, top + height - 1, width, 1));
                break;
        }
        foreach (var box in laid.Boxes) DrawBox(dc, index, b, box, top, selStart, selEnd);
        if (b.Text.Length == 0 && selStart < selEnd && selStart <= b.DocStart && selEnd > b.DocStart)
            dc.FillRectangle(p.Selection, new Rect(left, top, width, height));
    }

    private void DrawBox(DrawingContext dc, int index, ReaderBlock b, Box box, double top, int selStart, int selEnd)
    {
        var p = Palette;
        double scroll = box.Clip > 0 ? _codeScroll.GetValueOrDefault(b.Group) : 0;
        var origin = new Point(box.Origin.X - scroll, top + box.Origin.Y);
        using var clip = box.Clip > 0 ? dc.PushClip(new Rect(box.Origin.X, top, box.Clip, _heights[index])) : default(DrawingContext.PushedState?);
        if (b.Kind != ReaderBlockKind.Code)
            foreach (var r in InlineCodeRects(b, box))
                dc.DrawRectangle(p.InlineCode, null, new RoundedRect(new Rect(origin.X + r.X, origin.Y + r.Y, r.Width, r.Height), 6));
        int s = Math.Max(selStart - b.DocStart, box.Start), e = Math.Min(selEnd - b.DocStart, box.Start + box.Length);
        if (e > s)
            foreach (var r in box.Range(s - box.Start, e - box.Start))
                dc.FillRectangle(p.Selection, new Rect(origin.X + r.X, origin.Y + r.Y, Math.Max(r.Width, 3), r.Height));
        box.Layout.Draw(dc, origin);
        foreach (var span in b.Spans)
        {
            if (span.Link < 0 || (span.Link != _hoverLink && !(span.Link == _focusedLink && IsFocused))) continue;
            int from = Math.Max(span.Start, box.Start), to = Math.Min(span.Start + span.Length, box.Start + box.Length);
            if (to <= from) continue;
            foreach (var r in box.Range(from - box.Start, to - box.Start))
            {
                if (span.Link == _hoverLink) dc.FillRectangle(p.Link, new Rect(origin.X + r.X, origin.Y + r.Bottom - r.Height * 0.18, r.Width, 1));
                if (span.Link == _focusedLink && IsFocused) dc.DrawRectangle(null, new Pen(p.Link, 1), new Rect(origin.X + r.X - 1, origin.Y + r.Y, r.Width + 2, r.Height));
            }
        }
    }

    /// <summary>
    /// The backgrounds of the inline code of a box relative to its origin, one per line the code stands on: over the text and its side padding, 0.2em of the code font above and below its glyphs,
    /// centred on the line.
    /// </summary>
    private IEnumerable<Rect> InlineCodeRects(ReaderBlock b, Box box)
    {
        double codeSize = 0.85 * FontSize(b), height = (1.2 + 2 * 0.2) * codeSize;
        foreach (var span in b.Spans)
        {
            if ((span.Style & ReaderStyle.Code) == 0) continue;
            int from = Math.Max(span.Start, box.Start) - box.Start, to = Math.Min(span.Start + span.Length, box.Start + box.Length) - box.Start;
            if (to <= from) continue;
            int first = box.ToLayout(from), last = box.EndToLayout(to);
            if (span.Start >= box.Start) first--; // the padding before the code
            if (span.Start + span.Length <= box.Start + box.Length) last++; // and after it
            foreach (var r in box.Layout.HitTestTextRange(first, last - first))
                yield return new Rect(r.X, r.Y + Math.Max(0, (r.Height - height) / 2), r.Width, Math.Min(r.Height, height));
        }
    }

    /// <summary>The bars of quotes and alerts beside the block, joined to the blocks of the same container above and below, and the markers of the items it starts.</summary>
    private void DrawContainers(DrawingContext dc, int index, ReaderBlock b, Laid laid)
    {
        var p = Palette;
        double top = _tops[index], bottom = top + laid.Height;
        var previous = index > 0 ? _doc.Blocks[index - 1] : null;
        var next = index + 1 < _doc.Blocks.Count ? _doc.Blocks[index + 1] : null;
        for (int depth = 0; depth < b.Path.Length; depth++)
        {
            var c = b.Path[depth];
            if (c.Kind is not (ReaderContainerKind.Quote or ReaderContainerKind.Alert)) continue;
            double pad = c.Kind == ReaderContainerKind.Alert ? 0.5 * Em : 0;
            bool joinsAbove = previous is not null && Array.IndexOf(previous.Path, c) >= 0;
            bool joinsBelow = next is not null && Array.IndexOf(next.Path, c) >= 0;
            double from = joinsAbove ? top - Gap(index) : top - pad, to = joinsBelow ? bottom : bottom + pad;
            dc.FillRectangle(c.Kind == ReaderContainerKind.Alert ? p.Alert(c.AlertKind) : p.Border, new Rect(LeftAt(b, depth), from, 0.25 * Em, to - from));
        }
        double firstBaseline = laid.Boxes.Count > 0 && laid.Boxes[0].Layout.TextLines.Count > 0 ? laid.Boxes[0].Origin.Y + laid.Boxes[0].Layout.TextLines[0].Baseline : 0.75 * Em;
        foreach (var (depth, layout) in laid.Markers)
        {
            double baseline = layout.TextLines.Count > 0 ? layout.TextLines[0].Baseline : 0;
            layout.Draw(dc, new Point(LeftAt(b, depth), top + firstBaseline - baseline));
        }
        foreach (int depth in b.Markers)
            if (b.Path[depth].Task is { } done)
            {
                double size = 0.9 * Em, x = LeftAt(b, depth + 1) - 1.4 * Em, lineHeight = laid.Boxes.Count > 0 && laid.Boxes[0].Layout.TextLines.Count > 0 ? laid.Boxes[0].Layout.TextLines[0].Height : 1.5 * Em;
                var box = new Rect(x, top + (lineHeight - size) / 2, size, size);
                dc.DrawRectangle(done ? p.Link : null, new Pen(done ? p.Link : p.Muted, 1), new RoundedRect(box, 3));
                if (done)
                {
                    var tick = new StreamGeometry();
                    using (var g = tick.Open())
                    {
                        g.BeginFigure(new Point(box.X + size * 0.22, box.Y + size * 0.52), false);
                        g.LineTo(new Point(box.X + size * 0.42, box.Y + size * 0.72));
                        g.LineTo(new Point(box.X + size * 0.78, box.Y + size * 0.3));
                        g.EndFigure(false);
                    }
                    dc.DrawGeometry(null, new Pen(p.Page, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), tick);
                }
            }
    }

    // ---- code that scrolls sideways ----

    /// <summary>How far the widest line of a code block reaches past its box; 0 when it fits. Measured over the slices that are laid out.</summary>
    private (double Overflow, double Clip) CodeOverflow(int group)
    {
        double widest = 0, clip = 0;
        for (int i = _realized.First; i <= _realized.Last && i < _doc.Blocks.Count; i++)
            if (_doc.Blocks[i].Group == group && _doc.Blocks[i].Kind == ReaderBlockKind.Code && _laid.TryGetValue(i, out var laid) && laid.Boxes.Count > 0)
            {
                widest = Math.Max(widest, laid.Boxes[0].Layout.WidthIncludingTrailingWhitespace);
                clip = laid.Boxes[0].Clip;
            }
        return (Math.Max(0, widest - clip), clip);
    }

    private Rect? CodeThumb(int index)
    {
        var b = _doc.Blocks[index];
        if (b.Kind != ReaderBlockKind.Code || !b.GroupLast || !_laid.ContainsKey(index)) return null;
        var (overflow, clip) = CodeOverflow(b.Group);
        if (overflow <= 0 || clip <= 0) return null;
        double trackLeft = Left(b) + Em, bottom = _tops[index] + _heights[index];
        double thumb = Math.Max(24, clip * clip / (clip + overflow));
        double x = trackLeft + (clip - thumb) * (_codeScroll.GetValueOrDefault(b.Group) / overflow);
        return new Rect(x, bottom - 0.5 * Em, thumb, 4);
    }

    private void DrawCodeScrollBar(DrawingContext dc, int index, ReaderBlock b, double bottom)
    {
        if (CodeThumb(index) is { } thumb) dc.DrawRectangle(Palette.Muted, null, new RoundedRect(thumb, 2));
    }

    internal void ScrollCode(int group, double delta)
    {
        var (overflow, _) = CodeOverflow(group);
        _codeScroll[group] = Math.Clamp(_codeScroll.GetValueOrDefault(group) + delta, 0, overflow);
        InvalidateVisual();
    }

    internal double CodeScroll(int group) => _codeScroll.GetValueOrDefault(group);

    // ---- colours ----

    private ReaderPalette Palette => _palette ??= new ReaderPalette(this);

    /// <summary>The colours of the page: GitHub's light and dark ones from the theme (keys <c>Md…</c>), the theme's own brushes in high contrast.</summary>
    internal sealed class ReaderPalette
    {
        private readonly Control _owner;

        public ReaderPalette(Control owner)
        {
            _owner = owner;
            Fg = Get("MdFg", "Fg", Brushes.Black);
            Muted = Get("MdMuted", "FgMuted", Brushes.Gray);
            Border = Get("MdBorder", "Border", Brushes.LightGray);
            HeadingRule = Get("MdHeadingRule", "Border", Brushes.LightGray);
            CodeBg = Get("MdCodeBg", "Bg1", Brushes.WhiteSmoke);
            InlineCode = Get("MdInlineCode", "BgHover", Brushes.Gainsboro);
            Stripe = Get("MdStripe", "Bg1", Brushes.WhiteSmoke);
            Link = Get("MdLink", "Accent", Brushes.RoyalBlue);
            Page = Get("MdPage", "BgEditor", Brushes.White);
            Selection = Get("MdSelection", "SelectionBg", new SolidColorBrush(Color.FromArgb(0x55, 0x09, 0x69, 0xDA)));
        }

        private IBrush Get(string key, string fallback, IBrush last) =>
            _owner.TryFindResource(key, _owner.ActualThemeVariant, out var v) && v is IBrush b ? b
            : _owner.TryFindResource(fallback, _owner.ActualThemeVariant, out var f) && f is IBrush fb ? fb : last;

        public IBrush Fg { get; }
        public IBrush Muted { get; }
        public IBrush Border { get; }
        public IBrush HeadingRule { get; }
        public IBrush CodeBg { get; }
        public IBrush InlineCode { get; }
        public IBrush Stripe { get; }
        public IBrush Link { get; }
        public IBrush Page { get; }
        public IBrush Selection { get; }

        public IBrush Alert(string? kind) => (kind ?? "").ToUpperInvariant() switch
        {
            "TIP" => Get("MdTip", "Good", Brushes.Green),
            "IMPORTANT" => Get("MdImportant", "GitConflict", Brushes.Purple),
            "WARNING" => Get("MdWarning", "GitModified", Brushes.Goldenrod),
            "CAUTION" => Get("MdCaution", "Danger", Brushes.Red),
            _ => Get("MdNote", "Accent", Brushes.RoyalBlue),
        };
    }
}

/// <summary>The reader for a screen reader: a document named like its host, whose value is the selection or the text in view; a link reached with Tab gives the page its name.</summary>
internal sealed class MarkdownReaderAutomationPeer(MarkdownReader owner) : Avalonia.Automation.Peers.ControlAutomationPeer(owner), Avalonia.Automation.Provider.IValueProvider
{
    protected override Avalonia.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() => Avalonia.Automation.Peers.AutomationControlType.Document;

    protected override string GetClassNameCore() => nameof(MarkdownReader);

    protected override string? GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : Text.Loc.Get(Text.K.A11yPreviewBody);

    public bool IsReadOnly => true;

    public string? Value => owner.VisibleText();

    public void SetValue(string? value) { }
}
