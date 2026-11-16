using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Startup;

namespace NksHub.Textida.App.Editing;

internal readonly record struct TouchRange(long Anchor, long Caret)
{
    public long Start => Math.Min(Anchor, Caret);
    public long End => Math.Max(Anchor, Caret);
    public bool IsEmpty => Anchor == Caret;
}

internal sealed class TextTouchInput : IDisposable
{
    private readonly Control surface;
    private readonly Func<Point, CancellationToken, Task<long?>> hitTest;
    private readonly Func<long, CancellationToken, Task<TouchRange?>> wordAt;
    private readonly Func<TouchRange> selection;
    private readonly Action<TouchRange> select;
    private readonly Action<Vector> scroll;
    private readonly Func<long, Rect?> caretRect;
    private readonly bool holdingEnabled;
    private readonly DispatcherTimer hold = new();
    private readonly DispatcherTimer edge = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly HashSet<int> contacts = [];
    private readonly Handles handles;
    private IPointer? pointer;
    private CancellationTokenSource? gesture;
    private CancellationTokenSource? request;
    private Task<TouchRange?>? origin;
    private Point pressed, last;
    private Vector handleDelta;
    private TouchRange applied;
    private Mode mode;
    private bool showHandles, blocked, disposed, applying;
    private int generation, requestId;

    private enum Mode { None, Tap, Pan, Select }

    public TextTouchInput(Control surface,
        Func<Point, CancellationToken, Task<long?>> hitTest,
        Func<long, CancellationToken, Task<TouchRange?>> wordAt,
        Func<TouchRange> selection, Action<TouchRange> select, Action<Vector> scroll,
        Func<long, Rect?> caretRect)
    {
        this.surface = surface;
        this.hitTest = hitTest;
        this.wordAt = wordAt;
        this.selection = selection;
        this.select = select;
        this.scroll = scroll;
        this.caretRect = caretRect;
        // Avalonia's built-in hold still opens a context menu after PreventGestureRecognition.
        holdingEnabled = InputElement.GetIsHoldingEnabled(surface);
        InputElement.SetIsHoldingEnabled(surface, false);
        handles = new Handles(this) { IsHitTestVisible = false };
        surface.AddHandler(InputElement.PointerPressedEvent, Press, RoutingStrategies.Tunnel);
        surface.AddHandler(InputElement.PointerMovedEvent, Move, RoutingStrategies.Tunnel);
        surface.AddHandler(InputElement.PointerReleasedEvent, Release, RoutingStrategies.Tunnel);
        surface.AddHandler(InputElement.PointerCaptureLostEvent, CaptureLost);
        surface.AddHandler(InputElement.KeyDownEvent, KeyDown, RoutingStrategies.Tunnel);
        surface.DetachedFromVisualTree += Detached;
        hold.Tick += Hold;
        edge.Tick += ScrollAtEdge;
    }

    public void Refresh()
    {
        if (disposed) return;
        if (!applying && showHandles && selection() != applied) Cancel();
        handles.InvalidateVisual();
    }

    public void Cancel()
    {
        WindowsPointerTrace.Current?.Touch(surface, $"stage=cancel generation={generation} mode={mode} pointer_id={pointer?.Id} captured={pointer?.Captured?.GetType().FullName ?? "null"}");
        generation++;
        StopContact();
        request?.Cancel();
        gesture?.Cancel();
        request?.Dispose();
        gesture?.Dispose();
        request = gesture = null;
        origin = null;
        showHandles = false;
        handles.InvalidateVisual();
    }

