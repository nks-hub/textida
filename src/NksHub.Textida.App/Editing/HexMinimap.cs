using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex;

namespace NksHub.Textida.App.Editing;

/// <summary>What a mark in the lane of the hex minimap stands for.</summary>
internal enum HexMarkKind : byte { Match, Bookmark, Change }

/// <summary>
/// The map of the whole file beside the hex grid: one cell per stretch of the file, coloured by entropy or by the kind of byte, a narrow lane for the matches of the
/// last search, the bookmarks and the changes since the last save, and a rectangle for the part on screen. A click or a drag scrolls the grid. The map is built in the
/// background (see <see cref="ByteMapBuilder"/>): after the first pass it is a sample of the file, and it is redrawn as the passes refine it.
/// </summary>
internal sealed class HexMinimap : Control
{
    public const double MapWidth = 64;
    private const double Lane = 10;
    private const double CellPx = 4;
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);

    private readonly HexView _owner;
    private readonly DispatcherTimer _timer = new() { Interval = Settle };
    private CancellationTokenSource? _build;
    private ByteMap? _map;
    private int _generation;
    private long _builtRevision = -1;
    private int _builtBuckets;
    private ByteMapMode _mode = ByteMapMode.Entropy;
    private bool _dragging;
    private long _thumbFirst = -1, _thumbCount = -1;
    private IReadOnlyList<long> _matches = [];

    public HexMinimap(HexView owner)
    {
        _owner = owner;
        Width = MapWidth;
        ClipToBounds = true;
        Focusable = true;
        _timer.Tick += (_, _) => { _timer.Stop(); Rebuild(); };
        AutomationProperties.SetName(this, Loc.Get(K.HexMapA11y));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new HexMinimapAutomationPeer(this);

    /// <summary>Where the grid is in the file, in percent: the value a screen reader reads for the map.</summary>
    internal double Percent => Math.Round(_owner.FirstByte * 100.0 / Math.Max(1, _owner.Document.ByteLength));

    /// <summary>Scrolls the grid to a share of the file, 0 to 100 percent.</summary>
    internal void ScrollToPercent(double percent) =>
        _owner.ScrollToOffset((long)(Math.Clamp(percent, 0, 100) / 100 * _owner.Document.ByteLength));

    /// <summary>What the map shows, in words: what the cells mean, the part on screen and how many matches, bookmarks and changed places it marks.</summary>
    internal string Describe()
    {
        long length = _owner.Document.ByteLength, first = _owner.FirstByte, last = Math.Min(length, first + _owner.VisibleBytes);
        return Loc.Get(K.HexMapHelp, ("mode", Loc.Get(_mode == ByteMapMode.Entropy ? K.HexMapEntropy : K.HexMapClass)), ("start", _owner.OffsetText(first)),
            ("end", _owner.OffsetText(Math.Max(first, last - 1))), ("size", length), ("matches", _matches.Count), ("bookmarks", _owner.Marks.Items.Count), ("changes", _owner.ChangedRanges().Count));
    }

    /// <summary>The keyboard moves the caret, and the grid follows: a screen up or down, a tenth of the file, the start or the end. Esc and Enter give the keyboard back to the grid.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        long caret = _owner.Caret, screen = Math.Max(1, _owner.VisibleBytes), tenth = Math.Max(screen, _owner.Document.ByteLength / 10);
        long? target = e.Key switch
        {
            Key.Up => caret - screen,
            Key.Down => caret + screen,
            Key.PageUp => caret - tenth,
            Key.PageDown => caret + tenth,
            Key.Home => 0,
            Key.End => _owner.LastIndex,
            _ => null,
        };
        if (target is { } to) _owner.MoveTo(Math.Clamp(to, 0, _owner.LastIndex));
        else if (e.Key is Key.Escape or Key.Enter) _owner.Focus();
        else
        {
            base.OnKeyDown(e);
            return;
        }
        e.Handled = true;
    }

    /// <summary>The map as it is now; null before the first pass.</summary>
    internal ByteMap? Map => _map;

    public ByteMapMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            InvalidateVisual();
        }
    }

    /// <summary>Start offsets of the matches of the last search, marked in the lane.</summary>
    public IReadOnlyList<long> Matches
    {
        get => _matches;
        set
        {
            _matches = value;
            InvalidateVisual();
        }
    }

    /// <summary>Raised on the UI thread each time a pass of the build published a better map.</summary>
    public event Action? MapChanged;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Loc.Changed += ApplyTexts;
        _owner.DocumentChanged += OnDocumentChanged;
        BuildOrSchedule();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Loc.Changed -= ApplyTexts;
        _owner.DocumentChanged -= OnDocumentChanged;
        _timer.Stop();
        CancelBuild();
    }

    private void ApplyTexts() => AutomationProperties.SetName(this, Loc.Get(K.HexMapA11y));

    private void OnDocumentChanged()
    {
        if (_matches.Count > 0) _matches = []; // the offsets of a search belong to the revision it ran on
        InvalidateVisual(); // the lane shows the changes since the save at once; the map itself follows when the build is done
        Schedule();
    }

    /// <summary>The grid scrolled or got another size: the rectangle of the part on screen moves. Draws again only when it did.</summary>
    internal void NoteView()
    {
        if (_owner.FirstByte != _thumbFirst || _owner.VisibleBytes != _thumbCount) InvalidateVisual();
        (ControlAutomationPeer.FromElement(this) as HexMinimapAutomationPeer)?.Moved();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (Buckets() != _builtBuckets) BuildOrSchedule();
    }

    private int Buckets() => (int)Math.Max(16, Math.Floor(Bounds.Height / CellPx));

    /// <summary>The first map of a freshly shown view starts at once; the settle delay is only there so that typing or a resize drag does not restart the build at every step.</summary>
    private void BuildOrSchedule()
    {
        if (_build is null) Rebuild();
        else Schedule();
    }

    private void Schedule()
    {
        if (!IsVisible) return;
        _timer.Stop();
        _timer.Start();
    }

    private void CancelBuild()
    {
        _build?.Cancel();
        _build = null;
        _generation++;
    }

    /// <summary>Builds the map of the current revision again, in the background; a build that is still running is dropped.</summary>
    public void Rebuild()
    {
        if (!IsVisible || Bounds.Height < 20) return;
        _timer.Stop();
        CancelBuild();
        var doc = _owner.Document;
        if (doc.ByteLength == 0)
        {
            _map = null;
            _builtRevision = doc.Revision;
            InvalidateVisual();
            return;
        }
        DocumentSnapshot snapshot;
        try { snapshot = doc.GetSnapshot(); }
        catch (ObjectDisposedException) { return; }
        int buckets = Buckets();
        _builtRevision = snapshot.Revision;
        _builtBuckets = buckets;
        int generation = _generation;
        var cts = _build = new CancellationTokenSource();
        _ = Task.Run(() =>
        {
            try
            {
                ByteMapBuilder.Build(snapshot, buckets, map => Dispatcher.UIThread.Post(() =>
                {
                    if (generation != _generation) return;
                    _map = map;
                    InvalidateVisual();
                    MapChanged?.Invoke();
                }), cts.Token, Math.Max(1, Environment.ProcessorCount / 2));
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException or InvalidOperationException or UnauthorizedAccessException)
            {
                // the file moved under the snapshot or the view went away; the next change builds a fresh map
            }
        });
    }

    /// <summary>A build is running or the map is still a sample.</summary>
    internal bool IsBuilding => _build is not null && _map is not { IsExact: true };

    // ---- drawing ----

    private IBrush Brush(string key, IBrush fallback) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;

    private static Color Blend(Color from, Color to, double t) =>
        Color.FromRgb((byte)(from.R + (to.R - from.R) * t), (byte)(from.G + (to.G - from.G) * t), (byte)(from.B + (to.B - from.B) * t));

    private Color ColorOf(string key, Color fallback) => Brush(key, new SolidColorBrush(fallback)) is ISolidColorBrush s ? s.Color : fallback;

    public override void Render(DrawingContext context)
    {
        double width = Bounds.Width, height = Bounds.Height;
        var back = ColorOf("Bg1", Colors.WhiteSmoke);
        context.FillRectangle(new SolidColorBrush(back), new Rect(0, 0, width, height));
        long length = _owner.Document.ByteLength;
        if (length == 0 || height < 4) return;
        double mapWidth = width - Lane;
        if (_map is { } map && map.Buckets > 0)
        {
            var accent = ColorOf("Accent", Colors.Teal);
            var muted = ColorOf("FgMuted", Colors.Gray);
            var danger = ColorOf("Danger", Colors.Red);
            var warn = ColorOf("Highlight", Colors.Gold);
            for (int i = 0; i < map.Buckets; i++)
            {
                double y0 = Math.Floor(i * height / map.Buckets), y1 = Math.Floor((i + 1) * height / map.Buckets);
                if (y1 <= y0) y1 = y0 + 1;
                Color cell;
                if (map.Read[i] == 0) cell = Blend(back, muted, 0.08);
                else if (_mode == ByteMapMode.Entropy) cell = Blend(back, accent, 0.08 + 0.92 * map.Entropy[i] / 255.0);
                else cell = map.Classes[i] switch
                {
                    ByteClass.Zero => Blend(back, muted, 0.12),
                    ByteClass.Ones => danger,
                    ByteClass.Text => accent,
                    ByteClass.Control => warn,
                    _ => Blend(back, muted, 0.6),
                };
                context.FillRectangle(new SolidColorBrush(cell), new Rect(0, y0, mapWidth, y1 - y0));
            }
            DrawLane(context, map, length, mapWidth);
        }
        DrawThumb(context, length, mapWidth);
    }

    private void DrawLane(DrawingContext context, ByteMap map, long length, double mapWidth)
    {
        double height = Bounds.Height;
        double Y(long offset) => Math.Min(height - 2, Math.Floor((double)map.BucketOf(offset) * height / map.Buckets));
        var accent = Brush("Accent", Brushes.Teal);
        var highlight = Brush("Highlight", Brushes.Gold);
        double x = mapWidth + 2, w = Lane - 4;
        foreach (var r in _owner.ChangedRanges()) context.FillRectangle(accent, new Rect(x + w / 2, Y(r.Start), w / 2, 2));
        var fore = Brush("Fg", Brushes.Black);
        foreach (var b in _owner.Marks.Items)
            context.FillRectangle(HexView.HighContrast ? fore : b.Color is { } c ? new SolidColorBrush(Color.Parse(c)) : highlight, new Rect(x, Y(b.Range.Start), w / 2, 2));
        foreach (long match in _matches) context.FillRectangle(Brush("Danger", Brushes.Red), new Rect(x, Y(match), w, 1));
    }

    private void DrawThumb(DrawingContext context, long length, double mapWidth)
    {
        double height = Bounds.Height;
        long first = _owner.FirstByte, count = _owner.VisibleBytes;
        (_thumbFirst, _thumbCount) = (first, count);
        double top = Math.Clamp(first * height / length, 0, height - 3);
        double thumb = Math.Clamp(count * height / length, 6, height);
        var accent = ColorOf("Accent", Colors.Teal);
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B)), new Rect(0, top, mapWidth, thumb));
        context.DrawRectangle(new Pen(new SolidColorBrush(accent), 1.5), new Rect(0.75, top + 0.75, mapWidth - 1.5, Math.Max(1, thumb - 1.5)));
    }

    // ---- scrolling ----

    private long OffsetAt(double y)
    {
        long length = _owner.Document.ByteLength;
        return Math.Clamp((long)Math.Round(Math.Clamp(y, 0, Bounds.Height) / Math.Max(1, Bounds.Height) * length), 0, Math.Max(0, length - 1));
    }

    /// <summary>Scrolls the grid so that the file position at <paramref name="y"/> pixels from the top of the map is in the middle of it.</summary>
    internal void ScrollAt(double y) => _owner.ScrollToOffset(OffsetAt(y) - _owner.VisibleBytes / 2);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Pointer.Capture(this);
        _dragging = true;
        _owner.Focus(); // a click scrolls the grid and leaves the keyboard where it was; the map takes it only from its command
        ScrollAt(e.GetPosition(this).Y);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) ScrollAt(e.GetPosition(this).Y);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }
}
