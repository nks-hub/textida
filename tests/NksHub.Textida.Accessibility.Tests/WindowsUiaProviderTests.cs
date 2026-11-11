using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using NksHub.Textida.Accessibility.Windows;

namespace NksHub.Textida.Accessibility.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "UI Automation exists on Windows only.";
    }
}

/// <summary>The provider objects called directly, below the COM layer; the cross-process run is in tools/NksHub.Textida.Accessibility.UiaClient.</summary>
[SupportedOSPlatform("windows")]
public partial class WindowsUiaProviderTests
{
    [LibraryImport("oleaut32.dll")] private static partial int SafeArrayGetUBound(nint array, uint dim, out int ubound);
    [LibraryImport("oleaut32.dll")] private static partial int SafeArrayDestroy(nint array);
    [LibraryImport("oleaut32.dll")] private static unsafe partial int SafeArrayGetElement(nint array, int* index, out nint element);

    [WindowsFact]
    public unsafe void Scalar_and_array_ranges_expose_readable_native_text_interfaces()
    {
        using var surface = new TestSurface("one\ntwo");
        var provider = Provider(surface);
        nint scalar = (nint)ComInterfaceMarshaller<ITextRangeProvider>.ConvertToUnmanaged(provider.get_DocumentRange());
        try { Assert.Equal("one\ntwo", ReadNativeText(scalar)); }
        finally { Marshal.Release(scalar); }

        foreach (bool visible in new[] { false, true })
        {
            nint array = visible ? provider.GetVisibleRanges() : provider.GetSelection();
            nint element = 0;
            try
            {
                int index = 0;
                Assert.Equal(0, SafeArrayGetElement(array, &index, out element));
                Assert.NotEqual(0, element);
                Assert.Equal(visible ? "one\ntwo" : "", ReadNativeText(element));
            }
            finally
            {
                if (element != 0) Marshal.Release(element);
                SafeArrayDestroy(array);
            }
        }
    }

    private static unsafe string ReadNativeText(nint unknown)
    {
        Guid iid = new("5347ad7b-c355-46f8-aff5-909033582f63");
        Assert.Equal(0, Marshal.QueryInterface(unknown, in iid, out nint range));
        nint text = 0;
        try
        {
            var getText = (delegate* unmanaged[Stdcall]<nint, int, nint*, int>)(*(nint**)unknown)[12];
            Assert.Equal(0, getText(unknown, -1, &text));
            string raw = Marshal.PtrToStringBSTR(text);
            Marshal.FreeBSTR(text);
            text = 0;
            getText = (delegate* unmanaged[Stdcall]<nint, int, nint*, int>)(*(nint**)range)[12];
            Assert.Equal(0, getText(range, -1, &text));
            Assert.Equal(raw, Marshal.PtrToStringBSTR(text));
            return raw;
        }
        finally
        {
            Marshal.FreeBSTR(text);
            Marshal.Release(range);
        }
    }

    private static UiaEditorProvider Provider(TestSurface s) => new(s, 0, new UiaEditorInfo("Test editor", "test-editor"));

    [WindowsFact]
    public void Element_is_a_document_with_text_patterns()
    {
        using var s = new TestSurface("abc");
        var p = Provider(s);
        var type = p.GetPropertyValue(30003);
        Assert.Equal((Variant.VtI4, 50030), (type.Vt, (int)type.Value));
        Assert.Equal(Variant.VtBstr, p.GetPropertyValue(30005).Vt);
        Assert.Equal(Variant.VtEmpty, p.GetPropertyValue(99999).Vt);
        Assert.NotEqual(0, p.GetPatternProvider(10014));
        Assert.NotEqual(0, p.GetPatternProvider(10024));
        Assert.Equal(0, p.GetPatternProvider(10000));
    }

    [WindowsFact]
    public void Document_range_and_selection_come_from_the_surface()
    {
        using var s = new TestSurface("Hello\r\nworld");
        var p = Provider(s);
        var doc = p.get_DocumentRange();
        Assert.Equal("Hello\r\nworld", doc.GetText(-1));
        Assert.Equal("Hel", doc.GetText(3));

        s.Select(2, 5);
        nint sa = p.GetSelection();
        try
        {
            Assert.Equal(0, SafeArrayGetUBound(sa, 1, out int ubound));
            Assert.Equal(0, ubound);
        }
        finally { SafeArrayDestroy(sa); }
        p.GetCaretRange(out bool active, out var caret);
        Assert.Equal(("", false), (caret!.GetText(-1), active));
        s.Focus();
        p.GetCaretRange(out active, out _);
        Assert.True(active);
    }

    [WindowsFact]
    public void Range_calls_reach_the_range_model()
    {
        using var s = new TestSurface("one two\nthree");
        var p = Provider(s);
        var r = p.get_DocumentRange();
        Assert.Equal(1, r.Move(3, 1));                       // Line
        Assert.Equal("three", r.GetText(-1));
        r.ExpandToEnclosingUnit(6);                           // Document
        Assert.Equal(13, r.GetText(-1).Length);
        var found = r.FindText("two", false, false)!;
        Assert.Equal("two", found.GetText(-1));
        Assert.Equal(-1, found.CompareEndpoints(0, r.Clone(), 1));
        Assert.True(found.Compare(found.Clone()));
        found.Select();
        Assert.Equal(new TextSpan(4, 3), s.Selection);
    }

    [WindowsFact]
    public void Bounding_rectangles_are_a_safearray_of_four_doubles_per_line()
    {
        using var s = new TestSurface("ab\ncd");
        var range = Provider(s).get_DocumentRange();
        nint sa = range.GetBoundingRectangles();
        try
        {
            Assert.Equal(0, SafeArrayGetUBound(sa, 1, out int ubound));
            Assert.Equal(7, ubound);
        }
        finally { SafeArrayDestroy(sa); }
    }
}
