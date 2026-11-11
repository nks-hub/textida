using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace NksHub.Textida.Accessibility.Windows;

[Flags]
internal enum ProviderOptions { ServerSideProvider = 2 }

internal enum NavigateDirection { Parent, NextSibling, PreviousSibling, FirstChild, LastChild }

internal enum SupportedTextSelection { None, Single, Multiple }

[StructLayout(LayoutKind.Sequential)]
internal struct UiaRect { public double Left, Top, Width, Height; }

[StructLayout(LayoutKind.Sequential)]
internal struct UiaPoint { public double X, Y; }

/// <summary>Blittable VARIANT: the type tag and one pointer-sized payload (enough for I4, BOOL, BSTR and UNKNOWN).</summary>
[SupportedOSPlatform("windows")]
[StructLayout(LayoutKind.Sequential)]
internal struct Variant
{
    public ushort Vt;
    public ushort Reserved1, Reserved2, Reserved3;
    public nint Value;
    public nint Value2;

    public const ushort VtEmpty = 0, VtI4 = 3, VtBstr = 8, VtUnknown = 13, VtBool = 11;

    public static Variant FromInt(int v) => new() { Vt = VtI4, Value = v };
    public static Variant FromBool(bool v) => new() { Vt = VtBool, Value = v ? -1 : 0 };
    public static unsafe Variant FromString(string v)
    {
        fixed (char* p = v) return new() { Vt = VtBstr, Value = UiaNative.SysAllocStringLen(p, (uint)v.Length) };
    }
}

// Interfaces mirror uiautomationcore.h, in vtable order. [out, retval] results are plain return values.

