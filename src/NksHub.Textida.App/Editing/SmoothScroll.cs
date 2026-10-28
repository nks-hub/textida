using Avalonia.Threading;
using NksHub.Textida.App.Services;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// The glide of smooth scrolling. Wheel notches move a target; each frame the position goes a fixed share of the way to it, so it starts quickly and settles gently.
/// It knows nothing of the control: the caller gives it the position the control has and sets the position it answers with. A position the control has that the last
/// answer does not explain (the user moved the scroll bar, or a key scrolled) ends the glide.
/// </summary>
internal sealed class SmoothScroll
{
    /// <summary>The glide ends when the position is this close to the target; also how far from its last answer the position may be before someone else is taken to have moved it.</summary>
    private const double Tolerance = 1.5, Arrival = 0.5;

    private double _target, _lastAnswer;

    public bool Active { get; private set; }

    public double Target => _target;

    /// <summary>
    /// Adds a wheel move of <paramref name="delta"/> to the target, which starts from <paramref name="current"/> when no glide is under way and stays within
    /// <paramref name="min"/> and <paramref name="max"/>. Returns whether there is something to glide to.
    /// </summary>
    public bool Push(double current, double delta, double min, double max)
    {
        if (Active && Math.Abs(current - _lastAnswer) > Tolerance) Active = false;
        _target = Math.Clamp((Active ? _target : current) + delta, min, max);
        _lastAnswer = current;
        Active = Math.Abs(_target - current) >= Arrival;
        return Active;
    }

    /// <summary>
    /// The position for the next frame, <paramref name="share"/> of the way from <paramref name="current"/> to the target; the target itself once it is close enough.
    /// When <paramref name="current"/> is not where the last answer put it the glide ends and the position stays as it is.
    /// </summary>
    public double Next(double current, double share)
    {
        if (!Active) return current;
        if (Math.Abs(current - _lastAnswer) > Tolerance)
        {
            Active = false;
            return current;
        }
        double distance = _target - current;
        if (Math.Abs(distance) < Arrival)
        {
            Active = false;
            return _target;
        }
        return _lastAnswer = current + distance * share;
    }

    public void Stop() => Active = false;
}

/// <summary>A <see cref="SmoothScroll"/> driven by a timer: <paramref name="read"/> gives the position of the control, <paramref name="write"/> sets it.</summary>
internal sealed class Glide(Func<double> read, Action<double> write)
{
    private readonly SmoothScroll _scroll = new();
    private DispatcherTimer? _timer;

    public bool Active => _scroll.Active;
    public double Target => _scroll.Target;

    /// <summary>Moves the target by <paramref name="delta"/> and keeps the frames coming; false when the move is out of the range or nothing is left to glide.</summary>
    public bool Push(double delta, double min, double max)
    {
        if (!_scroll.Push(read(), delta, min, max)) return false;
        _timer ??= CreateTimer();
        _timer.Start();
        return true;
    }

    public void Stop()
    {
        _scroll.Stop();
        _timer?.Stop();
    }

    private DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PluginLimitsPlaceholders.SmoothScrollFrameMilliseconds) };
        timer.Tick += (_, _) => Frame();
        return timer;
    }

    /// <summary>One frame: sets the next position, and stops the timer when the glide is over.</summary>
    internal void Frame()
    {
        double now = read();
        double next = _scroll.Next(now, PluginLimitsPlaceholders.SmoothScrollShare);
        if (_scroll.Active || next != now) write(next);
        if (!_scroll.Active) _timer?.Stop();
    }
}
