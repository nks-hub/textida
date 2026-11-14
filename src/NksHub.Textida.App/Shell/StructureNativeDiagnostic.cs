using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.Core.Data.Structure;

namespace NksHub.Textida.App.Shell;

// Opt-in diagnostic: draw timestamps describe display-list recording. Only the external desktop reader observes presentation.
internal sealed partial class StructureNativeDiagnostic : IDisposable
{
    private readonly Window window;
    private readonly StructureView view;
    private readonly Func<StructureSource?>? source;
    private readonly string directory;
    private readonly CancellationTokenSource stopped = new();
    private readonly object outputGate = new();
    private readonly HashSet<(string Run, int Sample)> samples = [];
    private readonly HashSet<uint> markers = [];
    private AdornerLayer? layer;
    private FrameMarker? markerControl;
    private Pending? pending;
    private Visible? visible;
    private Identity? identity;
    private bool frameScheduled, readyWritten;
    private long lastStatus;
    private TreeKey? lastTreeKey;

    private sealed record Arm(string Run, int Sample, string Action, uint Marker, string Target, string? Direction);
    private sealed record Identity(string Source, string ExeSha, int Pid, int Session);
    private readonly record struct MarkerPoints(PixelPoint First, PixelPoint Second);
    private sealed record State(string? Selected, long? SelectedId, bool Expanded, int Children, bool WorkingComplete,
        bool ParsingComplete, double ScrollX, double ScrollY, int Items, bool TargetExists);
    private sealed class Pending(Arm arm, TreeViewItem target, State initial, MarkerPoints points, string? inspectionNonce)
    {
        public Arm Arm { get; } = arm;
        public TreeViewItem Target { get; } = target;
        public State Initial { get; } = initial;
        public MarkerPoints Points { get; } = points;
        public string? InspectionNonce { get; } = inspectionNonce;
        public JsonObject? InputEvent;
        public long ArmedAt { get; } = Stopwatch.GetTimestamp();
        public long InputAt;
        public bool Acknowledged;
        public long FirstSelectionCompletedQpc, LastSelectionCompletedQpc;
        public int SelectionCompletedCount;
        public WorkingObservation? WorkingObservation;
        public long FirstAnimationFrameCallbackQpc;
        public int AnimationFrameCallbackCount;
        public long FirstPaintQpc;
        public int PaintAttempts;
        public PaintObservation? FirstPaintState;
        public string? TypingTraceBeforeArm;
        public long TypingTraceWindowBeginQpc;
        public IBrush First { get; } = ColorBrush(arm.Marker);
        public IBrush Second { get; } = ColorBrush(arm.Marker ^ 0xFFFFFF);
    }
    private sealed class WorkingObservation(int selectionCount, string kind)
    {
        public int SelectionCount { get; } = selectionCount;
        public string Kind { get; } = kind;
        public long ObservedCompleteQpc;
    }
    private sealed record PaintObservation(bool WorkingComplete, bool ParsingComplete, bool ViewArrangeValid,
        bool ViewMeasureValid, bool TreeArrangeValid, bool TreeMeasureValid, bool TargetArrangeValid, bool TargetMeasureValid);
    private sealed record Visible(Pending Sample, State State);
    private readonly record struct TreeKey(Key Key, long ObservedQpc, long? FocusedNodeId);

