using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.App.Editing;

/// <summary>What the minimap needs from a text surface: the lines to draw, what is on screen and a way to scroll.</summary>
internal interface IMinimapHost
{
    /// <summary>Number of lines the map covers; for a paged document the lines indexed so far.</summary>
    long LineCount { get; }

    /// <summary>The first <see cref="Minimap.MaxChars"/> characters of a zero-based line; null when it is not available (yet).</summary>
    string? LineText(long line);

    /// <summary>The zero-based first line on screen and the number of lines shown.</summary>
    (long First, long Count) Visible { get; }

    /// <summary>Scrolls so that <paramref name="line"/> is the first line on screen.</summary>
    void ScrollToLine(long line);

    /// <summary>Raised when the first line on screen, the number of lines shown or the line count changed.</summary>
    event Action? MapChanged;

    /// <summary>Raised when <see cref="LineText"/> answers differently without an edit (hidden values were shown or hidden).</summary>
    event Action? ContentChanged;

    /// <summary>The document is a large file: when it is many screens long, or its line index is still being built, the map shows samples spread over the whole file placed by byte position (<see cref="MapBytes"/>).</summary>
    bool Sampled => false;

    /// <summary>False while <see cref="LineCount"/> still grows because the line index of a paged document is being built.</summary>
    bool IndexComplete => true;

    /// <summary>Length in bytes of a document whose map is placed by byte position, so that the picture and the pointer do not move while the line index grows; 0 for a document placed by lines.</summary>
    long MapBytes => 0;

    /// <summary>The bytes the lines on screen occupy, from the start of the first to the end of the last; (-1, -1) while that is not known.</summary>
    (long Start, long End) VisibleBytes => (-1, -1);

    /// <summary>Up to <paramref name="rows"/> lines (see <see cref="LineText"/>) from the line that holds the byte <paramref name="offset"/>; empty when that line cannot be found.</summary>
    string?[] SampleStrip(long offset, int rows) => [];

    /// <summary>Scrolls so that the line holding the byte <paramref name="offset"/> is the first on screen. The caret does not move.</summary>
    void ScrollToOffset(long offset)
    {
    }

    /// <summary>The place of a line in the file as a share of its length (0 to 1) when it is known without reading the file; NaN when it is not and the line number has to tell.</summary>
    double ShareOfLine(long line) => double.NaN;

    /// <summary>Zero-based lines that hold another occurrence of the selected text, ascending; empty when none are marked.</summary>
    IReadOnlyList<long> OccurrenceLines => [];

    /// <summary>Raised when <see cref="OccurrenceLines"/> changed.</summary>
    event Action? OccurrencesChanged
    {
        add { }
        remove { }
    }
}

/// <summary>What a mark in the strip stands for; in a row of the strip the higher value wins.</summary>
internal enum MinimapMarkKind : byte
{
    Occurrence = 1,
    Match = 2,
    Warning = 3,
    Error = 4,
}

/// <summary>A line to mark in the narrow strip at the right edge of the minimap, which covers the whole file.</summary>
internal readonly record struct MinimapMark(long Line, MinimapMarkKind Kind);

/// <summary>Where the picture of the lines and the viewport rectangle sit, in pixels of the minimap. A <paramref name="Sampled"/> picture covers the whole file and the rectangle is a thin pointer.</summary>
internal readonly record struct MinimapLayout(long Top, int Rows, int Capacity, double ThumbTop, double ThumbHeight, bool Sampled = false);

/// <summary>
/// A point the sliding picture is pinned to: with <paramref name="First"/> as the first line on screen the picture starts at line <paramref name="Top"/>. A click puts the rectangle under the
/// cursor and pins the picture there, so the text under the cursor does not move; the picture then slides proportionally from that point to both ends of the file.
/// </summary>
internal readonly record struct MinimapAnchor(long First, long Top);

/// <summary>
/// The arithmetic of the minimap. A file that fits shows every line. A longer one shows a window of the lines that slides proportionally with the scroll position, as in VS Code:
/// the rectangle travels the whole height of the map from the first line to the last, like a scroll bar, and only the lines of that window are ever read. A large file read in pages
/// is sampled instead: strips of consecutive lines from the starts of equal parts of the file, so the picture covers all of it, and everything is placed by byte position, so that nothing
/// moves while the line index is still being built.
/// </summary>
internal static class MinimapGeometry
{
    public const int Proportional = 4;

    public static int CapacityOf(int height, int rowPx) => Math.Max(1, height / Math.Max(1, rowPx));

    /// <summary>Whether a host is drawn as a sampled picture: it asks for sampling, and its line index is not complete yet or it has many screens of lines.</summary>
    public static bool UsesSampling(bool hostSampled, long mapBytes, bool indexComplete, long lines, int capacity) =>
        hostSampled && mapBytes > 0 && (!indexComplete || lines > (long)capacity * Proportional);