    private void Press(object? sender, PointerPressedEventArgs e)
    {
        WindowsPointerTrace.Current?.TouchPress(surface, e, disposed, generation, mode.ToString());
        if (disposed || e.Handled) return;
        if (e.Pointer.Type == PointerType.Mouse)
        {
            Cancel();
            return;
        }
        var properties = e.GetCurrentPoint(surface).Properties;
        e.PreventGestureRecognition();
        e.Handled = true;
        if (!properties.IsLeftButtonPressed || properties.IsBarrelButtonPressed || properties.IsEraser || properties.IsInverted) return;
        contacts.Add(e.Pointer.Id);
        if (contacts.Count > 1 || blocked)
        {
            Cancel();
            blocked = true;
            e.Pointer.Capture(surface);
            return;
        }
        bool fromHandle = HandleAt(e.GetPosition(surface), out long fixedEnd, out Vector delta);
        Cancel();
        blocked = false;
        pointer = e.Pointer;
        pressed = last = e.GetPosition(surface);
        handleDelta = default;
        gesture = new CancellationTokenSource();
        pointer.Capture(surface);
        if (fromHandle)
        {
            mode = Mode.Select;
            handleDelta = delta;
            origin = Task.FromResult<TouchRange?>(new TouchRange(fixedEnd, fixedEnd));
            UpdateSelection(last - handleDelta);
        }
        else if (e.Pointer.Type == PointerType.Pen || e.ClickCount > 1)
        {
            BeginSelection(e.Pointer.Type == PointerType.Touch);
        }
        else
        {
            mode = Mode.Tap;
            hold.Interval = Application.Current?.PlatformSettings?.HoldWaitDuration ?? TimeSpan.FromMilliseconds(500);
            hold.Start();
        }
        WindowsPointerTrace.Current?.Touch(surface, $"stage=press_selected generation={generation} mode={mode} pointer_id={pointer?.Id} captured={pointer?.Captured?.GetType().FullName ?? "null"} from_handle={fromHandle} contacts={contacts.Count} blocked={blocked}");
    }

    private void Move(object? sender, PointerEventArgs e)
    {
        if (pointer?.Id != e.Pointer.Id)
        {
            if (contacts.Contains(e.Pointer.Id)) { e.PreventGestureRecognition(); e.Handled = true; }
            return;
        }
        e.PreventGestureRecognition();
        e.Handled = true;
        var point = e.GetPosition(surface);
        if (!e.GetCurrentPoint(surface).Properties.IsLeftButtonPressed)
        {
            WindowsPointerTrace.Current?.Touch(surface, $"stage=move_without_left generation={generation} mode={mode} pointer_id={e.Pointer.Id} pointer_matches={pointer?.Id == e.Pointer.Id}");
            Cancel();
            return;
        }
        if (mode == Mode.Tap)
        {
            var threshold = Application.Current?.PlatformSettings?.GetTapSize(PointerType.Touch) ?? new Size(8, 8);
            if (Math.Abs(point.X - pressed.X) > threshold.Width || Math.Abs(point.Y - pressed.Y) > threshold.Height)
            {
                mode = Mode.Pan;
                hold.Stop();
                scroll(pressed - point);
            }
        }
        else if (mode == Mode.Pan) scroll(last - point);
        else if (mode == Mode.Select)
        {
            UpdateSelection(point - handleDelta);
            if (Outside(point)) edge.Start(); else edge.Stop();
        }
        last = point;
    }

    private void Release(object? sender, PointerReleasedEventArgs e)
    {
        WindowsPointerTrace.Current?.Touch(surface, $"stage=release_entry generation={generation} mode={mode} pointer_id={e.Pointer.Id} pointer_matches={pointer?.Id == e.Pointer.Id} handled={e.Handled} captured={e.Pointer.Captured?.GetType().FullName ?? "null"}");
        if (e.Pointer.Type == PointerType.Mouse) return;
        contacts.Remove(e.Pointer.Id);
        if (contacts.Count == 0) blocked = false;
        if (pointer?.Id != e.Pointer.Id)
        {
            if (e.Pointer.Captured == surface) { e.Handled = true; e.Pointer.Capture(null); }
            return;
        }
        e.PreventGestureRecognition();
        e.Handled = true;
        var point = e.GetPosition(surface);
        if (mode == Mode.Tap)
        {
            origin = Origin(point, false, gesture!.Token);
            UpdateSelection(point, finishing: true);
        }
        else if (mode == Mode.Select) UpdateSelection(point - handleDelta, finishing: true);
        StopContact();
    }

