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

    /// <summary>The document is a large file: when it is many screens long, the map shows samples spread over the whole file instead of the surroundings of the viewport.</summary>
    bool Sampled => false;

    /// <summary>False while <see cref="LineCount"/> still grows because the line index of a paged document is being built.</summary>
    bool IndexComplete => true;
}

internal enum MinimapMarkKind : byte
{
    Match = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>A line to mark in the narrow strip at the right edge of the minimap, which covers the whole file.</summary>
internal readonly record struct MinimapMark(long Line, MinimapMarkKind Kind);

/// <summary>Where the picture of the lines and the viewport rectangle sit, in pixels of the minimap. A <paramref name="Sampled"/> picture covers the whole file and the rectangle is a thin pointer.</summary>
internal readonly record struct MinimapLayout(long Top, int Rows, int Capacity, double ThumbTop, double ThumbHeight, bool Surroundings, bool Sampled = false);

/// <summary>
/// The arithmetic of the minimap. A file that fits shows every line; a file up to <see cref="Proportional"/> times what fits is shown
/// in a window that slides with the scroll position (as in Sublime Text); a longer one shows the surroundings of the viewport, so
/// nothing but the lines of that window is ever read. A longer large file is sampled instead: strips of consecutive lines from the starts
/// of equal parts of the file, so the picture covers all of it and still reads only the lines of the strips.
/// </summary>
internal static class MinimapGeometry
{
    public const int Proportional = 4;

    /// <summary>
    /// The line count a picture is spread over. While the line index of a big file grows, the count changes with every notice of the index, and a sampled picture is built again for each
    /// new count: it reads a strip from dozens of places of the file, which on a 50 GiB file competes for the disk with the index itself and held the window for seconds at a time.
    /// An unfinished count is rounded down to its three leading bits, so the picture is built again when the count has grown by an eighth to a quarter (about 70 times for 800 million lines);
    /// a finished count is exact.
    /// </summary>
    public static long MapLines(long lines, bool indexComplete)
    {
        if (indexComplete || lines < 4096) return lines;
        int shift = System.Numerics.BitOperations.Log2((ulong)lines) - 2;
        return lines >> shift << shift;
    }

    public static MinimapLayout Compute(long lines, long first, long count, int height, int rowPx, bool sampled = false)
    {
        rowPx = Math.Max(1, rowPx);
        int capacity = Math.Max(1, height / rowPx);
        lines = Math.Max(0, lines);
        count = Math.Max(1, count);
        first = Math.Clamp(first, 0, Math.Max(0, lines - 1));
        if (sampled && lines > (long)capacity * Proportional)
        {
            double full = capacity * (double)rowPx;
            double pointer = Math.Clamp(count * full / lines, 2, full);
            return new MinimapLayout(0, capacity, capacity, Math.Clamp(first * full / lines, 0, full - pointer), pointer, false, true);
        }
        long top;
        bool surroundings = false;
        if (lines <= capacity) top = 0;
        else if (lines <= (long)capacity * Proportional)
        {
            double fraction = lines <= count ? 0 : Math.Clamp(first / (double)(lines - count), 0, 1);
            top = (long)Math.Round(fraction * (lines - capacity));
        }
        else
        {
            surroundings = true;
            top = Math.Clamp(first + count / 2 - capacity / 2, 0, lines - capacity);
        }
        int rows = (int)Math.Min(lines, capacity);
        double shown = rows * (double)rowPx;
        double thumbHeight = Math.Clamp(count * (double)rowPx, Math.Min(8, shown), Math.Max(1, shown));
        double thumbTop = Math.Clamp((first - top) * (double)rowPx, 0, Math.Max(0, shown - thumbHeight));
        return new MinimapLayout(top, rows, capacity, thumbTop, thumbHeight, surroundings);
    }

    /// <summary>The zero-based line drawn at <paramref name="y"/>; in a sampled picture the line at that share of the file.</summary>
    public static long LineAt(MinimapLayout layout, long lines, int rowPx, double y) => layout.Sampled
        ? Math.Clamp((long)(y / Math.Max(1, layout.Rows * (double)Math.Max(1, rowPx)) * lines), 0, Math.Max(0, lines - 1))
        : Math.Clamp(layout.Top + (long)Math.Floor(y / Math.Max(1, rowPx)), 0, Math.Max(0, lines - 1));

    /// <summary>The zero-based line painted in <paramref name="row"/>. A sampled picture cuts the file into one equal part per strip of <paramref name="stripRows"/> rows and shows the first lines of each part.</summary>
    public static long LineOfRow(MinimapLayout layout, long lines, int row, int stripRows)
    {
        if (!layout.Sampled) return layout.Top + row;
        stripRows = Math.Max(1, stripRows);
        long strips = (layout.Rows + stripRows - 1) / stripRows;
        return Math.Clamp(row / stripRows * lines / strips + row % stripRows, 0, Math.Max(0, lines - 1));
    }

    /// <summary>The first line on screen after the viewport rectangle moved <paramref name="dy"/> pixels away from where it was with <paramref name="pressFirst"/> on top.</summary>
    public static long FirstAfterDrag(MinimapLayout layout, long lines, long count, int rowPx, int height, long pressFirst, double dy)
    {
        rowPx = Math.Max(1, rowPx);
        count = Math.Max(1, count);
        double perPixel;
        if (layout.Sampled) perPixel = lines / Math.Max(1, layout.Rows * (double)rowPx);
        else if (lines <= layout.Capacity || layout.Surroundings) perPixel = 1.0 / rowPx;
        else
        {
            double travel = (layout.Capacity - count) * (double)rowPx;
            perPixel = travel > 0 ? (lines - count) / travel : (lines - count) / (double)Math.Max(1, height);
        }
        return Math.Clamp((long)Math.Round(pressFirst + dy * perPixel), 0, Math.Max(0, lines - count));
    }
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
    private static readonly TimeSpan Slice = TimeSpan.FromMilliseconds(8);
    private static readonly TimeSpan TextSettle = TimeSpan.FromMilliseconds(120);