    public static MinimapLayout Compute(long lines, long first, long count, int height, int rowPx, MinimapAnchor? anchor = null)
    {
        rowPx = Math.Max(1, rowPx);
        int capacity = CapacityOf(height, rowPx);
        lines = Math.Max(0, lines);
        count = Math.Max(1, count);
        first = Math.Clamp(first, 0, Math.Max(0, lines - 1));
        long top = lines > capacity ? TopOf(lines, first, count, capacity, anchor) : 0;
        int rows = (int)Math.Min(lines, capacity);
        double shown = rows * (double)rowPx;
        double thumbHeight = Math.Clamp(count * (double)rowPx, Math.Min(8, shown), Math.Max(1, shown));
        double thumbTop = Math.Clamp((first - top) * (double)rowPx, 0, Math.Max(0, shown - thumbHeight));
        return new MinimapLayout(top, rows, capacity, thumbTop, thumbHeight);
    }

    /// <summary>The first line of the picture of a file longer than the picture: proportional to the scroll position, kept so that the rectangle stays on the picture.</summary>
    private static long TopOf(long lines, long first, long count, int capacity, MinimapAnchor? anchor)
    {
        long maxTop = lines - capacity, maxFirst = lines - count;
        if (maxFirst <= 0) return 0;
        first = Math.Min(first, maxFirst);
        double top = first * (double)maxTop / maxFirst;
        if (Effective(anchor, maxFirst, maxTop, capacity, count) is { } a)
            top = first <= a.First ? a.Top * (double)first / a.First : a.Top + (first - a.First) * (double)(maxTop - a.Top) / (maxFirst - a.First);
        long slack = Math.Max(0, capacity - count);
        return Math.Clamp(Math.Clamp((long)Math.Round(top), first - slack, first), 0, maxTop);
    }

    /// <summary>
    /// The anchor as the picture can honour it: the row of the rectangle in the picture is kept one row off both edges, so that dragging from here can still reach the first and the last
    /// line. Null when there is no anchor or it lies at an end of the file, where the proportional picture passes anyway.
    /// </summary>
    private static MinimapAnchor? Effective(MinimapAnchor? anchor, long maxFirst, long maxTop, int capacity, long count)
    {
        if (anchor is not { } a || a.First <= 0 || a.First >= maxFirst) return null;
        long slack = Math.Max(0, capacity - count);
        long low = Math.Max(Math.Min(1, a.First), a.First - maxTop), high = Math.Min(a.First, Math.Max(0, slack - 1));
        if (low > high) return null;
        return new MinimapAnchor(a.First, a.First - Math.Clamp(a.First - a.Top, low, high));
    }

    /// <summary>The zero-based line drawn at <paramref name="y"/>.</summary>
    public static long LineAt(MinimapLayout layout, long lines, int rowPx, double y) =>
        Math.Clamp(layout.Top + (long)Math.Floor(y / Math.Max(1, rowPx)), 0, Math.Max(0, lines - 1));

    /// <summary>
    /// A click at <paramref name="y"/> outside the rectangle: the clicked line goes to the middle of the screen, and the picture stays where it is, so the cursor is over the rectangle and the
    /// text under it has not moved. Returns the first line to scroll to and the anchor that pins the picture (null for a file that fits whole).
    /// </summary>
    public static (long First, MinimapAnchor? Anchor) ClickAt(MinimapLayout layout, long lines, long count, int rowPx, double y)
    {
        count = Math.Max(1, count);
        long first = Math.Clamp(LineAt(layout, lines, rowPx, y) - count / 2, 0, Math.Max(0, lines - count));
        return (first, lines > layout.Capacity ? new MinimapAnchor(first, layout.Top) : null);
    }

    /// <summary>
    /// The first line on screen after the rectangle moved <paramref name="dy"/> pixels away from where it was at the press: the rectangle follows the cursor over its whole travel, from the first
    /// line to the last. <paramref name="layout"/>, <paramref name="lines"/>, <paramref name="count"/> and <paramref name="anchor"/> are those of the press and stay fixed for the whole drag, so the scale cannot change under the cursor.
    /// </summary>
    public static long FirstAfterDrag(MinimapLayout layout, long lines, long count, int rowPx, double dy, MinimapAnchor? anchor = null)
    {
        rowPx = Math.Max(1, rowPx);
        count = Math.Max(1, count);
        long maxFirst = Math.Max(0, lines - count);
        double shown = layout.Rows * (double)rowPx, travel = shown - layout.ThumbHeight;
        if (lines <= layout.Capacity || travel <= 0 || maxFirst == 0)
            return Math.Clamp((long)Math.Round(layout.ThumbTop / rowPx + dy / rowPx), 0, maxFirst);
        double p = Math.Clamp(layout.ThumbTop + dy, 0, travel);
        double first;
        if (Effective(anchor, maxFirst, lines - layout.Capacity, layout.Capacity, count) is { } a)
        {
            double at = Math.Clamp((a.First - a.Top) * (double)rowPx, 0, travel);
            first = at <= 0 ? 0 : p <= at ? a.First * p / at : a.First + (maxFirst - a.First) * (p - at) / Math.Max(1, travel - at);
        }
        else first = maxFirst * p / travel;
        return Math.Clamp((long)Math.Round(first), 0, maxFirst);
    }

