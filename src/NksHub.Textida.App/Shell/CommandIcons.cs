using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Styling;
using NksHub.Textida.App.Services;
using NksHub.Textida.Workbench.Icons;

namespace NksHub.Textida.App.Shell;

/// <summary>Makes the controls that draw the icons of commands.</summary>
internal static class CommandIcons
{
    /// <summary>Side of an icon in menus, the palette and the toolbar, in device-independent pixels.</summary>
    public const double Size = 16;

    /// <summary>The control that draws the icon. It is decorative: it takes no focus and no clicks, and the screen reader does not see it (the item or button next to it carries the name).</summary>
    public static IconView Create(IconShape shape, double size = Size) => new(shape, size);
}

/// <summary>The kinds of command a coloured icon tells apart by its colour.</summary>
internal enum IconKind { File, Folder, History, Search, Git, Danger, Run }

/// <summary>
/// The colours of the coloured icons of the toolbar, by the kind of command. Each kind has a colour for the light and one for the dark theme, chosen to keep at least 3 : 1 against every
/// surface a toolbar button sits on (the toolbar, a hovered and a pressed button); high contrast never uses them. An icon of no kind keeps the colour of the text.
/// </summary>
internal static class IconTints
{
    public static IReadOnlyDictionary<IconKind, (Color Light, Color Dark)> Colors { get; } = new Dictionary<IconKind, (Color, Color)>
    {
        [IconKind.File] = (Color.Parse("#1F63C6"), Color.Parse("#6CA8F4")),
        [IconKind.Folder] = (Color.Parse("#9A5B00"), Color.Parse("#E6B04C")),
        [IconKind.History] = (Color.Parse("#7A3EC2"), Color.Parse("#B79AF2")),
        [IconKind.Search] = (Color.Parse("#00727F"), Color.Parse("#45C6D6")),
        [IconKind.Git] = (Color.Parse("#B84A0B"), Color.Parse("#F28B44")),
        [IconKind.Danger] = (Color.Parse("#C0262D"), Color.Parse("#F47A7A")),
        [IconKind.Run] = (Color.Parse("#2B7A31"), Color.Parse("#68C871")),
    };

    private static readonly Dictionary<string, IconKind> s_kinds = Kinds();

    private static Dictionary<string, IconKind> Kinds()
    {
        var kinds = new Dictionary<string, IconKind>(StringComparer.Ordinal);
        void Set(IconKind kind, params string[] names) { foreach (var name in names) kinds[name] = kind; }
        Set(IconKind.File, "file-new", "file-text", "save", "save-all", "save-as", "window-new", "download", "upload", "reload");
        Set(IconKind.Folder, "folder", "folder-open", "folder-add");
        Set(IconKind.History, "undo", "redo", "history", "repeat");
        Set(IconKind.Search, "search", "search-files", "replace", "filter", "regex", "goto", "zoom-in", "zoom-out", "zoom-reset");
        Set(IconKind.Danger, "trash", "close", "stop", "quit", "record");
        Set(IconKind.Run, "play", "check", "check-circle", "checklist");
        foreach (var name in BuiltinIcons.All.Keys.Where(n => n.StartsWith("git-", StringComparison.Ordinal))) kinds[name] = IconKind.Git;
        return kinds;
    }

    /// <summary>The kind of the built-in icon of that name; null for an icon of no kind.</summary>
    public static IconKind? KindOf(string? name) => name is not null && s_kinds.TryGetValue(name, out var kind) ? kind : null;

    /// <summary>The colour of the icon in the light or the dark theme; null when it has no kind.</summary>
    public static Color? ColorOf(IconShape shape, bool dark) => KindOf(IconSkin.NameOf(shape)) is { } kind ? dark ? Colors[kind].Dark : Colors[kind].Light : null;
}

/// <summary>
/// Draws an <see cref="IconShape"/>. Lines and fills take the foreground of the control the icon sits in (a menu item, a button, a row of the palette), so a hovered, chosen or disabled host
/// changes the icon as it changes its text, in the light, the dark and the high-contrast themes alike. The marked parts take the accent of the theme. The shapes are read by
/// <see cref="Geometry.Parse(string)"/> once, without an SVG reader or reflection, and drawn with the pen of the set: 1.25 units wide, round caps and joins.
/// </summary>
internal sealed class IconView : Control
{
    /// <summary>Width of a line in icon units.</summary>
    public const double StrokeWidth = 1.25;

    /// <summary>How much of its colour a marked part keeps while the icon is disabled; the lines are dimmed by the foreground of the host.</summary>
    public const double DisabledAccentOpacity = 0.4;

    // an icon is parsed once; a geometry may be shared by any number of controls
    private static readonly Dictionary<string, Geometry?> s_geometries = new(StringComparer.Ordinal);

