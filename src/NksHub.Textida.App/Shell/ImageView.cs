using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Markdown;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The picture viewer of a tab (P3.6.11): a PNG, JPEG, GIF, BMP, WebP or ICO file drawn on a checkerboard that shows through transparent pixels, fitted to the tab or at a zoom of the user's, moved with the
/// mouse or the arrow keys. View only. The bytes come from the document, so a hex edit made meanwhile is what is drawn. The file is read and decoded off the UI thread under the byte and pixel limits;
/// one that is too large, damaged or not a picture shows a message instead and the switcher bar offers text and hex.
/// </summary>
internal sealed class ImageView : ScrollViewer, IDisposable
{
    /// <summary>What was decoded: the size in pixels the decoder reports, the format found by the first bytes, and the size of the file.</summary>
    public readonly record struct Picture(int Width, int Height, string Format, long FileBytes);

    /// <summary>Why there is no picture: the key of a text and its values, so the message follows the interface language.</summary>
    private sealed record Issue(string Key, params (string Name, object Value)[] Args)
    {
        public string Text => Loc.Get(Key, Args);
    }

    private sealed record Decoded(Bitmap? Bitmap, Picture? Picture, Issue? Problem);

    private static readonly IBrush Checker = BuildChecker();

    private readonly OpenDocument _doc;
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly Border _paper = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private readonly Grid _stage = new();
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24), MaxWidth = 640 };
    private readonly CancellationTokenSource _closing = new();
    private Bitmap? _bitmap;
    private Issue? _problem;
    private bool _fit = true;
    private double _zoom = 1;
    private Point? _dragStart;
    private Vector _dragOffset;

    public ImageView(OpenDocument doc)
    {
        _doc = doc;
        Focusable = true;
        Classes.Add("imageview");
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        _paper.Background = Checker;
        _paper.Child = _image;
        _stage.Children.Add(_paper);
        _stage.Children.Add(_message);
        Content = _stage;
        HelpContext.Set(this, "view.image");
        AddHandler(PointerWheelChangedEvent, OnWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        SizeChanged += (_, _) =>
        {
            _stage.MinWidth = Math.Max(0, Area.Width);
            _stage.MinHeight = Math.Max(0, Area.Height);
            if (_fit) Refit();
        };
        SetMessage(Loc.Get(K.ImageLoading));
        _ = LoadAsync();
    }

    /// <summary>The scrolled view takes the look of every scroll viewer.</summary>
    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    /// <summary>Raised when what the status bar and the screen reader say about the picture changed: it was decoded, or the zoom moved.</summary>
    public event Action? StateChanged;

    /// <summary>Raised with a sentence a screen reader should read now: the picture that was loaded, the new zoom.</summary>
    public event Action<string>? Announced;

    /// <summary>The decoded picture; null while it loads and when it cannot be shown.</summary>
    public Picture? Info { get; private set; }

    /// <summary>The message that stands in the place of a picture that cannot be shown; null otherwise.</summary>
    public string? Problem => _problem?.Text;

    public bool Loading => Info is null && _problem is null;

    /// <summary>The zoom as a percentage of the real size.</summary>
    public int ZoomPercent => (int)Math.Round(_zoom * 100);

    /// <summary>The picture is fitted to the tab and follows its size.</summary>
    public bool IsFit => _fit;

    /// <summary>The checkerboard under the picture.</summary>
    internal Border Paper => _paper;

    internal Image Surface => _image;

    // ---- loading ----

    private static IBrush BuildChecker()
    {
        var drawing = new DrawingGroup
        {
            Children =
            {
                new GeometryDrawing { Brush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)), Geometry = new RectangleGeometry(new Rect(0, 0, 2, 2)) },
                new GeometryDrawing
                {
                    Brush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                    Geometry = new GeometryGroup { Children = { new RectangleGeometry(new Rect(0, 0, 1, 1)), new RectangleGeometry(new Rect(1, 1, 1, 1)) } },
                },
            },
        };
        return new DrawingBrush(drawing) { TileMode = TileMode.Tile, DestinationRect = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute) };
    }

    private async Task LoadAsync()
    {
        var snapshot = _doc.Doc.GetSnapshot();
        var loaded = await Task.Run(() => Decode(snapshot), CancellationToken.None);
        if (_closing.IsCancellationRequested)
        {
            loaded.Bitmap?.Dispose();
            return;
        }
        if (loaded.Bitmap is null || loaded.Picture is not { } picture)
        {
            _problem = loaded.Problem ?? new Issue(K.ImageBroken);
            SetMessage(_problem.Text);
            StateChanged?.Invoke();
            Announced?.Invoke(_problem.Text);
            return;
        }
        _bitmap = loaded.Bitmap;
        Info = picture;
        _image.Source = _bitmap;
        _message.IsVisible = false;
        _paper.IsVisible = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        _fit = true;
        Refit();
        UpdateName();
        StateChanged?.Invoke();
        Announced?.Invoke(Describe());
    }

    /// <summary>Reads and decodes the picture of a snapshot; the limits are checked before the pixels are made, so a small file that claims a huge picture is refused without memory.</summary>
    private static Decoded Decode(DocumentSnapshot snapshot)
    {
        long length = snapshot.ByteLength;
        if (length > PluginLimitsPlaceholders.ImageViewMaxBytes)
            return new(null, null, new Issue(K.ImageTooLarge, ("size", OpenDocument.FormatSize(length)), ("limit", OpenDocument.FormatSize(PluginLimitsPlaceholders.ImageViewMaxBytes))));
        try
        {
            var bytes = snapshot.ReadBytes(new ByteRange(0, length));
            if (RasterImage.FormatName(bytes) is not { } format || RasterImage.Size(bytes) is not { Width: > 0, Height: > 0 } size) return new(null, null, new Issue(K.ImageBroken));
            if ((long)size.Width * size.Height > PluginLimitsPlaceholders.PreviewImageMaxPixels)
                return new(null, null, new Issue(K.ImageTooManyPixels, ("width", size.Width), ("height", size.Height), ("limit", PluginLimitsPlaceholders.PreviewImageMaxPixels / 1_000_000)));
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new Bitmap(stream);
            return new(bitmap, new Picture(bitmap.PixelSize.Width, bitmap.PixelSize.Height, format, length), null);
        }
        catch (Exception) // a damaged file, a format the decoder lacks, a file that changed under the read, no memory for the pixels: the same message and the way out through text or hex
        {
            return new(null, null, new Issue(K.ImageBroken));
        }
    }

    private void SetMessage(string text)
    {
        _message.Text = text;
        _message.IsVisible = true;
        _paper.IsVisible = false;
        AutomationProperties.SetName(this, text);
        AutomationProperties.SetName(_message, text);
    }

    public void Dispose()
    {
        _closing.Cancel();
        _image.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }

    // ---- text for the status bar and the screen reader ----

    /// <summary>The picture in a sentence: format, size in pixels, size of the file and zoom; the name of the view for a screen reader.</summary>
    public string Describe() => Info is { } p
        ? Loc.Get(K.ImageA11y, ("name", _doc.Title), ("format", p.Format), ("width", p.Width), ("height", p.Height), ("size", OpenDocument.FormatSize(p.FileBytes)), ("zoom", ZoomPercent))
        : _problem?.Text ?? Loc.Get(K.ImageLoading);

    /// <summary>The status bar line: size in pixels, format, size of the file and zoom; the message when there is no picture.</summary>
    public string StatusText => Info is { } p
        ? Loc.Get(K.ImageStatus, ("width", p.Width), ("height", p.Height), ("format", p.Format), ("size", OpenDocument.FormatSize(p.FileBytes)), ("zoom", ZoomPercent))
        : _problem?.Text ?? Loc.Get(K.ImageLoading);

    /// <summary>The texts follow the interface language.</summary>
    public void Retranslate()
    {
        if (Info is not null) UpdateName();
        else SetMessage(_problem?.Text ?? Loc.Get(K.ImageLoading));
    }

    private void UpdateName()
    {
        AutomationProperties.SetName(this, Describe());
        AutomationProperties.SetName(_image, _doc.Title);
    }

    // ---- zoom and position ----

    /// <summary>The size of the area the picture is shown in.</summary>
    private Size Area => Viewport.Width > 0 && Viewport.Height > 0 ? Viewport : Bounds.Size;

    private void SetZoom(double zoom)
    {
        if (Info is not { } p) return;
        _zoom = Math.Clamp(zoom, PluginLimitsPlaceholders.ImageViewMinZoom, PluginLimitsPlaceholders.ImageViewMaxZoom);
        _paper.Width = Math.Max(1, p.Width * _zoom);
        _paper.Height = Math.Max(1, p.Height * _zoom);
        RenderOptions.SetBitmapInterpolationMode(_image, _zoom >= 2 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
        UpdateName();
        StateChanged?.Invoke();
    }

    /// <summary>Fits the picture to the tab; a picture smaller than the tab stays at its real size, because making a small one large is a zoom the user asks for.</summary>
    private void Refit()
    {
        if (Info is not { } p) return;
        var area = Area;
        SetZoom(area.Width <= 0 || area.Height <= 0 ? 1 : Math.Min(1, Math.Min(area.Width / p.Width, area.Height / p.Height)));
    }

    /// <summary>Fits the picture to the tab again and keeps it fitted when the tab changes its size.</summary>
    public void Fit()
    {
        _fit = true;
        Refit();
        Offset = default;
        Announced?.Invoke(Loc.Get(K.ImageZoomed, ("zoom", ZoomPercent)));
    }

    /// <summary>Shows the picture at its real size.</summary>
    public void ActualSize() => ZoomTo(1, null);

    /// <summary>Zooms in (positive) or out by steps of a quarter; <paramref name="around"/> is the point of the tab that stays where it is, the middle by default.</summary>
    public void ZoomBy(int steps, Point? around = null) => ZoomTo(_zoom * Math.Pow(PluginLimitsPlaceholders.ImageViewZoomStep, steps), around);

    private void ZoomTo(double zoom, Point? around)
    {
        if (Info is null) return;
        var anchor = around ?? new Point(Area.Width / 2, Area.Height / 2);
        var before = new Vector((Offset.X + anchor.X) / _zoom, (Offset.Y + anchor.Y) / _zoom);
        _fit = false;
        SetZoom(zoom);
        Announced?.Invoke(Loc.Get(K.ImageZoomed, ("zoom", ZoomPercent)));
        double scale = _zoom;
        Dispatcher.UIThread.Post(() => Offset = new Vector(Math.Max(0, before.X * scale - anchor.X), Math.Max(0, before.Y * scale - anchor.Y)), DispatcherPriority.Background);
    }

    // ---- keyboard and mouse ----

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift)) return;
        switch (e.Key)
        {
            case Key.Add or Key.OemPlus: ZoomBy(1); break;
            case Key.Subtract or Key.OemMinus: ZoomBy(-1); break;
            case Key.D0 or Key.NumPad0 when e.KeyModifiers == KeyModifiers.None: Fit(); break;
            case Key.D1 or Key.NumPad1 when e.KeyModifiers == KeyModifiers.None: ActualSize(); break;
            case Key.Up: LineUp(); break;
            case Key.Down: LineDown(); break;
            case Key.Left: LineLeft(); break;
            case Key.Right: LineRight(); break;
            case Key.PageUp: PageUp(); break;
            case Key.PageDown: PageDown(); break;
            case Key.Home: ScrollToHome(); break;
            case Key.End: ScrollToEnd(); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>Ctrl+wheel zooms around the pointer; the wheel alone moves the picture like in any scrolled view.</summary>
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers != Shortcuts.Primary || e.Delta.Y == 0 || Info is null) return;
        ZoomBy(e.Delta.Y > 0 ? 1 : -1, e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Info is null) return;
        _dragStart = e.GetPosition(this);
        _dragOffset = Offset;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragStart is not { } start) return;
        var now = e.GetPosition(this);
        Offset = new Vector(Math.Max(0, _dragOffset.X - (now.X - start.X)), Math.Max(0, _dragOffset.Y - (now.Y - start.Y)));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragStart is null) return;
        _dragStart = null;
        e.Pointer.Capture(null);
    }
}
