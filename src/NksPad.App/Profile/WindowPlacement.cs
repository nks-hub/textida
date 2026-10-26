namespace NksPad.App.Profile;

/// <summary>Integer rectangle in physical pixels; independent of Avalonia so the placement rule is a pure function.</summary>
public readonly record struct PixelBox(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public PixelBox Intersect(PixelBox o)
    {
        int x = Math.Max(X, o.X), y = Math.Max(Y, o.Y);
        int r = Math.Min(Right, o.Right), b = Math.Min(Bottom, o.Bottom);
        return r > x && b > y ? new PixelBox(x, y, r - x, b - y) : default;
    }

    public long Area => (long)Width * Height;
}

/// <summary>A monitor: usable area (without taskbar or menu bar), scale factor and whether it is the primary one.</summary>
public readonly record struct ScreenBox(PixelBox WorkArea, double Scale, bool Primary);

/// <summary>Where to put the window: pixel position, size in device-independent units, and whether it was moved off the saved spot.</summary>
public readonly record struct Placement(int X, int Y, double Width, double Height, bool Maximized, bool Relocated);

public static class WindowPlacement
{
    /// <summary>Height of the strip at the top of the window that must stay reachable so the user can drag it.</summary>
    public const int GrabHeight = 32;
    public const int MinGrabWidth = 96;
    public const double DefaultWidth = 1100, DefaultHeight = 720;

    /// <summary>
    /// Restores the saved window onto the monitor it still fits. If its title strip is not visible on any monitor
    /// (the monitor was unplugged or the layout changed) the window is centred on the primary monitor.
    /// Without saved state, or without any monitor, the default size is centred on the primary monitor.
    /// </summary>
    public static Placement Restore(SavedWindow? saved, IReadOnlyList<ScreenBox> screens)
    {
        if (screens.Count == 0) return new Placement(0, 0, saved?.Width is > 0 ? saved.Width : DefaultWidth, saved?.Height is > 0 ? saved.Height : DefaultHeight, saved?.Maximized ?? false, false);

        var primary = screens.FirstOrDefault(s => s.Primary);
        if (primary.WorkArea.Width == 0) primary = screens[0];

        double w = saved is { Width: > 0 } ? saved.Width : DefaultWidth;
        double h = saved is { Height: > 0 } ? saved.Height : DefaultHeight;
        if (saved is null) return Center(primary, w, h, relocated: false, maximized: false);

        var rect = new PixelBox(saved.X, saved.Y, Math.Max(1, (int)Math.Round(w * ScaleAt(screens, saved.X, saved.Y, primary))), Math.Max(1, (int)Math.Round(h * ScaleAt(screens, saved.X, saved.Y, primary))));
        var strip = new PixelBox(rect.X, rect.Y, rect.Width, GrabHeight);

        ScreenBox? best = null;
        long bestArea = 0;
        foreach (var s in screens)
        {
            var visible = strip.Intersect(s.WorkArea);
            if (visible.Width >= Math.Min(MinGrabWidth, rect.Width) && visible.Height >= GrabHeight / 2 && visible.Area > bestArea) { best = s; bestArea = visible.Area; }
        }

        if (best is not { } target) return Center(primary, w, h, relocated: true, saved.Maximized);

        // Keep the saved spot, but shrink and shift the window so it lies inside the monitor it is on.
        var wa = target.WorkArea;
        int pw = Math.Min(rect.Width, wa.Width), ph = Math.Min(rect.Height, wa.Height);
        int x = Math.Clamp(rect.X, wa.X, wa.Right - pw), y = Math.Clamp(rect.Y, wa.Y, wa.Bottom - ph);
        bool moved = pw != rect.Width || ph != rect.Height || x != rect.X || y != rect.Y;
        return new Placement(x, y, pw / target.Scale, ph / target.Scale, saved.Maximized, moved);
    }

    private static double ScaleAt(IReadOnlyList<ScreenBox> screens, int x, int y, ScreenBox fallback)
    {
        foreach (var s in screens) if (x >= s.WorkArea.X && x < s.WorkArea.Right && y >= s.WorkArea.Y && y < s.WorkArea.Bottom) return s.Scale;
        return fallback.Scale;
    }

    private static Placement Center(ScreenBox screen, double wDip, double hDip, bool relocated, bool maximized)
    {
        var wa = screen.WorkArea;
        int pw = Math.Min((int)Math.Round(wDip * screen.Scale), wa.Width), ph = Math.Min((int)Math.Round(hDip * screen.Scale), wa.Height);
        return new Placement(wa.X + (wa.Width - pw) / 2, wa.Y + (wa.Height - ph) / 2, pw / screen.Scale, ph / screen.Scale, maximized, relocated);
    }
}
