using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using NksHub.Textida.App.Profile;

namespace NksHub.Textida.App.Shell;

/// <summary>The panels that stand beside or below the text: the open documents, the files of a folder and the panel a plug-in fills.</summary>
internal enum SidePanel { Documents, Files, Plugin }

/// <summary>The sides of the window a panel is docked to.</summary>
internal enum PanelDock { Left, Right, Bottom }

/// <summary>
/// Where each side panel is docked and how large each side is. A side shows one panel at a time (the last one opened); the size belongs to the side, not to the panel.
/// This is the part of the layout that is kept between runs: which panels are open is not.
/// </summary>
internal sealed class PanelLayout
{
    public const double DefaultWidth = 220;
    public const double DefaultHeight = 200;
    public const double MinWidth = 140;
    public const double MinHeight = 80;
    /// <summary>The largest a side panel may be, however wide the window is; a panel never takes more than the share of the window in <see cref="MaxShare"/>.</summary>
    public const double MaxWidth = 640;
    public const double MaxHeight = 480;
    public const double MaxShare = 0.5;

    private readonly Dictionary<SidePanel, PanelDock> _docks = new() { [SidePanel.Documents] = PanelDock.Left, [SidePanel.Files] = PanelDock.Left, [SidePanel.Plugin] = PanelDock.Left };
    private readonly Dictionary<PanelDock, double> _sizes = new() { [PanelDock.Left] = DefaultWidth, [PanelDock.Right] = DefaultWidth, [PanelDock.Bottom] = DefaultHeight };

    public static IReadOnlyList<SidePanel> Panels { get; } = [SidePanel.Documents, SidePanel.Files, SidePanel.Plugin];

    public static IReadOnlyList<PanelDock> Docks { get; } = [PanelDock.Left, PanelDock.Right, PanelDock.Bottom];

    public PanelDock DockOf(SidePanel panel) => _docks[panel];

    public void Place(SidePanel panel, PanelDock dock) => _docks[panel] = dock;

    public double SizeOf(PanelDock dock) => _sizes[dock];

    /// <summary>The range a side may have. <paramref name="window"/> is the width (left, right) or the height (bottom) of the area the panels share with the text; 0 when it is not known yet.</summary>
    public static (double Min, double Max) Limits(PanelDock dock, double window)
    {
        bool bottom = dock == PanelDock.Bottom;
        double min = bottom ? MinHeight : MinWidth, max = bottom ? MaxHeight : MaxWidth;
        if (window > 0) max = Math.Max(min, Math.Min(max, window * MaxShare));
        return (min, max);
    }

    /// <summary>Sets the size of a side inside its limits; returns the size it has now.</summary>
    public double SetSize(PanelDock dock, double size, double window = 0)
    {
        var (min, max) = Limits(dock, window);
        return _sizes[dock] = double.IsFinite(size) ? Math.Clamp(size, min, max) : min;
    }

    public bool IsDefault => Docks.All(d => _sizes[d] == (d == PanelDock.Bottom ? DefaultHeight : DefaultWidth)) && Panels.All(p => _docks[p] == PanelDock.Left);

    public PanelLayout Copy()
    {
        var copy = new PanelLayout();
        foreach (var p in Panels) copy._docks[p] = _docks[p];
        foreach (var d in Docks) copy._sizes[d] = _sizes[d];
        return copy;
    }

    // ---- what is kept ----

    public SavedPanels ToSaved() => new()
    {
        Documents = Name(_docks[SidePanel.Documents]), Files = Name(_docks[SidePanel.Files]), Plugin = Name(_docks[SidePanel.Plugin]),
        LeftWidth = _sizes[PanelDock.Left], RightWidth = _sizes[PanelDock.Right], BottomHeight = _sizes[PanelDock.Bottom],
    };

    /// <summary>The layout from a saved one. A value that is not known, or a size outside the limits, is replaced by the default: a damaged file never keeps a panel off the screen.</summary>
    public static PanelLayout FromSaved(SavedPanels? saved)
    {
        var layout = new PanelLayout();
        if (saved is null) return layout;
        layout._docks[SidePanel.Documents] = Parse(saved.Documents);
        layout._docks[SidePanel.Files] = Parse(saved.Files);
        layout._docks[SidePanel.Plugin] = Parse(saved.Plugin);
        layout.SetSize(PanelDock.Left, saved.LeftWidth);
        layout.SetSize(PanelDock.Right, saved.RightWidth);
        layout.SetSize(PanelDock.Bottom, saved.BottomHeight);
        return layout;
    }

    public static string Name(PanelDock dock) => dock switch { PanelDock.Right => "right", PanelDock.Bottom => "bottom", _ => "left" };

    private static PanelDock Parse(string? name) => name switch { "right" => PanelDock.Right, "bottom" => PanelDock.Bottom, _ => PanelDock.Left };

    // ---- where a dragged panel is dropped ----