[GeneratedComInterface, Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c")]
internal partial interface IRawElementProviderSimple
{
    ProviderOptions get_ProviderOptions();
    nint GetPatternProvider(int patternId);
    Variant GetPropertyValue(int propertyId);
    nint get_HostRawElementProvider();
}

[GeneratedComInterface, Guid("f7063da8-8359-439c-9297-bbc5299a7d87")]
internal partial interface IRawElementProviderFragment
{
    IRawElementProviderFragment? Navigate(NavigateDirection direction);
    nint GetRuntimeId();
    UiaRect get_BoundingRectangle();
    nint GetEmbeddedFragmentRoots();
    void SetFocus();
    IRawElementProviderFragmentRoot? get_FragmentRoot();
}

[GeneratedComInterface, Guid("620ce2a5-ab8f-40a9-86cb-de3c75599b58")]
internal partial interface IRawElementProviderFragmentRoot
{
    IRawElementProviderFragment? ElementProviderFromPoint(double x, double y);
    IRawElementProviderFragment? GetFocus();
}

[GeneratedComInterface, Guid("3589c92c-63f3-4367-99bb-ada653b77cf2")]
internal partial interface ITextProvider
{
    nint GetSelection();
    nint GetVisibleRanges();
    ITextRangeProvider RangeFromChild(IRawElementProviderSimple childElement);
    ITextRangeProvider RangeFromPoint(UiaPoint point);
    ITextRangeProvider get_DocumentRange();
    SupportedTextSelection get_SupportedTextSelection();
}

[GeneratedComInterface, Guid("0dc5e6ed-3e16-4bf1-8f9a-a979878bc195")]
internal partial interface ITextProvider2 : ITextProvider
{
    ITextRangeProvider RangeFromAnnotation(IRawElementProviderSimple annotationElement);
    void GetCaretRange([MarshalAs(UnmanagedType.Bool)] out bool isActive, out ITextRangeProvider? range);
}

[GeneratedComInterface, Guid("5347ad7b-c355-46f8-aff5-909033582f63")]
internal partial interface ITextRangeProvider
{
    ITextRangeProvider Clone();
    [return: MarshalAs(UnmanagedType.Bool)]
    bool Compare(ITextRangeProvider range);
    int CompareEndpoints(int endpoint, ITextRangeProvider targetRange, int targetEndpoint);
    void ExpandToEnclosingUnit(int unit);
    ITextRangeProvider? FindAttribute(int attributeId, Variant value, [MarshalAs(UnmanagedType.Bool)] bool backward);
    ITextRangeProvider? FindText([MarshalAs(UnmanagedType.BStr)] string text, [MarshalAs(UnmanagedType.Bool)] bool backward, [MarshalAs(UnmanagedType.Bool)] bool ignoreCase);
    Variant GetAttributeValue(int attributeId);
    nint GetBoundingRectangles();
    IRawElementProviderSimple GetEnclosingElement();
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetText(int maxLength);
    int Move(int unit, int count);
    int MoveEndpointByUnit(int endpoint, int unit, int count);
    void MoveEndpointByRange(int endpoint, ITextRangeProvider targetRange, int targetEndpoint);
    void Select();
    void AddToSelection();
    void RemoveFromSelection();
    void ScrollIntoView([MarshalAs(UnmanagedType.Bool)] bool alignToTop);
    nint GetChildren();
}

[SupportedOSPlatform("windows")]
internal static unsafe partial class UiaNative
{
    public const int UiaRootObjectId = -25;
    public const int WmGetObject = 0x003D;
    public const int TextSelectionChangedEventId = 20014, TextChangedEventId = 20015, FocusChangedEventId = 20005;
    public const int HasKeyboardFocusPropertyId = 30008;

    [LibraryImport("UIAutomationCore.dll")]
    public static partial nint UiaReturnRawElementProvider(nint hwnd, nint wParam, nint lParam, nint provider);
    [LibraryImport("UIAutomationCore.dll")]
    public static partial int UiaHostProviderFromHwnd(nint hwnd, out nint provider);
    [LibraryImport("UIAutomationCore.dll")]
    public static partial int UiaRaiseAutomationEvent(nint provider, int eventId);
    [LibraryImport("UIAutomationCore.dll")]
    public static partial int UiaRaiseAutomationPropertyChangedEvent(nint provider, int propertyId, Variant oldValue, Variant newValue);
    [LibraryImport("UIAutomationCore.dll")]
    public static partial int UiaDisconnectProvider(nint provider);
    [LibraryImport("UIAutomationCore.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UiaClientsAreListening();
    [LibraryImport("UIAutomationCore.dll")]
    public static partial int UiaGetReservedNotSupportedValue(out nint value);

    [LibraryImport("oleaut32.dll")]
    public static partial nint SafeArrayCreateVector(ushort vt, int lowerBound, uint elements);
    [LibraryImport("oleaut32.dll")]
    public static partial int SafeArrayPutElement(nint array, int* index, void* value);
    [LibraryImport("oleaut32.dll")]
    public static partial int SafeArrayAccessData(nint array, out void* data);
    [LibraryImport("oleaut32.dll")]
    public static partial int SafeArrayUnaccessData(nint array);
    [LibraryImport("oleaut32.dll")]
    public static partial nint SysAllocStringLen(char* text, uint length);

    public const ushort VtI4 = 3, VtR8 = 5, VtUnknown = 13;

    public static nint DoubleArray(ReadOnlySpan<double> values)
    {
        nint sa = SafeArrayCreateVector(VtR8, 0, (uint)values.Length);
        if (values.Length == 0) return sa;
        Marshal.ThrowExceptionForHR(SafeArrayAccessData(sa, out void* data));
        values.CopyTo(new Span<double>(data, values.Length));
        SafeArrayUnaccessData(sa);
        return sa;
    }

    public static nint IntArray(ReadOnlySpan<int> values)
    {
        nint sa = SafeArrayCreateVector(VtI4, 0, (uint)values.Length);
        Marshal.ThrowExceptionForHR(SafeArrayAccessData(sa, out void* data));
        values.CopyTo(new Span<int>(data, values.Length));
        SafeArrayUnaccessData(sa);
        return sa;
    }

    /// <summary>A SAFEARRAY of IUnknown holding one reference to each object; the array owns its own references.</summary>
    public static nint RangeArray(ReadOnlySpan<ITextRangeProvider> ranges)
    {
        nint sa = SafeArrayCreateVector(VtUnknown, 0, (uint)ranges.Length);
        for (int i = 0; i < ranges.Length; i++)
        {
            void* unk = ComInterfaceMarshaller<ITextRangeProvider>.ConvertToUnmanaged(ranges[i]);
            try { Marshal.ThrowExceptionForHR(SafeArrayPutElement(sa, &i, unk)); }
            finally { Marshal.Release((nint)unk); }
        }
        return sa;
    }
}