    private readonly (Geometry Geometry, IconPaint Paint)[] _paths;
    private readonly double _size;
    private readonly SkinImage? _skin;

    static IconView()
    {
        AffectsRender<IconView>(TextElement.ForegroundProperty);
    }

    public IconView(IconShape shape, double size = CommandIcons.Size)
    {
        Shape = shape;
        _size = size;
        _skin = IconSkin.Current is { } skin && IconSkin.NameOf(shape) is { } name ? skin.Find(name) : null;
        _paths = [.. shape.Paths.Select(p => (Geometry: GeometryOf(p.Data), p.Paint)).Where(p => p.Geometry is not null).Select(p => (p.Geometry!, p.Paint))];
        Width = size;
        Height = size;
        IsHitTestVisible = false;
        Focusable = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        // the accent is looked up when the icon is drawn, so it is drawn again when the theme or its colours change
        ResourcesChanged += (_, _) => InvalidateVisual();
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>The drawing.</summary>
    public IconShape Shape { get; }

    /// <summary>How many paths are drawn; a path whose data cannot be parsed is left out.</summary>
    public int PathCount => _paths.Length;

    /// <summary>The brush of lines and plain fills: the foreground of the host.</summary>
    public IBrush LineBrush => TextElement.GetForeground(this) ?? ThemeColors.Text;

    /// <summary>The brush of the marked parts: the accent of the theme in use.</summary>
    public IBrush AccentBrush => this.TryFindResource("Accent", ActualThemeVariant, out var value) && value is IBrush brush ? brush : ThemeColors.Accent;

    /// <summary>True when the lines take the colour of the kind of command (<see cref="IconTints"/>) instead of the foreground of the host; high contrast keeps the foreground.</summary>
    public bool Colored
    {
        get => _colored;
        set
        {
            _colored = value;
            InvalidateVisual();
        }
    }

    private bool _colored;

    /// <summary>The brush of the lines of a coloured icon in the theme in use, or null: the lines take <see cref="LineBrush"/>.</summary>
    public IBrush? TintBrush => Colored && Application.Current is not App { Contrast.IsOn: true } && IconTints.ColorOf(Shape, ActualThemeVariant == ThemeVariant.Dark) is { } color ? new SolidColorBrush(color) : null;

    /// <summary>The opacity of the marked parts now.</summary>
    public double AccentOpacity => IsEffectivelyEnabled ? 1 : DisabledAccentOpacity;

    protected override Size MeasureOverride(Size availableSize) => new(_size, _size);

    /// <summary>The bitmap of the skin in use for this icon, or null: the vector drawing is used.</summary>
    public SkinImage? SkinBitmap => _skin;

    public override void Render(DrawingContext context)
    {
        if (_skin is { } image)
        {
            // the picture keeps its own colours; a disabled host dims it
            using var _dim = context.PushOpacity(IsEffectivelyEnabled ? 1 : DisabledAccentOpacity);
            context.DrawImage(image.Bitmap, new Rect(image.Source.X, image.Source.Y, image.Source.Width, image.Source.Height), new Rect(0, 0, _size, _size));
            return;
        }
        double scale = _size / Shape.Size;
        var tint = TintBrush;
        IBrush line = tint ?? LineBrush, accent = AccentBrush;
        using var _ = context.PushTransform(Matrix.CreateScale(scale, scale));
        foreach (var (geometry, paint) in _paths)
        {
            bool marked = paint is IconPaint.AccentLine or IconPaint.AccentFill;
            bool filled = paint is IconPaint.Fill or IconPaint.AccentFill;
            // a coloured line does not follow the foreground of a disabled host, so it is dimmed like the marked parts
            if ((marked || tint is not null) && AccentOpacity < 1)
            {
                using (context.PushOpacity(AccentOpacity)) Draw(context, geometry, marked ? accent : line, filled);
            }
            else Draw(context, geometry, marked ? accent : line, filled);
        }
    }

    private static void Draw(DrawingContext context, Geometry geometry, IBrush brush, bool filled)
    {
        if (filled) context.DrawGeometry(brush, null, geometry);
        else context.DrawGeometry(null, new Pen(brush, StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
    }

    private static Geometry? GeometryOf(string data)
    {
        if (s_geometries.TryGetValue(data, out var known)) return known;
        Geometry? geometry;
        // the parser of Avalonia reports bad data with more than one kind of exception; whatever it throws, an icon that cannot be read is an icon left out, never a menu that cannot open
        try { geometry = Geometry.Parse(data); }
        catch (Exception e) when (e is FormatException or ArgumentException or InvalidOperationException or InvalidDataException or IndexOutOfRangeException) { geometry = null; }
        return s_geometries[data] = geometry;
    }
}