    /// <param name="Lines">The line count a sampled picture was spread over; 0 for a picture of consecutive lines.</param>
    private readonly record struct BuildKey(long Top, int Rows, int Width, int Height, int RowPx, bool Dark, long Version, long Lines);

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
    private (string Grammar, Tokenizer Tokenizer)? _sampler;
    private LineState? _sampleState;
    private readonly List<Token> _sampleTokens = [];
    private byte[] _lane = [];
    private (int Height, long Lines, int Stamp) _laneKey;
    private int _marksStamp;
    private LogHeatmap? _heat;
    private (int Height, LogHeatmap? Heat) _heatKey;
    private byte[] _heatErrors = [];
    private byte[] _heatWarnings = [];
    private bool _dragging;
    private long _pressFirst;
    private double _pressY;

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
        _doc.Doc.Changed += OnDocumentChanged;
        _doc.Replaced += Invalidate;
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
        _doc.Doc.Changed -= OnDocumentChanged;
        _doc.Replaced -= Invalidate;
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

    private long MapLines() => MinimapGeometry.MapLines(_host.LineCount, _host.IndexComplete);

    /// <summary>How many times the picture was started from its first row, for tests: a growing index must not start it at every notice.</summary>
    internal int BuildsStarted { get; private set; }

    private (BuildKey Key, MinimapLayout Layout, int RowPx)? Wanted()
    {
        int width = PixelWidth, height = PixelHeight;
        if (width < 8 || height < 8 || !IsVisible) return null;
        int rowPx = Math.Max(2, (int)Math.Ceiling(height / (double)MaxRows));
        var (first, count) = _host.Visible;
        long lines = MapLines();
        var layout = MinimapGeometry.Compute(lines, first, count, height, rowPx, _host.Sampled);
        return (new BuildKey(layout.Top, layout.Rows, width, height, rowPx, Dark, _version, layout.Sampled ? lines : 0), layout, rowPx);
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
                for (int y = 0; y < key.Height; y++) new Span<uint>(pixels + (long)y * stride, key.Width).Clear();
            int columns = key.Width - LaneWidth(key.Width);
            int stripRows = PluginLimitsPlaceholders.MinimapSampleStripRows;
            var sampler = wanted.Layout.Sampled ? Sampler() : null;
            while (_nextRow < key.Rows)
            {
                long line = MinimapGeometry.LineOfRow(wanted.Layout, key.Lines, _nextRow, stripRows);
                string? text = _host.LineText(line);
                var tokens = wanted.Layout.Sampled ? SampleTokens(sampler, text, _nextRow % stripRows == 0) : _tokens.GetValueOrDefault(line);
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
                if (value != 0) pixels[(long)y * stride + column] = value;
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
        DrawLane(context, layout, width, height, plain);
    }

    /// <summary>The strip at the right edge covers the whole file: the marks and, when the picture shows only the surroundings, where they are in the file.</summary>
    private void DrawLane(DrawingContext context, MinimapLayout layout, int width, int height, Color plain)
    {
        long lines = MapLines();
        if (lines <= 0) return;
        int laneWidth = LaneWidth(width);
        double x = width - laneWidth;
        if (layout.Surroundings)
        {
            var (first, count) = _host.Visible;
            double y = first * (double)height / lines;
            context.FillRectangle(new ImmutableSolidColorBrush(plain, 0.45), new Rect(x, y, laneWidth, Math.Max(3, count * (double)height / lines)));
        }
        DrawHeat(context, x, laneWidth, height);
        if (_marks.Length == 0 && _filterMarks.Length == 0) return;
        if (_laneKey != (height, lines, _marksStamp))
        {
            _laneKey = (height, lines, _marksStamp);
            _lane = new byte[height];
            foreach (var mark in _marks.Concat(_filterMarks))
            {
                if (mark.Line < 0 || mark.Line >= lines) continue;
                int row = (int)Math.Min(height - 1, mark.Line * (long)height / lines);
                if ((byte)mark.Kind > _lane[row]) _lane[row] = (byte)mark.Kind;
            }
        }
        for (int y = 0; y < height;)
        {
            byte kind = _lane[y];
            int end = y + 1;
            while (end < height && _lane[end] == kind) end++;
            if (kind != 0) context.FillRectangle(MarkBrush((MinimapMarkKind)kind), new Rect(x, Math.Max(0, y - 1), laneWidth, end - y + 2));
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
        long lines = MapLines();
        var layout = wanted.Layout;
        if (y < layout.ThumbTop || y > layout.ThumbTop + layout.ThumbHeight)
        {
            first = Math.Clamp(MinimapGeometry.LineAt(layout, lines, wanted.RowPx, y) - count / 2, 0, Math.Max(0, lines - count));
            _host.ScrollToLine(first);
        }
        _pressFirst = first;
        _pressY = y;
        _dragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || Wanted() is not { } wanted) return;
        var (_, count) = _host.Visible;
        _host.ScrollToLine(MinimapGeometry.FirstAfterDrag(wanted.Layout, MapLines(), count, wanted.RowPx, wanted.Key.Height, _pressFirst, e.GetPosition(this).Y * Scale - _pressY));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
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