    // ---- a sampled picture: the whole file, placed by byte position ----

    public static MinimapLayout ComputeSampled(long bytes, long start, long end, int height, int rowPx)
    {
        rowPx = Math.Max(1, rowPx);
        int capacity = CapacityOf(height, rowPx);
        double full = capacity * (double)rowPx;
        bytes = Math.Max(1, bytes);
        start = Math.Clamp(start, 0, bytes);
        end = Math.Clamp(end, start, bytes);
        double pointer = Math.Clamp((end - start) * full / bytes, 2, full);
        return new MinimapLayout(0, capacity, capacity, Math.Clamp(start * full / bytes, 0, full - pointer), pointer, true);
    }

    /// <summary>The byte at <paramref name="y"/> of a sampled picture: the same share of the file.</summary>
    public static long OffsetAt(MinimapLayout layout, long bytes, int rowPx, double y) =>
        Math.Clamp((long)(y / Math.Max(1, layout.Rows * (double)Math.Max(1, rowPx)) * bytes), 0, Math.Max(0, bytes));

    /// <summary>
    /// The byte a strip of a sampled picture starts at. The picture is cut into one equal part of the file per strip of <paramref name="stripRows"/> rows and shows the first lines of each part.
    /// </summary>
    public static long StripOffset(MinimapLayout layout, long bytes, int strip, int stripRows)
    {
        long strips = (layout.Rows + Math.Max(1, stripRows) - 1) / Math.Max(1, stripRows);
        return Math.Clamp(strip * Math.Max(0, bytes) / Math.Max(1, strips), 0, Math.Max(0, bytes));
    }

    /// <summary>The first byte on screen after the pointer moved <paramref name="dy"/> pixels away from where it was with <paramref name="pressOffset"/> on top.</summary>
    public static long OffsetAfterDrag(MinimapLayout layout, long bytes, int rowPx, long pressOffset, double dy) =>
        Math.Clamp((long)Math.Round(pressOffset + dy / Math.Max(1, layout.Rows * (double)Math.Max(1, rowPx)) * bytes), 0, Math.Max(0, bytes));
}

/// <summary>
/// The reduced picture of the text at the right edge of a surface: the lines in the colors of the syntax highlighting, a translucent
/// rectangle for the part on screen, and a narrow strip for marks that cover the whole file. Dragging the rectangle (or clicking)
/// scrolls the surface. Only the lines of the picture are read, never the whole document, so the cost does not depend on its size;
/// the picture is drawn into a bitmap off screen in short slices and shown once complete. A log without a grammar is drawn in the
/// colors of its levels.
/// </summary>
internal sealed unsafe class Minimap : Control, IDisposable
{
    public const double LogicalWidth = 120;
    public const int MaxChars = 300;
    private const int MaxRows = 500;
    private const int TabColumns = 4;

    /// <summary>Pixels left free at the left edge of the picture for the bars of the changes.</summary>
    private const int TextPad = 5;
    private static readonly TimeSpan Slice = TimeSpan.FromMilliseconds(8);
    private static readonly TimeSpan TextSettle = TimeSpan.FromMilliseconds(120);

    /// <param name="Bytes">The length of the file a sampled picture was spread over; 0 for a picture of consecutive lines.</param>
    private readonly record struct BuildKey(long Top, int Rows, int Width, int Height, int RowPx, bool Dark, long Version, long Bytes);

    /// <summary>What a press of the mouse fixed for the whole drag, so that nothing the document does meanwhile can change the scale under the cursor.</summary>
    private sealed record DragState(MinimapLayout Layout, long Lines, long Count, int RowPx, long Bytes, long PressOffset, double PressY, MinimapAnchor? Anchor);

    private readonly OpenDocument _doc;
    private readonly IMinimapHost _host;
    private readonly DispatcherTimer _textTimer = new() { Interval = TextSettle };
    private readonly Dictionary<string, Color> _kindColors = [];
    private SyntaxFeeder? _feeder;
    private Dictionary<long, Token[]> _tokens = [];
    private long _version;
    private WriteableBitmap? _shown;
    private WriteableBitmap? _spare;
    private BuildKey _shownKey;
    private BuildKey _backKey;
    private WriteableBitmap? _back;
    private int _nextRow;
    private bool _queued;
    private bool _attached;
    private bool _disposed;
    private MinimapMark[] _marks = [];
    private MinimapMark[] _filterMarks = [];
    private GitMark[] _changes = [];
    private (string Grammar, Tokenizer Tokenizer)? _sampler;
    private LineState? _sampleState;
    private readonly List<Token> _sampleTokens = [];
    private byte[] _lane = [];
    private (int Height, long Lines, int Stamp, bool Sampled) _laneKey;
    private int _marksStamp;
    private LogHeatmap? _heat;
    private (int Height, LogHeatmap? Heat) _heatKey;
    private byte[] _heatErrors = [];
    private byte[] _heatWarnings = [];
    private DragState? _drag;
    private MinimapAnchor? _anchor;
    private int _stripIndex = -1;
    private string?[] _strip = [];