    private void CaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        WindowsPointerTrace.Current?.Touch(surface, $"stage=capture_lost generation={generation} mode={mode} pointer_id={e.Pointer.Id} pointer_matches={pointer?.Id == e.Pointer.Id} captured={e.Pointer.Captured?.GetType().FullName ?? "null"}");
        contacts.Remove(e.Pointer.Id);
        if (contacts.Count == 0) blocked = false;
        if (pointer?.Id == e.Pointer.Id) Cancel();
    }

    private void StopContact()
    {
        WindowsPointerTrace.Current?.Touch(surface, $"stage=stop_contact generation={generation} mode={mode} pointer_id={pointer?.Id} captured={pointer?.Captured?.GetType().FullName ?? "null"}");
        hold.Stop();
        edge.Stop();
        mode = Mode.None;
        var captured = pointer;
        pointer = null;
        if (captured?.Captured == surface) captured.Capture(null);
    }

    private void Hold(object? sender, EventArgs e)
    {
        hold.Stop();
        if (mode == Mode.Tap && pointer is not null) BeginSelection(true);
    }

    private void BeginSelection(bool word)
    {
        hold.Stop();
        mode = Mode.Select;
        origin = Origin(pressed, word, gesture!.Token);
        UpdateSelection(last);
    }

    private async Task<TouchRange?> Origin(Point point, bool word, CancellationToken token)
    {
        var offset = await hitTest(Clamp(point), token);
        WindowsPointerTrace.Current?.Touch(surface, $"stage=origin_hit generation={generation} word={word} point={point.X:R},{point.Y:R} offset={offset} cancelled={token.IsCancellationRequested}");
        token.ThrowIfCancellationRequested();
        if (offset is not { } at) return null;
        return word ? await wordAt(at, token) ?? new TouchRange(at, at) : new TouchRange(at, at);
    }

    private async void UpdateSelection(Point point, bool finishing = false)
    {
        if (origin is null || gesture is null) return;
        request?.Cancel();
        request?.Dispose();
        request = CancellationTokenSource.CreateLinkedTokenSource(gesture.Token);
        var token = request.Token;
        int currentGeneration = generation, currentRequest = ++requestId;
        try
        {
            var start = await origin;
            WindowsPointerTrace.Current?.Touch(surface, $"stage=selection_origin generation={generation} current_generation={currentGeneration} request={requestId} current_request={currentRequest} anchor={start?.Anchor} caret={start?.Caret} finishing={finishing} cancelled={token.IsCancellationRequested}");
            token.ThrowIfCancellationRequested();
            if (start is not { } initial) return;
            var end = await hitTest(Clamp(point), token);
            WindowsPointerTrace.Current?.Touch(surface, $"stage=selection_hit generation={generation} current_generation={currentGeneration} request={requestId} current_request={currentRequest} offset={end} cancelled={token.IsCancellationRequested}");
            token.ThrowIfCancellationRequested();
            if (end is not { } at || currentGeneration != generation || currentRequest != requestId) return;
            var next = at < initial.Start ? new TouchRange(initial.End, at)
                : at > initial.End ? new TouchRange(initial.Start, at) : initial;
            applied = next;
            showHandles = !next.IsEmpty;
            applying = true;
            WindowsPointerTrace.Current?.TouchSelection(surface, "apply_before", generation, next, selection);
            try { select(next); }
            finally { applying = false; }
            WindowsPointerTrace.Current?.TouchSelection(surface, "apply_after", generation, next, selection);
            EnsureHandles();
            handles.InvalidateVisual();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || gesture?.IsCancellationRequested != false) { }
        finally
        {
            if (finishing && currentGeneration == generation && currentRequest == requestId)
            {
                request?.Dispose();
                gesture?.Dispose();
                request = gesture = null;
                origin = null;
            }
        }
    }

    private Point Clamp(Point point) => new(Math.Clamp(point.X, 0, Math.Max(0, surface.Bounds.Width - 1)),
        Math.Clamp(point.Y, 0, Math.Max(0, surface.Bounds.Height - 1)));

    private bool Outside(Point p) => p.X < 0 || p.Y < 0 || p.X >= surface.Bounds.Width || p.Y >= surface.Bounds.Height;

    private void ScrollAtEdge(object? sender, EventArgs e)
    {
        if (mode != Mode.Select) { edge.Stop(); return; }
        static double Speed(double p, double extent) => p < 0 ? Math.Max(-40, p / 2 - 4)
            : p >= extent ? Math.Min(40, (p - extent) / 2 + 4) : 0;
        scroll(new Vector(Speed(last.X, surface.Bounds.Width), Speed(last.Y, surface.Bounds.Height)));
        UpdateSelection(last - handleDelta);
        handles.InvalidateVisual();
    }

    private void EnsureHandles()
    {
        if (handles.Parent is not null || AdornerLayer.GetAdornerLayer(surface) is not { } layer) return;
        AdornerLayer.SetAdornedElement(handles, surface);
        layer.Children.Add(handles);
    }

    private Point? HandleCenter(long offset, bool start)
    {
        if (caretRect(offset) is not { } rect || rect.Bottom < 0 || rect.Top >= surface.Bounds.Height
            || rect.X < 0 || rect.X >= surface.Bounds.Width) return null;
        return new Point(Math.Clamp(rect.X + (start ? -6 : 6), 12, Math.Max(12, surface.Bounds.Width - 12)),
            Math.Clamp(rect.Bottom + 10, 12, Math.Max(12, surface.Bounds.Height - 12)));
    }

    private bool HandleAt(Point p, out long fixedEnd, out Vector delta)
    {
        fixedEnd = 0;
        delta = default;
        if (!showHandles || selection() != applied || applied.IsEmpty) return false;
        var start = HandleCenter(applied.Start, true);
        var end = HandleCenter(applied.End, false);
        double a = start is { } s ? ((Vector)(p - s)).Length : double.PositiveInfinity;
        double b = end is { } t ? ((Vector)(p - t)).Length : double.PositiveInfinity;
        if (Math.Min(a, b) > 22) return false;
        bool first = a < b;
        long moving = first ? applied.Start : applied.End;
        fixedEnd = first ? applied.End : applied.Start;
        if (caretRect(moving) is { } rect) delta = p - new Point(rect.X, rect.Center.Y);
        return true;
    }

    private void KeyDown(object? sender, KeyEventArgs e) => Cancel();
    private void Detached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Cancel();
        contacts.Clear();
        blocked = false;
        if (handles.Parent is AdornerLayer layer) layer.Children.Remove(handles);
    }

    public void Dispose()
    {
        if (disposed) return;
        Detached(null, null!);
        disposed = true;
        surface.RemoveHandler(InputElement.PointerPressedEvent, Press);
        surface.RemoveHandler(InputElement.PointerMovedEvent, Move);
        surface.RemoveHandler(InputElement.PointerReleasedEvent, Release);
        surface.RemoveHandler(InputElement.PointerCaptureLostEvent, CaptureLost);
        surface.RemoveHandler(InputElement.KeyDownEvent, KeyDown);
        surface.DetachedFromVisualTree -= Detached;
        hold.Tick -= Hold;
        edge.Tick -= ScrollAtEdge;
        InputElement.SetIsHoldingEnabled(surface, holdingEnabled);
    }

    private sealed class Handles(TextTouchInput owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            if (!owner.showHandles || owner.selection() != owner.applied || owner.applied.IsEmpty) return;
            IBrush brush = owner.surface.TryFindResource("SystemAccentColor", out var color) && color is Color accent
                ? new SolidColorBrush(accent) : Brushes.DodgerBlue;
            Draw(owner.applied.Start, true);
            Draw(owner.applied.End, false);
            void Draw(long offset, bool start)
            {
                if (owner.HandleCenter(offset, start) is not { } center || owner.caretRect(offset) is not { } rect) return;
                context.DrawLine(new Pen(brush, 2), new Point(rect.X, rect.Bottom), center);
                context.DrawEllipse(brush, new Pen(Brushes.White, 1), center, 10, 10);
            }
        }
    }
}