    /// <summary>
    /// The side a panel dragged to <paramref name="point"/> of an area of size <paramref name="area"/> would be docked to: the bottom quarter, then the left and the right quarter. Null outside the area and
    /// in the middle, where a drop changes nothing.
    /// </summary>
    public static PanelDock? DropSide(Point point, Size area)
    {
        if (area.Width <= 0 || area.Height <= 0 || point.X < 0 || point.Y < 0 || point.X > area.Width || point.Y > area.Height) return null;
        if (point.Y >= area.Height * 0.75) return PanelDock.Bottom;
        if (point.X <= area.Width * 0.25) return PanelDock.Left;
        if (point.X >= area.Width * 0.75) return PanelDock.Right;
        return null;
    }
}

/// <summary>
/// The handle between a side panel and the text: dragging it changes the size of the panel inside its limits, the arrow keys change it by a step (Shift: five steps), Home and End take it to the
/// smallest and the largest size. A screen reader gets a slider whose value is the size in pixels.
/// </summary>
internal sealed class PanelSplitter : Border
{
    public const double Step = 20;
    public const double Thickness = 4;

    private readonly Func<double> _read;
    private readonly Func<(double Min, double Max)> _limits;
    private readonly Action<double, bool> _write;
    private Point? _origin;
    private double _startSize;

    /// <param name="read">The size now.</param>
    /// <param name="limits">The smallest and the largest size now.</param>
    /// <param name="write">Sets the size; the second argument is true when it is final (the end of a drag, a key), false while the pointer moves.</param>
    public PanelSplitter(PanelDock dock, Func<double> read, Func<(double Min, double Max)> limits, Action<double, bool> write)
    {
        Dock = dock;
        _read = read;
        _limits = limits;
        _write = write;
        Classes.Add("panelsplitter");
        Focusable = true;
        IsTabStop = true;
        CursorType = dock == PanelDock.Bottom ? StandardCursorType.SizeNorthSouth : StandardCursorType.SizeWestEast;
        Cursor = new Cursor(CursorType);
        if (dock == PanelDock.Bottom) Height = Thickness;
        else Width = Thickness;
    }

    protected override Type StyleKeyOverride => typeof(Border);

    public PanelDock Dock { get; }

    /// <summary>The pointer over the handle: the arrows that say the handle can be dragged along the axis of the panel.</summary>
    public StandardCursorType CursorType { get; }

    public double Value => _read();

    public (double Min, double Max) Range => _limits();

    /// <summary>The distance the pointer has to move for a drag along the axis of the panel, in the direction that makes the panel larger.</summary>
    private double Along(Point from, Point to) => Dock switch { PanelDock.Left => to.X - from.X, PanelDock.Right => from.X - to.X, _ => from.Y - to.Y };

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Parent is not Visual parent) return;
        _origin = e.GetPosition(parent);
        _startSize = _read();
        e.Pointer.Capture(this);
        Focus();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_origin is not { } origin || Parent is not Visual parent) return;
        _write(_startSize + Along(origin, e.GetPosition(parent)), false);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_origin is null) return;
        _origin = null;
        e.Pointer.Capture(null);
        _write(_read(), true);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_origin is null) return;
        _origin = null;
        _write(_read(), true);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? Step * 5 : Step;
        double? size = (Dock, e.Key) switch
        {
            (PanelDock.Left, Key.Right) or (PanelDock.Right, Key.Left) or (PanelDock.Bottom, Key.Up) => _read() + step,
            (PanelDock.Left, Key.Left) or (PanelDock.Right, Key.Right) or (PanelDock.Bottom, Key.Down) => _read() - step,
            (_, Key.Home) => _limits().Min,
            (_, Key.End) => _limits().Max,
            _ => null,
        };
        if (size is not { } to) return;
        _write(to, true);
        e.Handled = true;
    }

    /// <summary>Sets the size, as a key does.</summary>
    public void SetSize(double size) => _write(size, true);

    /// <summary>Tells a screen reader that the size changed.</summary>
    public void Changed() => (ControlAutomationPeer.FromElement(this) as PanelSplitterAutomationPeer)?.Moved();

    protected override AutomationPeer OnCreateAutomationPeer() => new PanelSplitterAutomationPeer(this);
}

/// <summary>A slider for a screen reader: the value is the size of the panel in pixels.</summary>
internal sealed class PanelSplitterAutomationPeer(PanelSplitter owner) : ControlAutomationPeer(owner), IRangeValueProvider
{
    private double _last = -1;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

    protected override string GetClassNameCore() => nameof(PanelSplitter);

    public bool IsReadOnly => false;

    public double Minimum => owner.Range.Min;

    public double Maximum => owner.Range.Max;

    public double Value => owner.Value;

    public double SmallChange => PanelSplitter.Step;

    public double LargeChange => PanelSplitter.Step * 5;

    public void SetValue(double value) => owner.SetSize(value);

    public void Moved()
    {
        double now = owner.Value;
        if (now == _last) return;
        RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, _last, now);
        _last = now;
    }
}
