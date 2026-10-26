using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace NksHub.Textida.Accessibility.Windows;

/// <summary>Name and id the editor element reports to UIA clients.</summary>
public sealed record UiaEditorInfo(string Name, string AutomationId, string ClassName = "TextidaEditor");

/// <summary>
/// The UIA element of an editor: a document-type fragment root with TextPattern and TextPattern2, backed by an
/// <see cref="ITextAutomationSurface"/>. UIA calls arrive on worker threads; every access goes through the surface's UI thread.
/// </summary>
[GeneratedComClass]
[SupportedOSPlatform("windows")]
internal sealed partial class UiaEditorProvider : IRawElementProviderSimple, IRawElementProviderFragment, IRawElementProviderFragmentRoot, ITextProvider2
{
    private const int PatternText = 10014, PatternText2 = 10024;
    private const int ControlTypeDocument = 50030;

    internal ITextAutomationSurface Surface { get; }
    private readonly nint _hwnd;
    private readonly UiaEditorInfo _info;

    public UiaEditorProvider(ITextAutomationSurface surface, nint hwnd, UiaEditorInfo info)
    {
        Surface = surface;
        _hwnd = hwnd;
        _info = info;
    }

    internal T Ui<T>(Func<T> func)
    {
        T result = default!;
        Exception? error = null;
        Surface.InvokeOnUiThread(() =>
        {
            try { result = func(); }
            catch (Exception e) { error = e; }
        });
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        return result;
    }

    internal void Ui(Action action) => Ui<object?>(() => { action(); return null; });

    // ---- IRawElementProviderSimple ----

    public ProviderOptions get_ProviderOptions() => ProviderOptions.ServerSideProvider;

    public unsafe nint GetPatternProvider(int patternId) => patternId switch
    {
        PatternText => (nint)ComInterfaceMarshaller<ITextProvider>.ConvertToUnmanaged(this),
        PatternText2 => (nint)ComInterfaceMarshaller<ITextProvider2>.ConvertToUnmanaged(this),
        _ => 0,
    };

    public Variant GetPropertyValue(int propertyId) => propertyId switch
    {
        30003 => Variant.FromInt(ControlTypeDocument),
        30004 => Variant.FromString("editor"),
        30005 => Variant.FromString(_info.Name),
        30008 => Variant.FromBool(Ui(() => Surface.HasFocus)),
        30009 => Variant.FromBool(true),
        30010 => Variant.FromBool(true),
        30011 => Variant.FromString(_info.AutomationId),
        30012 => Variant.FromString(_info.ClassName),
        30016 or 30017 => Variant.FromBool(true),
        30019 => Variant.FromBool(false),
        30022 => Variant.FromBool(false),
        30024 => Variant.FromString("Textida"),
        30040 or 30119 => Variant.FromBool(true),
        _ => default,
    };

    public nint get_HostRawElementProvider()
    {
        Marshal.ThrowExceptionForHR(UiaNative.UiaHostProviderFromHwnd(_hwnd, out nint host));
        return host;
    }

    // ---- IRawElementProviderFragment / FragmentRoot ----

    public IRawElementProviderFragment? Navigate(NavigateDirection direction) => null;

    // The element is the root of the window it is hosted in, which UIA identifies by the window handle.
    public nint GetRuntimeId() => 0;

    public UiaRect get_BoundingRectangle()
    {
        var b = Ui(() => Surface.ScreenBounds);
        return new() { Left = b.X, Top = b.Y, Width = b.Width, Height = b.Height };
    }

    public nint GetEmbeddedFragmentRoots() => 0;
    public void SetFocus() => Ui(Surface.Focus);
    public IRawElementProviderFragmentRoot? get_FragmentRoot() => this;
    public IRawElementProviderFragment? ElementProviderFromPoint(double x, double y) => this;
    public IRawElementProviderFragment? GetFocus() => Ui(() => Surface.HasFocus) ? this : null;

    // ---- ITextProvider ----

    internal UiaTextRange Wrap(TextRange range) => new(this, range);

    public nint GetSelection() => UiaNative.RangeArray([Wrap(Ui(() => TextRange.SelectionOf(Surface)))]);

    public nint GetVisibleRanges() => UiaNative.RangeArray([Wrap(Ui(() =>
    {
        var v = Surface.VisibleRange;
        return new TextRange(Surface, v.Start, v.End);
    }))]);

    public ITextRangeProvider RangeFromChild(IRawElementProviderSimple childElement) => Wrap(Ui(() => TextRange.Document(Surface)));

