namespace NksHub.Textida.Accessibility;

/// <summary>A half-open range of UTF-16 code units.</summary>
public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;
    public bool IsEmpty => Length == 0;
}

/// <summary>A rectangle in screen pixels, top-left origin.</summary>
public readonly record struct ScreenRect(double X, double Y, double Width, double Height);

/// <summary>One text change in UTF-16 units: <see cref="Removed"/> units at <see cref="Start"/> were replaced by <see cref="Inserted"/>.</summary>
public readonly record struct TextChange(int Start, int Removed, int Inserted, long Revision);

/// <summary>
/// What an editor surface offers to the platform accessibility layer (UIA TextPattern, NSAccessibility text protocols).
/// All offsets are UTF-16 code units of the text without BOM. Members are called on the UI thread unless noted;
/// the bridges reach the UI thread through <see cref="InvokeOnUiThread"/>.
/// </summary>
public interface ITextAutomationSurface
{
    /// <summary>Revision of the document the text belongs to.</summary>
    long Revision { get; }

    int Length { get; }
    string GetText(int start, int length);

    int LineCount { get; }
    /// <summary>Offset of the first unit of a line; <c>LineStart(LineCount)</c> is <see cref="Length"/>. A line includes its terminator.</summary>
    int LineStart(int line);
    int LineOfOffset(int offset);

    /// <summary>Selection from its start to its end; empty when only a caret exists.</summary>
    TextSpan Selection { get; }
    /// <summary>Caret offset, equal to one end of <see cref="Selection"/>.</summary>
    int CaretOffset { get; }
    void SetSelection(int start, int length);

    bool IsReadOnly { get; }
    bool HasFocus { get; }
    void Focus();

    /// <summary>Bounds of the whole editor view in screen pixels.</summary>
    ScreenRect ScreenBounds { get; }
    /// <summary>The part of the text currently drawn.</summary>
    TextSpan VisibleRange { get; }
    /// <summary>Screen rectangles covering the range, one per line; empty when the range is not drawn.</summary>
    IReadOnlyList<ScreenRect> GetRangeBounds(int start, int length);
    /// <summary>Offset of the character under a screen point, or -1 when the point is outside the text view.</summary>
    int OffsetFromPoint(double x, double y);
    void ScrollIntoView(int start, int length, bool alignToTop);

    event Action<TextChange>? TextChanged;
    /// <summary>Raised after the selection or the caret moved.</summary>
    event Action? SelectionChanged;
    event Action? FocusChanged;

    /// <summary>Runs the action on the UI thread and waits for it; runs inline when already there.</summary>
    void InvokeOnUiThread(Action action);
}
