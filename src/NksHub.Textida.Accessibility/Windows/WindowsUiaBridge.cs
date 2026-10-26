using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace NksHub.Textida.Accessibility.Windows;

/// <summary>
/// Publishes an <see cref="ITextAutomationSurface"/> to UI Automation as the root element of one native window, with
/// TextPattern and TextPattern2 and the text, selection and focus events. Built on source-generated COM
/// (<see cref="GeneratedComInterfaceAttribute"/>), so it runs under NativeAOT without built-in COM interop.
/// The window procedure of <c>hwnd</c> forwards <c>WM_GETOBJECT</c> to <see cref="TryHandleGetObject"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class WindowsUiaBridge : IDisposable
{
    private readonly UiaEditorProvider _provider;
    private readonly ITextAutomationSurface _surface;
    private readonly nint _hwnd;
    private nint _simple;
    private bool _wasFocused;
    private bool _disposed;

    public WindowsUiaBridge(ITextAutomationSurface surface, nint hwnd, UiaEditorInfo info)
    {
        _surface = surface;
        _hwnd = hwnd;
        _provider = new UiaEditorProvider(surface, hwnd, info);
        _simple = (nint)ComInterfaceMarshaller<IRawElementProviderSimple>.ConvertToUnmanaged(_provider);
        surface.TextChanged += OnTextChanged;
        surface.SelectionChanged += OnSelectionChanged;
        surface.FocusChanged += OnFocusChanged;
    }

    /// <summary>Answers <c>WM_GETOBJECT</c> for the UIA root object; false leaves the message to the default procedure.</summary>
    public bool TryHandleGetObject(nint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (_disposed || (int)lParam != UiaNative.UiaRootObjectId) return false;
        result = UiaNative.UiaReturnRawElementProvider(_hwnd, wParam, lParam, _simple);
        return true;
    }

    private void OnTextChanged(TextChange change) => Raise(UiaNative.TextChangedEventId);
    private void OnSelectionChanged() => Raise(UiaNative.TextSelectionChangedEventId);

    private void OnFocusChanged()
    {
        bool focused = _surface.HasFocus;
        if (focused == _wasFocused || _disposed) return;
        _wasFocused = focused;
        if (!UiaNative.UiaClientsAreListening()) return;
        UiaNative.UiaRaiseAutomationPropertyChangedEvent(_simple, UiaNative.HasKeyboardFocusPropertyId, Variant.FromBool(!focused), Variant.FromBool(focused));
        if (focused) UiaNative.UiaRaiseAutomationEvent(_simple, UiaNative.FocusChangedEventId);
    }

    private void Raise(int eventId)
    {
        if (_disposed || !UiaNative.UiaClientsAreListening()) return;
        UiaNative.UiaRaiseAutomationEvent(_simple, eventId);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _surface.TextChanged -= OnTextChanged;
        _surface.SelectionChanged -= OnSelectionChanged;
        _surface.FocusChanged -= OnFocusChanged;
        UiaNative.UiaDisconnectProvider(_simple);
        Marshal.Release(_simple);
        _simple = 0;
    }
}
