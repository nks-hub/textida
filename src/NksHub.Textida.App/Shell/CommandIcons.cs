using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
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

    static IconView()
    {
        AffectsRender<IconView>(TextElement.ForegroundProperty);
    }

    public IconView(IconShape shape, double size = CommandIcons.Size)
    {
        Shape = shape;
        _size = size;
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

    /// <summary>The opacity of the marked parts now.</summary>
    public double AccentOpacity => IsEffectivelyEnabled ? 1 : DisabledAccentOpacity;

    protected override Size MeasureOverride(Size availableSize) => new(_size, _size);

    public override void Render(DrawingContext context)
    {
        double scale = _size / Shape.Size;
        IBrush line = LineBrush, accent = AccentBrush;
        using var _ = context.PushTransform(Matrix.CreateScale(scale, scale));
        foreach (var (geometry, paint) in _paths)
        {
            bool marked = paint is IconPaint.AccentLine or IconPaint.AccentFill;
            bool filled = paint is IconPaint.Fill or IconPaint.AccentFill;
            if (marked && AccentOpacity < 1)
            {
                using (context.PushOpacity(AccentOpacity)) Draw(context, geometry, accent, filled);
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