    public Minimap(OpenDocument doc, IMinimapHost host)
    {
        _doc = doc;
        _host = host;
        Width = LogicalWidth;
        ClipToBounds = true;
        Focusable = false;
        _textTimer.Tick += (_, _) =>
        {
            _textTimer.Stop();
            Invalidate();
        };
        UpdateName();
    }

    /// <summary>Sets the screen reader name from the current UI language.</summary>
    public void UpdateName() => AutomationProperties.SetName(this, Loc.Get(K.A11yMinimap));

    /// <summary>Replaces the marks in the strip at the right edge; a line outside the document is ignored.</summary>
    public void SetMarks(IReadOnlyList<MinimapMark> marks)
    {
        _marks = [.. marks];
        _marksStamp++;
        InvalidateVisual();
    }

    /// <summary>Replaces the marks of the lines the filter of a live log view keeps. They share the strip with the find marks; in a row of the strip the more severe kind wins.</summary>
    public void SetFilterMarks(IReadOnlyList<MinimapMark> marks)
    {
        _filterMarks = [.. marks];
        _marksStamp++;
        InvalidateVisual();
    }

    /// <summary>Replaces the changes Git reports: a narrow bar at the left edge beside the rows of the lines they stand for (a change in the part of the file the picture does not show is not drawn here; the strip beside the scroll bar of the editor shows all of them).</summary>
    public void SetChangeMarks(IReadOnlyList<GitMark> marks)
    {
        _changes = [.. marks];
        InvalidateVisual();
    }

    /// <summary>The changes drawn at the left edge, for tests.</summary>
    internal IReadOnlyList<GitMark> ChangeMarks => _changes;

    /// <summary>The bars of the changes in the layout now: for each change that has rows in the picture, its first row and the number of rows; for tests.</summary>
    internal IReadOnlyList<(GitMark Mark, int Row, int Rows)> ChangeBars()
    {
        if (Wanted() is not { Layout.Sampled: false } wanted) return [];
        return [.. _changes.Select(m => (m, Row: (int)Math.Clamp(m.Line - wanted.Layout.Top, int.MinValue, int.MaxValue), Rows: Math.Max(1, m.Lines)))
            .Where(c => c.Row + c.Rows > 0 && c.Row < wanted.Layout.Rows)];
    }

    /// <summary>The marks of the live log view filter, for tests.</summary>
    internal IReadOnlyList<MinimapMark> FilterMarks => _filterMarks;

    /// <summary>The picture covers the whole file with samples, for tests.</summary>
    internal bool IsSampled => Wanted() is { Layout.Sampled: true };

    /// <summary>The heatmap that is shown, for tests.</summary>
    internal LogHeatmap? Heat => _heat;

    /// <summary>Shows the density of error and warning lines of a log along the strip at the right edge, which covers the whole file; null removes it.</summary>
    public void SetHeat(LogHeatmap? heat)
    {
        _heat = heat;
        InvalidateVisual();
    }