    internal static void Attach(Window window, StructureView view, Func<StructureSource?>? source = null)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("TEXTIDA_STRUCTURE_NATIVE") is not { Length: > 0 } root) return;
        _ = new StructureNativeDiagnostic(window, view, source, Path.GetFullPath(root));
    }

    private StructureNativeDiagnostic(Window window, StructureView view, Func<StructureSource?>? source, string directory)
    {
        this.window = window;
        this.view = view;
        this.source = source;
        this.directory = directory;
        view.Tree.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerPressedEvent, OnPointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel, handledEventsToo: true);
        view.Tree.SelectionChanged += OnSelection;
        view.AddHandler(ScrollViewer.ScrollChangedEvent, OnScroll, RoutingStrategies.Bubble, handledEventsToo: true);
        view.LayoutUpdated += OnLayout;
        window.Closed += OnClosed;
        _ = Task.Run(PollAsync);
    }

    private bool EnsureMarker()
    {
        if (!window.IsVisible || !view.Tree.IsEffectivelyVisible || view.Tree.Bounds.Width < 40 || view.Tree.Bounds.Height < 24
            || TopLevel.GetTopLevel(view.Tree) != window) return false;
        var current = AdornerLayer.GetAdornerLayer(view.Tree);
        if (current is null) return false;
        if (layer != current || markerControl is null)
        {
            if (markerControl is not null) layer?.Children.Remove(markerControl);
            layer = current;
            markerControl = new FrameMarker(this) { IsHitTestVisible = false };
            AdornerLayer.SetAdornedElement(markerControl, view.Tree);
            AdornerLayer.SetIsClipEnabled(markerControl, true);
            layer.Children.Add(markerControl);
        }
        return markerControl.Bounds.Width >= 40 && markerControl.Bounds.Height >= 24;
    }

    private MarkerPoints Points() => new(markerControl!.PointToScreen(new Point(markerControl.Bounds.Width - 12, markerControl.Bounds.Height - 6)),
        markerControl.PointToScreen(new Point(markerControl.Bounds.Width - 4, markerControl.Bounds.Height - 6)));

    private nint OwnedWindow()
    {
        nint hwnd = window.TryGetPlatformHandle()?.Handle ?? 0;
        if (hwnd == 0 || GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid != Environment.ProcessId)
            throw new InvalidOperationException("The diagnostic has no owned native window.");
        return hwnd;
    }

    private Pending ArmSample(Arm arm)
    {
        if (pending is not null) throw new InvalidOperationException("A diagnostic sample is already armed.");
        if (!EnsureMarker() || !view.Working.IsCompleted || !view.Parsing.IsCompleted || !view.Tree.IsArrangeValid || !view.Tree.IsMeasureValid)
            throw new InvalidOperationException("The structure tree is not ready.");
        if (GetForegroundWindow() != OwnedWindow()) throw new InvalidOperationException("The measured window is not foreground.");
        if (samples.Count >= 4096 || !samples.Add((arm.Run, arm.Sample)) || !markers.Add(arm.Marker))
            throw new InvalidOperationException("Samples and marker colors must be unique; at most 4096 samples are accepted.");
        var candidates = Items().Where(item => AutomationProperties.GetName(item) == arm.Target).Take(2).ToArray();
        if (candidates.Length != 1) throw new InvalidOperationException("The target label must identify exactly one tree item.");
        var target = candidates[0];
        if (arm.Action != "scroll" && !HeaderVisible(target)) throw new InvalidOperationException("The target header is outside the tree viewport.");
        var initial = CaptureState(target);
        if (arm.Action == "select" && initial.Selected == arm.Target) throw new InvalidOperationException("The selection target is already selected.");
        if (arm.Action is "expand" or "collapse")
        {
            if (target.Tag is not StructureNode { ChildCount: > 0 }) throw new InvalidOperationException("The target has no children.");
            if (initial.Expanded == (arm.Action == "expand")) throw new InvalidOperationException("The target already has the requested expansion state.");
        }
        if (arm.Action == "scroll" && Scroller() is null) throw new InvalidOperationException("The tree has no scroll viewport.");
        visible = null;
        pending = new(arm, target, initial, Points(), lastInspectionNonce);
        if (Core.TypingTrace.Enabled)
        {
            pending.TypingTraceBeforeArm = Core.TypingTrace.Summary();
            Core.TypingTrace.Reset();
            pending.TypingTraceWindowBeginQpc = Stopwatch.GetTimestamp();
        }
        markerControl!.InvalidateVisual();
        return pending;
    }

    private IEnumerable<TreeViewItem> Items()
    {
        var stack = new Stack<TreeViewItem>(view.Tree.Items.OfType<TreeViewItem>().Reverse());
        int count = 0;
        while (stack.TryPop(out var item))
        {
            if (++count > StructureView.MaximumItems) throw new InvalidOperationException("The diagnostic tree exceeded its control bound.");
            if (item.Tag is StructureNode) yield return item;
            foreach (var child in item.Items.OfType<TreeViewItem>().Reverse()) stack.Push(child);
        }
    }

    private ScrollViewer? Scroller() => view.Tree.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private State CaptureState(TreeViewItem target)
    {
        var selected = view.Tree.SelectedItem as TreeViewItem;
        var scroll = Scroller()?.Offset ?? default;
        return new(selected is null ? null : AutomationProperties.GetName(selected), selected?.Tag is StructureNode n ? n.Id : null,
            target.IsExpanded, target.Items.OfType<TreeViewItem>().Count(item => item.Tag is StructureNode),
            view.Working.IsCompleted, view.Parsing.IsCompleted, scroll.X, scroll.Y, view.CachedItems, Items().Any(item => ReferenceEquals(item, target)));
    }

    private bool HeaderVisible(TreeViewItem target)
    {
        var header = target.Header as Visual ?? target;
        if (!header.IsEffectivelyVisible || header.Bounds.Width <= 0 || header.Bounds.Height <= 0) return false;
        var origin = header.TranslatePoint(default, view.Tree);
        return origin.HasValue && new Rect(origin.Value, header.Bounds.Size).Intersects(new Rect(view.Tree.Bounds.Size));
    }

    private bool Complete(Pending sample, State state)
    {
        if (!state.TargetExists || !state.WorkingComplete || !state.ParsingComplete || !view.Tree.IsArrangeValid || !view.Tree.IsMeasureValid
            || sample.Arm.Action != "scroll" && (!sample.Target.IsArrangeValid || !sample.Target.IsMeasureValid)
            || !view.IsArrangeValid || !view.IsMeasureValid) return false;
        return sample.Arm.Action switch
        {
            "select" => ReferenceEquals(view.Tree.SelectedItem, sample.Target),
            "expand" => state.Expanded && state.Children > 0 && !sample.Target.Items.OfType<TreeViewItem>().Any(item => !item.IsEnabled && item.Tag is not StructureNode),
            "collapse" => !state.Expanded,
            "scroll" => sample.Arm.Direction == "down" ? state.ScrollY > sample.Initial.ScrollY + 0.001 : state.ScrollY < sample.Initial.ScrollY - 0.001,
            _ => false,
        };
    }

    private void ObserveInput(JsonObject actual)
    {
        if (pending is not { Acknowledged: true, InputAt: 0 } sample) return;
        if (actual["inTree"]?.GetValue<bool>() != true) return;
        string? kind = actual["kind"]?.GetValue<string>();
        if (sample.InspectionNonce is { } nonce)
        {
            if (actual["inspectionNonce"]?.GetValue<string>() != nonce) return;
            string expected = sample.Arm.Action switch { "select" => "pointer", "expand" or "collapse" => "key", "scroll" => "wheel", _ => "" };
            if (kind != expected || kind == "pointer" && actual["inHeader"]?.GetValue<bool>() != true) return;
        }
        if (sample.Arm.Action != "scroll")
        {
            long? nodeId = kind == "key" ? actual["keyTargetNodeId"]?.GetValue<long>() : actual["nodeId"]?.GetValue<long>();
            if (sample.Target.Tag is not StructureNode node || nodeId != node.Id) return;
        }
        else if (kind is not ("key" or "wheel")) return;
        sample.InputAt = actual["observedQpc"]!.GetValue<long>();
        sample.InputEvent = (JsonObject)actual.DeepClone();
        ScheduleFrame();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        long observed = Stopwatch.GetTimestamp();
        var focusedItem = FocusedTreeItem(window.FocusManager?.GetFocusedElement());
        lastTreeKey = new(e.Key, observed, focusedItem?.Tag is StructureNode node ? node.Id : null);
        var actual = InputJson("key", e.Source, observed);
        actual["key"] = e.Key.ToString();
        actual["focus"] = FocusJson();
        var keyTarget = focusedItem ?? (ReferenceEquals(window.FocusManager?.GetFocusedElement(), view.Tree) ? view.Tree.SelectedItem as TreeViewItem : null);
        actual["keyTargetNodeId"] = keyTarget?.Tag is StructureNode keyNode ? keyNode.Id : null;
        Record("input.jsonl", actual);
        if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Space or Key.Enter) ObserveInput(actual);
    }
    private void OnPointer(object? sender, PointerPressedEventArgs e)
    {
        long observed = Stopwatch.GetTimestamp();
        var actual = InputJson("pointer", e.Source, observed, window.PointToScreen(e.GetPosition(window)));
        actual["button"] = e.GetCurrentPoint(window).Properties.PointerUpdateKind.ToString();
        lastPointer = actual;
        Record("input.jsonl", actual);
        ObserveInput(actual);
    }
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        long observed = Stopwatch.GetTimestamp();
        var actual = InputJson("wheel", e.Source, observed, window.PointToScreen(e.GetPosition(window)));
        actual["deltaX"] = e.Delta.X; actual["deltaY"] = e.Delta.Y;
        lastWheel = actual;
        Record("input.jsonl", actual);
        ObserveInput(actual);
    }
    private void OnSelection(object? sender, SelectionChangedEventArgs e)
    {
        if (pending is { Acknowledged: true, InputAt: > 0 } sample)
        {
            long observed = Stopwatch.GetTimestamp();
            if (sample.SelectionCompletedCount++ == 0) sample.FirstSelectionCompletedQpc = observed;
            sample.LastSelectionCompletedQpc = observed;
            Task working = view.Working;
            bool completed = working.IsCompleted;
            var observation = new WorkingObservation(sample.SelectionCompletedCount, completed ? "already-complete" : "continuation");
            sample.WorkingObservation = observation;
            if (completed) Interlocked.CompareExchange(ref observation.ObservedCompleteQpc, Stopwatch.GetTimestamp(), 0);
            else _ = working.ContinueWith(static (_, state) =>
            {
                var captured = (WorkingObservation)state!;
                Interlocked.CompareExchange(ref captured.ObservedCompleteQpc, Stopwatch.GetTimestamp(), 0);
            }, observation, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        markerControl?.InvalidateVisual();
    }
    private void OnScroll(object? sender, ScrollChangedEventArgs e) => markerControl?.InvalidateVisual();
    private void OnLayout(object? sender, EventArgs e) { if (pending is not null || visible is not null) markerControl?.InvalidateVisual(); }

    private void ScheduleFrame()
    {
        if (frameScheduled || stopped.IsCancellationRequested) return;
        frameScheduled = true;
        window.RequestAnimationFrame(_ =>
        {
            long observed = Stopwatch.GetTimestamp();
            frameScheduled = false;
            if (pending is { InputAt: > 0 } sample)
            {
                if (sample.AnimationFrameCallbackCount++ == 0) sample.FirstAnimationFrameCallbackQpc = observed;
                markerControl?.InvalidateVisual(); ScheduleFrame();
            }
        });
    }

    private void Paint(DrawingContext context, FrameMarker marker)
    {
        if (pending is { Acknowledged: true, InputAt: > 0 } attempted && attempted.PaintAttempts++ == 0)
            attempted.FirstPaintQpc = Stopwatch.GetTimestamp();
        if (stopped.IsCancellationRequested || !EnsureMarker()) return;
        Pending? drawn = null;
        State? drawnState = null;
        if (pending is { Acknowledged: true, InputAt: > 0 } sample)
        {
            var state = CaptureState(sample.Target);
            if (sample.PaintAttempts == 1)
                sample.FirstPaintState = new(state.WorkingComplete, state.ParsingComplete, view.IsArrangeValid, view.IsMeasureValid,
                    view.Tree.IsArrangeValid, view.Tree.IsMeasureValid, sample.Target.IsArrangeValid, sample.Target.IsMeasureValid);
            if (Complete(sample, state) && sample.Points == Points())
            {
                visible = new(sample, state);
                pending = null;
                drawn = sample;
                drawnState = state;
            }
        }
        if (visible is not { } frame || frame.Sample.Points != Points() || CaptureState(frame.Sample.Target) != frame.State) return;
        context.FillRectangle(frame.Sample.First, new Rect(marker.Bounds.Width - 16, marker.Bounds.Height - 10, 8, 8));
        context.FillRectangle(frame.Sample.Second, new Rect(marker.Bounds.Width - 8, marker.Bounds.Height - 10, 8, 8));
        if (drawn is not null)
        {
            long drawAt = Stopwatch.GetTimestamp();
            var record = SampleJson(drawn, "drawn");
            record["inputObservedQpc"] = drawn.InputAt;
            record["drawQpc"] = drawAt;
            record["state"] = StateJson(drawnState!);
            Record("draw.jsonl", record);
        }
    }

    private sealed class FrameMarker(StructureNativeDiagnostic owner) : Control
    {
        protected override Type StyleKeyOverride => typeof(Control);
        public override void Render(DrawingContext context) => owner.Paint(context, this);
    }

    private static IBrush ColorBrush(uint rgb) => new ImmutableSolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    private void OnClosed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (stopped.IsCancellationRequested) return;
        stopped.Cancel();
        pending = null; visible = null;
        view.Tree.RemoveHandler(InputElement.KeyDownEvent, OnKey);
        window.RemoveHandler(InputElement.PointerPressedEvent, OnPointer);
        window.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
        view.Tree.SelectionChanged -= OnSelection;
        view.RemoveHandler(ScrollViewer.ScrollChangedEvent, OnScroll);
        view.LayoutUpdated -= OnLayout;
        window.Closed -= OnClosed;
        if (markerControl is not null) layer?.Children.Remove(markerControl);
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();
}
