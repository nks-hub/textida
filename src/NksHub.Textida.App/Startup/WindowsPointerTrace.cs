using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit.Rendering;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;

namespace NksHub.Textida.App.Startup;

internal sealed partial class WindowsPointerTrace
{
    private const int MaximumRecords = 256;
    private const RoutingStrategies Routes = RoutingStrategies.Tunnel | RoutingStrategies.Bubble;
    private readonly Window root;
    private readonly Channel<string> records = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = true,
        AllowSynchronousContinuations = false,
    });
    private readonly IDisposable pressed, released;
    private Control? touchSurface;
    private uint? nativePointerId;
    private int? routedPointerId;
    private int recordCount;
    private bool nativeEnded;
    internal static WindowsPointerTrace? Current { get; private set; }

    internal static void Attach(Window window)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("TEXTIDA_POINTER_TRACE") is not { Length: > 0 } path) return;
        StreamWriter output;
        try
        {
            output = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException)
        {
            return; // An unavailable diagnostic destination must not prevent startup.
        }
        Current = new WindowsPointerTrace(window, output);
    }

    private unsafe WindowsPointerTrace(Window window, StreamWriter output)
    {
        root = window;
        Write(FormattableString.Invariant($"layer=start qpc={Stopwatch.GetTimestamp()} qpc_frequency={Stopwatch.Frequency} pid={Environment.ProcessId} version={BuildStamp.Version} commit={BuildStamp.Commit} pointer_info_bytes={sizeof(NativePointerInfo)} max_records={MaximumRecords}"));
        _ = DrainAsync(output);
        Win32Properties.AddWndProcHookCallback(root, WndProc);
        root.AddHandler(InputElement.PointerPressedEvent, RootPressed, Routes, handledEventsToo: true);
        root.AddHandler(InputElement.PointerReleasedEvent, RootReleased, Routes, handledEventsToo: true);
        pressed = InputElement.PointerPressedEvent.AddClassHandler<TextView>((view, e) => Routed(view, e, "text_view"), Routes, handledEventsToo: true);
        released = InputElement.PointerReleasedEvent.AddClassHandler<TextView>((view, e) => Routed(view, e, "text_view"), Routes, handledEventsToo: true);
        root.Closed += Closed;
    }

    private void RootPressed(object? sender, PointerPressedEventArgs e) => Routed(root, e, "root");
    private void RootReleased(object? sender, PointerReleasedEventArgs e) => Routed(root, e, "root");

    internal void TouchPress(Control surface, PointerPressedEventArgs e, bool disposed, int generation, string mode)
    {
        if (nativePointerId is null || e.Pointer.Type != PointerType.Touch || TopLevel.GetTopLevel(surface) != root) return;
        touchSurface ??= surface;
        var properties = e.GetCurrentPoint(surface).Properties;
        Touch(surface, $"stage=press_entry generation={generation} mode={mode} disposed={disposed} pointer_id={e.Pointer.Id} type={e.Pointer.Type} handled={e.Handled} left_pressed={properties.IsLeftButtonPressed} barrel={properties.IsBarrelButtonPressed} eraser={properties.IsEraser} inverted={properties.IsInverted} captured={TypeName(e.Pointer.Captured)}");
    }

    internal void Touch(Control surface, FormattableString detail)
    {
        if (recordCount >= MaximumRecords || !ReferenceEquals(touchSurface, surface)) return;
        Write(FormattableString.Invariant($"layer=touch qpc={Stopwatch.GetTimestamp()} native_pointer_id={nativePointerId} {detail.ToString(CultureInfo.InvariantCulture)}"));
    }

    internal void TouchSelection(Control surface, string stage, int generation, TouchRange requested, Func<TouchRange> selection)
    {
        if (recordCount >= MaximumRecords || !ReferenceEquals(touchSurface, surface)) return;
        var selected = selection();
        Touch(surface, $"stage={stage} generation={generation} requested_anchor={requested.Anchor} requested_caret={requested.Caret} selection_anchor={selected.Anchor} selection_caret={selected.Caret}");
    }

    private void Routed(Control control, PointerEventArgs e, string layer)
    {
        if (recordCount >= MaximumRecords || nativePointerId is null || e.Pointer.Type != PointerType.Touch || TopLevel.GetTopLevel(control) != root) return;
        routedPointerId ??= e.Pointer.Id;
        if (e.Pointer.Id != routedPointerId) return;
        long qpc = Stopwatch.GetTimestamp();
        var point = e.GetCurrentPoint(control);
        var rootPosition = e.GetPosition(root);
        Write(FormattableString.Invariant($"layer={layer} qpc={qpc} event={e.RoutedEvent?.Name} route={e.Route} source={TypeName(e.Source)} pointer_type={e.Pointer.Type} pointer_id={e.Pointer.Id} native_pointer_id={nativePointerId} timestamp={e.Timestamp} position={Position(point.Position)} root_position={Position(rootPosition)} handled={e.Handled} captured={TypeName(e.Pointer.Captured)} left_pressed={point.Properties.IsLeftButtonPressed} update_kind={point.Properties.PointerUpdateKind}"));
    }

    private nint WndProc(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
    {
        const uint update = 0x0245, down = 0x0246, up = 0x0247, captureChanged = 0x024C;
        bool focusMessage = message is 0x0006 or 0x0007 or 0x0008;
        if (recordCount >= MaximumRecords || (!focusMessage && message is not (update or down or up or captureChanged))) return 0;
        uint id = (uint)(wParam.ToInt64() & 0xFFFF);
        if (!focusMessage && ((nativePointerId is null && message != down) || (nativePointerId is { } selected && selected != id) || (nativeEnded && message != captureChanged))) return 0;
        int systemError = Marshal.GetLastSystemError(), pinvokeError = Marshal.GetLastPInvokeError();
        try
        {
            long qpc = Stopwatch.GetTimestamp();
            if (focusMessage)
            {
                Write(FormattableString.Invariant($"layer=window qpc={qpc} message=0x{message:X4} target_hwnd=0x{hwnd:X} wparam=0x{wParam:X} lparam=0x{lParam:X}"));
                return 0;
            }
            bool infoSuccess = GetPointerInfo(id, out var info) != 0;
            int infoError = infoSuccess ? 0 : Marshal.GetLastPInvokeError();
            if (nativePointerId is null && infoSuccess && info.Type != 2) return 0;
            nativePointerId ??= id;
            bool rectSuccess = false;
            int rectError = 0;
            NativeRect deviceRect = default, displayRect = default;
            if (infoSuccess)
            {
                rectSuccess = GetPointerDeviceRects(info.Device, out deviceRect, out displayRect) != 0;
                rectError = rectSuccess ? 0 : Marshal.GetLastPInvokeError();
            }
            long afterQpc = Stopwatch.GetTimestamp();
            // Avalonia 2d79ba55 GetHimetricLocation uses these rectangles even when the API returns false.
            var screen = new Point(info.RawHimetric.X * displayRect.Width / (double)deviceRect.Width + displayRect.Left,
                info.RawHimetric.Y * displayRect.Height / (double)deviceRect.Height + displayRect.Top);
            var origin = root.PointToScreen(default);
            var client = new Point(screen.X - origin.X, screen.Y - origin.Y) / root.RenderScaling;
            Write(FormattableString.Invariant($"layer=native qpc={qpc} after_api_qpc={afterQpc} message=0x{message:X4} hwnd=0x{hwnd:X} wparam=0x{wParam:X} lparam=0x{lParam:X} pointer_id={id} info_success={infoSuccess} info_error={infoError} info_pointer_id={info.Id} pointer_type={info.Type} flags=0x{info.Flags:X8} button_change_type={info.ButtonChange} source_device=0x{info.Device:X} target=0x{info.Target:X} pixel={info.Pixel.X},{info.Pixel.Y} raw_pixel={info.RawPixel.X},{info.RawPixel.Y} himetric={info.Himetric.X},{info.Himetric.Y} raw_himetric={info.RawHimetric.X},{info.RawHimetric.Y} native_qpc={info.PerformanceCount} native_time={info.Time} rects_called={infoSuccess} rects_success={rectSuccess} rects_error={rectError} device_rect={Rectangle(deviceRect)} display_rect={Rectangle(displayRect)} calculated_screen={Position(screen)} calculated_client={Position(client)} client_origin={origin.X},{origin.Y} render_scaling={root.RenderScaling:R} hook_handled={handled}"));
            if (message == up) nativeEnded = true;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            Write(FormattableString.Invariant($"layer=native qpc={Stopwatch.GetTimestamp()} message=0x{message:X4} pointer_id={id} api_exception={e.GetType().Name}"));
        }
        finally
        {
            Marshal.SetLastSystemError(systemError);
            Marshal.SetLastPInvokeError(pinvokeError);
        }
        // Avalonia ignores this return value while handled remains false; the hook never assigns handled.
        return 0;
    }

    private void Write(string record)
    {
        if (recordCount >= MaximumRecords) return;
        recordCount++;
        records.Writer.TryWrite(record);
    }

    private async Task DrainAsync(StreamWriter output)
    {
        try
        {
            await using (output)
                await foreach (string record in records.Reader.ReadAllAsync().ConfigureAwait(false))
                    await output.WriteLineAsync(record).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            // A trace write failure must not escape into the input pipeline.
        }
    }

    private void Closed(object? sender, EventArgs e)
    {
        Win32Properties.RemoveWndProcHookCallback(root, WndProc);
        root.RemoveHandler(InputElement.PointerPressedEvent, RootPressed);
        root.RemoveHandler(InputElement.PointerReleasedEvent, RootReleased);
        root.Closed -= Closed;
        pressed.Dispose();
        released.Dispose();
        if (ReferenceEquals(Current, this)) Current = null;
        records.Writer.TryComplete();
    }

    private static string TypeName(object? value) => value?.GetType().FullName ?? "null";
    private static string Position(Point point) => string.Create(CultureInfo.InvariantCulture, $"{point.X:R},{point.Y:R}");
    private static string Rectangle(NativeRect rect) => FormattableString.Invariant($"{rect.Left},{rect.Top},{rect.Right},{rect.Bottom}");
}