    public ITextRangeProvider RangeFromPoint(UiaPoint point) => Wrap(Ui(() =>
    {
        int offset = Math.Max(0, Surface.OffsetFromPoint(point.X, point.Y));
        return new TextRange(Surface, offset, offset);
    }));

    public ITextRangeProvider get_DocumentRange() => Wrap(Ui(() => TextRange.Document(Surface)));

    public SupportedTextSelection get_SupportedTextSelection() => SupportedTextSelection.Single;

    // ---- ITextProvider2 ----

    public ITextRangeProvider RangeFromAnnotation(IRawElementProviderSimple annotationElement) =>
        throw new COMException("No annotations.", unchecked((int)0x80040201));

    public void GetCaretRange(out bool isActive, out ITextRangeProvider? range)
    {
        var (active, caret) = Ui(() => (Surface.HasFocus, TextRange.Caret(Surface)));
        isActive = active;
        range = Wrap(caret);
    }
}

[GeneratedComClass]
[SupportedOSPlatform("windows")]
internal sealed partial class UiaTextRange(UiaEditorProvider owner, TextRange range) : ITextRangeProvider
{
    internal TextRange Range => range;

    private static TextRange Unwrap(ITextRangeProvider other) =>
        other is UiaTextRange r ? r.Range : throw new ArgumentException("Foreign text range.", nameof(other));

    public ITextRangeProvider Clone() => owner.Ui(() => owner.Wrap(range.Clone()));

    public bool Compare(ITextRangeProvider other) => owner.Ui(() => range.Equals(Unwrap(other)));

    public int CompareEndpoints(int endpoint, ITextRangeProvider targetRange, int targetEndpoint) =>
        owner.Ui(() => range.CompareEndpoints((RangeEndpoint)endpoint, Unwrap(targetRange), (RangeEndpoint)targetEndpoint));

    public void ExpandToEnclosingUnit(int unit) => owner.Ui(() => range.ExpandToEnclosingUnit((TextUnit)unit));

    public ITextRangeProvider? FindAttribute(int attributeId, Variant value, bool backward) => null;

    public ITextRangeProvider? FindText(string text, bool backward, bool ignoreCase) =>
        owner.Ui(() => range.FindText(text, backward, ignoreCase) is { } found ? owner.Wrap(found) : null);

    public unsafe Variant GetAttributeValue(int attributeId)
    {
        const int IsReadOnlyAttribute = 40015;
        if (attributeId == IsReadOnlyAttribute) return Variant.FromBool(owner.Ui(() => owner.Surface.IsReadOnly));
        Marshal.ThrowExceptionForHR(UiaNative.UiaGetReservedNotSupportedValue(out nint notSupported));
        return new() { Vt = Variant.VtUnknown, Value = notSupported };
    }

    public nint GetBoundingRectangles()
    {
        var rects = owner.Ui(() => range.GetBounds());
        var values = new double[rects.Count * 4];
        for (int i = 0; i < rects.Count; i++)
        {
            values[i * 4] = rects[i].X;
            values[i * 4 + 1] = rects[i].Y;
            values[i * 4 + 2] = rects[i].Width;
            values[i * 4 + 3] = rects[i].Height;
        }
        return UiaNative.DoubleArray(values);
    }

    public IRawElementProviderSimple GetEnclosingElement() => owner;

    public string GetText(int maxLength) => owner.Ui(() => range.GetText(maxLength));

    public int Move(int unit, int count) => owner.Ui(() => range.Move((TextUnit)unit, count));

    public int MoveEndpointByUnit(int endpoint, int unit, int count) =>
        owner.Ui(() => range.MoveEndpointByUnit((RangeEndpoint)endpoint, (TextUnit)unit, count));

    public void MoveEndpointByRange(int endpoint, ITextRangeProvider targetRange, int targetEndpoint) =>
        owner.Ui(() => range.MoveEndpointByRange((RangeEndpoint)endpoint, Unwrap(targetRange), (RangeEndpoint)targetEndpoint));

    public void Select() => owner.Ui(range.Select);

    // Single selection only (SupportedTextSelection.Single): adding or removing a second selection is not supported.
    public void AddToSelection() => throw new COMException("Multiple selection is not supported.", unchecked((int)0x80131509));
    public void RemoveFromSelection() => throw new COMException("Multiple selection is not supported.", unchecked((int)0x80131509));

    public void ScrollIntoView(bool alignToTop) => owner.Ui(() => range.ScrollIntoView(alignToTop));

    public nint GetChildren() => 0;
}
