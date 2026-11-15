using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;

namespace NksHub.Textida.App.Tests;

internal sealed class TouchTestInput(Control surface, Window window, PointerType type) : IDisposable
{
    private readonly Pointer pointer = new(Pointer.GetNextFreeId(), type, true);
    private ulong timestamp;

    public void Down(Point local, RawInputModifiers modifiers = RawInputModifiers.None, int clicks = 1) => surface.RaiseEvent(
        new PointerPressedEventArgs(surface, pointer, window, Position(local), ++timestamp,
            new PointerPointProperties(modifiers | RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, clicks));

    public void Move(Point local, RawInputModifiers modifiers = RawInputModifiers.None) => surface.RaiseEvent(
        new PointerEventArgs(InputElement.PointerMovedEvent, surface, pointer, window, Position(local), ++timestamp,
            new PointerPointProperties(modifiers | RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));

    public void Up(Point local) => surface.RaiseEvent(new PointerReleasedEventArgs(surface, pointer, window, Position(local), ++timestamp,
        new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));

    public void LoseCapture() => pointer.Capture(null);
    public void Dispose() => pointer.Dispose();
    private Point Position(Point local) => surface.TranslatePoint(local, window) ?? throw new InvalidOperationException("Touch surface is detached.");
}