    private double Scale => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
    private int PixelWidth => (int)Math.Round(Bounds.Width * Scale);
    private int PixelHeight => (int)Math.Round(Bounds.Height * Scale);
    private bool Dark => ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_attached || _disposed) return;
        _attached = true;
        _host.MapChanged += OnMapChanged;
        _host.ContentChanged += Invalidate;
        _host.OccurrencesChanged += OnOccurrencesChanged;
        _doc.Doc.Changed += OnDocumentChanged;
        _doc.Replaced += OnReplaced;
        _doc.LanguageChanged += Invalidate;
        _doc.LogLevelsChanged += Invalidate;
        ActualThemeVariantChanged += OnThemeChanged;
        UpdateName();
        Invalidate();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Unhook();
    }

    private void Unhook()
    {
        if (!_attached) return;
        _attached = false;
        _host.MapChanged -= OnMapChanged;
        _host.ContentChanged -= Invalidate;
        _host.OccurrencesChanged -= OnOccurrencesChanged;
        _doc.Doc.Changed -= OnDocumentChanged;
        _doc.Replaced -= OnReplaced;
        _doc.LanguageChanged -= Invalidate;
        _doc.LogLevelsChanged -= Invalidate;
        ActualThemeVariantChanged -= OnThemeChanged;
        _textTimer.Stop();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Schedule(DispatcherPriority.Render);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        _kindColors.Clear();
        Invalidate();
    }

    /// <summary>The other occurrences of the selection changed: only the strip at the right edge is drawn again.</summary>
    private void OnOccurrencesChanged()
    {
        _marksStamp++;
        InvalidateVisual();
    }

    /// <summary>The text was replaced as a whole (a reload): what a click pinned the picture to is gone with it.</summary>
    private void OnReplaced()
    {
        _anchor = null;
        Invalidate();
    }

    private void OnMapChanged()
    {
        InvalidateVisual();
        Schedule(DispatcherPriority.Render);
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) RestartTextTimer();
        else Dispatcher.UIThread.Post(RestartTextTimer);
    }

    private void RestartTextTimer()
    {
        if (_disposed) return;
        _textTimer.Stop();
        _textTimer.Start();
    }

    /// <summary>Everything the picture shows may have changed: the text, the colors or the theme.</summary>
    private void Invalidate()
    {
        if (_disposed) return;
        _version++;
        InvalidateVisual();
        Schedule(DispatcherPriority.Render);
    }

    private void Schedule(DispatcherPriority priority)
    {
        if (_queued || _disposed) return;
        _queued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _queued = false;
            Pump();
        }, priority);
    }

    // ---- colors ----

    private void ApplyTokens(SyntaxViewport viewport)
    {
        if (viewport.Revision != _doc.Doc.Revision) return;
        _tokens = viewport.Lines.ToDictionary(line => line.Line, line => line.Tokens.ToArray());
        Invalidate();
    }

    private void ClearTokens()
    {
        if (_tokens.Count == 0) return;
        _tokens = [];
        Invalidate();
    }

    private void RequestColors(MinimapLayout layout, int columns)
    {
        if (_feeder is null)
        {
            if (LanguageModes.GrammarId(_doc) is null) return;
            _feeder = new SyntaxFeeder(_doc, ApplyTokens, ClearTokens, _ => null, () => _host.LineCount, Invalidate, TextSettle);
        }
        if (_feeder.Active && layout.Rows > 0) _feeder.SetVisible(layout.Top, layout.Top + layout.Rows - 1, 0, columns);
    }

    private Color Resource(string key, Color fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is ISolidColorBrush brush ? brush.Color : fallback;

    private Color KindColor(string kind, Color plain)
    {
        if (_kindColors.TryGetValue(kind, out var known)) return known;
        var palette = Dark ? SyntaxPalette.Dark : SyntaxPalette.Light;
        return _kindColors[kind] = palette.GetBrush(kind) is ISolidColorBrush brush ? brush.Color : plain;
    }

    // ---- building the picture ----

    /// <summary>How many times the picture was started from its first row, for tests: a growing index must not start it at every notice.</summary>
    internal int BuildsStarted { get; private set; }

    /// <summary>The layout the minimap shows now, for tests; null while it is not visible.</summary>
    internal MinimapLayout? Layout => Wanted()?.Layout;

    /// <summary>The line the picture starts at and the first line on screen, as the layout has them now; for tests.</summary>
    internal MinimapAnchor? Anchor => _anchor;

    private (BuildKey Key, MinimapLayout Layout, int RowPx)? Wanted()
    {
        int width = PixelWidth, height = PixelHeight;
        if (width < 8 || height < 8 || !IsVisible) return null;
        int rowPx = Math.Max(2, (int)Math.Ceiling(height / (double)MaxRows));
        var (first, count) = _host.Visible;
        long lines = _host.LineCount, bytes = _host.MapBytes;
        MinimapLayout layout;
        if (MinimapGeometry.UsesSampling(_host.Sampled, bytes, _host.IndexComplete, lines, MinimapGeometry.CapacityOf(height, rowPx)))
        {
            var (start, end) = _host.VisibleBytes;
            layout = MinimapGeometry.ComputeSampled(bytes, Math.Max(0, start), Math.Max(0, end), height, rowPx);
        }
        else layout = MinimapGeometry.Compute(lines, first, count, height, rowPx, _anchor);
        return (new BuildKey(layout.Top, layout.Rows, width, height, rowPx, Dark, _version, layout.Sampled ? bytes : 0), layout, rowPx);
    }

    private void Pump()
    {
        long trace = Core.TypingTrace.Begin();
        try { PumpCore(); }
        finally { Core.TypingTrace.End("minimap.pump", trace); }
    }

    private void PumpCore()
    {
        // a job queued before the window closed runs after its documents were disposed; a minimap that left the visual tree has nothing to draw
        if (_disposed || !_attached || Wanted() is not { } first) return;
        if (!first.Layout.Sampled) RequestColors(first.Layout, first.Key.Width); // the colors may arrive at once and change the version
        if (Wanted() is not { } wanted) return;
        var key = wanted.Key;
        if (_shown is not null && key == _shownKey) return;
        if (_back is null || key != _backKey)
        {
            _backKey = key;
            _nextRow = 0;
            BuildsStarted++;
            var size = new PixelSize(key.Width, key.Height);
            if (_back is null || _back.PixelSize != size)
            {
                _back?.Dispose();
                if (_spare is { } spare && spare.PixelSize == size) (_back, _spare) = (spare, null);
                else _back = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            }
        }
        var plain = Resource("Fg", Dark ? Colors.White : Colors.Black);
        long started = Stopwatch.GetTimestamp();
        using (var buffer = _back.Lock())
        {
            uint* pixels = (uint*)buffer.Address.ToPointer();
            int stride = buffer.RowBytes / 4;
            if (_nextRow == 0)
            {
                for (int y = 0; y < key.Height; y++) new Span<uint>(pixels + (long)y * stride, key.Width).Clear();
                _stripIndex = -1;
            }
            int columns = key.Width - LaneWidth(key.Width) - TextPad;
            int stripRows = PluginLimitsPlaceholders.MinimapSampleStripRows;
            var sampler = wanted.Layout.Sampled ? Sampler() : null;
            while (_nextRow < key.Rows)
            {
                string? text;
                Token[]? tokens;
                if (wanted.Layout.Sampled)
                {
                    int strip = _nextRow / stripRows, at = _nextRow % stripRows;
                    if (strip != _stripIndex)
                    {
                        _stripIndex = strip;
                        _strip = _host.SampleStrip(MinimapGeometry.StripOffset(wanted.Layout, key.Bytes, strip, stripRows), stripRows);
                    }
                    text = at < _strip.Length ? _strip[at] : null;
                    tokens = SampleTokens(sampler, text, at == 0);
                }
                else
                {
                    long line = key.Top + _nextRow;
                    text = _host.LineText(line);
                    tokens = _tokens.GetValueOrDefault(line);
                }
                PaintRow(pixels, stride, key, columns, _nextRow, text, tokens ?? LevelTokens(text), plain);
                _nextRow++;
                if (Stopwatch.GetElapsedTime(started) > Slice && _nextRow < key.Rows)
                {
                    Schedule(DispatcherPriority.Background);
                    return;
                }
            }
        }
        (_spare, _shown) = (_shown, _back);
        _shownKey = key;
        _back = null;
        InvalidateVisual();
    }

    private Tokenizer? Sampler()
    {
        if (LanguageModes.GrammarId(_doc) is not { } id) return null;
        if (_sampler is not { } known || known.Grammar != id) _sampler = known = (id, LanguageModes.Tokenizer(id));
        return known.Tokenizer;
    }

    /// <summary>The tokens of a line of a sampled picture, tokenized on the spot from the state the line above it in its strip left.</summary>
    /// <remarks>shortcut: each strip starts in the initial state, so a strip that begins inside a comment or string of several lines is colored as code; a checkpointed scan of the whole file would fix it.</remarks>
    private Token[]? SampleTokens(Tokenizer? tokenizer, string? text, bool stripStart)
    {
        if (tokenizer is null) return null;
        if (stripStart || _sampleState is null) _sampleState = tokenizer.InitialState;
        if (string.IsNullOrEmpty(text)) return null;
        _sampleTokens.Clear();
        _sampleState = tokenizer.TokenizeLine(text, _sampleState, _sampleTokens);
        return [.. _sampleTokens];
    }

    /// <summary>A line of a log in the color of its level, as the editor draws it; null for a line without a level and outside log mode.</summary>
    private Token[]? LevelTokens(string? text) =>
        _doc.IsLog && !string.IsNullOrEmpty(text) && _doc.LogLevels.Classify(text) is { } level ? [new Token(0, text.Length, LogLevelKinds.Of(level))] : null;

    private int LaneWidth(int width) => Math.Max(3, (int)Math.Round(width / 28.0));

    private void PaintRow(uint* pixels, int stride, BuildKey key, int columns, int row, string? text, Token[]? tokens, Color plain)
    {
        if (string.IsNullOrEmpty(text)) return;
        int y0 = row * key.RowPx;
        int column = 0, token = 0, lastToken = -1;
        uint full = 0, soft = 0;
        for (int i = 0; i < text.Length && column < columns; i++)
        {
            char ch = text[i];
            if (ch == ' ') { column++; continue; }
            if (ch == '\t') { column += TabColumns - column % TabColumns; continue; }
            if (char.IsLowSurrogate(ch) || char.IsControl(ch)) continue;
            int at = -1;
            if (tokens is not null)
            {
                while (token < tokens.Length && tokens[token].Start + tokens[token].Length <= i) token++;
                if (token < tokens.Length && tokens[token].Start <= i) at = token;
            }
            if (at != lastToken || full == 0)
            {
                lastToken = at;
                var color = at < 0 ? plain : KindColor(tokens![at].Kind, plain);
                int alpha = at < 0 ? 150 : 255;
                full = Pack(color, alpha);
                soft = Pack(color, alpha * 45 / 100);
            }
            for (int dy = 0; dy < key.RowPx; dy++)
            {
                int y = y0 + dy;
                if (y >= key.Height) break;
                uint value = key.RowPx == 2 ? (dy == 0 ? full : soft) : (dy == key.RowPx - 1 ? 0 : full);
                if (value != 0) pixels[(long)y * stride + TextPad + column] = value;
            }
            column++;
        }
    }

    private static uint Pack(Color color, int alpha) =>
        (uint)((alpha << 24) | (color.R * alpha / 255 << 16) | (color.G * alpha / 255 << 8) | (color.B * alpha / 255));

    // ---- drawing ----

    public override void Render(DrawingContext context)
    {
        long trace = Core.TypingTrace.Begin();
        try { RenderCore(context); }
        finally { Core.TypingTrace.End("minimap.render", trace); }
    }

    private void RenderCore(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        var plain = Resource("Fg", Dark ? Colors.White : Colors.Black);
        context.FillRectangle(new ImmutableSolidColorBrush(Resource("BgEditor", Dark ? Colors.Black : Colors.White)), bounds);
        double scale = Scale;
        using var _ = context.PushTransform(Matrix.CreateScale(1 / scale, 1 / scale));
        int width = PixelWidth, height = PixelHeight;
        if (_shown is not null) context.DrawImage(_shown, new Rect(_shown.Size), new Rect(_shown.Size));
        context.DrawLine(new Pen(new ImmutableSolidColorBrush(Resource("Border", plain)), 1), new Point(0.5, 0), new Point(0.5, height));
        if (Wanted() is not { } wanted) return;
        var layout = wanted.Layout;
        var thumb = new Rect(0, layout.ThumbTop, width, layout.ThumbHeight);
        // a file that fits on the screen has nothing to scroll to, so a viewport rectangle there is only a box beside the text
        bool scrolls = layout.Sampled || _host.LineCount > _host.Visible.Count;
        if (scrolls)
        {
            context.FillRectangle(new ImmutableSolidColorBrush(plain, 0.12), thumb);
            context.FillRectangle(new ImmutableSolidColorBrush(plain, 0.4), new Rect(width - 2, thumb.Y, 2, thumb.Height));
        }
        if (layout.Sampled) // the viewport of a large file is too small for a rectangle: a thin pointer shows where it is
            context.FillRectangle(new ImmutableSolidColorBrush(Resource("Accent", Color.Parse("#3B82F6"))), new Rect(0, thumb.Y, width, Math.Max(2, Math.Round(2 * scale))));
        DrawChanges(context, wanted, width, height);
        DrawLane(context, layout, width, height, plain);
    }

    private const int ChangeBarWidth = 3;

    /// <summary>The changes of Git as bars at the left edge, on the rows of their lines; a change that is staged is fainter. A removal is a bar of one pixel row on the edge between two rows.</summary>
    private void DrawChanges(DrawingContext context, (BuildKey Key, MinimapLayout Layout, int RowPx) wanted, int width, int height)
    {
        if (_changes.Length == 0 || wanted.Layout.Sampled) return;
        foreach (var mark in _changes)
        {
            long first = mark.Line - wanted.Layout.Top, last = first + Math.Max(1, mark.Lines);
            if (last <= 0 || first >= wanted.Layout.Rows) continue;
            double y = Math.Max(0, first) * wanted.RowPx, bottom = Math.Min(wanted.Layout.Rows, last) * wanted.RowPx;
            if (mark.Lines == 0) (y, bottom) = (Math.Max(0, first * wanted.RowPx - 1), Math.Max(0, first * wanted.RowPx - 1) + 2);
            if (!this.TryFindResource(GitChangeColors.KeyOf(mark.Kind), ActualThemeVariant, out var found) || found is not ISolidColorBrush brush) continue;
            context.FillRectangle(new ImmutableSolidColorBrush(brush.Color, mark.Staged ? 0.45 : 1), new Rect(1, y, ChangeBarWidth, Math.Max(2, bottom - y)));
        }
    }

    /// <summary>The strip at the right edge covers the whole file: the marks and the heatmap. In a sampled picture a mark stands where its line is in the file by bytes, like the pointer.</summary>
    private void DrawLane(DrawingContext context, MinimapLayout layout, int width, int height, Color plain)
    {
        long lines = _host.LineCount;
        if (lines <= 0) return;
        int laneWidth = LaneWidth(width);
        double x = width - laneWidth;
        DrawHeat(context, x, laneWidth, height);
        var occurrences = _host.OccurrenceLines;
        if (_marks.Length == 0 && _filterMarks.Length == 0 && occurrences.Count == 0) return;
        if (_laneKey != (height, lines, _marksStamp, layout.Sampled))
        {
            _laneKey = (height, lines, _marksStamp, layout.Sampled);
            _lane = new byte[height];
            void Put(long line, MinimapMarkKind kind)
            {
                if (line < 0 || line >= lines) return;
                double share = layout.Sampled ? _host.ShareOfLine(line) : double.NaN;
                int row = (int)Math.Min(height - 1, double.IsNaN(share) ? line * height / lines : (long)(share * height));
                if ((byte)kind > _lane[row]) _lane[row] = (byte)kind;
            }
            foreach (long line in occurrences) Put(line, MinimapMarkKind.Occurrence);
            foreach (var mark in _marks.Concat(_filterMarks)) Put(mark.Line, mark.Kind);
        }
        var occurrenceBrush = new ImmutableSolidColorBrush(Resource("OccurrenceBorder", Color.Parse("#3B82F6")));
        for (int y = 0; y < height;)
        {
            byte kind = _lane[y];
            int end = y + 1;
            while (end < height && _lane[end] == kind) end++;
            if (kind != 0) context.FillRectangle(kind == (byte)MinimapMarkKind.Occurrence ? occurrenceBrush : MarkBrush((MinimapMarkKind)kind), new Rect(x, Math.Max(0, y - 1), laneWidth, end - y + 2));
            y = end;
        }
    }

    private const int HeatSteps = 8;
    private static readonly IBrush[] HeatErrorBrushes = HeatBrushes(Color.Parse("#E5484D"));
    private static readonly IBrush[] HeatWarningBrushes = HeatBrushes(Color.Parse("#E2A400"));

    private static IBrush[] HeatBrushes(Color color) =>
        [.. Enumerable.Range(1, HeatSteps).Select(i => (IBrush)new ImmutableSolidColorBrush(color, 0.25 + 0.75 * i / HeatSteps))];

    /// <summary>Draws the heatmap behind the marks: every row of the strip takes the densest bucket that falls in it, shown in as many steps of opacity as <see cref="HeatSteps"/>.</summary>
    private void DrawHeat(DrawingContext context, double x, int laneWidth, int height)
    {
        if (_heat is not { Buckets: > 0 } heat) return;
        if (_heatKey != (height, heat))
        {
            _heatKey = (height, heat);
            _heatErrors = new byte[height];
            _heatWarnings = new byte[height];
            for (int b = 0; b < heat.Buckets; b++)
            {
                int row = (int)Math.Min(height - 1, (long)b * height / heat.Buckets);
                _heatErrors[row] = (byte)Math.Max(_heatErrors[row], Step(heat.ErrorDensity(b)));
                _heatWarnings[row] = (byte)Math.Max(_heatWarnings[row], Step(heat.WarningDensity(b)));
            }
        }
        for (int y = 0; y < height; y++)
        {
            byte error = _heatErrors[y], warning = _heatWarnings[y];
            if (error == 0 && warning == 0) continue;
            var brush = error >= warning && error > 0 ? HeatErrorBrushes[error - 1] : HeatWarningBrushes[warning - 1];
            context.FillRectangle(brush, new Rect(x, y, laneWidth, 1));
        }
    }

    private static byte Step(double density) => density <= 0 ? (byte)0 : (byte)Math.Clamp((int)Math.Ceiling(density * HeatSteps), 1, HeatSteps);

    private static readonly IBrush MatchBrush = new ImmutableSolidColorBrush(Color.Parse("#3B82F6"));
    private static readonly IBrush WarningBrush = new ImmutableSolidColorBrush(Color.Parse("#E2A400"));
    private static readonly IBrush ErrorBrush = new ImmutableSolidColorBrush(Color.Parse("#E5484D"));

    private static IBrush MarkBrush(MinimapMarkKind kind) => kind switch
    {
        MinimapMarkKind.Error => ErrorBrush,
        MinimapMarkKind.Warning => WarningBrush,
        _ => MatchBrush,
    };

    // ---- mouse ----

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Wanted() is not { } wanted) return;
        double y = e.GetPosition(this).Y * Scale;
        var (first, count) = _host.Visible;
        long lines = _host.LineCount;
        var layout = wanted.Layout;
        bool outside = y < layout.ThumbTop || y > layout.ThumbTop + layout.ThumbHeight;
        if (layout.Sampled)
        {
            long bytes = wanted.Key.Bytes;
            var (start, end) = _host.VisibleBytes;
            long from = Math.Max(0, start), span = Math.Max(0, end - from);
            if (outside)
            {
                // the clicked place goes to the middle of the screen; the sampled picture itself never moves
                from = Math.Clamp(MinimapGeometry.OffsetAt(layout, bytes, wanted.RowPx, y) - span / 2, 0, bytes);
                _host.ScrollToOffset(from);
                layout = MinimapGeometry.ComputeSampled(bytes, from, from + span, wanted.Key.Height, wanted.RowPx);
            }
            _drag = new DragState(layout, lines, count, wanted.RowPx, bytes, from, y, null);
        }
        else
        {
            if (outside)
            {
                (first, _anchor) = MinimapGeometry.ClickAt(layout, lines, count, wanted.RowPx, y);
                _host.ScrollToLine(first);
                layout = MinimapGeometry.Compute(lines, first, count, wanted.Key.Height, wanted.RowPx, _anchor);
            }
            _drag = new DragState(layout, lines, count, wanted.RowPx, 0, first, y, _anchor);
        }
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is not { } drag) return;
        double dy = e.GetPosition(this).Y * Scale - drag.PressY;
        if (drag.Layout.Sampled) _host.ScrollToOffset(MinimapGeometry.OffsetAfterDrag(drag.Layout, drag.Bytes, drag.RowPx, drag.PressOffset, dy));
        else _host.ScrollToLine(MinimapGeometry.FirstAfterDrag(drag.Layout, drag.Lines, drag.Count, drag.RowPx, dy, drag.Anchor));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag is null) return;
        _drag = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _drag = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var (first, count) = _host.Visible;
        _host.ScrollToLine(Math.Clamp(first - (long)Math.Round(e.Delta.Y * 3), 0, Math.Max(0, _host.LineCount - count)));
        e.Handled = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Unhook();
        _disposed = true;
        _feeder?.Dispose();
        _feeder = null;
        _shown?.Dispose();
        _back?.Dispose();
        _spare?.Dispose();
        _shown = _back = _spare = null;
    }
}
